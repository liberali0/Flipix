using ImageConverter.Core.Batch;

namespace ImageConverter.Core.Tests;

public class FileScannerTests
{
    private static readonly string[] Exts = { ".png", ".jpg" };

    [Fact]
    public void Scans_folder_recursively_and_filters_by_extension()
    {
        using var d = new TempDir();
        d.Touch("a.png"); d.Touch("sub/b.JPG"); d.Touch("sub/c.txt");

        var found = FileScanner.Scan(new[] { d.Path }, Exts).Select(f => Path.GetFileName(f.Path)).OrderBy(x => x).ToList();

        Assert.Equal(new[] { "a.png", "b.JPG" }, found);
    }

    [Fact]
    public void Deduplicates_paths_given_twice()
    {
        using var d = new TempDir();
        var f = d.Touch("a.png");

        var found = FileScanner.Scan(new[] { f, f, d.Path }, Exts).ToList();

        Assert.Single(found);
    }

    [Fact]
    public void Folder_items_carry_their_source_root()
    {
        using var d = new TempDir();
        d.Touch("sub/a.png");

        var found = FileScanner.Scan(new[] { d.Path }, Exts).Single();

        Assert.Equal(Path.GetFullPath(d.Path), found.SourceRoot);
    }
}
