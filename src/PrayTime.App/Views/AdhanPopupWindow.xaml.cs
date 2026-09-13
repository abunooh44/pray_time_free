using System.ComponentModel;
using System.Windows;
using PrayTime.App.ViewModels;
using PrayTime.Core.Model;
using PrayTime.Core.Scheduling;

namespace PrayTime.App.Views;

/// <summary>
/// بطاقة تظهر أعلى يسار الشاشة عند الأذان، ثم تتحوّل إلى عدّاد تنازلي للإقامة.
/// نافذة واحدة يُعاد استخدامها بدل إنشاء نافذة لكل حدث.
/// </summary>
public partial class AdhanPopupWindow : Window
{
    private readonly AppShell _shell;

    private PrayerEvent? _event;
    private DateTimeOffset? _iqamaAt;
    private DateTimeOffset _autoHideAt = DateTimeOffset.MaxValue;

    public AdhanPopupWindow(AppShell shell)
    {
        _shell = shell;
        InitializeComponent();
    }

    public void Present(PrayerEvent e)
    {
        _event = e;
        HeaderText.Text = e.ArabicLabel;
        BigText.Text = Numerals.Time(e.At);
        BtnStop.Visibility = Visibility.Visible;

        if (e.Kind == EventKind.Adhan)
        {
            SubText.Text = $"حان الآن وقت صلاة {e.Prayer.Ar()}";

            var delay = _shell.Settings.Prayers[e.Prayer];
            _iqamaAt = delay is { IqamaEnabled: true, IqamaDelayMinutes: > 0 }
                ? e.At.AddMinutes(delay.IqamaDelayMinutes)
                : null;

            // تبقى ظاهرة حتى الإقامة، أو عشر دقائق إن لم تكن هناك إقامة.
            _autoHideAt = _iqamaAt?.AddMinutes(1) ?? e.At.AddMinutes(10);
        }
        else
        {
            SubText.Text = $"أُقيمت صلاة {e.Prayer.Ar()}";
            _iqamaAt = null;
            CaptionText.Text = "";
            _autoHideAt = e.At.AddMinutes(2);
        }

        PositionTopLeading();
        Show();
        Tick();
    }

    /// <summary>يُستدعى كل ثانية من نبضة الجدولة.</summary>
    public void Tick()
    {
        if (!IsVisible) return;

        var now = DateTimeOffset.Now;

        if (_iqamaAt is { } iqama && now < iqama)
        {
            BigText.Text = Numerals.Countdown(iqama - now);
            CaptionText.Text = $"حتى إقامة {_event?.Prayer.Ar()}";
        }
        else if (_event is { Kind: EventKind.Adhan } && _iqamaAt is not null)
        {
            CaptionText.Text = "حان وقت الإقامة";
        }

        if (now >= _autoHideAt) Hide();
    }

    public void OnAudioStopped() => BtnStop.Visibility = Visibility.Collapsed;

    /// <summary>
    /// أعلى الحافة الابتدائية للشاشة. مع RTL «الابتداء» يمين، لكن الإشعارات في
    /// ويندز تظهر يسارًا أسفل؛ نضعها أعلى اليسار حتى لا تغطي أي شيء مهم.
    /// </summary>
    private void PositionTopLeading()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + 24;
        Top = area.Top + 24;
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        _shell.StopAudio();
        BtnStop.Visibility = Visibility.Collapsed;
    }

    private void OnDismissClick(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(CancelEventArgs e)
    {
        // النافذة مُعاد استخدامها طوال عمر التطبيق؛ لا نتلفها إلا عند الخروج.
        if (!_shell.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
