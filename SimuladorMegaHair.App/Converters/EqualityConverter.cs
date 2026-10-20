// Converters/EqualityConverter.cs
using System.Globalization;

namespace SimuladorMegaHair.App.Converters;

public class EqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2)
            return false;

        var val1 = values[0]?.ToString();
        var val2 = values[1]?.ToString();

        return string.Equals(val1, val2, StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}