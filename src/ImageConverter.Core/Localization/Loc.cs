using System.Globalization;

namespace ImageConverter.Core.Localization;

/// <summary>화면 언어. Auto는 Windows 표시 언어가 한국어면 한국어, 아니면 영어. 셸 확장도 settings.json의 language로 읽는다.</summary>
public enum UiLanguage { Auto, Korean, English }

/// <summary>
/// 화면 문구를 한국어/영어 중 하나로 고른다. 문구는 쓰는 자리에서 <c>Loc.T("한국어", "English")</c>로 함께 적는다.
/// 앱이 시작할 때 <see cref="Apply"/>를 한 번 부른다. 부르기 전(테스트 등)에는 한국어.
/// </summary>
public static class Loc
{
    public static bool English { get; private set; }

    public static void Apply(UiLanguage preference) => English = UsesEnglish(preference, CultureInfo.CurrentUICulture);

    public static bool UsesEnglish(UiLanguage preference, CultureInfo windowsCulture) => preference switch
    {
        UiLanguage.Korean => false,
        UiLanguage.English => true,
        _ => !string.Equals(windowsCulture.TwoLetterISOLanguageName, "ko", StringComparison.OrdinalIgnoreCase),
    };

    public static string T(string korean, string english) => English ? english : korean;
}
