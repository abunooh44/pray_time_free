using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PrayTime.App.Infrastructure;
using PrayTime.App.Services;
using PrayTime.App.ViewModels;
using PrayTime.Core.Calculation;
using PrayTime.Core.Model;
using PrayTime.Core.Scheduling;
using PrayTime.Core.Settings;

namespace PrayTime.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppShell _shell;
    private readonly AppSettings _settings;
    private readonly List<OffsetRowViewModel> _offsetRows = [];
    private readonly List<PrayerAudioRowViewModel> _audioRows = [];
    private bool _loading = true;

    public SettingsWindow(AppShell shell)
    {
        _shell = shell;
        _settings = shell.Settings;
        InitializeComponent();

        LoadFromSettings();
        _loading = false;

        VolumeSlider.ValueChanged += (_, _) => UpdateVolumeText();
        UpdatePreviewTimes();
    }

    // ================= التحميل =================

    private void LoadFromSettings()
    {
        MethodBox.ItemsSource = CalculationMethod.All;
        MethodBox.SelectedItem = CalculationMethod.ById(_settings.Calculation.MethodId);

        FajrAngleBox.Text = Fmt(_settings.Calculation.FajrAngle);
        IshaAngleBox.Text = _settings.Calculation.UseIshaInterval
            ? Fmt(_settings.Calculation.IshaIntervalMinutes)
            : Fmt(_settings.Calculation.IshaAngle);

        UpdateIshaLabel();

        AsrBox.SelectedIndex = _settings.Calculation.AsrJuristic == AsrJuristic.Hanafi ? 1 : 0;

        foreach (var p in PrayerNames.All)
            _offsetRows.Add(new OffsetRowViewModel(p, _settings.Prayers[p]));
        OffsetsList.ItemsSource = _offsetRows;

        foreach (var p in PrayerNames.Adhanable)
            _audioRows.Add(new PrayerAudioRowViewModel(p, _settings.Prayers[p]));
        PrayerAudioList.ItemsSource = _audioRows;

        VolumeSlider.Value = _settings.Audio.MasterVolume;
        UpdateVolumeText();

        LocationNameBox.Text = _settings.Location.Name;
        LatBox.Text = Fmt(_settings.Location.Latitude);
        LngBox.Text = Fmt(_settings.Location.Longitude);
        ElevBox.Text = Fmt(_settings.Location.ElevationMeters);

        ThemeBox.SelectedIndex = string.Equals(_settings.Ui.Theme, "Light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ArabicDigitsCheck.IsChecked = _settings.Ui.UseArabicIndicDigits;
        PopupCheck.IsChecked = _settings.Ui.ShowAdhanPopup;
        MiniBarCheck.IsChecked = _settings.Ui.ShowMiniBar;
        PinMiniBarCheck.IsChecked = _settings.Ui.PinMiniBarToTaskbar;
        UpdateHijriOffsetText();

        AutoStartCheck.IsChecked = _settings.Behavior.StartWithWindows;
        StartMinimizedCheck.IsChecked = _settings.Ui.StartMinimized;
        HideOnCloseCheck.IsChecked = _settings.Behavior.HideOnClose;
        KeepAwakeCheck.IsChecked = _settings.Audio.PreventSleepDuringAdhan;
        RequirePinCheck.IsChecked = _settings.Security.RequirePinForExit;
        ToleranceBox.Text = Fmt(_settings.Behavior.AdhanToleranceMinutes);

        UpdatePinStatus();
        UpdateAutoStartWarning();
        UpdateTimeZoneWarning();
    }

    private static string Fmt(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Fmt(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static bool TryNum(string text, out double value) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // ================= معاينة حيّة =================

    /// <summary>
    /// يحسب مواقيت اليوم بالإعدادات المعروضة حاليًا (لا المحفوظة)،
    /// فيرى المستخدم أثر تعديله قبل الحفظ.
    /// </summary>
    private void UpdatePreviewTimes()
    {
        if (_loading) return;

        try
        {
            var preview = CloneCurrentInputs();
            var day = ScheduleBuilder.BuildDay(DateOnly.FromDateTime(DateTime.Now), preview);

            foreach (var row in _offsetRows)
                row.ComputedTime = Numerals.Time(day[row.Prayer]);

            SaveHint.Text = "معاينة مواقيت اليوم بالإعدادات الحالية";
        }
        catch (Exception ex)
        {
            SaveHint.Text = $"تعذّرت المعاينة: {ex.Message}";
        }
    }

    /// <summary>
    /// نسخة مؤقتة من الإعدادات تحمل قيم الحقول المعروضة، لحساب المعاينة
    /// دون الكتابة على الإعدادات الحيّة.
    /// </summary>
    private AppSettings CloneCurrentInputs()
    {
        var s = new AppSettings();

        s.Location.Name = LocationNameBox.Text;
        if (TryNum(LatBox.Text, out var lat)) s.Location.Latitude = lat;
        if (TryNum(LngBox.Text, out var lng)) s.Location.Longitude = lng;
        if (TryNum(ElevBox.Text, out var elev)) s.Location.ElevationMeters = elev;
        s.Location.TimeZoneId = _settings.Location.TimeZoneId;

        var method = MethodBox.SelectedItem as CalculationMethod ?? CalculationMethod.OmanAwqaf;
        s.Calculation.MethodId = method.Id;
        s.Calculation.UseIshaInterval = method.IshaIntervalMinutes is not null;

        if (TryNum(FajrAngleBox.Text, out var fajr)) s.Calculation.FajrAngle = fajr;
        if (TryNum(IshaAngleBox.Text, out var isha))
        {
            if (s.Calculation.UseIshaInterval) s.Calculation.IshaIntervalMinutes = (int)isha;
            else s.Calculation.IshaAngle = isha;
        }

        s.Calculation.AsrJuristic = AsrBox.SelectedIndex == 1 ? AsrJuristic.Hanafi : AsrJuristic.Shafi;

        foreach (var row in _offsetRows)
            s.Prayers[row.Prayer].OffsetMinutes = row.Offset;

        return s;
    }

    // ================= المواقيت =================

    private void OnMethodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MethodBox.SelectedItem is not CalculationMethod method) return;

        MethodNote.Text = method.Note;
        FajrAngleBox.Text = Fmt(method.FajrAngle);
        IshaAngleBox.Text = method.IshaIntervalMinutes is { } m ? Fmt(m) : Fmt(method.IshaAngle);

        UpdateIshaLabel();
        UpdatePreviewTimes();
    }

    private void UpdateIshaLabel()
    {
        var method = MethodBox.SelectedItem as CalculationMethod ?? CalculationMethod.OmanAwqaf;
        MethodNote.Text = method.Note;
        IshaAngleLabel.Text = method.IshaIntervalMinutes is not null
            ? "العشاء بعد المغرب (دقيقة)"
            : "زاوية العشاء (درجة)";
    }

    private void OnOffsetUp(object sender, RoutedEventArgs e) => NudgeOffset(sender, +1);
    private void OnOffsetDown(object sender, RoutedEventArgs e) => NudgeOffset(sender, -1);

    private void NudgeOffset(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.Tag is not OffsetRowViewModel row) return;
        row.Offset = Math.Clamp(row.Offset + delta, -60, 60);
        UpdatePreviewTimes();
    }

    // ================= الصوت =================

    private void OnIqamaUp(object sender, RoutedEventArgs e) => NudgeIqama(sender, +1);
    private void OnIqamaDown(object sender, RoutedEventArgs e) => NudgeIqama(sender, -1);

    private void NudgeIqama(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.Tag is not PrayerAudioRowViewModel row) return;
        row.IqamaDelay = Math.Clamp(row.IqamaDelay + delta, 0, 120);
    }

    private void OnPreviewAdhan(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PrayerAudioRowViewModel row) return;
        Preview(row.AdhanSound);
    }

    private void OnPreviewIqama(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PrayerAudioRowViewModel row) return;
        Preview(row.IqamaSound);
    }

    private void Preview(string sound)
    {
        var error = _shell.Audio.Play(sound, VolumeSlider.Value, 400, keepAwake: false);
        SaveHint.Text = error ?? $"تجربة: {Path.GetFileName(sound)}";
    }

    private void OnStopPreview(object sender, RoutedEventArgs e) => _shell.StopAudio();

    private void OnBrowseAdhan(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PrayerAudioRowViewModel row) return;

        var dialog = new OpenFileDialog
        {
            Title = $"اختيار صوت أذان {row.Name}",
            Filter = "ملفات صوتية|*.mp3;*.wav|كل الملفات|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true) return;

        row.AdhanSound = dialog.FileName;
        row.RefreshChoices();
        SaveHint.Text = $"تم اختيار: {Path.GetFileName(dialog.FileName)}";
    }

    private void UpdateVolumeText() =>
        VolumeText.Text = Numerals.Convert($"{VolumeSlider.Value * 100:0}%");

    private void OnOpenAudioFolder(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.UserAudio);

    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.Logs);

    private void OnShowAbout(object sender, RoutedEventArgs e) => _shell.ShowAbout();

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"تعذّر فتح المجلد {path}: {ex.Message}");
        }
    }

    // ================= الموقع =================

    private void OnResetLocation(object sender, RoutedEventArgs e)
    {
        var b = GeoLocation.Bawshar;
        LocationNameBox.Text = b.Name;
        LatBox.Text = Fmt(b.Latitude);
        LngBox.Text = Fmt(b.Longitude);
        ElevBox.Text = Fmt(b.ElevationMeters);
        _settings.Location.TimeZoneId = b.TimeZoneId;

        UpdatePreviewTimes();
        UpdateTimeZoneWarning();
    }

    /// <summary>
    /// إن كانت ساعة ويندز على منطقة زمنية غير منطقة الموقع، تظهر المواقيت
    /// «صحيحة» لكنها تنطلق في وقت خاطئ على الشاشة. ننبّه صراحةً بدل الصمت.
    /// </summary>
    private void UpdateTimeZoneWarning()
    {
        var appZone = _settings.TimeZone;
        var systemZone = TimeZoneInfo.Local;
        var now = DateTime.Now;

        if (appZone.GetUtcOffset(now) == systemZone.GetUtcOffset(now))
        {
            TimeZoneWarning.Visibility = Visibility.Collapsed;
            return;
        }

        TimeZoneWarning.Text =
            $"تنبيه: منطقة الحساب ({appZone.Id}) تختلف عن منطقة ويندز ({systemZone.Id}). " +
            "المواقيت المعروضة بتوقيت الموقع، وقد لا تطابق ساعة جهازك.";
        TimeZoneWarning.Visibility = Visibility.Visible;
    }

    // ================= عام =================

    /// <summary>يمسح الموضع المحفوظ فيعود الشريط إلى شريط المهام — مخرج إن اختفى خارج الشاشة.</summary>
    private void OnResetMiniBar(object sender, RoutedEventArgs e)
    {
        _settings.Ui.MiniBarLeft = null;
        _settings.Ui.MiniBarTop = null;
        _settings.Ui.ShowMiniBar = true;
        _settings.Ui.PinMiniBarToTaskbar = true;
        MiniBarCheck.IsChecked = true;
        PinMiniBarCheck.IsChecked = true;

        _shell.ResetMiniBar();
        SaveHint.Text = "أُعيد الشريط إلى موضعه الافتراضي.";
    }

    private void OnHijriUp(object sender, RoutedEventArgs e) => NudgeHijri(+1);
    private void OnHijriDown(object sender, RoutedEventArgs e) => NudgeHijri(-1);

    private void NudgeHijri(int delta)
    {
        _settings.Ui.HijriOffsetDays = Math.Clamp(_settings.Ui.HijriOffsetDays + delta, -3, 3);
        UpdateHijriOffsetText();
    }

    private void UpdateHijriOffsetText() =>
        HijriOffsetText.Text = Numerals.Signed(_settings.Ui.HijriOffsetDays);

    private void UpdateAutoStartWarning()
    {
        if (AutoStartService.IsDisabledByWindows())
        {
            AutoStartWarning.Text =
                "التشغيل التلقائي معطّل من إعدادات ويندز (الإعدادات ← التطبيقات ← تطبيقات بدء التشغيل). " +
                "فعّله من هناك ليعمل.";
            AutoStartWarning.Visibility = Visibility.Visible;
        }
        else
        {
            AutoStartWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdatePinStatus() =>
        PinStatus.Text = _shell.Pin.IsConfigured
            ? "الرمز مضبوط. إن نسيته، احذف ملف settings.json من مجلد بيانات التطبيق لإعادة الضبط."
            : "لا يوجد رمز بعد. سيُطلب منك تعيينه عند أول محاولة إنهاء.";

    private void OnSetPin(object sender, RoutedEventArgs e)
    {
        if (PinDialog.PromptCreate(_shell)) UpdatePinStatus();
    }

    private void OnClearPin(object sender, RoutedEventArgs e)
    {
        if (!_shell.Pin.IsConfigured) return;

        // إزالة الرمز يجب أن تتطلب معرفته، وإلا صار حاجزًا بلا معنى.
        if (!PinDialog.PromptVerify(_shell)) return;

        _shell.Pin.ClearPin();
        UpdatePinStatus();
    }

    // ================= الحفظ =================

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!ValidateAndApply()) return;

        _shell.SaveAndReschedule();
        UpdatePreviewTimes();
        UpdateAutoStartWarning();
        UpdateTimeZoneWarning();
        SaveHint.Text = $"حُفظت الإعدادات في {Numerals.TimeWithSeconds(DateTimeOffset.Now)}";
    }

    private bool ValidateAndApply()
    {
        if (!TryNum(LatBox.Text, out var lat) || lat is < -90 or > 90)
            return Fail("خط العرض يجب أن يكون بين ‎-90 و‎+90.");

        if (!TryNum(LngBox.Text, out var lng) || lng is < -180 or > 180)
            return Fail("خط الطول يجب أن يكون بين ‎-180 و‎+180.");

        if (!TryNum(ElevBox.Text, out var elev) || elev is < -500 or > 9000)
            return Fail("الارتفاع يجب أن يكون بين ‎-500 و‎9000 متر.");

        if (!TryNum(FajrAngleBox.Text, out var fajrAngle) || fajrAngle is < 5 or > 25)
            return Fail("زاوية الفجر يجب أن تكون بين ٥ و٢٥ درجة.");

        if (!TryNum(IshaAngleBox.Text, out var ishaValue))
            return Fail("قيمة العشاء غير صالحة.");

        var method = MethodBox.SelectedItem as CalculationMethod ?? CalculationMethod.OmanAwqaf;
        var useInterval = method.IshaIntervalMinutes is not null;

        if (useInterval && ishaValue is < 30 or > 180)
            return Fail("الفاصل بعد المغرب يجب أن يكون بين ٣٠ و١٨٠ دقيقة.");

        if (!useInterval && ishaValue is < 5 or > 25)
            return Fail("زاوية العشاء يجب أن تكون بين ٥ و٢٥ درجة.");

        if (!TryNum(ToleranceBox.Text, out var tolerance) || tolerance is < 0 or > 60)
            return Fail("أقصى تأخير يجب أن يكون بين ٠ و٦٠ دقيقة.");

        if (string.IsNullOrWhiteSpace(LocationNameBox.Text))
            return Fail("اسم الموقع لا يمكن أن يكون فارغًا.");

        // كل شيء صالح: ننقل القيم إلى الإعدادات الحيّة.
        _settings.Location.Name = LocationNameBox.Text.Trim();
        _settings.Location.Latitude = lat;
        _settings.Location.Longitude = lng;
        _settings.Location.ElevationMeters = elev;

        _settings.Calculation.MethodId = method.Id;
        _settings.Calculation.FajrAngle = fajrAngle;
        _settings.Calculation.UseIshaInterval = useInterval;
        if (useInterval) _settings.Calculation.IshaIntervalMinutes = (int)ishaValue;
        else _settings.Calculation.IshaAngle = ishaValue;

        _settings.Calculation.AsrJuristic = AsrBox.SelectedIndex == 1 ? AsrJuristic.Hanafi : AsrJuristic.Shafi;

        _settings.Audio.MasterVolume = VolumeSlider.Value;
        _settings.Audio.PreventSleepDuringAdhan = KeepAwakeCheck.IsChecked == true;

        _settings.Ui.Theme = ThemeBox.SelectedIndex == 1 ? "Light" : "Dark";
        _settings.Ui.UseArabicIndicDigits = ArabicDigitsCheck.IsChecked == true;
        _settings.Ui.ShowAdhanPopup = PopupCheck.IsChecked == true;
        _settings.Ui.ShowMiniBar = MiniBarCheck.IsChecked == true;
        _settings.Ui.PinMiniBarToTaskbar = PinMiniBarCheck.IsChecked == true;
        _settings.Ui.StartMinimized = StartMinimizedCheck.IsChecked == true;

        _settings.Behavior.StartWithWindows = AutoStartCheck.IsChecked == true;
        _settings.Behavior.HideOnClose = HideOnCloseCheck.IsChecked == true;
        _settings.Behavior.AdhanToleranceMinutes = (int)tolerance;

        _settings.Security.RequirePinForExit = RequirePinCheck.IsChecked == true;

        return true;
    }

    private bool Fail(string message)
    {
        SaveHint.Text = message;
        MessageBox.Show(this, message, "قيمة غير صالحة", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
