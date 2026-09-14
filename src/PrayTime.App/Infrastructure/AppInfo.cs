namespace PrayTime.App.Infrastructure;

/// <summary>
/// بيانات التطبيق الثابتة. مصدر واحد للحقيقة يستخدمه «عن التطبيق»
/// وخصائص الملف التنفيذي والمثبّت، فلا تتناقض النسخ.
/// </summary>
public static class AppInfo
{
    public const string Name = "مواقيت";
    public const string FullName = "مواقيت — أوقات الصلاة";
    public const string Version = "1.1.1";

    public const string Developer = "Rashid Al Aamri — سلطنة عُمان";
    public const string Phone = "+968 9545 4788";

    /// <summary>رقم الهاتف بصيغة دولية بلا فواصل، لروابط واتساب والاتصال.</summary>
    public const string PhoneRaw = "96895454788";

    public const string Copyright = "© 2026 Rashid Al Aamri";

    public const string ProjectUrl = "https://github.com/abunooh44/pray_time_free";

    /// <summary>شعار الحماية الظاهر في التطبيق وفي خصائص الملف.</summary>
    public const string FreeNotice =
        "مجاني للاستخدام — يجوز نسخه وتوزيعه بلا مقابل، ولا يجوز بيعه.";
}
