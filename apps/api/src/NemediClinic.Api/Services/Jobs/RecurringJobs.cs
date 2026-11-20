using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services.Jobs;

/// <summary>Paquetes Activo/Pausado cuya vigencia ya pasó → Vencido (todas las clínicas).</summary>
public class PackageExpirationJob : IScheduledJob
{
    private readonly PatientPackageService _packages;

    public PackageExpirationJob(PatientPackageService packages) => _packages = packages;

    public string Nombre => "paquetes-vencimiento";
    public string Descripcion => "Pasa a Vencido los paquetes activos o pausados cuya vigencia ya terminó.";
    public string CronSugerido => "0 2 * * *";
    public string FrecuenciaSugerida => "Diario, 02:00";

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var vencidos = await _packages.ExpireOverdueAsync();
        return vencidos == 1 ? "1 paquete pasó a Vencido." : $"{vencidos} paquetes pasaron a Vencido.";
    }
}

/// <summary>
/// Lotes: un lote que se venció anoche deja de ser stock utilizable, pero Product.StockActual solo
/// se recalcula cuando hay un movimiento. Este job lo recalcula para todo producto con lotes y
/// resume cuántos lotes con existencia están vencidos, críticos o por vencer (la base para avisar
/// por correo o WhatsApp cuando exista ese canal).
/// </summary>
public class LotAlertsJob : IScheduledJob
{
    private readonly IClock _clock;
    private readonly AppDbContext _db;

    public LotAlertsJob(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public string Nombre => "lotes-alertas";
    public string Descripcion => "Recalcula el stock sin los lotes que se vencieron y cuenta los lotes vencidos, críticos y por vencer.";
    public string CronSugerido => "15 2 * * *";
    public string FrecuenciaSugerida => "Diario, 02:15";

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var hoy = _clock.Today;
        var lotes = await _db.ProductLots.IgnoreQueryFilters().Where(l => !l.IsDeleted).ToListAsync(ct);
        var productIds = lotes.Select(l => l.ProductId).Distinct().ToList();
        var productos = await _db.Products.IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && productIds.Contains(p.Id))
            .ToListAsync(ct);

        var porProducto = lotes.ToLookup(l => l.ProductId);
        var corregidos = 0;
        foreach (var producto in productos)
        {
            // Mismo criterio que ProductLotService.RecalculateStockAsync: lo disponible sin vencer
            var utilizable = porProducto[producto.Id]
                .Where(l => l.EstadoAl(hoy) != LotStatus.Vencido)
                .Sum(l => l.CantidadDisponible);
            if (producto.StockActual == utilizable) continue;
            producto.StockActual = utilizable;
            corregidos++;
        }
        if (corregidos > 0)
            await _db.SaveChangesAsync(ct);

        var vivos = productos.Select(p => p.Id).ToHashSet();
        var conExistencia = lotes.Where(l => l.CantidadDisponible > 0 && vivos.Contains(l.ProductId)).ToList();
        int Contar(LotStatus estado) => conExistencia.Count(l => l.EstadoAl(hoy) == estado);
        var clinicas = conExistencia
            .Where(l => l.EstadoAl(hoy) is LotStatus.Vencido or LotStatus.Critico or LotStatus.PorVencer)
            .Select(l => l.TenantId).Distinct().Count();

        return $"Stock recalculado en {corregidos} de {productos.Count} productos con lotes. " +
               $"Lotes con existencia: {Contar(LotStatus.Vencido)} vencidos, {Contar(LotStatus.Critico)} críticos (menos de 30 días) " +
               $"y {Contar(LotStatus.PorVencer)} por vencer (30 a 90 días), en {clinicas} clínicas.";
    }
}

/// <summary>
/// Borrado físico diferido. DELETE /files/{id} solo marca IsDeleted; este job borra del storage los
/// adjuntos eliminados hace más de 24 h (margen para deshacer un error) y los "pendientes" que
/// ninguna entidad reclamó en 24 h (formularios abandonados), y los marca PurgedAt.
/// </summary>
public class AttachmentPurgeJob : IScheduledJob
{
    private static readonly TimeSpan Grace = TimeSpan.FromHours(24);
    private const int BatchSize = 200;
    private const int MaxBatches = 10; // tope por corrida: lo que quede sale en la siguiente

    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IClock _clock;

    public AttachmentPurgeJob(AppDbContext db, IFileStorage storage, IClock clock)
    {
        _db = db;
        _storage = storage;
        _clock = clock;
    }

    public string Nombre => "adjuntos-purga";
    public string Descripcion => "Borra del disco los adjuntos eliminados hace más de 24 h y los que ningún formulario reclamó.";
    public string CronSugerido => "0 * * * *";
    public string FrecuenciaSugerida => "Cada hora (mínimo una vez al día)";

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var limit = _clock.UtcNow - Grace;
        var total = 0;
        for (var i = 0; i < MaxBatches; i++)
        {
            var batch = await _db.Attachments.IgnoreQueryFilters()
                .Where(a => a.PurgedAt == null
                    && ((a.IsDeleted && a.UpdatedAt < limit) || (!a.IsDeleted && a.EntityId == null && a.CreatedAt < limit)))
                .OrderBy(a => a.UpdatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var attachment in batch)
            {
                await _storage.DeleteAsync(attachment.StoragePath, ct);
                await _storage.DeleteAsync(attachment.ThumbnailPath, ct);
                attachment.IsDeleted = true;
                attachment.PurgedAt = _clock.UtcNow;
            }
            // SaveChangesAsync del contexto solo estampa TenantId en Added; aquí todo es Modified.
            await _db.SaveChangesAsync(ct);
            total += batch.Count;
            if (batch.Count < BatchSize) break;
        }
        return total == 1 ? "1 archivo purgado del disco." : $"{total} archivos purgados del disco.";
    }
}
