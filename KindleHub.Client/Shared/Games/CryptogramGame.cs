using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Cryptogram, ported from the official client — a famous quote with every letter
/// swapped for another. Tap a cipher letter, then a letter from the A–Z row to decode
/// it; solve every letter to finish.
/// </summary>
public sealed class CryptogramGame : GameBase
{
    private const int Cols = 12;
    private const int QuoteRows = 5;
    private const int AlphaBase = 60;  // A..Z run contiguously from here (26 cells)
    private const int HintCell = 86;
    private const int ClearCell = 87;

    // Genuinely famous, kept short (<= 60 chars) and apostrophe-free.
    private static readonly string[] Quotes =
    {
        "TO BE OR NOT TO BE",
        "I CAME, I SAW, I CONQUERED",
        "E PLURIBUS UNUM",
        "BREVITY IS THE SOUL OF WIT",
        "A JOURNEY OF A THOUSAND MILES BEGINS WITH A SINGLE STEP",
        "ALL THAT GLITTERS IS NOT GOLD",
        "THE ONLY THING WE HAVE TO FEAR IS FEAR ITSELF",
        "ASK NOT WHAT YOUR COUNTRY CAN DO FOR YOU",
        "THAT IS ONE SMALL STEP FOR MAN",
        "LIBERTY, EQUALITY, FRATERNITY",
    };

    private readonly List<GameCell> _cells = new();
    private readonly List<string> _rows = new();       // wrapped quote rows (<= 12 chars)
    private readonly Dictionary<char, char> _map = new();   // cipher -> plain guess
    private readonly Dictionary<char, char> _plainOf = new(); // cipher -> truth
    private char[] _perm = new char[26];               // plain index -> cipher letter
    private string _quote = "";
    private char _selected;                            // picked cipher letter, or '\0'
    private int _hints;
    private int _distinct;
    private bool _solved;
    private string _message = "";

    public CryptogramGame()
    {
        for (int i = 0; i < Cols * 8; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "cryptogram";
    public override string Name => "Cryptogram";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _solved ? Math.Max(50, 300 + 5 * _distinct - 50 * _hints) : 0;
    public override string? ResultText => _solved
        ? (_hints == 0 ? "Decoded with no hints." : $"Decoded with {_hints} hint(s).")
        : null;

    public override string StatusText
    {
        get
        {
            if (_solved) return $"Solved! {_quote}";
            if (!string.IsNullOrEmpty(_message)) return _message;
            if (_selected != '\0') return $"Cipher {_selected} picked — tap a letter below (or type it).";
            return $"{MappedCount()}/{_distinct} letters placed · {_hints} hints · tap a cipher letter";
        }
    }

    public override void Reset()
    {
        _quote = Quotes[Random.Shared.Next(Quotes.Length)];
        _map.Clear();
        _plainOf.Clear();
        _selected = '\0';
        _hints = 0;
        _solved = false;
        _message = "";
        ScoreCounts = false;

        // A shuffled alphabet with no letter left in place, so no cipher equals its plain.
        int guard = 0;
        do
        {
            _perm = Enumerable.Range(0, 26).Select(i => (char)('A' + i)).ToArray();
            for (int i = 25; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (_perm[i], _perm[j]) = (_perm[j], _perm[i]);
            }
        } while (Enumerable.Range(0, 26).Any(i => _perm[i] == 'A' + i) && guard++ < 40);

        for (int i = 0; i < 26; i++) _plainOf[_perm[i]] = (char)('A' + i);
        _distinct = _quote.Where(char.IsLetter).Distinct().Count();

        // Word-wrap the quote into rows of at most 12 display cells.
        _rows.Clear();
        string current = "";
        foreach (var token in _quote.Split(' '))
        {
            if (current.Length == 0) current = token;
            else if (current.Length + 1 + token.Length <= Cols) current += " " + token;
            else
            {
                _rows.Add(current);
                current = token;
            }
        }
        if (current.Length > 0) _rows.Add(current);

        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (key == GameKey.Backspace) return OnBackspace();
        return base.OnKey(key);
    }

    public override bool OnLetter(char letter)
    {
        if (_solved || _selected == '\0') return false;
        letter = char.ToUpperInvariant(letter);
        if (letter < 'A' || letter > 'Z') return false;
        Assign(letter);
        return true;
    }

    public override bool OnBackspace()
    {
        if (_solved || _selected == '\0') return false;
        _map.Remove(_selected);
        _message = "";
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_solved || index < 0 || index >= _cells.Count) return false;

        if (index == HintCell)
        {
            Hint();
            return true;
        }
        if (index == ClearCell)
        {
            _map.Clear();
            _selected = '\0';
            _message = "All guesses cleared.";
            Redraw();
            return true;
        }
        if (index >= AlphaBase && index < AlphaBase + 26)
        {
            if (_selected == '\0') return false;
            Assign((char)('A' + index - AlphaBase));
            return true;
        }
        if (index >= 0 && index < QuoteRows * Cols)
        {
            var line = index / Cols < _rows.Count ? _rows[index / Cols] : "";
            int col = index % Cols;
            if (col >= line.Length) return false;
            char ch = line[col];
            if (!char.IsLetter(ch)) return false;
            _selected = _perm[ch - 'A'];
            _message = "";
            Redraw();
            return true;
        }
        return false;
    }

    private void Assign(char plain)
    {
        // Reassigning a plain letter frees it from whichever cipher had it.
        foreach (var kvp in _map.ToArray())
            if (kvp.Value == plain) _map.Remove(kvp.Key);
        _map[_selected] = plain;
        _selected = '\0';
        _message = "";
        CheckSolved();
        Redraw();
    }

    private void Hint()
    {
        char? target = null;
        foreach (var ch in _quote)
        {
            if (!char.IsLetter(ch)) continue;
            char cipher = _perm[ch - 'A'];
            if (!_map.ContainsKey(cipher)) { target = cipher; break; }
        }
        if (target == null)
        {
            foreach (var ch in _quote)
            {
                if (!char.IsLetter(ch)) continue;
                char cipher = _perm[ch - 'A'];
                if (_map.TryGetValue(cipher, out var p) && p != ch) { target = cipher; break; }
            }
        }
        if (target == null) return;

        _hints++;
        char plain = _plainOf[target.Value];
        foreach (var kvp in _map.ToArray())
            if (kvp.Value == plain) _map.Remove(kvp.Key);
        _map[target.Value] = plain;
        _selected = '\0';
        _message = "";
        CheckSolved();
        Redraw();
    }

    private void CheckSolved()
    {
        foreach (var ch in _quote)
        {
            if (!char.IsLetter(ch)) continue;
            char cipher = _perm[ch - 'A'];
            if (!_map.TryGetValue(cipher, out var p) || p != ch) return;
        }
        _solved = true;
        ScoreCounts = true;
    }

    private int MappedCount() => _quote.Where(char.IsLetter)
        .Select(ch => _perm[ch - 'A'])
        .Distinct()
        .Count(c => _map.ContainsKey(c));

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

        for (int r = 0; r < QuoteRows; r++)
        {
            var line = r < _rows.Count ? _rows[r] : "";
            for (int col = 0; col < Cols; col++)
            {
                var cell = _cells[r * Cols + col];
                if (col >= line.Length) continue;
                char ch = line[col];
                if (char.IsLetter(ch))
                {
                    char cipher = _perm[ch - 'A'];
                    bool mapped = _map.TryGetValue(cipher, out char plain);
                    cell.Text = mapped ? plain.ToString() : cipher.ToString();
                    cell.FontSize = 20;
                    cell.Bold = mapped;
                    if (_selected == cipher && !_solved)
                    {
                        cell.Background = Accent;
                        cell.Foreground = Cell;
                    }
                    else if (mapped)
                    {
                        // A decoded letter shows in Good so progress stands out.
                        cell.Background = Cell;
                        cell.Foreground = Good;
                    }
                    else
                    {
                        cell.Background = Board;
                        cell.Foreground = Muted;
                    }
                    cell.IsEnabled = !_solved;
                }
                else if (ch != ' ')
                {
                    // Punctuation is not encrypted — show it, but keep it untappable.
                    cell.Text = ch.ToString();
                    cell.Foreground = Muted;
                    cell.FontSize = 18;
                }
            }
        }

        for (int i = 0; i < 26; i++)
        {
            var cell = _cells[AlphaBase + i];
            char letter = (char)('A' + i);
            cell.Text = letter.ToString();
            cell.FontSize = 16;
            if (_map.ContainsValue(letter))
            {
                cell.Background = Ink;
                cell.Foreground = Cell;
                cell.Bold = true;
            }
            else
            {
                cell.Background = Cell;
                cell.Foreground = Ink;
            }
            cell.IsEnabled = !_solved && _selected != '\0';
        }

        if (_solved) return;
        var hint = _cells[HintCell];
        hint.Text = "★";
        hint.Background = Cell;
        hint.FontSize = 17;
        hint.Bold = true;
        hint.IsEnabled = true;
        var clear = _cells[ClearCell];
        clear.Text = "✕";
        clear.Background = Cell;
        clear.FontSize = 14;
        clear.IsEnabled = true;
    }
}
