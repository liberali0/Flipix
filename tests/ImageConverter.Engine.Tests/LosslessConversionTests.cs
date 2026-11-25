using ImageMagick;

namespace ImageConverter.Engine.Tests;

/// <summary>The core promise: lossless targets keep every pixel value exactly.</summary>
public class LosslessConversionTests
{
    private readonly ConversionEngine _engine = new();

    public static TheoryData<ImageFormat> LosslessTargets() =>
        [ImageFormat.Png, ImageFormat.Tiff, ImageFormat.WebP, ImageFormat.Avif, ImageFormat.Bmp];

    public static TheoryData<MagickFormat> LosslessSources() =>
        [MagickFormat.Png, MagickFormat.Tiff, MagickFormat.WebP, MagickFormat.Bmp];

    [Theory]
    [MemberData(nameof(LosslessTargets))]
    public async Task Rgb8_png_converts_pixel_exact(ImageFormat target)
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: false);
        var input = SaveAsOtherFormat(source, dir, target);
        var output = OutputPaths.ForTarget(input, target);

        var result = await _engine.ConvertAsync(input, output, target);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.True(result.IsLossless);
        Assert.Empty(result.Warnings);
        Assert.Equal(output, result.OutputPath);
        Assert.Equal(target, _engine.DetectFormat(output));
        TestImages.AssertSamePixels(source, output);
    }

    [Theory]
    [MemberData(nameof(LosslessTargets))]
    public async Task Rgba8_keeps_alpha_pixel_exact(ImageFormat target)
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: true);
        var input = SaveAsOtherFormat(source, dir, target);
        var output = OutputPaths.ForTarget(input, target);

        var result = await _engine.ConvertAsync(input, output, target);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.True(result.IsLossless);
        TestImages.AssertSamePixels(source, output);
    }

    /// <summary>Saves as PNG, or TIFF when the target is PNG, so the test is a real conversion.</summary>
    private static string SaveAsOtherFormat(MagickImage source, TempDir dir, ImageFormat target) =>
        target == ImageFormat.Png
            ? TestImages.Save(source, dir.Path, "in.tif", MagickFormat.Tiff)
            : TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);

    [Theory]
    [MemberData(nameof(LosslessSources))]
    public async Task Any_lossless_source_to_png_is_pixel_exact(MagickFormat sourceFormat)
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: false);
        if (sourceFormat == MagickFormat.WebP)
        {
            source.Settings.SetDefine(MagickFormat.WebP, "lossless", "true");
        }

        var input = TestImages.Save(source, dir.Path, "in.src", sourceFormat);
        var output = dir.File("out.png");

        var result = await _engine.ConvertAsync(input, output, ImageFormat.Png);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        TestImages.AssertSamePixels(source, output);
    }

    [Theory]
    [InlineData(ImageFormat.Png)]
    [InlineData(ImageFormat.Tiff)]
    public async Task Sixteen_bit_survives_in_16_bit_formats(ImageFormat target)
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise16();
        // Use the other 16-bit format as the source so this is a real conversion, not a same-format copy.
        var input = target == ImageFormat.Png
            ? TestImages.Save(source, dir.Path, "in16.tif", MagickFormat.Tiff)
            : TestImages.Save(source, dir.Path, "in16.png", MagickFormat.Png);
        var output = OutputPaths.ForTarget(input, target);

        var result = await _engine.ConvertAsync(input, output, target);

        Assert.True(result.IsLossless);
        Assert.Empty(result.Warnings);
        TestImages.AssertSamePixels(source, output);
        using var written = new MagickImage(output);
        Assert.Equal(16u, written.Depth);
    }

    [Theory]
    [InlineData(ImageFormat.WebP)]
    [InlineData(ImageFormat.Bmp)]
    [InlineData(ImageFormat.Avif)]
    [InlineData(ImageFormat.Jpeg)]
    public async Task Sixteen_bit_into_8_bit_format_is_reported(ImageFormat target)
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise16();
        var input = TestImages.Save(source, dir.Path, "in16.png", MagickFormat.Png);

        var result = await _engine.ConvertAsync(input, OutputPaths.ForTarget(input, target), target);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.False(result.IsLossless);
        Assert.Contains(ConversionWarning.BitDepthReduced, result.Warnings);
    }

    [Fact]
    public async Task Heic_decodes_to_png_exactly_as_decoded()
    {
        using var dir = new TempDir();
        var input = Fixtures.Path("photo.heic");
        using var decoded = new MagickImage(input);
        var output = dir.File("photo.png");

        var result = await _engine.ConvertAsync(input, output, ImageFormat.Png);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(ImageFormat.Heic, result.SourceFormat);
        Assert.True(result.IsLossless);
        TestImages.AssertSamePixels(decoded, output);
    }

    [Fact]
    public async Task Same_format_copies_bytes_unchanged()
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: false);
        var input = TestImages.Save(source, dir.Path, "in.jpg", MagickFormat.Jpeg);
        var output = dir.File("copy.jpg");

        var result = await _engine.ConvertAsync(input, output, ImageFormat.Jpeg);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.True(result.IsLossless);
        Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(output));
    }

    [Theory]
    [MemberData(nameof(LosslessTargets))]
    public async Task Verify_option_confirms_identical_pixels(ImageFormat target)
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: true);
        var input = TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);
        if (target == ImageFormat.Png)
        {
            input = TestImages.Save(source, dir.Path, "in.tif", MagickFormat.Tiff);
        }

        var output = OutputPaths.ForTarget(input, target, dir.File("out"));

        var result = await _engine.ConvertAsync(input, output, target, new ConversionOptions { Verify = true });

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.NotNull(result.Verification);
        Assert.True(result.Verification!.Identical);
        Assert.Equal(0, result.Verification.DifferentPixels);
    }
}
