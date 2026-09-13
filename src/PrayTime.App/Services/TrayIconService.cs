using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using PrayTime.App.Infrastructure;

namespace PrayTime.App.Services;

/// <summary>
/// أيقونة صينية النظام وقائمتها.
/// القائمة عنصر WPF حقيقي، فترث اتجاه RTL والسمة تلقائيًا — بخلاف قائمة WinForms
/// التي لا يمكن تنسيقها ولا تعكس الاتجاه بشكل صحيح.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly AppShell _shell;
    private TaskbarIcon? _icon;
    private MenuItem? _stopItem;
    private MenuItem _miniBarItem = new();

    public TrayIconService(AppShell shell) => _shell = shell;

    public void Initialize()
    {
        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
            ToolTipText = "مواقيت الصلاة",
            Visibility = Visibility.Visible,
            NoLeftClickDelay = true
        };

        _icon.TrayLeftMouseDown += (_, _) => _shell.ShowMainWindow();
        _icon.ContextMenu = BuildMenu();

        Log.Info("أيقونة الصينية جاهزة.");
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };

        menu.Items.Add(Item("عرض المواقيت", () => _shell.ShowMainWindow(), bold: true));

        _stopItem = Item("إيقاف الصوت", () => _shell.StopAudio());
        _stopItem.IsEnabled = false;
        menu.Items.Add(_stopItem);

        _miniBarItem = new MenuItem { Header = "الشريط المصغّر", IsCheckable = true };
        _miniBarItem.Click += (_, _) => _shell.SetMiniBarVisible(_miniBarItem.IsChecked);
        menu.Items.Add(_miniBarItem);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("الإعدادات", () => _shell.ShowSettings()));
        menu.Items.Add(Item("عن التطبيق", () => _shell.ShowAbout()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("إنهاء التطبيق", () => _shell.RequestExit()));

        // نُحدّث حالة «إيقاف الصوت» لحظة فتح القائمة.
        menu.Opened += (_, _) =>
        {
            if (_stopItem is not null) _stopItem.IsEnabled = _shell.Audio.IsPlaying;
            _miniBarItem.IsChecked = _shell.IsMiniBarVisible;
        };

        return menu;
    }

    private static MenuItem Item(string header, Action action, bool bold = false)
    {
        var item = new MenuItem { Header = header };
        if (bold) item.FontWeight = FontWeights.SemiBold;
        item.Click += (_, _) => action();
        return item;
    }

    public void UpdateTooltip(string text)
    {
        if (_icon is not null) _icon.ToolTipText = text;
    }

    public void ShowBalloon(string title, string message)
    {
        try
        {
            _icon?.ShowNotification(title, message, NotificationIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warn($"تعذّر عرض الإشعار: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // بدون التخلّص الصريح تبقى أيقونة شبح في الصينية حتى يمرّ المؤشّر فوقها.
        _icon?.Dispose();
        _icon = null;
    }
}
