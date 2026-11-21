using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Quick Count, ported from the official client — how many marks are on screen? Pick
/// the right number from four; the grids get denser as you go. Ten rounds a run.
/// </summary>
public sealed class QuickCountGame : GameBase
{
    private const int Cols = 8;
    private const int Side = 6;         // counting grid is 6x6, sitting at cols 1..6
    private const int ChoiceBase = 50;  // row 6, cols 2..5
    private const int NextCell = 55;    // row 6, col 7
    private const int Rounds = 10;

    private readonly List<GameCell> _cells = new();
    private readonly bool[] _dots = new bool[Side * Side];
    private int[] _choices = Array.Empty<int>();
    private int _count;
    private int _correctIdx;
    private int _place;
    private int _correct;
    private int _streak;
    private int _bestStreak;
    private bool _answered;
    private int _chosen;
    private bool _done;

    public QuickCountGame()
    {
        for (int i = 0; i < Cols * 7; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "quickcount";
    public override string Name => "Quick Count";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _done ? 10 * _correct + 5 * _bestStreak : 0;
    public override string? ResultText => _done ? $"{_correct}/{Rounds} correct." : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Finished — {_correct}/{Rounds} correct.";
            if (_answered) return "Tap → to continue.";
            return $"Round {_place + 1}/{Rounds} · {_correct} right · how many dots?";
        }
    }

    public override void Reset()
    {
        _place = 0;
        _correct = 0;
        _streak = 0;
        _bestStreak = 0;
        _answered = false;
        _done = false;
        ScoreCounts = false;
        LoadRound();
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (key == GameKey.Confirm && _answered && !_done)
        {
            Advance();
            return true;
        }
        return base.OnKey(key);
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        bool isChoice = index >= ChoiceBase && index < ChoiceBase + 4;

        if (_answered)
        {
            if (isChoice || index == NextCell)
            {
                Advance();
                return true;
            }
            return false;
        }
        if (!isChoice) return false;

        int choice = index - ChoiceBase;
        _answered = true;
        _chosen = choice;
        if (choice == _correctIdx)
        {
            _correct++;
            _streak++;
            _bestStreak = Math.Max(_bestStreak, _streak);
        }
        else
        {
            _streak = 0;
        }
        Redraw();
        return true;
    }

    private void Advance()
    {
        _place++;
        _answered = false;
        if (_place >= Rounds)
        {
            _done = true;
            if (_correct > 0) ScoreCounts = true;
        }
        else
        {
            LoadRound();
        }
        Redraw();
    }

    private void LoadRound()
    {
        // Denser every round: from 4 marks up to 24 across the ten rounds.
        _count = 4 + 20 * _place / 9;
        Array.Clear(_dots, 0, _dots.Length);
        foreach (var i in Enumerable.Range(0, _dots.Length)
                     .OrderBy(_ => Random.Shared.Next()).Take(_count))
            _dots[i] = true;

        var options = new List<int> { _count };
        int guard = 0;
        while (options.Count < 4 && guard++ < 100)
        {
            int delta = Random.Shared.Next(1, 4) * (Random.Shared.Next(2) == 0 ? 1 : -1);
            int v = _count + delta;
            if (v > 0 && !options.Contains(v)) options.Add(v);
        }
        while (options.Count < 4) options.Add(_count + options.Count + 3);
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
        _choices = options.ToArray();
        _correctIdx = Array.IndexOf(_choices, _count);
    }

    public override void Redraw()
    {
        foreach (var c in _cells)
        {
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.Bold = false;
            c.FontSize = 16;
            c.Span = 1;
        }

        if (_done)
        {
            _cells[0].Text = $"Done — {_correct}/{Rounds}";
            _cells[0].FontSize = 14;
            _cells[0].Foreground = Muted;
            return;
        }

        for (int r = 0; r < Side; r++)
        {
            for (int col = 0; col < Side; col++)
            {
                if (!_dots[r * Side + col]) continue;
                var cell = _cells[r * Cols + 1 + col];
                cell.Text = "●";
                cell.FontSize = 18;
                cell.Foreground = Ink;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            var cell = _cells[ChoiceBase + i];
            cell.Text = _choices[i].ToString();
            cell.FontSize = 18;
            cell.Bold = true;
            if (_answered)
            {
                if (i == _correctIdx)
                {
                    cell.Background = Good;
                    cell.Foreground = Cell;
                }
                else if (i == _chosen)
                {
                    cell.Background = Bad;
                    cell.Foreground = Cell;
                }
                else
                {
                    cell.Foreground = Muted;
                }
            }
            else
            {
                cell.Background = Cell;
            }
        }

        if (_answered)
        {
            var next = _cells[NextCell];
            next.Text = "→";
            next.Background = Cell;
            next.FontSize = 20;
            next.Bold = true;
            next.IsEnabled = true;
        }
    }
}
