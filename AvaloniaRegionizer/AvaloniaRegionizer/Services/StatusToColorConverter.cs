using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AvaloniaRegionizer.Services;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var status = (value ?? "").ToString().ToLower();
        return status.Contains("ошибка") ? new SolidColorBrush(Colors.Red) :
               status.Contains("генерация") ? new SolidColorBrush(Colors.Blue) :
               !string.IsNullOrWhiteSpace(status) ? new SolidColorBrush(Colors.Green) :
               new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
