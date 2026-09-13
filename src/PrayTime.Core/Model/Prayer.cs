namespace PrayTime.Core.Model;

public enum Prayer
{
    Fajr = 0,
    Sunrise = 1,
    Dhuhr = 2,
    Asr = 3,
    Maghrib = 4,
    Isha = 5
}

public static class PrayerNames
{
    private static readonly string[] Arabic = ["الفجر", "الشروق", "الظهر", "العصر", "المغرب", "العشاء"];

    public static string Ar(this Prayer p) => Arabic[(int)p];

    /// <summary>الصلوات التي يُرفع لها أذان (الشروق ليس صلاة).</summary>
    public static readonly Prayer[] Adhanable =
        [Prayer.Fajr, Prayer.Dhuhr, Prayer.Asr, Prayer.Maghrib, Prayer.Isha];

    public static readonly Prayer[] All =
        [Prayer.Fajr, Prayer.Sunrise, Prayer.Dhuhr, Prayer.Asr, Prayer.Maghrib, Prayer.Isha];
}
