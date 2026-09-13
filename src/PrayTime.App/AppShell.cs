using System.Windows;
using System.Windows.Threading;
using PrayTime.App.Infrastructure;
using PrayTime.App.Services;
using PrayTime.App.ViewModels;
using PrayTime.App.Views;
using PrayTime.Core.Model;
using PrayTime.Core.Scheduling;
using PrayTime.Core.Settings;

namespace PrayTime.App;

/// <summary>
/// نقطة التجميع: تملك الإعدادات والخدمات وتنسّق بينها.
/// مركزية مقصودة — التطبيق صغير بما يكفي ألا يحتاج حاوية حقن تبعيات.
/// </summary>
public sealed class AppShell : IDisposable
{
    private AdhanPopupWindow? _popup;
    private MiniBarWindow? _miniBar;
    private AboutWindow? _aboutWindow;
    private SettingsWindow? _settingsWindow;
    private MainWindow? _mainWindow;

    public AppShell(Dispatcher dispatcher)
    {
        AppPaths.EnsureCreated();

        Store = new SettingsStore(AppPaths.Roaming, AppPaths.Local);
        Settings = Store.Load();

        if (Store.LastLoadError is { } err) Log.Warn(err);

        Ledger = Store.LoadLedger();
        Audio = new AudioService();
        Pin = new PinService(Store, Settings);
        Scheduler = new SchedulerHost(Settings, Store, Ledger, dispatcher);
        Main = new MainViewModel(Settings, Scheduler);
        Tray = new TrayIconService(this);

        Scheduler.EventDue += OnEventDue;
        Scheduler.Ticked += OnTicked;
        Audio.PlaybackEnded += () => dispatcher.InvokeAsync(OnPlaybackEnded);
    }

    public SettingsStore Store { get; }
    public AppSettings Settings { get; }
    public FiredLedger Ledger { get; }
    public AudioService Audio { get; }
    public PinService Pin { get; }
    public SchedulerHost Scheduler { get; }
    public MainViewModel Main { get; }
    public TrayIconService Tray { get; }

    public bool IsExiting { get; private set; }

    public void Start()
    {
        ApplyTheme(Settings.Ui.Theme);
        Tray.Initialize();
        Scheduler.Start();
        SetMiniBarVisible(Settings.Ui.ShowMiniBar);
        SyncAutoStart();
        Main.Refresh();
    }

    // ================= الصوت =================

    private void OnEventDue(PrayerEvent e)
    {
        var error = Audio.Play(
            e.SoundFile,
            e.Volume,
            Settings.Audio.FadeInMs,
            Settings.Audio.PreventSleepDuringAdhan);

        if (error is not null)
        {
            Tray.ShowBalloon("تعذّر تشغيل الصوت", $"{e.ArabicLabel}: {error}");
            return;
        }

        Main.ShowPlaying(e);

        if (Settings.Ui.ShowAdhanPopup) ShowPopup(e);
        else Tray.ShowBalloon(e.ArabicLabel, $"حان الآن وقت {e.Prayer.Ar()}");
    }

    private void ShowPopup(PrayerEvent e)
    {
        _popup ??= new AdhanPopupWindow(this);
        _popup.Present(e);
    }

    public void StopAudio()
    {
        Audio.Stop(Settings.Audio.FadeOutMs);
        OnPlaybackEnded();
    }

    private void OnPlaybackEnded()
    {
        Main.ClearPlaying();
        _popup?.OnAudioStopped();
    }

    private void OnTicked()
    {
        Main.Refresh();
        Tray.UpdateTooltip(Main.TrayTooltip);
        _popup?.Tick();
        _miniBar?.Tick();
    }

    // ================= الشريط المصغّر =================

    public bool IsMiniBarVisible => _miniBar is { IsVisible: true };

    public void SetMiniBarVisible(bool visible)
    {
        Settings.Ui.ShowMiniBar = visible;

        if (visible)
        {
            _miniBar ??= new MiniBarWindow(this);
            _miniBar.Show();
            _miniBar.Tick();
        }
        else
        {
            _miniBar?.Hide();
        }

        try { Store.Save(Settings); }
        catch (Exception ex) { Log.Warn($"تعذّر حفظ حالة الشريط: {ex.Message}"); }
    }

    /// <summary>يتلف الشريط ويعيد إنشاءه ليُطبَّق الموضع الافتراضي من جديد.</summary>
    public void ResetMiniBar()
    {
        if (_miniBar is not null)
        {
            _miniBar.Hide();
            _miniBar = null;
        }
        SetMiniBarVisible(true);
    }

    // ================= النوافذ =================

    public void ShowMainWindow()
    {
        try
        {
            // نافذة WPF مُغلقة فعليًا لا يمكن إظهارها مجددًا (تُلقي استثناءً)،
            // فنتخلّص من المرجع عند الإغلاق الحقيقي ونبني نافذة جديدة.
            if (_mainWindow is null)
            {
                _mainWindow = new MainWindow(this);
                _mainWindow.Closed += (_, _) => _mainWindow = null;
            }

            _mainWindow.Show();

            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;

            Log.Info($"إظهار النافذة الرئيسية: IsVisible={_mainWindow.IsVisible} " +
                     $"State={_mainWindow.WindowState}");
        }
        catch (Exception ex)
        {
            Log.Error("تعذّر إظهار النافذة الرئيسية — سيُعاد إنشاؤها", ex);
            _mainWindow = null;
        }
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    public void ShowAbout()
    {
        if (_aboutWindow is { IsLoaded: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
    }

    /// <summary>يُستدعى بعد أي تعديل: يحفظ ويعيد بناء الجدول ويحدّث الواجهة.</summary>
    public void SaveAndReschedule()
    {
        try
        {
            Store.Save(Settings);
        }
        catch (Exception ex)
        {
            Log.Error("تعذّر حفظ الإعدادات", ex);
            MessageBox.Show($"تعذّر حفظ الإعدادات:\n{ex.Message}", "مواقيت",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        ApplyTheme(Settings.Ui.Theme);
        SyncAutoStart();
        SetMiniBarVisible(Settings.Ui.ShowMiniBar);
        Scheduler.Reschedule();
        Main.Refresh();
    }

    private void SyncAutoStart()
    {
        if (Settings.Behavior.StartWithWindows)
        {
            if (!AutoStartService.IsEnabled()) AutoStartService.SetEnabled(true);
            else AutoStartService.SelfHeal();
        }
        else if (AutoStartService.IsEnabled())
        {
            AutoStartService.SetEnabled(false);
        }
    }

    public void ApplyTheme(string theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        // نستبدل قاموس الألوان فقط ونُبقي الأنماط، لأن الأنماط تشير بـDynamicResource.
        var light = dictionaries.FirstOrDefault(
            d => d.Source?.OriginalString.Contains("PaletteLight", StringComparison.OrdinalIgnoreCase) == true);

        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase))
        {
            if (light is null)
            {
                dictionaries.Insert(1, new ResourceDictionary
                {
                    Source = new Uri("Themes/PaletteLight.xaml", UriKind.Relative)
                });
            }
        }
        else if (light is not null)
        {
            dictionaries.Remove(light);
        }
    }

    // ================= الخروج =================

    /// <summary>
    /// الخروج النهائي. محمي برمز إن كان مفعّلًا.
    /// <paramref name="bypassPin"/> يُستخدم عند إغلاق ويندز نفسه: حجب إيقاف التشغيل
    /// خلف نافذة رمز يُنتج شاشة «تطبيق يمنع إيقاف التشغيل» ثم قتلًا قسريًا.
    /// </summary>
    public void RequestExit(bool bypassPin = false)
    {
        if (!bypassPin && Settings.Security.RequirePinForExit)
        {
            if (!Pin.IsConfigured)
            {
                var created = PinDialog.PromptCreate(this);
                if (!created) return;
            }

            if (!PinDialog.PromptVerify(this)) return;
        }

        IsExiting = true;
        Log.Info("إنهاء التطبيق بطلب المستخدم.");
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        IsExiting = true;

        Scheduler.EventDue -= OnEventDue;
        Scheduler.Ticked -= OnTicked;

        Scheduler.Dispose();
        Audio.Dispose();
        Tray.Dispose();

        try { Store.SaveLedger(Ledger); } catch (Exception ex) { Log.Warn($"تعذّر حفظ السجل: {ex.Message}"); }
        try { Store.Save(Settings); } catch (Exception ex) { Log.Warn($"تعذّر حفظ الإعدادات: {ex.Message}"); }

        Log.Info("=== انتهى التشغيل ===");
    }
}
