using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Collections.Specialized;
using System.Windows.Input;
using ImageConverter.App.Services;
using ImageConverter.Core;
using ImageConverter.Core.Batch;
using ImageConverter.Core.Engine;
using ImageConverter.Core.Settings;
using ImageConverter.Core.Updates;
using Microsoft.Win32;

namespace ImageConverter.App.ViewModels;

public sealed record Choice<T>(T Value, string Label);

public sealed class MainViewModel : ObservableObject
{
    private readonly IConversionEngine _engine;
    private readonly SettingsStore _store;
    private readonly ConversionQueue _queue;
    private readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    private AppSettings _settings;
    private CancellationTokenSource? _cts;

    private string _targetFormatId;
    private bool _useCustomFolder;
    private string _outputFolder = "";
    private ConflictPolicy _conflict;
    private bool _preserveStructure;
    private int _parallelism;
    private bool _isRunning;
    private bool _isPaused;
    private int _runTotal, _runDone, _runFailed;
    private string _statusText = "이미지를 추가하세요.";
    private bool _isScanning;
    private bool _verify;
    private bool _parallelismChanged;
    private OriginalFileAction _originalAction;
    private string _originalMoveFolder = "";
    private UpdateInfo? _update;
    private bool _isUpdating;

    public MainViewModel(IConversionEngine engine, SettingsStore store)
    {
        _engine = engine;
        _store = store;
        _queue = new ConversionQueue(engine);
        _queue.JobFinished += OnJobFinished;
        _settings = store.Load();

        _targetFormatId = FormatCatalog.TryGet(_settings.DefaultTargetFormat)?.Id ?? "png";
        _conflict = _settings.Conflict;
        _preserveStructure = _settings.PreserveStructure;
        _parallelism = _settings.EffectiveParallelism;
        _originalAction = _settings.OriginalAction;
        _originalMoveFolder = _settings.OriginalMoveFolder ?? "";
        if (!string.IsNullOrWhiteSpace(_settings.CustomOutputFolder))
        {
            _useCustomFolder = true;
            _outputFolder = _settings.CustomOutputFolder!;
        }

        Jobs.CollectionChanged += (_, _) => { Raise(nameof(IsEmpty)); Raise(nameof(JobCountText)); };

        AddFilesCommand = new RelayCommand(AddFiles, () => CanEdit);
        AddFolderCommand = new RelayCommand(AddFolder, () => CanEdit);
        ClearCommand = new RelayCommand(Clear, () => CanEdit && Jobs.Count > 0);
        RemoveFinishedCommand = new RelayCommand(RemoveFinished, () => CanEdit && Jobs.Any(j => j.IsFinished));
        RetryFailedCommand = new RelayCommand(RetryFailed, () => CanEdit && Jobs.Any(j => j.Status is JobStatus.Failed or JobStatus.Canceled or JobStatus.Skipped));
        BrowseOutputCommand = new RelayCommand(BrowseOutput);
        BrowseOriginalFolderCommand = new RelayCommand(BrowseOriginalFolder);
        StartCommand = new RelayCommand(() => _ = StartAsync(), () => CanEdit && Jobs.Any(j => j.Status == JobStatus.Pending));
        PauseResumeCommand = new RelayCommand(TogglePause, () => IsRunning);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        OpenSettingsCommand = new RelayCommand(OpenSettings, () => CanEdit);
        ContactCommand = new RelayCommand(OpenContact);
        UpdateCommand = new RelayCommand(() => _ = UpdateAsync(), () => _update != null && !_isUpdating);
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckForUpdatesAsync(userAsked: true), () => !_isUpdating);
        CreateShortcutCommand = new RelayCommand(CreateShortcut);
        OpenAdvancedSettingsCommand = new RelayCommand(OpenAdvancedSettings, () => CanEdit);
    }

    public ObservableCollection<ConversionJob> Jobs { get; } = new();
    public IReadOnlyList<FormatInfo> Formats => FormatCatalog.All;
    public IReadOnlyList<Choice<ConflictPolicy>> ConflictChoices { get; } = new[]
    {
        new Choice<ConflictPolicy>(ConflictPolicy.Rename, "이름 뒤에 번호 붙이기"),
        new Choice<ConflictPolicy>(ConflictPolicy.Overwrite, "덮어쓰기"),
        new Choice<ConflictPolicy>(ConflictPolicy.Skip, "건너뛰기"),
    };
    public IReadOnlyList<Choice<OriginalFileAction>> OriginalActionChoices { get; } = new[]
    {
        new Choice<OriginalFileAction>(OriginalFileAction.Keep, "그대로 두기"),
        new Choice<OriginalFileAction>(OriginalFileAction.Move, "지정 폴더로 옮기기"),
    };
    public IReadOnlyList<Choice<bool>> SaveLocationChoices { get; } = new[]
    {
        new Choice<bool>(false, "원본과 같은 폴더"),
        new Choice<bool>(true, "지정 폴더"),
    };
    public IReadOnlyList<Choice<ThemePreference>> ThemeChoices { get; } = new[]
    {
        new Choice<ThemePreference>(ThemePreference.System, "시스템"),
        new Choice<ThemePreference>(ThemePreference.Light, "라이트"),
        new Choice<ThemePreference>(ThemePreference.Dark, "다크"),
    };

    /// <summary>화면 테마. 바꾸면 바로 적용하고 설정에 저장한다.</summary>
    public ThemePreference Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value) return;
            _settings.Theme = value;
            Raise();
            ThemeService.Apply(value);
            try { _store.Save(_settings); } catch { /* 테마 저장 실패는 무시 */ }
        }
    }

    /// <summary>빠른 변환 카드: 우클릭 메뉴에 바로 보이는 형식(대문자).</summary>
    public IReadOnlyList<string> QuickFormats =>
        _settings.QuickMenu.Formats.Select(id => FormatCatalog.TryGet(id)?.Id.ToUpperInvariant()).OfType<string>().ToList();

    /// <summary>빠른 변환 카드: 변환이 끝난 원본을 어떻게 하는지 한 줄로.</summary>
    public string QuickOriginalText =>
        _settings.QuickMenu.MoveOriginal && !string.IsNullOrWhiteSpace(_settings.QuickMenu.MoveOriginalTo)
            ? "원본: " + _settings.QuickMenu.MoveOriginalTo + "(으)로 옮기기"
            : _settings.QuickMenu.DeleteOriginal ? "원본: 휴지통으로 보내기" : "원본: 그대로 두기";

    public string JobCountText => $"{Jobs.Count:N0}개";
    public int MaxParallelismLimit => Math.Max(2, Environment.ProcessorCount);

    public ICommand AddFilesCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RemoveFinishedCommand { get; }
    public ICommand RetryFailedCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand BrowseOriginalFolderCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand PauseResumeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ContactCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand CreateShortcutCommand { get; }
    public ICommand OpenAdvancedSettingsCommand { get; }

    /// <summary>개발자에게 연락하는 곳(X 프로필). 사용자가 2026-11-25 지정.</summary>
    public const string ContactUrl = "https://x.com/Liberali_0";
    public string ContactToolTip => "개발자에게 의견·문의 보내기 (X @Liberali_0)";

    public bool IsEmpty => Jobs.Count == 0;
    private bool CanEdit => !IsRunning && !_isScanning;

    public string TargetFormatId
    {
        get => _targetFormatId;
        set { if (Set(ref _targetFormatId, value)) Raise(nameof(LossyWarning)); }
    }
    public bool LossyWarning => FormatCatalog.TryGet(_targetFormatId)?.IsLossy == true;

    public bool UseCustomFolder { get => _useCustomFolder; set => Set(ref _useCustomFolder, value); }
    public string OutputFolder { get => _outputFolder; set => Set(ref _outputFolder, value); }
    public bool Verify { get => _verify; set => Set(ref _verify, value); }
    public OriginalFileAction OriginalAction
    {
        get => _originalAction;
        set { if (Set(ref _originalAction, value)) Raise(nameof(MoveOriginal)); }
    }
    public bool MoveOriginal => OriginalAction == OriginalFileAction.Move;
    public string OriginalMoveFolder { get => _originalMoveFolder; set => Set(ref _originalMoveFolder, value); }
    public ConflictPolicy Conflict { get => _conflict; set => Set(ref _conflict, value); }
    public bool PreserveStructure { get => _preserveStructure; set => Set(ref _preserveStructure, value); }
    public int Parallelism
    {
        get => _parallelism;
        set { if (Set(ref _parallelism, Math.Max(1, value))) _parallelismChanged = true; }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set { if (Set(ref _isRunning, value)) Raise(nameof(IsIdle)); }
    }
    public bool IsIdle => !IsRunning;
    public bool IsPaused { get => _isPaused; private set { if (Set(ref _isPaused, value)) Raise(nameof(PauseButtonText)); } }
    public string PauseButtonText => IsPaused ? "계속" : "일시정지";

    public double OverallProgress => _runTotal == 0 ? 0 : 100.0 * _runDone / _runTotal;
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public void TrySelectFormat(string id)
    {
        var f = FormatCatalog.TryGet(id);
        if (f != null) TargetFormatId = f.Id;
    }

    // ---------- 항목 추가 ----------

    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        if (!CanEdit) return;
        var list = paths.ToList();
        var exts = _engine.SupportedInputExtensions;
        _isScanning = true;
        StatusText = "파일을 찾는 중...";
        CommandManager.InvalidateRequerySuggested();
        try
        {
            // 큰 폴더도 UI가 멈추지 않도록 백그라운드에서 스캔
            var found = await Task.Run(() => FileScanner.Scan(list, exts).ToList());

            // 중복 제거는 UI 스레드에서 실제 목록 기준으로 한다.
            var known = new HashSet<string>(Jobs.Select(j => j.InputPath), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var f in found)
            {
                if (!known.Add(f.Path)) continue;
                Jobs.Add(new ConversionJob(f.Path, f.Bytes, f.SourceRoot));
                added++;
            }
            StatusText = added == 0 ? "추가할 이미지가 없습니다." : $"{added:N0}개 추가됨 (총 {Jobs.Count:N0}개)";
        }
        catch (Exception ex)
        {
            StatusText = "파일을 찾는 중 오류: " + ex.Message;
        }
        finally
        {
            _isScanning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void AddFiles()
    {
        var filter = string.Join(";", _engine.SupportedInputExtensions.Select(e => "*" + e));
        var dlg = new OpenFileDialog { Multiselect = true, Filter = $"이미지|{filter}|모든 파일|*.*" };
        if (dlg.ShowDialog() == true) _ = AddPathsAsync(dlg.FileNames);
    }

    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Multiselect = true };
        if (dlg.ShowDialog() == true) _ = AddPathsAsync(dlg.FolderNames);
    }

    private void BrowseOutput()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true) { OutputFolder = dlg.FolderName; UseCustomFolder = true; }
    }

    private void BrowseOriginalFolder()
    {
        var dlg = new OpenFolderDialog { Title = "변환이 끝난 원본을 옮길 폴더" };
        if (dlg.ShowDialog() == true) { OriginalMoveFolder = dlg.FolderName; OriginalAction = OriginalFileAction.Move; }
    }

    private void Clear() { Jobs.Clear(); _runTotal = _runDone = _runFailed = 0; Raise(nameof(OverallProgress)); StatusText = "이미지를 추가하세요."; }

    private void RemoveFinished()
    {
        foreach (var j in Jobs.Where(j => j.IsFinished).ToList()) Jobs.Remove(j);
    }

    private void RetryFailed()
    {
        foreach (var j in Jobs.Where(j => j.Status is JobStatus.Failed or JobStatus.Canceled or JobStatus.Skipped)) j.Reset();
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- 실행 ----------

    private async Task StartAsync()
    {
        if (UseCustomFolder && string.IsNullOrWhiteSpace(OutputFolder))
        {
            StatusText = "저장할 폴더를 지정하세요.";
            return;
        }

        if (MoveOriginal && (string.IsNullOrWhiteSpace(OriginalMoveFolder) || !Path.IsPathFullyQualified(OriginalMoveFolder.Trim())))
        {
            StatusText = "원본을 옮길 폴더를 전체 경로(예: D:\\원본 보관)로 지정하세요.";
            return;
        }

        var runJobs = Jobs.Where(j => j.Status == JobStatus.Pending).ToList();
        _runTotal = runJobs.Count; _runDone = 0; _runFailed = 0;
        Raise(nameof(OverallProgress));

        if (UseCustomFolder && !Path.IsPathRooted(OutputFolder))
        {
            StatusText = "저장 폴더는 전체 경로(예: D:\\변환결과)로 지정하세요.";
            return;
        }

        SaveSettings();
        var options = new BatchOptions
        {
            TargetFormat = TargetFormatId,
            MaxParallelism = Parallelism,
            Output = new OutputOptions
            {
                CustomFolder = UseCustomFolder ? OutputFolder : null,
                Conflict = Conflict,
                PreserveStructure = PreserveStructure,
            },
            Conversion = new ConversionOptions { Verify = Verify },
            MoveSourceTo = MoveOriginal ? OriginalMoveFolder.Trim() : null,
        };

        _cts = new CancellationTokenSource();
        IsRunning = true;
        IsPaused = false;
        StatusText = "변환 중...";
        try
        {
            var s = await _queue.RunAsync(runJobs, options, _cts.Token);
            StatusText = $"완료 {s.Succeeded:N0} · 실패 {s.Failed:N0} · 건너뜀 {s.Skipped:N0} · 취소 {s.Canceled:N0}";
        }
        catch (Exception ex)
        {
            StatusText = "오류: " + ex.Message;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
            IsPaused = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void TogglePause()
    {
        if (_queue.IsPaused) { _queue.Resume(); IsPaused = false; StatusText = "변환 중..."; }
        else { _queue.Pause(); IsPaused = true; StatusText = "일시정지됨 (진행 중인 항목은 끝까지 처리)"; }
    }

    private void OnJobFinished(ConversionJob job)
    {
        _ui.Post(_ =>
        {
            _runDone++;
            if (job.Status == JobStatus.Failed) _runFailed++;
            Raise(nameof(OverallProgress));
            if (IsRunning && !IsPaused)
                StatusText = $"변환 중... {_runDone:N0}/{_runTotal:N0}" + (_runFailed > 0 ? $" (실패 {_runFailed:N0})" : "");
        }, null);
    }

    // ---------- 설정 ----------

    private void SaveSettings()
    {
        _settings.DefaultTargetFormat = TargetFormatId;
        _settings.Conflict = Conflict;
        _settings.PreserveStructure = PreserveStructure;
        if (_parallelismChanged) _settings.MaxParallelism = Parallelism; // 건드리지 않았으면 자동(0) 유지
        _settings.CustomOutputFolder = UseCustomFolder ? OutputFolder : null;
        _settings.OriginalAction = OriginalAction;
        _settings.OriginalMoveFolder = string.IsNullOrWhiteSpace(OriginalMoveFolder) ? null : OriginalMoveFolder.Trim();
        try { _store.Save(_settings); } catch { /* 설정 저장 실패는 변환을 막지 않는다 */ }
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_settings.QuickMenu) { Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
        {
            _settings.QuickMenu.Formats = win.SelectedFormats.ToList();
            _settings.QuickMenu.DeleteOriginal = win.DeleteOriginal;
            _settings.QuickMenu.MoveOriginal = win.MoveOriginal;
            _settings.QuickMenu.MoveOriginalTo = win.MoveOriginalTo;
            Raise(nameof(QuickFormats));
            Raise(nameof(QuickOriginalText));
            try { _store.Save(_settings); StatusText = "빠른 변환 설정을 저장했습니다."; }
            catch (Exception ex) { StatusText = "설정 저장 실패: " + ex.Message; }
        }
    }

    private void OpenContact()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ContactUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"브라우저를 열지 못했습니다. {ContactUrl} 로 직접 접속해 주세요. ({ex.Message})";
        }
    }

    // ---------- 업데이트 · 바로 가기 ----------

    public string VersionText => "Flipix " + UpdateService.CurrentVersionText;

    /// <summary>새 버전이 있을 때만 상단 막대에 업데이트 단추를 보인다.</summary>
    public bool UpdateAvailable => _update != null;
    public string UpdateButtonText => _update is null ? "" : $"새 버전 v{_update.Version} 업데이트";

    private void OpenAdvancedSettings()
    {
        var win = new AdvancedSettingsWindow(_settings) { Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() != true) return;
        _settings.QuickMenu.Notify = win.Notify;
        _settings.CheckForUpdates = win.CheckForUpdates;
        try { _store.Save(_settings); StatusText = "상세 설정을 저장했습니다."; }
        catch (Exception ex) { StatusText = "설정 저장 실패: " + ex.Message; }
    }

    /// <summary>앱 시작 때 한 번. 설정이 꺼져 있으면 아무것도 하지 않는다.</summary>
    public Task CheckOnStartAsync() => _settings.CheckForUpdates ? CheckForUpdatesAsync(userAsked: false) : Task.CompletedTask;

    private async Task CheckForUpdatesAsync(bool userAsked)
    {
        try
        {
            if (userAsked) StatusText = "새 버전을 확인하는 중…";
            var info = await UpdateService.CheckAsync();
            SetUpdate(info);
            if (userAsked)
                StatusText = info is null ? $"최신 버전({UpdateService.CurrentVersionText})을 쓰고 있어요." : $"새 버전 v{info.Version}이(가) 있어요. 위의 업데이트 단추를 누르세요.";
        }
        catch (Exception ex)
        {
            // 시작할 때 자동 확인은 인터넷이 없어도 조용히 넘어간다.
            if (userAsked) StatusText = "새 버전을 확인하지 못했습니다: " + ex.Message;
        }
    }

    private void SetUpdate(UpdateInfo? info)
    {
        _update = info;
        Raise(nameof(UpdateAvailable));
        Raise(nameof(UpdateButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task UpdateAsync()
    {
        var info = _update;
        if (info is null || _isUpdating) return;
        var owner = System.Windows.Application.Current.MainWindow;
        if (IsRunning)
        {
            System.Windows.MessageBox.Show(owner, "변환이 끝난 뒤에 업데이트해 주세요.", "Flipix 업데이트",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        var answer = System.Windows.MessageBox.Show(owner,
            $"Flipix를 v{info.Version}(으)로 업데이트할까요?\n\n지금 설치된 위치와 설정은 그대로 두고 프로그램만 바꿔요. 업데이트하는 동안 Workshop이 잠시 닫혔다가 다시 열려요.",
            "Flipix 업데이트", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        _isUpdating = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            StatusText = $"v{info.Version} 내려받는 중…";
            var progress = new Progress<double>(p => StatusText = $"v{info.Version} 내려받는 중… {p:0}%");
            var setup = await UpdateService.DownloadAsync(info, progress);
            StatusText = "업데이트를 시작합니다…";
            UpdateService.LaunchInstaller(setup);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText = "업데이트하지 못했습니다: " + ex.Message;
            _isUpdating = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CreateShortcut()
    {
        try
        {
            DesktopShortcut.Create();
            StatusText = "바탕 화면에 Flipix 바로 가기를 만들었어요.";
        }
        catch (Exception ex)
        {
            StatusText = "바로 가기를 만들지 못했습니다: " + ex.Message;
        }
    }
}
