using ImageMagick;

namespace ImageConverter.Engine.Tests;

public class JpegConversionTests
{
    private readonly ConversionEngine _engine = new();

    [Fact]
    public async Task Jpeg_defaults_to_quality_100_without_chroma_subsampling()
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: false);
        var input = TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);
        var output = dir.File("out.jpg");

        var result = await _engine.ConvertAsync(input, output, ImageFormat.Jpeg);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.False(result.IsLossless);
        Assert.Equal([ConversionWarning.LossyFormat], result.Warnings);
        using var jpeg = new MagickImage(output);
        Assert.Equal(100u, jpeg.Quality);
        Assert.Equal("1x1,1x1,1x1", jpeg.GetAttribute("jpeg:sampling-factor"));
    }

    [Fact]
    public async Task Jpeg_quality_option_is_used()
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: false);
        var input = TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);
        var output = dir.File("out.jpg");

        await _engine.ConvertAsync(input, output, ImageFormat.Jpeg, new ConversionOptions { JpegQuality = 85 });

        using var jpeg = new MagickImage(output);
        Assert.Equal(85u, jpeg.Quality);
    }

    [Fact]
    public async Task Transparent_pixels_are_filled_with_white_for_jpeg()
    {
        using var dir = new TempDir();
        using var source = new MagickImage(MagickColors.Transparent, 8, 8);
        var input = TestImages.Save(source, dir.Path, "clear.png", MagickFormat.Png);
        var output = dir.File("clear.jpg");

        var result = await _engine.ConvertAsync(input, output, ImageFormat.Jpeg);

        Assert.Contains(ConversionWarning.AlphaFlattened, result.Warnings);
        using var jpeg = new MagickImage(output);
        Assert.False(jpeg.HasAlpha);
        var pixel = jpeg.GetPixels().GetPixel(4, 4).ToColor()!;
        Assert.True(pixel.R > 65000 && pixel.G > 65000 && pixel.B > 65000, $"expected white, got {pixel}");
    }

    [Fact]
    public async Task Opaque_alpha_channel_is_not_reported_as_flattened()
    {
        using var dir = new TempDir();
        using var source = new MagickImage(MagickColors.Red, 8, 8);
        source.Alpha(AlphaAction.Set);
        var input = TestImages.Save(source, dir.Path, "opaque.png", MagickFormat.Png);

        var result = await _engine.ConvertAsync(input, dir.File("opaque.jpg"), ImageFormat.Jpeg);

        Assert.DoesNotContain(ConversionWarning.AlphaFlattened, result.Warnings);
    }

    [Fact]
    public async Task Invalid_jpeg_quality_fails_cleanly()
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: false);
        var input = TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);

        var result = await _engine.ConvertAsync(input, dir.File("out.jpg"), ImageFormat.Jpeg, new ConversionOptions { JpegQuality = 0 });

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.False(File.Exists(dir.File("out.jpg")));
    }
}
