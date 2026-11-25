using System.Windows;
using ImageConverter.App.Services;
using ImageConverter.Core;
using ImageConverter.Core.Engine;
using ImageConverter.Core.Settings;
using Microsoft.Win32;

namespace ImageConverter.App;

public sealed class FormatToggle : ObservableObject
{
    private bool _isChecked;
    public FormatToggle(FormatInfo format, bool isChecked) { Format = format; _isChecked = isChecked; }
    public FormatInfo Format { get; }
    public string Label => Format.Display;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

public partial class SettingsWindow : Window
{
    private readonly List<FormatToggle> _toggles;

    public SettingsWindow(QuickMenuSettings current)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        var move = current.MoveOriginal && !string.IsNullOrWhiteSpace(current.MoveOriginalTo);
        MoveOriginalRadio.IsChecked = move;
        RecycleOriginal.IsChecked = !move && current.DeleteOriginal;
        KeepOriginal.IsChecked = !move && !current.DeleteOriginal;
        MoveFolder.Text = current.MoveOriginalTo ?? "";
        var set = new HashSet<string>(current.Formats, StringComparer.OrdinalIgnoreCase);
        _toggles = FormatCatalog.All.Select(f => new FormatToggle(f, set.Contains(f.Id))).ToList();
        FormatList.ItemsSource = _toggles;
    }

    /// <summary>선택된 포맷 id. 카탈로그 순서를 따른다.</summary>
    public IReadOnlyList<string> SelectedFormats { get; private set; } = Array.Empty<string>();

    /// <summary>빠른 변환 후 원본을 휴지통으로 보낼지.</summary>
    public bool DeleteOriginal { get; private set; }

    /// <summary>빠른 변환 후 원본을 <see cref="MoveOriginalTo"/>로 옮길지.</summary>
    public bool MoveOriginal { get; private set; }

    /// <summary>원본을 옮길 폴더. 옮기기를 고르지 않아도 입력한 값은 기억한다.</summary>
    public string? MoveOriginalTo { get; private set; }

    private void OnBrowseMoveFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "변환이 끝난 원본을 옮길 폴더" };
        if (dlg.ShowDialog(this) == true)
        {
            MoveFolder.Text = dlg.FolderName;
            MoveOriginalRadio.IsChecked = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SelectedFormats = _toggles.Where(t => t.IsChecked).Select(t => t.Format.Id).ToList();
        if (SelectedFormats.Count == 0)
        {
            MessageBox.Show(this, "하나 이상 선택하세요.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var folder = MoveFolder.Text.Trim();
        MoveOriginal = MoveOriginalRadio.IsChecked == true;
        if (MoveOriginal && (folder.Length == 0 || !System.IO.Path.IsPathFullyQualified(folder)))
        {
            MessageBox.Show(this, "원본을 옮길 폴더를 전체 경로(예: D:\\원본 보관)로 지정하세요.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DeleteOriginal = RecycleOriginal.IsChecked == true;
        MoveOriginalTo = folder.Length == 0 ? null : folder;
        DialogResult = true;
    }
}
