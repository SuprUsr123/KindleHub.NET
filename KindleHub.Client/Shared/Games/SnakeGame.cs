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
    private bool _won;

    public SnakeGame()
    {
        for (int i = 0; i < Size * Size; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "snake";
    public override string Name => "Snake";
    public override int Columns => Size;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;
    public override string? ResultText => _won
        ? $"Board filled — {Score} points."
        : _dead ? $"Game over — {Score} points." : null;

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
        _won = false;
        ScoreCounts = false;
        _step = TimeSpan.FromMilliseconds(130);
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
        while (_sinceMove >= _step && guard++ < 4)
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
            if (!PlaceFood())
            {
                _won = true;
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

        // Nokia-style turns are buffered one at a time and applied on the next
        // grid step; this avoids two queued turns making the snake feel twitchy.
        (int dx, int dy) last = _queue.Count > 0 ? _queue[^1] : _dir;
        if ((dx, dy) == (-last.dx, -last.dy)) return true;
        if ((dx, dy) == last) return true;
        if (_queue.Count == 0) _queue.Add((dx, dy));
        return true;
    }

    public override bool OnTap(int index)
    {
        // Tapping the board steers toward that cell — handy on a touch screen.
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
            var food = _food.y * Size + _food.x;
            _cells[food].Background = Bad;
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
