using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PrayTime.App.Infrastructure;

namespace PrayTime.App.Services;

/// <summary>
/// إزالة التطبيق. تعيش هنا لا في المثبّت عن قصد: نسخ المثبّت (٩٠ م.ب) بجوار
/// التطبيق لمجرد الإزالة يضاعف المساحة على القرص بلا فائدة، بينما الملف
/// التنفيذي للتطبيق موجود أصلًا.
/// </summary>
public static class Uninstaller
{
    private const string AppName = "PrayTime";

    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PrayTime";

    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const uint MbOkCancel = 0x00000001;
    private const uint MbIconInfo = 0x00000040;
    private const uint MbIconQuestion = 0x00000020;
    private const uint MbArabic = 0x00080000 | 0x00100000 | 0x00040000;
    private const int IdOk = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>يُنفّذ الإزالة ثم يُرجع رمز الخروج. يُستدعى من App قبل بناء أي واجهة.</summary>
    public static int Run(bool silent)
    {
        const string title = "مواقيت — أوقات الصلاة";

        if (!silent)
        {
            var confirm = MessageBoxW(IntPtr.Zero,
                "سيُزال «مواقيت» من هذا الجهاز.\n\n" +
                "إعداداتك ومواقيتك المخصّصة تبقى محفوظة، فلو أعدت التثبيت لاحقًا تجدها كما تركتها.\n\n" +
                "هل تريد المتابعة؟",
                title, MbOkCancel | MbIconQuestion | MbArabic);

            if (confirm != IdOk) return 2;
        }

        StopOtherInstances();
        RemoveRegistry();
        RemoveShortcuts();

        var installDir = Path.GetDirectoryName(AppPaths.ExecutablePath);
        if (!string.IsNullOrEmpty(installDir)) ScheduleDirectoryDeletion(installDir);

        if (!silent)
        {
            MessageBoxW(IntPtr.Zero,
                "تمت إزالة «مواقيت».\n\nشكرًا لاستخدامك التطبيق.",
                title, MbIconInfo | MbArabic);
        }

        return 0;
    }

    private static void StopOtherInstances()
    {
        var current = Environment.ProcessId;

        foreach (var process in Process.GetProcessesByName(AppName))
        {
            if (process.Id == current) continue;
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (Exception) { }
            finally { process.Dispose(); }
        }
    }

    private static void RemoveRegistry()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); }
        catch (Exception) { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception) { }
    }

    private static void RemoveShortcuts()
    {
        string[] shortcuts =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "مواقيت.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "مواقيت.lnk")
        ];

        foreach (var path in shortcuts)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// لا يمكن لعملية أن تحذف ملفها التنفيذي وهي تعمل، فنُجدّل الحذف
    /// بأمر مؤجّل ينفّذه ويندز بعد خروجنا.
    /// </summary>
    private static void ScheduleDirectoryDeletion(string directory)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c timeout /t 3 /nobreak > nul & rd /s /q \"{directory}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception) { }
    }
}
