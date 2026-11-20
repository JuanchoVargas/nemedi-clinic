namespace NemediClinic.Domain.Entities;

public class Branch : BaseEntity
{
    public string Nombre { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;

    public Tenant Tenant { get; set; } = null!;
    public ICollection<User> Users { get; set; } = [];
    /// <summary>Horario de atención. Sin filas = sin restricción; con filas, un día sin fila está cerrado.</summary>
    public ICollection<BranchSchedule> Horarios { get; set; } = [];
}
