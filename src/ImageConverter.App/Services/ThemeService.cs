using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ImageConverter.Core.Settings;
using Microsoft.Win32;

namespace ImageConverter.App.Services;

/// <summary>
/// 라이트/다크 테마 전환. Flipix 색(Themes/Palette.*.xaml)과 WPF Fluent 기본 컨트롤 테마,
/// 창 제목 표시줄 색을 함께 바꾼다. "시스템"이면 Windows 앱 모드 설정을 따라가고, 바뀌면 바로 반영한다.
/// </summary>
public static class ThemeService
{
    private static ThemePreference _preference = ThemePreference.System;
    private static bool _listening;

    public static bool IsDark { get; private set; }

    public static void Apply(ThemePreference preference)
    {
        _preference = preference;
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
            _listening = true;
        }
        Update();
    }

    /// <summary>각 창의 SourceInitialized에서 불러 제목 표시줄 색을 맞춘다.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1 이상). 실패해도 무시한다.
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    private static void OnSystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_preference != ThemePreference.System || e.Category != UserPreferenceCategory.General) return;
        Application.Current?.Dispatcher.BeginInvoke(Update);
    }

    private static void Update()
    {
        var app = Application.Current;
        if (app is null) return;

        IsDark = _preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => SystemUsesDarkMode(),
        };

#pragma warning disable WPF0001 // Fluent ThemeMode는 .NET 9부터 들어온 미리 보기 API
        app.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        var dictionaries = app.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Palette.{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        var old = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Themes/Palette.", StringComparison.Ordinal) == true);
        if (old is null) dictionaries.Add(palette);
        else dictionaries[dictionaries.IndexOf(old)] = palette;

        foreach (Window w in app.Windows) ApplyTitleBar(w);
    }

    private static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
