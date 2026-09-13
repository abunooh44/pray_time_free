using System.Security.Cryptography;
using PrayTime.App.Infrastructure;
using PrayTime.Core.Security;
using PrayTime.Core.Settings;

namespace PrayTime.App.Services;

public enum PinVerifyResult
{
    Ok,
    Wrong,
    LockedOut,
    NotConfigured,
    UnreadableOnThisMachine
}

/// <summary>
/// إنشاء رمز الخروج والتحقق منه.
///
/// الرمز نفسه لا يُخزَّن أبدًا. نخزّن بصمة PBKDF2-SHA256 بـ210 آلاف دورة مع ملح عشوائي،
/// ثم نلفّها بـDPAPI مربوطةً بحساب ويندز الحالي. السبب: رمز من أربعة أرقام له
/// عشرة آلاف احتمال فقط، وبصمة PBKDF2 وحدها يمكن كسرها في ثوانٍ إن نُسخ الملف.
/// التغليف بـDPAPI يجعل البصمة عديمة الفائدة خارج هذا الحساب.
/// </summary>
public sealed class PinService
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private PinAttemptState _attempts;

    public PinService(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        _attempts = store.LoadAttempts();
    }

    public bool IsConfigured => _settings.Security.Pin is not null;

    public bool IsLockedOut => _attempts.IsLocked(DateTimeOffset.UtcNow);

    public TimeSpan LockRemaining => _attempts.RemainingLock(DateTimeOffset.UtcNow);

    public void SetPin(string pin)
    {
        if (!PinHasher.IsAcceptablePin(pin))
            throw new ArgumentException("طول الرمز غير مقبول", nameof(pin));

        var (credential, rawHash, salt) = PinHasher.Create(pin);

        // نلفّ البصمة بـDPAPI. إن تعذّر (حالة نادرة) نبقيها بصمة PBKDF2 عادية.
        try
        {
            var protectedHash = ProtectedData.Protect(rawHash, salt, DataProtectionScope.CurrentUser);
            credential.HashBase64 = Convert.ToBase64String(protectedHash);
            credential.MachineProtected = true;
        }
        catch (CryptographicException ex)
        {
            Log.Warn($"تعذّر تغليف الرمز بـDPAPI، سيُحفظ كبصمة PBKDF2 فقط: {ex.Message}");
            credential.MachineProtected = false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawHash);
        }

        _settings.Security.Pin = credential;
        _attempts.Reset();
        _store.SaveAttempts(_attempts);
        _store.Save(_settings);

        Log.Info("تم تعيين رمز خروج جديد.");
    }

    public void ClearPin()
    {
        _settings.Security.Pin = null;
        _attempts.Reset();
        _store.SaveAttempts(_attempts);
        _store.Save(_settings);
        Log.Info("تمت إزالة رمز الخروج.");
    }

    public PinVerifyResult Verify(string pin)
    {
        var credential = _settings.Security.Pin;
        if (credential is null) return PinVerifyResult.NotConfigured;

        var now = DateTimeOffset.UtcNow;
        if (_attempts.IsLocked(now)) return PinVerifyResult.LockedOut;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(credential.SaltBase64);
            var stored = Convert.FromBase64String(credential.HashBase64);

            expected = credential.MachineProtected
                ? ProtectedData.Unprotect(stored, salt, DataProtectionScope.CurrentUser)
                : stored;
        }
        catch (CryptographicException ex)
        {
            // يحدث إذا انتقلت الإعدادات إلى جهاز أو حساب آخر: DPAPI لا يفكّ التغليف هناك.
            Log.Warn($"تعذّر فكّ تغليف الرمز على هذا الجهاز: {ex.Message}");
            return PinVerifyResult.UnreadableOnThisMachine;
        }
        catch (FormatException ex)
        {
            Log.Error("بيانات الرمز تالفة في ملف الإعدادات", ex);
            return PinVerifyResult.UnreadableOnThisMachine;
        }

        var ok = PinHasher.Verify(pin, expected, salt, credential.Iterations);
        CryptographicOperations.ZeroMemory(expected);

        if (ok)
        {
            _attempts.Reset();
            _store.SaveAttempts(_attempts);
            return PinVerifyResult.Ok;
        }

        _attempts.RegisterFailure(now);
        _store.SaveAttempts(_attempts);
        Log.Warn($"محاولة رمز خاطئة رقم {_attempts.FailedAttempts}");

        return _attempts.IsLocked(now) ? PinVerifyResult.LockedOut : PinVerifyResult.Wrong;
    }

    /// <summary>يُستدعى بعد حذف ملف الإعدادات يدويًا لإعادة الضبط.</summary>
    public void ReloadAttempts() => _attempts = _store.LoadAttempts();
}
