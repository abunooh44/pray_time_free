using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PrayTimeSetup;

/// <summary>
/// مثبّت «مواقيت». ملف تنفيذي واحد يحمل التطبيق كاملًا بداخله.
///
/// يثبّت في مجلد المستخدم لا في Program Files، فلا يحتاج صلاحيات مدير ولا
/// يُظهر نافذة UAC — وهو ما يجعل التشغيل التلقائي عند بدء ويندز يعمل فعلًا،
/// إذ يمنع ويندز مطالبات الرفع أثناء تسجيل الدخول.
/// </summary>
internal static class Program
{
    private const string AppName = "PrayTime";
    private const string DisplayName = "مواقيت — أوقات الصلاة";
    private const string Version = "1.1.1";
    private const string ExeName = "PrayTime.exe";

    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PrayTime";

    private static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", AppName);

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        "Programs", "مواقيت.lnk");

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "مواقيت.lnk");

    private static string RunKeyPath =>
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "PrayTimeSetup.log");

    /// <summary>سجل بسيط: بدونه يصير أي فشل في التثبيت لغزًا لا يُحل.</summary>
    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch (Exception) { }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Log($"--- بدء المثبّت {Version} | الوسائط: {string.Join(" ", args)} ---");
        try
        {
            var silent = args.Contains("/S", StringComparer.OrdinalIgnoreCase);

            return args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase)
                ? Uninstall(silent)
                : Install(silent);
        }
        catch (Exception ex)
        {
            Dialog.Error($"حدث خطأ غير متوقع:\n\n{ex.Message}");
            return 1;
        }
    }

    // ================= التثبيت =================

    private static int Install(bool silent)
    {
        var upgrading = Directory.Exists(InstallDir);

        if (!silent)
        {
            var message = upgrading
                ? $"سيُحدَّث «مواقيت» إلى الإصدار {Version}.\n\nمكان التثبيت:\n{InstallDir}\n\nهل تريد المتابعة؟"
                : $"سيُثبَّت «مواقيت — أوقات الصلاة» الإصدار {Version}.\n\n" +
                  $"مكان التثبيت:\n{InstallDir}\n\n" +
                  "لا يحتاج صلاحيات مدير، ولا يحتاج تثبيت أي برنامج آخر.\n\nهل تريد المتابعة؟";

            if (!Dialog.Confirm(message)) return 2;
        }

        StopRunningApp();

        try
        {
            Directory.CreateDirectory(InstallDir);
            ExtractPayload(InstallDir);
        }
        catch (Exception ex)
        {
            Dialog.Error($"تعذّر نسخ الملفات إلى:\n{InstallDir}\n\n{ex.Message}");
            return 1;
        }

        var exePath = Path.Combine(InstallDir, ExeName);
        if (!File.Exists(exePath))
        {
            Dialog.Error("اكتمل النسخ لكن الملف التنفيذي غير موجود. الحزمة تالفة.");
            return 1;
        }

        Log($"استُخرجت الملفات إلى {InstallDir}");

        CreateShortcut(StartMenuShortcut, exePath);
        CreateShortcut(DesktopShortcut, exePath);
        RegisterUninstallEntry(exePath);
        EnableAutoStart(exePath);
        Log("اكتمل التثبيت.");

        if (silent)
        {
            Launch(exePath);
            return 0;
        }

        var done = upgrading
            ? "تم التحديث بنجاح."
            : "تم التثبيت بنجاح.\n\n" +
              "• يبدأ التطبيق تلقائيًا مع ويندز ويعمل في الخلفية.\n" +
              "• أيقونته في شريط المهام بجوار الساعة.\n" +
              "• شريط صغير في شريط المهام يعرض الوقت المتبقي للإقامة.";

        if (Dialog.Confirm($"{done}\n\nهل تريد تشغيله الآن؟")) Launch(exePath);
        return 0;
    }

    private static void ExtractPayload(string targetDir)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException("حزمة التطبيق مفقودة من المثبّت.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));

            // حماية من مسارات خارج المجلد الهدف (Zip Slip).
            if (!destination.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    // ================= إلغاء التثبيت =================

    private static int Uninstall(bool silent)
    {
        if (!silent && !Dialog.Confirm(
                "سيُزال «مواقيت» من هذا الجهاز.\n\n" +
                "إعداداتك ومواقيتك المخصّصة ستبقى محفوظة في مجلد بيانات المستخدم.\n\n" +
                "هل تريد المتابعة؟"))
        {
            return 2;
        }

        StopRunningApp();

        DisableAutoStart();
        DeleteQuietly(StartMenuShortcut);
        DeleteQuietly(DesktopShortcut);

        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); }
        catch (Exception) { /* لا شيء يستحق إيقاف الإزالة */ }

        // المُزيل نفسه يعمل من داخل المجلد، فلا يمكنه حذف نفسه مباشرة.
        // نحذف كل شيء عداه، ثم نُجدول حذف المجلد بعد خروج العملية.
        if (RemoveInstalledFiles()) ScheduleSelfDelete();

        if (!silent) Dialog.Info("تمت إزالة «مواقيت».");
        return 0;
    }

    private static bool RemoveInstalledFiles()
    {
        if (!Directory.Exists(InstallDir)) return false;

        var self = Environment.ProcessPath;
        var anyLeft = false;

        foreach (var file in Directory.EnumerateFiles(InstallDir, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(file, self, StringComparison.OrdinalIgnoreCase))
            {
                anyLeft = true;
                continue;
            }
            DeleteQuietly(file);
        }

        if (!anyLeft)
        {
            try { Directory.Delete(InstallDir, recursive: true); } catch (Exception) { }
        }

        return anyLeft;
    }

    /// <summary>يحذف مجلد التثبيت بعد خروج المُزيل، عبر أمر مؤجّل بسيط.</summary>
    private static void ScheduleSelfDelete()
    {
        try
        {
            var command = $"/c timeout /t 3 /nobreak > nul & rd /s /q \"{InstallDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", command)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception) { }
    }

    // ================= خطوات مشتركة =================

    private static void StopRunningApp()
    {
        foreach (var process in Process.GetProcessesByName(AppName))
        {
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (Exception) { }
            finally { process.Dispose(); }
        }

        // مهلة قصيرة حتى يُحرّر ويندز قفل الملف التنفيذي.
        Thread.Sleep(400);
    }

    /// <summary>
    /// إنشاء اختصار .lnk. نستعين بـWScript.Shell عبر PowerShell بدل استيراد COM،
    /// لأن استيراد COM لا يتوافق مع تشذيب الملف التنفيذي.
    /// </summary>
    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

            var script = new StringBuilder()
                .Append("$s=(New-Object -ComObject WScript.Shell).CreateShortcut('")
                .Append(shortcutPath.Replace("'", "''")).Append("');")
                .Append("$s.TargetPath='").Append(targetPath.Replace("'", "''")).Append("';")
                .Append("$s.WorkingDirectory='").Append(Path.GetDirectoryName(targetPath)!.Replace("'", "''")).Append("';")
                .Append("$s.IconLocation='").Append(targetPath.Replace("'", "''")).Append(",0';")
                .Append("$s.Description='مواقيت الصلاة';")
                .Append("$s.Save()")
                .ToString();

            var info = new ProcessStartInfo("powershell.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(script);

            using var process = Process.Start(info);
            process?.WaitForExit(15000);
        }
        catch (Exception ex)
        {
            // فشل الاختصار لا يمنع التطبيق من العمل.
            Log($"تعذّر إنشاء الاختصار {shortcutPath}: {ex.Message}");
        }
    }

    private static void RegisterUninstallEntry(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true);
            if (key is null) return;

            key.SetValue("DisplayName", DisplayName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "PrayTime");
            key.SetValue("DisplayIcon", exePath);
            key.SetValue("InstallLocation", InstallDir);
            // التطبيق نفسه يتولّى الإزالة؛ لا داعي لنسخة ثانية من المثبّت على القرص.
            key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall /S");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", DirectorySizeKb(InstallDir), RegistryValueKind.DWord);

            Log("سجل إزالة البرامج: تم.");
        }
        catch (Exception ex)
        {
            Log($"سجل إزالة البرامج فشل: {ex}");
        }
    }

    private static int DirectorySizeKb(string dir)
    {
        try
        {
            long bytes = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(f).Length;
            return (int)(bytes / 1024);
        }
        catch (Exception) { return 0; }
    }

    private static void EnableAutoStart(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key?.SetValue(AppName, $"\"{exePath}\" --minimized", RegistryValueKind.String);
        }
        catch (Exception) { }
    }

    private static void DisableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception) { }
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception) { }
    }

    private static void Launch(string exePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = InstallDir
            });
        }
        catch (Exception ex)
        {
            Dialog.Error($"تعذّر تشغيل التطبيق:\n{ex.Message}");
        }
    }
}

/// <summary>
/// نوافذ حوار عبر واجهة ويندز مباشرة. لا نستخدم WPF ولا WinForms حتى يبقى
/// المثبّت قابلًا للتشذيب وصغير الحجم.
/// </summary>
internal static class Dialog
{
    private const uint OkCancel = 0x00000001;
    private const uint IconInfo = 0x00000040;
    private const uint IconError = 0x00000010;
    private const uint IconQuestion = 0x00000020;
    private const uint RightAlign = 0x00080000;
    private const uint RtlReading = 0x00100000;
    private const uint TopMost = 0x00040000;
    private const int IdOk = 1;

    private const string Title = "مواقيت — أوقات الصلاة";

    // العربية تحتاج RTLREADING وإلا ظهرت علامات الترقيم في الجانب الخاطئ.
    private const uint ArabicFlags = RightAlign | RtlReading | TopMost;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static bool Confirm(string message) =>
        MessageBoxW(IntPtr.Zero, message, Title, OkCancel | IconQuestion | ArabicFlags) == IdOk;

    public static void Info(string message) =>
        MessageBoxW(IntPtr.Zero, message, Title, IconInfo | ArabicFlags);

    public static void Error(string message) =>
        MessageBoxW(IntPtr.Zero, message, Title, IconError | ArabicFlags);
}
