namespace KindleHub.Client.Games;

/// <summary>Directional / action keys a game understands.</summary>
public enum GameKey
{
    None,
    Up,
    Down,
    Left,
    Right,
    Confirm,
}

/// <summary>
/// One renderable square on the game board. Games fill a flat list of these and the
/// shared <c>GameBoardView</c> lays them out, so a new game never touches XAML.
/// Colours are Avalonia brushes so a game can express "revealed", "correct",
/// "wrong" without the view needing to know what the game means.
/// </summary>
public sealed class GameCell
{
    public string Text { get; set; } = "";
    public Avalonia.Media.IBrush? Background { get; set; }
    public Avalonia.Media.IBrush? Foreground { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int FontSize { get; set; } = 20;
    public bool Bold { get; set; }
    /// <summary>Grid columns this cell occupies. 2 makes a keyboard key or peg twice as wide.</summary>
    public int Span { get; set; } = 1;
    /// <summary>Set when a cell is decoration rather than something to tap.</summary>
    public string? Tag { get; set; }
}
