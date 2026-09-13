using System.Globalization;

namespace PrayTime.Core.Scheduling;

/// <summary>
/// سجل الأحداث التي أُطلقت فعلًا. يُحفظ على القرص.
/// وجوده هو ما يمنع تكرار الأذان بعد إعادة تشغيل التطبيق أو عند رجوع ساعة النظام للخلف.
/// نحتفظ بثلاثة أيام فقط ثم نُنظّف.
/// </summary>
public sealed class FiredLedger
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _keys;

    public bool HasFired(string key) => _keys.Contains(key);

    /// <summary>يسجّل الحدث ويُرجع true إن كان جديدًا.</summary>
    public bool MarkFired(string key) => _keys.Add(key);

    public void Load(IEnumerable<string> keys)
    {
        _keys.Clear();
        foreach (var k in keys) _keys.Add(k);
    }

    public void Clear() => _keys.Clear();

    /// <summary>يحذف المفاتيح الأقدم من ثلاثة أيام حتى لا ينمو الملف بلا حد.</summary>
    public int Prune(DateOnly today, int keepDays = 3)
    {
        var cutoff = today.AddDays(-keepDays);
        var stale = _keys.Where(k => !IsOnOrAfter(k, cutoff)).ToList();
        foreach (var k in stale) _keys.Remove(k);
        return stale.Count;
    }

    private static bool IsOnOrAfter(string key, DateOnly cutoff)
    {
        var sep = key.IndexOf('|');
        if (sep < 0) return false;
        return DateOnly.TryParseExact(
                   key.AsSpan(0, sep), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var d)
               && d >= cutoff;
    }
}
