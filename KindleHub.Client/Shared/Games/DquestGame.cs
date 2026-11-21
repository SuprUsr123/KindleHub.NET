using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Dungeon Quest — the Kindle edition of the 3D crawler: first person, but turn
/// by turn. Turn ninety degrees, step forward or back, and the little window
/// ahead redraws. Find the two keys, loot five treasures and mind the traps —
/// they hide as plain floor until you step on one. "Map" overlays a mini-map of
/// everywhere you have been, with you and the exit marked.
/// </summary>
public sealed class DquestGame : GameBase
{
    public const int Size = 9;              // the maze is 9x9, border walls included
    private const int Cols = 9;
    private const int Rows = 14;
    private const int CompassBase = 3;      // compass strip, row 0 cols 3-6
    private const int ViewTop = 9;          // viewport rows 1-3, window in cols 3-5
    private const int TurnLeftCell = 36;    // controls + readouts, row 4
    private const int ForwardCell = 37;
    private const int BackCell = 38;
    private const int TurnRightCell = 39;
    private const int MapCell = 40;
    private const int HpCell = 41;
    private const int KeysCell = 42;
    private const int LootCell = 43;
    private const int MapBase = 45;         // 9x9 mini-map, rows 5-13
    private const int CellCount = Cols * Rows;
    private const int HeroHpMax = 20;
    private const int TreasureTotal = 5;
    private const int KeyCount = 2;

    private const int WallTile = 0, FloorTile = 1, KeyTile = 2, DoorTile = 3,
                      GoldTile = 4, TrapTile = 5, ExitTile = 6;

    private static readonly (int X, int Y)[] Dirs = { (0, -1), (1, 0), (0, 1), (-1, 0) };
    private static readonly string[] Compass = { "N ↑", "E →", "S ↓", "W ←" };
    private static readonly string[] RowLabels = { "FAR", "MID", "NEAR" };

    private static readonly IBrush WallBrush = new SolidColorBrush(Color.Parse("#85858d"));

    private readonly List<GameCell> _cells = new();
    private readonly int[] _map = new int[Size * Size];
    private readonly bool[] _visited = new bool[Size * Size];
    private int _hx = 1;
    private int _hy = 1;
    private int _dir = 1;                   // start facing East
    private int _hp;
    private int _keys;
    private int _loot;
    private bool _mapOn;
    private bool _dead;
    private bool _won;
    private string _msg = "";

    public DquestGame()
    {
        for (int i = 0; i < CellCount; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "dquest";
    public override string Name => "Dungeon Quest";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => 10 * _loot + (_won ? 150 : 0);

    public override string? ResultText => _won
        ? $"Escaped with {_loot} treasures."
        : _dead ? $"Died in the caves · {_loot} treasures looted." : null;

    public override string StatusText
    {
        get
        {
            if (_won) return $"Escaped · {Score} points. New game carves a new maze.";
            if (_dead) return _loot > 0 ? $"You died · {Score} points banked." : "You died in the dark.";
            string extra = _msg.Length > 0 ? _msg : "find the exit E";
            return $"HP {_hp} · keys {_keys} · loot {10 * _loot} · {extra}";
        }
    }

    public override void Reset()
    {
        _hx = 1;
        _hy = 1;
        _dir = 1;
        _hp = HeroHpMax;
        _keys = 0;
        _loot = 0;
        _mapOn = false;
        _dead = false;
        _won = false;
        _msg = "";
        ScoreCounts = false;
        Array.Clear(_visited, 0, _visited.Length);
        Carve();
        Populate();
        UpdateView();
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_dead || _won) return false;
        return key switch
        {
            GameKey.Left => Rotate(-1),
            GameKey.Right => Rotate(1),
            GameKey.Up => Move(1),
            GameKey.Down => Move(-1),
            _ => false,
        };
    }

    public override bool OnTap(int index)
    {
        if (_dead || _won || index < 0 || index >= _cells.Count) return false;
        switch (index)
        {
            case TurnLeftCell: return Rotate(-1);
            case ForwardCell: return Move(1);
            case BackCell: return Move(-1);
            case TurnRightCell: return Rotate(1);
            case MapCell:
                _mapOn = !_mapOn;
                Redraw();
                return true;
            default:
                return false;
        }
    }

    private bool Rotate(int delta)
    {
        _dir = (_dir + delta + 4) % 4;
        _msg = "";
        UpdateView();
        Redraw();
        return true;
    }

    /// <summary>Walk one tile forward (sign 1) or back (sign -1).</summary>
    private bool Move(int sign)
    {
        var (fx, fy) = Dirs[_dir];
        int nx = _hx + fx * sign, ny = _hy + fy * sign;
        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) return false;
        int i = ny * Size + nx;
        int t = _map[i];

        if (t == WallTile)
        {
            _msg = "A wall blocks the way";
            Redraw();
            return true;
        }

        if (t == DoorTile)
        {
            if (_keys > 0)
            {
                _keys--;
                _map[i] = FloorTile;
                _msg = _keys == 0
                    ? "Door open - that was the last key!"
                    : "Door unlocked";
            }
            else
            {
                _msg = "Locked - you need a key";
                Redraw();
                return true;
            }
        }

        _hx = nx;
        _hy = ny;
        _visited[i] = true;

        switch (_map[i])
        {
            case KeyTile:
                _keys++;
                _map[i] = FloorTile;
                _msg = "Picked up a key";
                break;
            case GoldTile:
                _loot++;
                _map[i] = FloorTile;
                _msg = "Treasure! +10";
                break;
            case TrapTile:
                _hp -= 5;
                _map[i] = FloorTile;
                _msg = "Trap! -5 HP";
                break;
            case ExitTile:
                if (_keys >= 1)
                {
                    _won = true;
                    ScoreCounts = true;
                    _msg = "Escaped the caves";
                    Redraw();
                    return true;
                }
                _msg = "The exit needs a key";
                break;
        }

        if (_hp <= 0)
        {
            _dead = true;
            ScoreCounts = _loot > 0;
            _msg = "You died in the dark";
        }

        UpdateView();
        Redraw();
        return true;
    }

    /// <summary>Marks the hero's tile and everything the window can see as visited.</summary>
    private void UpdateView()
    {
        _visited[_hy * Size + _hx] = true;
        var (fx, fy) = Dirs[_dir];
        int rx = -fy, ry = fx;
        for (int d = 1; d <= 3; d++)
            for (int l = -1; l <= 1; l++)
            {
                int x = _hx + fx * d + rx * l;
                int y = _hy + fy * d + ry * l;
                if (x >= 0 && y >= 0 && x < Size && y < Size) _visited[y * Size + x] = true;
            }
    }

    /// <summary>Recursive-backtracker maze on the odd cells, so every tile connects.</summary>
    private void Carve()
    {
        Array.Clear(_map, 0, _map.Length);
        var stack = new Stack<(int x, int y)>();
        stack.Push((1, 1));
        _map[Size + 1] = FloorTile;
        while (stack.Count > 0)
        {
            var (x, y) = stack.Peek();
            var dirs = new List<(int dx, int dy)> { (0, -2), (2, 0), (0, 2), (-2, 0) };
            for (int i = dirs.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }
            bool carved = false;
            foreach (var (dx, dy) in dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx <= 0 || ny <= 0 || nx >= Size - 1 || ny >= Size - 1) continue;
                if (_map[ny * Size + nx] != WallTile) continue;
                _map[(y + dy / 2) * Size + (x + dx / 2)] = FloorTile;
                _map[ny * Size + nx] = FloorTile;
                stack.Push((nx, ny));
                carved = true;
                break;
            }
            if (!carved) stack.Pop();
        }
    }

    private void Populate()
    {
        int start = _hy * Size + _hx;
        var floors = new List<int>();
        for (int i = 0; i < _map.Length; i++)
            if (_map[i] == FloorTile) floors.Add(i);
        int[] dist = Distances(start, -1);

        // Exit: the farthest junction, so it is never a dead end.
        int exit = -1, best = -1;
        foreach (var i in floors)
            if (i != start && Degree(i) >= 2 && dist[i] > best)
            {
                best = dist[i];
                exit = i;
            }
        if (exit < 0)
        {
            best = -1;
            foreach (var i in floors)
                if (i != start && dist[i] > best)
                {
                    best = dist[i];
                    exit = i;
                }
        }
        _map[exit] = ExitTile;

        // Keys: two junctions a little way from the start, with a fallback so
        // the exit is always openable.
        var keys = new List<int>();
        var junctions = new List<int>();
        foreach (var i in floors)
            if (i != start && _map[i] == FloorTile && Degree(i) >= 2 && dist[i] >= 4)
                junctions.Add(i);
        Shuffle(junctions);
        for (int k = 0; k < KeyCount && k < junctions.Count; k++)
        {
            _map[junctions[k]] = KeyTile;
            keys.Add(junctions[k]);
        }
        if (keys.Count < KeyCount)
        {
            var spare = new List<int>();
            foreach (var i in floors)
                if (i != start && _map[i] == FloorTile && dist[i] >= 2) spare.Add(i);
            Shuffle(spare);
            for (int k = 0; k < spare.Count && keys.Count < KeyCount; k++)
            {
                _map[spare[k]] = KeyTile;
                keys.Add(spare[k]);
            }
        }

        // Two locked doors seal dead-end vaults with a treasure behind each; a
        // door must never cut off the exit, a key or an earlier vault.
        var deadEnds = new List<int>();
        foreach (var i in floors)
            if (_map[i] == FloorTile && Degree(i) == 1 && dist[i] >= 3) deadEnds.Add(i);
        Shuffle(deadEnds);
        int vaults = 0;
        var vaultGold = new List<int>();
        foreach (var q in deadEnds)
        {
            if (vaults >= 2) break;
            if (_map[q] != FloorTile) continue;
            int p = Neighbor(q);
            if (p < 0 || _map[p] != FloorTile) continue;
            int[] reach = Distances(start, p);
            bool ok = reach[exit] >= 0;
            for (int k = 0; k < keys.Count && ok; k++)
                if (reach[keys[k]] < 0) ok = false;
            for (int k = 0; k < vaultGold.Count && ok; k++)
                if (reach[vaultGold[k]] < 0) ok = false;
            if (!ok) continue;
            _map[p] = DoorTile;
            _map[q] = GoldTile;
            vaultGold.Add(q);
            vaults++;
        }

        // Free treasure up to five, then traps on a quarter of what is left.
        var free = new List<int>();
        foreach (var i in floors)
            if (_map[i] == FloorTile && i != start && dist[i] >= 2) free.Add(i);
        Shuffle(free);
        int goldLeft = TreasureTotal - vaults;
        for (int k = 0; k < goldLeft && k < free.Count; k++) _map[free[k]] = GoldTile;

        var rest = new List<int>();
        foreach (var i in floors)
            if (_map[i] == FloorTile && i != start) rest.Add(i);
        Shuffle(rest);
        int traps = rest.Count / 4;
        for (int k = 0; k < traps; k++) _map[rest[k]] = TrapTile;
    }

    private int Degree(int i)
    {
        int x = i % Size, y = i / Size, n = 0;
        if (x > 0 && _map[i - 1] != WallTile) n++;
        if (x < Size - 1 && _map[i + 1] != WallTile) n++;
        if (y > 0 && _map[i - Size] != WallTile) n++;
        if (y < Size - 1 && _map[i + Size] != WallTile) n++;
        return n;
    }

    private int Neighbor(int i)
    {
        int x = i % Size, y = i / Size;
        if (x > 0 && _map[i - 1] != WallTile) return i - 1;
        if (x < Size - 1 && _map[i + 1] != WallTile) return i + 1;
        if (y > 0 && _map[i - Size] != WallTile) return i - Size;
        if (y < Size - 1 && _map[i + Size] != WallTile) return i + Size;
        return -1;
    }

    /// <summary>BFS distances from a tile, never entering the avoided one.</summary>
    private int[] Distances(int from, int avoid)
    {
        var dist = new int[Size * Size];
        Array.Fill(dist, -1);
        if (from == avoid) return dist;
        dist[from] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % Size, y = i / Size;
            foreach (var n in new[] { x > 0 ? i - 1 : -1, x < Size - 1 ? i + 1 : -1, y > 0 ? i - Size : -1, y < Size - 1 ? i + Size : -1 })
            {
                if (n < 0 || n == avoid || dist[n] >= 0 || _map[n] == WallTile) continue;
                dist[n] = dist[i] + 1;
                queue.Enqueue(n);
            }
        }
        return dist;
    }

    private static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
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
            c.FontSize = 16;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        DrawCompass();
        DrawRowLabels();
        DrawViewport();

        bool live = !_dead && !_won;
        Control(TurnLeftCell, "Turn ←", live);
        Control(ForwardCell, "Fwd ↑", live);
        Control(BackCell, "Back ↓", live);
        Control(TurnRightCell, "Turn →", live);
        Control(MapCell, _mapOn ? "Map ●" : "Map", live);

        var hp = _cells[HpCell];
        hp.Text = $"HP {_hp}";
        hp.Background = _hp <= 5 ? Bad : Cell;
        hp.Foreground = _hp <= 5 ? Cell : Ink;
        hp.FontSize = 13;
        hp.Bold = true;

        Readout(KeysCell, $"Keys {_keys}");
        Readout(LootCell, $"Loot {_loot}");

        DrawMiniMap();
    }

    private void DrawCompass()
    {
        for (int d = 0; d < 4; d++)
        {
            var c = _cells[CompassBase + d];
            c.Text = Compass[d];
            c.Background = d == _dir ? Accent : Board;
            c.Foreground = d == _dir ? Cell : Muted;
            c.FontSize = 14;
            c.Bold = d == _dir;
        }
    }

    private void DrawRowLabels()
    {
        for (int r = 0; r < RowLabels.Length; r++)
        {
            var c = _cells[ViewTop + r * Cols + 2];
            c.Text = RowLabels[r];
            c.Foreground = Muted;
            c.FontSize = 10;
        }
    }

    /// <summary>The window: near/mid/far rows, with walls occluding what is behind them.</summary>
    private void DrawViewport()
    {
        var (fx, fy) = Dirs[_dir];
        int rx = -fy, ry = fx;
        int[] block = { 99, 99, 99 };   // first wall depth per column, centre included
        for (int d = 1; d <= 3; d++)
        {
            int rowBase = ViewTop + (3 - d) * Cols;   // d=3 is the far row
            for (int l = -1; l <= 1; l++)
            {
                var c = _cells[rowBase + 3 + l + 1];
                int x = _hx + fx * d + rx * l;
                int y = _hy + fy * d + ry * l;
                int t = x < 0 || y < 0 || x >= Size || y >= Size ? WallTile : _map[y * Size + x];
                if (t == WallTile) block[l + 1] = Math.Min(block[l + 1], d);

                bool hidden = d > block[l + 1] || d > block[1];
                if (hidden || t == WallTile)
                {
                    c.Text = "▓";
                    c.Background = WallBrush;
                    c.Foreground = Cell;
                    c.FontSize = 20;
                    c.Bold = true;
                    continue;
                }

                // Traps fall to the default on purpose: they look like floor.
                switch (t)
                {
                    case KeyTile: c.Text = "K"; c.Background = Accent; c.Foreground = Cell; c.Bold = true; break;
                    case DoorTile: c.Text = "D"; c.Background = Warn; c.Foreground = Cell; c.Bold = true; break;
                    case GoldTile: c.Text = "$"; c.Background = Cell; c.Foreground = Warn; c.Bold = true; break;
                    case ExitTile: c.Text = "E"; c.Background = Good; c.Foreground = Cell; c.Bold = true; break;
                    default: c.Text = ""; c.Background = Cell; c.Foreground = Ink; c.Bold = false; break;
                }
                c.FontSize = 18;
            }
        }
    }

    /// <summary>Visited cells, remembered items, and the hero and exit marked.</summary>
    private void DrawMiniMap()
    {
        if (!_mapOn) return;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                var c = _cells[MapBase + y * Cols + x];
                // The exit is always marked so the map doubles as a goal marker.
                if (!_visited[i] && _map[i] != ExitTile) continue;
                switch (_map[i])
                {
                    case WallTile:
                        c.Text = "▓";
                        c.Background = WallBrush;
                        c.Foreground = Cell;
                        break;
                    case KeyTile:
                        c.Text = "K";
                        c.Background = Accent;
                        c.Foreground = Cell;
                        c.Bold = true;
                        break;
                    case DoorTile:
                        c.Text = "D";
                        c.Background = Warn;
                        c.Foreground = Cell;
                        c.Bold = true;
                        break;
                    case GoldTile:
                        c.Text = "$";
                        c.Background = Cell;
                        c.Foreground = Warn;
                        c.Bold = true;
                        break;
                    case ExitTile:
                        c.Text = "E";
                        c.Background = Good;
                        c.Foreground = Cell;
                        c.Bold = true;
                        break;
                    default:
                        c.Background = Cell;
                        break;
                }
                if (x == _hx && y == _hy)
                {
                    c.Text = "●";
                    c.Background = Ink;
                    c.Foreground = Cell;
                    c.Bold = true;
                }
            }
    }

    private void Control(int index, string text, bool enabled)
    {
        var c = _cells[index];
        c.Text = text;
        c.Background = Cell;
        c.Foreground = Ink;
        c.IsEnabled = enabled;
        c.FontSize = 14;
        c.Bold = true;
        c.BorderBrush = Accent;
        c.BorderThickness = new Avalonia.Thickness(1);
    }

    private void Readout(int index, string text)
    {
        var c = _cells[index];
        c.Text = text;
        c.Background = Cell;
        c.Foreground = Ink;
        c.FontSize = 13;
        c.Bold = true;
    }
}
