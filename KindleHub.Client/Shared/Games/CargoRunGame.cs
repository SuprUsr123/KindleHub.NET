using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Cargo Run - collect every crate and finish at the depot before the fuel runs
/// dry. Each step costs one fuel, so the route matters more than the reflexes;
/// fuel cans top the tank back up by eight. Maps are re-rolled until every
/// pickup is provably reachable from the start.
/// </summary>
public sealed class CargoRunGame : GameBase
{
    public const int Size = 11;

    private const int CrateCount = 5;
    private const int CanCount = 3;
    private const int StartFuel = 40;
    private const int CanFuel = 8;
    private const int ExtraWalls = 14;

    private const int Floor = 0, Wall = 1, Crate = 2, Depot = 3, Can = 4;

    private static readonly IBrush WallBrush = new SolidColorBrush(Color.Parse("#85858d"));

    private readonly List<GameCell> _cells = new();
    private readonly int[] _map = new int[Size * Size];
    private int _px;
    private int _py;
    private int _fuel;
    private int _left;
    private int _collected;
    private bool _over;
    private bool _won;

    public CargoRunGame()
    {
        for (int i = 0; i < Size * Size; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "cargorun";
    public override string Name => "Cargo Run";
    public override int Columns => Size;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => 50 * _collected + 2 * Math.Max(0, _fuel) + (_won ? 100 : 0);

    public override string? ResultText => !_over ? null
        : _won ? $"All {CrateCount} crates delivered with {_fuel} fuel to spare."
        : $"Out of fuel - {_collected}/{CrateCount} crates collected.";

    public override string StatusText => _over
        ? $"Final haul {_collected}/{CrateCount}. New game re-rolls the map."
        : $"Fuel {_fuel} · Crates {_collected}/{CrateCount} · then reach D";

    public override void Reset()
    {
        _fuel = StartFuel;
        _collected = 0;
        _left = CrateCount;
        _over = false;
        _won = false;
        ScoreCounts = false;
        Generate();
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        return key switch
        {
            GameKey.Up => Step(0, -1),
            GameKey.Down => Step(0, 1),
            GameKey.Left => Step(-1, 0),
            GameKey.Right => Step(1, 0),
            _ => false,
        };
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        int cx = index % Size, cy = index / Size;
        int dx = cx - _px, dy = cy - _py;
        if (dx == 0 && dy == 0) return true;
        if (Math.Abs(dx) + Math.Abs(dy) == 1)
        {
            Step(dx, dy);
            return true;
        }
        // Far taps take one greedy step toward the target, wall permitting.
        return Math.Abs(dx) >= Math.Abs(dy)
            ? Step(Math.Sign(dx), 0) || Step(0, Math.Sign(dy))
            : Step(0, Math.Sign(dy)) || Step(Math.Sign(dx), 0);
    }

    /// <summary>One step costs one fuel; blocked steps are free but go nowhere.</summary>
    private bool Step(int dx, int dy)
    {
        if (_over) return false;
        int nx = _px + dx, ny = _py + dy;
        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) return false;
        int i = ny * Size + nx;
        if (_map[i] == Wall) return false;

        _px = nx;
        _py = ny;
        _fuel--;

        if (_map[i] == Crate)
        {
            _map[i] = Floor;
            _left--;
            _collected++;
        }
        else if (_map[i] == Can)
        {
            _map[i] = Floor;
            _fuel += CanFuel;
        }

        if (_map[i] == Depot && _left == 0)
        {
            _over = true;
            _won = true;
            ScoreCounts = true;
            Redraw();
            return true;
        }

        if (_fuel <= 0)
        {
            _over = true;
            _won = false;
            ScoreCounts = _collected > 0;
            Redraw();
            return true;
        }

        Redraw();
        return true;
    }

    private void Generate()
    {
        for (int attempt = 0; ; attempt++)
        {
            Array.Clear(_map, 0, _map.Length);
            for (int x = 0; x < Size; x++)
            {
                _map[x] = Wall;
                _map[(Size - 1) * Size + x] = Wall;
            }
            for (int y = 0; y < Size; y++)
            {
                _map[y * Size] = Wall;
                _map[y * Size + Size - 1] = Wall;
            }

            int placed = 0;
            while (placed < ExtraWalls)
            {
                int i = Random.Shared.Next(1, Size - 1) * Size + Random.Shared.Next(1, Size - 1);
                if (_map[i] == Floor)
                {
                    _map[i] = Wall;
                    placed++;
                }
            }

            _px = Size / 2;
            _py = Size / 2;
            _map[_py * Size + _px] = Floor;

            var free = Enumerable.Range(0, Size * Size)
                .Where(i => _map[i] == Floor && i != _py * Size + _px)
                .ToList();
            Shuffle(free);
            if (free.Count >= CrateCount + CanCount + 1 && TryLay(free)) return;

            if (attempt >= 50)
            {
                // Fallback: an open yard with walls only along the border.
                Array.Clear(_map, 0, _map.Length);
                for (int x = 0; x < Size; x++)
                {
                    _map[x] = Wall;
                    _map[(Size - 1) * Size + x] = Wall;
                }
                for (int y = 0; y < Size; y++)
                {
                    _map[y * Size] = Wall;
                    _map[y * Size + Size - 1] = Wall;
                }
                var open = Enumerable.Range(0, Size * Size)
                    .Where(i => _map[i] == Floor && i != _py * Size + _px)
                    .ToList();
                Shuffle(open);
                TryLay(open);
                return;
            }
        }
    }

    private bool TryLay(List<int> free)
    {
        _map[free[0]] = Depot;
        for (int i = 1; i <= CrateCount; i++) _map[free[i]] = Crate;
        for (int i = CrateCount + 1; i <= CrateCount + CanCount; i++) _map[free[i]] = Can;
        return AllReachable();
    }

    /// <summary>Breadth-first check that nothing the run needs sits behind walls.</summary>
    private bool AllReachable()
    {
        var seen = new bool[Size * Size];
        var queue = new Queue<int>();
        int start = _py * Size + _px;
        queue.Enqueue(start);
        seen[start] = true;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % Size, y = i / Size;
            if (x > 0 && !seen[i - 1] && _map[i - 1] != Wall) { seen[i - 1] = true; queue.Enqueue(i - 1); }
            if (x < Size - 1 && !seen[i + 1] && _map[i + 1] != Wall) { seen[i + 1] = true; queue.Enqueue(i + 1); }
            if (y > 0 && !seen[i - Size] && _map[i - Size] != Wall) { seen[i - Size] = true; queue.Enqueue(i - Size); }
            if (y < Size - 1 && !seen[i + Size] && _map[i + Size] != Wall) { seen[i + Size] = true; queue.Enqueue(i + Size); }
        }
        for (int i = 0; i < _map.Length; i++)
            if ((_map[i] == Crate || _map[i] == Can || _map[i] == Depot) && !seen[i]) return false;
        return true;
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
            c.Bold = false;
            c.FontSize = 18;
            c.IsEnabled = !_over;
            c.Foreground = Ink;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
            switch (_map[i])
            {
                case Wall:
                    c.Text = "▓";
                    c.Background = WallBrush;
                    c.Foreground = Cell;
                    break;
                case Crate:
                    c.Text = "◆";
                    c.Background = Board;
                    c.Foreground = Warn;
                    c.Bold = true;
                    break;
                case Depot:
                    c.Text = "D";
                    c.Background = Accent;
                    c.Foreground = Cell;
                    c.Bold = true;
                    break;
                case Can:
                    c.Text = "F";
                    c.Background = Good;
                    c.Foreground = Cell;
                    c.Bold = true;
                    break;
                default:
                    c.Text = "";
                    c.Background = Cell;
                    break;
            }
        }

        var hero = _cells[_py * Size + _px];
        hero.Text = "●";
        hero.Background = Ink;
        hero.Foreground = Cell;
        hero.Bold = true;
    }
}
