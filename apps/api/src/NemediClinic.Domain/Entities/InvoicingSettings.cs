using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

/// <summary>
/// Configuración de facturación electrónica de una clínica (una fila por clínica). Las credenciales
/// del proveedor van cifradas en un solo bloque (ISecretProtector): nunca salen en claro por la API.
/// El emisor lo define la cuenta del proveedor; aquí se guardan sus datos fiscales para mostrarlos y
/// para verificar al probar la conexión que la cuenta es de esta clínica (mismo NIT).
/// </summary>
public class InvoicingSettings : BaseEntity
{
    public bool Habilitada { get; set; }
    public InvoicingEnvironment Ambiente { get; set; } = InvoicingEnvironment.Pruebas;

    /// <summary>client_id, client_secret, usuario y contraseña de Factus, cifrados juntos.</summary>
    public string? CredencialesCifradas { get; set; }

    // ── Emisor ──
    public string EmisorNit { get; set; } = string.Empty;
    public string? EmisorDv { get; set; }
    public string EmisorRazonSocial { get; set; } = string.Empty;
    /// <summary>Código DIAN: 1 persona jurídica, 2 persona natural.</summary>
    public string EmisorTipoOrganizacion { get; set; } = "1";
    /// <summary>Responsabilidad fiscal DIAN (O-13, O-47, R-99-PN…).</summary>
    public string EmisorResponsabilidad { get; set; } = "R-99-PN";
    /// <summary>Municipio DANE del domicilio fiscal.</summary>
    public string? EmisorMunicipio { get; set; }
    public string? EmisorDireccion { get; set; }
    public string? EmisorTelefono { get; set; }
    public string? EmisorEmail { get; set; }

    // ── Cómo se arma cada documento ──
    /// <summary>unit_measure_code por defecto (94 = unidad).</summary>
    public string UnidadMedida { get; set; } = "94";
    /// <summary>standard_code (999 = adopción del contribuyente).</summary>
    public string CodigoEstandar { get; set; } = "999";
    /// <summary>Una línea con IVA 0 % va como excluida (is_excluded), no como gravada al 0 %.</summary>
    public bool IvaCeroExcluido { get; set; } = true;
    /// <summary>Que el proveedor mande el PDF al correo del cliente al emitir.</summary>
    public bool EnviarCorreoAlEmitir { get; set; }
    /// <summary>JSON {"Tarjeta":"49"}: cambia el código DIAN de un medio de pago para esta clínica.</summary>
    public string? CodigosMetodoPago { get; set; }

    // ── Última prueba de conexión ──
    public DateTime? UltimaPrueba { get; set; }
    public bool UltimaPruebaExitosa { get; set; }
    public string? UltimaPruebaMensaje { get; set; }

    public ICollection<InvoicingBranchSettings> Sedes { get; set; } = [];
}

/// <summary>Rango de numeración por sede: cada sede lleva su propio consecutivo de facturas y de notas crédito.</summary>
public class InvoicingBranchSettings : BaseEntity
{
    public Guid InvoicingSettingsId { get; set; }
    public Guid BranchId { get; set; }
    /// <summary>numbering_range_id del proveedor para facturas de venta.</summary>
    public int? RangoFacturaId { get; set; }
    public string? PrefijoFactura { get; set; }
    /// <summary>numbering_range_id para notas crédito.</summary>
    public int? RangoNotaCreditoId { get; set; }
    public string? PrefijoNotaCredito { get; set; }

    public InvoicingSettings InvoicingSettings { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}
