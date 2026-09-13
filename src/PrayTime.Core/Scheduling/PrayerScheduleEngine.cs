using PrayTime.Core.Model;
using PrayTime.Core.Settings;

namespace PrayTime.Core.Scheduling;

public sealed record TickResult(
    IReadOnlyList<PrayerEvent> ToFire,
    IReadOnlyList<PrayerEvent> Missed,
    bool TimeJumpDetected,
    TimeSpan JumpMagnitude)
{
    public static readonly TickResult Empty =
        new([], [], false, TimeSpan.Zero);

    public bool HasWork => ToFire.Count > 0 || Missed.Count > 0;
}

/// <summary>
/// قلب النظام. يُستدعى <see cref="Tick"/> كل ثانية، وأيضًا خارج الدور عند الاستيقاظ
/// من السبات أو تغيّر ساعة النظام.
///
/// المبدأ الأساسي: لا يوجد أي مؤقّت طويل الأمد. كل قرار يُشتق من الوقت الجداري الحالي
/// مقارنًا بجدول اليوم. مؤقّت مضبوط على «بعد ٦ ساعات» يتجمّد أثناء سبات الجهاز
/// فينطلق متأخرًا ساعتين، أما المقارنة بالوقت الجداري فلا تُخطئ أبدًا.
/// </summary>
public sealed class PrayerScheduleEngine
{
    /// <summary>فارق يتجاوز هذا الحد بين نبضتين يعني سباتًا أو تغييرًا في الساعة، لا مرور زمن طبيعي.</summary>
    private static readonly TimeSpan JumpThreshold = TimeSpan.FromSeconds(90);

    private readonly Func<AppSettings> _settings;
    private readonly FiredLedger _ledger;

    private DateOnly _scheduleDate = DateOnly.MinValue;
    private IReadOnlyList<PrayerEvent> _events = [];
    private DateTimeOffset? _lastTick;
    private DateTimeOffset? _startedAt;

    public PrayerScheduleEngine(Func<AppSettings> settings, FiredLedger ledger)
    {
        _settings = settings;
        _ledger = ledger;
    }

    public IReadOnlyList<PrayerEvent> Events => _events;
    public PrayerDay? Today { get; private set; }
    public PrayerDay? Tomorrow { get; private set; }

    /// <summary>الحدث الصوتي القادم الذي لم يُطلق بعد.</summary>
    public PrayerEvent? NextEvent { get; private set; }

    /// <summary>الصلاة القادمة للعرض (تشمل الشروق) حتى لو كان أذانها مكتومًا.</summary>
    public (Prayer Prayer, DateTimeOffset At)? NextPrayer { get; private set; }

    /// <summary>يجب استدعاؤها مرة واحدة عند الإقلاع لتفعيل مهلة الصمت الابتدائية.</summary>
    public void Start(DateTimeOffset now)
    {
        _startedAt = now;
        Rebuild(now);
    }

    /// <summary>يعيد بناء الجدول فورًا. تُستدعى عند تغيير الإعدادات أو تغيّر ساعة النظام.</summary>
    public void Rebuild(DateTimeOffset now)
    {
        var settings = _settings();
        var today = DateOnly.FromDateTime(now.DateTime);

        _scheduleDate = today;
        Today = ScheduleBuilder.BuildDay(today, settings);
        Tomorrow = ScheduleBuilder.BuildDay(today.AddDays(1), settings);

        // نبني يومين حتى يبقى «القادم» صحيحًا بعد عشاء الليلة وقبل منتصف الليل.
        _events = ScheduleBuilder.BuildEvents(today, 2, settings);
        _ledger.Prune(today);

        RecomputeNext(now);
    }

    public TickResult Tick(DateTimeOffset now)
    {
        var settings = _settings();

        // ١) كشف قفزة الزمن: سبات، تعليق، خنق المعالج، أو ضبط الساعة.
        var jump = TimeSpan.Zero;
        var jumped = false;
        if (_lastTick is { } last)
        {
            var delta = now - last;
            if (delta.Duration() > JumpThreshold)
            {
                jumped = true;
                jump = delta;
            }
        }
        _lastTick = now;

        // ٢) تدوير اليوم أو إعادة البناء بعد قفزة زمنية.
        var today = DateOnly.FromDateTime(now.DateTime);
        if (today != _scheduleDate || jumped)
        {
            Rebuild(now);
        }

        // ٣) مهلة الصمت بعد الإقلاع: لا نُفاجئ المستخدم بأذان لحظة تسجيل الدخول.
        var inStartupGrace = _startedAt is { } started &&
                             (now - started) < TimeSpan.FromSeconds(settings.Behavior.StartupGraceSeconds);

        var toFire = new List<PrayerEvent>();
        var missed = new List<PrayerEvent>();

        foreach (var e in _events)
        {
            if (e.At > now) break;               // الأحداث مرتّبة، فما بعدها لم يحن بعد
            if (_ledger.HasFired(e.Key)) continue;

            var lateness = now - e.At;

            if (lateness <= e.Tolerance && !(inStartupGrace && lateness > TimeSpan.FromSeconds(60)))
            {
                toFire.Add(e);
            }
            else
            {
                // فات وقته بفارق كبير: نسجّله كفائت بلا صوت.
                // أذان يتأخر خمس دقائق مفيد؛ أذان يتأخر نصف ساعة مُربك وقد يقع في وقت صلاة أخرى.
                missed.Add(e);
            }

            _ledger.MarkFired(e.Key);
        }

        RecomputeNext(now);

        return toFire.Count == 0 && missed.Count == 0 && !jumped
            ? TickResult.Empty
            : new TickResult(toFire, missed, jumped, jump);
    }

    private void RecomputeNext(DateTimeOffset now)
    {
        NextEvent = _events.FirstOrDefault(e => e.At > now && !_ledger.HasFired(e.Key));

        NextPrayer = Today?.NextAfter(now) ?? Tomorrow?.NextAfter(now);
        if (NextPrayer is null && Tomorrow is not null)
        {
            NextPrayer = (Prayer.Fajr, Tomorrow.Fajr);
        }
    }

    /// <summary>
    /// الصلاة الحالية ووقت انتهائها، لرسم شريط التقدّم.
    /// بعد العشاء يمتد الوقت حتى فجر الغد.
    /// </summary>
    public (Prayer Prayer, DateTimeOffset From, DateTimeOffset To)? CurrentWindow(DateTimeOffset now)
    {
        if (Today is null) return null;

        var current = Today.CurrentAt(now);
        if (current is null)
        {
            // قبل فجر اليوم: نحن في وقت عشاء الأمس.
            return (Prayer.Isha, now, Today.Fajr);
        }

        var (prayer, from) = current.Value;
        var to = prayer == Prayer.Isha
            ? Tomorrow?.Fajr ?? from.AddHours(9)
            : Today.NextAfter(from, includeSunrise: false)?.At ?? from.AddHours(4);

        return (prayer, from, to);
    }
}
