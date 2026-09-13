using System.Globalization;
using PrayTime.Core.Model;

namespace PrayTime.Core.Scheduling;

public enum EventKind
{
    Adhan = 0,
    Iqama = 1
}

/// <summary>حدث صوتي مجدول: أذان أو إقامة لصلاة معيّنة في لحظة معيّنة.</summary>
public sealed record PrayerEvent(
    Prayer Prayer,
    EventKind Kind,
    DateTimeOffset At,
    TimeSpan Tolerance,
    string SoundFile,
    double Volume)
{
    /// <summary>
    /// مفتاح ثابت يميّز الحدث في سجل الإطلاق.
    /// التاريخ محلي وليس UTC حتى يبقى المفتاح مطابقًا لليوم الذي يراه المستخدم.
    /// التنسيق ثابت الثقافة إلزامًا: مع ثقافة عربية قد يتحوّل التاريخ إلى أرقام
    /// هندية أو إلى التقويم الهجري، فينكسر مطابقة المفاتيح ويتكرّر الأذان.
    /// </summary>
    public string Key =>
        string.Create(CultureInfo.InvariantCulture, $"{At:yyyy-MM-dd}|{Prayer}|{Kind}");

    public string ArabicLabel => Kind == EventKind.Adhan
        ? $"أذان {Prayer.Ar()}"
        : $"إقامة {Prayer.Ar()}";
}
