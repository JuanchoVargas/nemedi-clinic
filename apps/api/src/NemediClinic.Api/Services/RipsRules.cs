using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services;

/// <summary>
/// Qué exige RIPS (Resolución 2275 de 2023) y cuándo.
///
/// Los datos RIPS del paciente, del procedimiento y del tenant existen siempre, pero **solo se
/// exigen si la clínica es IPS** (Tenant.EsIps, lo marca la plataforma):
///   · Paciente: tipo de documento, fecha de nacimiento, sexo, país de residencia, municipio DANE
///     (si reside en Colombia), zona y régimen; la entidad (EPS) solo en los regímenes afiliados.
///     Se valida al crear y al editar la ficha. Convertir una valoración crea el paciente sin
///     ellos: la ficha queda marcada "RIPS incompleto" hasta que alguien la complete.
///   · Procedimiento marcado RequiereRips: código CUPS, finalidad y tipo de servicio.
///   · Tenant IPS: código de habilitación REPS de 12 dígitos.
/// El formato (DANE de 5 dígitos, país de 3, CUPS de 4 a 8 alfanuméricos) se valida siempre que
/// el dato venga, sea o no IPS: un dato mal escrito no sirve para nada.
/// </summary>
public static partial class RipsRules
{
    public const string Colombia = "170";

    /// <summary>Regímenes en los que la persona está afiliada a una entidad y hay que decir cuál.</summary>
    private static readonly TipoUsuarioRips[] ConEntidad =
    [
        TipoUsuarioRips.ContributivoCotizante, TipoUsuarioRips.ContributivoBeneficiario, TipoUsuarioRips.ContributivoAdicional,
        TipoUsuarioRips.Subsidiado, TipoUsuarioRips.EspecialCotizante, TipoUsuarioRips.EspecialBeneficiario,
        TipoUsuarioRips.EspecialNoCotizante
    ];

    public static bool RequiereEntidad(TipoUsuarioRips? tipo) => tipo is { } t && ConEntidad.Contains(t);

    public static async Task<bool> TenantEsIpsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var tenantId = db.CurrentTenantId;
        return await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.EsIps).FirstOrDefaultAsync(ct);
    }

    /// <summary>Datos RIPS que le faltan al paciente (etiquetas legibles). Vacío = completo.</summary>
    public static List<string> FaltantesPaciente(Patient p)
    {
        var faltan = new List<string>();
        if (p.TipoDocumento is null) faltan.Add("tipo de documento");
        if (p.FechaNacimiento is null) faltan.Add("fecha de nacimiento");
        if (p.Sexo is null) faltan.Add("sexo");
        if (string.IsNullOrWhiteSpace(p.PaisResidencia)) faltan.Add("país de residencia");
        if (p.PaisResidencia == Colombia && string.IsNullOrWhiteSpace(p.MunicipioResidencia)) faltan.Add("municipio de residencia");
        if (p.ZonaResidencia is null) faltan.Add("zona de residencia");
        if (p.TipoUsuario is null) faltan.Add("régimen de afiliación");
        if (RequiereEntidad(p.TipoUsuario) && string.IsNullOrWhiteSpace(p.CodigoEntidad)) faltan.Add("entidad (EPS)");
        return faltan;
    }

    /// <summary>Formato de lo que venga; lanza 400. Se aplica siempre, sea o no IPS.</summary>
    public static void ValidarFormatoPaciente(Patient p)
    {
        if (p.PaisResidencia is { Length: > 0 } pais && !TresDigitos().IsMatch(pais))
            throw ApiException.BadRequest("El país de residencia va con su código numérico de 3 dígitos (Colombia = 170).");
        if (p.MunicipioResidencia is { Length: > 0 } municipio)
        {
            if (!CincoDigitos().IsMatch(municipio))
                throw ApiException.BadRequest("El municipio va con su código DANE de 5 dígitos (Bogotá = 11001).");
            if (p.PaisResidencia is { Length: > 0 } && p.PaisResidencia != Colombia)
                throw ApiException.BadRequest("El municipio DANE solo aplica a quien reside en Colombia.");
        }
        if (p.CodigoEntidad is { Length: > 0 } && !RequiereEntidad(p.TipoUsuario) && p.TipoUsuario is not null)
            throw ApiException.BadRequest("Ese régimen no tiene entidad administradora: deja la EPS en blanco.");
    }

    public static async Task ValidarPacienteAsync(AppDbContext db, Patient p, CancellationToken ct = default)
    {
        ValidarFormatoPaciente(p);
        if (!await TenantEsIpsAsync(db, ct))
            return;
        var faltan = FaltantesPaciente(p);
        if (faltan.Count > 0)
            throw ApiException.BadRequest($"La clínica es IPS y RIPS exige estos datos del paciente: {string.Join(", ", faltan)}.");
    }

    public static List<string> FaltantesProcedimiento(Procedure p)
    {
        var faltan = new List<string>();
        if (!p.RequiereRips) return faltan;
        if (string.IsNullOrWhiteSpace(p.CodigoCups)) faltan.Add("código CUPS");
        if (p.FinalidadConsulta is null) faltan.Add("finalidad");
        if (p.TipoServicio is null) faltan.Add("tipo de servicio");
        return faltan;
    }

    public static async Task ValidarProcedimientoAsync(AppDbContext db, Procedure p, CancellationToken ct = default) =>
        ValidarProcedimiento(p, await TenantEsIpsAsync(db, ct));

    /// <summary>La misma regla sin ir a la base (el importador ya sabe si la clínica es IPS).</summary>
    public static void ValidarProcedimiento(Procedure p, bool esIps)
    {
        if (p.CodigoCups is { Length: > 0 } cups && !Cups().IsMatch(cups))
            throw ApiException.BadRequest("El código CUPS lleva de 4 a 8 letras mayúsculas o dígitos, sin espacios (p. ej. 890201).");
        if (!esIps)
            return;
        var faltan = FaltantesProcedimiento(p);
        if (faltan.Count > 0)
            throw ApiException.BadRequest($"El procedimiento se reporta en RIPS y le falta: {string.Join(", ", faltan)}.");
    }

    public static void ValidarReps(bool esIps, string? codigo)
    {
        if (codigo is { Length: > 0 } && !DoceDigitos().IsMatch(codigo))
            throw ApiException.BadRequest("El código de habilitación REPS tiene 12 dígitos (10 del prestador y 2 de la sede).");
        if (esIps && string.IsNullOrWhiteSpace(codigo))
            throw ApiException.BadRequest("Como prestador habilitado (IPS), la clínica necesita su código de habilitación REPS.");
    }

    /// <summary>Texto vacío = null; lo demás, sin espacios alrededor y en mayúsculas.</summary>
    public static string? Codigo(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToUpperInvariant();

    [GeneratedRegex("^[0-9]{3}$")] private static partial Regex TresDigitos();
    [GeneratedRegex("^[0-9]{5}$")] private static partial Regex CincoDigitos();
    [GeneratedRegex("^[0-9]{12}$")] private static partial Regex DoceDigitos();
    [GeneratedRegex("^[0-9A-Z]{4,8}$")] private static partial Regex Cups();
}
