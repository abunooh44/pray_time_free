using PrayTime.Core.Calculation;
using PrayTime.Core.Model;
using Xunit;

namespace PrayTime.Core.Tests;

public class PrayerTimesCalculatorTests
{
    private static readonly TimeZoneInfo Muscat = TimeZoneInfo.FindSystemTimeZoneById("Asia/Muscat");

    private static PrayerDay Compute(DateOnly date, CalculationParameters? p = null, double elevation = 0)
    {
        var loc = GeoLocation.Bawshar with { ElevationMeters = elevation };
        return PrayerTimesCalculator.Calculate(date, loc, p ?? CalculationMethod.OmanAwqaf.ToParameters(), Muscat);
    }

    private static void AssertWithinAMinute(PrayerDay day, Prayer prayer, string expected)
    {
        var actual = day[prayer];
        var parts = expected.Split(':');
        var want = new DateTime(day.Date.Year, day.Date.Month, day.Date.Day,
            int.Parse(parts[0]), int.Parse(parts[1]), 0);

        var deltaMinutes = Math.Abs((actual.DateTime - want).TotalMinutes);
        Assert.True(deltaMinutes <= 1.0,
            $"{prayer}: expected {expected}, got {actual:HH:mm} (فارق {deltaMinutes:F2} دقيقة)");
    }

    // القيم المرجعية مأخوذة من api.aladhan.com لإحداثيات بوشر
    // بطريقة مخصّصة: فجر ١٨° / عشاء ١٨° / عصر شافعي / Asia/Muscat.
    [Theory]
    [InlineData(2026, 9, 13, "04:36", "05:52", "12:02", "15:30", "18:12", "19:28")]
    [InlineData(2026, 6, 21, "03:53", "05:20", "12:08", "15:26", "18:56", "20:24")]
    [InlineData(2026, 12, 21, "05:23", "06:44", "12:04", "15:05", "17:25", "18:46")]
    public void MatchesReferenceTimesForBawshar(
        int y, int m, int d,
        string fajr, string sunrise, string dhuhr, string asr, string maghrib, string isha)
    {
        var day = Compute(new DateOnly(y, m, d));

        AssertWithinAMinute(day, Prayer.Fajr, fajr);
        AssertWithinAMinute(day, Prayer.Sunrise, sunrise);
        AssertWithinAMinute(day, Prayer.Dhuhr, dhuhr);
        AssertWithinAMinute(day, Prayer.Asr, asr);
        AssertWithinAMinute(day, Prayer.Maghrib, maghrib);
        AssertWithinAMinute(day, Prayer.Isha, isha);
    }

    [Fact]
    public void PrayersAreInChronologicalOrder()
    {
        for (var i = 0; i < 365; i += 7)
        {
            var day = Compute(new DateOnly(2026, 1, 1).AddDays(i));
            Assert.True(day.Fajr < day.Sunrise, $"{day.Date}: الفجر بعد الشروق");
            Assert.True(day.Sunrise < day.Dhuhr, $"{day.Date}: الشروق بعد الظهر");
            Assert.True(day.Dhuhr < day.Asr, $"{day.Date}: الظهر بعد العصر");
            Assert.True(day.Asr < day.Maghrib, $"{day.Date}: العصر بعد المغرب");
            Assert.True(day.Maghrib < day.Isha, $"{day.Date}: المغرب بعد العشاء");
        }
    }

    [Fact]
    public void HanafiAsrIsLaterThanShafi()
    {
        var date = new DateOnly(2026, 9, 13);
        var shafi = Compute(date, CalculationMethod.OmanAwqaf.ToParameters(AsrJuristic.Shafi));
        var hanafi = Compute(date, CalculationMethod.OmanAwqaf.ToParameters(AsrJuristic.Hanafi));

        Assert.True(hanafi.Asr > shafi.Asr);
        Assert.True((hanafi.Asr - shafi.Asr).TotalMinutes is > 30 and < 90);
    }

    [Fact]
    public void OffsetsShiftTimesExactly()
    {
        var date = new DateOnly(2026, 9, 13);
        var baseline = Compute(date);

        var shifted = Compute(date, CalculationMethod.OmanAwqaf.ToParameters(
            offsets: new Dictionary<Prayer, int> { [Prayer.Fajr] = -5, [Prayer.Isha] = 7 }));

        Assert.Equal(-5, (shifted.Fajr - baseline.Fajr).TotalMinutes, 0);
        Assert.Equal(7, (shifted.Isha - baseline.Isha).TotalMinutes, 0);
        Assert.Equal(baseline.Dhuhr, shifted.Dhuhr);
    }

    [Fact]
    public void IshaIntervalModeIsExactlyNinetyMinutesAfterMaghrib()
    {
        var day = Compute(new DateOnly(2026, 9, 13), CalculationMethod.UmmAlQura.ToParameters());
        Assert.Equal(90, (day.Isha - day.Maghrib).TotalMinutes, 0);
    }

    [Fact]
    public void ElevationDelaysSunsetSlightly()
    {
        var date = new DateOnly(2026, 9, 13);
        var seaLevel = Compute(date, elevation: 0);
        var elevated = Compute(date, elevation: 40);

        var delta = (elevated.Maghrib - seaLevel.Maghrib).TotalMinutes;
        Assert.InRange(delta, 0.5, 2.0);
        Assert.True(elevated.Sunrise < seaLevel.Sunrise);
    }

    [Fact]
    public void NoTimeIsEverInvalidAtMuscatLatitude()
    {
        // خط عرض 23.6°: الشمس تعبر زاوية 18° كل ليلة طوال السنة،
        // فلا ينبغي أن تُفعَّل قاعدة خطوط العرض العالية أبدًا.
        for (var i = 0; i < 366; i++)
        {
            var day = Compute(new DateOnly(2028, 1, 1).AddDays(i));
            foreach (var p in PrayerNames.All)
            {
                Assert.NotEqual(default, day[p]);
                Assert.Equal(day.Date.Day, day[p].Day);
            }
        }
    }

    [Fact]
    public void HighLatitudeRuleEngagesWhereTwilightNeverEnds()
    {
        // لندن في الانقلاب الصيفي: الشمس تشرق وتغرب، لكنها لا تنزل إلى ١٨° تحت الأفق أبدًا،
        // فيتعذّر حساب الفجر والعشاء بالزاوية. هنا يجب أن تتدخّل القاعدة.
        var london = new GeoLocation("London", 51.5074, -0.1278, 0, "UTC");
        var date = new DateOnly(2026, 6, 21);
        var parameters = CalculationMethod.OmanAwqaf.ToParameters() with
        {
            HighLatitudeRule = HighLatitudeRule.OneSeventh
        };

        var day = PrayerTimesCalculator.Calculate(date, london, parameters, TimeZoneInfo.Utc);

        Assert.True(day.Fajr < day.Sunrise, "الفجر يجب أن يسبق الشروق");
        Assert.True(day.Maghrib < day.Isha, "المغرب يجب أن يسبق العشاء");

        // القاعدة تقسّم الليل على سبعة، والليل في لندن يومها نحو ٧ ساعات و٤٠ دقيقة.
        var nightPortion = (day.Sunrise - day.Fajr).TotalMinutes;
        Assert.InRange(nightPortion, 50, 80);
    }

    [Fact]
    public void PolarDayProducesOrderedTimesInsteadOfCrashing()
    {
        // ترومسو في يونيو: شمس منتصف الليل، لا شروق ولا غروب إطلاقًا.
        // لا يمكن إنتاج مواقيت صحيحة، لكن يجب ألا تتسرّب NaN فتنهار الجدولة.
        var tromso = new GeoLocation("Tromsø", 69.6492, 18.9553, 0, "UTC");
        var day = PrayerTimesCalculator.Calculate(
            new DateOnly(2026, 6, 21), tromso, CalculationMethod.OmanAwqaf.ToParameters(), TimeZoneInfo.Utc);

        foreach (var p in PrayerNames.All)
            Assert.NotEqual(default, day[p]);

        Assert.True(day.Fajr < day.Sunrise);
        Assert.True(day.Dhuhr < day.Asr);
        Assert.True(day.Asr < day.Maghrib);
        Assert.True(day.Maghrib < day.Isha);
    }
}
