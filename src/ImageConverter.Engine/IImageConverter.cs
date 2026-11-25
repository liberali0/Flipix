namespace ImageConverter.Engine;

/// <summary>Stage reported through <see cref="ConversionProgress"/>.</summary>
public enum ConversionStage
{
    Reading,
    Processing,
    Writing,
    Verifying,
    Done,
}

/// <summary>Progress of one conversion.</summary>
/// <param name="Stage">Current stage.</param>
/// <param name="Fraction">0.0-1.0 over the whole conversion.</param>
public sealed record ConversionProgress(ConversionStage Stage, double Fraction);

/// <summary>Converts single image files. Thread-safe: one instance can run many conversions in parallel.</summary>
public interface IImageConverter
{
    /// <summary>All formats with their capabilities.</summary>
    IReadOnlyList<FormatInfo> SupportedFormats { get; }

    /// <summary>Detects the format from the file contents (not the extension). Null when unknown or unreadable.</summary>
    ImageFormat? DetectFormat(string path);

    /// <summary>
    /// Converts <paramref name="inputPath"/> to <paramref name="targetFormat"/> and writes it to <paramref name="outputPath"/>.
    /// Never throws for per-file problems; they are reported in the result. The output is written to a temporary
    /// file first and moved into place, so a failed or cancelled conversion leaves no partial file.
    /// </summary>
    Task<ConversionResult> ConvertAsync(
        string inputPath,
        string outputPath,
        ImageFormat targetFormat,
        ConversionOptions? options = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
