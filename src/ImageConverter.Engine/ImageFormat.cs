namespace ImageConverter.Engine;

/// <summary>Image formats the engine knows about.</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Bmp,
    Tiff,
    WebP,
    Avif,

    /// <summary>HEIC/HEIF (HEVC-coded). Readable; not writable (no HEVC encoder is bundled).</summary>
    Heic,

    /// <summary>GIF, including animations. Readable only (GIF stores at most 256 colours, so it is not an output).</summary>
    Gif,
}

/// <summary>Static facts about a format, for menus and settings screens.</summary>
/// <param name="Format">The format.</param>
/// <param name="Id">Lower-case id used on the command line, e.g. <c>png</c>, <c>jpg</c>.</param>
/// <param name="DisplayName">Short human name, e.g. <c>JPG</c>.</param>
/// <param name="Extensions">File extensions with a leading dot, the first one is the default for output.</param>
/// <param name="CanRead">The engine can decode this format.</param>
/// <param name="CanWrite">The engine can encode this format.</param>
/// <param name="IsLosslessOutput">Writing to this format keeps every pixel value (within <paramref name="MaxBitsPerChannel"/>).</param>
/// <param name="SupportsAlpha">The format can store transparency.</param>
/// <param name="MaxBitsPerChannel">Highest bit depth per channel the engine writes for this format.</param>
/// <param name="SupportsMetadata">The format can carry EXIF/XMP metadata.</param>
public sealed record FormatInfo(
    ImageFormat Format,
    string Id,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    bool CanRead,
    bool CanWrite,
    bool IsLosslessOutput,
    bool SupportsAlpha,
    int MaxBitsPerChannel,
    bool SupportsMetadata);
