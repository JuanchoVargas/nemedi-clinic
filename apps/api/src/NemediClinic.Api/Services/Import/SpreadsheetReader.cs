using System.Text;
using ExcelDataReader;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// Lee la primera hoja de un .xlsx, .xls o .csv EN MEMORIA (nunca toca el disco: son datos de
/// pacientes) y devuelve la grilla de celdas. El tipo se decide por el contenido, no por la extensión:
///   · PK… (zip) → .xlsx · D0 CF 11 E0 (OLE) → .xls · texto sin bytes nulos → .csv (separador
///     detectado entre ; , tab y |; UTF-8, y si no lo es, Windows-1252 como guarda Excel en español).
/// Cualquier otra cosa → <see cref="ImportFileException"/> (415).
/// </summary>
public static class SpreadsheetReader
{
    public const int MaxBytes = 10 * 1024 * 1024;

    static SpreadsheetReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public sealed record Sheet(string Format, List<object?[]> Rows);

    public static Sheet Read(Stream input)
    {
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        if (buffer.Length == 0) throw new ImportFileException(400, "El archivo está vacío.");
        if (buffer.Length > MaxBytes) throw new ImportFileException(413, "El archivo pesa más de 10 MB. Pártelo en varios.");
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var format = Detect(bytes);
        buffer.Position = 0;

        try
        {
            using var reader = format switch
            {
                "csv" => ExcelReaderFactory.CreateCsvReader(buffer, new ExcelReaderConfiguration
                {
                    FallbackEncoding = Encoding.GetEncoding(1252),
                    AutodetectSeparators = [';', ',', '\t', '|'],
                    AnalyzeInitialCsvRows = 50,
                }),
                _ => ExcelReaderFactory.CreateReader(buffer),
            };
            var rows = new List<object?[]>();
            // Solo la primera hoja
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++) row[i] = reader.GetValue(i);
                rows.Add(row);
            }
            return new Sheet(format, rows);
        }
        catch (Exception e) when (e is not ImportFileException)
        {
            throw new ImportFileException(415, $"No se pudo leer el archivo como {Label(format)}: está dañado o protegido con contraseña.");
        }
    }

    private static string Detect(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04) return "xlsx";
        if (b.Length >= 8 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0) return "xls";
        // Texto: sin bytes nulos en lo primero que se lee (un PDF, una imagen o un .docx no pasan)
        var sample = b[..Math.Min(b.Length, 4096)];
        if (sample.IndexOf((byte)0) < 0 && !sample.StartsWith("%PDF"u8)) return "csv";
        throw new ImportFileException(415, "El archivo no es un Excel (.xlsx, .xls) ni un CSV.");
    }

    private static string Label(string format) => format switch { "xlsx" => "Excel (.xlsx)", "xls" => "Excel (.xls)", _ => "CSV" };
}

/// <summary>Un problema con el archivo mismo (vacío, demasiado grande, no es Excel): status + mensaje para la persona.</summary>
public class ImportFileException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
