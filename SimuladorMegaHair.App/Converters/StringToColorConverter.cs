using System.Globalization;

namespace SimuladorMegaHair.App.Converters;

public class StringToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToLower() switch
        {
            "preto" => Color.FromArgb("#000000"),
            "castanho" => Color.FromArgb("#4B3621"),
            "chocolate" => Color.FromArgb("#3E2723"),
            "loiro" => Color.FromArgb("#E1C16E"),
            "platina" => Color.FromArgb("#E5E4E2"),
            "mel" => Color.FromArgb("#D2B48C"),
            _ => Color.FromArgb("#A9A5A0") // Cor padrão
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}