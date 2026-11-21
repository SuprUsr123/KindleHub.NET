using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Strands, ported from the official client — every letter in the 6x6 grid belongs to
/// exactly one theme word. Tap touching letters (any direction) to chain a word; a
/// chain that spells one of the six theme words is banked automatically.
/// </summary>
public sealed class StrandsGame : GameBase
{
    private const int Cols = 6;
    private const int GridCells = Cols * Cols;
    private const int ClearCell = 36;   // row 6, col 0

    // Each puzzle: theme, its six 6-letter words, and the letter grid. Words are laid
    // down the columns (boustrophedon: odd columns run bottom-to-top), so every word
    // is traceable through touching cells — validity is guaranteed by construction.
    private static readonly (string Theme, string[] Words, string[] Grid)[] Puzzles =
    {
        ("IN THE KITCHEN",
            new[] { "KNIVES", "SPOONS", "LADLES", "WHISKS", "GRATER", "TEAPOT" },
            new[] { "KSLSGT", "NNAKRO", "IODSAP", "VOLITA", "EPEHEE", "SSSWRT" }),
        ("WEATHER",
            new[] { "BREEZE", "CLOUDS", "STORMS", "FROSTY", "SQUALL", "FLURRY" },
            new[] { "BSSYSY", "RDTTQR", "EUOSUR", "EOROAU", "ZLMRLL", "ECSFLF" }),
        ("ON THE FARM",
            new[] { "FARMER", "HARROW", "MEADOW", "CHICKS", "STABLE", "PIGLET" },
            new[] { "FWMSST", "AOEKTE", "RRACAL", "MRDIBG", "EAOHLI", "RHWCEP" }),
        ("OUTER SPACE",
            new[] { "PLANET", "COMETS", "GALAXY", "METEOR", "ORBITS", "SATURN" },
            new[] { "PSGRON", "LTAORR", "AELEBU", "NMATIT", "EOXETA", "TCYSMS" }),
        ("IN THE OCEAN",
            new[] { "TURTLE", "WHALES", "SHARKS", "SQUIDS", "SALMON", "PRAWNS" },
            new[] { "TSSSSS", "UEHDAN", "RLAILW", "TARUMA", "LHKQOR", "EWSSNP" }),
        ("BIRDS",
            new[] { "ROBINS", "EAGLES", "PIGEON", "CANARY", "PARROT", "FALCON" },
            new[] { "RSPYPN", "OEIRAO", "BLGARC", "IGENRL", "NAOAOA", "SENCTF" }),
    };

    private readonly List<GameCell> _cells = new();
    private readonly bool[] _foundWord = new bool[6];
    private readonly List<string> _foundList = new();
    private readonly HashSet<int> _dimmed = new();   // cells used by found words
    private readonly List<int> _chain = new();
    private string[] _grid = Array.Empty<string>();
    private string[] _words = Array.Empty<string>();
    private string _theme = "";
    private bool _done;
    private string _message = "";

    public StrandsGame()
    {
        for (int i = 0; i < Cols * 8; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "strands";
    public override string Name => "Strands";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _done ? 50 * 6 + 25 : 0;
    public override string? ResultText => _done ? $"All six {_theme} words found!" : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"All found — {_theme}!";
            if (!string.IsNullOrEmpty(_message)) return _message;
            string chain = ChainWord();
            return $"{_theme} · {_foundList.Count}/6 · " +
                   (chain.Length > 0 ? $"chain: {chain}" : "tap touching letters");
        }
    }

    public override void Reset()
    {
        var puzzle = Puzzles[Random.Shared.Next(Puzzles.Length)];
        _theme = puzzle.Theme;
        _words = puzzle.Words;
        _grid = puzzle.Grid;
        Array.Clear(_foundWord, 0, _foundWord.Length);
        _foundList.Clear();
        _dimmed.Clear();
        _chain.Clear();
        _done = false;
        _message = "";
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (key == GameKey.Backspace) return OnBackspace();
        if (key == GameKey.Confirm && _chain.Count > 0 && !_done)
        {
            _chain.Clear();
            _message = "";
            Redraw();
            return true;
        }
        return base.OnKey(key);
    }

    public override bool OnBackspace()
    {
        if (_done || _chain.Count == 0) return false;
        _chain.RemoveAt(_chain.Count - 1);
        _message = "";
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;

        if (index == ClearCell)
        {
            _chain.Clear();
            _message = "";
            Redraw();
            return true;
        }
        if (index >= GridCells) return false;

        int at = _chain.IndexOf(index);
        if (at >= 0)
        {
            // Tapping the newest letter undoes it; older letters just warn.
            if (at == _chain.Count - 1) _chain.RemoveAt(at);
            else _message = "Already in the chain.";
            Redraw();
            return true;
        }

        if (_chain.Count > 0)
        {
            int last = _chain[^1];
            int dr = Math.Abs(last / Cols - index / Cols);
            int dc = Math.Abs(last % Cols - index % Cols);
            if (dr > 1 || dc > 1)
            {
                // A jump breaks the chain; the tapped letter starts a fresh one.
                _chain.Clear();
                _message = "Chain cleared — letters must touch.";
            }
        }
        _chain.Add(index);

        string word = ChainWord();
        int w = Array.IndexOf(_words, word);
        if (w >= 0 && !_foundWord[w])
        {
            FoundWord(w);
            return true;
        }
        if (_chain.Count >= 8)
        {
            _chain.Clear();
            _message = "Too long — chain cleared.";
        }
        Redraw();
        return true;
    }

    private void FoundWord(int w)
    {
        _foundWord[w] = true;
        _foundList.Add(_words[w]);
        foreach (var i in _chain) _dimmed.Add(i);
        _message = $"Found {_words[w]}!";
        _chain.Clear();
        if (_foundList.Count == 6)
        {
            _done = true;
            ScoreCounts = true;
        }
        Redraw();
    }

    private string ChainWord() =>
        new(_chain.Select(i => _grid[i / Cols][i % Cols]).ToArray());

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
            cell.Text = _grid[i / Cols][i % Cols].ToString();
            cell.FontSize = 20;
            cell.Bold = true;
            // Used letters stay tappable (another word's path may need them); they
            // just dim so the solved ones are easy to eye-skip.
            if (_chain.Contains(i))
            {
                cell.Background = Accent;
                cell.Foreground = Cell;
            }
            else if (_dimmed.Contains(i))
            {
                cell.Background = Board;
                cell.Foreground = Muted;
            }
            else
            {
                cell.Background = Cell;
                cell.Foreground = Ink;
            }
            cell.IsEnabled = !_done;
        }

        if (_done) return;
        var clear = _cells[ClearCell];
        clear.Text = "✕";
        clear.Background = Cell;
        clear.FontSize = 14;
        clear.IsEnabled = true;

        SpreadRow(37, string.Join(" ", _foundList.Take(3)), 5, 11, 5);
        SpreadRow(43, string.Join(" ", _foundList.Skip(3).Take(3)), 5, 11, 5);
        _cells[37].Foreground = Ink;
        _cells[43].Foreground = Ink;
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
