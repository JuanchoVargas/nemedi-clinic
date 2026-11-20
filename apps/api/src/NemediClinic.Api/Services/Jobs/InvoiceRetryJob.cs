using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services.Jobs;

/// <summary>
/// Facturas y notas crédito Pendientes (el proveedor no respondió) y emitidas a las que les falta el
/// PDF o el XML. Reenvía con la misma referencia: si el proveedor ya las había registrado, devuelve
/// el documento validado, así que correrlo dos veces no duplica nada. Un scope por clínica, con su
/// tenant fijado, para que InvoiceService trabaje con el filtro global como en una petición.
/// </summary>
public class InvoiceRetryJob : IScheduledJob
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopes;

    public InvoiceRetryJob(AppDbContext db, IServiceScopeFactory scopes)
    {
        _db = db;
        _scopes = scopes;
    }

    public string Nombre => "facturas-reintento";
    public string Descripcion => "Reenvía al proveedor las facturas y notas crédito pendientes y descarga el PDF/XML que faltó.";
    public string CronSugerido => "*/15 * * * *";
    public string FrecuenciaSugerida => "Cada 15 minutos";

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var tenants = await _db.ElectronicInvoices.IgnoreQueryFilters()
            .Where(f => !f.IsDeleted && (f.Estado == ElectronicInvoiceStatus.Pendiente
                || (f.Estado == ElectronicInvoiceStatus.Emitida && (f.PdfId == null || f.XmlId == null))))
            .Select(f => f.TenantId).Distinct().ToListAsync(ct);

        int revisadas = 0, emitidas = 0, rechazadas = 0;
        foreach (var tenantId in tenants)
        {
            using var scope = _scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantProvider>().SetTenant(tenantId);
            var (r, e, x) = await scope.ServiceProvider.GetRequiredService<InvoiceService>().RetryPendingAsync(ct);
            revisadas += r;
            emitidas += e;
            rechazadas += x;
        }
        return revisadas == 0
            ? "No hay documentos pendientes."
            : $"{revisadas} documento(s) revisado(s) en {tenants.Count} clínica(s): {emitidas} emitido(s), {rechazadas} rechazado(s).";
    }
}
