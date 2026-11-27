using ImageConverter.Core.Engine;
using Engine = ImageConverter.Engine;

namespace ImageConverter.App.Services;

/// <summary>무손실 변환 엔진(ImageConverter.Engine)을 앱 큐의 IConversionEngine 계약에 맞춘다.</summary>
public sealed class EngineAdapter : IConversionEngine
{
    private readonly Engine.IImageConverter _engine;

    public EngineAdapter(Engine.IImageConverter engine)
    {
        _engine = engine;
        SupportedInputExtensions = Engine.ImageFormats.All
            .Where(f => f.CanRead)
            .SelectMany(f => f.Extensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyCollection<string> SupportedInputExtensions { get; }

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        if (!Engine.ImageFormats.TryParse(request.TargetFormat, out var target))
            return ConversionResult.Fail($"지원하지 않는 형식: {request.TargetFormat}");

        var options = new Engine.ConversionOptions
        {
            JpegQuality = request.Options.JpegQuality,
            PreserveMetadata = request.Options.PreserveMetadata,
            BackgroundColor = request.Options.BackgroundColor,
            Verify = request.Options.Verify,
            AllFrames = request.Options.AllFrames,
            MaxSize = request.Options.MaxSize,
            ScalePercent = request.Options.ScalePercent,
            // 출력 경로는 큐가 이미 정했다(충돌 정책 반영). 원본은 엔진이 따로 지킨다.
            IfOutputExists = Engine.ExistingFileBehavior.Overwrite,
        };
        var engineProgress = progress == null ? null : new Progress<Engine.ConversionProgress>(p => progress.Report(p.Fraction * 100));

        var r = await _engine.ConvertAsync(request.InputPath, request.OutputPath, target, options, engineProgress, ct)
            .ConfigureAwait(false);

        switch (r.Status)
        {
            case Engine.ConversionStatus.Cancelled:
                throw new OperationCanceledException(ct);
            case Engine.ConversionStatus.Failed:
                return ConversionResult.Fail(DescribeError(r));
            default:
                long size = 0;
                try
                {
                    if (r.FrameCount > 0 && r.OutputPath != null)
                        size = new DirectoryInfo(r.OutputPath).EnumerateFiles().Sum(f => f.Length);
                    else if (r.OutputPath != null)
                        size = new FileInfo(r.OutputPath).Length;
                }
                catch { }
                return new ConversionResult(true, null, size, DescribeWarnings(r),
                    WrittenPath: r.FrameCount > 0 ? r.OutputPath : null,
                    KeepSource: r.Warnings.Contains(Engine.ConversionWarning.ExtraFramesDropped));
        }
    }

    private static string DescribeError(Engine.ConversionResult r) => r.Error switch
    {
        Engine.ConversionError.InputNotFound => "원본 파일을 찾을 수 없습니다.",
        Engine.ConversionError.UnsupportedInputFormat => "지원하지 않는 이미지 형식입니다.",
        Engine.ConversionError.DecodeFailed => "이미지를 읽지 못했습니다: " + r.ErrorMessage,
        Engine.ConversionError.EncodeFailed => "저장하지 못했습니다: " + r.ErrorMessage,
        Engine.ConversionError.OutputIsInput => "원본 파일을 덮어쓸 수 없습니다.",
        Engine.ConversionError.VerificationFailed => "변환 결과가 원본과 다릅니다(검증 실패).",
        Engine.ConversionError.IoError => "파일 입출력 오류: " + r.ErrorMessage,
        _ => r.ErrorMessage ?? "알 수 없는 오류",
    };

    /// <summary>성공 항목에 붙일 한국어 안내. 완전 무손실이면 null(또는 검증 통과 표시).</summary>
    private static string? DescribeWarnings(Engine.ConversionResult r)
    {
        var parts = new List<string>();
        foreach (var w in r.Warnings)
        {
            parts.Add(w switch
            {
                Engine.ConversionWarning.LossyFormat => "손실 압축 포맷",
                Engine.ConversionWarning.BitDepthReduced => "비트 깊이 축소",
                Engine.ConversionWarning.AlphaFlattened => "투명 영역을 배경색으로 채움",
                Engine.ConversionWarning.MetadataDropped => "일부 메타데이터 제외",
                Engine.ConversionWarning.ExtraFramesDropped => "첫 프레임만 변환",
                Engine.ConversionWarning.ColorSpaceConverted => "색 공간을 RGB로 변환",
                Engine.ConversionWarning.Resized => "크기 줄임",
                _ => w.ToString(),
            });
        }
        if (r.FrameCount > 0) parts.Insert(0, $"프레임 {r.FrameCount:N0}개를 폴더에 저장");
        if (r.Verification is { Identical: true }) parts.Add("픽셀 검증 통과");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
