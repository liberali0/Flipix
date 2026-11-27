using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ImageConverter.App;

/// <summary>오른쪽 아래 작은 알림. 여러 개면 위로 쌓는다.</summary>
public partial class NoticeWindow : Window
{
    private static double _stacked;
    private readonly TaskCompletionSource _closed = new();
    private DispatcherTimer? _timer;
    private double _height;

    public NoticeWindow(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        Loaded += (_, _) => Place();
        Closed += (_, _) => { _stacked = Math.Max(0, _stacked - _height); _closed.TrySetResult(); };
        // 마우스를 올려 두면 자동으로 닫히지 않는다.
        MouseEnter += (_, _) => _timer?.Stop();
        MouseLeave += (_, _) => _timer?.Start();
    }

    public string Message { get => MessageText.Text; set => MessageText.Text = value; }

    public Button AddButton(string text, bool primary, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(primary ? "Flx.PrimaryButton" : "Flx.Button"),
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(0, 0, 8, 0),
        };
        button.Click += (_, _) => onClick();
        Buttons.Children.Add(button);
        Buttons.Visibility = Visibility.Visible;
        return button;
    }

    public void SetButtonsEnabled(bool enabled)
    {
        foreach (var child in Buttons.Children.OfType<Button>()) child.IsEnabled = enabled;
    }

    public void CloseAfter(TimeSpan delay)
    {
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        _timer.Start();
    }

    /// <summary>띄우고, 닫힐 때 끝나는 작업을 돌려준다.</summary>
    public Task ShowNotice()
    {
        Show();
        return _closed.Task;
    }

    private void Place()
    {
        var area = SystemParameters.WorkArea;
        _height = ActualHeight;
        Left = area.Right - ActualWidth - 8;
        Top = area.Bottom - ActualHeight - 8 - _stacked;
        _stacked += _height;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
