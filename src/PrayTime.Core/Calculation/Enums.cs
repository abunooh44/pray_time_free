namespace PrayTime.Core.Calculation;

/// <summary>معامل الظل المعتمد في تحديد وقت العصر.</summary>
public enum AsrJuristic
{
    /// <summary>الشافعي والمالكي والحنبلي: ظل المثل (×1).</summary>
    Shafi = 1,

    /// <summary>الحنفي: ظل المثلين (×2).</summary>
    Hanafi = 2
}

/// <summary>قاعدة تصحيح خطوط العرض العالية. لا تنطبق على مسقط إطلاقًا (خط عرض 23.6°).</summary>
public enum HighLatitudeRule
{
    None = 0,
    NightMiddle = 1,
    OneSeventh = 2,
    AngleBased = 3
}

public enum TimeRounding
{
    Nearest = 0,
    Floor = 1,
    Ceiling = 2
}
