using NemediClinic.Api.Services;
using NemediClinic.Domain.Enums;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>Quién puede cambiar el rol, el estado o los datos de una cuenta (UserRules).</summary>
public class UserRulesTests
{
    private static readonly Guid Yo = Guid.NewGuid();
    private static readonly Guid Otro = Guid.NewGuid();

    [Fact]
    public void Recepcion_no_se_asciende_a_si_misma()
    {
        Assert.NotNull(UserRules.CanUpdate(Yo, UserRole.Admin, Yo, UserRole.Admin, UserRole.SuperAdmin, null));
    }

    [Fact]
    public void Nadie_cambia_su_propio_rol_ni_el_dueno()
    {
        Assert.NotNull(UserRules.CanUpdate(Yo, UserRole.SuperAdmin, Yo, UserRole.SuperAdmin, UserRole.Admin, null));
        Assert.NotNull(UserRules.CanUpdate(Yo, UserRole.Esteticista, Yo, UserRole.Esteticista, UserRole.Admin, null));
    }

    [Fact]
    public void Mandar_el_mismo_rol_no_es_un_cambio()
    {
        // La web manda todos los campos al guardar: editar los propios datos no puede fallar por eso
        Assert.Null(UserRules.CanUpdate(Yo, UserRole.Admin, Yo, UserRole.Admin, UserRole.Admin, true));
        Assert.Null(UserRules.CanUpdate(Yo, UserRole.SuperAdmin, Yo, UserRole.SuperAdmin, UserRole.SuperAdmin, true));
    }

    [Theory]
    [InlineData(UserRole.Esteticista, UserRole.Admin)]
    [InlineData(UserRole.Esteticista, UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin, UserRole.Esteticista)]
    [InlineData(UserRole.Admin, UserRole.SuperAdmin)]
    public void Recepcion_no_cambia_roles(UserRole actual, UserRole nuevo)
    {
        Assert.NotNull(UserRules.CanUpdate(Yo, UserRole.Admin, Otro, actual, nuevo, null));
    }

    [Theory]
    [InlineData(UserRole.Esteticista, UserRole.Admin)]
    [InlineData(UserRole.Admin, UserRole.SuperAdmin)]
    [InlineData(UserRole.SuperAdmin, UserRole.Admin)]
    public void El_dueno_cambia_el_rol_de_otros(UserRole actual, UserRole nuevo)
    {
        Assert.Null(UserRules.CanUpdate(Yo, UserRole.SuperAdmin, Otro, actual, nuevo, null));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, false)]
    [InlineData(UserRole.SuperAdmin, true)]
    public void Recepcion_no_toca_a_un_superadmin(UserRole? rol, bool? activo)
    {
        Assert.NotNull(UserRules.CanUpdate(Yo, UserRole.Admin, Otro, UserRole.SuperAdmin, rol, activo));
        Assert.NotNull(UserRules.CanDelete(Yo, UserRole.Admin, Otro, UserRole.SuperAdmin));
    }

    [Fact]
    public void Recepcion_gestiona_esteticistas()
    {
        Assert.Null(UserRules.CanUpdate(Yo, UserRole.Admin, Otro, UserRole.Esteticista, UserRole.Esteticista, false));
        Assert.Null(UserRules.CanDelete(Yo, UserRole.Admin, Otro, UserRole.Esteticista));
        Assert.Null(UserRules.CanResetPassword(UserRole.Admin, UserRole.Esteticista));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, false)]
    [InlineData(UserRole.Admin, true)]
    public void Recepcion_no_gestiona_a_otra_recepcion(UserRole? rol, bool? activo)
    {
        // Regla de negocio: solo el dueño gestiona Admins (editar, desactivar, correo, borrar, clave)
        Assert.NotNull(UserRules.CanUpdate(Yo, UserRole.Admin, Otro, UserRole.Admin, rol, activo));
        Assert.NotNull(UserRules.CanDelete(Yo, UserRole.Admin, Otro, UserRole.Admin));
        Assert.NotNull(UserRules.CanResetPassword(UserRole.Admin, UserRole.Admin));
        Assert.NotNull(UserRules.CanResetPassword(UserRole.Admin, UserRole.SuperAdmin));
    }

    [Fact]
    public void Una_esteticista_no_gestiona_a_nadie_aunque_llegue_a_la_regla()
    {
        // La política Admin del endpoint ya la deja fuera; la regla no depende de eso
        Assert.NotNull(UserRules.CanUpdate(Yo, UserRole.Esteticista, Otro, UserRole.Esteticista, null, false));
        Assert.NotNull(UserRules.CanDelete(Yo, UserRole.Esteticista, Otro, UserRole.Esteticista));
        Assert.NotNull(UserRules.CanResetPassword(UserRole.Esteticista, UserRole.Esteticista));
    }

    [Fact]
    public void El_dueno_gestiona_admins_y_otros_duenos()
    {
        Assert.Null(UserRules.CanUpdate(Yo, UserRole.SuperAdmin, Otro, UserRole.Admin, UserRole.Admin, false));
        Assert.Null(UserRules.CanDelete(Yo, UserRole.SuperAdmin, Otro, UserRole.Admin));
        Assert.Null(UserRules.CanResetPassword(UserRole.SuperAdmin, UserRole.Admin));
        Assert.Null(UserRules.CanResetPassword(UserRole.SuperAdmin, UserRole.SuperAdmin));
    }

    [Fact]
    public void Nadie_se_desactiva_ni_se_elimina_a_si_mismo()
    {
        Assert.NotNull(UserRules.CanUpdate(Yo, UserRole.SuperAdmin, Yo, UserRole.SuperAdmin, null, false));
        Assert.NotNull(UserRules.CanDelete(Yo, UserRole.SuperAdmin, Yo, UserRole.SuperAdmin));
        Assert.NotNull(UserRules.CanDelete(Yo, UserRole.Admin, Yo, UserRole.Admin));
    }
}
