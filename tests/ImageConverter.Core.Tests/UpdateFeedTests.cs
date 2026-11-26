using ImageConverter.Core.Updates;

namespace ImageConverter.Core.Tests;

public class UpdateFeedTests
{
    private const string Base = "https://github.com/liberali0/Flipix/releases/download/";

    private static string Release(string tag, bool prerelease = false, params (string Name, string Url)[] assets)
    {
        var list = string.Join(",", assets.Select(a => $"{{\"name\":\"{a.Name}\",\"browser_download_url\":\"{a.Url}\"}}"));
        return $"{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":{(prerelease ? "true" : "false")},\"assets\":[{list}]}}";
    }

    private static string Normal(string tag, string ver) => Release(tag, false,
        ($"Flipix-Setup-{ver}.exe", $"{Base}{tag}/Flipix-Setup-{ver}.exe"),
        ("SHA256SUMS.txt", $"{Base}{tag}/SHA256SUMS.txt"),
        ("ImageConverter.msix", $"{Base}{tag}/ImageConverter.msix"));

    [Fact]
    public void Finds_setup_and_checksums()
    {
        var info = UpdateFeed.Parse(Normal("v0.1.5", "0.1.5"));

        Assert.NotNull(info);
        Assert.Equal(new Version(0, 1, 5), info!.Version);
        Assert.Equal("Flipix-Setup-0.1.5.exe", info.SetupName);
        Assert.Equal($"{Base}v0.1.5/Flipix-Setup-0.1.5.exe", info.SetupUrl.ToString());
        Assert.Equal($"{Base}v0.1.5/SHA256SUMS.txt", info.ChecksumsUrl.ToString());
    }

    [Fact]
    public void Rejects_downloads_from_other_places()
    {
        var json = Release("v0.1.5", false,
            ("Flipix-Setup-0.1.5.exe", "https://evil.example/Flipix-Setup-0.1.5.exe"),
            ("SHA256SUMS.txt", $"{Base}v0.1.5/SHA256SUMS.txt"));

        Assert.Null(UpdateFeed.Parse(json));
    }

    [Fact]
    public void Needs_checksums_file()
    {
        var json = Release("v0.1.5", false, ("Flipix-Setup-0.1.5.exe", $"{Base}v0.1.5/Flipix-Setup-0.1.5.exe"));
        Assert.Null(UpdateFeed.Parse(json));
    }

    [Fact]
    public void Ignores_setup_of_another_version_and_prereleases()
    {
        Assert.Null(UpdateFeed.Parse(Normal("v0.1.5", "0.1.4")));
        var pre = Release("v0.1.5", true,
            ("Flipix-Setup-0.1.5.exe", $"{Base}v0.1.5/Flipix-Setup-0.1.5.exe"),
            ("SHA256SUMS.txt", $"{Base}v0.1.5/SHA256SUMS.txt"));
        Assert.Null(UpdateFeed.Parse(pre));
        Assert.Null(UpdateFeed.Parse(Normal("latest", "0.1.5")));
    }

    [Theory]
    [InlineData("0.1.5", "0.1.4.0", true)]
    [InlineData("0.2.0", "0.1.9", true)]
    [InlineData("0.1.4", "0.1.4.0", false)]
    [InlineData("0.1.3", "0.1.4", false)]
    public void Compares_versions(string latest, string current, bool expected)
    {
        Assert.True(UpdateFeed.TryParseVersion(latest, out var l));
        Assert.Equal(expected, UpdateFeed.IsNewer(l, Version.Parse(current)));
    }

    [Fact]
    public void Unknown_current_version_never_offers_update()
    {
        Assert.False(UpdateFeed.IsNewer(new Version(9, 9, 9), null));
    }

    [Fact]
    public void Finds_hash_for_exact_file_name()
    {
        var h = new string('a', 64);
        var other = new string('b', 64);
        var sums = $"{other}  ImageConverter.msix\r\n{h.ToUpperInvariant()}  Flipix-Setup-0.1.5.exe\r\n";

        Assert.Equal(h, UpdateFeed.FindHash(sums, "Flipix-Setup-0.1.5.exe"));
        Assert.Null(UpdateFeed.FindHash(sums, "Flipix-Setup-0.1.6.exe"));
        Assert.Null(UpdateFeed.FindHash("nothex  Flipix-Setup-0.1.5.exe", "Flipix-Setup-0.1.5.exe"));
    }
}
