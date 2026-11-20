namespace NemediClinic.Application.Interfaces;

/// <summary>
/// Almacenamiento de binarios. Hoy LocalFileStorage (disco); la interfaz solo habla de
/// claves opacas ("{tenantId}/{yyyy}/{guid}.ext") para poder cambiarla por S3/R2 sin tocar el resto.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Guarda el contenido y devuelve la clave con la que se recupera. Si el almacenamiento no
    /// permite escribir (permisos, disco lleno, carpeta inexistente) lanza StorageUnavailableException.
    /// </summary>
    Task<string> SaveAsync(Guid tenantId, string extension, Stream content, CancellationToken ct = default);

    /// <summary>Abre el archivo para lectura, o null si no existe.</summary>
    Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default);

    Task DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Escribe, lee y borra un archivo de prueba. null si se puede escribir; si no, el motivo (sin la
    /// ruta del servidor, porque /api/health es público).
    /// </summary>
    Task<string?> CheckWriteAccessAsync(CancellationToken ct = default);

    /// <summary>Carpeta del disco donde guarda (null si no es un disco local, p. ej. S3).</summary>
    string? LocalRoot { get; }
}

/// <summary>El almacenamiento de archivos no permite escribir. El filtro de errores responde 503 con un mensaje claro.</summary>
public class StorageUnavailableException(string reason, Exception? inner = null)
    : Exception($"El almacenamiento de archivos no permite escribir: {reason}", inner)
{
    public string Reason { get; } = reason;
}
