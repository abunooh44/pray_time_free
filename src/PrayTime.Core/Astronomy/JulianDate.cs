namespace PrayTime.Core.Astronomy;

public static class JulianDate
{
    /// <summary>اليوم اليولياني عند الساعة 00:00 بالتوقيت العالمي للتاريخ الميلادي المعطى.</summary>
    public static double FromCivilDate(int year, int month, int day)
    {
        if (month <= 2)
        {
            year -= 1;
            month += 12;
        }

        var a = Math.Floor(year / 100.0);
        var b = 2 - a + Math.Floor(a / 4.0);

        return Math.Floor(365.25 * (year + 4716))
             + Math.Floor(30.6001 * (month + 1))
             + day + b - 1524.5;
    }

    public static double FromCivilDate(DateOnly date) => FromCivilDate(date.Year, date.Month, date.Day);
}
