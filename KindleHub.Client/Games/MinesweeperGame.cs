using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Minesweeper — clear every square that isn't a mine. The first tap is always safe
/// (the board is laid after it), which is what players expect and what the official
/// client does; otherwise an unlucky first click ends the game before it starts.
/// </summary>
public sealed class MinesweeperGame : GameBase
{
    public const int Side = 9;
    public const int Mines = 10;

    private readonly List<GameCell> _cells = new();
    private readonly bool[] _mine = new bool[Side * Side];
    private readonly int[] _near = new int[Side * Side];
    private readonly bool[] _open = new bool[Side * Side];
    private readonly bool[] _flag = new bool[Side * Side];
    private bool _started;
    private bool _dead;
    /// <summary>The mine the player actually stepped on, so Redraw can mark it out.</summary>
    private int _hit = -1;
    private bool _won;
    private int _flags;

    public MinesweeperGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "minesweeper";
    public override string Name => "Minesweeper";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override string? ResultText => _won
        ? $"Cleared in {_opened} moves."
        : _dead ? "Boom — that was a mine." : null;

    public override string StatusText => _won
        ? "Board cleared."
        : _dead
            ? $"Hit a mine · you opened {_opened} squares."
            : $"{Mines} mines · {_flags} flagged · tap to open, tap a flag to mark";

    private int _opened;

    public override void Reset()
    {
        Array.Clear(_mine, 0, _mine.Length);
        Array.Clear(_open, 0, _open.Length);
        Array.Clear(_flag, 0, _flag.Length);
        _started = _dead = _won = false;
        _flags = _opened = 0;
        _hit = -1;
        ScoreCounts = false;
        Redraw();
    }

    /// <summary>Second tap on an already-open square flags it — a tap-only control scheme.</summary>
    public override bool OnTap(int index)
    {
        if (_won || index < 0 || index >= _mine.Length) return false;
        int x = index % Side, y = index / Side;

        if (_open[index])
        {
            if (_flag[index]) { _flag[index] = false; _flags--; }
            else { _flag[index] = true; _flags++; }
            Redraw();
            return true;
        }

        if (_flag[index]) return true;

        if (!_started) LayMines(x, y);

        if (_mine[index]) { _dead = true; _hit = index; _open[index] = true; Redraw(); return true; }

        Flood(x, y);
        if (!_dead && _open.Count(b => b) == Side * Side - Mines) { _won = true; ScoreCounts = true; }
        Redraw();
        return true;
    }

    /// <summary>Places mines anywhere except the first square and its neighbours.</summary>
    private void LayMines(int safeX, int safeY)
    {
        var banned = new HashSet<int>();
        foreach (var (dx, dy) in Around(safeX, safeY))
            if (InBounds(dx, dy)) banned.Add(dy * Side + dx);

        var free = Enumerable.Range(0, Side * Side).Where(i => !banned.Contains(i)).ToList();
        for (int i = free.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (free[i], free[j]) = (free[j], free[i]);
        }
        foreach (var i in free.Take(Mines)) _mine[i] = true;

        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                _near[y * Side + x] = Around(x, y).Count(n => InBounds(n.Item1, n.Item2)
                                                            && _mine[n.Item2 * Side + n.Item1]);
        _started = true;
    }

    private static bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Side && y < Side;

    private void Flood(int x, int y)
    {
        var stack = new Stack<(int x, int y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            int i = cy * Side + cx;
            if (_open[i] || _mine[i]) continue;
            _open[i] = true;
            _opened++;
            // A zero-count square opens its neighbours, which is what makes the flood work.
            if (_near[i] == 0)
                foreach (var n in Around(cx, cy))
                    if (InBounds(n.Item1, n.Item2)) stack.Push((n.Item1, n.Item2));
        }
    }

    public static IEnumerable<(int, int)> Around(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0) yield return (x + dx, y + dy);
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            if (_flag[i])
            {
                // A flag stays a flag even after the game ends — it used to fall
                // through to the number branch and show an adjacent-mine count
                // instead, because the "flagged" branch required !_dead.
                // A flag on a mined square is a wrong flag, which reads differently.
                bool wrong = _dead && !_mine[i];
                c.Text = wrong ? "✗" : "⚑";
                c.Background = wrong ? Bad : Cell;
                c.IsEnabled = false;
                c.Foreground = wrong ? Cell : Bad;
                c.FontSize = 20; c.Bold = true;
                continue;
            }
            if (!_open[i])
            {
                c.Text = ""; c.Background = Cell; c.IsEnabled = !_won; c.Foreground = Ink;
                c.FontSize = 20; c.Bold = true;
                continue;
            }
            if (_mine[i])
            {
                // The mine you stepped on is loud; the rest are revealed quietly.
                c.Text = "✱";
                c.Background = _dead && _hit == i ? Bad : Dug;
                c.IsEnabled = false;
                c.Foreground = _dead && _hit == i ? Cell : Ink;
                c.FontSize = 20; c.Bold = true;
                continue;
            }
            int n = _near[i];
            c.Text = n == 0 ? "" : n.ToString();
            c.IsEnabled = false;
            // Dug squares get their own surface rather than a shade of Cell, so the
            // board reads at a glance instead of looking like nothing happened.
            c.Background = Dug;
            c.Foreground = n == 0 ? Muted : DigitColour(n);
            c.FontSize = 18;
            c.Bold = true;
        }
    }

    /// <summary>Classic digit palette, kept dark enough to stay legible on e-ink.</summary>
    private static Avalonia.Media.IBrush DigitColour(int n) => n switch
    {
        1 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2f6fed")),
        2 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1f7a3d")),
        3 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#b3261e")),
        4 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6b3fa0")),
        5 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9a6700")),
        6 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0f7b8a")),
        7 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1b1b1f")),
        8 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6b6b76")),
        _ => Ink,
    };
}
