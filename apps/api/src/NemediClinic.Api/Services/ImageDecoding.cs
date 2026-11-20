using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace NemediClinic.Api.Services;

/// <summary>
/// Toda imagen que entra al API (adjuntos, firma del consentimiento) se decodifica con estas opciones:
/// una <see cref="Configuration"/> que solo trae JPEG, PNG y WebP. Los decodificadores de TIFF, BMP, GIF
/// y demás no están cargados, así que no se alcanzan aunque un archivo de ese formato llegara hasta aquí
/// (UnknownImageFormatException).
/// Por qué no se sube ImageSharp a 3.2 (que cierra los avisos de seguridad de 3.1.12): desde 3.2 exige
/// licencia de Six Labors y el build Release falla sin ella. Decisión del proyecto (2026-11-17):
/// quedarse en 3.1.12 con esta lista cerrada. Estado de cada aviso de 3.1.12:
///   · altos: GHSA-j3p4-wp97-rph4 (HistogramEqualization) y GHSA-j9gm-c75j-xc9q / GHSA-jjfr-hcj7-qf5w
///     (CODIFICADORES TIFF CCITT T4/T6): código que el API no usa (no ecualiza ni guarda TIFF);
///     HistogramEqualization está prohibido en BannedSymbols.txt;
///   · alto GHSA-v76p-62qx-wwq2 (TIFF fax en tiles: escritura fuera del búfer) y moderado GHSA-wmxv-xphr-5c9g
///     (bucle en BigTIFF): están en el DECODIFICADOR TIFF, que no está cargado: fuera de alcance;
///   · moderado GHSA-gwg2-r3hj-4w44 (perfiles ICC con dimensiones sin validar): SIGUE ABIERTO, porque un
///     JPEG, PNG o WebP puede traer perfil ICC; se cierra al subir a 3.2 con licencia.
/// BannedSymbols.txt prohíbe Image.Load*/Identify* sin DecoderOptions: toda carga pasa por aquí.
/// </summary>
public static class ImageDecoding
{
    public static readonly DecoderOptions Options = new()
    {
        Configuration = new Configuration(new JpegConfigurationModule(), new PngConfigurationModule(), new WebpConfigurationModule()),
    };

    public static Task<Image> LoadAsync(Stream input, CancellationToken ct) => Image.LoadAsync(Options, input, ct);

    public static Image Load(byte[] bytes) => Image.Load(Options, bytes);
}
