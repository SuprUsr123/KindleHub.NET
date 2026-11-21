using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Snakes &amp; Ladders, ported from the official client — roll the die and race the
/// computer to square 100. Ladders send you up, snakes slide you back, and you need
/// the exact roll to finish: overshoot and you bounce back.
/// </summary>
public sealed class SnakesLaddersGame : GameBase
{
    private const int Cols = 10;
    private const int Squares = 100;
    private const int RollCap = 40;

    private const int RollCell = 100; // row 10, col 0
    private const int DieCell = 101;  // row 10, col 1

    private static readonly Dictionary<int, int> Ladders = new()
    {
        [1] = 38, [4] = 14, [9] = 31, [21] = 42, [28] = 84, [36] = 44,
        [51] = 67, [71] = 91, [80] = 100,
    };
    private static readonly Dictionary<int, int> Snakes = new()
    {
        [16] = 6, [47] = 26, [49] = 11, [56] = 53, [62] = 19, [64] = 60,
        [87] = 24, [93] = 73, [95] = 75, [98] = 78,
    };

    private readonly List<GameCell> _cells = new();
    private int _you = 1;
    private int _cpu = 1;
    private int _rolls;
    private int _die;
    private bool _over, _win;
    private string _msg = "Roll to start.";

    public SnakesLaddersGame()
    {
        for (int i = 0; i < Cols * (Squares / Cols + 1); i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "snakesladders";
    public override string Name => "Snakes & Ladders";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _win ? 200 + Math.Max(0, 100 - _rolls * 5) : 0;

    public override string? ResultText => !_over ? null
        : _win ? $"You reached 100 in {_rolls} rolls."
        : _rolls >= RollCap ? "Out of rolls."
        : "The computer reached 100 first.";

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            return $"You {_you} ● · CPU {_cpu} ○ · roll {_rolls}/{RollCap} · {_msg}";
        }
    }

    public override void Reset()
    {
        _you = 1;
        _cpu = 1;
        _rolls = 0;
        _die = 0;
        _over = _win = false;
        ScoreCounts = false;
        _msg = "Roll to start.";
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count || _over) return false;
        if (index != RollCell) return false;
        Roll();
        return true;
    }

    public override bool OnKey(GameKey key)
    {
        if (key != GameKey.Confirm || _over) return false;
        Roll();
        return true;
    }

    private void Roll()
    {
        _rolls++;
        _die = Random.Shared.Next(6) + 1;
        _msg = $"You rolled {_die}";
        var (newYou, note) = Advance(_you, _die);
        _you = newYou;
        if (note.Length > 0) _msg += " · " + note;

        if (_you == Squares)
        {
            _over = _win = true;
            ScoreCounts = true;
            Redraw();
            return;
        }

        int cpuDie = Random.Shared.Next(6) + 1;
        var (newCpu, cpuNote) = Advance(_cpu, cpuDie);
        _cpu = newCpu;
        _msg += $" · CPU rolled {cpuDie}";
        if (cpuNote.Length > 0) _msg += " · " + cpuNote;

        if (_cpu == Squares)
        {
            _over = true;
            _msg = "The computer reached 100 first.";
        }
        else if (_rolls >= RollCap)
        {
            _over = true; // out of rolls — no win, no score
        }
        Redraw();
    }

    /// <summary>Exact roll to finish, bounce back on overshoot, then any jump applies.</summary>
    private static (int Square, string Note) Advance(int from, int die)
    {
        int to = from + die;
        if (to > Squares) to = Squares - (to - Squares);

        if (Ladders.TryGetValue(to, out int up)) return (up, $"▲ ladder to {up}");
        if (Snakes.TryGetValue(to, out int down)) return (down, $"▼ snake to {down}");
        return (to, "");
    }

    /// <summary>Square 1 sits bottom-left; rows snake left-to-right, right-to-left, …</summary>
    private static int CellOf(int square)
    {
        int n = square - 1;
        int rowFromBottom = n / Cols;
        int col = rowFromBottom % 2 == 0 ? n % Cols : Cols - 1 - n % Cols;
        return (Cols - 1 - rowFromBottom) * Cols + col;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Muted;
            c.IsEnabled = false;
            c.FontSize = 9;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        for (int s = 1; s <= Squares; s++)
        {
            var c = _cells[CellOf(s)];
            bool youHere = _you == s, cpuHere = _cpu == s;
            if (youHere || cpuHere)
            {
                c.Text = youHere ? "●" : "○";
                c.Foreground = youHere ? Ink : Bad;
                c.FontSize = 16;
                c.Bold = true;
                continue;
            }
            string mark = Ladders.ContainsKey(s) ? "▲" : Snakes.ContainsKey(s) ? "▼" : "";
            c.Text = s.ToString() + mark;
            c.Foreground = Ladders.ContainsKey(s) ? Good : Snakes.ContainsKey(s) ? Bad : Muted;
            c.Bold = mark.Length > 0;
        }

        var roll = _cells[RollCell];
        roll.Text = "Roll ⚂";
        roll.FontSize = 12;
        roll.Bold = true;
        roll.Background = !_over ? Cell : Board;
        roll.IsEnabled = !_over;

        var die = _cells[DieCell];
        die.Text = _die > 0 ? _die.ToString() : "";
        die.FontSize = 20;
        die.Bold = true;
        die.Foreground = Ink;

        Spread(102, _msg, 8, 10);
    }

    /// <summary>Splits text evenly across adjacent cells so one row carries a sentence.</summary>
    private void Spread(int baseIndex, string text, int parts, int fontSize)
    {
        text = text.Trim();
        if (text.Length > parts * 7) text = Ellipsis(text, parts * 7);
        int chunk = (text.Length + parts - 1) / parts;
        for (int i = 0; i < parts; i++)
        {
            int at = i * chunk;
            _cells[baseIndex + i].Text = at < text.Length ? text[at..Math.Min(at + chunk, text.Length)] : "";
            _cells[baseIndex + i].FontSize = fontSize;
        }
    }
}
