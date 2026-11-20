using Microsoft.Extensions.Configuration;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Infrastructure.Services;

/// <summary>
/// Storage en disco: {root}/{tenantId}/{yyyy}/{guid}{ext}. El root sale de Storage:UploadsPath
/// (por defecto /data/uploads; en Development, App_Data/uploads dentro del proyecto).
///
/// Si la carpeta no se puede crear o no se puede escribir en ella (en IIS: la identidad del pool no
/// tiene permiso), escribir lanza StorageUnavailableException con el motivo, y CheckWriteAccessAsync
/// lo detecta antes de que alguien suba algo (lo usa /api/health). El constructor no falla: un error de
/// permisos no debe tumbar todo el API, solo lo que guarda archivos.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly IClock _clock;

    public string? LocalRoot => _root;

    public LocalFileStorage(IConfiguration configuration, IClock clock)
    {
        _clock = clock;
        var configured = configuration["Storage:UploadsPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? "/data/uploads" : configured);
        try
        {
            Directory.CreateDirectory(_root);
        }
        catch (Exception e) when (IsAccessProblem(e))
        {
            // Se reporta al escribir y en /api/health
        }
    }

    public async Task<string> SaveAsync(Guid tenantId, string extension, Stream content, CancellationToken ct = default)
    {
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var key = $"{tenantId:D}/{_clock.Now:yyyy}/{Guid.NewGuid():N}{ext}";
        var fullPath = Resolve(key);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(file, ct);
        }
        catch (Exception e) when (IsAccessProblem(e) && !ct.IsCancellationRequested)
        {
            throw new StorageUnavailableException(Describe(e), e);
        }
        return key;
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var fullPath = Resolve(path);
        Stream? stream = File.Exists(fullPath)
            ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var fullPath = Resolve(path);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public async Task<string?> CheckWriteAccessAsync(CancellationToken ct = default)
    {
        var probe = Path.Combine(_root, $".write-check-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(_root);
            await File.WriteAllBytesAsync(probe, [0x6E, 0x65, 0x6D, 0x65, 0x64, 0x69], ct);
            var read = await File.ReadAllBytesAsync(probe, ct);
            File.Delete(probe);
            return read.Length == 6 ? null : "el archivo de prueba no se leyó completo";
        }
        catch (Exception e) when (IsAccessProblem(e))
        {
            return Describe(e);
        }
    }

    /// <summary>La clave viene de la base, pero igual se impide salir del root (path traversal).</summary>
    private string Resolve(string key)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, key));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Ruta de archivo fuera del almacenamiento.");
        return fullPath;
    }

    private static bool IsAccessProblem(Exception e) =>
        e is UnauthorizedAccessException or IOException or System.Security.SecurityException;

    /// <summary>Tipo y mensaje del error, con la ruta del servidor reemplazada (va a un endpoint público).</summary>
    private string Describe(Exception e) =>
        $"{e.GetType().Name}: {e.Message.Replace(_root, "<Storage:UploadsPath>", StringComparison.OrdinalIgnoreCase)}";
}
