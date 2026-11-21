using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Set, ported from the official client — twelve cards, each with a count, a
/// colour, a shape and a fill. Tap three cards; if every feature is all-same or
/// all-different it is a set. Find ten sets; an invalid trio costs 10 points.
/// </summary>
public sealed class SetGame : GameBase
{
    private const int Cols = 12;
    private const int Rows = 4;
    private const int TableSize = 12;
    private const int DeckSize = 81;
    private const int TargetSets = 10;

    private const int DealBase = 36;   // row 3, cols 0-2
    private const int MsgBase = 39;    // row 3, cols 3-8
    private const int EndBase = 45;    // row 3, cols 9-11

    private readonly record struct SetCard(int Count, int Colour, int Shape, int Fill);

    private static readonly string[] Shapes = { "●", "▲", "■" };

    private readonly List<GameCell> _cells = new();
    private readonly List<SetCard> _deck = new();
    private readonly List<SetCard> _table = new();
    private readonly HashSet<int> _sel = new();
    private int _sets;
    private int _misses;
    private bool _over;
    private string _msg = "Tap three cards.";

    public SetGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "set";
    public override string Name => "Set";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _over
        ? Math.Max(1, 50 * _sets - 10 * _misses + (_sets >= TargetSets ? 100 : 0))
        : 0;

    public override string? ResultText => !_over ? null
        : _sets >= TargetSets ? $"All {TargetSets} sets found."
        : $"Ended with {_sets} sets.";

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            return $"Sets {_sets}/{TargetSets} · misses {_misses} · deck {_deck.Count} · fill: bold, plain, beige";
        }
    }

    public override void Reset()
    {
        _deck.Clear();
        for (int count = 0; count < 3; count++)
            for (int colour = 0; colour < 3; colour++)
                for (int shape = 0; shape < 3; shape++)
                    for (int fill = 0; fill < 3; fill++)
                        _deck.Add(new SetCard(count, colour, shape, fill));
        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
        }

        _table.Clear();
        _sel.Clear();
        _sets = 0;
        _misses = 0;
        _over = false;
        ScoreCounts = false;
        _msg = "Tap three cards.";
        DealTable();
        EnsureSetAvailable();
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count || _over) return false;

        int card = CardAt(index);
        if (card >= 0 && card < _table.Count)
        {
            if (!_sel.Add(card)) _sel.Remove(card);
            if (_sel.Count == 3)
            {
                var trio = _sel.ToList();
                if (IsSet(_table[trio[0]], _table[trio[1]], _table[trio[2]]))
                {
                    _sets++;
                    _msg = "Set! +50";
                    RemoveAndReplace(trio);
                    if (_sets >= TargetSets)
                    {
                        _over = true;
                        ScoreCounts = true;
                    }
                    else EnsureSetAvailable();
                }
                else
                {
                    _misses++;
                    _msg = "Not a set -10";
                }
                _sel.Clear();
            }
            Redraw();
            return true;
        }

        if (index >= DealBase && index < DealBase + 3)
        {
            if (HasSet() || _over)
            {
                _msg = "There is a set on the table.";
                Redraw();
                return true;
            }
            Redeal();
            Redraw();
            return true;
        }

        if (index >= EndBase && index < EndBase + 3)
        {
            _over = true;
            if (_sets >= 3) ScoreCounts = true;
            else _msg = "Ended with too few sets.";
            Redraw();
            return true;
        }

        return false;
    }

    private void RemoveAndReplace(List<int> trio)
    {
        var kept = new List<SetCard>();
        for (int i = 0; i < _table.Count; i++)
        {
            if (trio.Contains(i))
            {
                // Replace in place while the deck lasts; the table shrinks at the tail.
                if (_deck.Count > 0)
                {
                    kept.Add(_deck[^1]);
                    _deck.RemoveAt(_deck.Count - 1);
                }
            }
            else kept.Add(_table[i]);
        }
        _table.Clear();
        _table.AddRange(kept);
        _sel.Clear();
    }

    /// <summary>A fresh twelve whenever the table has no set at all.</summary>
    private void EnsureSetAvailable()
    {
        int guard = 0;
        while (!HasSet() && _deck.Count > 0 && guard++ < 10) Redeal();
    }

    private void Redeal()
    {
        _deck.AddRange(_table);
        _table.Clear();
        _sel.Clear();
        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
        }
        DealTable();
        _msg = "No set on the table — redeal.";
    }

    private void DealTable()
    {
        while (_table.Count < TableSize && _deck.Count > 0)
        {
            _table.Add(_deck[^1]);
            _deck.RemoveAt(_deck.Count - 1);
        }
    }

    private bool HasSet()
    {
        for (int a = 0; a < _table.Count; a++)
            for (int b = a + 1; b < _table.Count; b++)
                for (int c = b + 1; c < _table.Count; c++)
                    if (IsSet(_table[a], _table[b], _table[c])) return true;
        return false;
    }

    private static bool IsSet(SetCard a, SetCard b, SetCard c) =>
        FeatureOk(a.Count, b.Count, c.Count)
        && FeatureOk(a.Colour, b.Colour, c.Colour)
        && FeatureOk(a.Shape, b.Shape, c.Shape)
        && FeatureOk(a.Fill, b.Fill, c.Fill);

    private static bool FeatureOk(int x, int y, int z) =>
        (x == y && y == z) || (x != y && x != z && y != z);

    private static int CardAt(int index)
    {
        int row = index / Cols, col = index % Cols;
        if (row > 2 || col % 3 != 0) return -1;
        return row * 4 + col / 3;
    }

    private static int CellOf(int card) => (card / 4) * Cols + (card % 4) * 3;

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 18;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        for (int k = 0; k < _table.Count && k < TableSize; k++)
        {
            var card = _table[k];
            var c = _cells[CellOf(k)];
            c.Text = string.Concat(Enumerable.Repeat(Shapes[card.Shape], card.Count + 1));
            c.Foreground = card.Colour == 0 ? Bad : card.Colour == 1 ? Good : Accent;
            c.Background = card.Fill == 0 ? Cell : card.Fill == 1 ? Board : Tiles4[0];
            c.Bold = card.Fill != 1; // solid and striped are bold; hollow is plain
            c.IsEnabled = !_over;
            if (_sel.Contains(k))
            {
                c.BorderBrush = Accent;
                c.BorderThickness = new Avalonia.Thickness(2);
            }
        }

        bool dealNeeded = !_over && !HasSet() && _deck.Count > 0;
        for (int i = 0; i < 3; i++)
        {
            var c = _cells[DealBase + i];
            c.FontSize = 10;
            c.Bold = true;
            c.Background = dealNeeded ? Cell : Board;
            c.IsEnabled = dealNeeded;
        }
        Spread(DealBase, "Deal new 12", 3, 10);

        for (int i = 0; i < 3; i++)
        {
            var c = _cells[EndBase + i];
            c.FontSize = 10;
            c.Bold = true;
            c.Background = !_over ? Cell : Board;
            c.IsEnabled = !_over;
        }
        Spread(EndBase, "End game", 3, 10);

        Spread(MsgBase, _msg, 6, 10);
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
