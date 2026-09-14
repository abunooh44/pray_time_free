using Microsoft.Win32;
using System.Windows.Threading;
using PrayTime.App.Infrastructure;

namespace PrayTime.App.Services;

/// <summary>
/// يُبقي الشاشة مقفولة طوال مدة الصلاة.
///
/// قفل ويندز وحده يُفتح بعد ثانية واحدة، فلا يحقق الغرض. هذا الحارس يعيد القفل
/// إن فُتح قبل انتهاء المدة، ثم يتوقف تمامًا عند انتهائها.
///
/// مخرج الطوارئ مقصود: بعد عدد محدود من محاولات الفتح يتوقّف الحارس ويسجّل ذلك.
/// أداة للتفرّغ للصلاة لا سجن؛ من احتاج جهازه لأمر طارئ يجب أن يصل إليه.
/// </summary>
public sealed class PrayerLockGuard : IDisposable
{
    /// <summary>مهلة قصيرة قبل إعادة القفل: القفل الفوري أثناء تسجيل الدخول يفشل أحيانًا.</summary>
    private static readonly TimeSpan RelockDelay = TimeSpan.FromSeconds(2);

    private readonly Dispatcher _dispatcher;

    private DateTimeOffset? _lockUntil;
    private int _relockCount;
    private int _maxRelocks = 5;
    private bool _disposed;

    public PrayerLockGuard(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public bool IsActive => _lockUntil is { } until && DateTimeOffset.Now < until;

    public TimeSpan Remaining =>
        _lockUntil is { } until && DateTimeOffset.Now < until ? until - DateTimeOffset.Now : TimeSpan.Zero;

    /// <summary>يبدأ فترة الحماية. تُستدعى مباشرة بعد نجاح قفل الشاشة.</summary>
    public void Begin(int durationMinutes, int maxRelocks = 5)
    {
        if (durationMinutes <= 0)
        {
            _lockUntil = null;
            return;
        }

        _lockUntil = DateTimeOffset.Now.AddMinutes(durationMinutes);
        _relockCount = 0;
        _maxRelocks = Math.Max(1, maxRelocks);

        Log.Info($"حارس القفل: الشاشة تبقى مقفولة {durationMinutes} دقيقة (حتى {_lockUntil:HH:mm}).");
    }

    /// <summary>إلغاء يدوي للحماية.</summary>
    public void Cancel()
    {
        if (_lockUntil is null) return;
        _lockUntil = null;
        Log.Info("حارس القفل: أُلغيت الحماية.");
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is not (SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)) return;
        if (!IsActive) return;

        // أحداث SystemEvents تصل على خيط خاص بها، لا على خيط الواجهة.
        _ = _dispatcher.InvokeAsync(async () =>
        {
            if (_disposed || !IsActive) return;

            if (_relockCount >= _maxRelocks)
            {
                Log.Warn($"حارس القفل: توقّف بعد {_maxRelocks} محاولات فتح — احترامًا لحاجة المستخدم لجهازه.");
                _lockUntil = null;
                return;
            }

            await Task.Delay(RelockDelay);
            if (_disposed || !IsActive) return;

            _relockCount++;
            Log.Info($"حارس القفل: إعادة قفل ({_relockCount}/{_maxRelocks})، " +
                     $"يتبقّى {Remaining.TotalMinutes:F1} دقيقة.");

            PowerActionService.Execute(Core.Scheduling.PreIqamaAction.Lock);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lockUntil = null;

        // SystemEvents يحتفظ بمرجع ثابت قوي؛ عدم الإلغاء يسرّب الكائن.
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}
