using PrayTime.Core.Astronomy;
using PrayTime.Core.Model;

namespace PrayTime.Core.Calculation;

/// <summary>
/// حساب مواقيت الصلاة فلكيًا وبالكامل دون اتصال بالإنترنت.
/// دوال خالصة: نفس المدخلات تعطي دائمًا نفس المخرجات.
/// </summary>
public static class PrayerTimesCalculator
{
    /// <summary>انكسار الغلاف الجوي (٣٤ دقيقة قوسية) + نصف قطر قرص الشمس (١٦ دقيقة قوسية).</summary>
    private const double SunsetRefraction = 0.833;

    // تقديرات ابتدائية بالساعات المحلية لبدء التكرار.
    private static readonly double[] Seeds = [5, 6, 12, 13, 18, 19];

    public static PrayerDay Calculate(
        DateOnly date,
        GeoLocation location,
        CalculationParameters parameters,
        TimeZoneInfo timeZone)
    {
        var localMidnight = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var tzHours = timeZone.GetUtcOffset(localMidnight).TotalHours;

        var jd0 = JulianDate.FromCivilDate(date);
        var lat = location.Latitude;
        var lng = location.Longitude;

        // انخفاض الأفق الناتج عن الارتفاع عن سطح البحر.
        var horizonDip = 0.0347 * Math.Sqrt(Math.Max(0, location.ElevationMeters));
        var sunsetAngle = -(SunsetRefraction + horizonDip);

        // تقديرات ابتدائية ثم دورات تنقيح: الميل ومعادلة الزمن يعتمدان على الوقت نفسه.
        var t = (double[])Seeds.Clone();

        for (var iter = 0; iter < Math.Max(1, parameters.Iterations); iter++)
        {
            var noon = ComputeNoon(jd0, tzHours, lng, t[(int)Prayer.Dhuhr]);

            t[(int)Prayer.Dhuhr] = noon;
            t[(int)Prayer.Fajr] = SolveBefore(jd0, tzHours, lat, t[(int)Prayer.Fajr], -parameters.FajrAngle, noon);
            t[(int)Prayer.Sunrise] = SolveBefore(jd0, tzHours, lat, t[(int)Prayer.Sunrise], sunsetAngle, noon);
            t[(int)Prayer.Maghrib] = SolveAfter(jd0, tzHours, lat, t[(int)Prayer.Maghrib], sunsetAngle, noon);
            t[(int)Prayer.Asr] = SolveAsr(jd0, tzHours, lat, t[(int)Prayer.Asr], parameters.AsrJuristic, noon);

            t[(int)Prayer.Isha] = parameters.IshaIntervalMinutes is { } interval
                ? t[(int)Prayer.Maghrib] + interval / 60.0
                : SolveAfter(jd0, tzHours, lat, t[(int)Prayer.Isha], -parameters.IshaAngle, noon);
        }

        SanitizeDegenerateDay(t);
        ApplyHighLatitudeRule(t, parameters);

        // التجسيد: إضافة الإزاحات، ثم التقريب، ثم التحويل إلى وقت مطلق.
        var results = new DateTimeOffset[6];
        for (var i = 0; i < 6; i++)
        {
            var minutes = t[i] * 60.0 + parameters.OffsetFor((Prayer)i);
            results[i] = Materialize(localMidnight, Round(minutes, parameters.Rounding), timeZone);
        }

        return new PrayerDay(date, results);
    }

    /// <summary>وقت عبور الشمس خط الزوال (الظهر الفلكي) بالساعات المحلية.</summary>
    private static double ComputeNoon(double jd0, double tzHours, double lng, double estimate)
    {
        var sun = SunAt(jd0, tzHours, estimate);
        return 12.0 + tzHours - lng / 15.0 - sun.EquationOfTimeHours;
    }

    private static SolarPosition SunAt(double jd0, double tzHours, double localHours)
    {
        var daysFromJ2000 = jd0 + (localHours - tzHours) / 24.0 - 2451545.0;
        return SolarCalculator.Compute(daysFromJ2000);
    }

    /// <summary>زاوية الساعة اللازمة لبلوغ الشمس ارتفاعًا معيّنًا، بالساعات. NaN إذا تعذّر.</summary>
    private static double HourAngle(double lat, double declination, double altitudeDeg)
    {
        var numerator = AngleMath.Sin(altitudeDeg) - AngleMath.Sin(lat) * AngleMath.Sin(declination);
        var denominator = AngleMath.Cos(lat) * AngleMath.Cos(declination);

        if (Math.Abs(denominator) < 1e-12) return double.NaN;

        if (!AngleMath.TryClampCos(numerator / denominator, out var clamped)) return double.NaN;

        return AngleMath.Acos(clamped) / 15.0;
    }

    private static double SolveBefore(double jd0, double tz, double lat,
                                      double estimate, double altitude, double noon)
    {
        var sun = SunAt(jd0, tz, estimate);
        var h = HourAngle(lat, sun.DeclinationDeg, altitude);
        return double.IsNaN(h) ? double.NaN : noon - h;
    }

    private static double SolveAfter(double jd0, double tz, double lat,
                                     double estimate, double altitude, double noon)
    {
        var sun = SunAt(jd0, tz, estimate);
        var h = HourAngle(lat, sun.DeclinationDeg, altitude);
        return double.IsNaN(h) ? double.NaN : noon + h;
    }

    /// <summary>
    /// العصر: عندما يصير ظل الشيء مثله (شافعي) أو مثليه (حنفي) زيادةً على ظل الزوال.
    /// ارتفاع الشمس المقابل = arccot(k + tan|φ − δ|).
    /// </summary>
    private static double SolveAsr(double jd0, double tz, double lat,
                                   double estimate, AsrJuristic juristic, double noon)
    {
        var sun = SunAt(jd0, tz, estimate);
        var altitude = AngleMath.Acot((int)juristic + AngleMath.Tan(Math.Abs(lat - sun.DeclinationDeg)));
        var h = HourAngle(lat, sun.DeclinationDeg, altitude);
        return double.IsNaN(h) ? double.NaN : noon + h;
    }

    /// <summary>
    /// الحالات الشاذة: شمس منتصف الليل أو ليل قطبي، حيث لا يوجد شروق أو غروب إطلاقًا.
    /// نضع بديلًا معقولًا (الظهر ∓ ٦ ساعات) حتى لا تتسرّب NaN إلى الجدولة فتنهار.
    /// مستحيل الحدوث في عُمان؛ موجود لأن الموقع قابل للتغيير.
    /// </summary>
    private static void SanitizeDegenerateDay(double[] t)
    {
        var noon = t[(int)Prayer.Dhuhr];
        if (double.IsNaN(noon)) noon = 12.0;

        if (double.IsNaN(t[(int)Prayer.Sunrise])) t[(int)Prayer.Sunrise] = noon - 6.0;
        if (double.IsNaN(t[(int)Prayer.Maghrib])) t[(int)Prayer.Maghrib] = noon + 6.0;
        if (double.IsNaN(t[(int)Prayer.Asr])) t[(int)Prayer.Asr] = noon + 3.0;
        t[(int)Prayer.Dhuhr] = noon;
    }

    /// <summary>
    /// تصحيح خطوط العرض العالية. لا يُفعَّل أبدًا عند مسقط —
    /// جيب تمام زاوية الساعة لا يتجاوز 0.56 مطلقًا عند خط عرض 23.6° بزاوية 18°.
    /// موجود لصحة الحساب إن غيّر المستخدم الموقع.
    /// </summary>
    private static void ApplyHighLatitudeRule(double[] t, CalculationParameters p)
    {
        var sunrise = t[(int)Prayer.Sunrise];
        var maghrib = t[(int)Prayer.Maghrib];

        // بدون شروق أو غروب صالحين لا يمكن اشتقاق أي قاعدة.
        if (double.IsNaN(sunrise) || double.IsNaN(maghrib)) return;

        var night = 24.0 - (maghrib - sunrise);

        double Portion(double angle) => p.HighLatitudeRule switch
        {
            HighLatitudeRule.NightMiddle => night / 2.0,
            HighLatitudeRule.OneSeventh => night / 7.0,
            HighLatitudeRule.AngleBased => night * angle / 60.0,
            _ => double.NaN
        };

        var fajrPortion = Portion(p.FajrAngle);
        if (!double.IsNaN(fajrPortion))
        {
            var earliest = sunrise - fajrPortion;
            if (double.IsNaN(t[(int)Prayer.Fajr]) || t[(int)Prayer.Fajr] < earliest)
                t[(int)Prayer.Fajr] = earliest;
        }
        else if (double.IsNaN(t[(int)Prayer.Fajr]))
        {
            t[(int)Prayer.Fajr] = sunrise - night / 7.0; // احتياطي حتى لا تنهار الجدولة
        }

        var ishaPortion = Portion(p.IshaIntervalMinutes is null ? p.IshaAngle : 18.0);
        if (!double.IsNaN(ishaPortion))
        {
            var latest = maghrib + ishaPortion;
            if (double.IsNaN(t[(int)Prayer.Isha]) || t[(int)Prayer.Isha] > latest)
                t[(int)Prayer.Isha] = latest;
        }
        else if (double.IsNaN(t[(int)Prayer.Isha]))
        {
            t[(int)Prayer.Isha] = maghrib + night / 7.0;
        }
    }

    private static double Round(double minutes, TimeRounding mode) => mode switch
    {
        TimeRounding.Floor => Math.Floor(minutes),
        TimeRounding.Ceiling => Math.Ceiling(minutes),
        _ => Math.Round(minutes, MidpointRounding.AwayFromZero)
    };

    /// <summary>
    /// تحويل «دقائق من منتصف الليل المحلي» إلى وقت مطلق.
    /// نستخدم TimeZoneInfo بدل رقم ثابت حتى يبقى صحيحًا لو غيّر المستخدم الموقع.
    /// </summary>
    private static DateTimeOffset Materialize(DateTime localMidnight, double minutes, TimeZoneInfo tz)
    {
        var whole = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        var naive = localMidnight.AddMinutes(whole);

        // التوقيت الصيفي يجعل بعض اللحظات غير موجودة أو مكرّرة. عُمان لا تطبّقه،
        // لكن الموقع قابل للتغيير فنتعامل مع الحالتين.
        if (tz.IsInvalidTime(naive)) naive = naive.AddHours(1);

        var offset = tz.IsAmbiguousTime(naive)
            ? tz.GetAmbiguousTimeOffsets(naive)[0]
            : tz.GetUtcOffset(naive);

        return new DateTimeOffset(naive, offset);
    }
}
