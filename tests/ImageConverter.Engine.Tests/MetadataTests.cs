using ImageMagick;

namespace ImageConverter.Engine.Tests;

public class MetadataTests
{
    private readonly ConversionEngine _engine = new();

    private const string Xmp =
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description xmlns:dc=\"http://purl.org/dc/elements/1.1/\" dc:format=\"test-xmp-marker\"/></rdf:RDF></x:xmpmeta>";

    private static string? XmpText(MagickImage image) =>
        image.GetXmpProfile() is { } xmp ? System.Text.Encoding.UTF8.GetString(xmp.ToByteArray()) : null;

    private static MagickImage TaggedSource(Orientation orientation = Orientation.TopLeft)
    {
        var image = TestImages.Noise8(alpha: false);
        image.SetProfile(ColorProfiles.AdobeRGB1998);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "TestCam");
        exif.SetValue(ExifTag.Orientation, (ushort)orientation);
        image.SetProfile(exif);
        image.SetProfile(new XmpProfile(System.Text.Encoding.UTF8.GetBytes(Xmp)));
        image.Orientation = orientation;
        return image;
    }

    [Theory]
    [InlineData(ImageFormat.Png)]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.WebP)]
    [InlineData(ImageFormat.Avif)]
    public async Task Exif_and_icc_are_kept(ImageFormat target)
    {
        using var dir = new TempDir();
        using var source = TaggedSource();
        var input = target == ImageFormat.Png
            ? TestImages.Save(source, dir.Path, "in.jpg", MagickFormat.Jpeg)
            : TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);
        var output = OutputPaths.ForTarget(input, target);

        var result = await _engine.ConvertAsync(input, output, target);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.DoesNotContain(ConversionWarning.MetadataDropped, result.Warnings);
        using var written = new MagickImage(output);
        Assert.Equal("TestCam", written.GetExifProfile()?.GetValue(ExifTag.Make)?.Value);
        Assert.Equal(ColorProfiles.AdobeRGB1998.ToByteArray(), written.GetColorProfile()?.ToByteArray());
        Assert.Contains("test-xmp-marker", XmpText(written));
    }

    [Fact]
    public async Task Tiff_keeps_icc_and_xmp_but_reports_dropped_exif()
    {
        // The TIFF encoder in Magick.NET does not write an EXIF directory.
        using var dir = new TempDir();
        using var source = TaggedSource();
        var input = TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);
        var output = dir.File("out.tif");

        var result = await _engine.ConvertAsync(input, output, ImageFormat.Tiff);

        Assert.Contains(ConversionWarning.MetadataDropped, result.Warnings);
        Assert.True(result.IsLossless);
        using var written = new MagickImage(output);
        Assert.Equal(ColorProfiles.AdobeRGB1998.ToByteArray(), written.GetColorProfile()?.ToByteArray());
        Assert.Contains("test-xmp-marker", XmpText(written));
    }

    [Fact]
    public async Task Bmp_reports_dropped_exif()
    {
        using var dir = new TempDir();
        using var source = TaggedSource();
        var input = TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);

        var result = await _engine.ConvertAsync(input, dir.File("out.bmp"), ImageFormat.Bmp);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Contains(ConversionWarning.MetadataDropped, result.Warnings);
        Assert.True(result.IsLossless);
    }

    [Fact]
    public async Task No_metadata_option_strips_everything()
    {
        using var dir = new TempDir();
        using var source = TaggedSource();
        var input = TestImages.Save(source, dir.Path, "in.jpg", MagickFormat.Jpeg);
        var output = dir.File("out.png");

        await _engine.ConvertAsync(input, output, ImageFormat.Png, new ConversionOptions { PreserveMetadata = false });

        using var written = new MagickImage(output);
        Assert.Null(written.GetExifProfile());
        Assert.Null(written.GetColorProfile());
    }

    [Fact]
    public async Task Orientation_tag_is_kept_without_rotating_pixels()
    {
        using var dir = new TempDir();
        using var source = TaggedSource(Orientation.RightTop);
        var input = TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);
        var output = dir.File("out.webp");

        await _engine.ConvertAsync(input, output, ImageFormat.WebP);

        using var written = new MagickImage(output);
        Assert.Equal(Orientation.RightTop, written.Orientation);
        TestImages.AssertSamePixels(source, output);
    }

    [Fact]
    public async Task Bmp_gets_upright_pixels_because_it_cannot_store_orientation()
    {
        using var dir = new TempDir();
        using var source = TaggedSource(Orientation.RightTop);
        var input = TestImages.Save(source, dir.Path, "in.png", MagickFormat.Png);
        var output = dir.File("out.bmp");

        await _engine.ConvertAsync(input, output, ImageFormat.Bmp);

        using var upright = (MagickImage)source.Clone();
        upright.AutoOrient();
        TestImages.AssertSamePixels(upright, output);
        Assert.Equal((uint)TestImages.Height, upright.Width);
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.Png)]
    public async Task Heic_exif_orientation_is_not_applied_twice(ImageFormat target)
    {
        // HEIF rotation lives in the irot box and is applied while decoding; a stale EXIF orientation must not survive.
        using var dir = new TempDir();
        var output = dir.File("out" + ImageFormats.Get(target).Extensions[0]);

        await _engine.ConvertAsync(Fixtures.Path("exif-orientation-6.heic"), output, target);

        using var written = new MagickImage(output);
        Assert.Equal(Orientation.TopLeft, written.Orientation);
    }
}
