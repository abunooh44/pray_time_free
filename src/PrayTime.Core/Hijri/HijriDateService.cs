using System.Globalization;

namespace PrayTime.Core.Hijri;

/// <summary>تحويل التاريخ الميلادي إلى هجري بتقويم أم القرى، مع إزاحة يدوية بالأيام.</summary>
public static class HijriDateService
{
    private static readonly UmAlQuraCalendar Calendar = new();

    private static readonly string[] Months =
    [
        "محرّم", "صفر", "ربيع الأول", "ربيع الآخر", "جمادى الأولى", "جمادى الآخرة",
        "رجب", "شعبان", "رمضان", "شوّال", "ذو القعدة", "ذو الحجة"
    ];

    private static readonly string[] Weekdays =
    [
        "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"
    ];

    /// <summary>
    /// تقويم أم القرى محسوب رياضيًا وقد يختلف عن الرؤية المحلية بيوم،
    /// فنسمح بإزاحة من ‎−٣ إلى ‎+٣ أيام.
    /// </summary>
    public static string Format(DateTime gregorian, int offsetDays = 0)
    {
        var date = gregorian.Date.AddDays(offsetDays);

        // تقويم أم القرى مدعوم تقريبًا من 1900-04-30 إلى 2077-11-16 فقط.
        if (date < Calendar.MinSupportedDateTime.Date || date > Calendar.MaxSupportedDateTime.Date)
            return "";

        try
        {
            var day = Calendar.GetDayOfMonth(date);
            var month = Calendar.GetMonth(date);
            var year = Calendar.GetYear(date);
            return $"{day} {Months[month - 1]} {year} هـ";
        }
        catch (ArgumentOutOfRangeException)
        {
            return "";
        }
    }

    public static string WeekdayAr(DateTime date) => Weekdays[(int)date.DayOfWeek];

    /// <summary>التاريخ الميلادي بأسماء الأشهر العربية، مستقلًّا عن ثقافة النظام.</summary>
    public static string FormatGregorian(DateTime date)
    {
        string[] months =
        [
            "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        ];
        return $"{date.Day} {months[date.Month - 1]} {date.Year} م";
    }
}
