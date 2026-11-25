using ImageMagick;

namespace ImageConverter.Engine.Tests;

public class DetectFormatTests
{
    private readonly ConversionEngine _engine = new();

    [Theory]
    [InlineData(MagickFormat.Png, ImageFormat.Png)]
    [InlineData(MagickFormat.Jpeg, ImageFormat.Jpeg)]
    [InlineData(MagickFormat.Bmp, ImageFormat.Bmp)]
    [InlineData(MagickFormat.Tiff, ImageFormat.Tiff)]
    [InlineData(MagickFormat.WebP, ImageFormat.WebP)]
    [InlineData(MagickFormat.Avif, ImageFormat.Avif)]
    public void Detects_format_from_content_not_extension(MagickFormat written, ImageFormat expected)
    {
        using var dir = new TempDir();
        using var image = TestImages.Noise8(alpha: false);
        var path = TestImages.Save(image, dir.Path, "misnamed.txt", written);

        Assert.Equal(expected, _engine.DetectFormat(path));
    }

    [Fact]
    public void Detects_heic()
    {
        Assert.Equal(ImageFormat.Heic, _engine.DetectFormat(Fixtures.Path("photo.heic")));
    }

    [Fact]
    public void Returns_null_for_non_images_and_missing_files()
    {
        using var dir = new TempDir();
        var text = dir.File("a.png");
        File.WriteAllText(text, "not an image at all");

        Assert.Null(_engine.DetectFormat(text));
        Assert.Null(_engine.DetectFormat(dir.File("missing.png")));
    }
}
