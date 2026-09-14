using PrayTime.Core.Calculation;
using PrayTime.Core.Model;
using PrayTime.Core.Settings;

namespace PrayTime.Core.Scheduling;

/// <summary>يحوّل مواقيت يومٍ ما إلى قائمة أحداث صوتية مرتّبة زمنيًا.</summary>
public static class ScheduleBuilder
{
    /// <summary>
    /// أقل فاصل بين الأذان وإجراء ما قبل الإقامة.
    /// قفل الشاشة لحظة رفع الأذان يقطعه ويربك المستخدم، وهو ما كان يحدث
    /// كلما ساوت مهلة التنبيه تأخيرَ الإقامة.
    /// </summary>
    public static readonly TimeSpan MinGapAfterAdhan = TimeSpan.FromMinutes(2);

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
                        // الهدف: قبل الإقامة بالمدة المطلوبة.
                        var actionAt = iqamaAt.AddMinutes(-settings.Behavior.PreIqamaMinutes);

                        // لكن لا يقع على الأذان نفسه. حين يكون تأخير الإقامة مساويًا للمهلة
                        // أو أقصر (المغرب مثلًا: إقامة بعد ٥ دقائق ومهلة ٥)، يقع الطرح على
                        // لحظة الأذان تمامًا فتُقفل الشاشة والأذان يُرفع.
                        var earliest = at.Add(MinGapAfterAdhan);
                        if (actionAt < earliest) actionAt = earliest;

                        // ولا يتجاوز الإقامة نفسها لو كان التأخير قصيرًا جدًا.
                        if (actionAt > iqamaAt) actionAt = iqamaAt;

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
