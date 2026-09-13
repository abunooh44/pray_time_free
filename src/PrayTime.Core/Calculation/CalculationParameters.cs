using PrayTime.Core.Model;

namespace PrayTime.Core.Calculation;

/// <summary>كل ما يتحكم في نتيجة الحساب. قابل للتعديل بالكامل من الإعدادات.</summary>
public sealed record CalculationParameters
{
    public required string MethodId { get; init; }

    /// <summary>انخفاض الشمس تحت الأفق وقت الفجر، بالدرجات الموجبة.</summary>
    public double FajrAngle { get; init; } = 18.0;

    /// <summary>انخفاض الشمس وقت العشاء بالدرجات. يُتجاهل إذا كان <see cref="IshaIntervalMinutes"/> غير فارغ.</summary>
    public double IshaAngle { get; init; } = 18.0;

    /// <summary>وضع الفاصل الزمني: العشاء = المغرب + هذا العدد من الدقائق (طريقة أم القرى والخليج).</summary>
    public int? IshaIntervalMinutes { get; init; }

    public AsrJuristic AsrJuristic { get; init; } = AsrJuristic.Shafi;
    public HighLatitudeRule HighLatitudeRule { get; init; } = HighLatitudeRule.None;
    public TimeRounding Rounding { get; init; } = TimeRounding.Nearest;

    /// <summary>عدد دورات التكرار لتنقيح موضع الشمس. ثلاث دورات تتقارب دون ثانية واحدة.</summary>
    public int Iterations { get; init; } = 3;

    /// <summary>إزاحة يدوية بالدقائق لكل صلاة (موجبة أو سالبة).</summary>
    public IReadOnlyDictionary<Prayer, int> OffsetsMinutes { get; init; } =
        new Dictionary<Prayer, int>();

    public int OffsetFor(Prayer p) => OffsetsMinutes.TryGetValue(p, out var v) ? v : 0;
}
