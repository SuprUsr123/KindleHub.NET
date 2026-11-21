using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Block Blast — drop the tray's block shapes onto the 8×8 board and clear full
/// rows and columns. Cleared lines simply open up (nothing falls), the tray
/// refills only when all three pieces are used, and the game ends the moment
/// none of the remaining pieces fits anywhere.
/// </summary>
public sealed class BlockBlastGame : GameBase
{
    private const int BoardSize = 8;
    private const int Cols = 8;    // the board's width, kept for index math
    private const int Rows = 14;   // 8 board rows + 3 tray mini-blocks of 2 rows
    private const int TrayY = 8;

    // The seven tetrominoes plus the two line triminoes, as (x, y) offsets;
    // every piece fits inside a 4×2 tray mini-block.
    private static readonly (int x, int y)[][] Shapes =
    {
        new[] { (0, 0), (1, 0), (2, 0), (3, 0) },  // I4
        new[] { (0, 0), (1, 0), (0, 1), (1, 1) },  // O
        new[] { (0, 0), (1, 0), (2, 0), (1, 1) },  // T
        new[] { (1, 0), (2, 0), (0, 1), (1, 1) },  // S
        new[] { (0, 0), (1, 0), (1, 1), (2, 1) },  // Z
        new[] { (0, 0), (1, 0), (2, 0), (2, 1) },  // L
        new[] { (0, 0), (1, 0), (2, 0), (0, 1) },  // J
        new[] { (0, 0), (1, 0) },                  // domino I2
        new[] { (0, 0), (1, 0), (2, 0) },          // tromino I3
    };

    private readonly List<GameCell> _cells = new();
    private readonly int[,] _board = new int[BoardSize, BoardSize]; // 0 empty, else shape id + 1
    private readonly int[] _tray = new int[3];                     // shape id, -1 when used
    private int _sel = -1;
    private int _points;
    private int _lines;
    private bool _over;
    private string _result = "";

    public BlockBlastGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "blockblast";
    public override string Name => "Block Blast";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _points;
    public override string? ResultText => _over ? _result : null;

    public override string StatusText => _over
        ? $"Game over — {_points} points."
        : _sel >= 0
            ? "Tap the board where the piece's top-left goes."
            : $"Score {_points} · tap a tray piece";

    public override void Reset()
    {
        Array.Clear(_board, 0, _board.Length);
        for (int i = 0; i < _tray.Length; i++) _tray[i] = Random.Shared.Next(Shapes.Length);
        _sel = -1;
        _points = 0;
        _lines = 0;
        _over = false;
        _result = "";
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        int x = index % Cols, y = index / Cols;

        if (y < BoardSize)
        {
            if (_sel < 0 || _tray[_sel] < 0) return false;
            TryPlace(x, y);   // an illegal placement is simply ignored
            Redraw();
            return true;
        }

        // The three tray mini-blocks sit below the board, two rows each.
        int slot = (y - TrayY) / 2;
        if (slot < 0 || slot >= _tray.Length) return false;
        _sel = _tray[slot] >= 0 ? slot : -1;
        Redraw();
        return true;
    }

    private bool Fits(int x, int y, int shape)
    {
        foreach (var (px, py) in Shapes[shape])
        {
            int nx = x + px, ny = y + py;
            if (nx < 0 || ny < 0 || nx >= BoardSize || ny >= BoardSize) return false;
            if (_board[nx, ny] != 0) return false;
        }
        return true;
    }

    private bool TryPlace(int x, int y)
    {
        int shape = _tray[_sel];
        if (!Fits(x, y, shape)) return false;

        foreach (var (px, py) in Shapes[shape]) _board[x + px, y + py] = shape + 1;
        _tray[_sel] = -1;
        _sel = -1;

        ClearLines();
        if (_tray[0] < 0 && _tray[1] < 0 && _tray[2] < 0)
            for (int i = 0; i < _tray.Length; i++) _tray[i] = Random.Shared.Next(Shapes.Length);
        CheckGameOver();
        return true;
    }

    private void ClearLines()
    {
        var clear = new bool[BoardSize, BoardSize];
        int lines = 0;
        for (int y = 0; y < BoardSize; y++)
        {
            bool full = true;
            for (int x = 0; x < BoardSize; x++)
                if (_board[x, y] == 0) { full = false; break; }
            if (full)
            {
                lines++;
                for (int x = 0; x < BoardSize; x++) clear[x, y] = true;
            }
        }
        for (int x = 0; x < BoardSize; x++)
        {
            bool full = true;
            for (int y = 0; y < BoardSize; y++)
                if (_board[x, y] == 0) { full = false; break; }
            if (full)
            {
                lines++;
                for (int y = 0; y < BoardSize; y++) clear[x, y] = true;
            }
        }
        if (lines == 0) return;

        // 10 per line plus 5 for every extra simultaneous line.
        _points += 10 * lines + 5 * (lines - 1);
        _lines += lines;
        for (int y = 0; y < BoardSize; y++)
            for (int x = 0; x < BoardSize; x++)
                if (clear[x, y]) _board[x, y] = 0;
    }

    private void CheckGameOver()
    {
        for (int slot = 0; slot < _tray.Length; slot++)
        {
            int shape = _tray[slot];
            if (shape < 0) continue;
            for (int y = 0; y < BoardSize; y++)
                for (int x = 0; x < BoardSize; x++)
                    if (Fits(x, y, shape)) return;
        }
        _over = true;
        _result = _points > 0
            ? $"Cleared {_lines} lines — {_points} points."
            : "No piece fits — 0 points.";
        ScoreCounts = _points > 0;
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

        for (int y = 0; y < BoardSize; y++)
            for (int x = 0; x < BoardSize; x++)
            {
                var c = _cells[y * Cols + x];
                int id = _board[x, y];
                c.Background = id == 0 ? Cell : Tiles4[(id - 1) % Tiles4.Length];
                c.Text = id == 0 ? "" : "■";
                c.Bold = id != 0;
                c.IsEnabled = !_over;
            }

        // The tray: three 4×2 mini-block rows, centred under the board.
        for (int slot = 0; slot < _tray.Length; slot++)
        {
            int shape = _tray[slot];
            if (shape < 0) continue;
            var shapeCells = Shapes[shape];
            int w = shapeCells.Max(p => p.x) + 1;
            int h = shapeCells.Max(p => p.y) + 1;
            int ox = 2 + (4 - w) / 2;                  // centre in the block's width
            int oy = TrayY + slot * 2 + (2 - h) / 2;   // …and in its two rows
            foreach (var (px, py) in shapeCells)
            {
                var c = _cells[(oy + py) * Cols + ox + px];
                c.Background = Tiles4[shape % Tiles4.Length];
                c.Text = "■";
                c.Foreground = Ink;
                c.Bold = _sel == slot;
                c.IsEnabled = !_over;
                c.FontSize = _sel == slot ? 24 : 18;
            }
        }
    }
}
