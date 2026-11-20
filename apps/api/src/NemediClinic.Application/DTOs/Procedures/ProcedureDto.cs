namespace NemediClinic.Application.DTOs.Procedures;

public class ProcedureDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal PrecioBase { get; set; }
    public int DuracionMinutos { get; set; }
    public string AreaCorporal { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public Guid? ImagenId { get; set; }
    public bool RequiereConsentimiento { get; set; }
    /// <summary>IVA propio; null = el de servicios de la clínica.</summary>
    public decimal? IvaPorcentaje { get; set; }
    /// <summary>Se reporta en RIPS (en una clínica IPS exige CUPS, finalidad y tipo de servicio).</summary>
    public bool RequiereRips { get; set; }
    public string? CodigoCups { get; set; }
    public string? FinalidadConsulta { get; set; }
    public string? TipoServicio { get; set; }
    public DateTime CreatedAt { get; set; }
}
