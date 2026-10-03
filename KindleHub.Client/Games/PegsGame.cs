using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Peg Solitaire on the standard English board. Tap a peg, then the hole it can
/// jump into; a peg with no legal jump clears itself so you can change your mind.
/// </summary>
public sealed class PegsGame : GameBase
{
    public const int Side = 7;

    /// <summary>
    /// The 33 holes of the English board, as (col,row) in a 7x7 canvas. The shape is
    /// the classic cross: 3-5-5-7-5-5-3, centred on (3,3), which is the square that
    /// starts empty.
    /// </summary>
    public static readonly (int X, int Y)[] Holes =
    {
        (1,0),(2,0),(3,0),
        (0,1),(1,1),(2,1),(3,1),(4,1),
        (0,2),(1,2),(2,2),(3,2),(4,2),
        (0,3),(1,3),(2,3),(3,3),(4,3),(5,3),(6,3),
        (1,4),(2,4),(3,4),(4,4),(5,4),
        (1,5),(2,5),(3,5),(4,5),(5,5),
        (2,6),(3,6),(4,6),
    };

    /// <summary>The hole that starts empty.</summary>
    private static readonly (int X, int Y) Start = (3, 3);

    private readonly List<GameCell> _cells = new();
    private readonly Dictionary<(int, int), int> _index = new();
    private readonly bool[] _occupied = new bool[Holes.Length];
    private int _moves;
    private bool _stuck;

    public PegsGame()
    {
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                _cells.Add(Blank());

        for (int i = 0; i < Holes.Length; i++) _index[Holes[i]] = i;
        Reset();
    }

    public override string Slug => "pegs";
    public override string Name => "Peg Solitaire";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => ScoreCounts ? Math.Max(0, 40 - _moves) : 0;

    public override string? ResultText
    {
        get
        {
            if (PegsLeft == 1)
            {
                int last = Array.FindIndex(_occupied, o => o);
                return last == _index[Start]
                    ? $"One peg left in the centre — a perfect game in {_moves} jumps."
                    : $"One peg left — {_moves} jumps.";
            }
            if (PegsLeft == 0) return $"Cleared the board in {_moves} jumps.";
            return _stuck ? $"No legal moves left — {PegsLeft} pegs remain." : null;
        }
    }

    public override string StatusText
    {
        get
        {
            if (_stuck) return $"No legal jumps remain · {_moves} jumps";
            if (_selected is null) return $"{PegsLeft} pegs left · {_moves} jumps · tap a peg";
            return $"{PegsLeft} pegs left · tap a highlighted hole to jump into";
        }
    }

    public int PegsLeft => _occupied.Count(o => o);

    private (int X, int Y)? _selected;

    public override void Reset()
    {
        for (int i = 0; i < _occupied.Length; i++) _occupied[i] = true;
        _occupied[_index[Start]] = false;
        _selected = null;
        _moves = 0;
        _stuck = false;
        ScoreCounts = false;
        Redraw();
    }

    /// <summary>Jumps available from a peg, as (from, over, to) cell coordinates.</summary>
    private IEnumerable<((int X, int Y) From, (int X, int Y) Over, (int X, int Y) To)> JumpsFrom((int X, int Y) p)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var over = (X: p.X + dx, Y: p.Y + dy);
            var to = (X: p.X + 2 * dx, Y: p.Y + 2 * dy);
            if (!_index.ContainsKey(over) || !_index.ContainsKey(to)) continue;
            if (!_occupied[_index[over]]) continue;
            if (_occupied[_index[to]]) continue;
            yield return (p, over, to);
        }
    }

    public override bool OnTap(int index)
    {
        if (_stuck || PegsLeft <= 1 || index < 0 || index >= _cells.Count) return false;
        var at = (X: index % Side, Y: index / Side);
        if (!_index.ContainsKey(at)) return false;   // not a hole — ignore

        int i = _index[at];
        if (_selected is null)
        {
            if (!_occupied[i]) return true;
            _selected = at;
            Redraw();
            return true;
        }

        var from = _selected.Value;
        var jump = JumpsFrom(from).FirstOrDefault(j => j.To == at);
        if (jump.To == at)
        {
            _occupied[_index[jump.From]] = false;
            _occupied[_index[jump.Over]] = false;
            _occupied[i] = true;
            _moves++;
            if (PegsLeft <= 1) ScoreCounts = true;
            else if (!HasAnyLegalMove()) _stuck = true;
        }
        _selected = null;   // either way the selection is spent
        Redraw();
        return true;
    }


    private bool HasAnyLegalMove()
    {
        for (int i = 0; i < Holes.Length; i++)
            if (_occupied[i] && JumpsFrom(Holes[i]).Any()) return true;
        return false;
    }

    public override void Redraw()
    {
        var legal = _selected is null
            ? new HashSet<(int, int)>()
            : JumpsFrom(_selected.Value).Select(j => j.To).ToHashSet();
        for (int y = 0; y < Side; y++)
        for (int x = 0; x < Side; x++)
        {
            var c = _cells[y * Side + x];
            var at = (x, y);
            if (!_index.ContainsKey(at))
            {
                c.Text = ""; c.Background = Board; c.IsEnabled = false; c.Foreground = Ink; c.FontSize = 18;
                continue;
            }

            bool peg = _occupied[_index[at]];
            bool isSel = _selected == at;
            bool isTarget = legal.Contains(at);
            c.Text = peg ? "●" : isTarget ? "○" : "";
            c.Foreground = peg ? Ink : isTarget ? Accent : Muted;
            c.Background = isSel ? Tiles4[2] : Board;
            c.IsEnabled = !_stuck && (peg || isTarget);
            c.FontSize = 22;
            c.Bold = false;
        }
    }
}
