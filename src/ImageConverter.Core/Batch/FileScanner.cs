namespace ImageConverter.Core.Batch;

public sealed record ScannedFile(string Path, long Bytes, string? SourceRoot);

public static class FileScanner
{
    /// <summary>파일/폴더 경로 목록에서 지원 확장자의 파일을 모은다. 중복 경로는 한 번만.</summary>
    public static IEnumerable<ScannedFile> Scan(IEnumerable<string> paths, IReadOnlyCollection<string> extensions, bool recursive = true)
    {
        var exts = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
        };

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string full;
            try { full = Path.GetFullPath(raw); } catch { continue; }

            if (Directory.Exists(full))
            {
                foreach (var f in Directory.EnumerateFiles(full, "*", opts))
                {
                    if (!exts.Contains(Path.GetExtension(f)) || !seen.Add(f)) continue;
                    long len;
                    try { len = new FileInfo(f).Length; } catch { continue; }
                    yield return new ScannedFile(f, len, full);
                }
            }
            else if (File.Exists(full) && exts.Contains(Path.GetExtension(full)) && seen.Add(full))
            {
                long len;
                try { len = new FileInfo(full).Length; } catch { continue; }
                yield return new ScannedFile(full, len, null);
            }
        }
    }
}
