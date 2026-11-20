using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Invoicing;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Invoicing;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services;

/// <summary>
/// Configuración de facturación electrónica de la clínica (Configuración → Facturación, solo el dueño).
///
///   · Las credenciales del proveedor se guardan cifradas en un solo bloque (ISecretProtector) y
///     nunca vuelven por la API: el DTO solo dice si están y muestra una pista del usuario.
///   · "Probar conexión" autentica con las credenciales del formulario (o las guardadas), trae el
///     emisor y los rangos de numeración y avisa si el NIT de la cuenta no es el de la clínica.
///   · Cada sede elige su rango de facturas y el de notas crédito (su propio consecutivo).
/// </summary>
public class InvoicingSettingsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IElectronicInvoiceProvider _provider;
    private readonly FactusCatalogService _catalogs;
    private readonly IClock _clock;

    public InvoicingSettingsService(AppDbContext db, ISecretProtector protector, IElectronicInvoiceProvider provider,
        FactusCatalogService catalogs, IClock clock)
    {
        _db = db;
        _protector = protector;
        _provider = provider;
        _catalogs = catalogs;
        _clock = clock;
    }

    /// <summary>Lo que se cifra: las cuatro credenciales juntas.</summary>
    public sealed record Credentials(string ClientId, string ClientSecret, string Usuario, string Contrasena);

    public async Task<InvoicingSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var settings = await LoadAsync(ct);
        var sedes = await _db.Branches.AsNoTracking().OrderBy(b => b.CreatedAt).Select(b => new { b.Id, b.Nombre }).ToListAsync(ct);
        if (settings is null)
            return new InvoicingSettingsDto
            {
                Proveedor = _provider.Nombre,
                Sedes = sedes.Select(b => new InvoicingBranchDto { BranchId = b.Id, BranchNombre = b.Nombre }).ToList(),
            };

        var cred = TryDecrypt(settings);
        return new InvoicingSettingsDto
        {
            Configurada = true,
            Habilitada = settings.Habilitada,
            Ambiente = settings.Ambiente.ToString(),
            Proveedor = _provider.Nombre,
            TieneCredenciales = cred is not null,
            CredencialesIlegibles = cred is null && !string.IsNullOrEmpty(settings.CredencialesCifradas),
            UsuarioPista = cred is null ? null : Hint(cred.Usuario),
            ClientIdPista = cred is null ? null : Hint(cred.ClientId),
            EmisorNit = settings.EmisorNit,
            EmisorDv = settings.EmisorDv,
            EmisorRazonSocial = settings.EmisorRazonSocial,
            EmisorTipoOrganizacion = settings.EmisorTipoOrganizacion,
            EmisorResponsabilidad = settings.EmisorResponsabilidad,
            EmisorMunicipio = settings.EmisorMunicipio,
            EmisorDireccion = settings.EmisorDireccion,
            EmisorTelefono = settings.EmisorTelefono,
            EmisorEmail = settings.EmisorEmail,
            UnidadMedida = settings.UnidadMedida,
            CodigoEstandar = settings.CodigoEstandar,
            IvaCeroExcluido = settings.IvaCeroExcluido,
            EnviarCorreoAlEmitir = settings.EnviarCorreoAlEmitir,
            CodigosMetodoPago = MethodCodes(settings),
            UltimaPrueba = settings.UltimaPrueba,
            UltimaPruebaExitosa = settings.UltimaPruebaExitosa,
            UltimaPruebaMensaje = settings.UltimaPruebaMensaje,
            Sedes = sedes.Select(b =>
            {
                var s = settings.Sedes.FirstOrDefault(x => x.BranchId == b.Id);
                return new InvoicingBranchDto
                {
                    BranchId = b.Id,
                    BranchNombre = b.Nombre,
                    RangoFacturaId = s?.RangoFacturaId,
                    PrefijoFactura = s?.PrefijoFactura,
                    RangoNotaCreditoId = s?.RangoNotaCreditoId,
                    PrefijoNotaCredito = s?.PrefijoNotaCredito,
                };
            }).ToList(),
        };
    }

    public async Task<InvoicingSettingsDto> UpdateAsync(UpdateInvoicingSettingsRequest r, CancellationToken ct = default)
    {
        var settings = await LoadAsync(ct, tracking: true);
        if (settings is null)
        {
            settings = new InvoicingSettings();
            _db.InvoicingSettings.Add(settings);
        }

        if (!Enum.TryParse<InvoicingEnvironment>(r.Ambiente, true, out var ambiente))
            throw ApiException.BadRequest("Ambiente inválido: Pruebas o Produccion.");

        var nuevas = NewCredentials(r.ClientId, r.ClientSecret, r.Usuario, r.Contrasena);
        if (nuevas is not null) settings.CredencialesCifradas = ProtectCredentials(_protector, nuevas);

        var nit = Digits(r.EmisorNit);
        if (r.Habilitada)
        {
            if (TryDecrypt(settings) is null)
                throw ApiException.BadRequest("Para habilitar la facturación ingresa las credenciales del proveedor.");
            if (nit.Length < 5) throw ApiException.BadRequest("Indica el NIT del emisor (sin dígito de verificación).");
            if (string.IsNullOrWhiteSpace(r.EmisorRazonSocial)) throw ApiException.BadRequest("Indica la razón social del emisor.");
        }

        var cat = _catalogs.Catalogs;
        Require(cat.LegalOrganizations, r.EmisorTipoOrganizacion, "Tipo de organización");
        Require(cat.Responsibilities, r.EmisorResponsabilidad, "Responsabilidad fiscal");
        Require(cat.UnitMeasures, r.UnidadMedida, "Unidad de medida");
        Require(cat.StandardCodes, r.CodigoEstandar, "Código estándar");
        if (!string.IsNullOrWhiteSpace(r.EmisorMunicipio)) Require(cat.Municipalities, r.EmisorMunicipio.Trim(), "Municipio");
        var codigos = (r.CodigosMetodoPago ?? [])
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim());
        foreach (var (metodo, codigo) in codigos)
        {
            if (!Enum.TryParse<PaymentMethod>(metodo, out _)) throw ApiException.BadRequest($"Medio de pago desconocido: {metodo}.");
            Require(cat.PaymentMethods, codigo, $"Código DIAN del medio {metodo}");
        }
        var dv = Digits(r.EmisorDv ?? string.Empty);

        settings.Habilitada = r.Habilitada;
        settings.Ambiente = ambiente;
        settings.EmisorNit = nit;
        settings.EmisorDv = dv.Length == 1 ? dv : null;
        settings.EmisorRazonSocial = r.EmisorRazonSocial.Trim();
        settings.EmisorTipoOrganizacion = r.EmisorTipoOrganizacion;
        settings.EmisorResponsabilidad = r.EmisorResponsabilidad;
        settings.EmisorMunicipio = Clean(r.EmisorMunicipio);
        settings.EmisorDireccion = Clean(r.EmisorDireccion);
        settings.EmisorTelefono = Clean(r.EmisorTelefono);
        settings.EmisorEmail = Clean(r.EmisorEmail);
        settings.UnidadMedida = r.UnidadMedida;
        settings.CodigoEstandar = r.CodigoEstandar;
        settings.IvaCeroExcluido = r.IvaCeroExcluido;
        settings.EnviarCorreoAlEmitir = r.EnviarCorreoAlEmitir;
        settings.CodigosMetodoPago = codigos.Count == 0 ? null : JsonSerializer.Serialize(codigos);

        var branchIds = await _db.Branches.Select(b => b.Id).ToListAsync(ct);
        foreach (var sede in r.Sedes)
        {
            if (!branchIds.Contains(sede.BranchId)) throw ApiException.BadRequest("Sede no encontrada.");
            var row = settings.Sedes.FirstOrDefault(s => s.BranchId == sede.BranchId);
            if (row is null)
            {
                row = new InvoicingBranchSettings { BranchId = sede.BranchId };
                settings.Sedes.Add(row);
            }
            row.RangoFacturaId = sede.RangoFacturaId;
            row.PrefijoFactura = Clean(sede.PrefijoFactura);
            row.RangoNotaCreditoId = sede.RangoNotaCreditoId;
            row.PrefijoNotaCredito = Clean(sede.PrefijoNotaCredito);
        }
        await _db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<TestConnectionResultDto> TestAsync(TestConnectionRequest r, CancellationToken ct = default)
    {
        var settings = await LoadAsync(ct, tracking: true);
        var nuevasCred = NewCredentials(r.ClientId, r.ClientSecret, r.Usuario, r.Contrasena);
        if (nuevasCred is null && settings is { CredencialesCifradas: { Length: > 0 } } && TryDecrypt(settings) is null)
            throw ApiException.BadRequest("Las credenciales guardadas no se pueden leer: se cifraron con otra clave (cambió Secrets:EncryptionKey o Jwt:Secret). Vuelve a escribirlas.");
        var cred = nuevasCred
            ?? (settings is null ? null : TryDecrypt(settings))
            ?? throw ApiException.BadRequest("Ingresa las credenciales del proveedor para probar la conexión.");
        var produccion = r.Ambiente is not null
            ? string.Equals(r.Ambiente, nameof(InvoicingEnvironment.Produccion), StringComparison.OrdinalIgnoreCase)
            : settings?.Ambiente == InvoicingEnvironment.Produccion;

        var conexion = Connection(settings, cred, produccion);
        var prueba = await _provider.ProbarConexionAsync(conexion, ct);
        var nitConfigurado = settings?.EmisorNit;
        var nitNoCoincide = prueba.Exitosa && !string.IsNullOrEmpty(nitConfigurado) && !string.IsNullOrEmpty(prueba.EmisorNit)
            && Digits(prueba.EmisorNit) != nitConfigurado;
        var mensaje = prueba.Exitosa
            ? nitNoCoincide
                ? $"Conexión exitosa, pero la cuenta es del NIT {prueba.EmisorNit} y la clínica tiene configurado {nitConfigurado}."
                : $"Conexión exitosa con {_provider.Nombre}{(prueba.EmisorNombre is null ? "" : $" ({prueba.EmisorNombre})")}."
            : prueba.Mensaje;

        if (settings is not null)
        {
            settings.UltimaPrueba = _clock.Now;
            settings.UltimaPruebaExitosa = prueba.Exitosa && !nitNoCoincide;
            settings.UltimaPruebaMensaje = mensaje.Length > 1000 ? mensaje[..1000] : mensaje;
            await _db.SaveChangesAsync(ct);
        }

        return new TestConnectionResultDto
        {
            Exitosa = prueba.Exitosa,
            Mensaje = mensaje,
            EmisorNit = prueba.EmisorNit,
            EmisorNombre = prueba.EmisorNombre,
            NitNoCoincide = nitNoCoincide,
            Rangos = prueba.Rangos.Select(x => new NumberingRangeDto
            {
                Id = x.Id, Documento = x.Documento, Prefijo = x.Prefijo, Desde = x.Desde, Hasta = x.Hasta,
                Actual = x.Actual, Resolucion = x.Resolucion, Vigente = x.Vigente,
            }).ToList(),
        };
    }

    public InvoicingCatalogsDto Catalogs()
    {
        var c = _catalogs.Catalogs;
        static List<CatalogItemDto> Map(IEnumerable<CatalogEntry> e) => e.Select(x => new CatalogItemDto { Codigo = x.Code, Nombre = x.Name }).ToList();
        return new InvoicingCatalogsDto
        {
            Fuente = c.Source,
            Generado = c.GeneratedAt,
            TiposDocumento = Map(c.IdentificationDocuments),
            TiposOrganizacion = Map(c.LegalOrganizations),
            Responsabilidades = Map(c.Responsibilities),
            MediosPago = Map(c.PaymentMethods),
            ConceptosCorreccion = Map(c.CreditNoteCorrections),
            CodigosEstandar = Map(c.StandardCodes),
            Tributos = Map(c.CustomerTributes),
            MediosPagoPorDefecto = new Dictionary<string, string>(_catalogs.Equivalences.MetodoPago),
        };
    }

    /// <summary>Municipios o unidades de medida: largas, se buscan por código o nombre.</summary>
    public List<CatalogItemDto> SearchCatalog(string tabla, string? search, int max = 30)
    {
        var entries = tabla switch
        {
            "municipios" => _catalogs.Catalogs.Municipalities,
            "unidades" => _catalogs.Catalogs.UnitMeasures,
            _ => throw ApiException.NotFound("Catálogo no encontrado."),
        };
        var q = search?.Trim();
        IEnumerable<CatalogEntry> result = entries;
        if (!string.IsNullOrEmpty(q))
        {
            var normal = Normalize(q);
            result = entries.Where(e => e.Code.StartsWith(q, StringComparison.OrdinalIgnoreCase) || Normalize(e.Name).Contains(normal));
        }
        return result.Take(max).Select(x => new CatalogItemDto { Codigo = x.Code, Nombre = x.Name }).ToList();
    }

    // ── Para InvoiceService ─────────────────────────────────────────

    /// <summary>Configuración utilizable o 501 (la clínica no factura electrónicamente todavía).</summary>
    public async Task<(InvoicingSettings Settings, InvoicingConnection Conexion)> RequireActiveAsync(CancellationToken ct)
    {
        var settings = await LoadAsync(ct);
        var cred = settings is { Habilitada: true } ? TryDecrypt(settings) : null;
        if (settings is null || cred is null)
            throw new ApiException(StatusCodes.Status501NotImplemented,
                "La facturación electrónica aún no está configurada para esta clínica.");
        return (settings, Connection(settings, cred, settings.Ambiente == InvoicingEnvironment.Produccion));
    }

    public async Task<InvoicingSettings?> LoadAsync(CancellationToken ct, bool tracking = false)
    {
        var q = _db.InvoicingSettings.Include(s => s.Sedes).AsQueryable();
        if (!tracking) q = q.AsNoTracking();
        return await q.OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);
    }

    private InvoicingConnection Connection(InvoicingSettings? s, Credentials cred, bool produccion) => new()
    {
        TenantId = _db.CurrentTenantId,
        Produccion = produccion,
        ClientId = cred.ClientId,
        ClientSecret = cred.ClientSecret,
        Usuario = cred.Usuario,
        Contrasena = cred.Contrasena,
        UnidadMedida = s?.UnidadMedida ?? _catalogs.Equivalences.PorDefecto.UnidadMedida,
        CodigoEstandar = s?.CodigoEstandar ?? _catalogs.Equivalences.PorDefecto.CodigoEstandar,
        IvaCeroExcluido = s?.IvaCeroExcluido ?? true,
        CodigosMetodoPago = s is null ? [] : MethodCodes(s),
    };

    private Credentials? TryDecrypt(InvoicingSettings s) => UnprotectCredentials(_protector, s.CredencialesCifradas);

    /// <summary>Las cuatro credenciales como un solo bloque cifrado (JSON → AES-GCM). Sin recortar nada más.</summary>
    public static string ProtectCredentials(ISecretProtector protector, Credentials credentials) =>
        protector.Protect(JsonSerializer.Serialize(credentials, Json));

    /// <summary>
    /// null si no hay o si no se pueden leer. AES-GCM es autenticado: con otra clave (cambió
    /// Secrets:EncryptionKey, o Jwt:Secret cuando no hay una propia) o un texto alterado, falla; nunca
    /// devuelve credenciales corruptas. Hay que volver a escribirlas.
    /// </summary>
    public static Credentials? UnprotectCredentials(ISecretProtector protector, string? cifradas)
    {
        if (string.IsNullOrEmpty(cifradas)) return null;
        try
        {
            return JsonSerializer.Deserialize<Credentials>(protector.Unprotect(cifradas), Json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Credentials? NewCredentials(string? clientId, string? clientSecret, string? usuario, string? contrasena)
    {
        var valores = new[] { clientId, clientSecret, usuario, contrasena };
        if (valores.All(string.IsNullOrWhiteSpace)) return null;
        if (valores.Any(string.IsNullOrWhiteSpace))
            throw ApiException.BadRequest("Para cambiar las credenciales ingresa las cuatro: client_id, client_secret, usuario y contraseña.");
        return new Credentials(clientId!.Trim(), clientSecret!.Trim(), usuario!.Trim(), contrasena!);
    }

    private static Dictionary<string, string> MethodCodes(InvoicingSettings s) =>
        string.IsNullOrEmpty(s.CodigosMetodoPago) ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(s.CodigosMetodoPago) ?? [];

    private static void Require(IReadOnlyList<CatalogEntry> table, string? code, string campo)
    {
        if (code is null || table.All(e => e.Code != code))
            throw ApiException.BadRequest($"{campo}: el código {code} no está en las tablas DIAN.");
    }

    private static string Hint(string s) => s.Length <= 4 ? "••••" : $"{s[..2]}•••{s[^2..]}";

    private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Normalize(string s) =>
        new(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant).ToArray());
}
