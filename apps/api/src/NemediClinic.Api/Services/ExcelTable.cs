using ClosedXML.Excel;

namespace NemediClinic.Api.Services;

/// <summary>
/// Una hoja de Excel con título, subtítulo y una tabla: lo que comparten la caja y los reportes.
/// Fechas con formato dd/mm/yyyy, decimales desde <c>moneyFromColumn</c> (1-based) con formato de pesos.
/// </summary>
public static class ExcelTable
{
    public static byte[] Build(string hoja, string titulo, string subtitulo, string[] encabezados,
        IEnumerable<object?[]> filas, int? moneyFromColumn = null, IEnumerable<int>? moneyColumns = null)
    {
        using var libro = new XLWorkbook();
        AddSheet(libro, hoja, titulo, subtitulo, encabezados, filas, moneyFromColumn, moneyColumns);
        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    public static void AddSheet(XLWorkbook libro, string hoja, string titulo, string subtitulo, string[] encabezados,
        IEnumerable<object?[]> filas, int? moneyFromColumn = null, IEnumerable<int>? moneyColumns = null)
    {
        // Excel no acepta más de 31 caracteres en el nombre de la hoja
        var sheet = libro.Worksheets.Add(hoja.Length > 31 ? hoja[..31] : hoja);
        var ancho = Math.Max(encabezados.Length, 1);
        sheet.Cell(1, 1).Value = titulo;
        sheet.Cell(2, 1).Value = subtitulo;
        sheet.Range(1, 1, 1, ancho).Merge().Style.Font.SetBold().Font.SetFontSize(13);
        sheet.Range(2, 1, 2, ancho).Merge().Style.Font.SetItalic();

        for (var i = 0; i < encabezados.Length; i++)
            sheet.Cell(4, i + 1).Value = encabezados[i];
        sheet.Row(4).Style.Font.SetBold();
        sheet.SheetView.FreezeRows(4);

        var dinero = new HashSet<int>(moneyColumns ?? []);
        var fila = 5;
        foreach (var valores in filas)
        {
            for (var c = 0; c < valores.Length; c++)
            {
                var cell = sheet.Cell(fila, c + 1);
                switch (valores[c])
                {
                    case null:
                        break;
                    case DateTime fecha:
                        cell.Value = fecha;
                        cell.Style.DateFormat.Format = fecha.TimeOfDay == TimeSpan.Zero ? "dd/mm/yyyy" : "dd/mm/yyyy hh:mm";
                        break;
                    case DateOnly dia:
                        cell.Value = dia.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = "dd/mm/yyyy";
                        break;
                    case decimal numero:
                        cell.Value = numero;
                        if ((moneyFromColumn.HasValue && c + 1 >= moneyFromColumn.Value) || dinero.Contains(c + 1))
                            cell.Style.NumberFormat.Format = "$ #,##0";
                        break;
                    case int entero:
                        cell.Value = entero;
                        break;
                    case bool si:
                        cell.Value = si ? "Sí" : "No";
                        break;
                    default:
                        cell.Value = valores[c]!.ToString();
                        break;
                }
            }
            fila++;
        }

        if (fila == 5)
            sheet.Cell(5, 1).Value = "Sin datos en el periodo seleccionado.";

        sheet.Columns(1, ancho).AdjustToContents();
    }
}
