using ImageConverter.Core.Engine;

namespace ImageConverter.Core.Batch;

/// <summary>배치 하나 동안 출력 경로를 정한다. 같은 배치 안의 이름 충돌도 막는다.</summary>
public sealed class OutputPathResolver
{
    private readonly OutputOptions _options;
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private readonly HashSet<string> _batchInputs = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="batchInputs">이 배치의 모든 입력 파일. 덮어쓰기 정책에서도 이 파일들은 지키지 않는다.</param>
    public OutputPathResolver(OutputOptions options, IEnumerable<string>? batchInputs = null)
    {
        _options = options;
        if (batchInputs != null)
            foreach (var i in batchInputs) _batchInputs.Add(Path.GetFullPath(i));
    }

    /// <returns>출력 경로. 충돌 정책이 Skip이라 건너뛰어야 하면 null.</returns>
    public string? Resolve(string inputPath, string? sourceRoot, string targetFormat)
    {
        var ext = FormatCatalog.Get(targetFormat).Extension;
        var input = Path.GetFullPath(inputPath);
        var dir = ResolveDirectory(input, sourceRoot);
        var name = Path.GetFileNameWithoutExtension(input);
        var candidate = Path.Combine(dir, name + ext);

        lock (_lock)
        {
            bool isSource = Same(candidate, input) || _batchInputs.Contains(Path.GetFullPath(candidate));
            bool reserved = _reserved.Contains(candidate);
            bool exists = File.Exists(candidate);

            if (!isSource && !reserved && !exists) return Reserve(candidate);

            switch (_options.Conflict)
            {
                case ConflictPolicy.Skip:
                    return null;
                case ConflictPolicy.Overwrite when !isSource && !reserved:
                    return Reserve(candidate);
                default:
                    for (int n = 1; ; n++)
                    {
                        var alt = Path.Combine(dir, $"{name} ({n}){ext}");
                        if (!_batchInputs.Contains(Path.GetFullPath(alt)) && !_reserved.Contains(alt) && !File.Exists(alt))
                            return Reserve(alt);
                    }
            }
        }
    }

    private string ResolveDirectory(string input, string? sourceRoot)
    {
        var inputDir = Path.GetDirectoryName(input)!;
        if (string.IsNullOrWhiteSpace(_options.CustomFolder)) return inputDir;
        var baseDir = Path.GetFullPath(_options.CustomFolder);
        if (_options.PreserveStructure && sourceRoot != null)
        {
            var rel = Path.GetRelativePath(sourceRoot, inputDir);
            bool outside = rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar) || rel.StartsWith(".." + Path.AltDirectorySeparatorChar) || Path.IsPathRooted(rel);
            if (rel != "." && !outside)
                return Path.GetFullPath(Path.Combine(baseDir, rel));
        }
        return baseDir;
    }

    private string Reserve(string path) { _reserved.Add(path); return path; }

    private static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
