// Converters/BoolToSelectionColorConverter.cs
using System.Globalization;

namespace SimuladorMegaHair.App.Converters;

public class BoolToStrokeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool selected = value is true;
        string gold = "#C8A96E";
        string normal = "#333333";
        return Color.FromArgb(selected ? gold : normal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool selected = value is true;
        return Color.FromArgb(selected ? "#2A2520" : "#1A1A20");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToTextColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool selected = value is true;
        string gold = "#C8A96E";
        return Color.FromArgb(selected ? gold : "#FFFFFF");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}