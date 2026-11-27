using ImageConverter.Core.Localization;

namespace ImageConverter.App.Services;

/// <summary>바탕 화면의 Flipix 바로 가기. 설치 마법사가 만드는 것과 같은 이름(Flipix.lnk)을 쓴다.</summary>
public static class DesktopShortcut
{
    private const string FileName = "Flipix.lnk";

    public static string UserPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FileName);

    /// <summary>내 바탕 화면이나 모든 사용자 바탕 화면에 이미 있으면 true.</summary>
    public static bool Exists =>
        File.Exists(UserPath) ||
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), FileName));

    /// <summary>지금 실행 중인 Workshop을 가리키는 바로 가기를 내 바탕 화면에 만든다(있으면 새로 고침).</summary>
    public static void Create()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException(Loc.T("실행 파일 위치를 알 수 없습니다.", "The app location is unknown."));
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException(Loc.T("바로 가기를 만들 수 없습니다.", "The shortcut could not be created."));
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic link = shell.CreateShortcut(UserPath);
            link.TargetPath = exe;
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.IconLocation = exe + ",0";
            link.Description = "Flipix Workshop";
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
