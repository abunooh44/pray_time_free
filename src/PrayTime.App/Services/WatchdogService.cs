using System.Diagnostics;
using PrayTime.App.Infrastructure;

namespace PrayTime.App.Services;

/// <summary>
/// حارس يعيد تشغيل التطبيق إن اختفى لأي سبب: إغلاق ويندز أُلغي بعد أن استجاب
/// التطبيق له، أو انهيار، أو إنهاء بالخطأ من مدير المهام.
///
/// مفتاح Run في السجل لا يكفي وحده: هو يعمل عند تسجيل الدخول فقط، فإن مات
/// التطبيق في منتصف الجلسة بقي ميتًا حتى الدخول التالي — وهو ما يعني ببساطة
/// أذانًا فائتًا.
///
/// نستخدم مجدول مهام ويندز عبر schtasks بدل واجهة COM: أبسط، ولا يتأثر
/// بتشذيب الملف التنفيذي، ويظهر للمستخدم في «مجدول المهام» ليعطّله متى شاء.
/// </summary>
public static class WatchdogService
{
    public const string TaskName = "PrayTime Watchdog";

    /// <summary>كل كم دقيقة يتحقق الحارس.</summary>
    public const int IntervalMinutes = 15;

    public static bool IsInstalled()
    {
        var (exitCode, _) = Run($"/Query /TN \"{TaskName}\"");
        return exitCode == 0;
    }

    public static bool Install()
    {
        // ‎--minimized‎ مقصود: النسخة الثانية تخرج بصمت بدل أن تُظهر النافذة،
        // وإلا قفزت النافذة في وجه المستخدم كل ربع ساعة.
        var command = $"\\\"{AppPaths.ExecutablePath}\\\" --minimized";

        var (exitCode, output) = Run(
            $"/Create /TN \"{TaskName}\" /TR \"{command}\" /SC MINUTE /MO {IntervalMinutes} /F");

        if (exitCode == 0)
        {
            Log.Info($"حارس إعادة التشغيل: مُثبّت (كل {IntervalMinutes} دقيقة).");
            return true;
        }

        Log.Warn($"تعذّر تثبيت الحارس (رمز {exitCode}): {output}");
        return false;
    }

    public static bool Uninstall()
    {
        var (exitCode, output) = Run($"/Delete /TN \"{TaskName}\" /F");

        if (exitCode == 0)
        {
            Log.Info("حارس إعادة التشغيل: أُزيل.");
            return true;
        }

        // رمز 1 يعني «المهمة غير موجودة» غالبًا، وهو ليس فشلًا.
        Log.Info($"إزالة الحارس: لا شيء لإزالته ({output.Trim()})");
        return false;
    }

    public static void Sync(bool enabled)
    {
        var installed = IsInstalled();

        if (enabled && !installed) Install();
        else if (!enabled && installed) Uninstall();
        else if (enabled) Install(); // ‎/F‎ يُحدّث المسار لو نُقل الملف التنفيذي
    }

    /// <summary>
    /// يجدول فحص إحياء بعد مهلة قصيرة.
    ///
    /// السبب: WPF يُنهي التطبيق بنفسه عند استئذان ويندز في إغلاق الجلسة، ولا
    /// يمكن منعه إلا بمعارضة الإغلاق — وهي تُظهر شاشة «تطبيق يمنع إيقاف التشغيل».
    /// فإن أُلغي الإغلاق بعد ذلك بقي الجهاز يعمل والتطبيق ميتًا.
    ///
    /// هذا الفحص يعمل في عملية منفصلة: إن أُكمل الإغلاق ماتت معه، وإن أُلغي
    /// أعادت التطبيق خلال دقيقة ونصف بدل انتظار دورة الحارس كاملة.
    /// </summary>
    public static void ScheduleRevivalCheck(int delaySeconds = 90)
    {
        try
        {
            var exe = AppPaths.ExecutablePath;
            var command =
                $"/c timeout /t {delaySeconds} /nobreak >nul & " +
                "tasklist /FI \"IMAGENAME eq PrayTime.exe\" | findstr /I PrayTime.exe >nul || " +
                $"start \"\" \"{exe}\" --minimized";

            Process.Start(new ProcessStartInfo("cmd.exe", command)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Log.Info($"جُدول فحص إحياء بعد {delaySeconds} ثانية تحسبًا لإلغاء الإغلاق.");
        }
        catch (Exception ex)
        {
            Log.Warn($"تعذّر جدولة فحص الإحياء: {ex.Message}");
        }
    }

    private static (int ExitCode, string Output) Run(string arguments)
    {
        try
        {
            var info = new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(info);
            if (process is null) return (-1, "تعذّر تشغيل schtasks");

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(20000);

            return (process.ExitCode, output);
        }
        catch (Exception ex)
        {
            Log.Warn($"فشل استدعاء schtasks: {ex.Message}");
            return (-1, ex.Message);
        }
    }
}
