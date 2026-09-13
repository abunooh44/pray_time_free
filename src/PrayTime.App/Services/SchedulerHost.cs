using System.Windows.Threading;
using Microsoft.Win32;
using PrayTime.App.Infrastructure;
using PrayTime.Core.Scheduling;
using PrayTime.Core.Settings;

namespace PrayTime.App.Services;

/// <summary>
/// يربط محرّك الجدولة بالعالم الحقيقي: نبضة كل ثانية، وأحداث الطاقة والساعة، وتشغيل الصوت.
///
/// لا يوجد هنا أي مؤقّت طويل الأمد بقصد. مؤقّت مضبوط على «بعد ٧ ساعات حتى الفجر»
/// يتجمّد أثناء سبات الجهاز فينطلق في الضحى. النبضة الثانوية + إعادة المزامنة عند
/// الاستيقاظ هي الطريقة الوحيدة الموثوقة.
/// </summary>
public sealed class SchedulerHost : IDisposable
{
    /// <summary>مهلة بعد الاستيقاظ: ساعة النظام قد لا تكون تزامنت، وأجهزة الصوت لم تُعدّ تعدادها بعد.</summary>
    private static readonly TimeSpan ResumeDebounce = TimeSpan.FromSeconds(3);

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly PrayerScheduleEngine _engine;
    private readonly FiredLedger _ledger;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;

    private DateTime _lastLedgerSave = DateTime.MinValue;
    private bool _disposed;

    public SchedulerHost(AppSettings settings, SettingsStore store, FiredLedger ledger, Dispatcher dispatcher)
    {
        _settings = settings;
        _store = store;
        _ledger = ledger;
        _dispatcher = dispatcher;
        _engine = new PrayerScheduleEngine(() => _settings, ledger);

        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTick;
    }

    public PrayerScheduleEngine Engine => _engine;

    /// <summary>يُرفع على خيط الواجهة عند حلول حدث صوتي.</summary>
    public event Action<PrayerEvent>? EventDue;

    /// <summary>يُرفع كل ثانية بعد تحديث الحالة، لتحديث العدّادات في الواجهة.</summary>
    public event Action? Ticked;

    public void Start()
    {
        _engine.Start(DateTimeOffset.Now);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _timer.Start();
        Log.Info($"بدأت الجدولة. الحدث القادم: {DescribeNext()}");
    }

    /// <summary>يُستدعى بعد أي تعديل في الإعدادات لإعادة بناء الجدول فورًا.</summary>
    public void Reschedule()
    {
        _engine.Rebuild(DateTimeOffset.Now);
        Log.Info($"أُعيد بناء الجدول بعد تغيير الإعدادات. القادم: {DescribeNext()}");
        Ticked?.Invoke();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            var result = _engine.Tick(DateTimeOffset.Now);

            if (result.TimeJumpDetected)
            {
                Log.Warn($"قفزة زمنية {result.JumpMagnitude.TotalMinutes:F1} دقيقة " +
                         "(سبات أو تغيير ساعة). أُعيد بناء الجدول.");
            }

            foreach (var missed in result.Missed)
                Log.Warn($"حدث فائت بلا صوت: {missed.ArabicLabel} الذي كان في {missed.At:HH:mm}");

            foreach (var due in result.ToFire)
            {
                Log.Info($"إطلاق: {due.ArabicLabel} ({due.At:HH:mm})");
                EventDue?.Invoke(due);
            }

            if (result.HasWork) PersistLedger(force: true);
            else PersistLedger(force: false);

            Ticked?.Invoke();
        }
        catch (Exception ex)
        {
            // نبضة فاشلة يجب ألا توقف المؤقّت، وإلا صمت التطبيق إلى الأبد.
            Log.Error("خطأ في نبضة الجدولة", ex);
        }
    }

    private void PersistLedger(bool force)
    {
        if (!force && (DateTime.UtcNow - _lastLedgerSave) < TimeSpan.FromMinutes(5)) return;
        _lastLedgerSave = DateTime.UtcNow;
        try { _store.SaveLedger(_ledger); }
        catch (Exception ex) { Log.Warn($"تعذّر حفظ سجل الإطلاق: {ex.Message}"); }
    }

    // أحداث SystemEvents تصل على خيط نافذة مخفية خاص بها، لا على خيط الواجهة.
    // كل معالج هنا يجب أن يعبر إلى الـDispatcher وإلا انهار التطبيق عشوائيًا.

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;

        Log.Info("استيقاظ الجهاز من السبات — إعادة مزامنة بعد مهلة قصيرة.");
        _ = _dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(ResumeDebounce);
            if (_disposed) return;
            _engine.Rebuild(DateTimeOffset.Now);
            OnTick(this, EventArgs.Empty);
        });
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        Log.Warn("تغيّرت ساعة النظام — إعادة بناء الجدول.");
        _ = _dispatcher.InvokeAsync(() =>
        {
            if (_disposed) return;
            _engine.Rebuild(DateTimeOffset.Now);
            OnTick(this, EventArgs.Empty);
        });
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is not (SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)) return;

        _ = _dispatcher.InvokeAsync(() =>
        {
            if (_disposed) return;
            OnTick(this, EventArgs.Empty);
        });
    }

    private string DescribeNext() =>
        _engine.NextEvent is { } n ? $"{n.ArabicLabel} في {n.At:HH:mm}" : "لا شيء";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Tick -= OnTick;

        // SystemEvents يحتفظ بمراجع ثابتة قوية للمعالجات؛ عدم الإلغاء يسرّب الكائن
        // ويستدعي معالجات على تطبيق نصف مُغلق.
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        PersistLedger(force: true);
    }
}
