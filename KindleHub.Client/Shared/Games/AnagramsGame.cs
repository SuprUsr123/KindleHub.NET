using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Anagrams, ported from the official client — unscramble the jumbled letters into the
/// word that fits the clue. Tap a letter tile to move it into the answer, tap the
/// answer to take it back; ten rounds a run.
/// </summary>
public sealed class AnagramsGame : GameBase
{
    private const int Cols = 10;
    private const int TilesRow = 1;
    private const int SlotsRow = 2;
    private const int ClearCell = 33;   // buttons row, col 3
    private const int SubmitCell = 34;  // buttons row, col 4
    private const int SkipCell = 36;    // buttons row, col 6
    private const int Rounds = 10;

    // Common 4-7 letter words, each with a one-line clue.
    private static readonly (string Word, string Clue)[] Bank =
    {
        ("GARDEN", "where flowers grow"),
        ("PLANET", "it orbits a star"),
        ("TIGER", "big striped cat"),
        ("BRIDGE", "it crosses a river"),
        ("WINTER", "the coldest season"),
        ("PUZZLE", "a problem to solve"),
        ("CASTLE", "a royal fortress"),
        ("DOLPHIN", "clever sea mammal"),
        ("GUITAR", "six strings to strum"),
        ("BREAD", "baked from flour"),
        ("RIVER", "it flows to the sea"),
        ("CANDLE", "a stick of burning wax"),
        ("FOREST", "dense stand of trees"),
        ("ENGINE", "it powers a car"),
        ("SILVER", "shiny grey metal"),
        ("DOCTOR", "treats the sick"),
        ("SUMMER", "the warmest season"),
        ("ISLAND", "land in the sea"),
        ("THUNDER", "sound after lightning"),
        ("ORANGE", "a citrus colour"),
        ("PIANO", "keys make the music"),
        ("CAMERA", "it takes photos"),
        ("HONEY", "made by bees"),
        ("SHADOW", "follows you in sun"),
        ("BOTTLE", "it holds a drink"),
        ("COMET", "icy space wanderer"),
        ("DESERT", "very dry land"),
        ("EAGLE", "powerful bird of prey"),
        ("LADDER", "it helps you climb"),
        ("MARKET", "where goods are sold"),
        ("NIGHT", "opposite of day"),
        ("OCTOPUS", "eight-armed sea animal"),
        ("PENCIL", "you write with it"),
        ("ROCKET", "it flies to space"),
        ("SALT", "it seasons your food"),
        ("TRAIN", "it runs on rails"),
        ("WINDOW", "glass in a wall"),
        ("ZEBRA", "striped African horse"),
        ("CHEESE", "made from milk"),
        ("FROZEN", "turned to ice"),
        ("GLOBE", "a model of Earth"),
        ("HELMET", "it protects your head"),
        ("JUNGLE", "thick tropical forest"),
        ("KETTLE", "it boils water"),
        ("MEADOW", "a field of grass"),
        ("NEEDLE", "used for sewing"),
        ("OYSTER", "it can hide a pearl"),
        ("PILOT", "flies the plane"),
        ("QUEEN", "a royal woman"),
        ("SPIDER", "it spins a web"),
    };

    private readonly List<GameCell> _cells = new();
    private readonly List<(string Word, string Clue)> _rounds = new();
    private readonly char[] _tiles = new char[7];
    private readonly bool[] _used = new bool[7];
    private readonly char[] _answer = new char[7];
    private readonly int[] _from = new int[7];
    private int _place;   // rounds answered so far (also the cursor)
    private int _solved;
    private int _streak;
    private int _bestStreak;
    private bool _done;
    private string _message = "";
    private string _word = "";
    private string _clue = "";

    public AnagramsGame()
    {
        for (int i = 0; i < Cols * 4; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "anagrams";
    public override string Name => "Anagrams";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _done ? 25 * _solved + 10 * _bestStreak : 0;
    public override string? ResultText => _done ? $"{_solved} of {Rounds} solved." : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Finished — {_solved}/{Rounds} solved.";
            if (!string.IsNullOrEmpty(_message)) return _message;
            return $"Round {_place + 1}/{Rounds} · {_solved} solved · streak {_streak}";
        }
    }

    public override void Reset()
    {
        _rounds.Clear();
        foreach (var i in Enumerable.Range(0, Bank.Length).OrderBy(_ => Random.Shared.Next()).Take(Rounds))
            _rounds.Add(Bank[i]);
        _place = 0;
        _solved = 0;
        _streak = 0;
        _bestStreak = 0;
        _done = false;
        _message = "";
        ScoreCounts = false;
        LoadRound();
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (key == GameKey.Confirm) return Submit();
        if (key == GameKey.Backspace) return OnBackspace();
        return base.OnKey(key);
    }

    public override bool OnLetter(char letter)
    {
        if (_done) return false;
        letter = char.ToUpperInvariant(letter);
        int len = _word.Length;
        for (int t = 0; t < len; t++)
        {
            if (_used[t] || _tiles[t] != letter) continue;
            int slot = Array.IndexOf(_answer, '\0', 0, len);
            if (slot < 0) return false;
            _used[t] = true;
            _answer[slot] = letter;
            _from[slot] = t;
            _message = "";
            Redraw();
            return true;
        }
        return false;
    }

    public override bool OnBackspace()
    {
        if (_done) return false;
        for (int s = _word.Length - 1; s >= 0; s--)
        {
            if (_answer[s] == '\0') continue;
            _used[_from[s]] = false;
            _answer[s] = '\0';
            _message = "";
            Redraw();
            return true;
        }
        return false;
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        if (index == SubmitCell) return Submit();
        if (index == SkipCell)
        {
            _streak = 0;
            _message = $"It was {_word}.";
            NextRound();
            Redraw();
            return true;
        }
        if (index == ClearCell)
        {
            for (int i = 0; i < _word.Length; i++) _used[i] = false;
            Array.Clear(_answer, 0, _word.Length);
            _message = "";
            Redraw();
            return true;
        }

        int len = _word.Length, start = (Cols - len) / 2;
        if (index >= TilesRow * Cols && index < TilesRow * Cols + Cols)
        {
            int t = index - TilesRow * Cols - start;
            if (t < 0 || t >= len || _used[t]) return false;
            int slot = Array.IndexOf(_answer, '\0', 0, len);
            if (slot < 0) return true;
            _used[t] = true;
            _answer[slot] = _tiles[t];
            _from[slot] = t;
            _message = "";
            Redraw();
            return true;
        }
        if (index >= SlotsRow * Cols && index < SlotsRow * Cols + Cols)
        {
            int s = index - SlotsRow * Cols - start;
            if (s < 0 || s >= len || _answer[s] == '\0') return false;
            _used[_from[s]] = false;
            _answer[s] = '\0';
            _message = "";
            Redraw();
            return true;
        }
        return false;
    }

    private bool Submit()
    {
        if (_done) return false;
        int len = _word.Length;
        if (Array.IndexOf(_answer, '\0', 0, len) >= 0)
        {
            _message = "Use every letter first.";
            Redraw();
            return true;
        }
        if (new string(_answer, 0, len) == _word)
        {
            _solved++;
            _streak++;
            _bestStreak = Math.Max(_bestStreak, _streak);
            _message = "Correct!";
            NextRound();
        }
        else
        {
            _message = "Not quite — tap a letter in the answer to take it back.";
        }
        Redraw();
        return true;
    }

    private void NextRound()
    {
        _place++;
        if (_place >= Rounds)
        {
            _done = true;
            // A run only posts when at least one word was actually solved.
            if (_solved > 0) ScoreCounts = true;
        }
        else
        {
            LoadRound();
        }
    }

    private void LoadRound()
    {
        _word = _rounds[_place].Word;
        _clue = _rounds[_place].Clue;
        var letters = _word.ToCharArray();
        int guard = 0;
        // Reshuffle until it actually looks scrambled, but never spin forever.
        do
        {
            for (int i = letters.Length - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (letters[i], letters[j]) = (letters[j], letters[i]);
            }
        } while (new string(letters) == _word && guard++ < 20);

        for (int i = 0; i < 7; i++)
        {
            _tiles[i] = i < letters.Length ? letters[i] : '\0';
            _used[i] = false;
            _answer[i] = '\0';
        }
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

        SpreadRow(0, _done ? $"Done — {_solved}/{Rounds} solved" : _clue, Cols, 12, 3);
        _cells[0].Foreground = Muted;

        int len = _word.Length, start = (Cols - len) / 2;
        for (int i = 0; i < len; i++)
        {
            var tile = _cells[TilesRow * Cols + start + i];
            if (!_used[i])
            {
                tile.Text = _tiles[i].ToString();
                tile.Background = Cell;
                tile.Bold = true;
                tile.FontSize = 22;
                tile.IsEnabled = !_done;
            }

            var slot = _cells[SlotsRow * Cols + start + i];
            if (_answer[i] != '\0')
            {
                slot.Text = _answer[i].ToString();
                slot.Background = Accent;
                slot.Foreground = Cell;
                slot.Bold = true;
                slot.FontSize = 22;
                slot.IsEnabled = !_done;
            }
            else
            {
                slot.Background = Cell;
                slot.IsEnabled = !_done;
            }
        }

        if (_done) return;
        var clear = _cells[ClearCell];
        clear.Text = "✕";
        clear.Background = Cell;
        clear.FontSize = 15;
        clear.IsEnabled = true;
        var submit = _cells[SubmitCell];
        submit.Text = "✔";
        submit.Background = Cell;
        submit.FontSize = 18;
        submit.Bold = true;
        submit.IsEnabled = true;
        var skip = _cells[SkipCell];
        skip.Text = "SKIP";
        skip.Background = Cell;
        skip.FontSize = 11;
        skip.IsEnabled = true;
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
