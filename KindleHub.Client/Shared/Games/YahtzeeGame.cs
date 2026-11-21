using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Yahtzee, ported from the official client — roll five dice up to three times a
/// turn, tapping dice to hold them between rolls, then score one of the thirteen
/// categories. Upper section 63+ earns the 35-point bonus and extra Yahtzees add 100.
/// </summary>
public sealed class YahtzeeGame : GameBase
{
    private const int Cols = 10;
    private const int CatCount = 13;
    private const int CatTop = 2;               // first scorecard row
    private const int TotalRow = CatTop + CatCount;
    private const int Rows = TotalRow + 1;

    private const int RollCell = 5;

    private static readonly string[] CatNames =
    {
        "Ones", "Twos", "Threes", "Fours", "Fives", "Sixes",
        "3 of a Kind", "4 of a Kind", "Full House", "Sm Straight", "Lg Straight",
        "Yahtzee", "Chance",
    };

    private readonly List<GameCell> _cells = new();
    private readonly int[] _dice = new int[5];
    private readonly bool[] _held = new bool[5];
    private readonly int[] _scores = new int[CatCount]; // -1 = unscored
    private int _rollsLeft = 3;
    private bool _rolled;
    private int _yahtzeeBonus;
    private bool _over;
    private string _msg = "Tap Roll to throw.";

    public YahtzeeGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "yahtzee";
    public override string Name => "Yahtzee";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _over ? Math.Max(1, GrandTotal()) : 0;

    public override string? ResultText => _over ? $"Game over — {GrandTotal()} points." : null;

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            if (!_rolled) return "Tap Roll to throw the dice.";
            if (_rollsLeft == 0) return "Pick a category to score it.";
            return $"Rolls left {_rollsLeft} · tap dice to hold · {_msg}";
        }
    }

    public override void Reset()
    {
        Array.Fill(_dice, 1);
        Array.Clear(_held, 0, _held.Length);
        Array.Fill(_scores, -1);
        _rollsLeft = 3;
        _rolled = false;
        _yahtzeeBonus = 0;
        _over = false;
        ScoreCounts = false;
        _msg = "Tap Roll to throw.";
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count || _over) return false;

        if (index < 5)
        {
            if (!_rolled || _rollsLeft is 0 or 3) return false;
            _held[index] = !_held[index];
            Redraw();
            return true;
        }

        if (index == RollCell)
        {
            Roll();
            return true;
        }

        int row = index / Cols;
        if (row < CatTop || row >= TotalRow) return false;
        int k = row - CatTop;
        if (index % Cols > 7) return false;
        ScoreCategory(k);
        return true;
    }

    public override bool OnKey(GameKey key)
    {
        if (key != GameKey.Confirm || _over || _rollsLeft == 0) return false;
        Roll();
        return true;
    }

    private void Roll()
    {
        if (_rollsLeft == 0) return;
        if (_rollsLeft == 3)
        {
            Array.Clear(_held, 0, _held.Length);
            for (int i = 0; i < 5; i++) _dice[i] = Random.Shared.Next(1, 7);
        }
        else
        {
            for (int i = 0; i < 5; i++)
                if (!_held[i]) _dice[i] = Random.Shared.Next(1, 7);
        }
        _rolled = true;
        _rollsLeft--;

        _msg = "Tap dice to hold, then roll again.";
        if (IsYahtzee(_dice) && _scores[11] == 50)
        {
            _yahtzeeBonus += 100;
            _msg = "Extra Yahtzee! +100";
        }
        Redraw();
    }

    private void ScoreCategory(int k)
    {
        if (!_rolled || _scores[k] >= 0) return;
        _scores[k] = CategoryScore(k, _dice);
        _msg = $"{CatNames[k]} scored {_scores[k]}.";
        _rollsLeft = 3;
        _rolled = false;
        Array.Clear(_held, 0, _held.Length);
        if (_scores.All(s => s >= 0))
        {
            _over = true;
            ScoreCounts = true;
        }
        Redraw();
    }

    private static bool IsYahtzee(int[] dice) => dice.All(d => d == dice[0]);

    private static int CategoryScore(int k, int[] dice)
    {
        var counts = new int[7];
        int sum = 0;
        foreach (var d in dice)
        {
            counts[d]++;
            sum += d;
        }
        bool hasN(int n) => counts.Skip(1).Any(c => c >= n);

        return k switch
        {
            <= 5 => counts[k + 1] * (k + 1),
            6 => hasN(3) ? sum : 0,               // 3 of a kind
            7 => hasN(4) ? sum : 0,               // 4 of a kind
            8 => IsFullHouse(counts) ? 25 : 0,
            9 => HasStraight(counts, 4) ? 30 : 0, // small straight
            10 => HasStraight(counts, 5) ? 40 : 0,// large straight
            11 => hasN(5) ? 50 : 0,               // Yahtzee
            _ => sum,                             // Chance
        };
    }

    private static bool IsFullHouse(int[] counts)
    {
        var used = counts.Skip(1).Where(c => c > 0).OrderByDescending(c => c).ToList();
        return used.Count == 2 && used[0] == 3 && used[1] == 2;
    }

    private static bool HasStraight(int[] counts, int length)
    {
        int run = 0;
        for (int f = 1; f <= 6; f++)
        {
            run = counts[f] > 0 ? run + 1 : 0;
            if (run >= length) return true;
        }
        return false;
    }

    private int UpperTotal()
    {
        int sum = 0;
        for (int k = 0; k < 6; k++)
            if (_scores[k] >= 0) sum += _scores[k];
        return sum;
    }

    private int GrandTotal()
    {
        int sum = _scores.Sum(s => Math.Max(0, s));
        bool upperDone = Enumerable.Range(0, 6).All(k => _scores[k] >= 0);
        if (upperDone && UpperTotal() >= 63) sum += 35;
        return sum + _yahtzeeBonus;
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
            c.FontSize = 11;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        for (int i = 0; i < 5; i++)
        {
            var c = _cells[i];
            c.Text = _rolled ? _dice[i].ToString() : "·";
            c.FontSize = 22;
            c.Bold = true;
            bool holdable = !_over && _rolled && _rollsLeft > 0 && _rollsLeft < 3;
            c.Background = _held[i] ? Tiles4[1] : Cell;
            c.IsEnabled = holdable;
            if (_held[i])
            {
                c.BorderBrush = Accent;
                c.BorderThickness = new Avalonia.Thickness(2);
            }
        }

        var roll = _cells[RollCell];
        roll.Text = "Roll ⚂";
        roll.FontSize = 12;
        roll.Bold = true;
        roll.Background = !_over && _rollsLeft > 0 ? Cell : Board;
        roll.IsEnabled = !_over && _rollsLeft > 0;
        var rolls = _cells[RollCell + 1];
        rolls.Text = _rollsLeft > 0 ? $"x{_rollsLeft}" : "";
        rolls.FontSize = 12;
        rolls.Bold = true;
        rolls.Foreground = Muted;

        for (int k = 0; k < CatCount; k++)
        {
            int row = CatTop + k;
            bool scoreable = !_over && _rolled && _scores[k] < 0;
            for (int col = 0; col <= 7; col++)
            {
                var c = _cells[row * Cols + col];
                c.Background = scoreable ? Cell : Board;
                c.IsEnabled = scoreable;
            }
            Spread(row * Cols, CatNames[k], 6, 11);
            var value = _cells[row * Cols + 6];
            value.Text = _scores[k] >= 0 ? _scores[k].ToString() : "";
            value.FontSize = 13;
            value.Bold = true;
            var tick = _cells[row * Cols + 7];
            tick.Text = _scores[k] >= 0 ? "✓" : "";
            tick.FontSize = 12;
            tick.Foreground = Good;
            tick.Bold = true;
        }

        bool upperDone = Enumerable.Range(0, 6).All(k => _scores[k] >= 0);
        for (int col = 0; col <= 7; col++)
        {
            var c = _cells[TotalRow * Cols + col];
            c.Background = Board;
            c.Bold = true;
        }
        Spread(TotalRow * Cols,
            upperDone && UpperTotal() >= 63 ? "TOTAL +35 bonus" : "TOTAL",
            6, 12);
        var total = _cells[TotalRow * Cols + 6];
        total.Text = GrandTotal().ToString();
        total.FontSize = 14;
        total.Bold = true;
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
