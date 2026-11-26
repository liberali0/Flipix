using System.Xml.Linq;

namespace ImageConverter.Core.Tests;

/// <summary>
/// Workshop(WPF) 리소스 사전의 키 충돌 검사. 같은 키가 색 팔레트와 스타일 사전에 함께 있으면
/// 나중에 합쳐진 쪽이 이겨서 Brush 자리에 Style이 들어가고, 앱이 시작하자마자 꺼진다(v0.1.3).
/// </summary>
public class AppResourceKeyTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ThemesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ImageConverter.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "ImageConverter.App", "Themes");
    }

    private static HashSet<string> Keys(string file) =>
        XDocument.Load(Path.Combine(ThemesDir(), file)).Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k != null).Select(k => k!).ToHashSet();

    [Theory]
    [InlineData("Palette.Light.xaml")]
    [InlineData("Palette.Dark.xaml")]
    public void Palette_and_styles_do_not_share_keys(string palette)
    {
        var shared = Keys(palette).Intersect(Keys("Styles.xaml")).ToList();
        Assert.Empty(shared);
    }

    [Fact]
    public void Light_and_dark_palettes_define_the_same_keys()
    {
        Assert.Equal(Keys("Palette.Light.xaml").OrderBy(k => k), Keys("Palette.Dark.xaml").OrderBy(k => k));
    }
}
