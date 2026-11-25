using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Collections.Specialized;
using System.Windows.Input;
using ImageConverter.Core;
using ImageConverter.Core.Batch;
using ImageConverter.Core.Engine;
using ImageConverter.Core.Settings;
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

        Jobs.CollectionChanged += (_, _) => { Raise(nameof(IsEmpty)); };

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
}
