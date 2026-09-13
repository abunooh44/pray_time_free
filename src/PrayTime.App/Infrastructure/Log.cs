using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;

namespace PrayTime.App.Infrastructure;

/// <summary>
/// سجل نصي بسيط بملف يومي. وجوده أساسي: بدونه يستحيل معرفة لماذا لم ينطلق أذان الفجر.
/// الكتابة على خيط منفصل حتى لا يتعثّر تشغيل الصوت بسبب القرص.
/// </summary>
public static class Log
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>());
    private static readonly Lock Gate = new();
    private static bool _started;

    public static void Start()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
        }

        Directory.CreateDirectory(AppPaths.Logs);
        PruneOldLogs();

        var thread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "PrayTime.Log"
        };
        thread.Start();

        Info($"=== بدء التشغيل — {AppPaths.ExecutablePath} ===");
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        if (!_started) return;
        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        Queue.Add($"{stamp} [{level}] {message}");
    }

    private static void WriterLoop()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                var file = Path.Combine(AppPaths.Logs,
                    $"praytime-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // فشل الكتابة في السجل يجب ألا يُسقط التطبيق أبدًا.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-14);
            foreach (var f in Directory.EnumerateFiles(AppPaths.Logs, "praytime-*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
