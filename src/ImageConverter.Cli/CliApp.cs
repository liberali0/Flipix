using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageConverter.Engine;

namespace ImageConverter.Cli;

/// <summary>The <c>imgconv</c> command line. See docs/cli.md for the contract.</summary>
internal static class CliApp
{
    public const int ExitOk = 0;
    public const int ExitSomeFailed = 1;
    public const int ExitUsage = 2;
    public const int ExitCancelled = 130;

    private const string Usage = """
        Usage:
          imgconv convert --to <png|jpg|bmp|tiff|webp|avif> [options] <file | @listfile>...
          imgconv formats [--json]
          imgconv detect [--json] <file>...
          imgconv --version
        Options for convert:
          --out-dir <dir>           write outputs to this folder (default: next to each source)
          --if-exists <mode>        rename (default) | overwrite | skip | fail
          --jpeg-quality <1-100>    default 100
          --no-metadata             drop EXIF/XMP/IPTC/ICC
          --verify                  re-read lossless outputs and compare every pixel
          --all-frames              animated GIF/WebP: write every frame into <name>_frames\
                                    (default: first frame only; the original is then always kept)
          --delete-source           move each successfully converted source to the Recycle Bin
                                    (turns on --verify for lossless targets)
          --move-source-to <dir>    move each successfully converted source into this folder
                                    (name (1).ext when the name is taken; never overwrites)
          --parallel <n>            files converted at once (default: half the CPU cores)
          --json                    one JSON object per line on stdout
        """;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep Korean and other non-ASCII paths readable instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            return UsageError(stderr, "No command given.");
        }

        var rest = args[1..];
        try
        {
            return args[0] switch
            {
                "convert" => await ConvertAsync(rest, stdout, stderr, ct).ConfigureAwait(false),
                "formats" => Formats(rest, stdout, stderr),
                "detect" => Detect(rest, stdout, stderr),
                "--version" or "-v" => Version(stdout),
                "--help" or "-h" or "help" => Help(stdout),
                _ => UsageError(stderr, $"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException e)
        {
            return UsageError(stderr, e.Message);
        }
    }

    private static async Task<int> ConvertAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var request = ConvertRequest.Parse(args);
        var engine = new ConversionEngine();
        var clock = Stopwatch.StartNew();
        var results = new List<ConversionResult>();
        var outputLock = new object();
        var deleteFailures = 0;

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = request.Parallel, CancellationToken = CancellationToken.None };
        await Parallel.ForEachAsync(PlanOutputs(request), parallel, async (job, _) =>
        {
            var (input, output) = job;
            var result = await engine.ConvertAsync(input, output, request.Target, request.Options, cancellationToken: ct).ConfigureAwait(false);
            var (sourceDeleted, deleteError) = request.DeleteSource ? DeleteSource(result) : (false, null);
            string? movedTo = null;
            if (request.MoveSourceTo is not null)
            {
                (movedTo, deleteError) = MoveSource(result, request.MoveSourceTo);
            }

            lock (outputLock)
            {
                results.Add(result);
                WriteResult(result, sourceDeleted, movedTo, request.Json, stdout, stderr);
                if (deleteError is not null)
                {
                    deleteFailures++;
                    stderr.WriteLine($"ERROR {result.InputPath}: Converted to {result.OutputPath}, but the original was kept: {OneLine(deleteError)}");
                }
            }
        }).ConfigureAwait(false);

        var summary = new
        {
            Type = "summary",
            Total = results.Count,
            Succeeded = results.Count(r => r.Status == ConversionStatus.Succeeded),
            Skipped = results.Count(r => r.Status == ConversionStatus.Skipped),
            Failed = results.Count(r => r.Status == ConversionStatus.Failed),
            Cancelled = results.Count(r => r.Status == ConversionStatus.Cancelled),
            SourceDeleteFailed = deleteFailures,
            ElapsedMs = (long)clock.Elapsed.TotalMilliseconds,
        };
        if (request.Json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(summary, Json));
        }

        stdout.Flush();
        stderr.Flush();
        if (ct.IsCancellationRequested)
        {
            return ExitCancelled;
        }

        return summary.Failed > 0 || deleteFailures > 0 ? ExitSomeFailed : ExitOk;
    }

    /// <summary>
    /// The source goes only when its conversion fully succeeded into a separate file that exists.
    /// Skipped (already in the target format), failed and cancelled files always stay.
    /// </summary>
    private static (bool Deleted, string? Error) DeleteSource(ConversionResult r)
    {
        if (!SourceCanGo(r))
        {
            return (false, null);
        }

        try
        {
            var error = SourceRecycler.Recycle(r.InputPath);
            return (error is null, error);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ExternalException)
        {
            return (false, e.Message);
        }
    }

    /// <summary>
    /// A fully converted source with a separate output (file, or frames folder) that exists. A source whose extra
    /// frames were dropped (first frame of an animation) stays, because the output does not hold all of it.
    /// </summary>
    private static bool SourceCanGo(ConversionResult r) =>
        r.Status == ConversionStatus.Succeeded && r.OutputPath is not null
        && !OutputPaths.Comparer.Equals(r.InputPath, r.OutputPath)
        && (File.Exists(r.OutputPath) || (r.FrameCount > 0 && Directory.Exists(r.OutputPath)))
        && !r.Warnings.Contains(ConversionWarning.ExtraFramesDropped);

    /// <summary>Same rules as <see cref="DeleteSource"/>: only a fully converted source with a separate output moves.</summary>
    private static (string? MovedTo, string? Error) MoveSource(ConversionResult r, string folder)
    {
        if (!SourceCanGo(r))
        {
            return (null, null);
        }

        try
        {
            return (SourceMover.MoveToFolder(r.InputPath, folder), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, e.Message);
        }
    }

    /// <summary>
    /// Gives every input its own output name up front, so parallel jobs never write the same file and
    /// no output (even with --if-exists overwrite) lands on another input of the batch.
    /// </summary>
    private static List<(string Input, string Output)> PlanOutputs(ConvertRequest request)
    {
        var inputs = new HashSet<string>(request.Inputs, OutputPaths.Comparer);
        var planned = new HashSet<string>(OutputPaths.Comparer);
        var jobs = new List<(string, string)>();
        foreach (var input in request.Inputs)
        {
            var output = OutputPaths.ForTarget(input, request.Target, request.OutDir);
            if (!OutputPaths.Comparer.Equals(input, output) && (inputs.Contains(output) || planned.Contains(output)))
            {
                output = OutputPaths.NextFree(output, p => inputs.Contains(p) || planned.Contains(p));
            }

            planned.Add(output);
            jobs.Add((input, output));
        }

        return jobs;
    }

    private static void WriteResult(ConversionResult r, bool sourceDeleted, string? movedTo, bool json, TextWriter stdout, TextWriter stderr)
    {
        if (r.Status == ConversionStatus.Failed)
        {
            stderr.WriteLine($"ERROR {r.InputPath}: {OneLine(r.ErrorMessage ?? r.Error.ToString())}");
        }

        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new
            {
                Type = "result",
                Input = r.InputPath,
                Output = r.OutputPath,
                r.Status,
                SourceFormat = r.SourceFormat is { } s ? ImageFormats.Get(s).Id : null,
                TargetFormat = ImageFormats.Get(r.TargetFormat).Id,
                Lossless = r.IsLossless,
                Frames = r.FrameCount > 0 ? r.FrameCount : (int?)null,
                r.Warnings,
                Error = r.Error == ConversionError.None ? null : r.Error.ToString(),
                Message = r.ErrorMessage,
                Verified = r.Verification?.Identical,
                SourceDeleted = sourceDeleted,
                SourceMovedTo = movedTo,
                ElapsedMs = (long)r.Elapsed.TotalMilliseconds,
            }, Json));
            return;
        }

        switch (r.Status)
        {
            case ConversionStatus.Succeeded:
                var warnings = r.Warnings.Count > 0 ? $" [warnings: {string.Join(", ", r.Warnings)}]" : string.Empty;
                var verified = r.Verification?.Identical == true ? " [verified]" : string.Empty;
                var deleted = sourceDeleted ? " [source recycled]" : movedTo is not null ? $" [source moved to {movedTo}]" : string.Empty;
                var frames = r.FrameCount > 0 ? $" [{r.FrameCount} frames]" : string.Empty;
                stdout.WriteLine($"OK {r.InputPath} -> {r.OutputPath}{frames}{verified}{warnings}{deleted}");
                break;
            case ConversionStatus.Skipped:
                stdout.WriteLine($"SKIPPED {r.InputPath}");
                break;
            case ConversionStatus.Cancelled:
                stdout.WriteLine($"CANCELLED {r.InputPath}");
                break;
        }
    }

    private static int Formats(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var json = ParseFlagsOnly(args, "--json");
        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(ImageFormats.All, Json));
            return ExitOk;
        }

        foreach (var f in ImageFormats.All)
        {
            var write = !f.CanWrite ? "read-only" : f.IsLosslessOutput ? "lossless" : "lossy";
            stdout.WriteLine($"{f.Id,-5} {f.DisplayName,-5} {string.Join(" ", f.Extensions),-24} {write}");
        }

        return ExitOk;
    }

    private static int Detect(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var json = args.Contains("--json");
        var files = args.Where(a => a != "--json").ToArray();
        if (files.Length == 0 || files.Any(f => f.StartsWith("--", StringComparison.Ordinal)))
        {
            throw new UsageException("detect needs one or more files.");
        }

        var engine = new ConversionEngine();
        foreach (var file in files)
        {
            var id = engine.DetectFormat(file) is { } f ? ImageFormats.Get(f).Id : "unknown";
            stdout.WriteLine(json ? JsonSerializer.Serialize(new { File = file, Format = id }, Json) : $"{file}: {id}");
        }

        return ExitOk;
    }

    private static int Version(TextWriter stdout)
    {
        var version = typeof(CliApp).Assembly.GetName().Version;
        stdout.WriteLine($"imgconv {version} (Magick.NET {typeof(ImageMagick.MagickNET).Assembly.GetName().Version})");
        return ExitOk;
    }

    private static int Help(TextWriter stdout)
    {
        stdout.WriteLine(Usage);
        return ExitOk;
    }

    private static int UsageError(TextWriter stderr, string message)
    {
        stderr.WriteLine($"imgconv: {message}");
        stderr.WriteLine(Usage);
        return ExitUsage;
    }

    private static bool ParseFlagsOnly(string[] args, string flag)
    {
        foreach (var a in args)
        {
            if (a != flag)
            {
                throw new UsageException($"Unknown option '{a}'.");
            }
        }

        return args.Length > 0;
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();

    private sealed class UsageException(string message) : Exception(message);

    private sealed record ConvertRequest(ImageFormat Target, IReadOnlyList<string> Inputs, string? OutDir, ConversionOptions Options, int Parallel, bool Json, bool DeleteSource, string? MoveSourceTo)
    {
        public static ConvertRequest Parse(string[] args)
        {
            ImageFormat? target = null;
            string? outDir = null;
            var options = new ConversionOptions();
            var parallel = Math.Max(1, Environment.ProcessorCount / 2);
            var json = false;
            var deleteSource = false;
            string? moveSourceTo = null;
            var inputs = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string Value() => i + 1 < args.Length ? args[++i] : throw new UsageException($"{arg} needs a value.");

                switch (arg)
                {
                    case "--to":
                        var id = Value();
                        if (!ImageFormats.TryParse(id, out var f))
                        {
                            throw new UsageException($"Unknown format '{id}'.");
                        }

                        if (!ImageFormats.Get(f).CanWrite)
                        {
                            throw new UsageException($"{ImageFormats.Get(f).DisplayName} can be read but not written. Try avif.");
                        }

                        target = f;
                        break;
                    case "--out-dir":
                        outDir = Path.GetFullPath(Value());
                        break;
                    case "--if-exists":
                        var mode = Value();
                        options = options with
                        {
                            IfOutputExists = mode.ToLowerInvariant() switch
                            {
                                "rename" => ExistingFileBehavior.Rename,
                                "overwrite" => ExistingFileBehavior.Overwrite,
                                "skip" => ExistingFileBehavior.Skip,
                                "fail" => ExistingFileBehavior.Fail,
                                _ => throw new UsageException($"--if-exists must be rename, overwrite, skip or fail, not '{mode}'."),
                            },
                        };
                        break;
                    case "--jpeg-quality":
                        var q = Value();
                        if (!int.TryParse(q, out var quality) || quality is < 1 or > 100)
                        {
                            throw new UsageException($"--jpeg-quality must be 1-100, not '{q}'.");
                        }

                        options = options with { JpegQuality = quality };
                        break;
                    case "--parallel":
                        var p = Value();
                        if (!int.TryParse(p, out parallel) || parallel < 1)
                        {
                            throw new UsageException($"--parallel must be a positive number, not '{p}'.");
                        }

                        break;
                    case "--no-metadata":
                        options = options with { PreserveMetadata = false };
                        break;
                    case "--verify":
                        options = options with { Verify = true };
                        break;
                    case "--all-frames":
                        options = options with { AllFrames = true };
                        break;
                    case "--json":
                        json = true;
                        break;
                    case "--delete-source":
                        deleteSource = true;
                        break;
                    case "--move-source-to":
                        var dir = Value();
                        if (string.IsNullOrWhiteSpace(dir))
                        {
                            throw new UsageException("--move-source-to needs a folder.");
                        }

                        moveSourceTo = Path.GetFullPath(dir);
                        break;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new UsageException($"Unknown option '{arg}'.");
                        }

                        if (arg.StartsWith('@'))
                        {
                            inputs.AddRange(ReadListFile(arg[1..]));
                        }
                        else
                        {
                            inputs.Add(arg);
                        }

                        break;
                }
            }

            if (target is null)
            {
                throw new UsageException("Missing --to <format>.");
            }

            if (inputs.Count == 0)
            {
                throw new UsageException("No input files.");
            }

            if (deleteSource && moveSourceTo is not null)
            {
                throw new UsageException("Use either --delete-source or --move-source-to, not both.");
            }

            if (deleteSource)
            {
                // Before an original is thrown away, prove every pixel made it into the new file.
                options = options with { Verify = true };
            }

            return new ConvertRequest(target.Value, inputs.Select(Path.GetFullPath).ToArray(), outDir, options, parallel, json, deleteSource, moveSourceTo);
        }

        private static IEnumerable<string> ReadListFile(string path)
        {
            try
            {
                // UTF-8 with or without BOM; the BOM is stripped by the decoder.
                return File.ReadAllLines(path, Encoding.UTF8)
                    .Select(line => line.Trim().Trim('"').Trim())
                    .Where(line => line.Length > 0)
                    .ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new UsageException($"Cannot read list file '{path}': {e.Message}");
            }
        }
    }
}
