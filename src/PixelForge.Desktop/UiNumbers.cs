using System.Globalization;

namespace PixelForge.Desktop;

public static class UiNumbers
{
    public static string Format(double value,int decimals=2)=>value.ToString(decimals==0?"0":"0."+new string('#',decimals),CultureInfo.InvariantCulture);
    public static bool TryParse(string text,int decimals,out double value)
    {
        value=0;
        if(!decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out decimal exact)||decimal.Round(exact,decimals)!=exact)return false;
        value=(double)exact;return double.IsFinite(value);
    }
}
