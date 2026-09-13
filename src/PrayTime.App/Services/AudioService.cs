using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PrayTime.App.Infrastructure;
using PrayTime.App.Interop;

namespace PrayTime.App.Services;

public sealed class AudioService : IDisposable
{
    private readonly Lock _gate = new();

    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private FadeInOutSampleProvider? _fader;
    private KeepAwakeScope? _keepAwake;
    private CancellationTokenSource? _fadeOutCts;

    /// <summary>يُرفع على خيط NAudio، لا على خيط الواجهة.</summary>
    public event Action? PlaybackEnded;

    public bool IsPlaying
    {
        get { lock (_gate) return _output is { PlaybackState: PlaybackState.Playing }; }
    }

    public string? CurrentFile { get; private set; }

    /// <summary>
    /// يشغّل ملفًا صوتيًا. أي تشغيل جديد يوقف السابق فورًا.
    /// يُرجع رسالة الخطأ عند الفشل، أو null عند النجاح.
    /// </summary>
    public string? Play(string? fileNameOrPath, double volume, int fadeInMs, bool keepAwake)
    {
        var path = AppPaths.ResolveAudio(fileNameOrPath);
        if (path is null)
        {
            var message = $"ملف الصوت غير موجود: {fileNameOrPath ?? "(فارغ)"}";
            Log.Error(message);
            return message;
        }

        Stop(fadeOutMs: 0);

        try
        {
            lock (_gate)
            {
                _reader = new AudioFileReader(path) { Volume = 1.0f };

                _fader = new FadeInOutSampleProvider(_reader.ToSampleProvider(), initiallySilent: fadeInMs > 0);
                var volumeProvider = new VolumeSampleProvider(_fader)
                {
                    Volume = (float)Math.Clamp(volume, 0.0, 1.0)
                };

                _output = new WaveOutEvent { DesiredLatency = 200 };
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Init(volumeProvider);

                if (fadeInMs > 0) _fader.BeginFadeIn(fadeInMs);
                _output.Play();

                CurrentFile = path;
                if (keepAwake) _keepAwake = new KeepAwakeScope();
            }

            Log.Info($"تشغيل الصوت: {Path.GetFileName(path)} بمستوى {volume:P0}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"تعذّر تشغيل {path}", ex);
            CleanUp();
            return $"تعذّر تشغيل الملف: {ex.Message}";
        }
    }

    /// <summary>إيقاف بتلاشٍ ناعم. تمرير صفر يوقف فورًا.</summary>
    public void Stop(int fadeOutMs = 0)
    {
        FadeInOutSampleProvider? fader;
        lock (_gate)
        {
            if (_output is null) return;
            fader = _fader;
        }

        if (fadeOutMs > 0 && fader is not null)
        {
            fader.BeginFadeOut(fadeOutMs);

            _fadeOutCts?.Cancel();
            _fadeOutCts = new CancellationTokenSource();
            var token = _fadeOutCts.Token;

            _ = Task.Delay(fadeOutMs + 120, token).ContinueWith(t =>
            {
                if (!t.IsCanceled) CleanUp();
            }, TaskScheduler.Default);
        }
        else
        {
            CleanUp();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            Log.Error("توقّف التشغيل بسبب خطأ في جهاز الصوت", e.Exception);

        CleanUp();
        PlaybackEnded?.Invoke();
    }

    private void CleanUp()
    {
        lock (_gate)
        {
            if (_output is not null)
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                try { _output.Stop(); } catch (Exception ex) { Log.Warn($"تجاهل خطأ إيقاف: {ex.Message}"); }
                _output.Dispose();
                _output = null;
            }

            _reader?.Dispose();
            _reader = null;
            _fader = null;
            CurrentFile = null;

            _keepAwake?.Dispose();
            _keepAwake = null;
        }
    }

    /// <summary>أسماء ملفات الصوت المتاحة في المكتبة المرفقة ومجلد المستخدم.</summary>
    public static IReadOnlyList<string> AvailableSounds(string prefix)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in new[] { AppPaths.BundledAudio, AppPaths.UserAudio })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(f);
                if (!ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)) continue;

                var name = Path.GetFileName(f);
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) names.Add(name);
            }
        }

        return names.ToList();
    }

    public void Dispose()
    {
        _fadeOutCts?.Cancel();
        CleanUp();
    }
}
