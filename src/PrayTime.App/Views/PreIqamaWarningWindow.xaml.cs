using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using PrayTime.App.Infrastructure;
using PrayTime.App.Services;
using PrayTime.App.ViewModels;
using PrayTime.Core.Model;
using PrayTime.Core.Scheduling;

namespace PrayTime.App.Views;

/// <summary>
/// تحذير قبل إنامة الجهاز أو قفله استعدادًا للإقامة.
///
/// العدّ التنازلي والزر «تأجيل» ليسا ترفًا: إنامة جهاز بلا إنذار قد تُضيّع
/// عملًا غير محفوظ أو تقطع مكالمة أو رفع ملف. الميزة هدفها تفريغ المستخدم
/// للصلاة، لا الإضرار به.
/// </summary>
public partial class PreIqamaWarningWindow : Window
{
    private readonly AppShell _shell;
    private readonly DispatcherTimer _timer;

    private PreIqamaAction _action = PreIqamaAction.None;
    private DateTimeOffset _deadline;
    private bool _executed;

    public PreIqamaWarningWindow(AppShell shell)
    {
        _shell = shell;
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Send, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += OnTick;
    }

    public void Present(PrayerEvent e, PreIqamaAction action, int warningSeconds)
    {
        _action = action;
        _executed = false;
        _deadline = DateTimeOffset.Now.AddSeconds(warningSeconds);

        HeaderText.Text = $"اقتربت إقامة {e.Prayer.Ar()}";
        DetailText.Text = $"سيقوم التطبيق بـ«{PowerActionService.ArabicName(action)}» بعد قليل.";

        UpdateCountdown();

        Show();
        Activate();
        Topmost = true;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        UpdateCountdown();
        if (DateTimeOffset.Now >= _deadline) Execute();
    }

    private void UpdateCountdown()
    {
        var remaining = _deadline - DateTimeOffset.Now;
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        CountdownText.Text = Numerals.Convert(seconds.ToString("00"));
    }

    private void Execute()
    {
        if (_executed) return;
        _executed = true;

        _timer.Stop();
        Hide();

        // نوقف الأذان أولًا: تشغيله يرفع طلب «امنع النوم» الذي يُبطل الإنامة.
        _shell.StopAudio();

        var error = PowerActionService.Execute(_action);
        if (error is not null)
        {
            Log.Warn(error);
            _shell.Tray.ShowBalloon("تعذّر تنفيذ الإجراء", error);
            return;
        }

        // قفل ويندز وحده يُفتح بعد ثانية؛ الحارس يُبقيه مقفولًا مدة الصلاة.
        if (_action == PreIqamaAction.Lock)
            _shell.LockGuard.Begin(_shell.Settings.Behavior.LockDurationMinutes);
    }

    private void OnNowClick(object sender, RoutedEventArgs e) => Execute();

    private void OnPostponeClick(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _executed = true;
        Hide();
        Log.Info($"أجّل المستخدم إجراء ما قبل الإقامة ({_action}).");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // النافذة مُعاد استخدامها؛ إغلاقها يعني التأجيل لا الإنهاء.
        if (!_shell.IsExiting)
        {
            e.Cancel = true;
            _timer.Stop();
            _executed = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
