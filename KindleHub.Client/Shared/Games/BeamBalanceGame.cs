using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Balance — put every weight on the left or the right of the beam so both sides
/// come to the same total. Five levels with more weights each time, ported from
/// the official client's beam-balance puzzle.
/// </summary>
public sealed class BeamBalanceGame : GameBase
{
    private const int Cols = 9;
    private const int Rows = 4;
    private const int Levels = 5;
    private const int CheckCell = 3 * Cols + 2;
    private const int ResetCell = 3 * Cols + 4;
    private const int EndCell = 3 * Cols + 6;

    // Light tints of the palette blue/amber so a side assignment reads at a glance.
    private static readonly Avalonia.Media.IBrush LeftTint =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#dbe4f7"));
    private static readonly Avalonia.Media.IBrush RightTint =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f0e4cf"));

    private readonly List<GameCell> _cells = new();
    private readonly List<int> _weights = new();
    private readonly List<int> _sides = new();       // -1 shelf, 0 left, 1 right
    private readonly List<int> _weightCells = new(); // board cell per weight
    private readonly Stack<int> _history = new();    // weight indices, for undo
    private int _level = 1;
    private int _solvedLevels;
    private int _score;
    private bool _done;
    private string _message = "";
    private string _result = "";

    public BeamBalanceGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "beambalance";
    public override string Name => "Balance";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText => _done ? _result : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Done — {_score} points.";
            if (_message.Length > 0) return _message;
            (int left, int right) = Totals();
            return $"Level {_level}/{Levels} · L {left} R {right} · tap weights, then Check";
        }
    }

    public override void Reset()
    {
        _level = 1;
        _solvedLevels = 0;
        _score = 0;
        _done = false;
        _result = "";
        ScoreCounts = false;
        StartLevel();
    }

    private void StartLevel()
    {
        int count = 5 + _level;
        _weights.Clear();
        _sides.Clear();
        _weightCells.Clear();
        _history.Clear();
        _message = "";

        if (!Generate(count))
        {
            // Hand-built sets, one per weight count; each splits into equal sums.
            int[][] fallback =
            {
                new[] { 1, 2, 3, 4, 5, 9 },              // 1+2+9 = 3+4+5
                new[] { 1, 2, 3, 4, 5, 6, 7 },           // 7+6+1 = 2+3+4+5
                new[] { 1, 2, 3, 4, 5, 6, 7, 8 },        // 8+7+3 = 1+2+4+5+6
                new[] { 1, 2, 3, 4, 5, 6, 7, 8, 10 },    // 10+8+5 = 1+2+3+4+6+7
                new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 13 }, // 13+9+7 = the rest
            };
            foreach (int w in fallback[count - 6]) _weights.Add(w);
        }
        for (int i = 0; i < _weights.Count; i++) _sides.Add(-1);

        // Spread the weights over two centred rows under the beam.
        int top = (_weights.Count + 1) / 2;
        for (int i = 0; i < _weights.Count; i++)
        {
            bool firstRow = i < top;
            int k = firstRow ? top : _weights.Count - top;
            int offset = firstRow ? i : i - top;
            int row = firstRow ? 1 : 2;
            _weightCells.Add(row * Cols + (Cols - k) / 2 + offset);
        }
        Redraw();
    }

    /// <summary>
    /// Builds two groups with equal sums — group A random, group B random except
    /// its last value — so a solution (all of A left, all of B right) always exists.
    /// </summary>
    private bool Generate(int count)
    {
        int a = (count + 1) / 2, b = count / 2;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int sum = 0;
            var all = new List<int>();
            for (int i = 0; i < a; i++)
            {
                int v = Random.Shared.Next(1, 13);
                all.Add(v);
                sum += v;
            }
            int partial = 0;
            for (int i = 0; i < b - 1; i++)
            {
                int v = Random.Shared.Next(1, 13);
                all.Add(v);
                partial += v;
            }
            int last = sum - partial;
            if (last < 1 || last > 12) continue;
            all.Add(last);

            for (int i = all.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (all[i], all[j]) = (all[j], all[i]);
            }
            _weights.AddRange(all);
            return true;
        }
        return false;
    }

    private (int Left, int Right) Totals()
    {
        int left = 0, right = 0;
        for (int i = 0; i < _weights.Count; i++)
        {
            if (_sides[i] == 0) left += _weights[i];
            else if (_sides[i] == 1) right += _weights[i];
        }
        return (left, right);
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;

        int w = _weightCells.IndexOf(index);
        if (w >= 0)
        {
            _history.Push(w);
            _sides[w] = (_sides[w] + 2) % 3 - 1;   // shelf → left → right → shelf
            _message = "";
            Redraw();
            return true;
        }

        if (index == CheckCell) return Check();
        if (index == ResetCell)
        {
            for (int i = 0; i < _sides.Count; i++) _sides[i] = -1;
            _history.Clear();
            _message = "Weights back on the shelf.";
            Redraw();
            return true;
        }
        if (index == EndCell)
        {
            if (_solvedLevels >= 1) Finish("Ended early");
            else _message = "Solve at least one level first.";
            Redraw();
            return true;
        }
        return false;
    }

    public override bool OnKey(GameKey key)
    {
        if (_done) return false;
        return key == GameKey.Confirm && Check();
    }

    /// <summary>Backspace un-places weights one tap at a time.</summary>
    public override bool OnBackspace()
    {
        if (_done || _history.Count == 0) return false;
        int w = _history.Pop();
        _sides[w] = _sides[w] == 0 ? -1 : _sides[w] == 1 ? 0 : 1;
        _message = "";
        Redraw();
        return true;
    }

    private bool Check()
    {
        if (_done) return false;
        if (_sides.Any(s => s < 0))
        {
            _message = "Place every weight first.";
            Redraw();
            return true;
        }
        (int left, int right) = Totals();
        if (left != right)
        {
            _message = $"Off by {Math.Abs(left - right)} — L {left}, R {right}.";
            Redraw();
            return true;
        }

        _score += 100 * _level;
        _solvedLevels++;
        if (_level == Levels)
        {
            Finish("All levels balanced");
            return true;
        }
        _level++;
        StartLevel();
        return true;
    }

    private void Finish(string why)
    {
        _done = true;
        ScoreCounts = true;
        _result = $"{why} — {_score} points.";
        Redraw();
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
            c.FontSize = 18;
            c.Bold = false;
        }

        (int left, int right) = Totals();
        bool allPlaced = _sides.Count > 0 && _sides.All(s => s >= 0);
        bool balanced = allPlaced && left == right;

        _cells[0].Text = left.ToString();
        _cells[0].Background = balanced ? Good : Cell;
        _cells[0].Foreground = balanced ? Cell : Ink;
        _cells[0].FontSize = 22;
        _cells[0].Bold = true;
        _cells[8].Text = right.ToString();
        _cells[8].Background = balanced ? Good : Cell;
        _cells[8].Foreground = balanced ? Cell : Ink;
        _cells[8].FontSize = 22;
        _cells[8].Bold = true;

        // The beam itself, with the pivot in the middle.
        for (int i = 1; i <= 7; i++)
        {
            if (i == 4) continue;
            _cells[i].Text = "─";
            _cells[i].Foreground = Muted;
            _cells[i].FontSize = 20;
        }
        _cells[4].Text = "●";
        _cells[4].FontSize = 22;
        _cells[4].Bold = true;

        for (int i = 0; i < _weights.Count; i++)
        {
            var c = _cells[_weightCells[i]];
            c.Text = _weights[i].ToString();
            c.Background = _sides[i] < 0 ? Board : _sides[i] == 0 ? LeftTint : RightTint;
            c.Foreground = Ink;
            c.IsEnabled = !_done;
            c.FontSize = 22;
            c.Bold = true;
        }

        DrawButton(CheckCell, "Check ✓", !_done);
        DrawButton(ResetCell, "Reset", !_done);
        DrawButton(EndCell, "End", !_done && _solvedLevels >= 1);
    }

    private void DrawButton(int index, string text, bool enabled)
    {
        var c = _cells[index];
        c.Text = text;
        c.Background = Cell;
        c.Foreground = enabled ? Ink : Muted;
        c.IsEnabled = enabled;
        c.Bold = true;
        c.FontSize = 15;
    }
}
