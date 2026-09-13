using System.Security.Cryptography;
using System.Text;

namespace PrayTime.Core.Security;

/// <summary>
/// بيانات التحقق من رمز الخروج. لا تحتوي الرمز نفسه، فقط بصمة PBKDF2 مع ملح عشوائي.
/// </summary>
public sealed class PinCredential
{
    public string Algorithm { get; set; } = PinHasher.Algorithm;
    public int Iterations { get; set; } = PinHasher.DefaultIterations;
    public string SaltBase64 { get; set; } = "";
    public string HashBase64 { get; set; } = "";

    /// <summary>هل البصمة ملفوفة إضافيًا بـDPAPI (مربوطة بحساب ويندز الحالي)؟</summary>
    public bool MachineProtected { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class PinHasher
{
    public const string Algorithm = "PBKDF2-HMACSHA256";

    /// <summary>عدد الدورات الموصى به من OWASP لـPBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 210_000;

    public const int SaltBytes = 16;
    public const int HashBytes = 32;

    public const int MinPinLength = 4;
    public const int MaxPinLength = 12;

    public static bool IsAcceptablePin(string? pin) =>
        !string.IsNullOrEmpty(pin) && pin.Length >= MinPinLength && pin.Length <= MaxPinLength;

    public static byte[] Derive(string pin, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, HashBytes);

    /// <summary>ينشئ بصمة جديدة. التغليف بـDPAPI يتم في طبقة التطبيق لأنه خاص بويندز.</summary>
    public static (PinCredential Credential, byte[] RawHash, byte[] Salt) Create(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(pin, salt, DefaultIterations);

        var credential = new PinCredential
        {
            Algorithm = Algorithm,
            Iterations = DefaultIterations,
            SaltBase64 = Convert.ToBase64String(salt),
            HashBase64 = Convert.ToBase64String(hash),
            MachineProtected = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        return (credential, hash, salt);
    }

    /// <summary>مقارنة بزمن ثابت ضد بصمة مفكوكة التغليف.</summary>
    public static bool Verify(string pin, byte[] expectedHash, byte[] salt, int iterations)
    {
        var actual = Derive(pin, salt, iterations);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedHash, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }
}

/// <summary>
/// كبح محاولات التخمين. يُحفظ على القرص حتى لا تُصفّر المحاولات بإعادة تشغيل التطبيق.
/// </summary>
public sealed class PinAttemptState
{
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }

    private const int FreeAttempts = 5;

    public bool IsLocked(DateTimeOffset nowUtc) =>
        LockedUntilUtc is { } until && nowUtc < until;

    public TimeSpan RemainingLock(DateTimeOffset nowUtc) =>
        LockedUntilUtc is { } until && nowUtc < until ? until - nowUtc : TimeSpan.Zero;

    public void RegisterFailure(DateTimeOffset nowUtc)
    {
        FailedAttempts++;
        if (FailedAttempts < FreeAttempts) return;

        // ٣٠ ثانية تتضاعف مع كل فشل، بحدٍّ أقصى ١٥ دقيقة.
        var over = FailedAttempts - FreeAttempts;
        var seconds = Math.Min(30.0 * Math.Pow(2, over), 900.0);
        LockedUntilUtc = nowUtc.AddSeconds(seconds);
    }

    public void Reset()
    {
        FailedAttempts = 0;
        LockedUntilUtc = null;
    }
}
