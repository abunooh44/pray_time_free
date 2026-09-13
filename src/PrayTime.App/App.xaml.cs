using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;
using PrayTime.App.Infrastructure;

namespace PrayTime.App;

public partial class App : Application
{
    private const string MutexName = @"Local\PrayTime.SingleInstance";
    private const string ShowEventName = @"Local\PrayTime.ShowWindow";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showRegistration;
    private AppShell? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ٠) وضع الإزالة: يُنفَّذ قبل أي شيء آخر، بلا نافذة ولا مؤقّتات ولا قفل نسخة واحدة.
        if (e.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            var silentRemoval = e.Args.Contains("/S", StringComparer.OrdinalIgnoreCase);
            Services.Uninstaller.Run(silentRemoval);
            Shutdown();
            return;
        }

        // ١) نسخة واحدة فقط. تشغيل نسخة ثانية يُظهر النافذة القائمة.
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        Log.Start();

        // ٢) ضبط لغة الواجهة.
        //    FrameworkElement.Language قيمته الافتراضية en-US مهما كانت ثقافة الخيط،
        //    وبدون هذا السطر تُنسَّق كل التواريخ في الروابط بالإنجليزية داخل واجهة عربية.
        var culture = new CultureInfo("ar-OM");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        // ٣) استثناء غير معالج يجب أن يُسجَّل ولا يُسقط تطبيقًا يفترض أن يعمل شهورًا.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // ٤) إغلاق ويندز: نخرج بهدوء بلا مطالبة برمز.
        SystemEvents.SessionEnding += OnSessionEnding;
        SessionEnding += OnAppSessionEnding;

        ListenForShowRequests();

        try
        {
            _shell = new AppShell(Dispatcher);
            _shell.Start();
        }
        catch (Exception ex)
        {
            Log.Error("فشل الإقلاع", ex);
            MessageBox.Show($"تعذّر بدء التطبيق:\n{ex.Message}", "مواقيت",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var startHidden = e.Args.Contains("--minimized") || _shell.Settings.Ui.StartMinimized;
        if (!startHidden) _shell.ShowMainWindow();
        else Log.Info("بدأ التطبيق مخفيًا في صينية النظام.");
    }

    private void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var handle))
            {
                handle.Set();
                handle.Dispose();
            }
        }
        catch (Exception)
        {
            // النسخة الأخرى تعمل على أي حال؛ لا داعي لإزعاج المستخدم.
        }
    }

    private void ListenForShowRequests()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        // استثناء داخل InvokeAsync يُخزَّن في العملية المُرجَعة ولا يمرّ على
        // DispatcherUnhandledException إطلاقًا. بلا try/catch هنا يفشل «إظهار النافذة» بصمت.
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.InvokeAsync(() =>
            {
                try { _shell?.ShowMainWindow(); }
                catch (Exception ex) { Log.Error("فشل طلب إظهار النافذة", ex); }
            }),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("استثناء غير معالج في خيط الواجهة", e.Exception);
        e.Handled = true; // البقاء حيًّا أهم من الانهيار: مهمة التطبيق ألا يفوّت أذانًا.
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log.Error("استثناء غير معالج", e.ExceptionObject as Exception);

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        Log.Info("ويندز يُغلق الجلسة — خروج بلا مطالبة بالرمز.");
        Dispatcher.Invoke(() => _shell?.RequestExit(bypassPin: true));
    }

    private void OnAppSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        e.Cancel = false;
        _shell?.RequestExit(bypassPin: true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionEnding -= OnSessionEnding;

        _showRegistration?.Unregister(null);
        _showEvent?.Dispose();

        _shell?.Dispose();

        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
