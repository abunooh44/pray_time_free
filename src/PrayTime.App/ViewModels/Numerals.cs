using System.Globalization;
using System.Text;

namespace PrayTime.App.ViewModels;

/// <summary>
/// تنسيق الأرقام. كل التنسيقات تمرّ من هنا بثقافة ثابتة ثم تُحوَّل اختياريًا
/// إلى الأرقام العربية الهندية — بدل الاعتماد على ثقافة النظام التي قد تقلب
/// التاريخ إلى التقويم الهجري دون قصد.
/// </summary>
public static class Numerals
{
    private static readonly char[] ArabicIndic = ['٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩'];

    public static bool UseArabicIndic { get; set; } = true;

    public static string Convert(string latin)
    {
        if (!UseArabicIndic) return latin;

        var sb = new StringBuilder(latin.Length);
        foreach (var c in latin)
            sb.Append(c is >= '0' and <= '9' ? ArabicIndic[c - '0'] : c);
        return sb.ToString();
    }

    public static string Int(int value) =>
        Convert(value.ToString(CultureInfo.InvariantCulture));

    public static string Signed(int value) =>
        Convert(value.ToString("+0;-0;0", CultureInfo.InvariantCulture));

    public static string Time(DateTimeOffset t) =>
        Convert(t.ToString("HH:mm", CultureInfo.InvariantCulture));

    public static string TimeWithSeconds(DateTimeOffset t) =>
        Convert(t.ToString("HH:mm:ss", CultureInfo.InvariantCulture));

    /// <summary>عدّاد تنازلي بصيغة س:دد:ثث، ويسقط الساعات إن كانت صفرًا.</summary>
    public static string Countdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        var text = span.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}",
                (int)span.TotalHours, span.Minutes, span.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", span.Minutes, span.Seconds);

        return Convert(text);
    }

    /// <summary>وصف مدة بالعربية، مثل «بعد ساعتين و١٥ دقيقة».</summary>
    public static string HumanDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;

        var parts = new List<string>(2);
        if (hours > 0) parts.Add(PluralHours(hours));
        if (minutes > 0 || hours == 0) parts.Add(PluralMinutes(minutes));

        return string.Join(" و", parts);
    }

    private static string PluralHours(int n) => n switch
    {
        1 => "ساعة",
        2 => "ساعتان",
        >= 3 and <= 10 => $"{Int(n)} ساعات",
        _ => $"{Int(n)} ساعة"
    };

    private static string PluralMinutes(int n) => n switch
    {
        0 => "أقل من دقيقة",
        1 => "دقيقة",
        2 => "دقيقتان",
        >= 3 and <= 10 => $"{Int(n)} دقائق",
        _ => $"{Int(n)} دقيقة"
    };
}
