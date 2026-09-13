using System.ComponentModel;
using System.Windows;
using PrayTime.App.Infrastructure;

namespace PrayTime.App.Views;

public partial class MainWindow : Window
{
    private readonly AppShell _shell;

    public MainWindow(AppShell shell)
    {
        _shell = shell;
        InitializeComponent();

        DataContext = shell.Main;
        shell.Main.PropertyChanged += OnViewModelChanged;

        CopyrightLine.Text = $"{Infrastructure.AppInfo.Copyright}  •  مجاني للاستخدام";

        Loaded += (_, _) => UpdateProgressBar();
        SizeChanged += (_, _) => UpdateProgressBar();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(_shell.Main.Progress):
                UpdateProgressBar();
                break;
            case nameof(_shell.Main.IsPlaying):
                var playing = _shell.Main.IsPlaying;
                BtnStop.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
                StatusText.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
                break;
        }
    }

    /// <summary>
    /// عرض شريط التقدّم يُحسب يدويًا: الربط بنسبة من عرض الأب يحتاج محوّلًا
    /// متعدد الروابط، والحساب المباشر أوضح وأخف.
    /// </summary>
    private void UpdateProgressBar()
    {
        var track = ProgressTrack.ActualWidth;
        if (track <= 0) return;
        ProgressFill.Width = Math.Max(0, track * _shell.Main.Progress);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _shell.ShowSettings();

    private void OnCopyrightClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _shell.ShowAbout();

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnStopAudioClick(object sender, RoutedEventArgs e) => _shell.StopAudio();

    protected override void OnClosing(CancelEventArgs e)
    {
        // زر الإغلاق يخفي فقط. الإنهاء الحقيقي يمرّ عبر قائمة الصينية ورمز الحماية.
        Log.Info($"طلب إغلاق النافذة الرئيسية (HideOnClose={_shell.Settings.Behavior.HideOnClose})");

        if (_shell.Settings.Behavior.HideOnClose && !_shell.IsExiting)
        {
            e.Cancel = true;
            Hide();

            if (!_shell.Settings.Ui.HideToTrayHintShown)
            {
                _shell.Tray.ShowBalloon(
                    "مواقيت يعمل في الخلفية",
                    "التطبيق لم يُغلق — أيقونته في شريط المهام بجوار الساعة.");

                _shell.Settings.Ui.HideToTrayHintShown = true;
                try { _shell.Store.Save(_shell.Settings); }
                catch (Exception ex) { Log.Warn($"تعذّر حفظ حالة التلميح: {ex.Message}"); }
            }
        }

        base.OnClosing(e);
    }
}
