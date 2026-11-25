namespace ImageConverter.Engine;

/// <summary>Outcome of one conversion.</summary>
public enum ConversionStatus
{
    Succeeded,
    Skipped,
    Failed,
    Cancelled,
}

/// <summary>Why a conversion failed. Stable codes for UIs to localise.</summary>
public enum ConversionError
{
    None,
    InvalidOptions,
    InputNotFound,
    UnsupportedInputFormat,
    UnsupportedOutputFormat,
    DecodeFailed,
    EncodeFailed,
    OutputExists,
    OutputIsInput,
    VerificationFailed,
    IoError,
}

/// <summary>Something that was not kept exactly. Stable codes for UIs to localise.</summary>
public enum ConversionWarning
{
    /// <summary>The target format is lossy (JPEG), so pixel values change.</summary>
    LossyFormat,

    /// <summary>The source has more bits per channel than the target can store.</summary>
    BitDepthReduced,

    /// <summary>Transparent pixels were filled with the background colour.</summary>
    AlphaFlattened,

    /// <summary>EXIF/XMP/IPTC metadata could not be stored in the target format.</summary>
    MetadataDropped,

    /// <summary>Only the first frame/page of a multi-frame source was converted.</summary>
    ExtraFramesDropped,

    /// <summary>Pixels were converted to RGB because the target cannot store the source colour space (for example CMYK).</summary>
    ColorSpaceConverted,
}

/// <summary>Result of decoding the written file again and comparing it with the source pixels.</summary>
/// <param name="Identical">Every pixel and channel matched exactly.</param>
/// <param name="DifferentPixels">Number of pixels that differ (0 when identical).</param>
public sealed record PixelVerification(bool Identical, long DifferentPixels);

/// <summary>Everything a caller needs to report one conversion.</summary>
public sealed record ConversionResult
{
    public required string InputPath { get; init; }

    /// <summary>Where the file was written. Null when nothing was written.</summary>
    public string? OutputPath { get; init; }

    public required ConversionStatus Status { get; init; }

    public ImageFormat? SourceFormat { get; init; }

    public required ImageFormat TargetFormat { get; init; }

    /// <summary>True when every source pixel value is stored exactly in the output.</summary>
    public bool IsLossless { get; init; }

    public IReadOnlyList<ConversionWarning> Warnings { get; init; } = [];

    public ConversionError Error { get; init; } = ConversionError.None;

    /// <summary>Human-readable error detail (English) when <see cref="Status"/> is Failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Set when <see cref="ConversionOptions.Verify"/> was on and the target is lossless.</summary>
    public PixelVerification? Verification { get; init; }

    public TimeSpan Elapsed { get; init; }
}
