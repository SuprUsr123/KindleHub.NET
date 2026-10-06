using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace KindleHub.Client.Converters;

public sealed class ProfileFrameBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var id = value as string ?? "";
        if (id.Length == 7 && id[0] == '#')
        {
            try { return new SolidColorBrush(Color.Parse(id)); }
            catch (FormatException) { }
        }
        var color = id switch
        {
            "" => "#777777",
            "plus_ring" => "#e8622a",
            "pro_darklaurel" or "pro_obsidian" => "#b34a24",
            "max_phoenix" or "max_dragons" or "max_sun" or "max_void" or "max_gate" => "#d6a43b",
            _ => "#c9962e"
        };
        return new SolidColorBrush(Color.Parse(color));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
