using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using NemediClinic.Api.Controllers;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>
/// POST /api/v1/auth/register ya no es anónimo. Antes, con la base sin ningún SuperAdmin (una clínica
/// recién creada desde plataforma, antes de su bootstrap-admin), cualquiera creaba un usuario con el rol
/// que quisiera en la primera clínica. Ese estado no se puede fabricar en la base de desarrollo
/// compartida sin dañarla, así que se prueba el contrato del endpoint (el e2e cubre 401/403).
/// </summary>
public class AuthEndpointTests
{
    private static MethodInfo Register => typeof(AuthController).GetMethod(nameof(AuthController.Register))!;

    [Fact]
    public void Register_no_es_anonimo()
    {
        Assert.Null(Register.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(typeof(AuthController).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Register_exige_la_politica_Admin()
    {
        var policies = Register.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);
        Assert.Contains("Admin", policies);
    }
}
