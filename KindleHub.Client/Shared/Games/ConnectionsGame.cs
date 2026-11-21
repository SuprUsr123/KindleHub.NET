using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Connections, ported from the official client — sixteen words hiding four groups of
/// four. Select up to four words and submit; a correct group locks in as a banner row,
/// a wrong one costs a life out of four.
/// </summary>
public sealed class ConnectionsGame : GameBase
{
    private const int Cols = 8;
    private const int BoardRows = 4;
    private const int SubmitCell = 35;     // buttons row, col 3
    private const int DeselectCell = 36;   // buttons row, col 4
    private const int Lives = 4;

    // One banner colour per solved group; purple/teal match the digit palette
    // Minesweeper already uses so the board stays low-chroma on e-ink.
    private static readonly IBrush[] GroupBrushes =
    {
        new SolidColorBrush(Color.Parse("#2f6fed")),
        new SolidColorBrush(Color.Parse("#1f7a3d")),
        new SolidColorBrush(Color.Parse("#9a6700")),
        new SolidColorBrush(Color.Parse("#6b3fa0")),
    };

    private static readonly (string Name, string[] Words)[][] Puzzles =
    {
        new[]
        {
            ("BREADS", new[] { "RYE", "NAAN", "PITA", "WHEAT" }),
            ("PLANETS", new[] { "MARS", "VENUS", "SATURN", "PLUTO" }),
            ("RED THINGS", new[] { "BLOOD", "TOMATO", "ROSE", "FIRE" }),
            ("DOGS", new[] { "PUG", "BEAGLE", "LAB", "POODLE" }),
        },
        new[]
        {
            ("SHAPES", new[] { "CIRCLE", "SQUARE", "OVAL", "DIAMOND" }),
            ("GEMS", new[] { "RUBY", "PEARL", "OPAL", "JADE" }),
            ("CARD GAMES", new[] { "POKER", "BRIDGE", "RUMMY", "HEARTS" }),
            ("GREEN THINGS", new[] { "GRASS", "MOSS", "LIME", "EMERALD" }),
        },
        new[]
        {
            ("COFFEE", new[] { "LATTE", "MOCHA", "ESPRESSO", "DECAF" }),
            ("TEAS", new[] { "GREEN", "BLACK", "OOLONG", "CHAI" }),
            ("ITALIAN CITIES", new[] { "ROME", "MILAN", "NAPLES", "TURIN" }),
            ("PIZZA TOPPINGS", new[] { "OLIVE", "BASIL", "HAM", "ONION" }),
        },
        new[]
        {
            ("METALS", new[] { "IRON", "COPPER", "ZINC", "NICKEL" }),
            ("BODY PARTS", new[] { "ELBOW", "ANKLE", "WRIST", "HIP" }),
            ("MUSICAL TERMS", new[] { "TEMPO", "CHORD", "OCTAVE", "LYRIC" }),
            ("BOATS", new[] { "CANOE", "YACHT", "FERRY", "BARGE" }),
        },
        new[]
        {
            ("TREES", new[] { "OAK", "MAPLE", "BIRCH", "CEDAR" }),
            ("MONTHS", new[] { "MARCH", "APRIL", "JUNE", "AUGUST" }),
            ("COLORS", new[] { "TEAL", "CORAL", "OLIVE", "IVORY" }),
            ("GOLF TERMS", new[] { "EAGLE", "BIRDIE", "BOGEY", "PAR" }),
        },
        new[]
        {
            ("JAPANESE FOOD", new[] { "SUSHI", "RAMEN", "MISO", "TOFU" }),
            ("SOUPS", new[] { "BROTH", "GUMBO", "CHOWDER", "BISQUE" }),
            ("FRENCH WORDS", new[] { "CAFE", "BALLET", "DEBUT", "ENCORE" }),
            ("OPERA TERMS", new[] { "ARIA", "DUET", "TENOR", "CHORUS" }),
        },
        new[]
        {
            ("BOARD GAMES", new[] { "CHESS", "DOMINO", "LUDO", "RISK" }),
            ("CHESS PIECES", new[] { "PAWN", "ROOK", "KNIGHT", "QUEEN" }),
            ("MUSICALS", new[] { "CATS", "WICKED", "GREASE", "RENT" }),
            ("SHAKESPEARE", new[] { "HAMLET", "MACBETH", "OTHELLO", "VIOLA" }),
        },
        new[]
        {
            ("WEATHER", new[] { "SLEET", "DRIZZLE", "THUNDER", "FOG" }),
            ("CURRENCIES", new[] { "EURO", "PESO", "DINAR", "FRANC" }),
            ("NOISES", new[] { "BANG", "RUMBLE", "HISS", "ROAR" }),
            ("CAR PARTS", new[] { "BRAKE", "AXLE", "CLUTCH", "FENDER" }),
        },
    };

    private readonly List<GameCell> _cells = new();
    private readonly string[] _words = new string[16];
    private readonly int[] _groupOf = new int[16];
    private readonly List<int> _order = new();          // display order of unsolved words
    private readonly HashSet<int> _selected = new();
    private readonly List<int> _solvedGroups = new();   // group ids in solve order
    private readonly string[] _groupNames = new string[4];
    private int _lives = Lives;
    private bool _won;
    private bool _lost;
    private string _message = "";

    public ConnectionsGame()
    {
        for (int i = 0; i < Cols * 5; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "connections";
    public override string Name => "Connections";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _won ? 100 * (_lives + 1) + 50 * _solvedGroups.Count : 0;

    public override string? ResultText
    {
        get
        {
            if (_won) return $"All four groups found with {_lives} lives left.";
            if (_lost)
            {
                var left = Enumerable.Range(0, 4)
                    .Where(g => !_solvedGroups.Contains(g))
                    .Select(g => _groupNames[g]);
                return "Out of lives — the groups were: " + string.Join(", ", left) + ".";
            }
            return null;
        }
    }

    public override string StatusText
    {
        get
        {
            if (_won) return "All four groups solved!";
            if (_lost) return "Out of lives.";
            if (!string.IsNullOrEmpty(_message)) return _message;
            return $"Lives {new string('♥', _lives)} · picked {_selected.Count}/4 · tap words, then ✔";
        }
    }

    public override void Reset()
    {
        var puzzle = Puzzles[Random.Shared.Next(Puzzles.Length)];
        int w = 0;
        for (int g = 0; g < 4; g++)
        {
            _groupNames[g] = puzzle[g].Name;
            foreach (var word in puzzle[g].Words)
            {
                _words[w] = word;
                _groupOf[w] = g;
                w++;
            }
        }
        _order.Clear();
        foreach (var i in Enumerable.Range(0, 16).OrderBy(_ => Random.Shared.Next())) _order.Add(i);
        _selected.Clear();
        _solvedGroups.Clear();
        _lives = Lives;
        _won = _lost = false;
        _message = "";
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_won || _lost || index < 0 || index >= _cells.Count) return false;
        if (index == SubmitCell) return Submit();
        if (index == DeselectCell)
        {
            _selected.Clear();
            _message = "";
            Redraw();
            return true;
        }

        int r = index / Cols, c = index % Cols;
        if (r >= BoardRows || c < 2 || c > 5) return false;
        int banners = _solvedGroups.Count;
        if (r < banners) return false;   // solved banners are not tappable
        int d = (r - banners) * 4 + (c - 2);
        if (d >= _order.Count) return false;

        int word = _order[d];
        if (_selected.Contains(word)) _selected.Remove(word);
        else if (_selected.Count >= 4) _message = "Only four at a time.";
        else _selected.Add(word);
        if (_selected.Count != 4) _message = "";
        Redraw();
        return true;
    }

    private bool Submit()
    {
        if (_selected.Count < 4)
        {
            _message = "Pick four words first.";
            Redraw();
            return true;
        }

        int group = _groupOf[_selected.First()];
        if (_selected.All(x => _groupOf[x] == group))
        {
            _solvedGroups.Add(group);
            // Solved words leave the pool so the unsolved restack into whole rows.
            _order.RemoveAll(x => _groupOf[x] == group);
            _selected.Clear();
            _message = $"Solved — {_groupNames[group]}!";
            if (_solvedGroups.Count == 4)
            {
                _won = true;
                ScoreCounts = true;
            }
        }
        else
        {
            int closest = _selected.GroupBy(x => _groupOf[x]).Max(g => g.Count());
            _lives--;
            if (_lives <= 0)
            {
                _lost = true;
                _selected.Clear();
                _message = "Out of lives.";
            }
            else
            {
                _message = closest == 3 ? "One away..." : "Not a group.";
            }
        }
        Redraw();
        return true;
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
            c.FontSize = 14;
            c.Span = 1;
        }

        int banners = _solvedGroups.Count;
        for (int r = 0; r < BoardRows; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                var cell = _cells[r * Cols + 2 + c];
                if (r < banners)
                {
                    // A solved group collapses into one banner row in its own colour.
                    int g = _solvedGroups[r];
                    cell.Text = Slice4(_groupNames[g], c);
                    cell.Background = GroupBrushes[g % GroupBrushes.Length];
                    cell.Foreground = Cell;
                    cell.Bold = true;
                    cell.FontSize = 11;
                }
                else
                {
                    int d = (r - banners) * 4 + c;
                    if (d >= _order.Count) continue;
                    int word = _order[d];
                    cell.Text = _words[word];
                    cell.FontSize = 11;
                    if (_selected.Contains(word))
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
                    cell.IsEnabled = !_won && !_lost;
                }
            }
        }

        if (_won || _lost) return;
        var submit = _cells[SubmitCell];
        submit.Text = "✔";
        submit.Background = Cell;
        submit.FontSize = 18;
        submit.Bold = true;
        submit.IsEnabled = true;
        var deselect = _cells[DeselectCell];
        deselect.Text = "✕";
        deselect.Background = Cell;
        deselect.FontSize = 15;
        deselect.IsEnabled = true;
    }

    /// <summary>Quarter of a group name, centred in a 16-character banner.</summary>
    private static string Slice4(string text, int quarter)
    {
        text = text.PadLeft((16 + text.Length) / 2).PadRight(16);
        return text.Substring(quarter * 4, 4);
    }
}
