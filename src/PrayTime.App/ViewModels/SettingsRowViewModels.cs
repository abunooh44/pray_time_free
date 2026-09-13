using PrayTime.App.Services;
using PrayTime.Core.Model;
using PrayTime.Core.Settings;

namespace PrayTime.App.ViewModels;

/// <summary>صف تعديل دقائق صلاة واحدة، مع معاينة حيّة للوقت الناتج.</summary>
public sealed class OffsetRowViewModel : Observable
{
    private readonly PrayerSettings _config;
    private string _computedTime = "--:--";

    public OffsetRowViewModel(Prayer prayer, PrayerSettings config)
    {
        Prayer = prayer;
        Name = prayer.Ar();
        _config = config;
    }

    public Prayer Prayer { get; }
    public string Name { get; }

    public int Offset
    {
        get => _config.OffsetMinutes;
        set
        {
            if (_config.OffsetMinutes == value) return;
            _config.OffsetMinutes = value;
            Raise(nameof(Offset));
            Raise(nameof(OffsetText));
            Raise(nameof(OffsetLabel));
        }
    }

    public string OffsetText => Numerals.Signed(Offset);

    public string OffsetLabel => Offset == 0
        ? "بلا تعديل"
        : Offset > 0
            ? $"مؤخّر {Numerals.Int(Offset)} دقيقة"
            : $"مقدّم {Numerals.Int(-Offset)} دقيقة";

    public string ComputedTime
    {
        get => _computedTime;
        set => Set(ref _computedTime, value);
    }
}

/// <summary>صف إعدادات الصوت والإقامة لصلاة واحدة.</summary>
public sealed class PrayerAudioRowViewModel : Observable
{
    private readonly PrayerSettings _config;

    public PrayerAudioRowViewModel(Prayer prayer, PrayerSettings config)
    {
        Prayer = prayer;
        Name = prayer.Ar();
        _config = config;

        AdhanChoices = BuildChoices("adhan", config.AdhanSound);
        IqamaChoices = BuildChoices("iqama", config.IqamaSound);
    }

    public Prayer Prayer { get; }
    public string Name { get; }

    public IReadOnlyList<string> AdhanChoices { get; private set; }
    public IReadOnlyList<string> IqamaChoices { get; private set; }

    public bool AdhanEnabled
    {
        get => _config.AdhanEnabled;
        set { _config.AdhanEnabled = value; Raise(); }
    }

    public bool IqamaEnabled
    {
        get => _config.IqamaEnabled;
        set { _config.IqamaEnabled = value; Raise(); }
    }

    public string AdhanSound
    {
        get => _config.AdhanSound;
        set
        {
            if (string.IsNullOrEmpty(value) || _config.AdhanSound == value) return;
            _config.AdhanSound = value;
            Raise();
        }
    }

    public string IqamaSound
    {
        get => _config.IqamaSound;
        set
        {
            if (string.IsNullOrEmpty(value) || _config.IqamaSound == value) return;
            _config.IqamaSound = value;
            Raise();
        }
    }

    public int IqamaDelay
    {
        get => _config.IqamaDelayMinutes;
        set
        {
            if (_config.IqamaDelayMinutes == value) return;
            _config.IqamaDelayMinutes = value;
            Raise(nameof(IqamaDelay));
            Raise(nameof(IqamaText));
        }
    }

    public string IqamaText => Numerals.Int(IqamaDelay);

    /// <summary>
    /// يضيف الملف المختار حاليًا إلى القائمة حتى لو كان مسارًا خارجيًا،
    /// وإلا اختفى اختيار المستخدم من القائمة المنسدلة.
    /// </summary>
    private static List<string> BuildChoices(string prefix, string current)
    {
        var list = AudioService.AvailableSounds(prefix).ToList();
        if (!string.IsNullOrEmpty(current) && !list.Contains(current, StringComparer.OrdinalIgnoreCase))
            list.Insert(0, current);
        return list;
    }

    public void RefreshChoices()
    {
        AdhanChoices = BuildChoices("adhan", _config.AdhanSound);
        IqamaChoices = BuildChoices("iqama", _config.IqamaSound);
        Raise(nameof(AdhanChoices));
        Raise(nameof(IqamaChoices));
        Raise(nameof(AdhanSound));
        Raise(nameof(IqamaSound));
    }
}
