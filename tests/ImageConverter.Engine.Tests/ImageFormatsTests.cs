using ImageConverter.Engine;

namespace ImageConverter.Engine.Tests;

public class ImageFormatsTests
{
    [Theory]
    [InlineData("png", ImageFormat.Png)]
    [InlineData("PNG", ImageFormat.Png)]
    [InlineData(".png", ImageFormat.Png)]
    [InlineData("jpg", ImageFormat.Jpeg)]
    [InlineData("jpeg", ImageFormat.Jpeg)]
    [InlineData("bmp", ImageFormat.Bmp)]
    [InlineData("tif", ImageFormat.Tiff)]
    [InlineData("tiff", ImageFormat.Tiff)]
    [InlineData("webp", ImageFormat.WebP)]
    [InlineData("avif", ImageFormat.Avif)]
    [InlineData("heic", ImageFormat.Heic)]
    [InlineData("heif", ImageFormat.Heic)]
    public void TryParse_accepts_ids_and_extensions(string text, ImageFormat expected)
    {
        Assert.True(ImageFormats.TryParse(text, out var format));
        Assert.Equal(expected, format);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gif")]
    [InlineData("svg")]
    public void TryParse_rejects_unknown(string text)
    {
        Assert.False(ImageFormats.TryParse(text, out _));
    }

    [Fact]
    public void Jpeg_is_the_only_lossy_writable_format()
    {
        var lossy = ImageFormats.All.Where(f => f.CanWrite && !f.IsLosslessOutput).Select(f => f.Format);
        Assert.Equal([ImageFormat.Jpeg], lossy);
    }

    [Fact]
    public void Heic_is_read_only()
    {
        var heic = ImageFormats.Get(ImageFormat.Heic);
        Assert.True(heic.CanRead);
        Assert.False(heic.CanWrite);
    }

    [Fact]
    public void Every_format_has_a_default_extension_and_id()
    {
        foreach (var f in ImageFormats.All)
        {
            Assert.StartsWith(".", f.Extensions[0]);
            Assert.Equal(f.Id, f.Id.ToLowerInvariant());
        }
        Assert.Equal(".jpg", ImageFormats.Get(ImageFormat.Jpeg).Extensions[0]);
        Assert.Equal(".tif", ImageFormats.Get(ImageFormat.Tiff).Extensions[0]);
    }

    [Theory]
    [InlineData(@"C:\a\IMG_1.HEIC", ImageFormat.Heic)]
    [InlineData("/a/b.jpeg", ImageFormat.Jpeg)]
    [InlineData("x.webp", ImageFormat.WebP)]
    public void FromExtension_maps_known_extensions(string path, ImageFormat expected)
    {
        Assert.Equal(expected, ImageFormats.FromExtension(path));
    }

    [Fact]
    public void FromExtension_returns_null_for_unknown()
    {
        Assert.Null(ImageFormats.FromExtension("a.txt"));
        Assert.Null(ImageFormats.FromExtension("noext"));
    }
}
