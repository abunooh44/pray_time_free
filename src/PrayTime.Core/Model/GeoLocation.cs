namespace PrayTime.Core.Model;

public sealed record GeoLocation(
    string Name,
    double Latitude,
    double Longitude,
    double ElevationMeters,
    string TimeZoneId)
{
    /// <summary>
    /// بوشر، محافظة مسقط — الموقع الافتراضي.
    /// الارتفاع صفر عن قصد: التقاويم الرسمية تُحسب عند مستوى سطح البحر،
    /// وإدخال ارتفاع فعلي يزيح المغرب والشروق نحو دقيقة عن التقويم المطبوع.
    /// المستخدم يستطيع تغييره من الإعدادات إن أراد الدقة الفلكية لموقعه.
    /// </summary>
    public static readonly GeoLocation Bawshar =
        new("بوشر، مسقط", 23.5859, 58.4059, 0, "Asia/Muscat");

    public bool IsValid =>
        Latitude is >= -90 and <= 90 &&
        Longitude is >= -180 and <= 180 &&
        ElevationMeters is >= -500 and <= 9000 &&
        !string.IsNullOrWhiteSpace(TimeZoneId);
}
