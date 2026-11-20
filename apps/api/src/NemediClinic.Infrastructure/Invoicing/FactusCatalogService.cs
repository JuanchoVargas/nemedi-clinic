using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NemediClinic.Infrastructure.Invoicing;

/// <summary>
/// Catálogos DIAN y equivalencias, cargados una vez y cacheados (singleton).
///
///   · factus-catalogs.json — las tablas de referencia de Factus (documentos, medios de pago,
///     tributos, conceptos de corrección, municipios, unidades…). La API v2 de Factus no las expone:
///     las baja de su documentación oficial apps/api/tools/factus-catalogs.mjs.
///   · invoicing-equivalences.json — qué código DIAN corresponde a cada dato de NemediClinic
///     (tipo de documento RIPS, medio de pago) y los valores por defecto.
///
/// Al cargar verifica que cada código de las equivalencias exista en su tabla: un catálogo
/// regenerado que deje de tener un código rompe aquí, no en una factura rechazada.
/// </summary>
public sealed class FactusCatalogService
{
    private readonly Lazy<Data> _data = new(Load);

    public FactusCatalogs Catalogs => _data.Value.Catalogs;
    public FactusEquivalences Equivalences => _data.Value.Equivalences;

    public bool Exists(IReadOnlyList<CatalogEntry> table, string? code) =>
        code is not null && table.Any(e => e.Code == code);

    public string? Name(IReadOnlyList<CatalogEntry> table, string? code) =>
        table.FirstOrDefault(e => e.Code == code)?.Name;

    private sealed record Data(FactusCatalogs Catalogs, FactusEquivalences Equivalences);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static Data Load()
    {
        var catalogs = Read<FactusCatalogs>("factus-catalogs.json");
        var eq = Read<FactusEquivalences>("invoicing-equivalences.json");

        var missing = new List<string>();
        void Check(IReadOnlyList<CatalogEntry> table, string? code, string what)
        {
            if (code is null || table.All(e => e.Code != code)) missing.Add($"{what} = {code}");
        }
        foreach (var (k, v) in eq.TipoDocumento) Check(catalogs.IdentificationDocuments, v, $"tipoDocumento.{k}");
        foreach (var (k, v) in eq.MetodoPago) Check(catalogs.PaymentMethods, v, $"metodoPago.{k}");
        Check(catalogs.IdentificationDocuments, eq.ConsumidorFinal.TipoDocumento, "consumidorFinal.tipoDocumento");
        var d = eq.PorDefecto;
        Check(catalogs.UnitMeasures, d.UnidadMedida, "porDefecto.unidadMedida");
        Check(catalogs.StandardCodes, d.CodigoEstandar, "porDefecto.codigoEstandar");
        Check(catalogs.Taxes, d.ImpuestoIva, "porDefecto.impuestoIva");
        Check(catalogs.CustomerTributes, d.TributoCliente, "porDefecto.tributoCliente");
        Check(catalogs.Responsibilities, d.Responsabilidad, "porDefecto.responsabilidad");
        Check(catalogs.LegalOrganizations, d.PersonaNatural, "porDefecto.personaNatural");
        Check(catalogs.LegalOrganizations, d.PersonaJuridica, "porDefecto.personaJuridica");
        Check(catalogs.PaymentForms, d.FormaPagoContado, "porDefecto.formaPagoContado");
        Check(catalogs.PaymentForms, d.FormaPagoCredito, "porDefecto.formaPagoCredito");
        Check(catalogs.CreditNoteCorrections, d.CorreccionAnulacion, "porDefecto.correccionAnulacion");
        Check(catalogs.CreditNoteCorrections, d.CorreccionDevolucion, "porDefecto.correccionDevolucion");
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "invoicing-equivalences.json usa códigos que no están en factus-catalogs.json: " + string.Join(", ", missing));
        return new Data(catalogs, eq);
    }

    private static T Read<T>(string file)
    {
        var assembly = typeof(FactusCatalogService).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + file, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<T>(stream, Json)
            ?? throw new InvalidOperationException($"{file} está vacío.");
    }
}

public sealed record CatalogEntry(string Code, string Name);

public sealed class FactusCatalogs
{
    public string Source { get; init; } = string.Empty;
    public string GeneratedAt { get; init; } = string.Empty;
    public List<CatalogEntry> IdentificationDocuments { get; init; } = [];
    public List<CatalogEntry> LegalOrganizations { get; init; } = [];
    public List<CatalogEntry> CustomerTributes { get; init; } = [];
    public List<CatalogEntry> Taxes { get; init; } = [];
    public List<CatalogEntry> PaymentMethods { get; init; } = [];
    public List<CatalogEntry> PaymentForms { get; init; } = [];
    public List<CatalogEntry> CreditNoteCorrections { get; init; } = [];
    public List<CatalogEntry> StandardCodes { get; init; } = [];
    public List<CatalogEntry> Responsibilities { get; init; } = [];
    public List<CatalogEntry> Municipalities { get; init; } = [];
    public List<CatalogEntry> UnitMeasures { get; init; } = [];
}

public sealed class FactusEquivalences
{
    public Dictionary<string, string> TipoDocumento { get; init; } = [];
    public Dictionary<string, string> MetodoPago { get; init; } = [];
    public ConsumidorFinalEq ConsumidorFinal { get; init; } = new();
    public DefaultsEq PorDefecto { get; init; } = new();

    public sealed class ConsumidorFinalEq
    {
        public string TipoDocumento { get; init; } = string.Empty;
        public string Documento { get; init; } = string.Empty;
        public string Nombre { get; init; } = string.Empty;
    }

    public sealed class DefaultsEq
    {
        public string UnidadMedida { get; init; } = string.Empty;
        public string CodigoEstandar { get; init; } = string.Empty;
        public string ImpuestoIva { get; init; } = string.Empty;
        public string TributoCliente { get; init; } = string.Empty;
        public string Responsabilidad { get; init; } = string.Empty;
        public string PersonaNatural { get; init; } = string.Empty;
        public string PersonaJuridica { get; init; } = string.Empty;
        public string FormaPagoContado { get; init; } = string.Empty;
        public string FormaPagoCredito { get; init; } = string.Empty;
        public string CorreccionAnulacion { get; init; } = string.Empty;
        public string CorreccionDevolucion { get; init; } = string.Empty;
        public string Pais { get; init; } = string.Empty;
    }
}
