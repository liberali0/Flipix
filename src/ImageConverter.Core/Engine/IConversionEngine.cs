using ImageConverter.Core.Localization;

namespace ImageConverter.Core.Engine;

public sealed record ConversionOptions
{
    /// <summary>JPG 출력 품질. 기본 100(+4:4:4)으로 손실 최소화.</summary>
    public int JpegQuality { get; init; } = 100;
    public bool PreserveMetadata { get; init; } = true;
    /// <summary>투명 픽셀을 JPG/BMP 등에 쓸 때 채울 색.</summary>
    public string BackgroundColor { get; init; } = "#FFFFFF";
    /// <summary>무손실 포맷으로 저장한 뒤 다시 읽어 모든 픽셀을 비교한다(느려짐).</summary>
    public bool Verify { get; init; }
    /// <summary>움직이는 GIF·WEBP: 모든 프레임을 "이름_frames" 폴더에 각각 저장한다. 끄면 첫 프레임만.</summary>
    public bool AllFrames { get; init; }
    /// <summary>긴 쪽이 이 픽셀을 넘지 않게 줄인다(비율 유지, 키우지 않음). null이면 그대로.</summary>
    public int? MaxSize { get; init; }
    /// <summary>가로·세로를 이 비율(1~100%)로 줄인다. null이면 그대로.</summary>
    public int? ScalePercent { get; init; }
}

public sealed record ConversionRequest(
    string InputPath,
    string OutputPath,
    string TargetFormat,
    ConversionOptions Options);

/// <param name="Note">성공했지만 사용자에게 알릴 내용(예: 손실 압축 포맷, 투명 배경 채움).</param>
/// <param name="WrittenPath">요청한 출력 경로와 다른 곳에 썼을 때(모든 프레임 폴더) 그 경로.</param>
/// <param name="KeepSource">결과에 원본이 다 담기지 않아(첫 프레임만 변환) 원본을 옮기면 안 될 때.</param>
public sealed record ConversionResult(bool Success, string? ErrorMessage = null, long OutputBytes = 0, string? Note = null,
    string? WrittenPath = null, bool KeepSource = false)
{
    public static ConversionResult Ok(long bytes = 0, string? note = null) => new(true, null, bytes, note);
    public static ConversionResult Fail(string message) => new(false, message);
}

/// <summary>
/// 변환 엔진 계약. 실제 변환 엔진 구현은 이 인터페이스 뒤에 연결한다.
/// 구현은 출력 파일을 임시 이름에 쓴 뒤 완료 시 이동(원자적 쓰기)해야 한다.
/// </summary>
public interface IConversionEngine
{
    IReadOnlyCollection<string> SupportedInputExtensions { get; }
    Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct);
}

/// <summary>엔진이 아직 연결되지 않았을 때 쓰는 자리표시자.</summary>
public sealed class UnavailableEngine : IConversionEngine
{
    public IReadOnlyCollection<string> SupportedInputExtensions => FormatCatalog.DefaultInputExtensions;

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct)
        => Task.FromResult(ConversionResult.Fail(Loc.T("변환 엔진이 아직 연결되지 않았습니다.", "The conversion engine is not available.")));
}
