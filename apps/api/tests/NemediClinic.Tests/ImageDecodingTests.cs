using NemediClinic.Api.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>
/// El API solo decodifica JPEG, PNG y WebP (ImageDecoding): los decodificadores de los demás formatos
/// de ImageSharp 3.1 (TIFF, BMP, GIF…) ni siquiera están cargados, así que sus fallas conocidas no se
/// alcanzan aunque un archivo de ese formato llegara a decodificarse.
/// </summary>
public class ImageDecodingTests
{
    private static byte[] Encode(Action<Image<Rgba32>, Stream> save)
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(200, 30, 30));
        using var ms = new MemoryStream();
        save(image, ms);
        return ms.ToArray();
    }

    public static TheoryData<string> Prohibidos => new() { "tiff", "bmp", "gif" };
    public static TheoryData<string> Permitidos => new() { "jpeg", "png", "webp" };

    private static byte[] Archivo(string formato) => formato switch
    {
        "tiff" => Encode((i, s) => i.SaveAsTiff(s)),
        "bmp" => Encode((i, s) => i.SaveAsBmp(s)),
        "gif" => Encode((i, s) => i.SaveAsGif(s)),
        "jpeg" => Encode((i, s) => i.SaveAsJpeg(s)),
        "png" => Encode((i, s) => i.SaveAsPng(s)),
        "webp" => Encode((i, s) => i.SaveAsWebp(s)),
        _ => throw new ArgumentOutOfRangeException(nameof(formato)),
    };

    [Theory]
    [MemberData(nameof(Prohibidos))]
    public async Task Un_formato_fuera_de_la_lista_no_se_decodifica(string formato)
    {
        var bytes = Archivo(formato);
        Assert.Throws<UnknownImageFormatException>(() => ImageDecoding.Load(bytes));
        await Assert.ThrowsAsync<UnknownImageFormatException>(() => ImageDecoding.LoadAsync(new MemoryStream(bytes), CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Permitidos))]
    public async Task Jpeg_png_y_webp_se_decodifican(string formato)
    {
        var bytes = Archivo(formato);
        using var sync = ImageDecoding.Load(bytes);
        using var async = await ImageDecoding.LoadAsync(new MemoryStream(bytes), CancellationToken.None);
        Assert.Equal(8, sync.Width);
        Assert.Equal(8, async.Height);
    }
}
