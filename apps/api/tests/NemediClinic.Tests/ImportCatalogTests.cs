using NemediClinic.Api.Services.Import;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>Importador de catálogo (productos, procedimientos, paquetes) sin base: números, vencimientos y mapeo automático.</summary>
public class ImportCatalogTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static Dictionary<string, string> Mapped(List<ImportField> fields, params string[] headers)
    {
        var rows = new List<object?[]> { headers.Cast<object?>().ToArray(), headers.Select(_ => (object?)"x").ToArray() };
        var columns = ImportEngine.Columns(rows, 0);
        return ImportEngine.SuggestMapping(columns, fields).ToDictionary(m => m.Key, m => headers[m.Value]);
    }

    // ── Mapeo automático con los alias pedidos ──

    [Fact]
    public void Productos_alias_colombianos()
    {
        var m = Mapped(ProductImportDefinition.Fields(),
            "Ref", "Producto", "Valor", "IVA", "Clasificación", "Registro INVIMA", "Lote", "Cant", "Vence", "Proveedor", "Costo", "Categoría");
        Assert.Equal("Ref", m["referencia"]);
        Assert.Equal("Producto", m["nombre"]);
        Assert.Equal("Valor", m["precioVenta"]);
        Assert.Equal("IVA", m["iva"]);
        Assert.Equal("Clasificación", m["tipoRegulatorio"]);
        Assert.Equal("Registro INVIMA", m["registroInvima"]);
        Assert.Equal("Lote", m["numeroLote"]);
        Assert.Equal("Cant", m["cantidad"]);
        Assert.Equal("Vence", m["vencimiento"]);
        Assert.Equal("Proveedor", m["proveedor"]);
        // El costo va con el lote; la categoría con el producto
        Assert.Equal("Costo", m["costo"]);
        Assert.Equal("Categoría", m["categoria"]);
    }

    [Theory]
    [InlineData("Precio de compra", "costo")]
    [InlineData("Costo unitario", "costo")]
    [InlineData("Línea", "categoria")]
    [InlineData("Familia", "categoria")]
    public void Productos_costo_y_categoria(string header, string field) =>
        Assert.Equal(header, Mapped(ProductImportDefinition.Fields(), header, "Nombre", "Precio de venta")[field]);

    [Theory]
    [InlineData("facial", "Facial")]           // ya existe: se usa la de la clínica
    [InlineData("  FACIAL ", "Facial")]
    [InlineData("corporal", "Corporal")]       // nueva: primera letra en mayúscula
    [InlineData("Cuidado   capilar", "Cuidado capilar")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Categoria_sin_variantes_de_lo_mismo(string value, string? expected) =>
        Assert.Equal(expected, NemediClinic.Api.Services.ProductCategories.Normalize(value, ["Facial", "Insumos generales"]));

    [Theory]
    [InlineData("Referencia", "referencia")]
    [InlineData("SKU", "referencia")]
    [InlineData("Precio de venta", "precioVenta")]
    [InlineData("Stock inicial", "cantidad")]
    [InlineData("Fecha de vencimiento", "vencimiento")]
    [InlineData("Número de lote", "numeroLote")]
    public void Productos_otros_encabezados(string header, string field) =>
        Assert.Equal(header, Mapped(ProductImportDefinition.Fields(), header, "Nombre")[field]);

    [Fact]
    public void Procedimientos_alias_colombianos()
    {
        var m = Mapped(ProcedureImportDefinition.Fields(esIps: true),
            "Servicio", "Dura", "Valor", "IVA", "Zona", "CUPS", "Finalidad", "Tipo de servicio", "Reporta RIPS");
        Assert.Equal("Servicio", m["nombre"]);
        Assert.Equal("Dura", m["duracion"]);
        Assert.Equal("Valor", m["precio"]);
        Assert.Equal("Zona", m["areaCorporal"]);
        Assert.Equal("CUPS", m["codigoCups"]);
        Assert.Equal("Finalidad", m["finalidad"]);
        Assert.Equal("Tipo de servicio", m["tipoServicio"]);
        Assert.Equal("Reporta RIPS", m["requiereRips"]);
        Assert.Equal("Tiempo", Mapped(ProcedureImportDefinition.Fields(false), "Tiempo", "Nombre")["duracion"]);
        Assert.Equal("Precio de venta", Mapped(ProcedureImportDefinition.Fields(false), "Precio de venta", "Nombre")["precio"]);
        Assert.False(Mapped(ProcedureImportDefinition.Fields(esIps: false), "CUPS", "Nombre").ContainsKey("codigoCups"));
    }

    [Fact]
    public void Paquetes_alias()
    {
        var m = Mapped(PackageImportDefinition.Fields(), "Paquete", "Valor", "Vigencia", "Procedimiento", "Sesiones");
        Assert.Equal("Paquete", m["paquete"]);
        Assert.Equal("Valor", m["precio"]);
        Assert.Equal("Vigencia", m["vigencia"]);
        Assert.Equal("Procedimiento", m["procedimiento"]);
        Assert.Equal("Sesiones", m["sesiones"]);
    }

    // ── Números ──

    [Theory]
    [InlineData("$ 150.000", 150000)]
    [InlineData("150.000", 150000)]
    [InlineData("150,000", 150000)]
    [InlineData("1.250.000", 1250000)]
    [InlineData("150.000,50", 150000.50)]
    [InlineData("150,000.50", 150000.50)]
    [InlineData("19,5", 19.5)]
    [InlineData("12.50", 12.50)]
    [InlineData("COP 80000", 80000)]
    public void Precios_colombianos(string text, double expected) =>
        Assert.Equal((decimal)expected, ImportNumbers.Decimal(text).Value);

    [Fact]
    public void Precio_de_celda_numerica_y_errores()
    {
        Assert.Equal(150000m, ImportNumbers.Decimal(150000d).Value);
        Assert.NotNull(ImportNumbers.Decimal("ciento cincuenta").Error);
        Assert.NotNull(ImportNumbers.NonNegative("-5").Error);
        Assert.NotNull(ImportNumbers.NonNegative("0", strictlyPositive: true).Error);
    }

    [Theory]
    [InlineData("19", 19)]
    [InlineData("19%", 19)]
    [InlineData("5 %", 5)]
    [InlineData("exento", 0)]
    [InlineData("Excluido", 0)]
    [InlineData("0", 0)]
    public void Iva(string text, double expected) => Assert.Equal((decimal)expected, ImportNumbers.Iva(text).Value);

    [Fact]
    public void Iva_de_celda_porcentaje_y_tarifas_invalidas()
    {
        Assert.Equal(19m, ImportNumbers.Iva(0.19d).Value);
        Assert.Contains("0, 5 o 19", ImportNumbers.Iva("16").Error);
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("60 min", 60)]
    [InlineData("90 minutos", 90)]
    [InlineData("1 h", 60)]
    [InlineData("1 hora", 60)]
    [InlineData("1:30", 90)]
    [InlineData("1h30", 90)]
    [InlineData("1 h 30 min", 90)]
    [InlineData("1,5 h", 90)]
    public void Duraciones(string text, int minutes) => Assert.Equal(minutes, ImportNumbers.Minutes(text).Value);

    [Theory]
    [InlineData("180", 180)]
    [InlineData("180 días", 180)]
    [InlineData("6 meses", 180)]
    [InlineData("1 año", 365)]
    [InlineData("2 semanas", 14)]
    public void Vigencias(string text, int days) => Assert.Equal(days, ImportNumbers.Days(text).Value);

    [Theory]
    [InlineData("sí", true)]
    [InlineData("X", true)]
    [InlineData("1", true)]
    [InlineData("No", false)]
    [InlineData("falso", false)]
    public void Si_o_no(string text, bool expected) => Assert.Equal(expected, ImportNumbers.Boolean(text).Value);

    [Fact]
    public void Duracion_fuera_de_rango_es_error()
    {
        var field = ProcedureImportDefinition.Fields(false).First(f => f.Key == "duracion");
        Assert.Contains("máximo", ImportEngine.ParseValue(field, "9 h", ImportDates.Order.Unknown, Today).Error);
        Assert.NotNull(ImportEngine.ParseValue(field, "0", ImportDates.Order.Unknown, Today).Error);
    }

    // ── Vencimientos ──

    [Theory]
    [InlineData("15/03/2028", 2028, 3, 15)]
    [InlineData("15/03/28", 2028, 3, 15)]
    [InlineData("03/2027", 2027, 3, 31)]
    [InlineData("02/2028", 2028, 2, 29)]
    [InlineData("mar-2027", 2027, 3, 31)]
    [InlineData("marzo de 2027", 2027, 3, 31)]
    [InlineData("2027-06-30", 2027, 6, 30)]
    public void Vencimientos_aceptan_futuro_y_mes_anio(string text, int y, int m, int d)
    {
        var r = ImportDates.Parse(text, ImportDates.Order.Unknown, Today, expiry: true);
        Assert.Null(r.Error);
        Assert.Equal(new DateOnly(y, m, d), r.Value);
    }

    [Fact]
    public void Nacimiento_sigue_rechazando_el_futuro_y_el_mes_anio()
    {
        Assert.Contains("futuro", ImportDates.Parse("15/03/2028", ImportDates.Order.Unknown, Today).Error);
        Assert.NotNull(ImportDates.Parse("03/2027", ImportDates.Order.Unknown, Today).Error);
    }
}
