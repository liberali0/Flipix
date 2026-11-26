using System.Text.Json;
using ImageConverter.Core.Batch;

namespace ImageConverter.Core.Settings;

/// <summary>우클릭 셸 확장이 읽는 섹션. 키 이름/형태는 셸 확장(src/ShellExtension)과 맞춰야 한다.</summary>
public sealed class QuickMenuSettings
{
    /// <summary>"빠른 이미지 포맷 변환" 하위 메뉴에 표시할 포맷 id(소문자). 순서가 메뉴 순서.</summary>
    public List<string> Formats { get; set; } = new() { "png", "jpg", "bmp" };
    public bool ShowDetails { get; set; } = true;
    /// <summary>true면 우클릭 빠른 변환이 성공한 원본을 휴지통으로 보낸다(imgconv --delete-source). 기본은 남김.</summary>
    public bool DeleteOriginal { get; set; }
    /// <summary>true이고 <see cref="MoveOriginalTo"/>가 있으면 성공한 원본을 그 폴더로 옮긴다(imgconv --move-source-to). DeleteOriginal보다 우선.</summary>
    public bool MoveOriginal { get; set; }
    /// <summary>원본을 옮길 폴더(전체 경로). 옮기기를 꺼도 다음에 쓰도록 기억한다.</summary>
    public string? MoveOriginalTo { get; set; }
}

/// <summary>Flipix Workshop 화면 테마. System은 Windows 앱 모드(라이트/다크)를 따른다.</summary>
public enum ThemePreference { System, Light, Dark }

/// <summary>Flipix Workshop(앱)에서 변환이 끝난 원본 파일을 어떻게 할지.</summary>
public enum OriginalFileAction { Keep, Move }

/// <summary>앱과 우클릭 메뉴가 함께 읽는 사용자 설정. 셸 확장은 quickMenu 섹션만 읽는다.</summary>
public sealed class AppSettings
{
    /// <summary>우클릭 "빠른 이미지 포맷 변환" 하위 메뉴에 표시할 포맷 id (순서 유지).</summary>
    public QuickMenuSettings QuickMenu { get; set; } = new();
    public string DefaultTargetFormat { get; set; } = "png";
    /// <summary>Flipix Workshop 화면 테마. 기본은 Windows 설정을 따른다.</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    /// <summary>0이면 자동(코어 수의 절반).</summary>
    public int MaxParallelism { get; set; } = 0;
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Rename;
    public string? CustomOutputFolder { get; set; }
    public bool PreserveStructure { get; set; } = true;
    /// <summary>Flipix Workshop: 변환에 성공한 원본 처리. 기본은 그대로 둔다.</summary>
    public OriginalFileAction OriginalAction { get; set; } = OriginalFileAction.Keep;
    /// <summary>Flipix Workshop: 원본을 옮길 폴더(전체 경로).</summary>
    public string? OriginalMoveFolder { get; set; }

    public int EffectiveParallelism =>
        MaxParallelism > 0 ? MaxParallelism : Math.Max(1, Environment.ProcessorCount / 2);
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public SettingsStore(string? path = null) => Path = path ?? DefaultPath;

    public string Path { get; }

    /// <summary>%LOCALAPPDATA%\QuickImageConverter\settings.json</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickImageConverter", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppSettings();
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Json) ?? new AppSettings();
            s.QuickMenu ??= new QuickMenuSettings();
            s.QuickMenu.Formats ??= new QuickMenuSettings().Formats;
            return s;
        }
        catch
        {
            return new AppSettings(); // 손상된 파일은 기본값으로
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, Path, overwrite: true);
    }
}
