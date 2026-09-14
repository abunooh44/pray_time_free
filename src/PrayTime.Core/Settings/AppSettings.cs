using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using PrayTime.Core.Calculation;
using PrayTime.Core.Model;
using PrayTime.Core.Scheduling;
using PrayTime.Core.Security;

namespace PrayTime.Core.Settings;

/// <summary>أساس خفيف للإشعار بتغيّر الخصائص، حتى تتحدّث الواجهة فورًا مع تعديل الإعدادات.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AppSettings : Observable
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public LocationSettings Location { get; set; } = new();
    public CalculationSettings Calculation { get; set; } = new();
    public PrayerSettingsMap Prayers { get; set; } = PrayerSettingsMap.CreateDefault();
    public AudioSettings Audio { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public BehaviorSettings Behavior { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();

    public CalculationParameters ToCalculationParameters()
    {
        var offsets = new Dictionary<Prayer, int>();
        foreach (var p in PrayerNames.All) offsets[p] = Prayers[p].OffsetMinutes;

        return new CalculationParameters
        {
            MethodId = Calculation.MethodId,
            FajrAngle = Calculation.FajrAngle,
            IshaAngle = Calculation.IshaAngle,
            IshaIntervalMinutes = Calculation.UseIshaInterval ? Calculation.IshaIntervalMinutes : null,
            AsrJuristic = Calculation.AsrJuristic,
            HighLatitudeRule = Calculation.HighLatitudeRule,
            Rounding = Calculation.Rounding,
            Iterations = 3,
            OffsetsMinutes = offsets
        };
    }

    [JsonIgnore]
    public TimeZoneInfo TimeZone
    {
        get
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(Location.TimeZoneId); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
            catch (InvalidTimeZoneException) { return TimeZoneInfo.Local; }
        }
    }

    [JsonIgnore]
    public GeoLocation GeoLocation =>
        new(Location.Name, Location.Latitude, Location.Longitude, Location.ElevationMeters, Location.TimeZoneId);
}

public sealed class LocationSettings : Observable
{
    private string _name = GeoLocation.Bawshar.Name;
    private double _latitude = GeoLocation.Bawshar.Latitude;
    private double _longitude = GeoLocation.Bawshar.Longitude;
    private double _elevationMeters = GeoLocation.Bawshar.ElevationMeters;
    private string _timeZoneId = GeoLocation.Bawshar.TimeZoneId;

    public string Name { get => _name; set => Set(ref _name, value); }
    public double Latitude { get => _latitude; set => Set(ref _latitude, value); }
    public double Longitude { get => _longitude; set => Set(ref _longitude, value); }
    public double ElevationMeters { get => _elevationMeters; set => Set(ref _elevationMeters, value); }
    public string TimeZoneId { get => _timeZoneId; set => Set(ref _timeZoneId, value); }
}

public sealed class CalculationSettings : Observable
{
    private string _methodId = CalculationMethod.OmanAwqaf.Id;
    private double _fajrAngle = 18.0;
    private double _ishaAngle = 18.0;
    private bool _useIshaInterval;
    private int _ishaIntervalMinutes = 90;
    private AsrJuristic _asrJuristic = AsrJuristic.Shafi;
    private HighLatitudeRule _highLatitudeRule = HighLatitudeRule.None;
    private TimeRounding _rounding = TimeRounding.Nearest;

    public string MethodId { get => _methodId; set => Set(ref _methodId, value); }
    public double FajrAngle { get => _fajrAngle; set => Set(ref _fajrAngle, value); }
    public double IshaAngle { get => _ishaAngle; set => Set(ref _ishaAngle, value); }
    public bool UseIshaInterval { get => _useIshaInterval; set => Set(ref _useIshaInterval, value); }
    public int IshaIntervalMinutes { get => _ishaIntervalMinutes; set => Set(ref _ishaIntervalMinutes, value); }
    public AsrJuristic AsrJuristic { get => _asrJuristic; set => Set(ref _asrJuristic, value); }
    public HighLatitudeRule HighLatitudeRule { get => _highLatitudeRule; set => Set(ref _highLatitudeRule, value); }
    public TimeRounding Rounding { get => _rounding; set => Set(ref _rounding, value); }

    /// <summary>تطبيق قالب جاهز على الزوايا دون المساس بالإزاحات اليدوية.</summary>
    public void ApplyMethod(CalculationMethod method)
    {
        MethodId = method.Id;
        FajrAngle = method.FajrAngle;
        UseIshaInterval = method.IshaIntervalMinutes is not null;
        if (method.IshaIntervalMinutes is { } m) IshaIntervalMinutes = m;
        else IshaAngle = method.IshaAngle;
    }
}

public sealed class PrayerSettings : Observable
{
    private bool _adhanEnabled = true;
    private bool _iqamaEnabled = true;
    private int _iqamaDelayMinutes = 10;
    private int _offsetMinutes;
    private string _adhanSound = "adhan_makkah.mp3";
    private string _iqamaSound = "iqama.mp3";
    private double _volume = 0.85;

    public bool AdhanEnabled { get => _adhanEnabled; set => Set(ref _adhanEnabled, value); }
    public bool IqamaEnabled { get => _iqamaEnabled; set => Set(ref _iqamaEnabled, value); }
    public int IqamaDelayMinutes { get => _iqamaDelayMinutes; set => Set(ref _iqamaDelayMinutes, Math.Clamp(value, 0, 120)); }
    public int OffsetMinutes { get => _offsetMinutes; set => Set(ref _offsetMinutes, Math.Clamp(value, -60, 60)); }
    public string AdhanSound { get => _adhanSound; set => Set(ref _adhanSound, value); }
    public string IqamaSound { get => _iqamaSound; set => Set(ref _iqamaSound, value); }
    public double Volume { get => _volume; set => Set(ref _volume, Math.Clamp(value, 0, 1)); }
}

/// <summary>إعدادات كل صلاة على حدة. الشروق مُدرج للإزاحة والعرض فقط، بلا أذان.</summary>
public sealed class PrayerSettingsMap : Observable
{
    public PrayerSettings Fajr { get; set; } = new();
    public PrayerSettings Sunrise { get; set; } = new();
    public PrayerSettings Dhuhr { get; set; } = new();
    public PrayerSettings Asr { get; set; } = new();
    public PrayerSettings Maghrib { get; set; } = new();
    public PrayerSettings Isha { get; set; } = new();

    [JsonIgnore]
    public PrayerSettings this[Prayer p] => p switch
    {
        Prayer.Fajr => Fajr,
        Prayer.Sunrise => Sunrise,
        Prayer.Dhuhr => Dhuhr,
        Prayer.Asr => Asr,
        Prayer.Maghrib => Maghrib,
        _ => Isha
    };

    public static PrayerSettingsMap CreateDefault() => new()
    {
        Fajr = new PrayerSettings { IqamaDelayMinutes = 20, AdhanSound = "adhan_fajr.mp3", Volume = 0.7 },
        Sunrise = new PrayerSettings { AdhanEnabled = false, IqamaEnabled = false },
        Dhuhr = new PrayerSettings { IqamaDelayMinutes = 10 },
        Asr = new PrayerSettings { IqamaDelayMinutes = 10 },
        Maghrib = new PrayerSettings { IqamaDelayMinutes = 5 },
        Isha = new PrayerSettings { IqamaDelayMinutes = 10 }
    };
}

public sealed class AudioSettings : Observable
{
    private double _masterVolume = 0.85;
    private int _fadeInMs = 2000;
    private int _fadeOutMs = 1200;
    private bool _preventSleepDuringAdhan = true;

    public double MasterVolume { get => _masterVolume; set => Set(ref _masterVolume, Math.Clamp(value, 0, 1)); }
    public int FadeInMs { get => _fadeInMs; set => Set(ref _fadeInMs, Math.Clamp(value, 0, 15000)); }
    public int FadeOutMs { get => _fadeOutMs; set => Set(ref _fadeOutMs, Math.Clamp(value, 0, 15000)); }
    public bool PreventSleepDuringAdhan { get => _preventSleepDuringAdhan; set => Set(ref _preventSleepDuringAdhan, value); }
}

public sealed class UiSettings : Observable
{
    private string _theme = "Dark";
    private bool _useArabicIndicDigits = true;
    private int _hijriOffsetDays;
    // التشغيل اليدوي يُظهر النافذة؛ إقلاع ويندز يمرّر ‎--minimized فيبدأ مخفيًا.
    private bool _startMinimized;
    private bool _showAdhanPopup = true;
    private bool _hideToTrayHintShown;
    private bool _showMiniBar = true;
    private bool _pinMiniBarToTaskbar = true;
    private double? _miniBarLeft;
    private double? _miniBarTop;

    /// <summary>Dark أو Light.</summary>
    public string Theme { get => _theme; set => Set(ref _theme, value); }
    public bool UseArabicIndicDigits { get => _useArabicIndicDigits; set => Set(ref _useArabicIndicDigits, value); }
    public int HijriOffsetDays { get => _hijriOffsetDays; set => Set(ref _hijriOffsetDays, Math.Clamp(value, -3, 3)); }
    public bool StartMinimized { get => _startMinimized; set => Set(ref _startMinimized, value); }
    public bool ShowAdhanPopup { get => _showAdhanPopup; set => Set(ref _showAdhanPopup, value); }
    public bool HideToTrayHintShown { get => _hideToTrayHintShown; set => Set(ref _hideToTrayHintShown, value); }

    /// <summary>الشريط الصغير الملاصق لشريط المهام، يعرض العدّاد المتبقي.</summary>
    public bool ShowMiniBar { get => _showMiniBar; set => Set(ref _showMiniBar, value); }

    /// <summary>
    /// مثبّت داخل شريط المهام (لا يُسحب، ويتبع الشريط تلقائيًا) بدل نافذة حرّة عائمة.
    /// </summary>
    public bool PinMiniBarToTaskbar { get => _pinMiniBarToTaskbar; set => Set(ref _pinMiniBarToTaskbar, value); }

    /// <summary>موضع الشريط الحرّ بعد سحبه. يُتجاهل تمامًا في وضع التثبيت.</summary>
    public double? MiniBarLeft { get => _miniBarLeft; set => Set(ref _miniBarLeft, value); }
    public double? MiniBarTop { get => _miniBarTop; set => Set(ref _miniBarTop, value); }
}

public sealed class BehaviorSettings : Observable
{
    private bool _startWithWindows = true;
    private bool _hideOnClose = true;
    private bool _watchdogEnabled = true;
    private int _adhanToleranceMinutes = 5;
    private int _iqamaToleranceMinutes = 2;
    private int _startupGraceSeconds = 60;
    private PreIqamaAction _preIqamaAction = PreIqamaAction.None;
    private int _preIqamaMinutes = 5;
    private int _preIqamaWarningSeconds = 45;
    private int _lockDurationMinutes = 20;

    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public bool HideOnClose { get => _hideOnClose; set => Set(ref _hideOnClose, value); }

    /// <summary>
    /// مهمة في مجدول ويندز تتحقق دوريًا وتُعيد تشغيل التطبيق إن اختفى.
    /// مفتاح Run يعمل عند تسجيل الدخول فقط، فلا يغطّي موت التطبيق في منتصف الجلسة.
    /// </summary>
    public bool WatchdogEnabled { get => _watchdogEnabled; set => Set(ref _watchdogEnabled, value); }

    /// <summary>أقصى تأخير مقبول لتشغيل أذان فات وقته (مثلًا بعد استيقاظ الجهاز من السبات).</summary>
    public int AdhanToleranceMinutes { get => _adhanToleranceMinutes; set => Set(ref _adhanToleranceMinutes, Math.Clamp(value, 0, 60)); }

    public int IqamaToleranceMinutes { get => _iqamaToleranceMinutes; set => Set(ref _iqamaToleranceMinutes, Math.Clamp(value, 0, 30)); }

    /// <summary>مهلة صمت بعد إقلاع التطبيق، حتى لا ينطلق أذان فور تسجيل الدخول.</summary>
    public int StartupGraceSeconds { get => _startupGraceSeconds; set => Set(ref _startupGraceSeconds, Math.Clamp(value, 0, 600)); }

    /// <summary>ما يُفعل بالجهاز قبل الإقامة: لا شيء، أو إنامته، أو قفل الشاشة.</summary>
    public PreIqamaAction PreIqamaAction { get => _preIqamaAction; set => Set(ref _preIqamaAction, value); }

    /// <summary>كم دقيقة قبل الإقامة يُنفَّذ الإجراء.</summary>
    public int PreIqamaMinutes { get => _preIqamaMinutes; set => Set(ref _preIqamaMinutes, Math.Clamp(value, 0, 30)); }

    /// <summary>
    /// مهلة التحذير قبل التنفيذ. إنامة الجهاز بلا إنذار قد تُضيع عملًا غير محفوظ،
    /// فنعرض عدًّا تنازليًا يمكن إلغاؤه.
    /// </summary>
    public int PreIqamaWarningSeconds { get => _preIqamaWarningSeconds; set => Set(ref _preIqamaWarningSeconds, Math.Clamp(value, 5, 300)); }

    /// <summary>
    /// كم دقيقة تبقى الشاشة مقفولة بعد القفل.
    /// قفل ويندز وحده يُفتح فورًا؛ الحارس يعيد القفل خلال هذه المدة. صفر يعطّل الحارس.
    /// </summary>
    public int LockDurationMinutes { get => _lockDurationMinutes; set => Set(ref _lockDurationMinutes, Math.Clamp(value, 0, 60)); }
}

public sealed class SecuritySettings : Observable
{
    private bool _requirePinForExit = true;

    public bool RequirePinForExit { get => _requirePinForExit; set => Set(ref _requirePinForExit, value); }

    /// <summary>بيانات التحقق من الرمز. لا يُخزَّن الرمز نفسه أبدًا.</summary>
    public PinCredential? Pin { get; set; }

    [JsonIgnore]
    public bool IsPinConfigured => Pin is not null;
}
