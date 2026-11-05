namespace Djohnnie.SoftwareFactory.Application.Features.Uploads;

/// <summary>The raster image formats that can be uploaded, detected from their file signature (SVG is excluded on purpose).</summary>
public sealed record ImageFormat(string ContentType, string Extension)
{
    public static readonly ImageFormat Png = new("image/png", ".png");
    public static readonly ImageFormat Jpeg = new("image/jpeg", ".jpg");
    public static readonly ImageFormat Gif = new("image/gif", ".gif");
    public static readonly ImageFormat Webp = new("image/webp", ".webp");

    public static ImageFormat? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Png;
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return Jpeg;
        }

        if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8))
        {
            return Gif;
        }

        if (content.Length >= 12 && content.StartsWith("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        return null;
    }
}
