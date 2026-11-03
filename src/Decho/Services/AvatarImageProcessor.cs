using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Decho.Services;

internal static class AvatarImageProcessor
{
    private const int OutputSize = 256;

    /// <summary>
    /// Center-crops to a square PNG. The server stretches uploads to a square and drops
    /// transparency, so both are handled here before the image leaves the client.
    /// </summary>
    public static byte[] PrepareForUpload(Stream source)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(source);

        image.Mutate(context => context
            .AutoOrient()
            .Resize(new ResizeOptions
            {
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center,
                Size = new Size(OutputSize, OutputSize),
            })
            .BackgroundColor(Color.ParseHex("2B2D31")));

        using MemoryStream output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }
}
