using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Mahjong Match, ported from the official client — match pairs of identical tiles
/// from a 72-tile two-layer board. A tile comes off only when nothing sits on top
/// of it and its left or right side is clear. Every board is generated solvable by
/// placing pairs onto always-free squares, in reverse of a solving order.
/// </summary>
public sealed class MahjongGame : GameBase
{
    private const int Cols = 8;
    private const int BaseRows = 6;
    private const int LayerRows = 4;
    private const int TotalRows = BaseRows + LayerRows + 1; // + the control row
    private const int TileCount = 72;
    private const int BaseTiles = 48;

    private const int ShuffleCell = 80; // row 10, col 0
    private const int NewCell = 87;     // row 10, col 7

    private readonly List<GameCell> _cells = new();
    private readonly string[] _code = new string[TileCount];
    private readonly bool[] _gone = new bool[TileCount];
    private int _sel = -1;
    private int _cleared;
    private bool _shuffled;
    private bool _over;
    private bool _won;
    private string _msg = "Tap two matching free tiles.";

    public MahjongGame()
    {
        for (int i = 0; i < Cols * TotalRows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "mahjong";
    public override string Name => "Mahjong Match";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    /// <summary>10 points a tile, plus 100 for clearing without a shuffle.</summary>
    public override int Score => _won ? 10 * TileCount + (_shuffled ? 0 : 100)
        : _over ? Math.Max(1, 10 * _cleared) : 0;

    public override string? ResultText => !_over ? null
        : _won ? "Board cleared!"
        : "No moves left.";

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            return $"{_msg} · cleared {_cleared}/{TileCount} · grey tiles are blocked";
        }
    }

    public override void Reset()
    {
        Array.Clear(_gone, 0, _gone.Length);
        _sel = -1;
        _cleared = 0;
        _shuffled = false;
        _over = _won = false;
        ScoreCounts = false;
        _msg = "Tap two matching free tiles · layer 2 is shown below.";
        Generate();
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count) return false;
        if (index == NewCell)
        {
            Reset(); // "New ⟲" deals a fresh board even after a deadlock
            return true;
        }
        if (_over) return false;

        if (index == ShuffleCell)
        {
            if (HasMove() || _shuffled)
            {
                _msg = _shuffled ? "The one shuffle is used." : "There is still a move.";
                Redraw();
                return true;
            }
            ShuffleRemainder();
            Redraw();
            return true;
        }

        int tile = CellToTile(index);
        if (tile < 0 || _gone[tile] || !Free(tile)) return false;

        if (_sel < 0)
        {
            _sel = tile;
            Redraw();
            return true;
        }
        if (_sel == tile)
        {
            _sel = -1;
            Redraw();
            return true;
        }
        if (Matches(_sel, tile))
        {
            _gone[_sel] = true;
            _gone[tile] = true;
            _cleared += 2;
            _sel = -1;
            if (_cleared >= TileCount)
            {
                _over = _won = true;
                ScoreCounts = true;
            }
            else if (!HasMove())
            {
                if (!_shuffled) _msg = "No moves left — shuffle!";
                else
                {
                    _over = true;
                    if (_cleared >= 1) ScoreCounts = true;
                }
            }
            else _msg = "Pair removed.";
            Redraw();
            return true;
        }

        _msg = "Those tiles don't match.";
        _sel = tile;
        Redraw();
        return true;
    }

    /// <summary>Rescrambles the remaining tiles so a free pair exists again.</summary>
    private void ShuffleRemainder()
    {
        var left = Enumerable.Range(0, TileCount).Where(t => !_gone[t]).ToList();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var codes = left.Select(t => _code[t]).ToList();
            for (int i = codes.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (codes[i], codes[j]) = (codes[j], codes[i]);
            }
            for (int i = 0; i < left.Count; i++) _code[left[i]] = codes[i];
            if (HasMove()) break;
        }
        _shuffled = true;
        _sel = -1;
        _msg = "Tiles shuffled.";
    }

    private bool HasMove()
    {
        var groups = new Dictionary<string, int>();
        for (int t = 0; t < TileCount; t++)
        {
            if (_gone[t] || !Free(t)) continue;
            string key = MatchKey(t);
            groups[key] = groups.GetValueOrDefault(key) + 1;
            if (groups[key] >= 2) return true;
        }
        return false;
    }

    private bool Matches(int a, int b) => _code[a] == _code[b]
        || (_code[a][0] == 'F' && _code[b][0] == 'F');

    /// <summary>Flowers pair with any other flower; everything else needs its own code.</summary>
    private string MatchKey(int t) => _code[t][0] == 'F' ? "F" : _code[t];

    private bool Free(int t)
    {
        int above = Above(t);
        if (above >= 0 && !_gone[above]) return false;
        int left = Left(t), right = Right(t);
        bool leftClear = left < 0 || _gone[left];
        bool rightClear = right < 0 || _gone[right];
        return leftClear || rightClear;
    }

    // ─── board geometry ──────────────────────────────────────────────────────
    // Base layer: 8×6. Second layer: 6×4, centred, shown in the four rows below.

    private static int CellToTile(int cell)
    {
        int row = cell / Cols, col = cell % Cols;
        if (row < BaseRows) return cell;
        if (row < BaseRows + LayerRows)
        {
            if (col < 1 || col > 6) return -1;
            return BaseTiles + (row - BaseRows) * 6 + (col - 1);
        }
        return -1;
    }

    private static int TileToCell(int t)
    {
        if (t < BaseTiles) return t;
        int n = t - BaseTiles;
        return (BaseRows + n / 6) * Cols + 1 + n % 6;
    }

    private static int Above(int t)
    {
        if (t >= BaseTiles) return -1;
        int x = t % Cols, y = t / Cols;
        return x >= 1 && x <= 6 && y >= 1 && y <= 4
            ? BaseTiles + (y - 1) * 6 + (x - 1)
            : -1;
    }

    private static int Left(int t) => SideTile(t, -1);

    private static int Right(int t) => SideTile(t, 1);

    private static int SideTile(int t, int step)
    {
        if (t < BaseTiles)
        {
            int x = t % Cols + step, y = t / Cols;
            return x >= 0 && x < Cols ? y * Cols + x : -1;
        }
        int n = t - BaseTiles;
        int xx = n % 6 + step;
        return xx >= 0 && xx < 6 ? BaseTiles + n / 6 * 6 + xx : -1;
    }

    // ─── solvable deal ───────────────────────────────────────────────────────
    // Tiles are placed pair by pair; that is a valid game played backwards, so
    // the board is solvable by removing the pairs in reverse order. Two rules
    // keep every placed pair simultaneously removable: rows are consumed from
    // one fixed end (so a tile's outward neighbour is always taken later and is
    // therefore already gone when the pair comes off), and the two tiles of a
    // pair always come from different rows (same-row partners would block each
    // other's only open side). The base layer goes down before the layer above.

    private void Generate()
    {
        var pairs = new List<(string A, string B)>();
        for (int v = 1; v <= 9; v++)
        {
            pairs.Add(($"C{v}", $"C{v}"));
            pairs.Add(($"B{v}", $"B{v}"));
            pairs.Add(($"O{v}", $"O{v}"));
        }
        foreach (var w in new[] { "WE", "WS", "WN", "WW" }) pairs.Add((w, w));
        foreach (var d in new[] { "DR", "DG", "DW" }) pairs.Add((d, d));
        pairs.Add(("F1", "F2"));
        pairs.Add(("F3", "F4"));
        for (int i = pairs.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (pairs[i], pairs[j]) = (pairs[j], pairs[i]);
        }

        // Rows 0..5 are the base (8 wide); rows 6..9 the upper layer (6 wide).
        var fronts = new int[BaseRows + LayerRows];
        var dirs = new int[BaseRows + LayerRows];
        for (int r = 0; r < fronts.Length; r++)
        {
            bool ltr = Random.Shared.Next(2) == 0;
            dirs[r] = ltr ? 1 : -1;
            fronts[r] = ltr ? 0 : (r < BaseRows ? Cols : 6) - 1;
        }

        // Each round every row gives up exactly one square, paired with a
        // different row's square, so no row is ever left to pair with itself.
        int pairIndex = 0;
        foreach (var phase in new[] { 0, 1 })
        {
            int first = phase == 0 ? 0 : BaseRows;
            int rowCount = phase == 0 ? BaseRows : LayerRows;
            int rounds = phase == 0 ? Cols : 6;
            for (int round = 0; round < rounds; round++)
            {
                var order = Enumerable.Range(first, rowCount).ToList();
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = Random.Shared.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                for (int i = 0; i + 1 < order.Count; i += 2)
                {
                    int a = TakeFromRow(order[i], fronts, dirs);
                    int b = TakeFromRow(order[i + 1], fronts, dirs);
                    _code[a] = pairs[pairIndex].A;
                    _code[b] = pairs[pairIndex].B;
                    pairIndex++;
                }
            }
        }
    }

    private static int TakeFromRow(int row, int[] fronts, int[] dirs)
    {
        int width = row < BaseRows ? Cols : 6;
        int at = fronts[row];
        int x = at;
        fronts[row] += dirs[row];
        return row < BaseRows ? row * Cols + x : BaseTiles + row * 6 - BaseRows * 6 + x;
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
            c.FontSize = 13;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        for (int t = 0; t < TileCount; t++)
        {
            var c = _cells[TileToCell(t)];
            if (_gone[t]) continue;
            bool free = Free(t);
            c.Text = _code[t];
            c.Background = t >= BaseTiles ? Tiles4[0] : Cell;
            c.Foreground = free ? Ink : Muted;
            c.Bold = free;
            c.IsEnabled = free && !_over;
            if (_sel == t)
            {
                c.BorderBrush = Accent;
                c.BorderThickness = new Avalonia.Thickness(2);
            }
        }

        var shuffle = _cells[ShuffleCell];
        bool canShuffle = !_over && !_shuffled && !HasMove();
        shuffle.Text = "Shuffle ⟲";
        shuffle.FontSize = 9;
        shuffle.Bold = true;
        shuffle.Background = canShuffle ? Cell : Board;
        shuffle.IsEnabled = canShuffle;

        var newBoard = _cells[NewCell];
        newBoard.Text = "New ⟲";
        newBoard.FontSize = 9;
        newBoard.Bold = true;
        newBoard.Background = Cell;
        newBoard.IsEnabled = true;
    }
}
