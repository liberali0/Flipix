using System.IO;
using System.Windows;
using ImageConverter.App.Services;
using ImageConverter.App.ViewModels;
using ImageConverter.Core.Settings;

namespace ImageConverter.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var store = new SettingsStore();
        ThemeService.Apply(store.Load().Theme);
        var engine = EngineFactory.Create();
        var vm = new MainViewModel(engine, store);

        // 우클릭 메뉴의 [자세히] 등에서 넘어온 인자: 경로들과 선택적 `--to <포맷>`
        var paths = new List<string>();
        string? format = null;
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--to" && i + 1 < e.Args.Length) format = e.Args[++i];
            else if (e.Args[i].StartsWith('@')) paths.AddRange(ReadListFile(e.Args[i][1..]));
            else paths.Add(e.Args[i]);
        }
        if (format != null) vm.TrySelectFormat(format);

        var window = new MainWindow { DataContext = vm };
        window.Show();
        if (paths.Count > 0) _ = vm.AddPathsAsync(paths);
    }

    // 선택 항목이 많을 때 셸 확장이 넘기는 `@목록파일` (UTF-8, 한 줄에 경로 하나)
    private static IEnumerable<string> ReadListFile(string path)
    {
        try
        {
            return File.ReadAllLines(path, System.Text.Encoding.UTF8)
                .Select(l => l.Trim().Trim('"'))
                .Where(l => l.Length > 0)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}
