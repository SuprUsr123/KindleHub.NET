using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Bridges (Hashiwokakuro) — every island shows how many bridges must touch it.
/// Tap two islands in the same row or column to build a bridge, tap again to
/// double it, a third tap removes it; bridges never cross or pass an island.
/// </summary>
public sealed class BridgesGame : GameBase
{
    private const int Side = 7;
    private const int Levels = 5;

    private sealed record Island(int X, int Y, int Count);

    // Five hand-designed layouts; each was drawn as a bridge solution on paper
    // first, so a solution always exists and no solution bridge crosses another.
    private static readonly Island[][] Puzzles =
    {
        new[]
        {
            new Island(0, 0, 1), new Island(4, 0, 1), new Island(0, 3, 3),
            new Island(4, 3, 3), new Island(0, 6, 1), new Island(4, 6, 1),
        },
        new[]
        {
            new Island(0, 0, 2), new Island(6, 0, 2), new Island(0, 3, 3),
            new Island(3, 3, 2), new Island(6, 3, 3), new Island(0, 6, 2),
            new Island(6, 6, 2),
        },
        new[]
        {
            new Island(1, 0, 3), new Island(3, 0, 2), new Island(5, 0, 2),
            new Island(1, 3, 4), new Island(5, 3, 2), new Island(1, 6, 3),
            new Island(3, 6, 2), new Island(5, 6, 2),
        },
        new[]
        {
            new Island(0, 0, 2), new Island(3, 0, 3), new Island(6, 0, 2),
            new Island(0, 3, 3), new Island(3, 3, 2), new Island(6, 3, 2),
            new Island(0, 6, 2), new Island(3, 6, 2), new Island(6, 6, 2),
        },
        new[]
        {
            new Island(0, 0, 3), new Island(3, 0, 2), new Island(6, 0, 2),
            new Island(0, 2, 3), new Island(6, 2, 3), new Island(0, 4, 3),
            new Island(6, 4, 3), new Island(0, 6, 3), new Island(3, 6, 2),
            new Island(6, 6, 2),
        },
    };

    private readonly List<GameCell> _cells = new();
    private readonly List<Island> _islands = new();
    private readonly int[,] _islandAt = new int[Side, Side];
    private readonly List<(int a, int b, int n)> _bridges = new();
    private int _sel = -1;
    private int _level = 1;
    private int _score;
    private bool _done;
    private string _message = "";
    private string _result = "";

    public BridgesGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "bridges";
    public override string Name => "Bridges";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText => _done ? _result : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Done — {_score} points.";
            if (_message.Length > 0) return _message;
            int ok = Enumerable.Range(0, _islands.Count).Count(i => BridgeCount(i) == _islands[i].Count);
            return $"Level {_level}/{Levels} · {ok}/{_islands.Count} islands · tap two in a line";
        }
    }

    public override void Reset()
    {
        _level = 1;
        _score = 0;
        _done = false;
        _result = "";
        ScoreCounts = false;
        LoadLevel();
    }

    private void LoadLevel()
    {
        _islands.Clear();
        _bridges.Clear();
        _sel = -1;
        _message = "";
        foreach (var isle in Puzzles[_level - 1]) _islands.Add(isle);
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                _islandAt[x, y] = -1;
        for (int i = 0; i < _islands.Count; i++)
            _islandAt[_islands[i].X, _islands[i].Y] = i;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        int x = index % Side, y = index / Side;
        int island = _islandAt[x, y];
        if (island < 0) return false;

        if (_sel < 0)
        {
            _sel = island;
            _message = "";
            Redraw();
            return true;
        }
        if (_sel == island)
        {
            _sel = -1;
            Redraw();
            return true;
        }
        if (!Aligned(_sel, island))
        {
            // Tapping an island that isn't in line simply switches the selection.
            _sel = island;
            Redraw();
            return true;
        }

        int existing = BridgeBetween(_sel, island);
        if (existing < 0)
        {
            if (!PathClear(_sel, island))
            {
                _message = "A bridge cannot pass an island.";
                _sel = -1;
                Redraw();
                return true;
            }
            if (Crosses(_sel, island))
            {
                _message = "Bridges cannot cross.";
                _sel = -1;
                Redraw();
                return true;
            }
            _bridges.Add((_sel, island, 1));
        }
        else
        {
            // One tap builds, a second doubles, a third removes.
            var (a, b, n) = _bridges[existing];
            if (n == 1) _bridges[existing] = (a, b, 2);
            else _bridges.RemoveAt(existing);
        }

        _sel = -1;
        _message = "";
        if (Solved()) LevelUp();
        Redraw();
        return true;
    }

    private bool Aligned(int a, int b) =>
        _islands[a].X == _islands[b].X || _islands[a].Y == _islands[b].Y;

    private int BridgeBetween(int a, int b)
    {
        for (int i = 0; i < _bridges.Count; i++)
        {
            var (x, y, _) = _bridges[i];
            if ((x == a && y == b) || (x == b && y == a)) return i;
        }
        return -1;
    }

    private int BridgeCount(int island) =>
        _bridges.Where(b => b.a == island || b.b == island).Sum(b => b.n);

    /// <summary>No island may sit strictly between the two bridge endpoints.</summary>
    private bool PathClear(int a, int b)
    {
        var ia = _islands[a];
        var ib = _islands[b];
        if (ia.Y == ib.Y)
        {
            for (int x = Math.Min(ia.X, ib.X) + 1; x < Math.Max(ia.X, ib.X); x++)
                if (_islandAt[x, ia.Y] >= 0) return false;
        }
        else
        {
            for (int y = Math.Min(ia.Y, ib.Y) + 1; y < Math.Max(ia.Y, ib.Y); y++)
                if (_islandAt[ia.X, y] >= 0) return false;
        }
        return true;
    }

    /// <summary>True when the new bridge would cut an existing one mid-segment.</summary>
    private bool Crosses(int a, int b)
    {
        var ia = _islands[a];
        var ib = _islands[b];
        foreach (var (c, d, _) in _bridges)
        {
            var ic = _islands[c];
            var id = _islands[d];
            if (ia.Y == ib.Y && ic.X == id.X &&
                ic.X > Math.Min(ia.X, ib.X) && ic.X < Math.Max(ia.X, ib.X) &&
                ia.Y > Math.Min(ic.Y, id.Y) && ia.Y < Math.Max(ic.Y, id.Y)) return true;
            if (ia.X == ib.X && ic.Y == id.Y &&
                ic.Y > Math.Min(ia.Y, ib.Y) && ic.Y < Math.Max(ia.Y, ib.Y) &&
                ia.X > Math.Min(ic.X, id.X) && ia.X < Math.Max(ic.X, id.X)) return true;
        }
        return false;
    }

    /// <summary>Every count satisfied and all islands connected (union-find).</summary>
    private bool Solved()
    {
        for (int i = 0; i < _islands.Count; i++)
            if (BridgeCount(i) != _islands[i].Count) return false;

        var parent = Enumerable.Range(0, _islands.Count).ToArray();
        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }
        foreach (var (a, b, _) in _bridges) parent[Find(a)] = Find(b);
        int root = Find(0);
        return _islands.Count == 1 || Enumerable.Range(1, _islands.Count - 1).All(i => Find(i) == root);
    }

    private void LevelUp()
    {
        _score += 200 + 25 * _level;
        if (_level == Levels)
        {
            _done = true;
            ScoreCounts = true;
            _result = $"All {Levels} puzzles bridged — {_score} points.";
            return;
        }
        _level++;
        LoadLevel();
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

        // Draw each bridge onto the water cells it passes over; a double
        // bridge uses = / | so it reads differently from a single one.
        foreach (var (a, b, n) in _bridges)
        {
            var ia = _islands[a];
            var ib = _islands[b];
            if (ia.Y == ib.Y)
            {
                char ch = n == 1 ? '─' : '=';
                for (int x = Math.Min(ia.X, ib.X) + 1; x < Math.Max(ia.X, ib.X); x++)
                {
                    var c = _cells[ia.Y * Side + x];
                    c.Text = ch.ToString();
                    c.Foreground = n == 1 ? Muted : Ink;
                    c.Bold = n > 1;
                }
            }
            else
            {
                char ch = n == 1 ? '│' : '|';
                for (int y = Math.Min(ia.Y, ib.Y) + 1; y < Math.Max(ia.Y, ib.Y); y++)
                {
                    var c = _cells[y * Side + ia.X];
                    c.Text = ch.ToString();
                    c.Foreground = n == 1 ? Muted : Ink;
                    c.Bold = n > 1;
                }
            }
        }

        for (int i = 0; i < _islands.Count; i++)
        {
            var isle = _islands[i];
            var c = _cells[isle.Y * Side + isle.X];
            int cnt = BridgeCount(i);
            c.Text = isle.Count.ToString();
            c.FontSize = 20;
            c.Bold = true;
            if (_sel == i)
            {
                c.Background = Accent;
                c.Foreground = Cell;
            }
            else if (cnt == isle.Count)
            {
                c.Background = Good;
                c.Foreground = Cell;
            }
            else
            {
                c.Background = Cell;
                c.Foreground = Ink;
            }
            c.IsEnabled = !_done;
        }
    }
}
