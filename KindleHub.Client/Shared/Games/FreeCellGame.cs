using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// FreeCell, ported from the official client — solitaire with every card face up,
/// so it is thinking rather than luck. Build the cascades down in alternating
/// colours, park single cards in the four free cells, and build the foundations
/// up by suit. The Auto button flushes every safe card to the foundations.
/// </summary>
public sealed class FreeCellGame : GameBase
{
    private const int Cols = 8;
    private const int CascadeTop = 2;
    private const int CascadeRows = 19; // 7 dealt + room for everything you can stack
    private const int TotalRows = CascadeTop + CascadeRows;
    private const int CardCount = 52;

    private const int AutoCell = 8;
    private const int NewCell = 9;

    private static readonly string[] Suits = { "♠", "♥", "♦", "♣" };
    private static readonly string[] RankName = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

    private readonly List<GameCell> _cells = new();
    private readonly List<List<int>> _cascade = new();
    private readonly int[] _free = new int[4];   // -1 = empty
    private readonly int[] _found = new int[4];  // cards parked per foundation
    private int _selCol = -1, _selPos = -1;
    private int _selFree = -1;
    private int _moves;
    private bool _over;
    private string _msg = "Tap a card, then a destination.";

    public FreeCellGame()
    {
        for (int i = 0; i < Cols * TotalRows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "freecell";
    public override string Name => "FreeCell";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    /// <summary>The official web formula: the fewer moves, the more points.</summary>
    public override int Score => _over ? Math.Max(1, 2000 - _moves) : 0;

    public override string? ResultText => _over ? $"Solved in {_moves} moves." : null;

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            int parked = _found.Sum();
            return $"{_msg} · moves {_moves} · foundations {parked}/52";
        }
    }

    public override void Reset()
    {
        _cascade.Clear();
        for (int i = 0; i < Cols; i++) _cascade.Add(new List<int>());
        Array.Fill(_free, -1);
        Array.Clear(_found, 0, _found.Length);
        _selCol = _selPos = _selFree = -1;
        _moves = 0;
        _over = false;
        ScoreCounts = false;
        _msg = "Tap a card, then a destination.";

        var deck = Enumerable.Range(0, CardCount).ToList();
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        for (int i = 0; i < deck.Count; i++) _cascade[i % Cols].Add(deck[i]);
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count) return false;
        if (index == NewCell)
        {
            Reset(); // "New ⟲" deals again even after a win
            return true;
        }
        if (_over) return false;
        if (index == AutoCell)
        {
            AutoToFoundations();
            Redraw();
            return true;
        }

        if (index < 4)
        {
            TapFreeCell(index);
            Redraw();
            return true;
        }
        if (index < 8)
        {
            TapFoundation(index - 4);
            Redraw();
            return true;
        }
        if (index >= CascadeTop * Cols)
        {
            int depth = index / Cols - CascadeTop;
            int col = index % Cols;
            TapCascade(col, depth);
            Redraw();
            return true;
        }
        return false;
    }

    private void TapFreeCell(int i)
    {
        var run = SelectedRun();
        if (run.Count == 1 && _free[i] < 0)
        {
            TakeSelection();
            _free[i] = run[0];
            _moves++;
            ClearSelection();
            CheckWin();
            return;
        }
        ClearSelection();
        if (_free[i] >= 0) _selFree = i;
    }

    private void TapFoundation(int f)
    {
        var run = SelectedRun();
        if (run.Count == 1 && CanGoFoundation(run[0], f))
        {
            int card = run[0];
            TakeSelection();
            _found[card / 13]++;
            _moves++;
            ClearSelection();
            CheckWin();
            return;
        }
        ClearSelection();
    }

    private void TapCascade(int col, int depth)
    {
        var pile = _cascade[col];
        if (depth < pile.Count)
        {
            bool tappingSelf = _selCol == col && depth >= _selPos && _selCol >= 0;
            if (HasSelection() && !tappingSelf && TryMoveToCascade(col)) return;

            if (ValidRunFrom(col, depth) || depth == pile.Count - 1)
            {
                ClearSelection();
                _selCol = col;
                _selPos = depth;
                _msg = "Now tap a destination.";
            }
            else
            {
                ClearSelection();
                _msg = "That isn't a movable run.";
            }
            return;
        }

        if (pile.Count == 0 && depth == 0 && HasSelection() && TryMoveToCascade(col)) return;
        ClearSelection();
    }

    private bool TryMoveToCascade(int col)
    {
        var run = SelectedRun();
        if (run.Count == 0) return false;
        var dest = _cascade[col];

        if (dest.Count > 0)
        {
            int top = dest[^1];
            if (run[0] % 13 != top % 13 - 1 || IsRed(run[0]) == IsRed(top))
            {
                _msg = "Cascades build down in alternating colours.";
                return false;
            }
        }

        int max = MaxRun(dest.Count == 0);
        if (run.Count > max)
        {
            _msg = $"That run needs more free cells (max {max}).";
            return false;
        }

        TakeSelection();
        dest.AddRange(run);
        _moves++;
        ClearSelection();
        _msg = "Moved.";
        return true;
    }

    /// <summary>Supermove size: (free cells + 1) doubled per empty cascade.</summary>
    private int MaxRun(bool destEmpty)
    {
        int freeCells = _free.Count(f => f < 0);
        int emptyCols = _cascade.Count(p => p.Count == 0);
        if (destEmpty && emptyCols > 0) emptyCols--;
        return (1 + freeCells) << emptyCols;
    }

    private void AutoToFoundations()
    {
        bool moved = true;
        while (moved)
        {
            moved = false;
            for (int col = 0; col < Cols; col++)
            {
                var pile = _cascade[col];
                if (pile.Count == 0) continue;
                int card = pile[^1];
                if (!SafeForFoundation(card) || !CanGoFoundation(card, card / 13)) continue;
                pile.RemoveAt(pile.Count - 1);
                _found[card / 13]++;
                _moves++;
                moved = true;
            }
            for (int i = 0; i < 4; i++)
            {
                int card = _free[i];
                if (card < 0 || !SafeForFoundation(card) || !CanGoFoundation(card, card / 13)) continue;
                _free[i] = -1;
                _found[card / 13]++;
                _moves++;
                moved = true;
            }
        }
        ClearSelection();
        _msg = "Safe cards parked.";
        CheckWin();
    }

    /// <summary>Aces and twos always; higher cards once the other colour is caught up.</summary>
    private bool SafeForFoundation(int card)
    {
        int r = card % 13;
        if (r <= 1) return true;
        int o1 = IsRed(card) ? 0 : 1;
        int o2 = IsRed(card) ? 3 : 2;
        return _found[o1] >= r && _found[o2] >= r;
    }

    private bool CanGoFoundation(int card, int f) => card / 13 == f && card % 13 == _found[f];

    private bool ValidRunFrom(int col, int pos)
    {
        var pile = _cascade[col];
        for (int i = pos; i < pile.Count - 1; i++)
            if (pile[i + 1] % 13 != pile[i] % 13 - 1 || IsRed(pile[i + 1]) == IsRed(pile[i]))
                return false;
        return true;
    }

    private bool HasSelection() => _selCol >= 0 || _selFree >= 0;

    private List<int> SelectedRun()
    {
        if (_selFree >= 0) return new List<int> { _free[_selFree] };
        if (_selCol >= 0 && _selPos < _cascade[_selCol].Count)
            return _cascade[_selCol].GetRange(_selPos, _cascade[_selCol].Count - _selPos);
        return new List<int>();
    }

    private void TakeSelection()
    {
        if (_selFree >= 0)
        {
            _free[_selFree] = -1;
            return;
        }
        if (_selCol >= 0)
            _cascade[_selCol].RemoveRange(_selPos, _cascade[_selCol].Count - _selPos);
    }

    private void ClearSelection()
    {
        _selCol = _selPos = _selFree = -1;
    }

    private void CheckWin()
    {
        if (_found.Sum() >= CardCount)
        {
            _over = true;
            ScoreCounts = true;
        }
    }

    private static bool IsRed(int card) => card / 13 is 1 or 2;

    private static string CardText(int c) => RankName[c % 13] + Suits[c / 13];

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 15;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        // Free cells (row 0, left) and foundations (row 0, right).
        for (int i = 0; i < 4; i++)
        {
            var c = _cells[i];
            if (_free[i] >= 0)
            {
                c.Text = CardText(_free[i]);
                c.Foreground = IsRed(_free[i]) ? Bad : Ink;
                c.Background = Cell;
                c.IsEnabled = !_over;
                if (_selFree == i)
                {
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(2);
                }
            }
            else
            {
                c.Text = "·";
                c.Foreground = Muted;
                c.IsEnabled = !_over && HasSelection();
            }
        }
        for (int f = 0; f < 4; f++)
        {
            var c = _cells[4 + f];
            if (_found[f] > 0)
            {
                int top = f * 13 + _found[f] - 1;
                c.Text = CardText(top);
                c.Foreground = IsRed(top) ? Bad : Ink;
                c.Background = Cell;
            }
            else
            {
                c.Text = Suits[f];
                c.Foreground = Muted;
            }
            c.IsEnabled = !_over && HasSelection();
        }

        var auto = _cells[AutoCell];
        auto.Text = "Auto ⏩";
        auto.FontSize = 10;
        auto.Bold = true;
        auto.Background = !_over ? Cell : Board;
        auto.IsEnabled = !_over;
        var newDeal = _cells[NewCell];
        newDeal.Text = "New ⟲";
        newDeal.FontSize = 10;
        newDeal.Bold = true;
        newDeal.Background = Cell;
        newDeal.IsEnabled = true;

        // Cascades: bottom row first, cap the display at the panel height.
        for (int col = 0; col < Cols; col++)
        {
            var pile = _cascade[col];
            int shown = Math.Min(pile.Count, CascadeRows);
            int skip = pile.Count - shown;
            for (int j = 0; j < shown; j++)
            {
                int card = pile[skip + j];
                var c = _cells[(CascadeTop + j) * Cols + col];
                c.Text = CardText(card);
                c.Foreground = IsRed(card) ? Bad : Ink;
                c.Background = Cell;
                c.IsEnabled = !_over;
                if (_selCol == col && skip + j >= _selPos)
                {
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(1);
                }
            }
            if (pile.Count == 0)
            {
                var c = _cells[CascadeTop * Cols + col];
                c.Text = "·";
                c.Foreground = Muted;
                c.IsEnabled = !_over && HasSelection();
            }
        }
    }
}
