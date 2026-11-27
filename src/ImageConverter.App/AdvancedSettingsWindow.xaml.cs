using System.Windows;
using ImageConverter.App.Services;
using ImageConverter.Core.Settings;

namespace ImageConverter.App;

/// <summary>자주 바꾸지 않는 설정(알림, 업데이트). 저장하면 호출한 쪽이 설정 파일에 쓴다.</summary>
public partial class AdvancedSettingsWindow : Window
{
    public AdvancedSettingsWindow(AppSettings current)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        NotifyAlways.IsChecked = current.QuickMenu.Notify == QuickNotify.Always;
        NotifyFailures.IsChecked = current.QuickMenu.Notify != QuickNotify.Always;
        UpdateNotices.IsChecked = current.CheckForUpdates;
    }

    public QuickNotify Notify { get; private set; }
    public bool CheckForUpdates { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Notify = NotifyAlways.IsChecked == true ? QuickNotify.Always : QuickNotify.Failures;
        CheckForUpdates = UpdateNotices.IsChecked == true;
        DialogResult = true;
    }
}
