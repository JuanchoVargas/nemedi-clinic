namespace NemediClinic.Domain.Entities;

public class Package : BaseEntity
{
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal PrecioTotal { get; set; }
    public int SesionesTotales { get; set; }
    public int VigenciaDias { get; set; }
    public int DiasAlertaVencimiento { get; set; } = 15;
    public bool Activo { get; set; } = true;
    /// <summary>IVA propio del paquete (%). Null = el IVA de servicios de la clínica.</summary>
    public decimal? IvaPorcentaje { get; set; }

    public ICollection<PackageProcedure> PackageProcedures { get; set; } = [];
}
