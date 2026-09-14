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

    /// <summary>
    /// هل تملك هذه النسخة القفل فعلًا؟
    /// ReleaseMutex من نسخة لا تملكه يُلقي ApplicationException ويُسقط التطبيق —
    /// وهو ما كان يحدث في كل مرة يُنقر فيها الاختصار والتطبيق يعمل.
    /// </summary>
    private bool _ownsMutex;

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

        var startedHidden = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        // ١) نسخة واحدة فقط.
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        _ownsMutex = isFirstInstance;

        if (!isFirstInstance)
        {
            // الحارس يشغّلنا كل ربع ساعة بـ‎--minimized‎ للتأكد أن التطبيق حيّ.
            // لو أظهرنا النافذة عندها لقفزت في وجه المستخدم كل ربع ساعة.
            if (!startedHidden) SignalExistingInstance();

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

        // ٤) إشعار إغلاق ويندز — للحفظ فقط، لا للخروج. انظر التعليق على المعالج.
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

        if (!startedHidden && !_shell.Settings.Ui.StartMinimized) _shell.ShowMainWindow();
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

    /// <summary>
    /// إشعار ويندز بأن الجلسة «على وشك» الانتهاء — وهو سؤال لا إعلان نهائي،
    /// ويمكن أن يُلغى الإغلاق بعده (تطبيق آخر يمانع، أو يتراجع المستخدم).
    ///
    /// كان الكود هنا يُنهي التطبيق فورًا. فإذا أُلغي الإغلاق بقي الجهاز يعمل
    /// والتطبيق قد مات، ولا شيء يعيده حتى تسجيل الدخول التالي — أي أذان فائت
    /// بلا سبب. الآن نكتفي بحفظ الحالة على القرص ونترك ويندز ينهي العملية
    /// بنفسه إن أكمل الإغلاق فعلًا.
    /// </summary>
    private void OnAppSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        Log.Info($"ويندز يستأذن في إنهاء الجلسة ({e.ReasonSessionEnding}) — حفظ الحالة دون خروج.");

        e.Cancel = false; // لا نمانع الإغلاق أبدًا: المعارضة تُظهر شاشة «تطبيق يمنع الإغلاق».
        _shell?.FlushState();

        // WPF سيُنهي التطبيق بعد هذا المعالج مهما فعلنا. نترك خلفنا فحصًا
        // يعيدنا إن تبيّن أن الإغلاق أُلغي ولم يُغلق الجهاز فعلًا.
        if (_shell?.Settings.Behavior.WatchdogEnabled == true)
            Services.WatchdogService.ScheduleRevivalCheck();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showRegistration?.Unregister(null);
        _showEvent?.Dispose();

        _shell?.Dispose();

        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); }
            catch (ApplicationException ex) { Log.Warn($"تحرير القفل: {ex.Message}"); }
        }

        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
