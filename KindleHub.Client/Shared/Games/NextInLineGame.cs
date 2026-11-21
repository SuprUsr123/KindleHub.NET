using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Next in Line, ported from the official client — five numbers follow a rule; work
/// out the rule and pick the number that comes next. The rule is explained after
/// each answer. Ten questions a run.
/// </summary>
public sealed class NextInLineGame : GameBase
{
    private const int Cols = 8;
    private const int ChoiceBase = 10;  // choices row, cols 2..5
    private const int NextCell = 15;    // choices row, col 7
    private const int Questions = 10;

    private static readonly int[] Primes =
        { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43 };

    private readonly List<GameCell> _cells = new();
    private int[] _seq = Array.Empty<int>();
    private int[] _choices = Array.Empty<int>();
    private int _answer;
    private int _correctIdx;
    private string _rule = "";
    private int _q;          // questions answered (cursor)
    private int _correct;
    private int _streak;
    private int _bestStreak;
    private bool _answered;  // waiting for the reveal tap
    private int _chosen;
    private bool _done;
    private string _message = "";

    public NextInLineGame()
    {
        for (int i = 0; i < Cols * 2; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "nextinline";
    public override string Name => "Next in Line";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _done ? 10 * _correct + 5 * _bestStreak : 0;
    public override string? ResultText => _done ? $"{_correct}/{Questions} correct." : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Finished — {_correct}/{Questions} correct.";
            if (_answered)
                return $"{_message} Tap → to continue.";
            return $"Q{_q + 1}/{Questions} · {_correct} right · streak {_streak} · what comes next?";
        }
    }

    public override void Reset()
    {
        _q = 0;
        _correct = 0;
        _streak = 0;
        _bestStreak = 0;
        _answered = false;
        _done = false;
        _message = "";
        ScoreCounts = false;
        LoadQuestion();
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
            // After the reveal, any choice or the arrow moves to the next question.
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
            _message = $"Correct — {_rule}.";
        }
        else
        {
            _streak = 0;
            _message = $"It was {_choices[_correctIdx]} — {_rule}.";
        }
        Redraw();
        return true;
    }

    private void Advance()
    {
        _q++;
        _answered = false;
        if (_q >= Questions)
        {
            _done = true;
            if (_correct > 0) ScoreCounts = true;
        }
        else
        {
            LoadQuestion();
        }
        Redraw();
    }

    private void LoadQuestion()
    {
        (_seq, _answer, _rule) = Generate();
        _choices = MakeChoices(_answer);
        _correctIdx = Array.IndexOf(_choices, _answer);
    }

    private static (int[] Seq, int Answer, string Rule) Generate()
    {
        var seq = new int[5];
        switch (Random.Shared.Next(6))
        {
            case 0:
            {
                int a = Random.Shared.Next(1, 10), k = Random.Shared.Next(2, 10);
                for (int i = 0; i < 5; i++) seq[i] = a + k * i;
                return (seq, a + 5 * k, $"the rule is add {k} each time");
            }
            case 1:
            {
                int a = Random.Shared.Next(1, 4), k = Random.Shared.Next(2, 4);
                for (int i = 0; i < 5; i++) seq[i] = a * (int)Math.Pow(k, i);
                return (seq, a * (int)Math.Pow(k, 5), $"the rule is multiply by {k} each time");
            }
            case 2:
            {
                int s = Random.Shared.Next(1, 10), a = Random.Shared.Next(1, 10), b = Random.Shared.Next(2, 10);
                if (b == a) b = a == 1 ? 2 : a - 1;
                seq[0] = s;
                for (int i = 1; i < 5; i++) seq[i] = seq[i - 1] + (i % 2 == 1 ? a : b);
                return (seq, seq[4] + a, $"the rule is add {a} and {b} alternately");
            }
            case 3:
            {
                int n0 = Random.Shared.Next(3, 8);
                for (int i = 0; i < 5; i++) seq[i] = (n0 + i) * (n0 + i);
                return (seq, (n0 + 5) * (n0 + 5), "the rule is square numbers in order");
            }
            case 4:
            {
                int a = Random.Shared.Next(1, 10), b = Random.Shared.Next(1, 10);
                seq[0] = a;
                seq[1] = b;
                for (int i = 2; i < 5; i++) seq[i] = seq[i - 1] + seq[i - 2];
                return (seq, seq[4] + seq[3], "each number is the sum of the two before it");
            }
            default:
            {
                int p0 = Random.Shared.Next(0, 9);
                for (int i = 0; i < 5; i++) seq[i] = Primes[p0 + i];
                return (seq, Primes[p0 + 5], "the rule is prime numbers in order");
            }
        }
    }

    /// <summary>The right answer plus three nearby, distinct, positive decoys.</summary>
    private static int[] MakeChoices(int answer)
    {
        var options = new List<int> { answer };
        int guard = 0;
        while (options.Count < 4 && guard++ < 100)
        {
            int delta = Random.Shared.Next(1, 4) * (Random.Shared.Next(2) == 0 ? 1 : -1);
            int v = answer + delta;
            if (v > 0 && !options.Contains(v)) options.Add(v);
        }
        while (options.Count < 4) options.Add(answer + options.Count + 3);
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
        return options.ToArray();
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
            SpreadRow(0, $"Done — {_correct}/{Questions} correct", Cols, 14, 3);
            return;
        }

        for (int i = 0; i < 5; i++)
        {
            var cell = _cells[2 + i];
            cell.Text = _seq[i].ToString();
            cell.FontSize = 20;
            cell.Bold = true;
            cell.Foreground = Ink;
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

    /// <summary>Splits text evenly across a row of cells so one row can carry a sentence.</summary>
    private void SpreadRow(int baseIndex, string text, int parts, int fontSize, int perCell)
    {
        text = text.Trim();
        if (text.Length > parts * perCell) text = Ellipsis(text, parts * perCell);
        int chunk = (text.Length + parts - 1) / parts;
        for (int i = 0; i < parts; i++)
        {
            int at = i * chunk;
            _cells[baseIndex + i].Text = at < text.Length
                ? text[at..Math.Min(at + chunk, text.Length)]
                : "";
            _cells[baseIndex + i].FontSize = fontSize;
        }
    }
}
