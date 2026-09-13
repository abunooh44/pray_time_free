using Microsoft.Win32;
using PrayTime.App.Infrastructure;

namespace PrayTime.App.Services;

/// <summary>
/// التشغيل التلقائي عبر مفتاح Run الخاص بالمستخدم — لا يحتاج صلاحيات مدير،
/// ولا يُظهر نافذة UAC عند الإقلاع (وهو ما يجعل خيار «تشغيل كمسؤول» يفشل صامتًا).
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "PrayTime";

    private static string CommandLine => $"\"{AppPaths.ExecutablePath}\" --minimized";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch (Exception ex)
        {
            Log.Warn($"تعذّرت قراءة مفتاح التشغيل التلقائي: {ex.Message}");
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (enabled) key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);

            Log.Info($"التشغيل التلقائي: {(enabled ? "مُفعّل" : "مُعطّل")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("تعذّر تعديل التشغيل التلقائي", ex);
            return false;
        }
    }

    /// <summary>
    /// يصحّح المسار المخزّن إن نقل المستخدم الملف التنفيذي إلى مكان آخر.
    /// بدون هذا يبقى المفتاح يشير إلى مسار قديم فلا يعمل التشغيل التلقائي.
    /// </summary>
    public static void SelfHeal()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string existing) return;

            if (!string.Equals(existing, CommandLine, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
                Log.Info($"تصحيح مسار التشغيل التلقائي إلى: {CommandLine}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"تعذّر تصحيح مسار التشغيل التلقائي: {ex.Message}");
        }
    }

    /// <summary>
    /// هل عطّل المستخدم التشغيل التلقائي من إعدادات ويندز أو مدير المهام؟
    /// ويندز يخزّن ذلك في StartupApproved كقيمة ثنائية؛ البتّ الأول من أول بايت يعني «معطّل».
    /// لا نكتب في هذا المفتاح إطلاقًا — تجاوز اختيار المستخدم الصريح تصرّف عدائي.
    /// </summary>
    public static bool IsDisabledByWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedPath);
            if (key?.GetValue(ValueName) is byte[] { Length: > 0 } data)
                return (data[0] & 0x01) != 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"تعذّرت قراءة حالة StartupApproved: {ex.Message}");
        }
        return false;
    }
}
