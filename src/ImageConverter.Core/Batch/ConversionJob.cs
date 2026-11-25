namespace ImageConverter.Core.Batch;

public enum JobStatus { Pending, Running, Succeeded, Failed, Skipped, Canceled }

public sealed class ConversionJob : ObservableObject
{
    private JobStatus _status = JobStatus.Pending;
    private double _progress;
    private string? _message;
    private string? _outputPath;

    public ConversionJob(string inputPath, long inputBytes, string? sourceRoot = null)
    {
        InputPath = inputPath;
        InputBytes = inputBytes;
        SourceRoot = sourceRoot;
    }

    public string InputPath { get; }
    public long InputBytes { get; }
    /// <summary>폴더로 추가된 경우 그 폴더(하위 구조 유지용).</summary>
    public string? SourceRoot { get; }
    public string FileName => Path.GetFileName(InputPath);
    public string SizeText => FormatSize(InputBytes);

    public JobStatus Status
    {
        get => _status;
        set { if (Set(ref _status, value)) Raise(nameof(StatusText)); }
    }

    public string StatusText => Status switch
    {
        JobStatus.Pending => "대기",
        JobStatus.Running => "변환 중",
        JobStatus.Succeeded => "완료",
        JobStatus.Failed => "실패",
        JobStatus.Skipped => "건너뜀",
        JobStatus.Canceled => "취소됨",
        _ => "",
    };

    /// <summary>0~100.</summary>
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string? Message { get => _message; set => Set(ref _message, value); }
    public string? OutputPath { get => _outputPath; set => Set(ref _outputPath, value); }
    /// <summary>변환 후 원본을 옮겼다면 옮긴 위치.</summary>
    public string? MovedSourcePath { get; set; }

    public bool IsFinished => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Skipped or JobStatus.Canceled;

    /// <summary>실패/취소/건너뜀 항목을 다시 대기 상태로.</summary>
    public void Reset()
    {
        Status = JobStatus.Pending;
        Progress = 0;
        Message = null;
        OutputPath = null;
        MovedSourcePath = null;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.#} {units[i]}";
    }
}
