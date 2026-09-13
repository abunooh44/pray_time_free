using PrayTime.Core.Calculation;
using PrayTime.Core.Model;
using PrayTime.Core.Settings;

namespace PrayTime.Core.Scheduling;

/// <summary>يحوّل مواقيت يومٍ ما إلى قائمة أحداث صوتية مرتّبة زمنيًا.</summary>
public static class ScheduleBuilder
{
    public static PrayerDay BuildDay(DateOnly date, AppSettings settings) =>
        PrayerTimesCalculator.Calculate(
            date, settings.GeoLocation, settings.ToCalculationParameters(), settings.TimeZone);

    public static IReadOnlyList<PrayerEvent> BuildEvents(DateOnly from, int days, AppSettings settings)
    {
        var events = new List<PrayerEvent>(days * 10);
        var adhanTolerance = TimeSpan.FromMinutes(settings.Behavior.AdhanToleranceMinutes);
        var iqamaTolerance = TimeSpan.FromMinutes(settings.Behavior.IqamaToleranceMinutes);

        for (var i = 0; i < days; i++)
        {
            var day = BuildDay(from.AddDays(i), settings);

            foreach (var prayer in PrayerNames.Adhanable)
            {
                var config = settings.Prayers[prayer];
                var volume = config.Volume * settings.Audio.MasterVolume;
                var at = day[prayer];

                if (config.AdhanEnabled)
                {
                    events.Add(new PrayerEvent(
                        prayer, EventKind.Adhan, at, adhanTolerance, config.AdhanSound, volume));
                }

                // الإقامة مستقلة عن الأذان: يمكن كتم الأذان وإبقاء تنبيه الإقامة.
                if (config.IqamaEnabled && config.IqamaDelayMinutes > 0)
                {
                    var iqamaAt = at.AddMinutes(config.IqamaDelayMinutes);

                    events.Add(new PrayerEvent(
                        prayer, EventKind.Iqama, iqamaAt, iqamaTolerance, config.IqamaSound, volume));

                    if (settings.Behavior.PreIqamaAction != PreIqamaAction.None)
                    {
                        // لا يسبق الأذانَ أبدًا: لو كان تأخير الإقامة أقصر من مهلة الإجراء
                        // لصار التنبيه قبل دخول الوقت أصلًا.
                        var actionAt = iqamaAt.AddMinutes(-settings.Behavior.PreIqamaMinutes);
                        if (actionAt < at) actionAt = at;

                        events.Add(new PrayerEvent(
                            prayer, EventKind.PreIqama, actionAt,
                            // تسامح ضيّق: إنامة الجهاز بعد فوات الوقت بكثير تصرّف مزعج بلا فائدة.
                            TimeSpan.FromMinutes(1), string.Empty, 0));
                    }
                }
            }
        }

        events.Sort((a, b) => a.At.CompareTo(b.At));
        return events;
    }
}
