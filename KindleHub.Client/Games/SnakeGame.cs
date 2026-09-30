using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>Snake — the official client's grid snake: eat food, grow, don't hit a wall or yourself.</summary>
public sealed class SnakeGame : GameBase
{
    public const int Size = 20;

    private readonly List<GameCell> _cells = new();
    private readonly List<(int x, int y)> _snake = new();
    private readonly List<(int x, int y)> _queue = new();
    private (int x, int y) _food;
    private (int dx, int dy) _dir = (dx: 1, dy: 0);
    private TimeSpan _sinceMove;
    private TimeSpan _step;
    private int _score;
    private bool _dead;

    public SnakeGame()
    {
        for (int i = 0; i < Size * Size; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "snake";
    public override string Name => "Snake";
    public override bool UsesArrowKeys => true;
    public override int Columns => Size;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;
    public override string? ResultText => _dead ? $"Game over — {Score} points." : null;

    public override int Score => _score;

    public override string StatusText =>
        _dead ? $"Final score {_score}." : $"Score {_score} · arrows or WASD";

    public override void Reset()
    {
        _snake.Clear();
        _snake.Add((Size / 2, Size / 2));
        _snake.Add((Size / 2 - 1, Size / 2));
        _dir = (1, 0);
        _queue.Clear();
        _score = 0;
        _dead = false;
        ScoreCounts = false;
        _step = TimeSpan.FromMilliseconds(150);
        _sinceMove = TimeSpan.Zero;
        PlaceFood();
        Redraw();
    }

    private void PlaceFood()
    {
        var free = new List<(int x, int y)>();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (!_snake.Contains((x, y))) free.Add((x, y));
        _food = free.Count == 0 ? (0, 0) : free[Random.Shared.Next(free.Count)];
    }

    public override void Tick(TimeSpan elapsed)
    {
        if (_dead) return;
        _sinceMove += elapsed;
        // Catch up at most a few steps so a slow frame can't teleport the snake.
        int guard = 0;
        while (_sinceMove >= _step && guard++ < 4)
        {
            _sinceMove -= _step;
            Step();
            if (_dead) return;
        }
    }

    private void Step()
    {
        if (_queue.Count > 0) _dir = _queue[0];
        _queue.Clear();

        var (x, y) = _snake[0];
        int nx = x + _dir.dx, ny = y + _dir.dy;

        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size || _snake.Take(_snake.Count - 1).Contains((nx, ny)))
        {
            _dead = true;
            // Snake always has a meaningful score, so a finished run always posts.
            ScoreCounts = true;
            Redraw();
            return;
        }

        _snake.Insert(0, (nx, ny));
        if ((nx, ny) == _food)
        {
            _score += 10;
            // Speed up gradually, same shape the web game uses.
            var ms = Math.Max(65, 130 - _score / 5);
            _step = TimeSpan.FromMilliseconds(ms);
            PlaceFood();
        }
        else
        {
            _snake.RemoveAt(_snake.Count - 1);
        }
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_dead) return false;
        var (dx, dy) = key switch
        {
            GameKey.Up => (0, -1),
            GameKey.Down => (0, 1),
            GameKey.Left => (-1, 0),
            GameKey.Right => (1, 0),
            _ => (0, 0),
        };
        if ((dx, dy) == (0, 0)) return false;

        // Reject a reversal outright, and coalesce a double-press in one frame.
        (int dx, int dy) last = _queue.Count > 0 ? _queue[^1] : _dir;
        if ((dx, dy) == (-last.dx, -last.dy)) return true;
        if ((dx, dy) == last) return true;
        if (_queue.Count < 2) _queue.Add((dx, dy));
        return true;
    }

    public override bool OnTap(int index)
    {
        // Tapping the board steers toward that cell — handy on a touch screen.
        var (hx, hy) = _snake[0];
        int cx = index % Size, cy = index / Size;
        int vx = cx - hx, vy = cy - hy;
        if (Math.Abs(vx) >= Math.Abs(vy)) OnKey(vx > 0 ? GameKey.Right : GameKey.Left);
        else OnKey(vy > 0 ? GameKey.Down : GameKey.Up);
        return true;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++) _cells[i] = Blank();
        var food = _food.y * Size + _food.x;
        _cells[food].Background = Bad;
        _cells[food].IsEnabled = false;
        for (int i = 0; i < _snake.Count; i++)
        {
            var (x, y) = _snake[i];
            var c = _cells[y * Size + x];
            c.Background = i == 0 ? Accent : Good;
            c.Foreground = Cell;
        }
    }
}
