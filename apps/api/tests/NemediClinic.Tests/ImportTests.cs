using System.Text;
using ClosedXML.Excel;
using NemediClinic.Api.Services.Import;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>El motor del importador sin base: lectura, encabezado, mapeo automático, fechas y limpieza.</summary>
public class ImportTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private static readonly List<ImportField> Fields = PatientImportDefinition.Fields(esIps: false);
    private static readonly List<ImportField> IpsFields = PatientImportDefinition.Fields(esIps: true);

    private static SpreadsheetReader.Sheet Xlsx(params object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Pacientes");
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = ws.Cell(r + 1, c + 1);
                switch (rows[r][c])
                {
                    case null: break;
                    case DateTime d: cell.Value = d; break;
                    case double n: cell.Value = n; break;
                    case int n: cell.Value = n; break;
                    case var v: cell.Value = v.ToString(); break;
                }
            }
        // Una segunda hoja que no se debe leer
        wb.AddWorksheet("Otra").Cell(1, 1).Value = "no";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return SpreadsheetReader.Read(new MemoryStream(ms.ToArray()));
    }

    private static SpreadsheetReader.Sheet Csv(string text, Encoding? encoding = null) =>
        SpreadsheetReader.Read(new MemoryStream((encoding ?? Encoding.UTF8).GetBytes(text)));

    private static Dictionary<string, string> MappedHeaders(SpreadsheetReader.Sheet sheet, List<ImportField>? fields = null)
    {
        fields ??= Fields;
        var header = ImportEngine.DetectHeaderRow(sheet.Rows, fields);
        var columns = ImportEngine.Columns(sheet.Rows, header);
        return ImportEngine.SuggestMapping(columns, fields)
            .ToDictionary(m => m.Key, m => columns.First(c => c.Index == m.Value).Header);
    }

    // ── Archivo ──

    [Fact]
    public void Archivo_limpio_xlsx_se_lee_y_mapea_completo()
    {
        var sheet = Xlsx(
            ["Nombre", "Apellido", "Tipo de documento", "Cédula", "Fecha nac", "Sexo", "Celular", "Correo", "Dirección", "Ciudad"],
            ["MARÍA JOSÉ", "pérez  de la  hoz", "CC", "1.020.304.050", new DateTime(1990, 3, 15), "F", "+57 310 555 1234", "Maria@Example.com", "Cra 7 # 45-10", "BOGOTÁ"]);
        Assert.Equal("xlsx", sheet.Format);
        var mapping = MappedHeaders(sheet);
        Assert.Equal("Cédula", mapping["documento"]);
        Assert.Equal("Celular", mapping["telefono"]);
        Assert.Equal("Fecha nac", mapping["fechaNacimiento"]);
        Assert.Equal("Correo", mapping["email"]);
        Assert.Equal("Tipo de documento", mapping["tipoDocumento"]);
        Assert.Equal(10, mapping.Count);
        Assert.False(mapping.ContainsKey("nombreCompleto"));

        var rows = Read(sheet);
        var row = Assert.Single(rows);
        Assert.Empty(row.Errors);
        Assert.Equal("María José", row.Text("nombre"));
        Assert.Equal("Pérez de la Hoz", row.Text("apellido"));
        Assert.Equal("1020304050", row.Text("documento"));
        Assert.Equal("3105551234", row.Text("telefono"));
        Assert.Equal("maria@example.com", row.Text("email"));
        Assert.Equal("Bogotá", row.Text("ciudad"));
        Assert.Equal(new DateOnly(1990, 3, 15), row.Get<DateOnly>("fechaNacimiento"));
        Assert.Equal("Mujer", row.Text("sexo"));
    }

    [Fact]
    public void Encabezado_en_la_fila_3_se_detecta()
    {
        var sheet = Xlsx(
            ["Clínica Demo — listado de pacientes"],
            ["Generado el 30/09/2026", null, "Página 1"],
            ["Documento", "Nombres", "Apellidos", "Teléfono"],
            ["52111222", "Ana", "Gómez", "3001112233"],
            ["79888777", "Luis", "Mora", "3014445566"]);
        var fields = Fields;
        Assert.Equal(2, ImportEngine.DetectHeaderRow(sheet.Rows, fields));
        var rows = Read(sheet);
        Assert.Equal([4, 5], rows.Select(r => r.Fila));
        Assert.Equal("Ana", rows[0].Text("nombre"));
    }

    [Fact]
    public void Columnas_en_desorden_y_con_nombres_distintos()
    {
        var sheet = Xlsx(
            ["E-mail", "Teléfono celular", "Identificación", "Primer apellido", "Primer nombre", "Género", "Observaciones"],
            ["a@example.com", "3101234567", "123456789", "Ruiz", "Camila", "Femenino", "alérgica"]);
        var mapping = MappedHeaders(sheet);
        Assert.Equal("E-mail", mapping["email"]);
        Assert.Equal("Teléfono celular", mapping["telefono"]);
        Assert.Equal("Identificación", mapping["documento"]);
        Assert.Equal("Primer apellido", mapping["apellido"]);
        Assert.Equal("Primer nombre", mapping["nombre"]);
        Assert.Equal("Género", mapping["sexo"]);
        Assert.DoesNotContain("Observaciones", mapping.Values); // sin campo: se ignora
    }

    [Fact]
    public void Columnas_sin_titulo_no_se_asignan_solas()
    {
        // Con la fila 1 (un título del reporte) como encabezado, las demás columnas son "Columna B", "Columna C"…
        var sheet = Xlsx(["Listado de pacientes"], ["Ana", "Gómez", "52111222"]);
        var columns = ImportEngine.Columns(sheet.Rows, 0);
        Assert.Contains(columns, c => c.Header == "Columna C" && !c.Titled);
        Assert.Empty(ImportEngine.SuggestMapping(columns, Fields));
    }

    [Fact]
    public void Una_columna_tenant_no_se_mapea_a_nada()
    {
        var sheet = Xlsx(["TenantId", "Nombre", "Apellido", "Cédula"], [Guid.NewGuid().ToString(), "Ana", "Gómez", "52111222"]);
        Assert.DoesNotContain("TenantId", MappedHeaders(sheet).Values);
    }

    [Fact]
    public void Nombre_completo_cuando_viene_en_una_columna()
    {
        var sheet = Xlsx(["Paciente", "CC"], ["ANA MARÍA GÓMEZ DE LA ROSA", "52111222"]);
        var mapping = MappedHeaders(sheet);
        Assert.Equal("Paciente", mapping["nombreCompleto"]);
        Assert.Equal("CC", mapping["documento"]);
    }

    [Fact]
    public void Campos_rips_solo_para_ips()
    {
        var sheet = Xlsx(["Nombre", "Apellido", "Cédula", "Régimen", "EPS", "Código EPS", "Código DANE", "Zona"], ["A", "B", "123456", "01", "Sura", "EPS010", "5001", "U"]);
        var basic = MappedHeaders(sheet);
        Assert.False(basic.ContainsKey("tipoUsuario"));
        var ips = MappedHeaders(sheet, IpsFields);
        Assert.Equal("Régimen", ips["tipoUsuario"]);
        Assert.Equal("EPS", ips["nombreEntidad"]);
        Assert.Equal("Código EPS", ips["codigoEntidad"]);
        Assert.Equal("Código DANE", ips["municipioResidencia"]);
        var row = Assert.Single(Read(sheet, IpsFields));
        Assert.Equal("05001", row.Text("municipioResidencia"));
        Assert.Contains(row.Warnings, w => w.Contains("0 inicial"));
        Assert.Equal("ContributivoCotizante", row.Text("tipoUsuario"));
        Assert.Equal("Urbana", row.Text("zonaResidencia"));
    }

    [Fact]
    public void Csv_con_punto_y_coma_y_ansi()
    {
        var sheet = Csv("Cédula;Nombre;Apellido\r\n52.111.222;josé;muñoz\r\n", Encoding.GetEncoding(1252));
        Assert.Equal("csv", sheet.Format);
        var row = Assert.Single(Read(sheet));
        Assert.Equal("José", row.Text("nombre"));
        Assert.Equal("Muñoz", row.Text("apellido"));
        Assert.Equal("52111222", row.Text("documento"));
    }

    [Fact]
    public void Archivo_vacio_es_400()
    {
        var e = Assert.Throws<ImportFileException>(() => SpreadsheetReader.Read(new MemoryStream()));
        Assert.Equal(400, e.Status);
    }

    [Fact]
    public void Archivo_que_no_es_excel_es_415()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D };
        Assert.Equal(415, Assert.Throws<ImportFileException>(() => SpreadsheetReader.Read(new MemoryStream(png))).Status);
        var pdf = "%PDF-1.7\nhola"u8.ToArray();
        Assert.Equal(415, Assert.Throws<ImportFileException>(() => SpreadsheetReader.Read(new MemoryStream(pdf))).Status);
        // Un zip que no es xlsx (p. ej. un .docx dañado)
        var zip = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6 };
        Assert.Equal(415, Assert.Throws<ImportFileException>(() => SpreadsheetReader.Read(new MemoryStream(zip))).Status);
    }

    [Fact]
    public void Mas_de_5000_filas_es_400()
    {
        var rows = new List<object?[]> { new object?[] { "Cédula", "Nombre", "Apellido" } };
        for (var i = 0; i < ImportEngine.MaxRows + 1; i++) rows.Add([$"{1000000 + i}", "A", "B"]);
        var mapping = new Dictionary<string, int> { ["documento"] = 0, ["nombre"] = 1, ["apellido"] = 2 };
        var e = Assert.Throws<ImportFileException>(() => ImportEngine.ReadRows(rows, 0, mapping, Fields, Today));
        Assert.Equal(400, e.Status);
        Assert.Contains("5.000", e.Message);
    }

    [Fact]
    public void Obligatorio_sin_columna_o_vacio_es_error()
    {
        var rows = new List<object?[]> { new object?[] { "Nombre", "Cédula" }, new object?[] { "Ana", null }, new object?[] { "Luis", "123" } };
        var sinDocumento = ImportEngine.ReadRows(rows, 0, new Dictionary<string, int> { ["nombre"] = 0 }, Fields, Today);
        Assert.All(sinDocumento, r => Assert.Contains(r.Errors, e => e.Contains("no está asignada")));
        var conDocumento = ImportEngine.ReadRows(rows, 0, new Dictionary<string, int> { ["nombre"] = 0, ["documento"] = 1 }, Fields, Today);
        Assert.Contains(conDocumento[0].Errors, e => e.Contains("está vacío"));
        Assert.Empty(conDocumento[1].Errors);
    }

    // ── Fechas ──

    [Theory]
    [InlineData("15/03/1990", 1990, 3, 15)]
    [InlineData("5/3/1990", 1990, 3, 5)]
    [InlineData("15-03-1990", 1990, 3, 15)]
    [InlineData("15.03.1990", 1990, 3, 15)]
    [InlineData("15/03/90", 1990, 3, 15)]
    [InlineData("15-3-05", 2005, 3, 15)]
    [InlineData("1990-03-15", 1990, 3, 15)]
    [InlineData("1990/03/15", 1990, 3, 15)]
    [InlineData("15 de marzo de 1990", 1990, 3, 15)]
    [InlineData("15 mar 1990", 1990, 3, 15)]
    [InlineData("15-mar-90", 1990, 3, 15)]
    [InlineData("Marzo 15 de 1990", 1990, 3, 15)]
    [InlineData("15 Sept 1990", 1990, 9, 15)]
    [InlineData("15031990", 1990, 3, 15)]
    [InlineData("19900315", 1990, 3, 15)]
    [InlineData("32947", 1990, 3, 15)]
    [InlineData("15/03/1990 00:00:00", 1990, 3, 15)]
    [InlineData("15/03/1990 12:00 a. m.", 1990, 3, 15)]
    [InlineData("1990-03-15T00:00:00", 1990, 3, 15)]
    public void Formatos_de_fecha(string text, int y, int m, int d)
    {
        var r = ImportDates.Parse(text, ImportDates.Order.Unknown, Today);
        Assert.Null(r.Error);
        Assert.Equal(new DateOnly(y, m, d), r.Value);
    }

    [Fact]
    public void Fecha_de_excel_como_celda_y_como_serial()
    {
        Assert.Equal(new DateOnly(1990, 3, 15), ImportDates.Parse(new DateTime(1990, 3, 15), ImportDates.Order.Unknown, Today).Value);
        Assert.Equal(new DateOnly(1990, 3, 15), ImportDates.Parse(32947d, ImportDates.Order.Unknown, Today).Value);
    }

    [Fact]
    public void Fecha_ambigua_se_lee_dia_mes_y_se_avisa()
    {
        var r = ImportDates.Parse("03/04/1990", ImportDates.Order.Unknown, Today);
        Assert.Equal(new DateOnly(1990, 4, 3), r.Value);
        Assert.Contains("ambigua", r.Warning);
        // Mismo día y mes: no hay nada que confundir
        Assert.Null(ImportDates.Parse("04/04/1990", ImportDates.Order.Unknown, Today).Warning);
    }

    [Fact]
    public void La_columna_decide_dia_mes_o_mes_dia()
    {
        Assert.Equal(ImportDates.Order.DayFirst, ImportDates.DetectOrder(["03/04/1990", "25/12/1985"]));
        Assert.Equal(ImportDates.Order.MonthFirst, ImportDates.DetectOrder(["03/04/1990", "12/25/1985"]));
        Assert.Equal(ImportDates.Order.Unknown, ImportDates.DetectOrder(["03/04/1990", "01/02/1985"]));

        var dayFirst = ImportDates.Parse("03/04/1990", ImportDates.Order.DayFirst, Today);
        Assert.Equal(new DateOnly(1990, 4, 3), dayFirst.Value);
        Assert.Null(dayFirst.Warning);
        var monthFirst = ImportDates.Parse("03/04/1990", ImportDates.Order.MonthFirst, Today);
        Assert.Equal(new DateOnly(1990, 3, 4), monthFirst.Value);
        Assert.Contains("mes/día", monthFirst.Warning);
    }

    [Fact]
    public void Varios_formatos_en_la_misma_columna()
    {
        var sheet = Xlsx(
            ["Cédula", "Nombre", "Apellido", "Fecha de nacimiento"],
            ["1001", "A", "B", "25/12/1985"],
            ["1002", "C", "D", new DateTime(1990, 3, 15)],
            ["1003", "E", "F", 32947d],
            ["1004", "G", "H", "15 de marzo de 1990"],
            ["1005", "I", "J", "1990-03-15"],
            ["1006", "K", "L", "03/04/1990"]);
        var rows = Read(sheet);
        Assert.All(rows, r => Assert.Empty(r.Errors));
        Assert.Equal(new DateOnly(1985, 12, 25), rows[0].Get<DateOnly>("fechaNacimiento"));
        Assert.All(rows.Skip(1).Take(4), r => Assert.Equal(new DateOnly(1990, 3, 15), r.Get<DateOnly>("fechaNacimiento")));
        // La columna trae un 25/12: es día/mes y 03/04 es 3 de abril sin aviso
        Assert.Equal(new DateOnly(1990, 4, 3), rows[5].Get<DateOnly>("fechaNacimiento"));
        Assert.Empty(rows[5].Warnings);
    }

    [Theory]
    [InlineData("31/02/1990", "inexistente")]
    [InlineData("15/03/2030", "futuro")]
    [InlineData("15/03/1850", "antigua")]
    [InlineData("ayer", "ilegible")]
    [InlineData("15/13/1990", "inexistente")]
    public void Fechas_invalidas(string text, string error)
    {
        var r = ImportDates.Parse(text, ImportDates.Order.Unknown, Today);
        Assert.Null(r.Value);
        Assert.Contains(error, r.Error);
    }

    [Fact]
    public void Anio_de_dos_digitos_avisa_el_siglo()
    {
        var r = ImportDates.Parse("15/03/26", ImportDates.Order.Unknown, Today);
        Assert.Equal(new DateOnly(2026, 3, 15), r.Value);
        Assert.Contains("2026", r.Warning);
        Assert.Equal(1927, ImportDates.Parse("15/03/27", ImportDates.Order.Unknown, Today).Value!.Value.Year);
    }

    // ── Limpieza ──

    [Theory]
    [InlineData("  MARÍA   JOSÉ ", "María José")]
    [InlineData("juan DE LOS santos", "Juan de los Santos")]
    [InlineData("ana-maría", "Ana-María")]
    public void Nombres_con_mayusculas_consistentes(string raw, string expected) => Assert.Equal(expected, ImportText.Name(raw));

    [Theory]
    [InlineData("1.020.304.050", "1020304050")]
    [InlineData("1020-304 050", "1020304050")]
    [InlineData("ab-123", "AB123")]
    public void Documentos_sin_puntos_ni_guiones(string raw, string expected) => Assert.Equal(expected, ImportText.Document(raw));

    [Theory]
    [InlineData("+57 (310) 555-1234", "3105551234")]
    [InlineData("573105551234", "3105551234")]
    [InlineData("601 345 6789", "6013456789")]
    public void Telefonos_sin_formato(string raw, string expected) => Assert.Equal(expected, ImportText.Phone(raw));

    [Fact]
    public void Numeros_de_excel_sin_decimales_en_documento_y_telefono()
    {
        var row = Assert.Single(Read(Xlsx(["Cédula", "Nombre", "Apellido", "Celular"], [1020304050d, "A", "B", 3105551234d])));
        Assert.Equal("1020304050", row.Text("documento"));
        Assert.Equal("3105551234", row.Text("telefono"));
    }

    [Fact]
    public void Sexo_M_depende_de_la_columna()
    {
        Assert.Equal(["Hombre", "Mujer"], SexColumn("M", "F"));
        Assert.Equal(["Mujer", "Hombre"], SexColumn("M", "H"));
        var solo = Read(Xlsx(["Cédula", "Nombre", "Apellido", "Sexo"], ["1001", "A", "B", "M"]));
        Assert.Contains(solo[0].Errors, e => e.Contains("ambigua"));
    }

    private static string[] SexColumn(params string[] values)
    {
        var rows = new List<object?[]> { new object?[] { "Cédula", "Nombre", "Apellido", "Sexo" } };
        rows.AddRange(values.Select((v, i) => new object?[] { $"{1000 + i}", "A", "B", v }));
        return Read(new SpreadsheetReader.Sheet("xlsx", rows)).Select(r => r.Text("sexo") ?? "").ToArray();
    }

    [Fact]
    public void Correo_invalido_y_texto_largo_son_error()
    {
        var row = Assert.Single(Read(Xlsx(["Cédula", "Nombre", "Apellido", "Correo"], ["1001", new string('a', 101), "B", "no-es-correo"])));
        Assert.Contains(row.Errors, e => e.StartsWith("Correo"));
        Assert.Contains(row.Errors, e => e.StartsWith("Nombre") && e.Contains("máximo"));
    }

    private static List<ImportRow> Read(SpreadsheetReader.Sheet sheet, List<ImportField>? fields = null)
    {
        fields ??= Fields;
        var header = ImportEngine.DetectHeaderRow(sheet.Rows, fields);
        var mapping = ImportEngine.SuggestMapping(ImportEngine.Columns(sheet.Rows, header), fields);
        return ImportEngine.ReadRows(sheet.Rows, header, mapping, fields, Today);
    }
}
