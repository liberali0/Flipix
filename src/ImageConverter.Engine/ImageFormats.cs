namespace ImageConverter.Engine;

/// <summary>The format table shared by the engine, the CLI, the right-click menu and the app.</summary>
public static class ImageFormats
{
    /// <summary>Every format, in menu order.</summary>
    public static IReadOnlyList<FormatInfo> All { get; } =
    [
        new(ImageFormat.Png, "png", "PNG", [".png"], CanRead: true, CanWrite: true, IsLosslessOutput: true, SupportsAlpha: true, MaxBitsPerChannel: 16, SupportsMetadata: true),
        new(ImageFormat.Jpeg, "jpg", "JPG", [".jpg", ".jpeg", ".jpe", ".jfif"], CanRead: true, CanWrite: true, IsLosslessOutput: false, SupportsAlpha: false, MaxBitsPerChannel: 8, SupportsMetadata: true),
        new(ImageFormat.Bmp, "bmp", "BMP", [".bmp", ".dib"], CanRead: true, CanWrite: true, IsLosslessOutput: true, SupportsAlpha: true, MaxBitsPerChannel: 8, SupportsMetadata: false),
        new(ImageFormat.Tiff, "tiff", "TIFF", [".tif", ".tiff"], CanRead: true, CanWrite: true, IsLosslessOutput: true, SupportsAlpha: true, MaxBitsPerChannel: 16, SupportsMetadata: true),
        new(ImageFormat.WebP, "webp", "WebP", [".webp"], CanRead: true, CanWrite: true, IsLosslessOutput: true, SupportsAlpha: true, MaxBitsPerChannel: 8, SupportsMetadata: true),
        new(ImageFormat.Avif, "avif", "AVIF", [".avif"], CanRead: true, CanWrite: true, IsLosslessOutput: true, SupportsAlpha: true, MaxBitsPerChannel: 8, SupportsMetadata: true),
        new(ImageFormat.Heic, "heic", "HEIC", [".heic", ".heif", ".hif"], CanRead: true, CanWrite: false, IsLosslessOutput: false, SupportsAlpha: true, MaxBitsPerChannel: 16, SupportsMetadata: true),
        new(ImageFormat.Gif, "gif", "GIF", [".gif"], CanRead: true, CanWrite: false, IsLosslessOutput: false, SupportsAlpha: true, MaxBitsPerChannel: 8, SupportsMetadata: false),
    ];

    private static readonly Dictionary<string, ImageFormat> ByName = BuildNameIndex();

    /// <summary>Capabilities of <paramref name="format"/>.</summary>
    public static FormatInfo Get(ImageFormat format) => All.First(f => f.Format == format);

    /// <summary>Parses an id or extension such as <c>png</c>, <c>JPEG</c>, <c>.tif</c>, <c>heif</c>.</summary>
    public static bool TryParse(string? text, out ImageFormat format)
    {
        format = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var key = text.Trim().TrimStart('.').ToLowerInvariant();
        return ByName.TryGetValue(key, out format);
    }

    /// <summary>Format implied by the file extension of <paramref name="path"/>, or null.</summary>
    public static ImageFormat? FromExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return TryParse(ext, out var format) && ext.Length > 1 ? format : null;
    }

    private static Dictionary<string, ImageFormat> BuildNameIndex()
    {
        var index = new Dictionary<string, ImageFormat>(StringComparer.Ordinal);
        foreach (var info in All)
        {
            index[info.Id] = info.Format;
            foreach (var ext in info.Extensions)
            {
                index[ext.TrimStart('.')] = info.Format;
            }
        }

        index["jpeg"] = ImageFormat.Jpeg;
        return index;
    }
}
