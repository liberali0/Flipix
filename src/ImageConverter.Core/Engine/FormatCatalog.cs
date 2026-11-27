using ImageConverter.Core.Localization;

namespace ImageConverter.Core.Engine;

/// <summary>변환 대상(출력) 포맷 정보. HEIC는 입력 전용, GIF는 무손실 보장이 안 되어 제외. 우클릭 메뉴/앱 UI가 같은 목록을 쓴다.</summary>
public sealed record FormatInfo(string Id, string Extension, string KoreanDisplay, bool IsLossy, string? EnglishDisplay = null)
{
    /// <summary>화면에 보이는 이름(현재 화면 언어).</summary>
    public string Display => Loc.T(KoreanDisplay, EnglishDisplay ?? KoreanDisplay);
}

public static class FormatCatalog
{
    public static IReadOnlyList<FormatInfo> All { get; } = new[]
    {
        new FormatInfo("png",  ".png",  "PNG",  false),
        new FormatInfo("jpg",  ".jpg",  "JPG (손실 압축)", true, "JPG (lossy)"),
        new FormatInfo("bmp",  ".bmp",  "BMP",  false),
        new FormatInfo("webp", ".webp", "WEBP (무손실)", false, "WEBP (lossless)"),
        new FormatInfo("avif", ".avif", "AVIF (무손실)", false, "AVIF (lossless)"),
        new FormatInfo("tiff", ".tif",  "TIFF", false),
    };

    public static FormatInfo Get(string id) =>
        All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"지원하지 않는 포맷: {id}", nameof(id));

    public static FormatInfo? TryGet(string id) =>
        All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>엔진이 아직 없을 때 UI가 쓰는 기본 입력 확장자.</summary>
    public static IReadOnlyCollection<string> DefaultInputExtensions { get; } = new[]
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif",
    };
}
