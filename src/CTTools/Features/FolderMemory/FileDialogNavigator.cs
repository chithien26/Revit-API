using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CTTools.Features.FolderMemory;

/// <summary>
/// Waits for the next Windows file dialog opened by Revit and makes it jump to a folder, by typing the folder
/// into the "File name" box and pressing Open (standard dialog behaviour). Runs on a background thread and only
/// uses Win32 - never the Revit API.
/// </summary>
internal static class FileDialogNavigator
{
    private const int FileNameComboId = 1148; // cmb13: "File name" box of Explorer-style and Vista dialogs
    private const int FileNameEditId = 1152;  // edt1: "File name" box of old-style dialogs
    private const int OkButtonId = 1;         // IDOK: Open / Save button

    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint WM_COMMAND = 0x0111;
    private const uint BM_CLICK = 0x00F5;

    private static CancellationTokenSource? _watch;

    public static void NavigateNextDialog(string folder)
    {
        _watch?.Cancel();
        var watch = _watch = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var alreadyOpen = FindFileDialogs().ToHashSet();
        Task.Run(() => WatchAsync(folder, alreadyOpen, watch.Token));
    }

    private static async Task WatchAsync(string folder, HashSet<IntPtr> ignore, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(100, token);
                var dialog = FindFileDialogs().FirstOrDefault(h => !ignore.Contains(h));
                if (dialog == IntPtr.Zero)
                    continue;

                // Let Revit finish initializing the dialog, otherwise it may override our folder
                await Task.Delay(300, token);
                await NavigateAsync(dialog, folder);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // No file dialog appeared in time, or another command started
        }
    }

    private static async Task NavigateAsync(IntPtr dialog, string folder)
    {
        var edit = FindFileNameEdit(dialog);
        if (edit == IntPtr.Zero)
            return;

        var originalName = GetText(edit);
        SetText(edit, folder);

        var ok = FindDescendant(dialog, h => GetDlgCtrlID(h) == OkButtonId && GetClassName(h) == "Button");
        if (ok != IntPtr.Zero)
            PostMessage(ok, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        else
            PostMessage(dialog, WM_COMMAND, OkButtonId, IntPtr.Zero);

        // Restore a suggested file name (e.g. in export dialogs) once the dialog has moved to the folder
        await Task.Delay(400);
        if (!string.IsNullOrWhiteSpace(originalName) && !Directory.Exists(originalName))
        {
            edit = FindFileNameEdit(dialog);
            if (edit != IntPtr.Zero)
                SetText(edit, originalName);
        }
    }

    private static IEnumerable<IntPtr> FindFileDialogs()
    {
        var processId = (uint)Environment.ProcessId;
        var dialogs = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == processId && IsWindowVisible(hWnd) && GetClassName(hWnd) == "#32770"
                && FindFileNameEdit(hWnd) != IntPtr.Zero)
            {
                dialogs.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return dialogs;
    }

    private static IntPtr FindFileNameEdit(IntPtr dialog)
    {
        var box = FindDescendant(dialog, h => GetDlgCtrlID(h) is FileNameComboId or FileNameEditId);
        if (box == IntPtr.Zero)
            return IntPtr.Zero;
        return GetClassName(box) == "Edit" ? box : FindDescendant(box, h => GetClassName(h) == "Edit");
    }

    private static IntPtr FindDescendant(IntPtr parent, Func<IntPtr, bool> match)
    {
        var found = IntPtr.Zero;
        EnumChildWindows(parent, (hWnd, _) =>
        {
            if (!match(hWnd))
                return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static string GetText(IntPtr hWnd)
    {
        var length = (int)SendMessage(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        var buffer = new StringBuilder(length + 1);
        SendMessage(hWnd, WM_GETTEXT, buffer.Capacity, buffer);
        return buffer.ToString();
    }

    private static void SetText(IntPtr hWnd, string text) => SendMessage(hWnd, WM_SETTEXT, IntPtr.Zero, text);

    private static string GetClassName(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, int wParam, StringBuilder lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, int wParam, IntPtr lParam);
}
