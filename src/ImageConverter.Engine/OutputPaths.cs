namespace ImageConverter.Engine;

/// <summary>Output file naming shared by the CLI, the right-click menu and the app.</summary>
public static class OutputPaths
{
    /// <summary>
    /// <c>folder\base.ext</c> where folder is <paramref name="outputDirectory"/> or the input's folder,
    /// and ext is the target's default extension.
    /// </summary>
    public static string ForTarget(string inputPath, ImageFormat target, string? outputDirectory = null)
    {
        var folder = outputDirectory ?? Path.GetDirectoryName(inputPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(inputPath) + ImageFormats.Get(target).Extensions[0];
        return Path.Combine(folder, name);
    }

    /// <summary>Path equality as the file system sees it (case-insensitive on Windows).</summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Returns <paramref name="path"/> if free, else <c>name (1).ext</c>, <c>name (2).ext</c>, ...
    /// A name is taken when it exists on disk or <paramref name="isTaken"/> says so (e.g. planned outputs of a batch).
    /// </summary>
    public static string NextFree(string path, Func<string, bool>? isTaken = null)
    {
        bool Taken(string p) => File.Exists(p) || Directory.Exists(p) || (isTaken?.Invoke(p) ?? false);

        if (!Taken(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({n}){ext}");
            if (!Taken(candidate))
            {
                return candidate;
            }
        }
    }
}
