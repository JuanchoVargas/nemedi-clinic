using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Users;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services;

/// <summary>
/// Edición y baja de cuentas de la clínica (PUT/DELETE /api/v1/users/{id}). Las reglas de quién
/// puede hacer qué están en <see cref="UserRules"/>; aquí van la carga, la validación y el guardado.
/// El filtro global de tenant deja fuera a los usuarios de otras clínicas (404).
/// </summary>
public class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task UpdateAsync(Guid id, UpdateUserRequest request, Guid actorId, UserRole actorRol, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ApiException.NotFound("Usuario no encontrado.");

        // Solo si la edición puede quitar un dueño (el usuario es SuperAdmin y se desactiva o cambia de rol)
        // se toma el lock de los dueños y se vuelve a leer al usuario bajo él: así la decisión usa lo que
        // hay bajo el lock y las demás ediciones no se serializan.
        bool QuitaDueno() => EsDuenoActivo(user)
            && (request.IsActive == false || (request.Rol.HasValue && request.Rol.Value != UserRole.SuperAdmin));
        await using var tx = QuitaDueno() ? await _db.Database.BeginTransactionAsync(ct) : null;
        var duenos = tx is null ? 0 : await LockActiveOwnersAsync(user, ct);

        if (request.Rol.HasValue && !Enum.IsDefined(request.Rol.Value))
            throw ApiException.BadRequest("Rol inválido.");

        if (UserRules.CanUpdate(actorId, actorRol, user.Id, user.Rol, request.Rol, request.IsActive) is { } prohibido)
            throw new ApiException(StatusCodes.Status403Forbidden, prohibido);

        // Desactivar o degradar a un dueño activo: tiene que quedar otro
        if (tx is not null && QuitaDueno() && duenos <= 1)
            throw ApiException.Conflict(SinDueno);

        var email = request.Email?.Trim();
        if (email is not null && email != user.Email)
        {
            // En todo el sistema, no solo en esta clínica: el login busca el correo en todas
            if (await AccountEmails.InUseAsync(_db, email, id, ct))
                throw ApiException.Conflict(AccountEmails.EnUso);
            user.Email = email;
        }

        if (request.Nombre is not null) user.Nombre = request.Nombre;
        if (request.Apellido is not null) user.Apellido = request.Apellido;
        if (request.Rol.HasValue) user.Rol = request.Rol.Value;
        // Solo si cambia: la web manda la sede de siempre al guardar, y una sede que luego se borró no
        // puede impedir editar al usuario
        if (request.BranchId.HasValue && request.BranchId != user.BranchId)
        {
            // Filtro global de tenant: una sede de otra clínica no aparece (como en auth/register)
            if (!await _db.Branches.AnyAsync(b => b.Id == request.BranchId.Value, ct))
                throw ApiException.BadRequest("La sede especificada no existe.");
            user.BranchId = request.BranchId;
        }
        if (request.IsActive.HasValue)
        {
            user.IsActive = request.IsActive.Value;
            if (!user.IsActive) RevokeSessions(user);
        }

        await _db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(Guid id, Guid actorId, UserRole actorRol, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ApiException.NotFound("Usuario no encontrado.");
        await using var tx = EsDuenoActivo(user) ? await _db.Database.BeginTransactionAsync(ct) : null;
        var duenos = tx is null ? 0 : await LockActiveOwnersAsync(user, ct);

        if (UserRules.CanDelete(actorId, actorRol, user.Id, user.Rol) is { } prohibido)
            throw new ApiException(StatusCodes.Status403Forbidden, prohibido);

        if (tx is not null && EsDuenoActivo(user) && duenos <= 1)
            throw ApiException.Conflict(SinDueno);

        user.IsDeleted = true;
        user.IsActive = false;
        RevokeSessions(user);
        await _db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
    }

    public const string SinDueno = "La clínica no puede quedarse sin un dueño activo: asigna antes a otro SuperAdmin.";

    private static bool EsDuenoActivo(User user) => user.Rol == UserRole.SuperAdmin && user.IsActive && !user.IsDeleted;

    /// <summary>
    /// Nunca sin dueño: cuenta los SuperAdmin activos de la clínica con UPDLOCK + HOLDLOCK (dentro de la
    /// transacción eso serializa las operaciones que pueden quitar un dueño: dos dueños que se desactivan
    /// el uno al otro esperan uno al otro) y vuelve a leer al usuario bajo el lock, por si otra petición lo
    /// cambió entre la primera lectura y el lock.
    /// </summary>
    private async Task<int> LockActiveOwnersAsync(User user, CancellationToken ct)
    {
        var tenantId = _db.CurrentTenantId;
        var duenos = await _db.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*) AS Value FROM Users WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId = {tenantId} AND Rol = {nameof(UserRole.SuperAdmin)} AND IsActive = 1 AND IsDeleted = 0
                """)
            .SingleAsync(ct);
        await _db.Entry(user).ReloadAsync(ct);
        if (user.IsDeleted)
            throw ApiException.NotFound("Usuario no encontrado.");
        return duenos;
    }

    /// <summary>
    /// Sin refresh token, /auth/refresh responde 401. El JWT ya emitido deja de valer en la siguiente
    /// petición por SessionValidator (cuenta desactivada, borrada o con otro rol); esto evita que la
    /// sesión se renueve si la cuenta se reactiva.
    /// </summary>
    private static void RevokeSessions(User user)
    {
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
    }
}
