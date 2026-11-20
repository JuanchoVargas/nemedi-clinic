using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

public class Procedure : BaseEntity
{
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal PrecioBase { get; set; }
    public int DuracionMinutos { get; set; }
    public string AreaCorporal { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public Guid? ImagenId { get; set; }
    /// <summary>Si es true, una cita de este procedimiento no pasa a EnCurso sin un consentimiento firmado vigente.</summary>
    public bool RequiereConsentimiento { get; set; }
    /// <summary>
    /// IVA propio del procedimiento (%). Null = el IVA de servicios de la clínica (0 % por defecto:
    /// los servicios de salud pueden estar excluidos; los estéticos, según el caso, no).
    /// </summary>
    public decimal? IvaPorcentaje { get; set; }

    // ── RIPS ──
    /// <summary>true: se reporta en RIPS. En una clínica IPS exige CUPS, finalidad y tipo de servicio.</summary>
    public bool RequiereRips { get; set; }
    /// <summary>Código CUPS vigente del procedimiento o la consulta.</summary>
    public string? CodigoCups { get; set; }
    public FinalidadConsulta? FinalidadConsulta { get; set; }
    public TipoServicioRips? TipoServicio { get; set; }

    public ICollection<PackageProcedure> PackageProcedures { get; set; } = [];
}
