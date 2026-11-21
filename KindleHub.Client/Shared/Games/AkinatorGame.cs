using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Mind Reader - the official client's Akinator-style mind reader, shrunk to a
/// number trick that needs no AI at all: think of a number from 1 to 100, answer
/// Yes / Higher / Lower, and a plain binary search names it within seven guesses.
/// Contradictory answers bust the read, just like thinking of someone unknowable.
/// </summary>
public sealed class AkinatorGame : GameBase
{
    private const int Wide = 9;
    private const int Rows = 4;

    // Fixed layout in the 9x4 grid.
    private const int GuessCell = 4;
    private const int AskedCell = 9;
    private const int LoCell = 12;
    private const int RangeCell = 13;
    private const int HiCell = 14;
    private const int YesCell = 21;
    private const int HigherCell = 22;
    private const int LowerCell = 23;
    private const int RestartCell = 31;

    private readonly List<GameCell> _cells = new();
    private int _lo = 1;
    private int _hi = 100;
    private int _guess = 50;
    private int _asked = 1;
    private bool _won;
    private bool _busted;

    public AkinatorGame()
    {
        for (int i = 0; i < Wide * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "akinator";
    public override string Name => "Mind Reader";
    public override int Columns => Wide;
    public override IReadOnlyList<GameCell> Cells => _cells;

    // Seven questions is the worst a binary search of 1..100 ever needs; each
    // one costs 100 points, the confirming Yes included.
    public override int Score => _won ? Math.Max(100, 700 - 100 * _asked) : 0;

    public override string? ResultText => _won
        ? $"Your number was {_guess} - read in {_asked} guesses."
        : _busted ? "You changed your mind!" : null;

    public override string StatusText
    {
        get
        {
            if (_won) return $"Read in {_asked} guesses · {Score} points. Restart for another.";
            if (_busted) return "The answers contradicted - Restart and think again.";
            return $"Think of 1..100 · guess {_asked} · range {_lo}-{_hi}";
        }
    }

    public override void Reset()
    {
        _lo = 1;
        _hi = 100;
        _guess = 50;
        _asked = 1;
        _won = false;
        _busted = false;
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnKey(GameKey key) => key switch
    {
        GameKey.Confirm => Answer(0),
        GameKey.Up => Answer(1),
        GameKey.Down => Answer(2),
        _ => false,
    };

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count) return false;
        if (index == RestartCell)
        {
            if (!_won && !_busted) return false;
            Reset();
            return true;
        }
        return index switch
        {
            YesCell => Answer(0),
            HigherCell => Answer(1),
            LowerCell => Answer(2),
            _ => false,
        };
    }

    /// <summary>0 = yes, 1 = the number is higher, 2 = the number is lower.</summary>
    private bool Answer(int kind)
    {
        if (_won || _busted) return false;
        if (kind == 0)
        {
            _won = true;
            ScoreCounts = true;
            Redraw();
            return true;
        }

        if (kind == 1) _lo = _guess + 1;
        else _hi = _guess - 1;

        if (_lo > _hi)
        {
            // The range inverted, so an earlier answer was a lie.
            _busted = true;
            Redraw();
            return true;
        }

        _guess = (_lo + _hi) / 2;
        _asked++;
        Redraw();
        return true;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 16;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        var guess = _cells[GuessCell];
        guess.Text = _busted ? "?" : _guess.ToString();
        guess.Background = _won ? Good : _busted ? Bad : Accent;
        guess.Foreground = Cell;
        guess.FontSize = 34;
        guess.Bold = true;

        _cells[AskedCell].Text = $"Q {_asked}";
        _cells[AskedCell].FontSize = 14;
        _cells[AskedCell].Foreground = Muted;
        _cells[LoCell].Text = _lo.ToString();
        _cells[LoCell].FontSize = 14;
        _cells[HiCell].Text = _hi.ToString();
        _cells[HiCell].FontSize = 14;
        _cells[RangeCell].Text = "..";
        _cells[RangeCell].Foreground = Muted;

        Button(YesCell, "Yes ✓", Good);
        Button(HigherCell, "Higher ↑", Accent);
        Button(LowerCell, "Lower ↓", Accent);

        if (_won || _busted)
        {
            var r = _cells[RestartCell];
            r.Text = "Restart";
            r.Background = Cell;
            r.Foreground = Ink;
            r.IsEnabled = true;
            r.FontSize = 14;
            r.Bold = true;
            r.BorderBrush = Accent;
            r.BorderThickness = new Avalonia.Thickness(2);
        }
    }

    private void Button(int index, string text, IBrush bg)
    {
        var c = _cells[index];
        c.Text = text;
        c.Background = bg;
        c.Foreground = Cell;
        c.IsEnabled = !_won && !_busted;
        c.FontSize = 14;
        c.Bold = true;
    }
}
