using System.IO;

namespace PrayTime.App.Infrastructure;

public static class AppPaths
{
    public const string AppFolderName = "PrayTime";

    /// <summary>الإعدادات — تنتقل مع حساب المستخدم.</summary>
    public static string Roaming { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    /// <summary>السجلات وسجل الإطلاق ومحاولات الرمز — خاصة بهذا الجهاز، يجب ألا تنتقل.</summary>
    public static string Local { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string Logs { get; } = Path.Combine(Local, "logs");

    /// <summary>
    /// مسار الملف التنفيذي.
    /// Assembly.Location يعود فارغًا في النشر أحادي الملف — وهذا أشهر سبب
    /// لفشل التشغيل التلقائي صامتًا. ProcessPath صحيح دائمًا.
    /// </summary>
    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PrayTime.exe");

    /// <summary>مجلد ملفات الصوت المرفقة، بجوار الملف التنفيذي.</summary>
    public static string BundledAudio { get; } = Path.Combine(AppContext.BaseDirectory, "audio");

    /// <summary>مجلد أصوات المستخدم الإضافية.</summary>
    public static string UserAudio { get; } = Path.Combine(Roaming, "audio");

    /// <summary>
    /// يحوّل اسم ملف صوت مخزّنًا في الإعدادات إلى مسار كامل موجود فعلًا.
    /// يقبل مسارًا مطلقًا (اختاره المستخدم) أو اسم ملف من المكتبة المرفقة.
    /// </summary>
    public static string? ResolveAudio(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;

        if (Path.IsPathRooted(nameOrPath))
            return File.Exists(nameOrPath) ? nameOrPath : null;

        var user = Path.Combine(UserAudio, nameOrPath);
        if (File.Exists(user)) return user;

        var bundled = Path.Combine(BundledAudio, nameOrPath);
        return File.Exists(bundled) ? bundled : null;
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Roaming);
        Directory.CreateDirectory(Local);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(UserAudio);
    }
}
