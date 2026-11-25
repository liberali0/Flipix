using ImageConverter.Core.Engine;

namespace ImageConverter.Core.Batch;

public enum ConflictPolicy { Rename, Overwrite, Skip }

public sealed record OutputOptions
{
    /// <summary>null이면 원본과 같은 폴더.</summary>
    public string? CustomFolder { get; init; }
    public ConflictPolicy Conflict { get; init; } = ConflictPolicy.Rename;
    /// <summary>폴더로 추가한 항목을 지정 폴더 아래에도 같은 하위 구조로 저장.</summary>
    public bool PreserveStructure { get; init; } = true;
}

public sealed record BatchOptions
{
    public string TargetFormat { get; init; } = "png";
    public int MaxParallelism { get; init; } = 2;
    /// <summary>이 크기 이상은 "대용량"으로 보고 동시에 처리하는 수를 제한한다(메모리 보호).</summary>
    public long LargeFileThresholdBytes { get; init; } = 128L * 1024 * 1024;
    public int MaxParallelLargeFiles { get; init; } = 1;
    public OutputOptions Output { get; init; } = new();
    public ConversionOptions Conversion { get; init; } = new();
    /// <summary>null이 아니면 변환에 성공한 원본을 이 폴더로 옮긴다. 실패·건너뜀·취소된 원본은 그대로 둔다.</summary>
    public string? MoveSourceTo { get; init; }
}

public sealed record BatchSummary(int Succeeded, int Failed, int Skipped, int Canceled);
