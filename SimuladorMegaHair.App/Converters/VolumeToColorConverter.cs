using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimuladorMegaHair.App.Converters
{
    public class VolumeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int nivel && parameter is int currentNivel)
            {
                return nivel == currentNivel ? Color.FromArgb("#F4D03F") : Color.FromArgb("#333333");
            }
            return Color.FromArgb("#333333");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
