using System.Runtime.InteropServices;

namespace PrayTime.App.Interop;

internal static class NativeMethods
{
    [Flags]
    public enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        AwayModeRequired = 0x00000040
    }

    public static readonly IntPtr HwndTopmost = new(-1);

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    // DllImport بدل LibraryImport عن قصد: الأخير يفرض AllowUnsafeBlocks على
    // المشروع كله من أجل استدعاء واحد لا يحتاج أي تنظيم بيانات (marshalling).
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    // ---- موضع شريط المهام الحقيقي ----
    // الاشتقاق من فرق «منطقة العمل» عن الشاشة يفشل مع الإخفاء التلقائي
    // ومع شريط على حافة جانبية. هذه هي الواجهة التي يقصدها ويندز لهذا الغرض.

    public const uint AbmGetTaskbarPos = 0x00000005;

    public enum AppBarEdge : uint
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public Rect rc;
        public int lParam;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern IntPtr SHAppBarMessage(uint dwMessage, ref AppBarData pData);

    // ---- أنماط النافذة الممتدة ----

    public const int GwlExStyle = -20;

    /// <summary>يمنع النافذة من أخذ التركيز عند النقر عليها.</summary>
    public const long WsExNoActivate = 0x08000000L;

    /// <summary>يُخفيها من Alt+Tab ومن شريط المهام.</summary>
    public const long WsExToolWindow = 0x00000080L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    /// <summary>
    /// منطقة الساعة وأيقونات الإشعارات في شريط المهام، بالبكسل الفعلي.
    /// نستخدمها كمرساة: المساحة التي تسبقها فارغة عادةً، فلا نغطي شيئًا.
    /// </summary>
    public static Rect? GetTrayAreaRect()
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return null;

        var notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify == IntPtr.Zero) return null;

        return GetWindowRect(notify, out var rect) ? rect : null;
    }

    /// <summary>مستطيل شريط المهام بالبكسل الفعلي، أو null إن تعذّر.</summary>
    public static (Rect Rect, AppBarEdge Edge)? GetTaskbar()
    {
        var data = new AppBarData { cbSize = (uint)Marshal.SizeOf<AppBarData>() };
        var result = SHAppBarMessage(AbmGetTaskbarPos, ref data);
        if (result == IntPtr.Zero) return null;
        return (data.rc, (AppBarEdge)data.uEdge);
    }
}

/// <summary>
/// يمنع الجهاز من الدخول في السبات أثناء تشغيل الأذان.
/// بدونه قد ينام الحاسب في منتصف الأذان إن كانت مهلة السكون قصيرة.
/// </summary>
public sealed class KeepAwakeScope : IDisposable
{
    private bool _disposed;

    public KeepAwakeScope()
    {
        NativeMethods.SetThreadExecutionState(
            NativeMethods.ExecutionState.Continuous |
            NativeMethods.ExecutionState.SystemRequired |
            NativeMethods.ExecutionState.AwayModeRequired);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.SetThreadExecutionState(NativeMethods.ExecutionState.Continuous);
    }
}
