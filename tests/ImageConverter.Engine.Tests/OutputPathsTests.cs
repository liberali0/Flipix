namespace ImageConverter.Engine.Tests;

public class OutputPathsTests
{
    [Fact]
    public void For_target_keeps_folder_and_base_name_and_uses_default_extension()
    {
        var input = Path.Combine("photos", "IMG_0001.HEIC");
        Assert.Equal(Path.Combine("photos", "IMG_0001.jpg"), OutputPaths.ForTarget(input, ImageFormat.Jpeg));
        Assert.Equal(Path.Combine("photos", "IMG_0001.tif"), OutputPaths.ForTarget(input, ImageFormat.Tiff));
    }

    [Fact]
    public void For_target_can_redirect_to_another_folder()
    {
        var input = Path.Combine("photos", "a.png");
        Assert.Equal(Path.Combine("out", "a.webp"), OutputPaths.ForTarget(input, ImageFormat.WebP, "out"));
    }

    [Fact]
    public void Next_free_returns_path_unchanged_when_free()
    {
        using var dir = new TempDir();
        Assert.Equal(dir.File("a.png"), OutputPaths.NextFree(dir.File("a.png")));
    }

    [Fact]
    public void Next_free_numbers_like_explorer()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.png"), "");
        File.WriteAllText(dir.File("a (1).png"), "");

        Assert.Equal(dir.File("a (2).png"), OutputPaths.NextFree(dir.File("a.png")));
    }

    [Fact]
    public void Next_free_also_skips_names_reserved_by_the_caller()
    {
        using var dir = new TempDir();
        var reserved = new HashSet<string>(OutputPaths.Comparer) { dir.File("a.png"), dir.File("a (1).png") };

        Assert.Equal(dir.File("a (2).png"), OutputPaths.NextFree(dir.File("a.png"), reserved.Contains));
    }
}
