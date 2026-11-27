using System.Windows;
using ImageConverter.App.Services;
using ImageConverter.App.ViewModels;

namespace ImageConverter.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
    }

    // 더보기(⋯) 단추: 왼쪽 클릭으로 메뉴를 연다. 메뉴는 창과 다른 트리라 DataContext를 넘겨 준다.
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            menu.DataContext = DataContext;
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && DataContext is MainViewModel vm)
                await vm.AddPathsAsync(paths);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
