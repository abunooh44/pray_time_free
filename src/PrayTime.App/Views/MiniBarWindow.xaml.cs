using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PrayTime.App.Infrastructure;
using PrayTime.App.Interop;

namespace PrayTime.App.Views;

/// <summary>
/// شريط صغير يعرض الوقت المتبقي للإقامة (أو للأذان القادم) داخل شريط المهام.
///
/// حقيقة تقنية يجب توثيقها: ويندز ١١ لا يسمح لأي تطبيق خارجي بإضافة عنصر
/// داخل شريط المهام. واجهة deskbands القديمة أُلغيت، وعنصر الطقس جزء من ويندز نفسه.
/// فالمتاح الوحيد هو نافذة دائمة الظهور تُرسى فوق منطقة الشريط وتتبع تغيّراته،
/// وهو ما تفعله كل التطبيقات التي تبدو «داخل الشريط».
///
/// المرساة الافتراضية هي يسار منطقة الساعة: تلك المساحة فارغة في ويندز ١١
/// (الأيقونات موسّطة، والطقس أقصى اليسار)، فلا نغطي أي عنصر للنظام.
/// </summary>
public partial class MiniBarWindow : Window
{
    private static readonly SolidColorBrush IqamaAccent =
        new(Color.FromRgb(0x4F, 0xBF, 0xA8));

    /// <summary>مسافة أمان بين الشريط ومنطقة الساعة، بوحدات مستقلة عن كثافة البكسل.</summary>
    private const double TrayGap = 12;

    private readonly AppShell _shell;
    private DateTime _lastAnchor = DateTime.MinValue;
    private Point _dragStart;
    private bool _dragged;
    private bool? _lastLightTaskbar;

    public MiniBarWindow(AppShell shell)
    {
        _shell = shell;
        InitializeComponent();

        Shell.MouseLeftButtonDown += OnMouseDown;
        Shell.MouseLeftButtonUp += OnMouseUp;
        Shell.MouseRightButtonUp += OnRightClick;

        // القياس لا يكتمل قبل أول رسم؛ الرسو قبل ذلك يستخدم ارتفاعًا صفريًا
        // فيسقط الشريط تحت حافة الشاشة. هذا سبب ظهوره خارج الشريط سابقًا.
        ContentRendered += (_, _) => Anchor(force: true);
        SizeChanged += (_, _) => Anchor(force: true);
        SourceInitialized += (_, _) => MakeNonActivating();
    }

    /// <summary>
    /// WS_EX_NOACTIVATE يمنع النافذة من أخذ التركيز عند النقر.
    /// بدونه: النقر يُنشّط نافذتنا، فيُعيد ويندز رفع شريط المهام فوقها، فتختفي خلفه.
    /// WS_EX_TOOLWINDOW يُبعدها عن Alt+Tab وعن شريط المهام.
    /// </summary>
    private void MakeNonActivating()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var style = (long)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
            style |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;

            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(style));
        }
        catch (Exception ex)
        {
            Log.Warn($"تعذّر ضبط أنماط الشريط: {ex.Message}");
        }
    }

    /// <summary>يُستدعى كل ثانية من نبضة الجدولة.</summary>
    public void Tick()
    {
        if (!IsVisible) return;

        var vm = _shell.Main;
        CountdownText.Text = vm.MiniCountdown;
        CaptionText.Text = vm.MiniCaption;
        ApplyTaskbarTheme();
        if (vm.MiniIsIqama) CountdownText.Foreground = IqamaAccent;

        // شريط المهام نافذة دائمة الظهور أيضًا؛ أي تنشيط له يرفعه فوقنا.
        // التثبيت كل ثانية يُبقي الشريط ظاهرًا دائمًا، والاستدعاء رخيص جدًا.
        AssertTopmost(force: false);

        // إعادة الرسو أقل تكرارًا: تغيّر الدقة أو نقل الشريط أو ظهور أيقونات جديدة.
        if ((DateTime.UtcNow - _lastAnchor).TotalSeconds > 3) Anchor(force: false);
    }

    /// <summary>
    /// لون النص يتبع سمة شريط المهام لا سمة التطبيق: أبيض على شريط داكن،
    /// وأسود على شريط فاتح. بدون هذا يختفي النص تمامًا على السمة الفاتحة.
    /// </summary>
    private void ApplyTaskbarTheme()
    {
        var lightTaskbar = IsSystemLightTheme();
        if (lightTaskbar == _lastLightTaskbar) return;

        _lastLightTaskbar = lightTaskbar;
        var foreground = lightTaskbar ? Brushes.Black : Brushes.White;

        Shell.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, foreground);
        CountdownText.Foreground = foreground;
        CaptionText.Foreground = foreground;
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch (Exception)
        {
            return false; // الافتراضي داكن، وهو الأشيع
        }
    }

    // ================= الرسو =================

    private void Anchor(bool force)
    {
        _lastAnchor = DateTime.UtcNow;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        if (_shell.Settings.Ui.PinMiniBarToTaskbar) PositionOnTaskbar();
        else RestoreFreePosition();

        AssertTopmost(force);
    }

    private void PositionOnTaskbar()
    {
        var dpi = VisualTreeHelper.GetDpi(this);

        if (NativeMethods.GetTaskbar() is not { } tb ||
            tb.Edge != NativeMethods.AppBarEdge.Bottom)
        {
            // شريط جانبي أو علوي أو غير معروف: نكتفي بزاوية منطقة العمل.
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 16;
            Top = work.Bottom - ActualHeight - 8;
            return;
        }

        // واجهات ويندز تعطي بكسلات فعلية؛ WPF يضع النوافذ بوحدات مستقلة عن الكثافة.
        var barTop = tb.Rect.Top / dpi.DpiScaleY;
        var barHeight = tb.Rect.Height / dpi.DpiScaleY;
        var barLeft = tb.Rect.Left / dpi.DpiScaleX;
        var barRight = tb.Rect.Right / dpi.DpiScaleX;

        Top = barTop + (barHeight - ActualHeight) / 2;

        var anchorRight = NativeMethods.GetTrayAreaRect() is { } tray
            ? tray.Left / dpi.DpiScaleX
            : barRight;

        Left = Math.Max(barLeft + 8, anchorRight - ActualWidth - TrayGap);
    }

    private void RestoreFreePosition()
    {
        var ui = _shell.Settings.Ui;
        if (ui.MiniBarLeft is { } left && ui.MiniBarTop is { } top && IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
            return;
        }

        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 20;
        Top = work.Bottom - ActualHeight - 20;
    }

    private static bool IsOnScreen(double left, double top)
    {
        var l = SystemParameters.VirtualScreenLeft;
        var t = SystemParameters.VirtualScreenTop;
        var w = SystemParameters.VirtualScreenWidth;
        var h = SystemParameters.VirtualScreenHeight;

        // يكفي أن يبقى جزء معقول منه مرئيًا، حتى لا يختفي بعد فصل شاشة ثانية.
        return left >= l - 40 && top >= t - 40 && left <= l + w - 60 && top <= t + h - 20;
    }

    private void AssertTopmost(bool force)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            NativeMethods.SetWindowPos(
                handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize |
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        }
        catch (Exception ex) when (!force)
        {
            Log.Warn($"تعذّر إبقاء الشريط في المقدمة: {ex.Message}");
        }
    }

    // ================= التفاعل =================

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_shell.Settings.Ui.PinMiniBarToTaskbar) return; // مثبّت داخل الشريط: لا سحب

        _dragStart = e.GetPosition(this);
        _dragged = false;
        Shell.MouseMove += OnMouseMove;
        Shell.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(this);
        if (!_dragged &&
            Math.Abs(current.X - _dragStart.X) < 4 &&
            Math.Abs(current.Y - _dragStart.Y) < 4) return;

        _dragged = true;
        Shell.ReleaseMouseCapture();
        Shell.MouseMove -= OnMouseMove;

        try { DragMove(); }
        catch (InvalidOperationException) { /* أُفلت الزر أثناء بدء السحب */ }

        SavePosition();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        Shell.MouseMove -= OnMouseMove;
        if (Shell.IsMouseCaptured) Shell.ReleaseMouseCapture();

        if (!_dragged) _shell.ShowMainWindow();
        _dragged = false;

        // إظهار النافذة الرئيسية ينقل التركيز؛ نُعيد فرض بقاء الشريط فوق شريط المهام.
        AssertTopmost(force: true);
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        // النقر الأيمن يوقف الصوت إن كان يعمل، وإلا يُخفي الشريط.
        if (_shell.Audio.IsPlaying) _shell.StopAudio();
        else _shell.SetMiniBarVisible(false);
    }

    private void SavePosition()
    {
        _shell.Settings.Ui.MiniBarLeft = Left;
        _shell.Settings.Ui.MiniBarTop = Top;

        try { _shell.Store.Save(_shell.Settings); }
        catch (Exception ex) { Log.Warn($"تعذّر حفظ موضع الشريط: {ex.Message}"); }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_shell.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
