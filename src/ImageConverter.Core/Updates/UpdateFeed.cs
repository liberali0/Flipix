using System.Text.Json;
using System.Text.RegularExpressions;

namespace ImageConverter.Core.Updates;

/// <summary>GitHub 최신 릴리스에서 찾은 새 버전.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string SetupName, Uri SetupUrl, Uri ChecksumsUrl, Uri PageUrl);

/// <summary>
/// Flipix 릴리스(liberali0/Flipix) 정보를 읽는다. 네트워크는 쓰지 않고 응답 문자열만 해석해서 테스트할 수 있게 둔다.
/// 보안: 이 저장소 릴리스의 다운로드 주소만 받아들이고, 설치 파일은 같은 릴리스의 SHA256SUMS.txt 해시와 맞아야 쓴다.
/// </summary>
public static class UpdateFeed
{
    public static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/liberali0/Flipix/releases/latest");
    public const string ReleasesPage = "https://github.com/liberali0/Flipix/releases";
    private const string DownloadPrefix = "https://github.com/liberali0/Flipix/releases/download/";
    public const string ChecksumsName = "SHA256SUMS.txt";

    private static readonly Regex TagPattern = new(@"^v?(\d+)\.(\d+)\.(\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex SetupPattern = new(@"^Flipix-Setup-(\d+\.\d+\.\d+)\.exe$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex HashPattern = new(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (tag is null) return false;
        var m = TagPattern.Match(tag.Trim());
        if (!m.Success) return false;
        try
        {
            version = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            return true;
        }
        catch (OverflowException) { return false; }
    }

    /// <summary>latest가 current보다 새 버전인지. 네 번째 자리(빌드 번호)는 보지 않는다.</summary>
    public static bool IsNewer(Version latest, Version? current)
    {
        if (current is null) return false; // 내 버전을 모르면 알리지 않는다(개발 빌드 등)
        var cur = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        var lat = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));
        return lat > cur;
    }

    /// <summary>GitHub "latest release" 응답에서 설치 파일과 해시 목록을 찾는다. 둘 중 하나라도 없으면 null.</summary>
    public static UpdateInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;
        var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (!TryParseVersion(tag, out var version)) return null;

        string? setupName = null;
        Uri? setupUrl = null, sumsUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var url = a.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                if (name is null || !IsTrustedDownload(url, tag!, name, out var uri)) continue;
                var m = SetupPattern.Match(name);
                if (m.Success && TryParseVersion(m.Groups[1].Value, out var fileVersion) && fileVersion == version)
                {
                    setupName = name;
                    setupUrl = uri;
                }
                else if (name == ChecksumsName)
                {
                    sumsUrl = uri;
                }
            }
        }
        if (setupName is null || setupUrl is null || sumsUrl is null) return null;
        return new UpdateInfo(version, tag!, setupName, setupUrl, sumsUrl, new Uri($"{ReleasesPage}/tag/{tag}"));
    }

    /// <summary>SHA256SUMS.txt("해시  파일이름" 줄들)에서 파일의 해시(소문자)를 찾는다.</summary>
    public static string? FindHash(string checksums, string fileName)
    {
        foreach (var raw in checksums.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOfAny(new[] { ' ', '\t' });
            if (space <= 0) continue;
            var hash = line[..space];
            var name = line[space..].Trim().TrimStart('*');
            if (name == fileName && HashPattern.IsMatch(hash)) return hash.ToLowerInvariant();
        }
        return null;
    }

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    // 이 저장소, 이 태그, 이 파일이름의 다운로드 주소만 믿는다.
    private static bool IsTrustedDownload(string? url, string tag, string name, out Uri uri)
    {
        uri = null!;
        if (url is null || url != DownloadPrefix + tag + "/" + name) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps;
    }
}
