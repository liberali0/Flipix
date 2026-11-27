using System.Globalization;
using ImageConverter.Core.Localization;

namespace ImageConverter.Core.Tests;

public class LocTests
{
    [Fact]
    public void Auto_follows_the_Windows_display_language()
    {
        Assert.False(Loc.UsesEnglish(UiLanguage.Auto, new CultureInfo("ko-KR")));
        Assert.True(Loc.UsesEnglish(UiLanguage.Auto, new CultureInfo("en-US")));
        Assert.True(Loc.UsesEnglish(UiLanguage.Auto, new CultureInfo("ja-JP")));
    }

    [Fact]
    public void A_chosen_language_wins()
    {
        Assert.False(Loc.UsesEnglish(UiLanguage.Korean, new CultureInfo("en-US")));
        Assert.True(Loc.UsesEnglish(UiLanguage.English, new CultureInfo("ko-KR")));
    }

    [Fact]
    public void Korean_until_the_app_applies_a_language()
    {
        // 앱만 Apply를 부른다. 다른 테스트가 한국어 문구를 확인하므로 테스트에서는 바꾸지 않는다.
        Assert.Equal("저장", Loc.T("저장", "Save"));
    }
}
