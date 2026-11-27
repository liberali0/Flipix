namespace ImageConverter.Engine;

/// <summary>What to do when the output path already exists.</summary>
public enum ExistingFileBehavior
{
    /// <summary>Write to <c>name (1).ext</c>, <c>name (2).ext</c>, ... instead. The default.</summary>
    Rename,

    /// <summary>Replace the existing file (never the input file itself).</summary>
    Overwrite,

    /// <summary>Do nothing and report <see cref="ConversionStatus.Skipped"/>.</summary>
    Skip,

    /// <summary>Report <see cref="ConversionStatus.Failed"/> with <see cref="ConversionError.OutputExists"/>.</summary>
    Fail,
}

/// <summary>Options for one conversion. Defaults favour quality: nothing is lost unless the target format forces it.</summary>
public sealed record ConversionOptions
{
    /// <summary>Shared default instance.</summary>
    public static ConversionOptions Default { get; } = new();

    /// <summary>JPEG quality 1-100. Default 100. JPEG is always written with 4:4:4 (no chroma subsampling).</summary>
    public int JpegQuality { get; init; } = 100;

    /// <summary>Keep EXIF, XMP, IPTC and ICC data where the target format can hold it. Default true.</summary>
    public bool PreserveMetadata { get; init; } = true;

    /// <summary>After writing a lossless format, decode the output again and compare every pixel. Default false.</summary>
    public bool Verify { get; init; }

    /// <summary>Colour used to fill transparent areas when the target cannot store alpha (JPEG). Default white.</summary>
    public string BackgroundColor { get; init; } = "#FFFFFF";

    /// <summary>Behaviour when the output file exists. Default <see cref="ExistingFileBehavior.Rename"/>.</summary>
    public ExistingFileBehavior IfOutputExists { get; init; } = ExistingFileBehavior.Rename;

    /// <summary>
    /// For animated GIF/WebP: write every frame (as it appears on screen) into a folder
    /// <c>&lt;name&gt;_frames</c> next to the output, as <c>&lt;name&gt;_001.png</c>, ... Default false: the first frame only.
    /// </summary>
    public bool AllFrames { get; init; }

    /// <summary>Shrink (never enlarge) so that the longer side is at most this many pixels, keeping the aspect ratio. Null: keep the size.</summary>
    public int? MaxSize { get; init; }

    /// <summary>Shrink both sides to this percentage (1-99). Null or 100: keep the size. Ignored when <see cref="MaxSize"/> is set.</summary>
    public int? ScalePercent { get; init; }
}
