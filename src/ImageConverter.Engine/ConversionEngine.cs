using System.Diagnostics;
using ImageMagick;

namespace ImageConverter.Engine;

/// <summary>Default <see cref="IImageConverter"/> backed by Magick.NET (Q16, so 16-bit channels stay exact).</summary>
public sealed class ConversionEngine : IImageConverter
{
    /// <inheritdoc />
    public IReadOnlyList<FormatInfo> SupportedFormats => ImageFormats.All;

    /// <inheritdoc />
    public ImageFormat? DetectFormat(string path) => FormatDetector.Detect(path);

    /// <inheritdoc />
    public async Task<ConversionResult> ConvertAsync(
        string inputPath,
        string outputPath,
        ImageFormat targetFormat,
        ConversionOptions? options = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPath);
        ArgumentNullException.ThrowIfNull(outputPath);
        var job = new ConversionJob(inputPath, outputPath, targetFormat, options ?? ConversionOptions.Default, progress, cancellationToken);
        try
        {
            return await Task.Run(job.Run, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Run() reports every expected failure itself; this is the last-resort guard for the "never throws" contract.
            return job.Fail(ConversionError.IoError, e.Message);
        }
    }

    /// <summary>One conversion. Not reused.</summary>
    private sealed class ConversionJob(
        string inputPath,
        string outputPath,
        ImageFormat target,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        CancellationToken ct)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<ConversionWarning> _warnings = [];
        private readonly FormatInfo _target = ImageFormats.Get(target);
        private ImageFormat? _source;
        private string? _tempPath;

        public ConversionResult Run()
        {
            try
            {
                return RunCore();
            }
            catch (OperationCanceledException)
            {
                return Result(ConversionStatus.Cancelled);
            }
            catch (MagickException) when (ct.IsCancellationRequested)
            {
                return Result(ConversionStatus.Cancelled);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Fail(ConversionError.IoError, e.Message);
            }
            finally
            {
                DeleteTemp();
            }
        }

        public ConversionResult Fail(ConversionError error, string message) =>
            Result(ConversionStatus.Failed) with { Error = error, ErrorMessage = message };

        private ConversionResult RunCore()
        {
            if (!_target.CanWrite)
            {
                return Fail(ConversionError.UnsupportedOutputFormat, $"Writing {_target.DisplayName} is not supported.");
            }

            if (options.JpegQuality is < 1 or > 100)
            {
                return Fail(ConversionError.InvalidOptions, "JPEG quality must be between 1 and 100.");
            }

            MagickColor background;
            try
            {
                background = new MagickColor(options.BackgroundColor);
            }
            catch (Exception e) when (e is ArgumentException or MagickException)
            {
                return Fail(ConversionError.InvalidOptions, $"Invalid background colour '{options.BackgroundColor}'.");
            }

            var input = Path.GetFullPath(inputPath);
            if (!File.Exists(input))
            {
                return Fail(ConversionError.InputNotFound, "Input file not found.");
            }

            _source = FormatDetector.Detect(input);
            if (_source is null)
            {
                return Fail(ConversionError.UnsupportedInputFormat, "Not a supported image format.");
            }

            var output = Path.GetFullPath(outputPath);
            if (OutputPaths.Comparer.Equals(input, output))
            {
                if (_source == target)
                {
                    // Already in the target format: nothing to do.
                    return Result(ConversionStatus.Skipped);
                }

                if (options.IfOutputExists != ExistingFileBehavior.Rename)
                {
                    return Fail(ConversionError.OutputIsInput, "The output path is the input file.");
                }
            }

            if (File.Exists(output))
            {
                switch (options.IfOutputExists)
                {
                    case ExistingFileBehavior.Skip:
                        return Result(ConversionStatus.Skipped) with { OutputPath = null };
                    case ExistingFileBehavior.Fail:
                        return Fail(ConversionError.OutputExists, "The output file already exists.");
                    case ExistingFileBehavior.Rename:
                        output = OutputPaths.NextFree(output);
                        break;
                }
            }

            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            _tempPath = Path.Combine(Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");

            if (_source == target)
            {
                Report(ConversionStage.Writing, 0.5);
                File.Copy(input, _tempPath);
                output = Commit(output);
                Report(ConversionStage.Done, 1);
                return Result(ConversionStatus.Succeeded) with { OutputPath = output, IsLossless = true };
            }

            using var image = new MagickImage();
            image.Progress += OnMagickProgress;

            Report(ConversionStage.Reading, 0);
            if (!TryRead(image, input, out var readError))
            {
                return ct.IsCancellationRequested
                    ? Result(ConversionStatus.Cancelled)
                    : Fail(ConversionError.DecodeFailed, readError);
            }

            ct.ThrowIfCancellationRequested();
            Report(ConversionStage.Processing, 0.4);
            Prepare(image, background);

            Report(ConversionStage.Writing, 0.5);
            try
            {
                Encode(image, _tempPath);
            }
            catch (MagickException e) when (!ct.IsCancellationRequested)
            {
                return Fail(ConversionError.EncodeFailed, e.Message);
            }

            ct.ThrowIfCancellationRequested();
            PixelVerification? verification = null;
            if (options.Verify && _target.IsLosslessOutput)
            {
                Report(ConversionStage.Verifying, 0.8);
                verification = PixelComparer.Compare(image, _tempPath, ToMagick(target));
                if (!verification.Identical)
                {
                    return Fail(ConversionError.VerificationFailed, $"{verification.DifferentPixels} pixels differ after writing.")
                        with { Verification = verification };
                }
            }

            ct.ThrowIfCancellationRequested();
            output = Commit(output);
            Report(ConversionStage.Done, 1);
            return Result(ConversionStatus.Succeeded) with
            {
                OutputPath = output,
                IsLossless = IsLossless(),
                Verification = verification,
            };
        }

        private bool TryRead(MagickImage image, string input, out string error)
        {
            error = string.Empty;
            var format = ToMagick(_source!.Value);
            try
            {
                var settings = new MagickReadSettings { Format = format };
                if (_source is ImageFormat.Tiff or ImageFormat.WebP or ImageFormat.Avif or ImageFormat.Heic)
                {
                    using var frames = new MagickImageCollection();
                    frames.Ping(input, settings);
                    if (frames.Count > 1)
                    {
                        _warnings.Add(ConversionWarning.ExtraFramesDropped);
                    }

                    settings.FrameIndex = 0;
                    settings.FrameCount = 1;
                }

                image.Read(input, settings);
                return true;
            }
            catch (MagickException e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>Applies only the changes the target format forces, and records each as a warning.</summary>
        private void Prepare(MagickImage image, MagickColor background)
        {
            if (!_target.IsLosslessOutput)
            {
                _warnings.Insert(0, ConversionWarning.LossyFormat);
            }

            if (image.ColorSpace == ColorSpace.CMYK && target is not (ImageFormat.Jpeg or ImageFormat.Tiff))
            {
                _warnings.Add(ConversionWarning.ColorSpaceConverted);
                if (image.GetColorProfile() is not null)
                {
                    image.TransformColorSpace(ColorProfiles.SRGB);
                }
                else
                {
                    image.ColorSpace = ColorSpace.sRGB;
                }
            }

            if (!options.PreserveMetadata || target == ImageFormat.Bmp)
            {
                // BMP has no orientation tag; rotating by the EXIF orientation is lossless and keeps the picture upright.
                image.AutoOrient();
            }

            if (!options.PreserveMetadata)
            {
                image.Strip();
            }
            else
            {
                var dropped = false;
                foreach (var name in ProfilesTheEncoderDrops(target))
                {
                    if (image.HasProfile(name))
                    {
                        image.RemoveProfile(name);
                        dropped = true;
                    }
                }

                if (dropped)
                {
                    _warnings.Add(ConversionWarning.MetadataDropped);
                }
            }

            if (image.HasAlpha && !_target.SupportsAlpha)
            {
                if (!image.IsOpaque)
                {
                    _warnings.Add(ConversionWarning.AlphaFlattened);
                }

                image.BackgroundColor = background;
                image.Alpha(AlphaAction.Remove);
                image.Alpha(AlphaAction.Off);
            }

            if (image.Depth > _target.MaxBitsPerChannel)
            {
                _warnings.Add(ConversionWarning.BitDepthReduced);
                image.Depth = (uint)_target.MaxBitsPerChannel;
            }
        }

        private void Encode(MagickImage image, string path)
        {
            var format = ToMagick(target);
            switch (target)
            {
                case ImageFormat.Png:
                    // zlib level 3 is ~3x faster than the default on photos for ~6% larger files; still lossless.
                    image.Settings.SetDefine(MagickFormat.Png, "compression-level", "3");
                    break;
                case ImageFormat.Jpeg:
                    image.Quality = (uint)options.JpegQuality;
                    image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:4:4");
                    break;
                case ImageFormat.Tiff:
                    image.Settings.Compression = CompressionMethod.Zip;
                    break;
                case ImageFormat.WebP:
                    image.Settings.SetDefine(MagickFormat.WebP, "lossless", "true");
                    // Keep the RGB values of fully transparent pixels instead of zeroing them.
                    image.Settings.SetDefine(MagickFormat.WebP, "exact", "true");
                    break;
                case ImageFormat.Avif:
                    // Lossless AV1 needs quality 100, no chroma subsampling and the identity (RGB) matrix.
                    image.Quality = 100;
                    image.Settings.SetDefine(MagickFormat.Heic, "chroma", "444");
                    image.Settings.SetDefine(MagickFormat.Heic, "cicp", "1/13/0/1");
                    // aom speed 8 is ~3x faster than the default with the same lossless output size.
                    image.Settings.SetDefine(MagickFormat.Heic, "speed", "8");
                    break;
            }

            image.Write(path, format);
        }

        private string Commit(string output)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(_tempPath!, output, overwrite: options.IfOutputExists == ExistingFileBehavior.Overwrite);
                    _tempPath = null;
                    return output;
                }
                catch (IOException) when (options.IfOutputExists == ExistingFileBehavior.Rename && File.Exists(output) && attempt < 10)
                {
                    // Another process took the name between our check and the move.
                    output = OutputPaths.NextFree(output);
                }
            }
        }

        private bool IsLossless() =>
            _target.IsLosslessOutput && !_warnings.Any(w => w is ConversionWarning.BitDepthReduced
                or ConversionWarning.AlphaFlattened
                or ConversionWarning.ColorSpaceConverted
                or ConversionWarning.ExtraFramesDropped);

        private void OnMagickProgress(object? sender, ProgressEventArgs e)
        {
            if (ct.IsCancellationRequested)
            {
                e.Cancel = true;
            }
        }

        private void Report(ConversionStage stage, double fraction) =>
            progress?.Report(new ConversionProgress(stage, fraction));

        private ConversionResult Result(ConversionStatus status) => new()
        {
            InputPath = inputPath,
            Status = status,
            SourceFormat = _source,
            TargetFormat = target,
            Warnings = _warnings.ToArray(),
            Elapsed = _clock.Elapsed,
        };

        private void DeleteTemp()
        {
            if (_tempPath is null)
            {
                return;
            }

            try
            {
                File.Delete(_tempPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Metadata profiles the Magick.NET encoder for <paramref name="format"/> cannot write.</summary>
    private static string[] ProfilesTheEncoderDrops(ImageFormat format) => format switch
    {
        ImageFormat.Bmp => ["exif", "xmp", "iptc", "8bim"],
        ImageFormat.Tiff => ["exif"],
        _ => [],
    };

    internal static MagickFormat ToMagick(ImageFormat format) => format switch
    {
        ImageFormat.Png => MagickFormat.Png,
        ImageFormat.Jpeg => MagickFormat.Jpeg,
        ImageFormat.Bmp => MagickFormat.Bmp,
        ImageFormat.Tiff => MagickFormat.Tiff,
        ImageFormat.WebP => MagickFormat.WebP,
        ImageFormat.Avif => MagickFormat.Avif,
        ImageFormat.Heic => MagickFormat.Heic,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
