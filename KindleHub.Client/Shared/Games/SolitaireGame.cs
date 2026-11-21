using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Klondike Solitaire (draw one), ported from the official client. Build the four
/// foundations up by suit from Ace to King and stack the tableau down in
/// alternating colours; tap the stock to draw, tap it again to recycle. A card
/// with exactly one legal destination moves itself.
/// </summary>
public sealed class SolitaireGame : GameBase
{
    private const int Cols = 10;
    private const int TabCols = 7;
    private const int TabTop = 1;
    private const int TabRows = 19; // deepest legal stack
    private const int TotalRows = TabTop + TabRows;
    private const int CardCount = 52;

    private const int StockCell = 0, WasteCell = 1, EndCell = 9;
    private const int NewDealBase = 7; // cols 7-8 of the top row

    private readonly record struct TCard(int Card, bool Up); // card 0..51, suit = card/13

    private static readonly string[] Suits = { "♠", "♥", "♦", "♣" };
    private static readonly string[] RankName = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

    private readonly List<GameCell> _cells = new();
    private readonly List<List<TCard>> _tab = new();
    private readonly List<int> _stock = new();
    private readonly List<int> _waste = new();
    private readonly int[] _found = new int[4];
    private int _selKind; // 0 none, 1 waste, 2 tableau
    private int _selCol, _selPos;
    private bool _over, _won;
    private string _msg = "Tap the stock to draw.";

    public SolitaireGame()
    {
        for (int i = 0; i < Cols * TotalRows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "solitaire";
    public override string Name => "Solitaire";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _over ? 10 * _found.Sum() + (_won ? 300 : 0) : 0;

    public override string? ResultText => !_over ? null
        : _won ? "All 52 cards are home!"
        : $"Ended with {_found.Sum()} cards on the foundations.";

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            return $"Foundations {_found.Sum()}/52 · {_msg}";
        }
    }

    public override void Reset()
    {
        _tab.Clear();
        for (int i = 0; i < TabCols; i++) _tab.Add(new List<TCard>());
        _stock.Clear();
        _waste.Clear();
        Array.Clear(_found, 0, _found.Length);
        _selKind = 0;
        _selCol = _selPos = 0;
        _over = _won = false;
        ScoreCounts = false;
        _msg = "Tap the stock to draw.";

        var deck = Enumerable.Range(0, CardCount).ToList();
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        int at = 0;
        for (int col = 0; col < TabCols; col++)
        {
            for (int j = 0; j <= col; j++) _tab[col].Add(new TCard(deck[at++], false));
            _tab[col][^1] = _tab[col][^1] with { Up = true };
        }
        _stock.AddRange(deck.Skip(at));
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count) return false;
        if (index is NewDealBase or NewDealBase + 1)
        {
            Reset();
            return true;
        }
        if (index == EndCell)
        {
            if (_over) return false;
            Finish();
            Redraw();
            return true;
        }
        if (_over) return false;

        if (index == StockCell)
        {
            TapStock();
            Redraw();
            return true;
        }
        if (index == WasteCell)
        {
            TapWaste();
            Redraw();
            return true;
        }
        if (index is >= 2 and <= 5)
        {
            TapFoundation(index - 2);
            Redraw();
            return true;
        }
        if (index >= Cols)
        {
            int col = index % Cols, depth = index / Cols - TabTop;
            if (col >= TabCols) return false;
            TapTableau(col, depth);
            Redraw();
            return true;
        }
        return false;
    }

    private void TapStock()
    {
        if (_stock.Count > 0)
        {
            _waste.Add(_stock[^1]);
            _stock.RemoveAt(_stock.Count - 1);
            ClearSelection();
            _msg = "Drawn.";
        }
        else if (_waste.Count > 0)
        {
            while (_waste.Count > 0)
            {
                _stock.Add(_waste[^1]);
                _waste.RemoveAt(_waste.Count - 1);
            }
            ClearSelection();
            _msg = "Waste recycled.";
        }
        else _msg = "Nothing left to draw.";
    }

    private void TapWaste()
    {
        if (_waste.Count == 0) return;
        var run = new List<int> { _waste[^1] };
        if (DestinationCount(run) == 1)
        {
            _selKind = 1;
            MoveToSingleDestination();
            return;
        }
        ClearSelection();
        _selKind = 1;
        _msg = "Tap a destination.";
    }

    private void TapFoundation(int f)
    {
        var run = SelectedRun();
        if (run.Count == 1 && CanGoFoundation(run[0], f))
        {
            TakeSelection();
            _found[run[0] / 13]++;
            ClearSelection();
            FlipTops();
            _msg = "Parked.";
            CheckWin();
            return;
        }
        ClearSelection();
    }

    private void TapTableau(int col, int depth)
    {
        var pile = _tab[col];
        if (depth < pile.Count)
        {
            if (!pile[depth].Up)
            {
                ClearSelection();
                return;
            }
            bool self = _selKind == 2 && _selCol == col && depth >= _selPos;
            if (_selKind != 0 && !self && TryMoveToColumn(col)) return;

            var run = RunFrom(col, depth);
            if (DestinationCount(run) == 1)
            {
                _selKind = 2;
                _selCol = col;
                _selPos = depth;
                MoveToSingleDestination();
                return;
            }
            ClearSelection();
            _selKind = 2;
            _selCol = col;
            _selPos = depth;
            _msg = "Tap a destination.";
            return;
        }

        if (pile.Count == 0 && depth == 0 && _selKind != 0 && TryMoveToColumn(col)) return;
        ClearSelection();
    }

    /// <summary>Auto-plays when exactly one destination exists, foundations first.</summary>
    private void MoveToSingleDestination()
    {
        var run = SelectedRun();
        if (run.Count == 1 && CanGoFoundation(run[0], run[0] / 13))
        {
            TakeSelection();
            _found[run[0] / 13]++;
            ClearSelection();
            FlipTops();
            _msg = "Parked.";
            CheckWin();
            return;
        }
        for (int col = 0; col < TabCols; col++)
            if (TryMoveToColumn(col))
            {
                FlipTops();
                return;
            }
        ClearSelection();
    }

    private bool TryMoveToColumn(int col)
    {
        var run = SelectedRun();
        if (run.Count == 0) return false;
        var pile = _tab[col];

        if (pile.Count == 0)
        {
            if (run[0] % 13 != 12) // only a King starts an empty column
            {
                _msg = "Only a King starts an empty column.";
                return false;
            }
        }
        else
        {
            int top = pile[^1].Card;
            if (run[0] % 13 != top % 13 - 1 || IsRed(run[0]) == IsRed(top))
            {
                _msg = "Tableau builds down in alternating colours.";
                return false;
            }
        }

        TakeSelection();
        _tab[col].AddRange(run.Select(c => new TCard(c, true)));
        ClearSelection();
        FlipTops();
        _msg = "Moved.";
        CheckWin();
        return true;
    }

    private int DestinationCount(List<int> run)
    {
        int n = 0;
        if (run.Count == 1 && CanGoFoundation(run[0], run[0] / 13)) n++;
        for (int col = 0; col < TabCols; col++)
        {
            var pile = _tab[col];
            if (pile.Count == 0)
            {
                if (run[0] % 13 == 12) n++;
            }
            else
            {
                int top = pile[^1].Card;
                if (run[0] % 13 == top % 13 - 1 && IsRed(run[0]) != IsRed(top)) n++;
            }
        }
        return n;
    }

    private bool CanGoFoundation(int card, int f) => card / 13 == f && card % 13 == _found[f];

    private List<int> SelectedRun()
    {
        if (_selKind == 1 && _waste.Count > 0) return new List<int> { _waste[^1] };
        if (_selKind == 2 && _selCol < TabCols)
        {
            var pile = _tab[_selCol];
            if (_selPos < pile.Count)
                return pile.Skip(_selPos).Select(t => t.Card).ToList();
        }
        return new List<int>();
    }

    private void TakeSelection()
    {
        if (_selKind == 1 && _waste.Count > 0)
        {
            _waste.RemoveAt(_waste.Count - 1);
            return;
        }
        if (_selKind == 2 && _selCol < TabCols && _selPos < _tab[_selCol].Count)
            _tab[_selCol].RemoveRange(_selPos, _tab[_selCol].Count - _selPos);
    }

    private List<int> RunFrom(int col, int depth) =>
        _tab[col].Skip(depth).Select(t => t.Card).ToList();

    private void ClearSelection() => _selKind = 0;

    private void FlipTops()
    {
        foreach (var pile in _tab)
            if (pile.Count > 0 && !pile[^1].Up)
                pile[^1] = pile[^1] with { Up = true };
    }

    private void CheckWin()
    {
        if (_found.Sum() >= CardCount)
        {
            _over = _won = true;
            ScoreCounts = true;
        }
    }

    private void Finish()
    {
        _over = true;
        if (_found.Sum() >= 13) ScoreCounts = true;
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

        var stock = _cells[StockCell];
        if (_stock.Count > 0)
        {
            stock.Text = "▓";
            stock.Background = Ink;
            stock.Foreground = Cell;
            stock.FontSize = 14;
        }
        else if (_waste.Count > 0)
        {
            stock.Text = "○"; // tapping recycles the waste
            stock.Foreground = Muted;
            stock.FontSize = 14;
        }
        stock.IsEnabled = !_over;

        if (_waste.Count > 0)
        {
            var top = _waste[^1];
            var w = _cells[WasteCell];
            w.Text = CardText(top);
            w.Foreground = IsRed(top) ? Bad : Ink;
            w.Background = Cell;
            w.IsEnabled = !_over;
            if (_selKind == 1)
            {
                w.BorderBrush = Accent;
                w.BorderThickness = new Avalonia.Thickness(2);
            }
        }

        for (int f = 0; f < 4; f++)
        {
            var c = _cells[2 + f];
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
            c.IsEnabled = !_over && _selKind != 0;
        }

        for (int i = 0; i < 2; i++)
        {
            var c = _cells[NewDealBase + i];
            c.FontSize = 10;
            c.Bold = true;
            c.Background = Cell;
            c.IsEnabled = true;
        }
        Spread(NewDealBase, "New deal", 2, 10);
        var end = _cells[EndCell];
        end.Text = "End";
        end.FontSize = 10;
        end.Bold = true;
        end.Background = !_over ? Cell : Board;
        end.IsEnabled = !_over;

        for (int col = 0; col < TabCols; col++)
        {
            var pile = _tab[col];
            int shown = Math.Min(pile.Count, TabRows);
            int skip = pile.Count - shown;
            for (int j = 0; j < shown; j++)
            {
                var tcard = pile[skip + j];
                var c = _cells[(TabTop + j) * Cols + col];
                if (tcard.Up)
                {
                    c.Text = CardText(tcard.Card);
                    c.Foreground = IsRed(tcard.Card) ? Bad : Ink;
                    c.Background = Cell;
                }
                else
                {
                    c.Text = "▓";
                    c.Background = Ink;
                    c.Foreground = Cell;
                }
                c.IsEnabled = !_over && tcard.Up;
                if (_selKind == 2 && _selCol == col && skip + j >= _selPos && tcard.Up)
                {
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(1);
                }
            }
            if (pile.Count == 0)
            {
                var c = _cells[TabTop * Cols + col];
                c.Text = "·";
                c.Foreground = Muted;
                c.IsEnabled = !_over && _selKind != 0;
            }
        }
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
