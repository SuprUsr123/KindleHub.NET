using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Slither — the wrap-around snake variant, ported from the official client. Steer
/// toward where you want to go, eat the glowing dots to grow, and never bite your
/// own body. The board has no walls: every edge wraps.
/// </summary>
public sealed class SlitherGame : GameBase
{
    public const int Size = 18;
    private const int StartLength = 5;

    private const int MaxSteps = 4;   // catch-up guard: at most 4 snake steps per Tick
    private const int StartMs = 150;  // starting step interval
    private const int MinMs = 70;     // fastest step interval
    private const int SpeedUpMs = 2;  // faster per dot eaten

    private readonly List<GameCell> _cells = new();
    private readonly List<(int x, int y)> _snake = new();
    private readonly List<(int dx, int dy)> _queue = new();
    private (int x, int y) _food;
    private (int dx, int dy) _dir = (1, 0);
    private TimeSpan _sinceMove;
    private TimeSpan _step;
    private int _eaten;
    private int _finalScore;
    private bool _dead;
    private bool _won;

    public SlitherGame()
    {
        for (int i = 0; i < Size * Size; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "slither";
    public override string Name => "Slither";
    public override int Columns => Size;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(
        _dead || _won ? _finalScore : Math.Max(0, 10 * _snake.Count - 50), 0, 1_000_000);

    public override string? ResultText => _won
        ? $"Board filled — {Score} points."
        : _dead ? $"Game over — {Score} points." : null;

    public override string StatusText => _dead || _won
        ? $"Final score {Score}."
        : $"Length {_snake.Count} · arrows or tap to steer";

    public override void Reset()
    {
        _snake.Clear();
        int cx = Size / 2, cy = Size / 2;
        for (int i = 0; i < StartLength; i++) _snake.Add(((cx - i + Size) % Size, cy));
        _dir = (1, 0);
        _queue.Clear();
        _eaten = 0;
        _finalScore = 0;
        _dead = false;
        _won = false;
        ScoreCounts = false;
        _step = TimeSpan.FromMilliseconds(StartMs);
        _sinceMove = TimeSpan.Zero;
        PlaceFood();
        Redraw();
    }

    private bool PlaceFood()
    {
        var free = new List<(int x, int y)>();
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
            if (!_snake.Contains((x, y))) free.Add((x, y));
        if (free.Count == 0) return false;
        _food = free[Random.Shared.Next(free.Count)];
        return true;
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_dead || _won) return false;
        _sinceMove += elapsed;
        // Catch up at most a few steps so a slow frame can't teleport the snake.
        int guard = 0;
        bool changed = false;
        while (_sinceMove >= _step && guard++ < MaxSteps)
        {
            _sinceMove -= _step;
            Step();
            changed = true;
            if (_dead || _won) return true;
        }
        return changed;
    }

    private void Step()
    {
        if (_queue.Count > 0) _dir = _queue[0];
        _queue.Clear();

        var (x, y) = _snake[0];
        // No walls anywhere: both axes wrap.
        int nx = (x + _dir.dx + Size) % Size;
        int ny = (y + _dir.dy + Size) % Size;

        // Head-on into your own body ends the run; the tail cell is about to move.
        if (_snake.Take(_snake.Count - 1).Contains((nx, ny)))
        {
            _dead = true;
            _finalScore = Math.Max(10, 10 * _snake.Count - 50);
            ScoreCounts = true;
            Redraw();
            return;
        }

        _snake.Insert(0, (nx, ny));
        if ((nx, ny) == _food)
        {
            _eaten++;
            _step = TimeSpan.FromMilliseconds(Math.Max(MinMs, StartMs - _eaten * SpeedUpMs));
            if (!PlaceFood())
            {
                _won = true;
                _finalScore = Math.Max(10, 10 * _snake.Count - 50);
                ScoreCounts = true;
                Redraw();
                return;
            }
        }
        else
        {
            _snake.RemoveAt(_snake.Count - 1);
        }
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_dead || _won) return false;
        var (dx, dy) = key switch
        {
            GameKey.Up => (0, -1),
            GameKey.Down => (0, 1),
            GameKey.Left => (-1, 0),
            GameKey.Right => (1, 0),
            _ => (0, 0),
        };
        if ((dx, dy) == (0, 0)) return false;

        // One buffered turn at a time, applied on the next grid step.
        (int dx, int dy) last = _queue.Count > 0 ? _queue[^1] : _dir;
        if ((dx, dy) == (-last.dx, -last.dy)) return true;
        if ((dx, dy) == last) return true;
        if (_queue.Count == 0) _queue.Add((dx, dy));
        return true;
    }

    public override bool OnTap(int index)
    {
        // Absolute steering: tap where you want the head to go.
        if (_dead || _won || index < 0 || index >= _cells.Count) return false;
        var (hx, hy) = _snake[0];
        int cx = index % Size, cy = index / Size;
        int vx = cx - hx, vy = cy - hy;
        if (Math.Abs(vx) >= Math.Abs(vy)) OnKey(vx > 0 ? GameKey.Right : GameKey.Left);
        else OnKey(vy > 0 ? GameKey.Down : GameKey.Up);
        return true;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = !_dead && !_won;
            c.FontSize = 16;
            c.Bold = false;
        }

        if (!_won)
        {
            var food = _cells[_food.y * Size + _food.x];
            food.Text = "♦"; // the glowing dot
            food.Background = Bad;
            food.Foreground = Cell;
        }

        for (int i = 0; i < _snake.Count; i++)
        {
            var (x, y) = _snake[i];
            var c = _cells[y * Size + x];
            c.Background = i == 0 ? Accent : Good;
            c.Foreground = Cell;
        }
    }
}
