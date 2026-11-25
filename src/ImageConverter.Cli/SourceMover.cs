using ImageConverter.Engine;

namespace ImageConverter.Cli;

/// <summary>
/// Moves a converted source file into a folder (<c>--move-source-to</c>). A name already taken there
/// gets <c>name (1).ext</c>, so nothing in the folder is ever overwritten.
/// </summary>
internal static class SourceMover
{
    /// <summary>The path the source ended up at. A source already in that folder stays where it is.</summary>
    public static string MoveToFolder(string sourcePath, string folder)
    {
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var current = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? string.Empty);
        if (OutputPaths.Comparer.Equals(current, target))
        {
            return sourcePath;
        }

        Directory.CreateDirectory(target);
        var wanted = Path.Combine(target, Path.GetFileName(sourcePath));
        for (var attempt = 0; ; attempt++)
        {
            var candidate = OutputPaths.NextFree(wanted);
            try
            {
                File.Move(sourcePath, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (attempt < 20 && File.Exists(candidate) && File.Exists(sourcePath))
            {
                // Another job took that name in the meantime; pick the next free one.
            }
        }
    }
}
