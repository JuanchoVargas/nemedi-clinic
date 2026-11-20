using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using NemediClinic.Api.Services;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private const string WithOffset = "yyyy-MM-dd'T'HH:mm:sszzz";

    private readonly IClock _clock;
    private readonly TimeProvider _time;
    private readonly WriteAccessMonitor _writeAccess;

    public HealthController(IClock clock, TimeProvider time, WriteAccessMonitor writeAccess)
    {
        _clock = clock;
        _time = time;
        _writeAccess = writeAccess;
    }

    /// <summary>
    /// Vivo + las tres horas: UTC, la del servidor (informativa: el sistema no la usa) y la de la
    /// clínica calculada con Clock:TimeZone. Van como texto con su offset, no como DateTime, para que
    /// el converter de la API no las lleve a la hora de la clínica. verify.ps1 las compara. También el
    /// runtime con el que corre y el framework para el que se compiló: verify.ps1 los compara con el
    /// del servidor (un desajuste de versión mayor es el 500.31). Y la versión compilada, que lleva el
    /// commit ("1.0.0+sha"): verify.ps1 confirma que el despliegue reemplazó de verdad la DLL.
    /// Y si puede escribir en la carpeta de archivos y en la de logs (WriteAccessMonitor, cacheado
    /// 60 s): sin permiso, subir una foto falla. El error va sin la ruta del servidor.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var escritura = await _writeAccess.GetAsync(ct);
        var instant = _time.GetUtcNow();
        var server = TimeZoneInfo.ConvertTime(instant, _time.LocalTimeZone);
        var clinic = TimeZoneInfo.ConvertTime(instant, _clock.TimeZone);
        return Ok(new
        {
            status = "healthy",
            utc = instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            horaServidor = server.ToString(WithOffset, CultureInfo.InvariantCulture),
            zonaServidor = _time.LocalTimeZone.Id,
            horaClinica = clinic.ToString(WithOffset, CultureInfo.InvariantCulture),
            zonaConfigurada = _clock.TimeZone.Id,
            utcOffsetClinica = FormatOffset(clinic.Offset),
            runtime = RuntimeInformation.FrameworkDescription,
            targetFramework = AppContext.TargetFrameworkName,
            version = typeof(HealthController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            archivos = new { escribible = escritura.Archivos.Escribible, error = escritura.Archivos.Error, centinela = escritura.Archivos.Centinela },
            logs = escritura.Logs is null ? null : new { escribible = escritura.Logs.Escribible, error = escritura.Logs.Error, centinela = escritura.Logs.Centinela }
        });
    }

    public static string FormatOffset(TimeSpan offset) =>
        $"{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
}
