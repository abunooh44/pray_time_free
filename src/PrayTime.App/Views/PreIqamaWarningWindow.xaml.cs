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
/// تحذير إلزامي قبل إنامة الجهاز أو قفله استعدادًا للإقامة.
///
/// لا يوجد زر تأجيل عن قصد: زر التأجيل يُلغي الميزة عمليًا لأن المستخدم
/// سيضغطه كل مرة. العدّ التنازلي يبقى — لا كمهرب بل كمهلة لحفظ العمل.
///
/// المخرج الحقيقي قبل الحدث لا أثناءه: تعطيل الميزة من الإعدادات،
/// وحدّ محاولات إعادة القفل بعده.
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
            _shell.LockGuard.Begin(
                _shell.Settings.Behavior.LockDurationMinutes,
                _shell.Settings.Behavior.LockRelockLimit);
    }

    private void OnNowClick(object sender, RoutedEventArgs e) => Execute();

    /// <summary>
    /// النافذة لا تُغلق قبل تنفيذ الإجراء: Alt+F4 أو Esc أو زر النظام
    /// كلها كانت ستصير زر «تأجيل» مقنّعًا.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_shell.IsExiting && !_executed)
        {
            e.Cancel = true;
            Log.Info("محاولة إغلاق نافذة ما قبل الإقامة — مرفوضة، الإجراء إلزامي.");
        }
        else if (!_shell.IsExiting)
        {
            // نُعيد استخدام النافذة، فنخفيها بدل إتلافها.
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
