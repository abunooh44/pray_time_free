using PrayTime.Core.Model;

namespace PrayTime.Core.Calculation;

public sealed record CalculationMethod(
    string Id,
    string ArabicName,
    double FajrAngle,
    double IshaAngle,
    int? IshaIntervalMinutes,
    string Note)
{
    /// <summary>
    /// السجلات في C# تُولّد ToString يطبع كل الحقول. القوائم المنسدلة تعرض هذا
    /// النص الخام، فنتجاوزه بالاسم العربي وحده.
    /// </summary>
    public override string ToString() => ArabicName;

    public CalculationParameters ToParameters(
        AsrJuristic asr = AsrJuristic.Shafi,
        IReadOnlyDictionary<Prayer, int>? offsets = null) => new()
        {
            MethodId = Id,
            FajrAngle = FajrAngle,
            IshaAngle = IshaAngle,
            IshaIntervalMinutes = IshaIntervalMinutes,
            AsrJuristic = asr,
            OffsetsMinutes = offsets ?? new Dictionary<Prayer, int>()
        };

    public static readonly CalculationMethod OmanAwqaf =
        new("OmanAwqaf", "وزارة الأوقاف — سلطنة عُمان", 18.0, 18.0, null,
            "الفجر ١٨° والعشاء ١٨°. الافتراضي لبوشر ومسقط.");

    public static readonly CalculationMethod GulfRegion =
        new("GulfRegion", "منطقة الخليج", 19.5, 0, 90,
            "الفجر ١٩.٥° والعشاء بعد المغرب بـ٩٠ دقيقة.");

    public static readonly CalculationMethod UmmAlQura =
        new("UmmAlQura", "أم القرى — مكة المكرمة", 18.5, 0, 90,
            "الفجر ١٨.٥° والعشاء بعد المغرب بـ٩٠ دقيقة.");

    public static readonly CalculationMethod MuslimWorldLeague =
        new("MWL", "رابطة العالم الإسلامي", 18.0, 17.0, null, "الفجر ١٨° والعشاء ١٧°.");

    public static readonly CalculationMethod Egyptian =
        new("Egyptian", "الهيئة المصرية العامة للمساحة", 19.5, 17.5, null, "الفجر ١٩.٥° والعشاء ١٧.٥°.");

    public static readonly CalculationMethod Karachi =
        new("Karachi", "جامعة العلوم الإسلامية — كراتشي", 18.0, 18.0, null, "الفجر ١٨° والعشاء ١٨°.");

    public static readonly CalculationMethod Isna =
        new("ISNA", "الجمعية الإسلامية لأمريكا الشمالية", 15.0, 15.0, null, "الفجر ١٥° والعشاء ١٥°.");

    public static readonly IReadOnlyList<CalculationMethod> All =
    [
        OmanAwqaf, GulfRegion, UmmAlQura, MuslimWorldLeague, Egyptian, Karachi, Isna
    ];

    public static CalculationMethod ById(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) ?? OmanAwqaf;
}
