using System.Security.Claims;
using NemediClinic.Api.Services;
using NemediClinic.Domain.Entities;
using NemediClinic.Infrastructure.Services;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>El JWT solo vale mientras describa la cuenta como está en la base (SessionValidator.Check).</summary>
public class SessionValidatorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private const string Llave = "llave-de-pruebas-de-al-menos-treinta-y-dos-caracteres";
    private static readonly string Sello = PasswordStamp.Compute("$2a$11$hashDeLaClaveActual", Llave);

    private static ClaimsPrincipal Token(string rol, Guid? tenant, string? sello = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, rol) };
        if (tenant is { } t) claims.Add(new Claim(SessionValidator.TenantClaim, t.ToString()));
        if ((sello ?? Sello) is { Length: > 0 } s) claims.Add(new Claim(PasswordStamp.ClaimName, s));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static SessionValidator.Account Cuenta(string rol, Guid? tenant, bool activo = true, bool clinicaActiva = true) =>
        new(activo, rol, tenant, clinicaActiva, Sello);

    [Fact]
    public void Acepta_la_cuenta_activa_con_el_mismo_rol_clinica_y_clave()
    {
        Assert.Null(SessionValidator.Check(Token("Admin", Tenant), Cuenta("Admin", Tenant)));
        Assert.Null(SessionValidator.Check(Token(PlatformAdmin.RoleName, null), Cuenta(PlatformAdmin.RoleName, null)));
    }

    [Fact]
    public void Rechaza_una_cuenta_borrada_o_inexistente()
    {
        Assert.NotNull(SessionValidator.Check(Token("SuperAdmin", Tenant), null));
        Assert.NotNull(SessionValidator.Check(Token(PlatformAdmin.RoleName, null), null));
    }

    [Fact]
    public void Rechaza_una_cuenta_desactivada()
    {
        Assert.NotNull(SessionValidator.Check(Token("Admin", Tenant), Cuenta("Admin", Tenant, activo: false)));
        Assert.NotNull(SessionValidator.Check(Token(PlatformAdmin.RoleName, null), Cuenta(PlatformAdmin.RoleName, null, activo: false)));
    }

    [Fact]
    public void Rechaza_un_token_con_un_rol_que_ya_no_tiene()
    {
        // SuperAdmin degradado a Esteticista: su token viejo todavía dice SuperAdmin
        Assert.NotNull(SessionValidator.Check(Token("SuperAdmin", Tenant), Cuenta("Esteticista", Tenant)));
    }

    [Fact]
    public void Rechaza_un_token_de_otra_clinica_o_sin_clinica()
    {
        Assert.NotNull(SessionValidator.Check(Token("Admin", Guid.NewGuid()), Cuenta("Admin", Tenant)));
        Assert.NotNull(SessionValidator.Check(Token("Admin", null), Cuenta("Admin", Tenant)));
    }

    [Fact]
    public void Rechaza_si_la_clinica_ya_no_esta_activa()
    {
        Assert.NotNull(SessionValidator.Check(Token("SuperAdmin", Tenant), Cuenta("SuperAdmin", Tenant, clinicaActiva: false)));
    }

    [Fact]
    public void Rechaza_un_token_emitido_antes_de_cambiar_la_clave()
    {
        var anterior = PasswordStamp.Compute("$2a$11$hashDeLaClaveAnterior", Llave);
        Assert.NotNull(SessionValidator.Check(Token("Admin", Tenant, anterior), Cuenta("Admin", Tenant)));
        Assert.NotNull(SessionValidator.Check(Token(PlatformAdmin.RoleName, null, anterior), Cuenta(PlatformAdmin.RoleName, null)));
        // Token sin sello (emitido antes de esta regla): renueva con el refresh token
        Assert.NotNull(SessionValidator.Check(Token("Admin", Tenant, sello: ""), Cuenta("Admin", Tenant)));
    }

    [Fact]
    public void El_sello_usa_una_subllave_y_no_la_llave_de_los_jwt()
    {
        // El HMAC con la llave directa de los JWT (lo que se hacía antes) no puede coincidir con el sello
        var directo = System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Llave), System.Text.Encoding.UTF8.GetBytes("pwd-stamp|$2a$11$hashDeLaClaveActual"));
        var directoTexto = Convert.ToBase64String(directo, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.NotEqual(directoTexto, Sello);

        // Y es exactamente HMAC-SHA256(HKDF-SHA256(llave, info "NemediClinic.PasswordStamp.v1"), hash), 16 bytes
        var subllave = System.Security.Cryptography.HKDF.DeriveKey(System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Text.Encoding.UTF8.GetBytes(Llave), 32, [], System.Text.Encoding.UTF8.GetBytes("NemediClinic.PasswordStamp.v1"));
        var esperado = System.Security.Cryptography.HMACSHA256.HashData(subllave, System.Text.Encoding.UTF8.GetBytes("$2a$11$hashDeLaClaveActual"));
        Assert.Equal(Convert.ToBase64String(esperado, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_'), Sello);
    }

    [Fact]
    public void Renovar_exige_el_sello_de_la_clave_actual_si_el_token_lo_trae()
    {
        Assert.True(SessionValidator.StampAllowsRefresh(Token("Admin", Tenant), "$2a$11$hashDeLaClaveActual", Llave));
        Assert.False(SessionValidator.StampAllowsRefresh(Token("Admin", Tenant), "$2a$11$hashDeOtraClave", Llave));
        // Token de antes de la regla (sin sello): renueva una vez y el nuevo ya lo trae
        Assert.True(SessionValidator.StampAllowsRefresh(Token("Admin", Tenant, sello: ""), "$2a$11$hashDeOtraClave", Llave));
    }

    [Fact]
    public void El_sello_depende_de_la_llave_y_del_hash()
    {
        Assert.NotEqual(Sello, PasswordStamp.Compute("$2a$11$hashDeLaClaveActual", "otra-llave-de-pruebas-de-treinta-y-dos-caracteres!"));
        Assert.Equal(Sello, PasswordStamp.Compute("$2a$11$hashDeLaClaveActual", Llave));
        Assert.DoesNotContain("hashDeLaClave", Sello);
    }
}
