using ImageConverter.Core.Batch;

namespace ImageConverter.Core.Tests;

public class OutputPathResolverTests
{
    [Fact]
    public void Same_folder_changes_extension()
    {
        using var d = new TempDir();
        var input = d.Touch("a.jpg");

        var r = new OutputPathResolver(new OutputOptions()).Resolve(input, null, "png");

        Assert.Equal(Path.Combine(d.Path, "a.png"), r);
    }

    [Fact]
    public void Never_overwrites_the_source_even_with_overwrite_policy()
    {
        using var d = new TempDir();
        var input = d.Touch("a.png");

        var r = new OutputPathResolver(new OutputOptions { Conflict = ConflictPolicy.Overwrite }).Resolve(input, null, "png");

        Assert.Equal(Path.Combine(d.Path, "a (1).png"), r);
    }

    [Fact]
    public void Rename_picks_next_free_name()
    {
        using var d = new TempDir();
        var input = d.Touch("a.jpg"); d.Touch("a.png"); d.Touch("a (1).png");

        var r = new OutputPathResolver(new OutputOptions()).Resolve(input, null, "png");

        Assert.Equal(Path.Combine(d.Path, "a (2).png"), r);
    }

    [Fact]
    public void Skip_returns_null_when_target_exists()
    {
        using var d = new TempDir();
        var input = d.Touch("a.jpg"); d.Touch("a.png");

        Assert.Null(new OutputPathResolver(new OutputOptions { Conflict = ConflictPolicy.Skip }).Resolve(input, null, "png"));
    }

    [Fact]
    public void Overwrite_returns_existing_target()
    {
        using var d = new TempDir();
        var input = d.Touch("a.jpg"); var existing = d.Touch("a.png");

        var r = new OutputPathResolver(new OutputOptions { Conflict = ConflictPolicy.Overwrite }).Resolve(input, null, "png");

        Assert.Equal(existing, r);
    }

    [Fact]
    public void Same_batch_collisions_are_renamed()
    {
        using var d = new TempDir();
        var a = d.Touch("a.jpg"); var b = d.Touch("a.bmp");
        var resolver = new OutputPathResolver(new OutputOptions { Conflict = ConflictPolicy.Overwrite });

        var r1 = resolver.Resolve(a, null, "png");
        var r2 = resolver.Resolve(b, null, "png");

        Assert.NotEqual(r1, r2);
    }

    [Fact]
    public void Custom_folder_preserves_subfolder_structure()
    {
        using var src = new TempDir(); using var dst = new TempDir();
        var input = src.Touch("sub/deep/a.jpg");
        var opts = new OutputOptions { CustomFolder = dst.Path, PreserveStructure = true };

        var r = new OutputPathResolver(opts).Resolve(input, Path.GetFullPath(src.Path), "png");

        Assert.Equal(Path.Combine(dst.Path, "sub", "deep", "a.png"), r);
    }

    [Fact]
    public void Overwrite_never_clobbers_another_input_in_the_batch()
    {
        using var d = new TempDir();
        var jpg = d.Touch("a.jpg"); var png = d.Touch("a.png");
        var resolver = new OutputPathResolver(new OutputOptions { Conflict = ConflictPolicy.Overwrite }, new[] { jpg, png });

        var r = resolver.Resolve(jpg, null, "png");

        Assert.Equal(Path.Combine(d.Path, "a (1).png"), r);
    }

    [Fact]
    public void Folder_names_starting_with_dots_are_not_treated_as_outside()
    {
        using var src = new TempDir(); using var dst = new TempDir();
        var input = src.Touch("..backup/a.jpg");
        var opts = new OutputOptions { CustomFolder = dst.Path, PreserveStructure = true };

        var r = new OutputPathResolver(opts).Resolve(input, Path.GetFullPath(src.Path), "png");

        Assert.Equal(Path.Combine(dst.Path, "..backup", "a.png"), r);
    }
}
