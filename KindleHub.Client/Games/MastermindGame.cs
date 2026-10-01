using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Mastermind — crack a hidden four-peg code from six colours.
///
/// The board is 10 rows of 4 guess pegs, a gutter, then two score pegs (exact,
/// then colour-but-wrong-place). Tapping a peg cycles it through empty → 1…6; the
/// row submits itself the moment the fourth peg is set, which is the same beat as
/// the web game and needs no extra button.
/// </summary>
public sealed class MastermindGame : GameBase
{
    public const int Pegs = 4;
    public const int Rows = 10;
    public const int Colours = 6;
    public const int BoardColumns = Pegs + 1 + 2; // 4 pegs + gutter + 2 score pegs

    private readonly List<GameCell> _cells = new();
    private readonly int[] _secret = new int[Pegs];
    private readonly List<Row> _history = new();
    private readonly int[] _guess = new int[Pegs];
    private bool _won;

    private readonly record struct Row(int[] Pegs, int Exact, int Loose);

    public MastermindGame()
    {
        for (int i = 0; i < Rows * BoardColumns; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "mastermind";
    public override string Name => "Mastermind";
    public override int Columns => BoardColumns;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public bool CanSubmit => !_won && _history.Count < Rows && _guess.All(v => v > 0);

    public override string? ResultText
    {
        get
        {
            if (_won) return "Cracked it!";
            return _history.Count >= Rows ? $"Out of guesses — the code was {string.Join("", _secret)}." : null;
        }
    }

    public override string StatusText
    {
        get
        {
            if (_won) return "Solved. New game deals a fresh code.";
            if (_history.Count >= Rows) return "Out of guesses. New game deals a fresh code.";
            var set = _guess.Count(v => v > 0);
            return $"Row {_history.Count + 1}/{Rows} · {set}/{Pegs} set · Submit when ready · green = right place, gold = right colour";
        }
    }

    public override void Reset()
    {
        for (int i = 0; i < Pegs; i++) _secret[i] = Random.Shared.Next(1, Colours + 1);
        _history.Clear();
        Array.Clear(_guess, 0, Pegs);
        _won = false;
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_won || _history.Count >= Rows || index < 0 || index >= _cells.Count) return false;
        int row = _history.Count;
        if (row != index / BoardColumns) return false;   // only the live row takes input
        int col = index % BoardColumns;
        if (col >= Pegs) return false;                   // gutter and score pegs are decorative

        // empty → 1…6 → empty
        _guess[col] = _guess[col] >= Colours ? 0 : _guess[col] + 1;

        Redraw();
        return true;
    }

    public override bool OnKey(GameKey key)
    {
        if (key != GameKey.Confirm || !CanSubmit) return false;
        Commit();
        return true;
    }

    private void Commit()
    {
        int exact = 0;
        var consumed = new bool[Pegs];
        for (int i = 0; i < Pegs; i++)
            if (_guess[i] == _secret[i]) { exact++; consumed[i] = true; }

        int loose = 0;
        for (int i = 0; i < Pegs; i++)
        {
            if (consumed[i]) continue;
            for (int j = 0; j < Pegs; j++)
            {
                if (consumed[j] || _guess[i] != _secret[j]) continue;
                consumed[j] = true;
                loose++;
                break;
            }
        }

        _history.Add(new Row((int[])_guess.Clone(), exact, loose));
        Array.Clear(_guess, 0, Pegs);

        if (exact == Pegs) { _won = true; ScoreCounts = true; }
        Redraw();
    }

    public override void Redraw()
    {
        int liveRow = _history.Count;
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            int row = i / BoardColumns;
            int col = i % BoardColumns;
            bool live = row == liveRow && !_won && liveRow < Rows;

            c.Bold = false;
            c.FontSize = 18;

            if (col < Pegs)
            {
                int v = row < liveRow ? _history[row].Pegs[col] : live ? _guess[col] : 0;
                c.Text = v == 0 ? "" : v.ToString();
                c.Background = v == 0 ? (live ? Board : Cell) : Tiles4[v - 1];
                c.Foreground = Ink;
                c.BorderBrush = live ? Accent : Board;
                c.BorderThickness = live ? new Avalonia.Thickness(2) : new Avalonia.Thickness(0);
                c.IsEnabled = live;
                c.Bold = v != 0;
                continue;
            }

            if (col == Pegs) { c.Text = "│"; c.Background = Board; c.IsEnabled = false; c.Foreground = Muted; c.FontSize = 22; continue; }

            bool exactPeg = col == BoardColumns - 2;
            int n = row < liveRow ? (exactPeg ? _history[row].Exact : _history[row].Loose) : 0;
            c.Text = n == 0 ? "" : n.ToString();
            c.Background = n > 0 ? (exactPeg ? Good : Warn) : Board;
            c.Foreground = Cell;
            c.BorderBrush = exactPeg ? Good : Warn;
            c.BorderThickness = new Avalonia.Thickness(1);
            c.FontSize = 15;
            c.IsEnabled = false;
        }
    }
}
