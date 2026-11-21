using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Match 3 — swap adjacent pieces to line up three or more of a kind. Matches
/// clear, tiles fall, the top refills and cascades score an ever-growing combo.
/// The board is generated without matches and reshuffles if no swap is left.
/// </summary>
public sealed class CandyCrushGame : GameBase
{
    private const int Size = 8;
    private const int MoveBudget = 25;
    private const int MaxWaves = 20;

    private const string Glyphs = "●▲■◆★";

    // Five distinct tints, one per symbol; reused from the shared palette.
    private static readonly Avalonia.Media.IBrush[] Tints =
    {
        Tiles4[2], Tiles4[3], Tiles4[4], Tiles4[5],
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9fb4c8")),
    };

    private readonly List<GameCell> _cells = new();
    private int[] _board = new int[Size * Size];
    private int _sel = -1;
    private int _movesLeft = MoveBudget;
    private int _score;
    private bool _over;
    private string _result = "";

    public CandyCrushGame()
    {
        for (int i = 0; i < Size * Size; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "candycrush";
    public override string Name => "Match 3";
    public override int Columns => Size;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText => _over ? _result : null;

    public override string StatusText => _over
        ? $"Out of moves — {_score} points."
        : $"Moves {_movesLeft} · Score {_score} · line up 3";

    public override void Reset()
    {
        _board = FreshBoard();
        int guard = 0;
        while (!HasMove(_board) && guard++ < 50) _board = FreshBoard();
        if (!HasMove(_board)) _board = FallbackBoard();
        _sel = -1;
        _movesLeft = MoveBudget;
        _score = 0;
        _over = false;
        _result = "";
        ScoreCounts = false;
        Redraw();
    }

    /// <summary>
    /// Fills the board avoiding any immediate three-in-a-row: the two cells to
    /// the left and the two above ban at most two symbols, so a choice always
    /// exists out of five.
    /// </summary>
    private static int[] FreshBoard()
    {
        var b = new int[Size * Size];
        for (int i = 0; i < b.Length; i++)
        {
            int x = i % Size, y = i / Size;
            var banned = new HashSet<int>();
            if (x >= 2 && b[i - 1] == b[i - 2]) banned.Add(b[i - 1]);
            if (y >= 2 && b[i - Size] == b[i - Size * 2]) banned.Add(b[i - Size]);
            int v;
            do { v = Random.Shared.Next(Glyphs.Length); } while (banned.Contains(v));
            b[i] = v;
        }
        return b;
    }

    /// <summary>
    /// A known-good board: the diagonal pattern has no matches and swapping
    /// (row 1, col 3) with (row 1, col 4) lines up a column of three.
    /// </summary>
    private static int[] FallbackBoard()
    {
        var b = new int[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                b[y * Size + x] = (y + x) % Glyphs.Length;
        b[2 * Size + 3] = 3;
        b[1 * Size + 4] = 3;
        return b;
    }

    private static HashSet<int> FindMatches(int[] b)
    {
        var set = new HashSet<int>();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                int run = 1;
                while (x + run < Size && b[y * Size + x + run] == b[i]) run++;
                if (run >= 3)
                    for (int k = 0; k < run; k++) set.Add(y * Size + x + k);

                run = 1;
                while (y + run < Size && b[(y + run) * Size + x] == b[i]) run++;
                if (run >= 3)
                    for (int k = 0; k < run; k++) set.Add((y + k) * Size + x);
            }
        return set;
    }

    /// <summary>Any single swap on this board that would make a match?</summary>
    private static bool HasMove(int[] b)
    {
        var copy = (int[])b.Clone();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (x + 1 < Size)
                {
                    (copy[y * Size + x], copy[y * Size + x + 1]) = (copy[y * Size + x + 1], copy[y * Size + x]);
                    if (FindMatches(copy).Count > 0) return true;
                    (copy[y * Size + x], copy[y * Size + x + 1]) = (copy[y * Size + x + 1], copy[y * Size + x]);
                }
                if (y + 1 < Size)
                {
                    (copy[y * Size + x], copy[(y + 1) * Size + x]) = (copy[(y + 1) * Size + x], copy[y * Size + x]);
                    if (FindMatches(copy).Count > 0) return true;
                    (copy[y * Size + x], copy[(y + 1) * Size + x]) = (copy[(y + 1) * Size + x], copy[y * Size + x]);
                }
            }
        return false;
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;

        if (_sel < 0)
        {
            _sel = index;
            Redraw();
            return true;
        }
        if (index == _sel)
        {
            _sel = -1;
            Redraw();
            return true;
        }

        int ax = _sel % Size, ay = _sel / Size;
        int bx = index % Size, by = index / Size;
        bool adjacent = Math.Abs(ax - bx) + Math.Abs(ay - by) == 1;

        if (adjacent && TrySwap(_sel, index))
        {
            _sel = -1;
            _movesLeft--;
            if (_movesLeft == 0) GameOver();
            else if (!HasMove(_board)) Reshuffle();
            Redraw();
            return true;
        }

        // Not adjacent or no match: the swap snaps back, selection moves on.
        _sel = index;
        Redraw();
        return true;
    }

    private bool TrySwap(int a, int b)
    {
        (_board[a], _board[b]) = (_board[b], _board[a]);
        if (FindMatches(_board).Count == 0)
        {
            (_board[a], _board[b]) = (_board[b], _board[a]);
            return false;
        }

        // Cascades: each wave of matches scores 10 per tile times the wave number.
        int wave = 1;
        while (wave <= MaxWaves)
        {
            var matches = FindMatches(_board);
            if (matches.Count == 0) break;
            _score += 10 * matches.Count * wave;
            foreach (int i in matches) _board[i] = -1;

            for (int x = 0; x < Size; x++)
            {
                int write = Size - 1;
                for (int y = Size - 1; y >= 0; y--)
                {
                    if (_board[y * Size + x] < 0) continue;
                    _board[write * Size + x] = _board[y * Size + x];
                    if (write != y) _board[y * Size + x] = -1;
                    write--;
                }
                for (int y = write; y >= 0; y--)
                    _board[y * Size + x] = Random.Shared.Next(Glyphs.Length);
            }
            wave++;
        }
        return true;
    }

    /// <summary>Shuffles the existing symbols until there is no match and a move exists.</summary>
    private void Reshuffle()
    {
        var values = _board.ToList();
        for (int attempt = 0; attempt < 50; attempt++)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
            var candidate = values.ToArray();
            if (FindMatches(candidate).Count == 0 && HasMove(candidate))
            {
                _board = candidate;
                return;
            }
        }
        _board = FallbackBoard();
    }

    private void GameOver()
    {
        _over = true;
        _result = _score > 0 ? $"Out of moves — {_score} points." : "Out of moves — no matches.";
        ScoreCounts = _score > 0;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            int v = _board[i];
            c.Text = Glyphs[v].ToString();
            c.Background = Tints[v];
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.Bold = true;
            c.FontSize = i == _sel ? 28 : 20;
        }
    }
}
