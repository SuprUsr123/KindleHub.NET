using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace KindleHub.Client.Converters;

/// <summary>
/// Highlights the signed-in player's own row in the in-game leaderboard strip:
/// their line renders in strong ink, everyone else's in the muted grey the rest
/// of the board uses. Binds to the row's IsMe flag.
/// </summary>
public sealed class LeaderboardMeBrushConverter : IValueConverter
{
    public static readonly LeaderboardMeBrushConverter Instance = new();

    private static readonly IBrush Mine = new SolidColorBrush(Color.Parse("#1b1b1f"));
    private static readonly IBrush Others = new SolidColorBrush(Color.Parse("#6b6b76"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Mine : Others;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
