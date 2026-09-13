namespace PrayTime.Core.Astronomy;

/// <summary>ميل الشمس ومعادلة الزمن للحظة معيّنة.</summary>
public readonly record struct SolarPosition(double DeclinationDeg, double EquationOfTimeHours);

public static class SolarCalculator
{
    /// <summary>
    /// موضع الشمس منخفض الدقة وفق تقويم المرصد الفلكي الأمريكي (USNO).
    /// الدقة أفضل من 0.01 درجة — أعلى بكثير مما تحتاجه مواقيت بدقة الدقيقة.
    /// </summary>
    /// <param name="daysFromJ2000">عدد الأيام من الحقبة J2000.0 بالتوقيت العالمي.</param>
    public static SolarPosition Compute(double daysFromJ2000)
    {
        var d = daysFromJ2000;

        var g = AngleMath.Fix360(357.529 + 0.98560028 * d);   // الشذوذ الوسطي
        var q = AngleMath.Fix360(280.459 + 0.98564736 * d);   // الطول الوسطي
        var l = AngleMath.Fix360(q + 1.915 * AngleMath.Sin(g) + 0.020 * AngleMath.Sin(2 * g)); // الطول الظاهري

        var e = 23.439 - 0.00000036 * d;                      // ميل دائرة البروج

        var declination = AngleMath.Asin(AngleMath.Sin(e) * AngleMath.Sin(l));

        var rightAscensionHours =
            AngleMath.Fix360(AngleMath.Atan2(AngleMath.Cos(e) * AngleMath.Sin(l), AngleMath.Cos(l))) / 15.0;

        var equationOfTime = AngleMath.Fix12Signed(q / 15.0 - rightAscensionHours);

        return new SolarPosition(declination, equationOfTime);
    }
}
