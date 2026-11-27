using System.Windows;
using ImageConverter.App.Services;
using ImageConverter.Core.Localization;
using ImageConverter.Core.Settings;

namespace ImageConverter.App;

/// <summary>자주 바꾸지 않는 설정(알림, 프레임, 업데이트, 화면 언어). 저장하면 호출한 쪽이 설정 파일에 쓴다.</summary>
public partial class AdvancedSettingsWindow : Window
{
    public AdvancedSettingsWindow(AppSettings current)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        NotifyAlways.IsChecked = current.QuickMenu.Notify == QuickNotify.Always;
        NotifyFailures.IsChecked = current.QuickMenu.Notify != QuickNotify.Always;
        UpdateNotices.IsChecked = current.CheckForUpdates;
        AllFramesRadio.IsChecked = current.AllFrames;
        FirstFrame.IsChecked = !current.AllFrames;
        LanguageKorean.IsChecked = current.Language == UiLanguage.Korean;
        LanguageEnglish.IsChecked = current.Language == UiLanguage.English;
        LanguageAuto.IsChecked = current.Language == UiLanguage.Auto;
    }

    public QuickNotify Notify { get; private set; }
    public bool CheckForUpdates { get; private set; }
    public bool AllFrames { get; private set; }
    public UiLanguage Language { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Notify = NotifyAlways.IsChecked == true ? QuickNotify.Always : QuickNotify.Failures;
        CheckForUpdates = UpdateNotices.IsChecked == true;
        AllFrames = AllFramesRadio.IsChecked == true;
        Language = LanguageKorean.IsChecked == true ? UiLanguage.Korean
            : LanguageEnglish.IsChecked == true ? UiLanguage.English
            : UiLanguage.Auto;
        DialogResult = true;
    }
}
