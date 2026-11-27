using ImageMagick;

namespace ImageConverter.Engine.Tests;

public class ResizeTests
{
    private readonly ConversionEngine _engine = new();

    private static string Input(TempDir dir, uint width, uint height, string name = "in.png")
    {
        using var image = new MagickImage(MagickColors.Coral, width, height);
        var path = dir.File(name);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    private static (uint Width, uint Height) SizeOf(string path)
    {
        using var image = new MagickImage(path);
        return (image.Width, image.Height);
    }

    [Fact]
    public async Task Max_size_shrinks_the_longer_side_and_keeps_the_ratio()
    {
        using var dir = new TempDir();
        var input = Input(dir, 400, 200);

        var result = await _engine.ConvertAsync(input, dir.File("in.bmp"), ImageFormat.Bmp, new ConversionOptions { MaxSize = 100 });

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal((100u, 50u), SizeOf(result.OutputPath!));
        Assert.Contains(ConversionWarning.Resized, result.Warnings);
        Assert.False(result.IsLossless);
    }

    [Fact]
    public async Task Max_size_never_enlarges()
    {
        using var dir = new TempDir();
        var input = Input(dir, 40, 20);

        var result = await _engine.ConvertAsync(input, dir.File("in.bmp"), ImageFormat.Bmp, new ConversionOptions { MaxSize = 1920 });

        Assert.Equal((40u, 20u), SizeOf(result.OutputPath!));
        Assert.DoesNotContain(ConversionWarning.Resized, result.Warnings);
        Assert.True(result.IsLossless);
    }

    [Fact]
    public async Task Percent_scales_both_sides()
    {
        using var dir = new TempDir();
        var input = Input(dir, 200, 101);

        var result = await _engine.ConvertAsync(input, dir.File("in.webp"), ImageFormat.WebP, new ConversionOptions { ScalePercent = 50 });

        Assert.Equal((100u, 51u), SizeOf(result.OutputPath!));
        Assert.Contains(ConversionWarning.Resized, result.Warnings);
    }

    [Fact]
    public async Task Same_format_is_resized_instead_of_copied()
    {
        using var dir = new TempDir();
        var input = Input(dir, 300, 300);

        var result = await _engine.ConvertAsync(input, dir.File(Path.Combine("out", "in.png")), ImageFormat.Png, new ConversionOptions { MaxSize = 30 });

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal((30u, 30u), SizeOf(result.OutputPath!));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    [InlineData(null, 101)]
    public async Task Rejects_bad_sizes(int? maxSize, int? percent)
    {
        using var dir = new TempDir();
        var input = Input(dir, 10, 10);

        var result = await _engine.ConvertAsync(input, dir.File("in.bmp"), ImageFormat.Bmp,
            new ConversionOptions { MaxSize = maxSize, ScalePercent = percent });

        Assert.Equal(ConversionError.InvalidOptions, result.Error);
    }
}
