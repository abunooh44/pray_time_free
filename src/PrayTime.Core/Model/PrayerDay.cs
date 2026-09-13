namespace PrayTime.Core.Model;

/// <summary>مواقيت يوم كامل، مُجسّدة كأوقات مطلقة في المنطقة الزمنية للموقع.</summary>
public sealed class PrayerDay
{
    private readonly DateTimeOffset[] _times;

    public PrayerDay(DateOnly date, DateTimeOffset[] times)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(times.Length, 6);
        Date = date;
        _times = times;
    }

    public DateOnly Date { get; }

    public DateTimeOffset this[Prayer p] => _times[(int)p];

    public DateTimeOffset Fajr => _times[0];
    public DateTimeOffset Sunrise => _times[1];
    public DateTimeOffset Dhuhr => _times[2];
    public DateTimeOffset Asr => _times[3];
    public DateTimeOffset Maghrib => _times[4];
    public DateTimeOffset Isha => _times[5];

    /// <summary>منتصف الليل الشرعي: نقطة المنتصف بين المغرب وفجر الغد.</summary>
    public DateTimeOffset IslamicMidnight(DateTimeOffset nextFajr) =>
        Maghrib + (nextFajr - Maghrib) / 2;

    /// <summary>بداية الثلث الأخير من الليل.</summary>
    public DateTimeOffset LastThird(DateTimeOffset nextFajr) =>
        Maghrib + (nextFajr - Maghrib) * 2 / 3;

    /// <summary>الصلاة القادمة في هذا اليوم، أو null إذا انقضت كلها.</summary>
    public (Prayer Prayer, DateTimeOffset At)? NextAfter(DateTimeOffset now, bool includeSunrise = true)
    {
        foreach (var p in includeSunrise ? PrayerNames.All : PrayerNames.Adhanable)
        {
            if (this[p] > now) return (p, this[p]);
        }
        return null;
    }

    /// <summary>الصلاة التي نحن في وقتها الآن، أو null إذا لم يدخل الفجر بعد.</summary>
    public (Prayer Prayer, DateTimeOffset At)? CurrentAt(DateTimeOffset now)
    {
        (Prayer, DateTimeOffset)? found = null;
        foreach (var p in PrayerNames.Adhanable)
        {
            if (this[p] <= now) found = (p, this[p]);
        }
        return found;
    }
}
