using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Common;
using NemediClinic.Application.DTOs.Imports;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Entities;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// El importador desde archivos, para cualquier entidad registrada como IImportDefinition.
///
/// SIN ESTADO a propósito: el archivo no se guarda (ni en disco ni en memoria entre pasos). El
/// navegador lo conserva y lo reenvía en cada paso; cada uno lo lee de cero:
///   analizar → columnas, encabezado detectado y mapeo propuesto
///   vista previa → resumen de todas las filas + muestra de 50 (nada se guarda)
///   errores → Excel con las filas que fallaron y el motivo
///   importar → vuelve a leer y validar (la base pudo cambiar), guarda TODO en una transacción junto con
///              su ImportRun; si algo falla, no queda nada a medias.
/// Cancelar antes de importar no deja rastro en el servidor. El tenant sale del JWT (filtro global y
/// SaveChanges): una columna "tenant" del archivo no es un campo de nada y se ignora.
/// </summary>
public class ImportService
{
    public const int PreviewRows = 50;
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly AppDbContext _db;
    private readonly IEnumerable<IImportDefinition> _definitions;
    private readonly IClock _clock;

    public ImportService(AppDbContext db, IEnumerable<IImportDefinition> definitions, IClock clock)
    {
        _db = db;
        _definitions = definitions;
        _clock = clock;
    }

    public static string XlsxContentType => Xlsx;

    public sealed record Request(int FilaEncabezados, Dictionary<string, int> Mapeo, DuplicateMode Duplicados);

    /// <param name="filaEncabezados">1-based; null = detectarla. La persona la corrige si la detección falló.</param>
    public async Task<ImportAnalysisDto> AnalyzeAsync(string entidad, IFormFile? file, int? filaEncabezados, CancellationToken ct)
    {
        var definition = Definition(entidad);
        var sheet = Read(file);
        var fields = await definition.GetFieldsAsync(ct);
        if (filaEncabezados is { } fila && (fila < 1 || fila > sheet.Rows.Count))
            throw new ApiException(400, $"La fila de encabezados debe estar entre 1 y {sheet.Rows.Count}.");
        var header = filaEncabezados is { } f ? f - 1 : ImportEngine.DetectHeaderRow(sheet.Rows, fields);
        var columns = ImportEngine.Columns(sheet.Rows, header);
        var dataRows = ImportEngine.DataRows(sheet.Rows, header).Count();
        if (dataRows == 0) throw new ApiException(400, "El archivo no tiene filas de datos debajo de los encabezados.");
        if (dataRows > ImportEngine.MaxRows)
            throw new ApiException(400, $"El archivo tiene {dataRows:N0} filas y el máximo es {ImportEngine.MaxRows:N0} por archivo. Pártelo en varios y súbelos uno por uno.");

        return new ImportAnalysisDto
        {
            ArchivoNombre = file!.FileName,
            Formato = sheet.Format,
            FilaEncabezados = header + 1,
            FilasDeDatos = dataRows,
            MaxFilas = ImportEngine.MaxRows,
            Columnas = columns.Select(c => new ImportColumnDto { Index = c.Index, Letter = ImportEngine.ColumnLetter(c.Index), Header = c.Header, Samples = [.. c.Samples] }).ToList(),
            Campos = fields.Select(f => new ImportFieldDto { Key = f.Key, Label = f.Label, Required = f.Required, Hint = f.Hint, Group = f.Group }).ToList(),
            MapeoSugerido = ImportEngine.SuggestMapping(columns, fields),
            FormatosDeFecha = [.. ImportDates.Formats],
        };
    }

    public async Task<ImportPreviewDto> PreviewAsync(string entidad, IFormFile? file, Request request, bool soloProblemas, CancellationToken ct)
    {
        var definition = Definition(entidad);
        var (_, fields, rows) = await ValidateAsync(entidad, file, request, ct);
        var plan = definition.Plan(rows);
        var sample = soloProblemas ? rows.Where(r => r.Status != ImportRowStatus.Valida || r.Warnings.Count > 0) : rows;
        return new ImportPreviewDto
        {
            Total = rows.Count,
            Validas = rows.Count(r => r.Status == ImportRowStatus.Valida),
            ConError = rows.Count(r => r.Status == ImportRowStatus.Error),
            Duplicadas = rows.Count(r => r.Status == ImportRowStatus.Duplicada),
            ConAvisos = rows.Count(r => r.Warnings.Count > 0),
            Nuevos = plan.Nuevos,
            Existentes = plan.Existentes,
            Detalle = plan.Detalle,
            AvisosGenerales = [.. definition.Notices(request.Mapeo.Keys)],
            Filas = sample.Take(PreviewRows).Select(r => ToDto(r, fields)).ToList(),
        };
    }

    /// <summary>Las filas con error, con las columnas originales del archivo y el motivo: para corregirlas y volver a subir ESE archivo.</summary>
    public async Task<(byte[] Content, string FileName)> ErrorsWorkbookAsync(string entidad, IFormFile? file, Request request, CancellationToken ct)
    {
        var (sheet, _, rows) = await ValidateAsync(entidad, file, request, ct);
        var failed = rows.Where(r => r.Status == ImportRowStatus.Error).ToList();
        var headerRow = request.FilaEncabezados - 1;
        var width = Math.Max(sheet.Rows[headerRow].Length, failed.Count == 0 ? 0 : failed.Max(r => r.Raw.Length));
        var headers = Enumerable.Range(0, width)
            .Select(c => (c < sheet.Rows[headerRow].Length ? ImportText.Clean(sheet.Rows[headerRow][c]) : null) ?? $"Columna {ImportEngine.ColumnLetter(c)}")
            .Append("Motivo del error").Append("Fila original").ToArray();
        var content = ExcelTable.Build("Filas con error", "Filas que no se importaron",
            $"{failed.Count} de {rows.Count} filas · corrige el motivo y vuelve a subir este archivo (las columnas Motivo y Fila se ignoran)",
            headers,
            failed.Select(r => Enumerable.Range(0, width).Select(c => (object?)ImportEngine.Display(c < r.Raw.Length ? r.Raw[c] : null))
                .Append(string.Join(" · ", r.Errors)).Append(r.Fila).ToArray()));
        var name = Path.GetFileNameWithoutExtension(file!.FileName);
        return (content, $"{name} - errores.xlsx");
    }

    public async Task<ImportResultDto> ExecuteAsync(string entidad, IFormFile? file, Request request, Guid userId, CancellationToken ct)
    {
        var definition = Definition(entidad);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var (_, _, rows) = await ValidateAsync(entidad, file, request, ct);
        var applied = await definition.ApplyAsync(rows, new ImportApplyContext(request.Duplicados, request.Mapeo.Keys, userId, _clock.Now), ct);
        var run = new ImportRun
        {
            Entidad = definition.Entidad,
            ArchivoNombre = Truncate(file!.FileName, 260),
            UsuarioId = userId,
            FilasTotales = rows.Count,
            Creadas = applied.Creadas,
            Actualizadas = applied.Actualizadas,
            Omitidas = applied.Omitidas,
            ConError = rows.Count(r => r.Status == ImportRowStatus.Error),
            ModoDuplicados = request.Duplicados.ToString(),
            Detalle = applied.Detalle is null ? null : Truncate(applied.Detalle, 300),
        };
        _db.ImportRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        await definition.AfterSaveAsync(ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ImportResultDto
        {
            Id = run.Id, Total = run.FilasTotales, Creadas = run.Creadas, Actualizadas = run.Actualizadas,
            Omitidas = run.Omitidas, ConError = run.ConError, Detalle = run.Detalle,
        };
    }

    public async Task<PagedResponse<ImportRunDto>> HistoryAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.ImportRuns.AsNoTracking().OrderByDescending(r => r.CreatedAt);
        var total = await query.CountAsync(ct);
        var runs = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r, Usuario = r.Usuario == null ? "" : r.Usuario.Nombre + " " + r.Usuario.Apellido })
            .ToListAsync(ct);
        var names = _definitions.ToDictionary(d => d.Entidad, d => d.Nombre);
        return new PagedResponse<ImportRunDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            Items = runs.Select(x => new ImportRunDto
            {
                Id = x.r.Id, Entidad = x.r.Entidad, EntidadNombre = names.GetValueOrDefault(x.r.Entidad, x.r.Entidad),
                ArchivoNombre = x.r.ArchivoNombre, Fecha = x.r.CreatedAt, Usuario = x.Usuario.Trim(),
                FilasTotales = x.r.FilasTotales, Creadas = x.r.Creadas, Actualizadas = x.r.Actualizadas,
                Omitidas = x.r.Omitidas, ConError = x.r.ConError, ModoDuplicados = x.r.ModoDuplicados, Detalle = x.r.Detalle,
            }).ToList(),
        };
    }

    // ── Internos ──

    private async Task<(SpreadsheetReader.Sheet Sheet, IReadOnlyList<ImportField> Fields, List<ImportRow> Rows)> ValidateAsync(
        string entidad, IFormFile? file, Request request, CancellationToken ct)
    {
        var definition = Definition(entidad);
        var sheet = Read(file);
        var fields = await definition.GetFieldsAsync(ct);
        var headerRow = request.FilaEncabezados - 1;
        if (headerRow < 0 || headerRow >= sheet.Rows.Count) throw new ApiException(400, "La fila de encabezados no existe en el archivo.");
        CheckMapping(request.Mapeo, fields, sheet.Rows, headerRow);
        List<ImportRow> rows;
        try
        {
            rows = ImportEngine.ReadRows(sheet.Rows, headerRow, request.Mapeo, fields, _clock.Today);
        }
        catch (ImportFileException e)
        {
            throw new ApiException(e.Status, e.Message);
        }
        if (rows.Count == 0) throw new ApiException(400, "El archivo no tiene filas de datos debajo de los encabezados.");
        await definition.ValidateAsync(rows, ct);
        return (sheet, fields, rows);
    }

    private static void CheckMapping(Dictionary<string, int> mapping, IReadOnlyList<ImportField> fields, List<object?[]> rows, int headerRow)
    {
        var keys = fields.Select(f => f.Key).ToHashSet();
        var unknown = mapping.Keys.Where(k => !keys.Contains(k)).ToList();
        if (unknown.Count > 0) throw new ApiException(400, $"Campos desconocidos en el mapeo: {string.Join(", ", unknown)}.");
        var width = rows.Skip(headerRow).Take(200).Select(r => r.Length).DefaultIfEmpty(0).Max();
        if (mapping.Values.Any(c => c < 0 || c >= width)) throw new ApiException(400, "El mapeo apunta a una columna que el archivo no tiene.");
        var repeated = mapping.GroupBy(m => m.Value).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
            throw new ApiException(400, $"La columna {ImportEngine.ColumnLetter(repeated.Key)} está asignada a más de un campo.");
    }

    private IImportDefinition Definition(string entidad) =>
        _definitions.FirstOrDefault(d => string.Equals(d.Entidad, entidad, StringComparison.OrdinalIgnoreCase))
        ?? throw ApiException.NotFound($"No se puede importar «{entidad}».");

    private static SpreadsheetReader.Sheet Read(IFormFile? file)
    {
        if (file is null) throw new ApiException(400, "Sube un archivo .xlsx, .xls o .csv.");
        if (file.Length == 0) throw new ApiException(400, "El archivo está vacío.");
        try
        {
            using var stream = file.OpenReadStream();
            return SpreadsheetReader.Read(stream);
        }
        catch (ImportFileException e)
        {
            throw new ApiException(e.Status, e.Message);
        }
    }

    private static ImportPreviewRowDto ToDto(ImportRow row, IReadOnlyList<ImportField> fields) => new()
    {
        Fila = row.Fila,
        Estado = row.Status.ToString(),
        Valores = row.Values.Where(v => v.Value is not null)
            .ToDictionary(v => v.Key, v => Show(v.Value!)),
        Errores = row.Errors,
        Avisos = row.Warnings,
        Existente = row.ExistingLabel,
    };

    /// <summary>Cómo se ve un valor ya limpio en la vista previa: fechas dd/mm/aaaa, pesos con puntos de miles, sí/no.</summary>
    private static string Show(object value) => value switch
    {
        DateOnly d => d.ToString("dd/MM/yyyy"),
        decimal m => m.ToString(m == decimal.Truncate(m) ? "#,0" : "#,0.##", Es),
        bool b => b ? "Sí" : "No",
        _ => value.ToString()!,
    };

    private static readonly System.Globalization.CultureInfo Es = System.Globalization.CultureInfo.GetCultureInfo("es-CO");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>El mapeo llega como JSON en el formulario multipart: {"nombre": 0, "documento": 3}.</summary>
    public static Dictionary<string, int> ParseMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ApiException(400, "Falta el mapeo de columnas.");
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];
        }
        catch (JsonException)
        {
            throw new ApiException(400, "El mapeo de columnas no es válido.");
        }
    }
}
