namespace ImageConverter.Core.Batch;

/// <summary>변환이 끝난 원본 파일을 지정 폴더로 옮긴다. 같은 이름이 있으면 "이름 (1).확장자"처럼 번호를 붙여 덮어쓰지 않는다.</summary>
public static class SourceMover
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>옮긴 뒤의 경로. 원본이 이미 그 폴더에 있으면 옮기지 않고 원래 경로를 돌려준다.</summary>
    public static string MoveToFolder(string sourcePath, string folder)
    {
        var target = Path.GetFullPath(folder);
        var currentFolder = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? string.Empty;
        if (PathComparer.Equals(Path.TrimEndingDirectorySeparator(currentFolder), Path.TrimEndingDirectorySeparator(target)))
            return sourcePath;

        Directory.CreateDirectory(target);
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        for (var n = 0; ; n++)
        {
            var candidate = Path.Combine(target, n == 0 ? stem + ext : $"{stem} ({n}){ext}");
            if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
            try
            {
                File.Move(sourcePath, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate) && File.Exists(sourcePath))
            {
                // 그 사이 다른 작업이 같은 이름을 차지했다. 다음 번호로 다시 시도.
            }
        }
    }
}
