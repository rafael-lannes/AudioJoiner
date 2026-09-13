using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AudioJoiner.Models;
using MediaColor = System.Windows.Media.Color;

namespace AudioJoiner.UI.Converters;

public class VolumeToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float vol)
        {
            return $"{(int)Math.Round(vol * 100)}%";
        }
        if (value is double volD)
        {
            return $"{(int)Math.Round(volD * 100)}%";
        }
        return "100%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && int.TryParse(s.Replace("%", "").Trim(), out int val))
        {
            return val / 100f;
        }
        return 1.0f;
    }
}

public class VolumeFloatToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float f) return (double)f;
        return 1.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return (float)d;
        return 1.0f;
    }
}

public class PeakToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float peak)
        {
            double maxWidth = parameter is string paramStr && double.TryParse(paramStr, out double max) ? max : 100.0;
            return Math.Clamp(peak * maxWidth, 0, maxWidth);
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool flag && flag;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool visible = value is Visibility v && v == Visibility.Visible;
        return Invert ? !visible : visible;
    }
}

public class DeviceStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string status = value?.ToString() ?? "";
        if (status.Contains("Ativo") || status.Contains("Capturando"))
        {
            return new SolidColorBrush(MediaColor.FromRgb(16, 185, 129)); // #10B981 Emerald
        }
        if (status.Contains("Falha") || status.Contains("Erro") || status.Contains("Desconectado"))
        {
            return new SolidColorBrush(MediaColor.FromRgb(239, 68, 68)); // #EF4444 Coral Red
        }
        if (status.Contains("Pronto"))
        {
            return new SolidColorBrush(MediaColor.FromRgb(14, 165, 233)); // #0EA5E9 Sky Blue
        }
        return new SolidColorBrush(MediaColor.FromRgb(100, 116, 139)); // #64748B Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class DeviceIconToVectorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DeviceIconType iconType)
        {
            return iconType switch
            {
                DeviceIconType.Headphones => "M12 3a9 9 0 0 0-9 9v7c0 1.1.9 2 2 2h2a2 2 0 0 0 2-2v-4a2 2 0 0 0-2-2H5v-1a7 7 0 0 1 14 0v1h-2a2 2 0 0 0-2 2v4a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2v-7a9 9 0 0 0-9-9z",
                DeviceIconType.Monitor => "M21 2H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h7v2H8v2h8v-2h-2v-2h7c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm0 14H3V4h18v12z",
                DeviceIconType.Bluetooth => "M14.88 16.29L13 18.17V14.41l1.88 1.88zM13 5.83l1.88 1.88L13 9.59V5.83M17.71 7.71L12 2h-1v7.59L6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 11 14.41V22h1l5.71-5.71-4.3-4.29 4.3-4zm-2.83 8.58l-1.88-1.88V18.17l1.88-1.88z",
                DeviceIconType.Usb => "M15 7v4h1v2h-3V5h2l-3-4-3 4h2v8H8v-2.07c.6-.33 1-.97 1-1.68 0-1.1-.9-2-2-2s-2 .9-2 2c0 .71.4 1.35 1 1.68V13c0 1.1.9 2 2 2h3v5.05c-.6.31-1 .95-1 1.65 0 1.1.9 2 2 2s2-.9 2-2c0-.7-.4-1.34-1-1.65V15h3c1.1 0 2-.9 2-2v-2h1V7h-4z",
                _ => "M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z"
            };
        }
        return "M3 9v6h4l5 5V4L7 9H3z";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
