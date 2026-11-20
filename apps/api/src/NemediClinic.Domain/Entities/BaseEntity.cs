namespace NemediClinic.Domain.Entities;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    /// <summary>UTC. Lo estampa AppDbContext.SaveChangesAsync con IClock.UtcNow.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>UTC. Lo estampa AppDbContext.SaveChangesAsync con IClock.UtcNow.</summary>
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
