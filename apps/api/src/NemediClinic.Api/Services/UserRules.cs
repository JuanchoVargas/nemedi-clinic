using NemediClinic.Domain.Enums;

namespace NemediClinic.Api.Services;

/// <summary>
/// Quién puede cambiar qué de una cuenta de la clínica. Puro (sin base), para poder probarlo solo.
///   · Nadie asigna un rol mayor que el suyo ni cambia el rol de alguien de su nivel o superior:
///     el dueño (SuperAdmin) cambia cualquier rol; recepción (Admin) ninguno — igual que en el alta,
///     donde solo crea esteticistas.
///   · Solo el dueño (SuperAdmin) gestiona cuentas de recepción o administración: recepción (Admin)
///     edita, desactiva, borra y restablece la clave solo de esteticistas, además de sus propios datos
///     (regla de negocio de la clínica).
///   · Nadie cambia su propio rol, se desactiva ni se elimina: la clínica no se queda sin dueño.
/// Devuelve el mensaje de error (403) o null si está permitido.
/// </summary>
public static class UserRules
{
    public static string? CanUpdate(Guid actorId, UserRole actorRol, Guid targetId, UserRole targetRol, UserRole? nuevoRol, bool? nuevoActivo)
    {
        var esPropio = actorId == targetId;

        if (!esPropio && !Gestiona(actorRol, targetRol))
            return SoloElDueno;

        if (nuevoRol.HasValue && nuevoRol.Value != targetRol)
        {
            if (esPropio)
                return "No puedes cambiar tu propio rol.";
            if (actorRol != UserRole.SuperAdmin && (!EsInferior(nuevoRol.Value, actorRol) || !EsInferior(targetRol, actorRol)))
                return "Solo el dueño puede cambiar el rol de un usuario.";
        }

        if (esPropio && nuevoActivo == false)
            return "No puedes desactivar tu propia cuenta.";

        return null;
    }

    public static string? CanDelete(Guid actorId, UserRole actorRol, Guid targetId, UserRole targetRol)
    {
        if (actorId == targetId)
            return "No puedes eliminar tu propia cuenta.";
        if (!Gestiona(actorRol, targetRol))
            return SoloElDueno;
        return null;
    }

    /// <summary>Restablecer la clave de otro (la propia se cambia con "Cambiar contraseña").</summary>
    public static string? CanResetPassword(UserRole actorRol, UserRole targetRol) =>
        Gestiona(actorRol, targetRol) ? null : SoloElDueno;

    private const string SoloElDueno = "Solo el dueño puede gestionar cuentas de recepción o administración.";

    /// <summary>
    /// El dueño gestiona a todos; recepción, solo a esteticistas; nadie más a nadie. No depende de la
    /// política del endpoint: aunque una esteticista llegara aquí, la regla la rechaza.
    /// </summary>
    private static bool Gestiona(UserRole actorRol, UserRole targetRol) => actorRol switch
    {
        UserRole.SuperAdmin => true,
        UserRole.Admin => targetRol == UserRole.Esteticista,
        _ => false,
    };

    /// <summary>SuperAdmin = 0 &lt; Admin = 1 &lt; Esteticista = 2: un número mayor es un rol menor.</summary>
    private static bool EsInferior(UserRole rol, UserRole respectoDe) => (int)rol > (int)respectoDe;
}
