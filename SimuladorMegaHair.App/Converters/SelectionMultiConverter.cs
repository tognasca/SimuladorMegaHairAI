// Converters/SelectionMultiConverter.cs
using System.Globalization;

namespace SimuladorMegaHair.App.Converters;

public class SelectionMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string type = parameter?.ToString() ?? "Stroke";

        if (values == null || values.Length < 2)
            return GetDefault(type);

        string val1 = values[0]?.ToString() ?? "";
        string val2 = values[1]?.ToString() ?? "";
        bool isSelected = !string.IsNullOrEmpty(val1)
            && string.Equals(val1, val2, StringComparison.OrdinalIgnoreCase);

        return type switch
        {
            "Stroke" => Color.FromArgb(isSelected ? "#C8A96E" : "#333333"),
            "Background" => Color.FromArgb(isSelected ? "#2A2520" : "#1A1A20"),
            "Text" => Color.FromArgb(isSelected ? "#C8A96E" : "#FFFFFF"),
            "Thickness" => isSelected ? 3.0 : 1.0,
            _ => GetDefault(type)
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();

    private static object GetDefault(string type) => type switch
    {
        "Stroke" => Color.FromArgb("#333333"),
        "Background" => Color.FromArgb("#1A1A20"),
        "Text" => Color.FromArgb("#FFFFFF"),
        "Thickness" => 1.0,
        _ => Color.FromArgb("#333333")
    };
}