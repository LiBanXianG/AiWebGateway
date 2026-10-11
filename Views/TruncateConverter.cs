using System.Globalization;
using System.Windows.Data;

namespace AiWebGateway.Views;

public class TruncateConverter : IValueConverter
{
    public static readonly TruncateConverter Instance = new();

    public object Convert(object value, Type targetType,
                          object parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        if (s.Length <= 40) return s;
        return s.Substring(0, 40) + "...";
    }

    public object ConvertBack(object value, Type targetType,
                              object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}