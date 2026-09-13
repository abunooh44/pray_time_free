using PrayTime.Core.Model;
using PrayTime.Core.Scheduling;
using PrayTime.Core.Settings;
using Xunit;

namespace PrayTime.Core.Tests;

public class ScheduleEngineTests
{
    private static readonly TimeZoneInfo Muscat = TimeZoneInfo.FindSystemTimeZoneById("Asia/Muscat");

    private static AppSettings Defaults()
    {
        var s = new AppSettings();
        s.Behavior.StartupGraceSeconds = 0; // نعطّل مهلة الإقلاع في معظم الاختبارات
        return s;
    }

    private static DateTimeOffset Muscat_(int y, int m, int d, int hh, int mm, int ss = 0) =>
        new(new DateTime(y, m, d, hh, mm, ss), Muscat.GetUtcOffset(new DateTime(y, m, d, hh, mm, ss)));

    private static (PrayerScheduleEngine Engine, FiredLedger Ledger) Build(AppSettings settings)
    {
        var ledger = new FiredLedger();
        return (new PrayerScheduleEngine(() => settings, ledger), ledger);
    }

    [Fact]
    public void FiresAdhanExactlyOnceAtItsTime()
    {
        var settings = Defaults();
        var (engine, _) = Build(settings);

        var start = Muscat_(2026, 9, 13, 12, 0);
        engine.Start(start);

        // الظهر 12:02 في بوشر. ننبض ثانية بثانية عبره.
        var fired = new List<PrayerEvent>();
        for (var t = start; t <= start.AddMinutes(5); t = t.AddSeconds(1))
            fired.AddRange(engine.Tick(t).ToFire);

        var adhans = fired.Where(e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan }).ToList();
        Assert.Single(adhans);
        Assert.Equal(12, adhans[0].At.Hour);
        Assert.Equal(2, adhans[0].At.Minute);
    }

    [Fact]
    public void FiresIqamaAfterTheConfiguredDelay()
    {
        var settings = Defaults();
        settings.Prayers.Dhuhr.IqamaDelayMinutes = 7;
        var (engine, _) = Build(settings);

        var start = Muscat_(2026, 9, 13, 12, 0);
        engine.Start(start);

        var fired = new List<PrayerEvent>();
        for (var t = start; t <= start.AddMinutes(15); t = t.AddSeconds(1))
            fired.AddRange(engine.Tick(t).ToFire);

        var adhan = fired.Single(e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan });
        var iqama = fired.Single(e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Iqama });

        Assert.Equal(7, (iqama.At - adhan.At).TotalMinutes);
    }

    [Fact]
    public void SleepingPastAPrayerMarksItMissedInsteadOfFiringLate()
    {
        var settings = Defaults();
        settings.Behavior.AdhanToleranceMinutes = 5;
        var (engine, _) = Build(settings);

        // ننام قبل الظهر بقليل ونستيقظ بعده بثماني ساعات.
        var before = Muscat_(2026, 9, 13, 11, 30);
        engine.Start(before);
        engine.Tick(before);

        var afterSleep = before.AddHours(8);
        var result = engine.Tick(afterSleep);

        Assert.True(result.TimeJumpDetected);
        Assert.Contains(result.Missed, e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan });
        Assert.DoesNotContain(result.ToFire, e => e.Prayer == Prayer.Dhuhr);
    }

    [Fact]
    public void WakingUpShortlyAfterAPrayerStillFiresIt()
    {
        var settings = Defaults();
        settings.Behavior.AdhanToleranceMinutes = 5;
        var (engine, _) = Build(settings);

        var before = Muscat_(2026, 9, 13, 11, 58);
        engine.Start(before);
        engine.Tick(before);

        // استيقاظ بعد الظهر بثلاث دقائق فقط — داخل نافذة التسامح.
        var result = engine.Tick(Muscat_(2026, 9, 13, 12, 5));

        Assert.True(result.TimeJumpDetected);
        Assert.Contains(result.ToFire, e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan });
    }

    [Fact]
    public void ClockJumpingBackwardsDoesNotRefireTheSameAdhan()
    {
        var settings = Defaults();
        var (engine, _) = Build(settings);

        var start = Muscat_(2026, 9, 13, 12, 0);
        engine.Start(start);

        var firedFirstTime = new List<PrayerEvent>();
        for (var t = start; t <= start.AddMinutes(4); t = t.AddSeconds(30))
            firedFirstTime.AddRange(engine.Tick(t).ToFire);

        Assert.Contains(firedFirstTime, e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan });

        // المستخدم أرجع ساعة النظام نصف ساعة للخلف.
        var refired = new List<PrayerEvent>();
        for (var t = start.AddMinutes(-30); t <= start.AddMinutes(4); t = t.AddSeconds(30))
            refired.AddRange(engine.Tick(t).ToFire);

        Assert.DoesNotContain(refired, e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan });
    }

    [Fact]
    public void LedgerSurvivesRestartSoAdhanIsNotRepeated()
    {
        var settings = Defaults();
        var ledger = new FiredLedger();

        var start = Muscat_(2026, 9, 13, 12, 0);
        var first = new PrayerScheduleEngine(() => settings, ledger);
        first.Start(start);
        for (var t = start; t <= start.AddMinutes(4); t = t.AddSeconds(30)) first.Tick(t);

        // إعادة تشغيل التطبيق بنفس السجل المحفوظ على القرص.
        var second = new PrayerScheduleEngine(() => settings, ledger);
        second.Start(start.AddMinutes(3));

        var fired = new List<PrayerEvent>();
        for (var t = start.AddMinutes(3); t <= start.AddMinutes(6); t = t.AddSeconds(30))
            fired.AddRange(second.Tick(t).ToFire);

        Assert.DoesNotContain(fired, e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan });
    }

    [Fact]
    public void DayRolloverRebuildsTheScheduleAutomatically()
    {
        var settings = Defaults();
        var (engine, _) = Build(settings);

        var beforeMidnight = Muscat_(2026, 9, 13, 23, 59, 30);
        engine.Start(beforeMidnight);
        engine.Tick(beforeMidnight);

        var dateBefore = engine.Today!.Date;

        engine.Tick(Muscat_(2026, 9, 14, 0, 0, 30));

        Assert.Equal(new DateOnly(2026, 9, 13), dateBefore);
        Assert.Equal(new DateOnly(2026, 9, 14), engine.Today!.Date);
        Assert.Equal(new DateOnly(2026, 9, 15), engine.Tomorrow!.Date);
    }

    [Fact]
    public void DisabledAdhanIsNotScheduledButIqamaStillIs()
    {
        var settings = Defaults();
        settings.Prayers.Dhuhr.AdhanEnabled = false;
        settings.Prayers.Dhuhr.IqamaEnabled = true;
        settings.Prayers.Dhuhr.IqamaDelayMinutes = 10;

        var (engine, _) = Build(settings);
        var start = Muscat_(2026, 9, 13, 12, 0);
        engine.Start(start);

        var fired = new List<PrayerEvent>();
        for (var t = start; t <= start.AddMinutes(20); t = t.AddSeconds(30))
            fired.AddRange(engine.Tick(t).ToFire);

        Assert.DoesNotContain(fired, e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan });
        Assert.Contains(fired, e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Iqama });
    }

    [Fact]
    public void StartupGraceSuppressesAnAdhanThatAlreadyPassed()
    {
        var settings = Defaults();
        settings.Behavior.StartupGraceSeconds = 60;
        settings.Behavior.AdhanToleranceMinutes = 5;

        var (engine, _) = Build(settings);

        // التطبيق يقلع بعد الظهر بثلاث دقائق (تسجيل دخول متأخر).
        var boot = Muscat_(2026, 9, 13, 12, 5);
        engine.Start(boot);
        var result = engine.Tick(boot);

        Assert.DoesNotContain(result.ToFire, e => e.Prayer == Prayer.Dhuhr);
        Assert.Contains(result.Missed, e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan });
    }

    [Fact]
    public void NextPrayerAdvancesAcrossMidnightToTomorrowFajr()
    {
        var settings = Defaults();
        var (engine, _) = Build(settings);

        var lateNight = Muscat_(2026, 9, 13, 23, 0);
        engine.Start(lateNight);
        engine.Tick(lateNight);

        Assert.NotNull(engine.NextPrayer);
        Assert.Equal(Prayer.Fajr, engine.NextPrayer!.Value.Prayer);
        Assert.Equal(14, engine.NextPrayer.Value.At.Day);
    }

    [Fact]
    public void LedgerPruneKeepsRecentAndDropsOld()
    {
        var ledger = new FiredLedger();
        ledger.MarkFired("2026-09-13|Fajr|Adhan");
        ledger.MarkFired("2026-09-01|Fajr|Adhan");
        ledger.MarkFired("garbage-key");

        var removed = ledger.Prune(new DateOnly(2026, 9, 13));

        Assert.Equal(2, removed);
        Assert.True(ledger.HasFired("2026-09-13|Fajr|Adhan"));
        Assert.False(ledger.HasFired("2026-09-01|Fajr|Adhan"));
    }
}
