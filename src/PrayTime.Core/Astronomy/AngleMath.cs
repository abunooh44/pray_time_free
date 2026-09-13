namespace PrayTime.Core.Astronomy;

/// <summary>حسابات مثلثية بالدرجات بدل الراديان، مع دوال تطبيع الزوايا والساعات.</summary>
public static class AngleMath
{
    public const double DegToRad = Math.PI / 180.0;
    public const double RadToDeg = 180.0 / Math.PI;

    public static double Sin(double deg) => Math.Sin(deg * DegToRad);
    public static double Cos(double deg) => Math.Cos(deg * DegToRad);
    public static double Tan(double deg) => Math.Tan(deg * DegToRad);

    public static double Asin(double x) => Math.Asin(x) * RadToDeg;
    public static double Acos(double x) => Math.Acos(x) * RadToDeg;
    public static double Atan2(double y, double x) => Math.Atan2(y, x) * RadToDeg;

    /// <summary>ظل التمام العكسي بالدرجات. تُستخدم لحساب ارتفاع الشمس وقت العصر.</summary>
    public static double Acot(double x) => Math.Atan(1.0 / x) * RadToDeg;

    /// <summary>تطبيع زاوية إلى المدى [0, 360).</summary>
    public static double Fix360(double a) => Fix(a, 360.0);

    /// <summary>تطبيع ساعة إلى المدى [0, 24).</summary>
    public static double Fix24(double h) => Fix(h, 24.0);

    /// <summary>تطبيع فارق ساعات إلى المدى (-12, +12]. تُستخدم لمعادلة الزمن.</summary>
    public static double Fix12Signed(double h)
    {
        var v = Fix(h, 24.0);
        return v > 12.0 ? v - 24.0 : v;
    }

    private static double Fix(double a, double range)
    {
        if (double.IsNaN(a) || double.IsInfinity(a)) return a;
        a -= range * Math.Floor(a / range);
        return a < 0 ? a + range : a;
    }

    /// <summary>
    /// يقيّد ناتج جيب تمام زاوية الساعة داخل [-1, 1].
    /// يمنع تسرّب NaN إلى الجدولة عند خطوط العرض العالية أو الزوايا الشاذة.
    /// </summary>
    public static bool TryClampCos(double cosValue, out double clamped)
    {
        clamped = Math.Clamp(cosValue, -1.0, 1.0);
        return !double.IsNaN(cosValue) && cosValue >= -1.0 && cosValue <= 1.0;
    }
}
