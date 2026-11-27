using System.Text;
using System.Text.Json;
using ImageConverter.Engine;
using ImageMagick;

namespace ImageConverter.Cli.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "imgconv-cli-tests", Guid.NewGuid().ToString("N"));
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public CliTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string File(string name) => Path.Combine(_dir, name);

    private string Image(string name, MagickFormat format = MagickFormat.Png)
    {
        using var image = new MagickImage(MagickColors.Coral, 7, 5);
        var path = File(name);
        image.Write(path, format);
        return path;
    }

    private Task<int> Run(params string[] args) => CliApp.RunAsync(args, _out, _err, CancellationToken.None);

    [Fact]
    public async Task Converts_next_to_source_and_exits_0()
    {
        var a = Image("a.png");
        var b = Image("b.tif", MagickFormat.Tiff);

        var code = await Run("convert", "--to", "webp", a, b);

        Assert.Equal(0, code);
        Assert.True(System.IO.File.Exists(File("a.webp")));
        Assert.True(System.IO.File.Exists(File("b.webp")));
        Assert.Contains($"OK {a} -> {File("a.webp")}", _out.ToString());
        Assert.Equal("", _err.ToString());
    }

    [Fact]
    public async Task Never_overwrites_by_default()
    {
        var a = Image("a.png");
        System.IO.File.WriteAllText(File("a.bmp"), "keep");

        var code = await Run("convert", "--to", "bmp", a);

        Assert.Equal(0, code);
        Assert.Equal("keep", System.IO.File.ReadAllText(File("a.bmp")));
        Assert.True(System.IO.File.Exists(File("a (1).bmp")));
    }

    [Fact]
    public async Task Response_file_supplies_paths_with_bom_and_blank_lines()
    {
        var a = Image("가 나.png");
        var b = Image("b.png");
        var list = File("list.txt");
        System.IO.File.WriteAllText(list, $"{a}\r\n\r\n{b}\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var code = await Run("convert", "--to", "jpg", "@" + list);

        Assert.Equal(0, code);
        Assert.True(System.IO.File.Exists(File("가 나.jpg")));
        Assert.True(System.IO.File.Exists(File("b.jpg")));
    }

    [Fact]
    public async Task Failed_file_gives_exit_1_and_one_stderr_line_while_others_convert()
    {
        var good = Image("good.png");
        var missing = File("missing.png");

        var code = await Run("convert", "--to", "bmp", good, missing);

        Assert.Equal(1, code);
        Assert.True(System.IO.File.Exists(File("good.bmp")));
        var lines = _err.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.StartsWith($"ERROR {missing}: ", lines[0]);
    }

    [Fact]
    public async Task Out_dir_collects_outputs()
    {
        var a = Image("a.png");
        var outDir = File("converted");

        var code = await Run("convert", "--to", "tiff", "--out-dir", outDir, a);

        Assert.Equal(0, code);
        Assert.True(System.IO.File.Exists(Path.Combine(outDir, "a.tif")));
    }

    [Fact]
    public async Task Json_output_is_one_object_per_line_with_summary()
    {
        var a = Image("a.png");

        var code = await Run("convert", "--to", "jpg", "--json", a);

        Assert.Equal(0, code);
        var lines = _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using var result = JsonDocument.Parse(lines[0]);
        Assert.Equal("result", result.RootElement.GetProperty("type").GetString());
        Assert.Equal("Succeeded", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("png", result.RootElement.GetProperty("sourceFormat").GetString());
        Assert.Equal("jpg", result.RootElement.GetProperty("targetFormat").GetString());
        Assert.False(result.RootElement.GetProperty("lossless").GetBoolean());
        Assert.Equal("LossyFormat", result.RootElement.GetProperty("warnings")[0].GetString());
        using var summary = JsonDocument.Parse(lines[1]);
        Assert.Equal("summary", summary.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, summary.RootElement.GetProperty("succeeded").GetInt32());
    }

    [Fact]
    public async Task Verify_flag_reports_verification()
    {
        var a = Image("a.png");

        await Run("convert", "--to", "webp", "--verify", "--json", a);

        using var result = JsonDocument.Parse(_out.ToString().Split('\n')[0]);
        Assert.True(result.RootElement.GetProperty("verified").GetBoolean());
    }

    [Theory]
    [InlineData("convert", "a.png")]
    [InlineData("convert", "--to", "png")]
    [InlineData("convert", "--to", "heic", "a.png")]
    [InlineData("convert", "--to", "gif", "a.png")]
    [InlineData("convert", "--to", "png", "--if-exists", "maybe", "a.png")]
    [InlineData("convert", "--to", "png", "--jpeg-quality", "101", "a.png")]
    [InlineData("convert", "--to", "png", "--bogus", "a.png")]
    [InlineData("convert", "--to", "png", "@missing-list.txt")]
    [InlineData("frobnicate")]
    [InlineData()]
    public async Task Usage_errors_exit_2(params string[] args)
    {
        var code = await Run(args);

        Assert.Equal(2, code);
        Assert.NotEqual("", _err.ToString());
    }

    [Fact]
    public async Task Formats_json_lists_every_format()
    {
        var code = await Run("formats", "--json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(_out.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToArray();
        Assert.Equal(ImageFormats.All.Select(f => f.Id), ids);
    }

    [Fact]
    public async Task Detect_prints_format_from_content()
    {
        var a = Image("really-a-png.jpg");
        var junk = File("junk.png");
        System.IO.File.WriteAllText(junk, "x");

        var code = await Run("detect", a, junk);

        Assert.Equal(0, code);
        Assert.Contains($"{a}: png", _out.ToString());
        Assert.Contains($"{junk}: unknown", _out.ToString());
    }

    [Fact]
    public async Task Cancellation_exits_130()
    {
        var a = Image("a.png");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var code = await CliApp.RunAsync(["convert", "--to", "bmp", a], _out, _err, cts.Token);

        Assert.Equal(130, code);
    }

    [Fact]
    public async Task Overwrite_never_replaces_another_input_of_the_same_batch()
    {
        var jpg = Image("a.jpg", MagickFormat.Jpeg);
        var png = Image("a.png");
        var before = System.IO.File.ReadAllBytes(png);

        var code = await Run("convert", "--to", "png", "--if-exists", "overwrite", jpg, png);

        Assert.Equal(0, code); // a.png is already PNG, so it is skipped
        Assert.Equal(before, System.IO.File.ReadAllBytes(png));
        Assert.True(System.IO.File.Exists(File("a (1).png")));
    }

    [Fact]
    public async Task Inputs_with_the_same_base_name_get_distinct_outputs()
    {
        var png = Image("a.png");
        var tif = Image("a.tif", MagickFormat.Tiff);

        var code = await Run("convert", "--to", "webp", "--if-exists", "overwrite", png, tif);

        Assert.Equal(0, code);
        Assert.True(System.IO.File.Exists(File("a.webp")));
        Assert.True(System.IO.File.Exists(File("a (1).webp")));
    }

    [Fact]
    public async Task Response_file_lines_may_be_quoted()
    {
        var a = Image("a b.png");
        var list = File("list.txt");
        System.IO.File.WriteAllText(list, $"\"{a}\"\n");

        var code = await Run("convert", "--to", "bmp", "@" + list);

        Assert.Equal(0, code);
        Assert.True(System.IO.File.Exists(File("a b.bmp")));
    }

    [Fact]
    public async Task Delete_source_moves_original_to_recycle_bin_after_success()
    {
        // Real shell call on Windows; plain delete elsewhere.
        var a = Image("a.png");

        var code = await Run("convert", "--to", "webp", "--delete-source", a);

        Assert.Equal(0, code);
        Assert.False(System.IO.File.Exists(a));
        Assert.True(System.IO.File.Exists(File("a.webp")));
        Assert.Contains("[verified]", _out.ToString());
        Assert.Contains("[source recycled]", _out.ToString());
        Assert.Equal("", _err.ToString());
    }

    [Fact]
    public async Task Sources_stay_without_delete_source()
    {
        var a = Image("a.png");

        await Run("convert", "--to", "webp", a);

        Assert.True(System.IO.File.Exists(a));
    }

    [Fact]
    public async Task Delete_source_keeps_failed_and_skipped_files()
    {
        var recycled = new List<string>();
        var previous = SourceRecycler.Recycle;
        SourceRecycler.Recycle = path => { recycled.Add(path); return null; };
        try
        {
            var good = Image("good.png");
            var already = Image("already.webp", MagickFormat.WebP);
            var broken = File("broken.png");
            System.IO.File.WriteAllText(broken, "not an image");

            var code = await Run("convert", "--to", "webp", "--delete-source", "--json", good, already, broken);

            Assert.Equal(1, code);
            Assert.Equal(new[] { good }, recycled);
            var lines = _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => JsonDocument.Parse(l).RootElement).ToArray();
            var goodLine = lines.Single(l => l.GetProperty("type").GetString() == "result" && l.GetProperty("input").GetString() == good);
            Assert.True(goodLine.GetProperty("sourceDeleted").GetBoolean());
        }
        finally
        {
            SourceRecycler.Recycle = previous;
        }
    }

    [Fact]
    public async Task Failure_to_recycle_keeps_output_and_reports_error()
    {
        var previous = SourceRecycler.Recycle;
        SourceRecycler.Recycle = _ => "locked by another program";
        try
        {
            var a = Image("a.png");

            var code = await Run("convert", "--to", "bmp", "--delete-source", a);

            Assert.Equal(1, code);
            Assert.True(System.IO.File.Exists(a));
            Assert.True(System.IO.File.Exists(File("a.bmp")));
            Assert.Contains($"ERROR {a}: Converted to {File("a.bmp")}, but the original was kept: locked by another program", _err.ToString());
        }
        finally
        {
            SourceRecycler.Recycle = previous;
        }
    }

    [Fact]
    public async Task Move_source_to_moves_original_after_success()
    {
        var a = Image("a.png");
        var keep = File("원본 보관");

        var code = await Run("convert", "--to", "webp", "--move-source-to", keep, a);

        Assert.Equal(0, code);
        Assert.False(System.IO.File.Exists(a));
        Assert.True(System.IO.File.Exists(Path.Combine(keep, "a.png")));
        Assert.True(System.IO.File.Exists(File("a.webp")));
        Assert.Contains($"[source moved to {Path.Combine(keep, "a.png")}]", _out.ToString());
        Assert.Equal("", _err.ToString());
    }

    [Fact]
    public async Task Move_source_to_keeps_failed_and_skipped_files_and_never_overwrites()
    {
        var keep = File("keep");
        Directory.CreateDirectory(keep);
        System.IO.File.WriteAllText(Path.Combine(keep, "good.png"), "older file");
        var good = Image("good.png");
        var already = Image("already.webp", MagickFormat.WebP);
        var broken = File("broken.png");
        System.IO.File.WriteAllText(broken, "not an image");

        var code = await Run("convert", "--to", "webp", "--move-source-to", keep, "--json", good, already, broken);

        Assert.Equal(1, code);
        Assert.Equal("older file", System.IO.File.ReadAllText(Path.Combine(keep, "good.png")));
        Assert.True(System.IO.File.Exists(Path.Combine(keep, "good (1).png")));
        Assert.False(System.IO.File.Exists(good));
        Assert.True(System.IO.File.Exists(already));
        Assert.True(System.IO.File.Exists(broken));
        var goodLine = _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l).RootElement)
            .Single(l => l.GetProperty("type").GetString() == "result" && l.GetProperty("input").GetString() == good);
        Assert.Equal(Path.Combine(keep, "good (1).png"), goodLine.GetProperty("sourceMovedTo").GetString());
    }

    [Fact]
    public async Task Delete_and_move_source_together_is_a_usage_error()
    {
        var a = Image("a.png");

        var code = await Run("convert", "--to", "webp", "--delete-source", "--move-source-to", File("keep"), a);

        Assert.Equal(2, code);
        Assert.True(System.IO.File.Exists(a));
    }

    private string AnimatedGif(string name)
    {
        using var frames = new MagickImageCollection();
        frames.Add(new MagickImage(MagickColors.Red, 6, 4) { AnimationDelay = 10 });
        frames.Add(new MagickImage(MagickColors.Blue, 6, 4) { AnimationDelay = 10 });
        var path = File(name);
        frames.Write(path, MagickFormat.Gif);
        return path;
    }

    [Fact]
    public async Task Animated_gif_first_frame_keeps_the_original_even_with_delete_source()
    {
        var previous = SourceRecycler.Recycle;
        var recycled = new List<string>();
        SourceRecycler.Recycle = p => { recycled.Add(p); return null; };
        try
        {
            var gif = AnimatedGif("anim.gif");

            var code = await Run("convert", "--to", "png", "--delete-source", gif);

            Assert.Equal(0, code);
            Assert.True(System.IO.File.Exists(File("anim.png")));
            Assert.Empty(recycled);
        }
        finally
        {
            SourceRecycler.Recycle = previous;
        }
    }

    [Fact]
    public async Task All_frames_writes_a_folder_and_then_the_original_can_go()
    {
        var previous = SourceRecycler.Recycle;
        var recycled = new List<string>();
        SourceRecycler.Recycle = p => { recycled.Add(p); return null; };
        try
        {
            var gif = AnimatedGif("anim.gif");

            var code = await Run("convert", "--to", "png", "--all-frames", "--delete-source", gif);

            Assert.Equal(0, code);
            Assert.True(System.IO.File.Exists(File(Path.Combine("anim_frames", "anim_001.png"))));
            Assert.True(System.IO.File.Exists(File(Path.Combine("anim_frames", "anim_002.png"))));
            Assert.Contains("[2 frames]", _out.ToString());
            Assert.Equal(new[] { gif }, recycled);
        }
        finally
        {
            SourceRecycler.Recycle = previous;
        }
    }
}
