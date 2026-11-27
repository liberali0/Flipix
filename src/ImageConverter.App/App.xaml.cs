using System.IO;
using System.Windows;
using ImageConverter.App.Services;
using ImageConverter.App.ViewModels;
using ImageConverter.Core.Localization;
using ImageConverter.Core.Settings;
using ImageConverter.Core.Updates;

namespace ImageConverter.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 처리되지 않은 오류로 조용히 꺼지지 않도록, 내용을 보여 주고 로그로 남긴다.
        DispatcherUnhandledException += (_, args) =>
        {
            ReportCrash(args.Exception);
            args.Handled = true;
            Shutdown(1);
        };

        var store = new SettingsStore();
        var settings = store.Load();
        ThemeService.Apply(settings.Theme);
        Loc.Apply(settings.Language);

        // 우클릭 변환이 끝난 뒤 셸 확장이 부르는 모드: 창 없이 알림만 띄우고 스스로 끝난다.
        if (e.Args.Contains("--background"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunBackgroundAsync(e.Args, settings);
            return;
        }

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
        _ = vm.CheckOnStartAsync(); // 새 버전이 있으면 상단 막대에 업데이트 단추가 나타난다
    }

    private async Task RunBackgroundAsync(string[] args, AppSettings settings)
    {
        try
        {
            var notices = new List<Task>();
            var at = Array.IndexOf(args, "--converted");
            if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var converted) && converted > 0)
            {
                var done = new NoticeWindow(Loc.T("빠른 변환 완료", "Quick convert done"),
                    Loc.T($"{converted:N0}개 파일을 모두 변환했어요.", converted == 1 ? "The file was converted." : $"All {converted:N0} files were converted."));
                done.CloseAfter(TimeSpan.FromSeconds(5));
                notices.Add(done.ShowNotice());
            }

            if (args.Contains("--check-update") && settings.CheckForUpdates)
            {
                UpdateInfo? info = null;
                try { info = await UpdateService.CheckAsync(); } catch { /* 인터넷이 없으면 조용히 넘어간다 */ }
                if (info != null) notices.Add(ShowUpdateNotice(info));
            }

            await Task.WhenAll(notices);
        }
        catch (Exception ex)
        {
            try { LogError(ex); } catch { }
        }
        Shutdown();
    }

    private static Task ShowUpdateNotice(UpdateInfo info)
    {
        var notice = new NoticeWindow(Loc.T($"Flipix v{info.Version}이(가) 나왔어요", $"Flipix v{info.Version} is available"),
            Loc.T("지금 업데이트할까요? 설치 위치와 설정은 그대로예요.", "Update now? Your install folder and settings stay the same."));
        notice.AddButton(Loc.T("업데이트", "Update"), primary: true, async () =>
        {
            notice.SetButtonsEnabled(false);
            try
            {
                var progress = new Progress<double>(p => notice.Message = Loc.T($"내려받는 중… {p:0}%", $"Downloading… {p:0}%"));
                var setup = await UpdateService.DownloadAsync(info, progress);
                notice.Message = Loc.T("업데이트를 시작해요.", "Starting the update.");
                UpdateService.LaunchInstaller(setup);
                notice.Close();
            }
            catch (Exception ex)
            {
                notice.Message = Loc.T("업데이트하지 못했어요: ", "Could not update: ") + ex.Message;
                notice.SetButtonsEnabled(true);
            }
        });
        notice.AddButton(Loc.T("나중에", "Later"), primary: false, notice.Close);
        return notice.ShowNotice();
    }

    private static void LogError(Exception ex)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flipix");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
    }

    private static void ReportCrash(Exception ex)
    {
        string? logPath = null;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flipix");
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { logPath = null; }

        var detail = logPath is null ? ex.Message : $"{ex.Message}\n\n{Loc.T("자세한 내용", "Details")}: {logPath}";
        var text = Loc.T("Flipix Workshop를 실행하는 중 문제가 생겼습니다.", "Flipix Workshop ran into a problem.");
        try { MessageBox.Show($"{text}\n\n{detail}", "Flipix Workshop", MessageBoxButton.OK, MessageBoxImage.Error); }
        catch { }
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
