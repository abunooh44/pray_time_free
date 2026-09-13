using System.Collections.ObjectModel;
using PrayTime.App.Services;
using PrayTime.Core.Hijri;
using PrayTime.Core.Model;
using PrayTime.Core.Scheduling;
using PrayTime.Core.Settings;

namespace PrayTime.App.ViewModels;

public sealed class PrayerRowViewModel : Observable
{
    private string _timeText = "--:--";
    private bool _isCurrent;
    private bool _isNext;
    private bool _isMuted;

    public PrayerRowViewModel(Prayer prayer)
    {
        Prayer = prayer;
        Name = prayer.Ar();
    }

    public Prayer Prayer { get; }
    public string Name { get; }

    public string TimeText { get => _timeText; set => Set(ref _timeText, value); }
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    public bool IsNext { get => _isNext; set => Set(ref _isNext, value); }
    public bool IsMuted { get => _isMuted; set => Set(ref _isMuted, value); }

    /// <summary>الشروق ليس صلاة، فلا يقبل كتمًا ولا إقامة.</summary>
    public bool CanMute => Prayer != Prayer.Sunrise;
}

public sealed class MainViewModel : Observable
{
    private readonly AppSettings _settings;
    private readonly SchedulerHost _host;

    private string _clock = "--:--:--";
    private string _gregorian = "";
    private string _hijri = "";
    private string _weekday = "";
    private string _nextPrayerName = "";
    private string _nextPrayerTime = "--:--";
    private string _countdown = "--:--";
    private string _countdownCaption = "حتى الصلاة القادمة";
    private double _progress;
    private string _locationName = "";
    private string _status = "زر الإغلاق يُخفي التطبيق في شريط المهام — يواصل العمل بالخلفية";
    private bool _isPlaying;
    private string _playingLabel = "";
    private string _midnight = "--:--";
    private string _lastThird = "--:--";
    private string _miniCountdown = "--:--";
    private string _miniCaption = "";
    private bool _miniIsIqama;

    public MainViewModel(AppSettings settings, SchedulerHost host)
    {
        _settings = settings;
        _host = host;

        foreach (var p in PrayerNames.All) Rows.Add(new PrayerRowViewModel(p));
        Refresh();
    }

    public ObservableCollection<PrayerRowViewModel> Rows { get; } = [];

    public string Clock { get => _clock; private set => Set(ref _clock, value); }
    public string GregorianDate { get => _gregorian; private set => Set(ref _gregorian, value); }
    public string HijriDate { get => _hijri; private set => Set(ref _hijri, value); }
    public string Weekday { get => _weekday; private set => Set(ref _weekday, value); }
    public string NextPrayerName { get => _nextPrayerName; private set => Set(ref _nextPrayerName, value); }
    public string NextPrayerTime { get => _nextPrayerTime; private set => Set(ref _nextPrayerTime, value); }
    public string Countdown { get => _countdown; private set => Set(ref _countdown, value); }
    public string CountdownCaption { get => _countdownCaption; private set => Set(ref _countdownCaption, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string LocationName { get => _locationName; private set => Set(ref _locationName, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>منتصف الليل الشرعي: نقطة المنتصف بين المغرب وفجر الغد.</summary>
    public string IslamicMidnight { get => _midnight; private set => Set(ref _midnight, value); }

    /// <summary>بداية الثلث الأخير من الليل.</summary>
    public string LastThird { get => _lastThird; private set => Set(ref _lastThird, value); }

    // ===== الشريط الصغير الملاصق لشريط المهام =====

    /// <summary>العدّاد المعروض في الشريط الصغير: للإقامة إن كانت وشيكة، وإلا للأذان القادم.</summary>
    public string MiniCountdown { get => _miniCountdown; private set => Set(ref _miniCountdown, value); }

    public string MiniCaption { get => _miniCaption; private set => Set(ref _miniCaption, value); }

    /// <summary>يميّز حالة انتظار الإقامة بلون مختلف.</summary>
    public bool MiniIsIqama { get => _miniIsIqama; private set => Set(ref _miniIsIqama, value); }

    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }
    public string PlayingLabel { get => _playingLabel; set => Set(ref _playingLabel, value); }

    /// <summary>نص تلميح أيقونة الصينية.</summary>
    public string TrayTooltip =>
        string.IsNullOrEmpty(NextPrayerName)
            ? "مواقيت الصلاة"
            : $"{NextPrayerName} {NextPrayerTime} — بعد {Countdown}";

    public void Refresh()
    {
        var now = DateTimeOffset.Now;
        Numerals.UseArabicIndic = _settings.Ui.UseArabicIndicDigits;

        Clock = Numerals.TimeWithSeconds(now);
        Weekday = HijriDateService.WeekdayAr(now.DateTime);
        GregorianDate = Numerals.Convert(HijriDateService.FormatGregorian(now.DateTime));
        HijriDate = Numerals.Convert(HijriDateService.Format(now.DateTime, _settings.Ui.HijriOffsetDays));
        LocationName = _settings.Location.Name;

        var today = _host.Engine.Today;
        if (today is null) return;

        var next = _host.Engine.NextPrayer;
        var current = _host.Engine.CurrentWindow(now);

        foreach (var row in Rows)
        {
            row.TimeText = Numerals.Time(today[row.Prayer]);
            row.IsNext = next is { } n && n.Prayer == row.Prayer && n.At.Date == today[row.Prayer].Date;
            row.IsCurrent = current is { } c && c.Prayer == row.Prayer;
            row.IsMuted = row.CanMute && !_settings.Prayers[row.Prayer].AdhanEnabled;
        }

        if (next is { } np)
        {
            NextPrayerName = np.Prayer.Ar();
            NextPrayerTime = Numerals.Time(np.At);
            Countdown = Numerals.Countdown(np.At - now);
            CountdownCaption = $"حتى أذان {np.Prayer.Ar()}";
        }

        UpdateMiniBar(now);

        // ليل الليلة يمتد من مغرب اليوم إلى فجر الغد.
        if (_host.Engine.Tomorrow is { } tomorrow)
        {
            IslamicMidnight = Numerals.Time(today.IslamicMidnight(tomorrow.Fajr));
            LastThird = Numerals.Time(today.LastThird(tomorrow.Fajr));
        }

        if (current is { } win && win.To > win.From)
        {
            var elapsed = (now - win.From).TotalSeconds;
            var total = (win.To - win.From).TotalSeconds;
            Progress = Math.Clamp(elapsed / total, 0, 1);
        }
        else
        {
            Progress = 0;
        }

        Raise(nameof(TrayTooltip));
    }

    /// <summary>
    /// الحدث القادم غير المُطلق هو ما يهم المستخدم فعلًا: بعد الأذان مباشرة
    /// يصير الحدث القادم هو الإقامة، فيتحوّل الشريط تلقائيًا إلى عدّاد الإقامة.
    /// </summary>
    private void UpdateMiniBar(DateTimeOffset now)
    {
        var next = _host.Engine.NextEvent;

        if (next is not null)
        {
            MiniIsIqama = next.Kind == EventKind.Iqama;
            MiniCountdown = Numerals.Countdown(next.At - now);
            MiniCaption = MiniIsIqama
                ? $"حتى إقامة {next.Prayer.Ar()}"
                : $"حتى أذان {next.Prayer.Ar()}";
            return;
        }

        // كل الأحداث الصوتية مكتومة أو انتهت: نعرض الصلاة القادمة على أي حال.
        MiniIsIqama = false;
        if (_host.Engine.NextPrayer is { } np)
        {
            MiniCountdown = Numerals.Countdown(np.At - now);
            MiniCaption = $"حتى {np.Prayer.Ar()}";
        }
        else
        {
            MiniCountdown = "--:--";
            MiniCaption = "";
        }
    }

    public void SetStatus(string text) => Status = text;

    public void ShowPlaying(PrayerEvent e)
    {
        IsPlaying = true;
        PlayingLabel = e.ArabicLabel;
    }

    public void ClearPlaying()
    {
        IsPlaying = false;
        PlayingLabel = "";
    }
}
