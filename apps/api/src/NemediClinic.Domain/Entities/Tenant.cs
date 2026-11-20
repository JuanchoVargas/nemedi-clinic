using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

public class Tenant : BaseEntity
{
    public string Nombre { get; set; } = string.Empty;
    public string NIT { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Logo { get; set; }
    /// <summary>Logo subido (Attachment Kind=Logo). La interfaz de la clínica lo muestra en lugar del logo del canal.</summary>
    public Guid? LogoId { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Impuestos: IVA por defecto de lo que se vende (cada ítem del catálogo puede fijar el suyo) ──
    /// <summary>IVA de los productos (%). 19 por defecto.</summary>
    public decimal IvaProductos { get; set; } = 19m;
    /// <summary>IVA de procedimientos y paquetes (%). 0 por defecto: servicios de salud excluidos.</summary>
    public decimal IvaServicios { get; set; }

    // ── Datos y políticas (Configuración de la clínica) ──
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    /// <summary>Horas mínimas entre ahora y el inicio de una cita nueva o reagendada. 0 = sin mínimo.</summary>
    public int AnticipacionMinimaHoras { get; set; }
    /// <summary>false apaga el bloqueo de "Iniciar" por falta de consentimiento, aunque el procedimiento lo pida.</summary>
    public bool ExigirConsentimiento { get; set; } = true;

    // ── Nivel de plataforma ────────────────────────────────────────
    public Guid ChannelId { get; set; }
    public TenantPlan Plan { get; set; } = TenantPlan.Basico;
    public int SedesAdicionales { get; set; }
    /// <summary>Prestador de servicios de salud habilitado (IPS): activa los datos y validaciones de RIPS.</summary>
    public bool EsIps { get; set; }
    /// <summary>Código de habilitación en el REPS (12 dígitos: prestador + sede). Obligatorio si EsIps.</summary>
    public string? CodigoHabilitacionReps { get; set; }
    public TenantEstado Estado { get; set; } = TenantEstado.Activo;
    public DateTime? FechaActivacion { get; set; }

    /// <summary>Si tiene valor, reemplaza Channel.PorcentajeCanal para este tenant (un acuerdo particular con esa clínica).</summary>
    public decimal? PorcentajeCanalOverride { get; set; }

    public Channel Channel { get; set; } = null!;
    public ICollection<Branch> Branches { get; set; } = [];
    public ICollection<User> Users { get; set; } = [];
}
