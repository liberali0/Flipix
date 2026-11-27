using System.Windows.Markup;
using ImageConverter.Core.Localization;

namespace ImageConverter.App;

/// <summary>XAML에서 화면 문구를 한국어/영어로 함께 적는다: <c>Text="{l:T '저장', 'Save'}"</c>. 창을 만들 때의 화면 언어로 고른다.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension(string korean, string english)
    {
        Korean = korean;
        English = english;
    }

    [ConstructorArgument("korean")] public string Korean { get; set; }
    [ConstructorArgument("english")] public string English { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Korean, English);
}
