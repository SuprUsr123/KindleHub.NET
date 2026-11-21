using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Deep Halls — a turn-based dungeon crawler: move with the arrow keys or tap a
/// square next to you, walk into a monster to battle it, take the stairs deeper.
/// Nothing moves until you do. The amulet sits on depth 3, and the exit stairs
/// there stay sealed until you carry it.
/// </summary>
public sealed class DeepHallsGame : GameBase
{
    public const int Width = 13;
    public const int Height = 9;

    private const int WallTile = 0, FloorTile = 1, PotionTile = 2, StairsTile = 3, AmuletTile = 4;
    private const int HeroHpMax = 20;
    private const int HeroAtk = 3;

    private static readonly IBrush WallBrush = new SolidColorBrush(Color.Parse("#85858d"));

    private sealed class Monster
    {
        public int X, Y, Hp, Kind;     // 0 rat, 1 bat, 2 skeleton
        public int Atk => Kind == 0 ? 1 : Kind == 1 ? 2 : 3;
        public int MaxHp => Kind == 0 ? 3 : Kind == 1 ? 4 : 8;
        public string Name => Kind == 0 ? "Rat" : Kind == 1 ? "Bat" : "Skeleton";
        public string Glyph => Kind == 0 ? "r" : Kind == 1 ? "b" : "s";
        public IBrush Brush => Kind == 0 ? Muted : Kind == 1 ? Warn : Bad;
    }

    private readonly List<GameCell> _cells = new();
    private readonly int[] _map = new int[Width * Height];
    private readonly List<Monster> _monsters = new();
    private int _hx;
    private int _hy;
    private int _hp;
    private int _depth;
    private int _kills;
    private bool _amulet;
    private bool _dead;
    private bool _won;
    private string _msg = "";

    public DeepHallsGame()
    {
        for (int i = 0; i < Width * Height; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "deephalls";
    public override string Name => "Deep Halls";
    public override int Columns => Width;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score =>
        100 * _depth + 10 * _kills + (_amulet ? 200 : 0) + (_won ? 300 : 0);

    public override string? ResultText => _won
        ? "Escaped the halls with the amulet!"
        : _dead ? $"Slain on depth {_depth} · {_kills} kills." : null;

    public override string StatusText
    {
        get
        {
            if (_won) return $"Escaped · {Score} points. New game digs fresh halls.";
            if (_dead) return $"You died on depth {_depth} · {Score} points.";
            string extra = _msg.Length > 0 ? _msg : _amulet ? "reach the stairs >" : "find the stairs >";
            return $"D{_depth} · HP {_hp}/{HeroHpMax} · ATK {HeroAtk} · {extra}";
        }
    }

    public override void Reset()
    {
        _depth = 1;
        _hp = HeroHpMax;
        _kills = 0;
        _amulet = false;
        _dead = false;
        _won = false;
        _msg = "";
        ScoreCounts = false;
        Generate();
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_dead || _won) return false;
        return key switch
        {
            GameKey.Up => Act(0, -1),
            GameKey.Down => Act(0, 1),
            GameKey.Left => Act(-1, 0),
            GameKey.Right => Act(1, 0),
            _ => false,
        };
    }

    public override bool OnTap(int index)
    {
        if (_dead || _won || index < 0 || index >= _cells.Count) return false;
        int cx = index % Width, cy = index / Width;
        int dx = cx - _hx, dy = cy - _hy;
        if (dx == 0 && dy == 0) return true;
        if (Math.Abs(dx) + Math.Abs(dy) == 1)
        {
            Act(dx, dy);
            return true;
        }
        // Far taps take one step toward the target, walls permitting.
        return Math.Abs(dx) >= Math.Abs(dy)
            ? Act(Math.Sign(dx), 0) || Act(0, Math.Sign(dy))
            : Act(0, Math.Sign(dy)) || Act(Math.Sign(dx), 0);
    }

    /// <summary>One hero action: a step, a pickup, a descend or a battle.</summary>
    private bool Act(int dx, int dy)
    {
        if (_dead || _won) return false;
        int nx = _hx + dx, ny = _hy + dy;
        if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) return false;
        int ni = ny * Width + nx;

        var mon = MonsterAt(nx, ny);
        if (mon != null)
        {
            Battle(mon);
            AfterTurn();
            Redraw();
            return true;
        }

        if (_map[ni] == WallTile) return false;
        _hx = nx;
        _hy = ny;

        if (_map[ni] == PotionTile)
        {
            _map[ni] = FloorTile;
            _hp = Math.Min(HeroHpMax, _hp + 8);
            _msg = "Potion +8 HP";
        }
        else if (_map[ni] == AmuletTile)
        {
            _map[ni] = FloorTile;
            _amulet = true;
            _msg = "Amulet taken!";
        }
        else if (_map[ni] == StairsTile)
        {
            if (_depth < 3)
            {
                _depth++;
                _msg = $"Depth {_depth}";
                Generate();
                Redraw();
                return true;
            }
            if (_amulet)
            {
                _won = true;
                ScoreCounts = true;
                _msg = "The stairs open!";
                Redraw();
                return true;
            }
            _msg = "Stairs sealed - find the amulet ★";
        }

        AfterTurn();
        Redraw();
        return true;
    }

    /// <summary>Walk into a monster and trade blows until one of you drops.</summary>
    private void Battle(Monster m)
    {
        while (true)
        {
            m.Hp -= HeroAtk;
            if (m.Hp <= 0)
            {
                _monsters.Remove(m);
                _kills++;
                _msg = $"{m.Name} slain";
                return;
            }
            _hp -= m.Atk;
            if (_hp <= 0)
            {
                _dead = true;
                ScoreCounts = true;
                _msg = $"Killed by {m.Name}";
                return;
            }
        }
    }

    /// <summary>The halls only move when you do: adjacent monsters strike, near ones close in.</summary>
    private void AfterTurn()
    {
        if (_dead || _won) return;
        foreach (var m in _monsters)
        {
            int dist = Math.Abs(m.X - _hx) + Math.Abs(m.Y - _hy);
            if (dist == 1)
            {
                _hp -= m.Atk;
                _msg = $"{m.Name} hits for {m.Atk}";
            }
            else if (dist <= 4)
            {
                StepToward(m);
            }
        }
        if (_hp <= 0)
        {
            _dead = true;
            ScoreCounts = true;
        }
    }

    private void StepToward(Monster m)
    {
        int dx = Math.Sign(_hx - m.X), dy = Math.Sign(_hy - m.Y);
        bool preferX = Math.Abs(_hx - m.X) >= Math.Abs(_hy - m.Y);
        var steps = preferX ? new[] { (dx, 0), (0, dy) } : new[] { (0, dy), (dx, 0) };
        foreach (var (sx, sy) in steps)
        {
            if (sx == 0 && sy == 0) continue;
            if (Free(m.X + sx, m.Y + sy))
            {
                m.X += sx;
                m.Y += sy;
                return;
            }
        }
    }

    private bool Free(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height &&
        _map[y * Width + x] != WallTile &&
        !(x == _hx && y == _hy) &&
        MonsterAt(x, y) == null;

    private Monster? MonsterAt(int x, int y)
    {
        foreach (var m in _monsters)
            if (m.X == x && m.Y == y) return m;
        return null;
    }

    /// <summary>Rooms carved at random, tied together with L-shaped corridors.</summary>
    private void Generate()
    {
        _monsters.Clear();
        for (int i = 0; i < _map.Length; i++) _map[i] = WallTile;

        var centers = new List<(int x, int y)>();
        int rooms = 4 + Random.Shared.Next(3);
        for (int r = 0; r < rooms; r++)
        {
            int rw = 3 + Random.Shared.Next(4);
            int rh = 2 + Random.Shared.Next(3);
            int rx = Random.Shared.Next(1, Width - rw - 1);
            int ry = Random.Shared.Next(1, Height - rh - 1);
            for (int y = ry; y < ry + rh; y++)
                for (int x = rx; x < rx + rw; x++)
                    _map[y * Width + x] = FloorTile;
            centers.Add((rx + rw / 2, ry + rh / 2));
        }

        for (int r = 1; r < centers.Count; r++)
        {
            var (x0, y0) = centers[r - 1];
            var (x1, y1) = centers[r];
            int x = x0;
            while (x != x1)
            {
                _map[y0 * Width + x] = FloorTile;
                x += Math.Sign(x1 - x);
            }
            int y = y0;
            while (y != y1)
            {
                _map[y * Width + x1] = FloorTile;
                y += Math.Sign(y1 - y);
            }
            _map[y1 * Width + x1] = FloorTile;
        }

        _hx = centers[0].x;
        _hy = centers[0].y;
        _map[_hy * Width + _hx] = FloorTile;

        var floors = Enumerable.Range(0, _map.Length)
            .Where(i => _map[i] == FloorTile && Dist(i) >= 4)
            .ToList();
        Shuffle(floors);

        // Stairs sit in the last room, away from the hero.
        int stairs = -1;
        foreach (var i in floors)
            if (Dist(i) >= 6)
            {
                stairs = i;
                break;
            }
        if (stairs < 0) stairs = floors.Count > 0 ? floors[0] : _hy * Width + _hx;
        _map[stairs] = StairsTile;
        floors.Remove(stairs);

        // Monster counts scale with depth: 3, then 5, then 6 of the meaner mix.
        int rats = _depth == 1 ? 2 : _depth == 2 ? 2 : 1;
        int bats = _depth == 1 ? 1 : _depth == 2 ? 2 : 2;
        int skels = _depth == 1 ? 0 : _depth == 2 ? 1 : 3;
        SpawnKind(floors, 0, rats);
        SpawnKind(floors, 1, bats);
        SpawnKind(floors, 2, skels);

        for (int p = 0; p < 2 && floors.Count > 0; p++)
        {
            int i = floors[0];
            floors.RemoveAt(0);
            _map[i] = PotionTile;
        }

        if (_depth == 3 && floors.Count > 0)
        {
            int i = floors[0];
            floors.RemoveAt(0);
            _map[i] = AmuletTile;
        }
    }

    private int Dist(int i) => Math.Abs(i % Width - _hx) + Math.Abs(i / Width - _hy);

    private void SpawnKind(List<int> floors, int kind, int count)
    {
        for (int n = 0; n < count && floors.Count > 0; n++)
        {
            int i = floors[0];
            floors.RemoveAt(0);
            _monsters.Add(new Monster { X = i % Width, Y = i / Width, Hp = kind == 0 ? 3 : kind == 1 ? 4 : 8, Kind = kind });
        }
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
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
            c.Bold = false;
            c.FontSize = 17;
            c.IsEnabled = !_dead && !_won;
            c.Foreground = Ink;
            switch (_map[i])
            {
                case WallTile:
                    c.Text = "▓";
                    c.Background = WallBrush;
                    c.Foreground = Cell;
                    break;
                case PotionTile:
                    c.Text = "+";
                    c.Background = Good;
                    c.Foreground = Cell;
                    c.Bold = true;
                    break;
                case StairsTile:
                    c.Text = ">";
                    c.Background = Cell;
                    c.Foreground = Accent;
                    c.Bold = true;
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(1);
                    break;
                case AmuletTile:
                    c.Text = "★";
                    c.Background = Warn;
                    c.Foreground = Cell;
                    c.Bold = true;
                    break;
                default:
                    c.Text = "";
                    c.Background = Cell;
                    break;
            }
        }

        foreach (var m in _monsters)
        {
            var c = _cells[m.Y * Width + m.X];
            c.Text = m.Glyph;
            c.Background = m.Brush;
            c.Foreground = Cell;
            c.Bold = true;
        }

        var hero = _cells[_hy * Width + _hx];
        hero.Text = "●";
        hero.Background = Accent;
        hero.Foreground = Cell;
        hero.Bold = true;
    }
}
