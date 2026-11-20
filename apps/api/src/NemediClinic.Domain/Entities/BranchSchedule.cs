namespace NemediClinic.Domain.Entities;

/// <summary>Franja de atención de una sede para un día de la semana (0 = domingo … 6 = sábado).</summary>
public class BranchSchedule : BaseEntity
{
    public Guid BranchId { get; set; }
    public int DiaSemana { get; set; }
    public TimeOnly HoraApertura { get; set; }
    public TimeOnly HoraCierre { get; set; }

    public Branch Branch { get; set; } = null!;
}
