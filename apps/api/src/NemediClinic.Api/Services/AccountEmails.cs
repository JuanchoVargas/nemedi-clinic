using Microsoft.EntityFrameworkCore;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services;

/// <summary>
/// Un correo identifica una sola cuenta en todo el sistema: el login busca por correo en todas las
/// clínicas y en los administradores de plataforma, así que una clínica que repite el correo de
/// otra (o el del admin de plataforma) le desvía o le bloquea el ingreso. La base solo tiene un
/// índice único por clínica (TenantId + Email); esta es la regla entre clínicas. La comparación la
/// hace SQL Server (sin distinguir mayúsculas). Los usuarios borrados no cuentan.
/// </summary>
public static class AccountEmails
{
    public const string EnUso = "El correo ya está en uso.";

    public static async Task<bool> InUseAsync(AppDbContext db, string email, Guid? exceptUserId, CancellationToken ct)
    {
        var correo = email.Trim();
        return await db.Users.IgnoreQueryFilters()
                   .AnyAsync(u => u.Email == correo && !u.IsDeleted && (exceptUserId == null || u.Id != exceptUserId), ct)
               || await db.PlatformAdmins.AnyAsync(a => a.Email == correo, ct);
    }
}
