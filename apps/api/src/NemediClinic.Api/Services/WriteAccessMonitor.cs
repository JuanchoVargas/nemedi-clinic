using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Services;

/// <summary>
/// ¿Puede el API escribir donde lo necesita? La carpeta de archivos (Storage:UploadsPath) y la de
/// logs (Logging:File:Path). En IIS la identidad del pool de /api necesita permiso de escritura en
/// las dos: sin él, subir una foto fallaba con un 500 y los logs no se escribían (así que ni siquiera
/// quedaba el error).
///
/// Además deja en cada carpeta un CENTINELA (<see cref="SentinelName"/>): un archivo que existe de
/// verdad, sin datos, para que verify.ps1 y build-release.ps1 lo pidan por HTTP y exijan 404. En IIS
/// esas carpetas viven bajo App_Data dentro del sitio, protegidas por su web.config; pedir la carpeta
/// o un nombre inventado también da 404 aunque la protección falle, así que solo un archivo que existe
/// prueba algo.
///
/// Se revisa al arrancar (un CRIT en el log si falla) y en /api/health, cacheado 60 s: el endpoint es
/// público y no debe escribir en disco en cada llamada, pero sí notar el arreglo del administrador sin
/// reiniciar el API.
/// </summary>
public class WriteAccessMonitor
{
    public const string SentinelName = "nemedi-privado.txt";
    private const string SentinelText =
        "Carpeta privada de NemediClinic (fotos de pacientes, comprobantes, logs).\r\n" +
        "Este archivo no tiene datos: existe para comprobar que nada de esta carpeta se sirve por HTTP.\r\n" +
        "Si lo estás leyendo desde un navegador, la carpeta quedó expuesta: avisa de inmediato.\r\n";

    public sealed record Probe(bool Escribible, string? Error, string? Centinela);
    public sealed record Status(Probe Archivos, Probe? Logs, DateTime RevisadoUtc);

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private readonly IFileStorage _storage;
    private readonly IClock _clock;
    private readonly ILogger<WriteAccessMonitor> _logger;
    private readonly string? _logDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Status? _last;

    public WriteAccessMonitor(IFileStorage storage, IConfiguration configuration, IClock clock, ILogger<WriteAccessMonitor> logger)
    {
        _storage = storage;
        _clock = clock;
        _logger = logger;
        var logs = configuration["Logging:File:Path"];
        _logDirectory = string.IsNullOrWhiteSpace(logs) ? null : Path.GetFullPath(logs);
    }

    public async Task<Status> GetAsync(CancellationToken ct = default)
    {
        if (_last is { } fresh && _clock.UtcNow - fresh.RevisadoUtc < CacheFor) return fresh;
        await _gate.WaitAsync(ct);
        try
        {
            if (_last is { } other && _clock.UtcNow - other.RevisadoUtc < CacheFor) return other;
            var archivos = await _storage.CheckWriteAccessAsync(ct);
            var archivosCentinela = archivos is null && _storage.LocalRoot is { } root ? await EnsureSentinelAsync(root, ct) : null;
            var logs = _logDirectory is null ? null : await CheckDirectoryAsync(_logDirectory, ct);
            var logsCentinela = _logDirectory is not null && logs is null ? await EnsureSentinelAsync(_logDirectory, ct) : null;
            var status = new Status(
                new Probe(archivos is null, archivos, archivosCentinela),
                _logDirectory is null ? null : new Probe(logs is null, logs, logsCentinela),
                _clock.UtcNow);

            // Solo al cambiar: sin repetir el mismo error cada minuto
            if (archivos is not null && _last?.Archivos.Escribible != false)
                _logger.LogCritical("El API no puede escribir en la carpeta de archivos (Storage:UploadsPath): {Error} Subir fotos y comprobantes fallará.", archivos);
            if (logs is not null && _last?.Logs?.Escribible != false)
                _logger.LogCritical("El API no puede escribir en la carpeta de logs (Logging:File:Path): {Error}", logs);
            _last = status;
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deja el centinela si no está. Devuelve su nombre, o null si no se pudo escribir.</summary>
    private async Task<string?> EnsureSentinelAsync(string directory, CancellationToken ct)
    {
        var path = Path.Combine(directory, SentinelName);
        try
        {
            if (!File.Exists(path)) await File.WriteAllTextAsync(path, SentinelText, ct);
            return SentinelName;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning("No se pudo dejar el centinela en {Directory}: {Error}", directory, e.Message);
            return null;
        }
    }

    private static async Task<string?> CheckDirectoryAsync(string directory, CancellationToken ct)
    {
        var probe = Path.Combine(directory, $".write-check-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(probe, "nemedi", ct);
            File.Delete(probe);
            return null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return $"{e.GetType().Name}: {e.Message.Replace(directory, "<Logging:File:Path>", StringComparison.OrdinalIgnoreCase)}";
        }
    }
}
