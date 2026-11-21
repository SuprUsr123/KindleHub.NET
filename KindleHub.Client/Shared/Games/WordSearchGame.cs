using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Word Search, ported from the official client — find every hidden word in the 10x10
/// letter grid. Tap the first letter of a word, then the last; a straight line that
/// spells a target word (either way round) is marked found.
/// </summary>
public sealed class WordSearchGame : GameBase
{
    private const int Cols = 10;
    private const int GridCells = Cols * Cols;
    private const int EndCell = 109;    // message row, col 9
    private const int WordCount = 6;

    private static readonly (int Dx, int Dy)[] Dirs =
    {
        (1, 0), (0, 1), (1, 1), (1, -1), (-1, 0), (0, -1), (-1, -1), (-1, 1),
    };

    // Short common words; six are hidden each game.
    private static readonly string[] Bank =
    {
        "FISH", "BIRD", "TREE", "LEAF", "ROSE", "STAR", "MOON", "LAKE", "ROAD", "KING",
        "QUEEN", "SHIP", "BOAT", "KITE", "BALL", "GATE", "FARM", "WIND", "SNOW", "RAIN",
        "FIRE", "GOLD", "CLOUD", "RIVER", "STONE", "APPLE", "GRAPE", "LEMON", "BREAD",
        "TIGER", "ZEBRA", "HORSE", "MOUSE", "HOUSE", "GARDEN", "WINTER", "ORANGE",
        "PLANET", "SILVER", "DOLPHIN",
    };

    private readonly List<GameCell> _cells = new();
    private readonly char[] _grid = new char[GridCells];
    private readonly bool[] _foundCells = new bool[GridCells];
    private string[] _words = new string[WordCount];
    private readonly bool[] _wordFound = new bool[WordCount];
    private int _first = -1;
    private int _foundCount;
    private bool _over;
    private bool _allFound;
    private int _score;
    private string _message = "";

    public WordSearchGame()
    {
        for (int i = 0; i < Cols * 11; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "wordsearch";
    public override string Name => "Word Search";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _over ? _score : 0;
    public override string? ResultText => _over
        ? (_allFound ? "All six words found!" : $"Ended with {_foundCount}/{WordCount} words.")
        : null;

    public override string StatusText
    {
        get
        {
            if (_over) return _allFound ? "All six words found!" : $"Done — {_foundCount}/{WordCount} words.";
            if (!string.IsNullOrEmpty(_message)) return _message;
            return _first >= 0
                ? "Now tap the word's last letter"
                : $"Found {_foundCount}/{WordCount} · tap a word's first letter";
        }
    }

    public override void Reset()
    {
        _words = Bank.OrderBy(_ => Random.Shared.Next()).Take(WordCount)
            .OrderByDescending(w => w.Length).ToArray();
        Array.Clear(_foundCells, 0, _foundCells.Length);
        Array.Clear(_wordFound, 0, _wordFound.Length);
        _first = -1;
        _foundCount = 0;
        _over = _allFound = false;
        _score = 0;
        _message = "";
        ScoreCounts = false;

        // Place every word; on the rare clash the whole board is re-laid.
        int guard = 0;
        while (!TryBuild() && guard++ < 30) { }
        // Absolute fallback: never leave an empty cell unrenderable.
        for (int i = 0; i < _grid.Length; i++)
            if (_grid[i] == '\0') _grid[i] = (char)('A' + Random.Shared.Next(26));

        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (key == GameKey.Confirm && _first >= 0 && !_over)
        {
            _first = -1;
            Redraw();
            return true;
        }
        return base.OnKey(key);
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;

        if (index == EndCell)
        {
            if (_foundCount < 2)
            {
                _message = "Find at least two words first.";
                Redraw();
                return true;
            }
            // Ending early keeps 50 points per word already found.
            _over = true;
            _score = 50 * _foundCount;
            ScoreCounts = true;
            Redraw();
            return true;
        }
        if (index >= GridCells || _foundCells[index]) return false;

        if (_first < 0)
        {
            _first = index;
            _message = "";
            Redraw();
            return true;
        }
        if (_first == index)
        {
            _first = -1;
            Redraw();
            return true;
        }

        int r1 = _first / Cols, c1 = _first % Cols;
        int r2 = index / Cols, c2 = index % Cols;
        int dx = c2 - c1, dy = r2 - r1;
        int adx = Math.Abs(dx), ady = Math.Abs(dy);
        _first = -1;

        if (dx != 0 && dy != 0 && adx != ady)
        {
            _message = "Pick a straight line.";
            Redraw();
            return true;
        }

        int steps = Math.Max(adx, ady);
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        var chars = new char[steps + 1];
        for (int i = 0; i <= steps; i++)
            chars[i] = _grid[(r1 + sy * i) * Cols + c1 + sx * i];
        string line = new(chars);
        string back = new(line.Reverse().ToArray());

        for (int w = 0; w < _words.Length; w++)
        {
            if (_wordFound[w]) continue;
            if (line != _words[w] && back != _words[w]) continue;
            for (int i = 0; i <= steps; i++)
                _foundCells[(r1 + sy * i) * Cols + c1 + sx * i] = true;
            _wordFound[w] = true;
            _foundCount++;
            _message = $"Found {_words[w]}!";
            if (_foundCount == WordCount)
            {
                _over = _allFound = true;
                _score = 50 * WordCount + 25;
                ScoreCounts = true;
            }
            Redraw();
            return true;
        }

        for (int w = 0; w < _words.Length; w++)
        {
            if (!_wordFound[w]) continue;
            if (line == _words[w] || back == _words[w])
            {
                _message = $"{_words[w]} is already found.";
                Redraw();
                return true;
            }
        }

        _message = "No word there.";
        Redraw();
        return true;
    }

    /// <summary>Lays the six words in any of eight directions; letters may coincide.</summary>
    private bool TryBuild()
    {
        Array.Clear(_grid, 0, _grid.Length);
        foreach (var word in _words)
        {
            if (!TryPlace(word)) return false;
        }
        for (int i = 0; i < _grid.Length; i++)
            if (_grid[i] == '\0') _grid[i] = (char)('A' + Random.Shared.Next(26));
        return true;
    }

    private bool TryPlace(string word)
    {
        for (int attempt = 0; attempt < 400; attempt++)
        {
            var (dx, dy) = Dirs[Random.Shared.Next(Dirs.Length)];
            int c0 = Random.Shared.Next(Cols), r0 = Random.Shared.Next(Cols);
            int cEnd = c0 + dx * (word.Length - 1), rEnd = r0 + dy * (word.Length - 1);
            if (cEnd < 0 || cEnd >= Cols || rEnd < 0 || rEnd >= Cols) continue;

            bool ok = true;
            for (int i = 0; i < word.Length && ok; i++)
            {
                char cur = _grid[(r0 + dy * i) * Cols + c0 + dx * i];
                ok = cur == '\0' || cur == word[i];
            }
            if (!ok) continue;

            for (int i = 0; i < word.Length; i++)
                _grid[(r0 + dy * i) * Cols + c0 + dx * i] = word[i];
            return true;
        }
        return false;
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

        for (int i = 0; i < GridCells; i++)
        {
            var cell = _cells[i];
            cell.Text = _grid[i].ToString();
            cell.FontSize = 16;
            cell.Bold = true;
            if (_foundCells[i])
            {
                cell.Background = Good;
                cell.Foreground = Cell;
            }
            else if (i == _first)
            {
                cell.Background = Ink;
                cell.Foreground = Cell;
            }
            else
            {
                cell.Background = Cell;
            }
            cell.IsEnabled = !_over;
        }

        // Found words ride the message row so the board stays a pure letter grid.
        string found = string.Join(" ",
            _words.Where((_, w) => _wordFound[w]));
        SpreadRow(100, found, 8, 11, 6);
        _cells[100].Foreground = Ink;

        if (_over) return;
        var end = _cells[EndCell];
        end.Text = "END";
        end.Background = Cell;
        end.FontSize = 11;
        end.IsEnabled = true;
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
