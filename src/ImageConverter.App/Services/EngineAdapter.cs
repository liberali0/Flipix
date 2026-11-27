using ImageConverter.Core.Engine;
using ImageConverter.Core.Localization;
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
            return ConversionResult.Fail(Loc.T($"지원하지 않는 형식: {request.TargetFormat}", $"Unsupported format: {request.TargetFormat}"));

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
        Engine.ConversionError.InputNotFound => Loc.T("원본 파일을 찾을 수 없습니다.", "The original file was not found."),
        Engine.ConversionError.UnsupportedInputFormat => Loc.T("지원하지 않는 이미지 형식입니다.", "This image format is not supported."),
        Engine.ConversionError.DecodeFailed => Loc.T("이미지를 읽지 못했습니다: ", "Could not read the image: ") + r.ErrorMessage,
        Engine.ConversionError.EncodeFailed => Loc.T("저장하지 못했습니다: ", "Could not save: ") + r.ErrorMessage,
        Engine.ConversionError.OutputIsInput => Loc.T("원본 파일을 덮어쓸 수 없습니다.", "The original file cannot be overwritten."),
        Engine.ConversionError.VerificationFailed => Loc.T("변환 결과가 원본과 다릅니다(검증 실패).", "The result differs from the original (verification failed)."),
        Engine.ConversionError.IoError => Loc.T("파일 입출력 오류: ", "File error: ") + r.ErrorMessage,
        _ => r.ErrorMessage ?? Loc.T("알 수 없는 오류", "Unknown error"),
    };

    /// <summary>성공 항목에 붙일 한국어 안내. 완전 무손실이면 null(또는 검증 통과 표시).</summary>
    private static string? DescribeWarnings(Engine.ConversionResult r)
    {
        var parts = new List<string>();
        foreach (var w in r.Warnings)
        {
            parts.Add(w switch
            {
                Engine.ConversionWarning.LossyFormat => Loc.T("손실 압축 포맷", "Lossy format"),
                Engine.ConversionWarning.BitDepthReduced => Loc.T("비트 깊이 축소", "Bit depth reduced"),
                Engine.ConversionWarning.AlphaFlattened => Loc.T("투명 영역을 배경색으로 채움", "Transparency filled with the background color"),
                Engine.ConversionWarning.MetadataDropped => Loc.T("일부 메타데이터 제외", "Some metadata left out"),
                Engine.ConversionWarning.ExtraFramesDropped => Loc.T("첫 프레임만 변환", "Only the first frame converted"),
                Engine.ConversionWarning.ColorSpaceConverted => Loc.T("색 공간을 RGB로 변환", "Color space converted to RGB"),
                Engine.ConversionWarning.Resized => Loc.T("크기 줄임", "Resized"),
                _ => w.ToString(),
            });
        }
        if (r.FrameCount > 0) parts.Insert(0, Loc.T($"프레임 {r.FrameCount:N0}개를 폴더에 저장", $"{r.FrameCount:N0} frames saved to a folder"));
        if (r.Verification is { Identical: true }) parts.Add(Loc.T("픽셀 검증 통과", "Pixels verified"));
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
