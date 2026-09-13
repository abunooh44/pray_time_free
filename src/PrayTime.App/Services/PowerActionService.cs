using System.Runtime.InteropServices;
using PrayTime.App.Infrastructure;
using PrayTime.App.Interop;
using PrayTime.Core.Scheduling;

namespace PrayTime.App.Services;

/// <summary>
/// إنامة الجهاز أو قفل الشاشة قبل الإقامة.
/// كلاهما لا يحتاج صلاحيات مدير للمستخدم التفاعلي.
/// </summary>
public static class PowerActionService
{
    /// <summary>
    /// SetSuspendState من powrprof: أول وسيط false يعني «نوم» لا «إسبات».
    /// </summary>
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    public static string ArabicName(PreIqamaAction action) => action switch
    {
        PreIqamaAction.Sleep => "إنامة الجهاز",
        PreIqamaAction.Lock => "قفل الشاشة",
        _ => "لا شيء"
    };

    /// <summary>ينفّذ الإجراء ويُرجع رسالة الخطأ عند الفشل، أو null عند النجاح.</summary>
    public static string? Execute(PreIqamaAction action)
    {
        try
        {
            switch (action)
            {
                case PreIqamaAction.Sleep:
                    // لا بد من رفع منع السبات أولًا، وإلا رفض ويندز الطلب فورًا.
                    ReleaseKeepAwake();

                    if (!SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false))
                    {
                        var code = Marshal.GetLastWin32Error();
                        Log.Warn($"تعذّرت إنامة الجهاز (رمز {code}) — سنكتفي بقفل الشاشة.");

                        // على بعض الأجهزة يكون النوم معطّلًا في إعدادات الطاقة.
                        // القفل بديل مضمون خير من ألا يحدث شيء.
                        return LockWorkStation() ? null : $"تعذّرت إنامة الجهاز وقفل الشاشة (رمز {code}).";
                    }

                    Log.Info("أُنيم الجهاز قبل الإقامة.");
                    return null;

                case PreIqamaAction.Lock:
                    if (!LockWorkStation())
                    {
                        var code = Marshal.GetLastWin32Error();
                        return $"تعذّر قفل الشاشة (رمز {code}).";
                    }

                    Log.Info("قُفلت الشاشة قبل الإقامة.");
                    return null;

                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"فشل إجراء ما قبل الإقامة: {action}", ex);
            return ex.Message;
        }
    }

    /// <summary>
    /// يلغي طلب «امنع النوم» الذي يرفعه تشغيل الأذان.
    /// بدونه يتجاهل ويندز طلب الإنامة بصمت.
    /// </summary>
    private static void ReleaseKeepAwake() =>
        NativeMethods.SetThreadExecutionState(NativeMethods.ExecutionState.Continuous);
}
