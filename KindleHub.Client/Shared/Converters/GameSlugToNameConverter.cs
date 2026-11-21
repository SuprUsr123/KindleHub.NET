using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KindleHub.Client.Models;

namespace KindleHub.Client.Converters;

/// <summary>
/// Turns a relay game slug ("ttt", "crazy8", "g2048") into the display name the
/// official client uses ("Tic-Tac-Toe", "Uno", "2048"). The relay only ever
/// carries the slug, so anything listing a live room has to resolve it, and an
/// unknown slug falls through unchanged rather than blanking the row.
/// </summary>
public sealed class GameSlugToNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var slug = value as string;
        return string.IsNullOrEmpty(slug) ? "" : GameCatalog.NameFor(slug);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
