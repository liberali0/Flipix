using System.Runtime.InteropServices;

namespace ImageConverter.Cli;

/// <summary>
/// Moves a converted source file to the Recycle Bin (<c>--delete-source</c>), so the user can still
/// restore it. Never shows a dialog: the CLI runs hidden behind the right-click menu.
/// </summary>
internal static class SourceRecycler
{
    /// <summary>Returns null on success, otherwise an error message. Replaceable in tests.</summary>
    internal static Func<string, string?> Recycle { get; set; } = RecycleWithShell;

    private static string? RecycleWithShell(string path)
    {
        if (!File.Exists(path))
        {
            return "the file no longer exists";
        }

        if (!OperatingSystem.IsWindows())
        {
            // No Recycle Bin to use; only reached when running the tests or tools off Windows.
            File.Delete(path);
            return null;
        }

        var op = new ShFileOpStruct
        {
            wFunc = FoDelete,
            pFrom = path + "\0\0", // double-null-terminated list
            fFlags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent,
        };
        var code = SHFileOperationW(ref op);
        if (code != 0)
        {
            return $"could not move it to the Recycle Bin (code 0x{code:X})";
        }

        if (op.fAnyOperationsAborted)
        {
            return "moving it to the Recycle Bin was cancelled";
        }

        return File.Exists(path) ? "it is still there after moving it to the Recycle Bin" : null;
    }

    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOpStruct lpFileOp);
}
