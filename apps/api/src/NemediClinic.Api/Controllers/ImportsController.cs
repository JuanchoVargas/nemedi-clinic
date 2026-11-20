using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NemediClinic.Api.Services;
using NemediClinic.Api.Services.Import;
using NemediClinic.Application.DTOs.Imports;

namespace NemediClinic.Api.Controllers;

/// <summary>
/// Importar desde Excel/CSV (dueño y recepción). Cada paso recibe el archivo de nuevo: el servidor no lo
/// guarda (se lee en memoria y se descarta). El tenant es el del JWT; nada del archivo lo cambia.
/// </summary>
[ApiController]
[Route("api/v1/imports")]
[Authorize(Policy = "Admin")]
public class ImportsController(ImportService imports) : ControllerBase
{
    // 10 MB del archivo + el resto del formulario
    private const long MaxRequest = SpreadsheetReader.MaxBytes + 1024 * 1024;

    public class StepForm
    {
        public IFormFile? File { get; set; }
        /// <summary>1-based, como en Excel.</summary>
        public int FilaEncabezados { get; set; }
        /// <summary>JSON campo → índice de columna.</summary>
        public string? Mapeo { get; set; }
        public DuplicateMode Duplicados { get; set; } = DuplicateMode.Omitir;
        public bool SoloProblemas { get; set; }

        public ImportService.Request ToRequest() => new(FilaEncabezados, ImportService.ParseMapping(Mapeo), Duplicados);
    }

    [HttpGet]
    public async Task<IActionResult> History([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await imports.HistoryAsync(page, pageSize, ct));

    [HttpPost("{entidad}/analyze")]
    [RequestSizeLimit(MaxRequest)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequest)]
    public async Task<ActionResult<ImportAnalysisDto>> Analyze(string entidad, IFormFile? file, [FromForm] int? filaEncabezados, CancellationToken ct) =>
        Ok(await imports.AnalyzeAsync(entidad, file, filaEncabezados, ct));

    [HttpPost("{entidad}/preview")]
    [RequestSizeLimit(MaxRequest)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequest)]
    public async Task<ActionResult<ImportPreviewDto>> Preview(string entidad, [FromForm] StepForm form, CancellationToken ct) =>
        Ok(await imports.PreviewAsync(entidad, form.File, form.ToRequest(), form.SoloProblemas, ct));

    [HttpPost("{entidad}/errors")]
    [RequestSizeLimit(MaxRequest)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequest)]
    public async Task<IActionResult> Errors(string entidad, [FromForm] StepForm form, CancellationToken ct)
    {
        var (content, name) = await imports.ErrorsWorkbookAsync(entidad, form.File, form.ToRequest(), ct);
        return File(content, ImportService.XlsxContentType, name);
    }

    [HttpPost("{entidad}/execute")]
    [RequestSizeLimit(MaxRequest)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequest)]
    public async Task<ActionResult<ImportResultDto>> Execute(string entidad, [FromForm] StepForm form, CancellationToken ct) =>
        Ok(await imports.ExecuteAsync(entidad, form.File, form.ToRequest(), User.UserId(), ct));
}
