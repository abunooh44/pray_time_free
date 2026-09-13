using PrayTime.Core.Model;
using PrayTime.Core.Scheduling;
using PrayTime.Core.Settings;
using Xunit;

namespace PrayTime.Core.Tests;

public class PreIqamaActionTests
{
    private static AppSettings Settings(PreIqamaAction action, int minutesBefore)
    {
        var s = new AppSettings();
        s.Behavior.PreIqamaAction = action;
        s.Behavior.PreIqamaMinutes = minutesBefore;
        return s;
    }

    private static IReadOnlyList<PrayerEvent> BuildToday(AppSettings s) =>
        ScheduleBuilder.BuildEvents(new DateOnly(2026, 9, 13), 1, s);

    [Fact]
    public void NoEventsAreAddedWhenTheFeatureIsOff()
    {
        var events = BuildToday(Settings(PreIqamaAction.None, 5));
        Assert.DoesNotContain(events, e => e.Kind == EventKind.PreIqama);
    }

    [Fact]
    public void OneEventPerPrayerFiresTheConfiguredMinutesBeforeIqama()
    {
        var s = Settings(PreIqamaAction.Lock, 5);
        var events = BuildToday(s);

        foreach (var prayer in PrayerNames.Adhanable)
        {
            var iqama = events.Single(e => e.Prayer == prayer && e.Kind == EventKind.Iqama);
            var action = events.Single(e => e.Prayer == prayer && e.Kind == EventKind.PreIqama);

            Assert.Equal(5, (iqama.At - action.At).TotalMinutes);
        }
    }

    [Fact]
    public void ActionNeverFiresBeforeTheAdhanItself()
    {
        // المغرب تأخير إقامته ٥ دقائق افتراضيًا، والمهلة المطلوبة ١٥ —
        // الطرح المباشر يضع التنبيه قبل دخول الوقت بعشر دقائق.
        var s = Settings(PreIqamaAction.Lock, 15);
        var events = BuildToday(s);

        var adhan = events.Single(e => e is { Prayer: Prayer.Maghrib, Kind: EventKind.Adhan });
        var action = events.Single(e => e is { Prayer: Prayer.Maghrib, Kind: EventKind.PreIqama });

        Assert.Equal(adhan.At, action.At);
        Assert.True(action.At >= adhan.At, "التنبيه سبق الأذان");
    }

    [Fact]
    public void NoActionForPrayersWithIqamaDisabled()
    {
        var s = Settings(PreIqamaAction.Lock, 5);
        s.Prayers.Asr.IqamaEnabled = false;

        var events = BuildToday(s);

        Assert.DoesNotContain(events, e => e is { Prayer: Prayer.Asr, Kind: EventKind.PreIqama });
        Assert.Contains(events, e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.PreIqama });
    }

    [Fact]
    public void ActionToleranceIsTightSoItNeverFiresLongAfterWaking()
    {
        var events = BuildToday(Settings(PreIqamaAction.Sleep, 5));
        var action = events.First(e => e.Kind == EventKind.PreIqama);

        // نافذة واسعة تعني إنامة الجهاز بعد فوات الصلاة، وهو إزعاج بلا فائدة.
        Assert.Equal(TimeSpan.FromMinutes(1), action.Tolerance);
    }

    [Fact]
    public void EngineFiresTheActionEventAtItsTime()
    {
        var s = Settings(PreIqamaAction.Lock, 5);
        s.Behavior.StartupGraceSeconds = 0;
        s.Prayers.Dhuhr.IqamaDelayMinutes = 10;

        var engine = new PrayerScheduleEngine(() => s, new FiredLedger());
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Muscat");
        var start = new DateTimeOffset(new DateTime(2026, 9, 13, 12, 0, 0), tz.BaseUtcOffset);

        engine.Start(start);

        var fired = new List<PrayerEvent>();
        for (var t = start; t <= start.AddMinutes(15); t = t.AddSeconds(20))
            fired.AddRange(engine.Tick(t).ToFire);

        var adhan = fired.Single(e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Adhan });
        var action = fired.Single(e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.PreIqama });
        var iqama = fired.Single(e => e is { Prayer: Prayer.Dhuhr, Kind: EventKind.Iqama });

        // الأذان 12:02، الإقامة 12:12، والإجراء قبلها بخمس: 12:07
        Assert.Equal(5, (action.At - adhan.At).TotalMinutes);
        Assert.Equal(5, (iqama.At - action.At).TotalMinutes);
    }
}
