using ImageMagick;

namespace ImageConverter.Engine.Tests;

public class FileHandlingTests
{
    private readonly ConversionEngine _engine = new();

    private static string PngInput(TempDir dir, string name = "in.png")
    {
        using var source = TestImages.Noise8(alpha: false);
        return TestImages.Save(source, dir.Path, name, MagickFormat.Png);
    }

    private static string[] LeftoverTempFiles(TempDir dir) =>
        Directory.GetFiles(dir.Path, "*.tmp", SearchOption.AllDirectories);

    [Fact]
    public async Task Existing_output_is_renamed_by_default()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);
        File.WriteAllText(dir.File("in.bmp"), "keep me");

        var result = await _engine.ConvertAsync(input, dir.File("in.bmp"), ImageFormat.Bmp);

        Assert.Equal(dir.File("in (1).bmp"), result.OutputPath);
        Assert.Equal("keep me", File.ReadAllText(dir.File("in.bmp")));
        Assert.Equal(ImageFormat.Bmp, _engine.DetectFormat(dir.File("in (1).bmp")));
    }

    [Fact]
    public async Task Existing_output_can_be_overwritten()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);
        File.WriteAllText(dir.File("in.bmp"), "old");

        var result = await _engine.ConvertAsync(input, dir.File("in.bmp"), ImageFormat.Bmp,
            new ConversionOptions { IfOutputExists = ExistingFileBehavior.Overwrite });

        Assert.Equal(dir.File("in.bmp"), result.OutputPath);
        Assert.Equal(ImageFormat.Bmp, _engine.DetectFormat(dir.File("in.bmp")));
    }

    [Fact]
    public async Task Existing_output_can_be_skipped()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);
        File.WriteAllText(dir.File("in.bmp"), "old");

        var result = await _engine.ConvertAsync(input, dir.File("in.bmp"), ImageFormat.Bmp,
            new ConversionOptions { IfOutputExists = ExistingFileBehavior.Skip });

        Assert.Equal(ConversionStatus.Skipped, result.Status);
        Assert.Null(result.OutputPath);
        Assert.Equal("old", File.ReadAllText(dir.File("in.bmp")));
    }

    [Fact]
    public async Task Existing_output_can_fail()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);
        File.WriteAllText(dir.File("in.bmp"), "old");

        var result = await _engine.ConvertAsync(input, dir.File("in.bmp"), ImageFormat.Bmp,
            new ConversionOptions { IfOutputExists = ExistingFileBehavior.Fail });

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal(ConversionError.OutputExists, result.Error);
    }

    [Fact]
    public async Task File_already_in_target_format_is_skipped_when_output_is_itself()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);
        var before = File.ReadAllBytes(input);

        var result = await _engine.ConvertAsync(input, input, ImageFormat.Png,
            new ConversionOptions { IfOutputExists = ExistingFileBehavior.Overwrite });

        Assert.Equal(ConversionStatus.Skipped, result.Status);
        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [Fact]
    public async Task Misnamed_file_is_converted_next_to_itself_without_touching_it()
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: false);
        var input = TestImages.Save(source, dir.Path, "really-jpeg.png", MagickFormat.Jpeg);
        var before = File.ReadAllBytes(input);

        var result = await _engine.ConvertAsync(input, input, ImageFormat.Png);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(dir.File("really-jpeg (1).png"), result.OutputPath);
        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [Fact]
    public async Task Never_overwrites_the_input_even_when_asked()
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: false);
        var input = TestImages.Save(source, dir.Path, "really-jpeg.png", MagickFormat.Jpeg);
        var before = File.ReadAllBytes(input);

        var result = await _engine.ConvertAsync(input, input, ImageFormat.Png,
            new ConversionOptions { IfOutputExists = ExistingFileBehavior.Overwrite });

        Assert.Equal(ConversionError.OutputIsInput, result.Error);
        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [Fact]
    public async Task Missing_input_is_reported()
    {
        using var dir = new TempDir();

        var result = await _engine.ConvertAsync(dir.File("nope.png"), dir.File("nope.bmp"), ImageFormat.Bmp);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal(ConversionError.InputNotFound, result.Error);
    }

    [Fact]
    public async Task Non_image_input_is_reported()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("notes.png"), "hello");

        var result = await _engine.ConvertAsync(dir.File("notes.png"), dir.File("notes.bmp"), ImageFormat.Bmp);

        Assert.Equal(ConversionError.UnsupportedInputFormat, result.Error);
    }

    [Fact]
    public async Task Corrupt_image_fails_without_leaving_files()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);
        var bytes = File.ReadAllBytes(input);
        File.WriteAllBytes(input, bytes.AsSpan(0, 60).ToArray());

        var result = await _engine.ConvertAsync(input, dir.File("out.bmp"), ImageFormat.Bmp);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal(ConversionError.DecodeFailed, result.Error);
        Assert.False(File.Exists(dir.File("out.bmp")));
        Assert.Empty(LeftoverTempFiles(dir));
    }

    [Fact]
    public async Task Heic_output_is_unsupported()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);

        var result = await _engine.ConvertAsync(input, dir.File("out.heic"), ImageFormat.Heic);

        Assert.Equal(ConversionError.UnsupportedOutputFormat, result.Error);
        Assert.False(File.Exists(dir.File("out.heic")));
    }

    [Fact]
    public async Task Cancelled_before_start_writes_nothing()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _engine.ConvertAsync(input, dir.File("out.bmp"), ImageFormat.Bmp, cancellationToken: cts.Token);

        Assert.Equal(ConversionStatus.Cancelled, result.Status);
        Assert.False(File.Exists(dir.File("out.bmp")));
        Assert.Empty(LeftoverTempFiles(dir));
    }

    [Fact]
    public async Task Creates_missing_output_folder()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);
        var output = Path.Combine(dir.Path, "a", "b", "out.webp");

        var result = await _engine.ConvertAsync(input, output, ImageFormat.WebP);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.True(File.Exists(output));
        Assert.Empty(LeftoverTempFiles(dir));
    }

    [Fact]
    public async Task Reports_progress_through_to_done()
    {
        using var dir = new TempDir();
        var input = PngInput(dir);
        var stages = new List<ConversionStage>();
        var progress = new SynchronousProgress(p => stages.Add(p.Stage));

        await _engine.ConvertAsync(input, dir.File("out.tif"), ImageFormat.Tiff, progress: progress);

        Assert.Equal(ConversionStage.Reading, stages[0]);
        Assert.Equal(ConversionStage.Done, stages[^1]);
    }

    [Fact]
    public async Task Extra_frames_are_reported()
    {
        using var dir = new TempDir();
        using var frames = new MagickImageCollection { TestImages.Noise8(false, 1), TestImages.Noise8(false, 2) };
        var input = dir.File("pages.tif");
        frames.Write(input, MagickFormat.Tiff);

        var result = await _engine.ConvertAsync(input, dir.File("pages.png"), ImageFormat.Png);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Contains(ConversionWarning.ExtraFramesDropped, result.Warnings);
        Assert.False(result.IsLossless);
        TestImages.AssertSamePixels((MagickImage)frames[0], dir.File("pages.png"));
    }

    [Fact]
    public async Task Cmyk_to_png_is_converted_and_reported()
    {
        using var dir = new TempDir();
        using var source = TestImages.Noise8(alpha: false);
        source.ColorSpace = ColorSpace.CMYK;
        var input = TestImages.Save(source, dir.Path, "cmyk.jpg", MagickFormat.Jpeg);

        var result = await _engine.ConvertAsync(input, dir.File("cmyk.png"), ImageFormat.Png);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Contains(ConversionWarning.ColorSpaceConverted, result.Warnings);
    }

    [Fact]
    public async Task Parallel_conversions_on_one_instance_all_succeed()
    {
        using var dir = new TempDir();
        var inputs = Enumerable.Range(0, 8).Select(i => PngInput(dir, $"in{i}.png")).ToArray();

        var results = await Task.WhenAll(inputs.Select(i => _engine.ConvertAsync(i, OutputPaths.ForTarget(i, ImageFormat.WebP), ImageFormat.WebP)));

        Assert.All(results, r => Assert.Equal(ConversionStatus.Succeeded, r.Status));
    }

    private sealed class SynchronousProgress(Action<ConversionProgress> handler) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => handler(value);
    }
}
