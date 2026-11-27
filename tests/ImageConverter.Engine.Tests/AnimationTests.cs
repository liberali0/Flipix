using ImageMagick;

namespace ImageConverter.Engine.Tests;

public class AnimationTests
{
    private readonly ConversionEngine _engine = new();

    // Three frames: a full red canvas, then a small blue square drawn on top, then a green square.
    // GIF frames 2 and 3 are smaller than the canvas, so only a coalesced read shows them as on screen.
    private static string AnimatedGif(TempDir dir)
    {
        using var frames = new MagickImageCollection();
        var first = new MagickImage(MagickColors.Red, 20, 10);
        first.AnimationDelay = 10;
        frames.Add(first);
        var blue = new MagickImage(MagickColors.Blue, 4, 4) { AnimationDelay = 10, GifDisposeMethod = GifDisposeMethod.None };
        blue.Page = new MagickGeometry(2, 2, 4, 4);
        frames.Add(blue);
        var green = new MagickImage(MagickColors.Lime, 4, 4) { AnimationDelay = 10, GifDisposeMethod = GifDisposeMethod.None };
        green.Page = new MagickGeometry(10, 4, 4, 4);
        frames.Add(green);
        var path = dir.File("anim.gif");
        frames.Write(path, MagickFormat.Gif);
        return path;
    }

    private static IMagickColor<ushort> PixelAt(string path, int x, int y)
    {
        using var image = new MagickImage(path);
        using var pixels = image.GetPixels();
        return pixels.GetPixel(x, y).ToColor()!;
    }

    [Fact]
    public void Gif_is_detected_and_read_only()
    {
        using var dir = new TempDir();
        Assert.Equal(ImageFormat.Gif, _engine.DetectFormat(AnimatedGif(dir)));
        var gif = ImageFormats.Get(ImageFormat.Gif);
        Assert.True(gif.CanRead);
        Assert.False(gif.CanWrite);
    }

    [Fact]
    public async Task First_frame_by_default_with_a_warning()
    {
        using var dir = new TempDir();
        var input = AnimatedGif(dir);

        var result = await _engine.ConvertAsync(input, dir.File("anim.png"), ImageFormat.Png);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(dir.File("anim.png"), result.OutputPath);
        Assert.Equal(0, result.FrameCount);
        Assert.Contains(ConversionWarning.ExtraFramesDropped, result.Warnings);
        Assert.False(result.IsLossless);
        using var png = new MagickImage(result.OutputPath!);
        Assert.Equal(20u, png.Width);
        Assert.Equal(10u, png.Height);
    }

    [Fact]
    public async Task All_frames_go_into_a_folder_as_shown_on_screen()
    {
        using var dir = new TempDir();
        var input = AnimatedGif(dir);

        var result = await _engine.ConvertAsync(input, dir.File("anim.png"), ImageFormat.Png,
            new ConversionOptions { AllFrames = true, Verify = true });

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(dir.File("anim_frames"), result.OutputPath);
        Assert.Equal(3, result.FrameCount);
        Assert.True(result.IsLossless);
        Assert.Equal(true, result.Verification?.Identical);
        Assert.DoesNotContain(ConversionWarning.ExtraFramesDropped, result.Warnings);
        var files = Directory.GetFiles(result.OutputPath!).Select(p => Path.GetFileName(p)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "anim_001.png", "anim_002.png", "anim_003.png" }, files);

        // Every frame has the full canvas size, and later frames still show what was drawn before them.
        foreach (var file in files)
        {
            using var frame = new MagickImage(Path.Combine(result.OutputPath!, file));
            Assert.Equal(20u, frame.Width);
            Assert.Equal(10u, frame.Height);
        }

        var third = Path.Combine(result.OutputPath!, "anim_003.png");
        Assert.Equal(MagickColors.Red, PixelAt(third, 0, 0));
        Assert.Equal(MagickColors.Blue, PixelAt(third, 3, 3));
        Assert.Equal(MagickColors.Lime, PixelAt(third, 11, 5));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Existing_frames_folder_is_renamed()
    {
        using var dir = new TempDir();
        var input = AnimatedGif(dir);
        Directory.CreateDirectory(dir.File("anim_frames"));

        var result = await _engine.ConvertAsync(input, dir.File("anim.png"), ImageFormat.Png, new ConversionOptions { AllFrames = true });

        Assert.Equal(dir.File("anim_frames (1)"), result.OutputPath);
        Assert.Empty(Directory.GetFiles(dir.File("anim_frames")));
    }

    [Fact]
    public async Task Still_gif_converts_to_one_file_even_with_all_frames()
    {
        using var dir = new TempDir();
        using (var still = new MagickImage(MagickColors.Red, 8, 8))
        {
            still.Write(dir.File("still.gif"), MagickFormat.Gif);
        }

        var result = await _engine.ConvertAsync(dir.File("still.gif"), dir.File("still.png"), ImageFormat.Png,
            new ConversionOptions { AllFrames = true });

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(dir.File("still.png"), result.OutputPath);
        Assert.Equal(0, result.FrameCount);
        Assert.DoesNotContain(ConversionWarning.ExtraFramesDropped, result.Warnings);
    }
}
