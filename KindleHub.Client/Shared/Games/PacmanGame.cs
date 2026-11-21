using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Pac-Man — steer round the maze and eat every dot while dodging the three
/// ghosts, ported from the official client. Power pellets turn the tables for a
/// few seconds; eaten ghosts respawn from the lair.
/// </summary>
public sealed class PacmanGame : GameBase
{
    private const int Size = 15;
    private const int Lives = 3;

    private const int FrameMs = 50;        // fixed simulation frame
    private const int MaxFrames = 6;       // catch-up guard: at most 6 frames per Tick
    private const int PlayerMs = 170;      // base player cadence, one cell per step
    private const int GhostMs = 210;       // base ghost cadence
    private const int FrightMs = 320;      // frightened ghosts are slower
    private const int FrightenFor = 6000;  // power pellet duration
    private const int LairFor = 4000;      // ghost respawn delay
    private const int FreezeFor = 1000;    // pause after losing a life
    private const int DotPoints = 10;
    private const int PowerPoints = 50;
    private const int GhostPoints = 50;
    private const int LevelPoints = 500;

    // Symmetric maze: # wall, . dot, o power pellet, ' ' empty corridor.
    private static readonly string[] Maze =
    {
        "###############",
        "#o.....#.....o#",
        "#.###.....###.#",
        "#.#...#.#...#.#",
        "#.#.#.....#.#.#",
        "#...#.###.#...#",
        "#.#.#.#...#.#.#",
        "#o#.#.....#.#o#",
        "#.#.###.###.#.#",
        "#...#.###.#...#",
        "#.#.#.....#.#.#",
        "#.#...#.#...#.#",
        "#.###.....###.#",
        "#o.....#.....o#",
        "###############",
    };

    private static readonly (int X, int Y)[] LairCells = { (6, 7), (7, 7), (8, 7) };
    private static readonly (int X, int Y) Start = (7, 12);

    private sealed class Ghost
    {
        public int X, Y, Dx, Dy;
        public IBrush Tint = Bad!;
        public bool InLair;
        public int LairIn;
    }

    private readonly List<GameCell> _cells = new();
    private readonly char[,] _grid = new char[Size, Size];
    private readonly List<Ghost> _ghosts = new();
    private int _px, _py;
    private (int dx, int dy) _dir = (1, 0);
    private (int dx, int dy) _nextDir;
    private int _dots;
    private int _level = 1;
    private int _lives;
    private int _score;
    private int _playerIn, _ghostIn, _frightIn, _freezeIn;
    private TimeSpan _acc;
    private bool _over;

    public PacmanGame()
    {
        for (int i = 0; i < Size * Size; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "pacman";
    public override string Name => "Pac-Man";
    public override int Columns => Size;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => _over ? $"Game over — {Score} points." : null;

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"Score {_score} · level {_level} · lives {_lives}";

    public override void Reset()
    {
        _score = 0;
        _level = 1;
        _lives = Lives;
        _over = false;
        _acc = TimeSpan.Zero;
        ScoreCounts = false;
        LoadLevel();
    }

    private void LoadLevel()
    {
        _dots = 0;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            _grid[x, y] = Maze[y][x];
            if (_grid[x, y] is '.' or 'o') _dots++;
        }

        _px = Start.X;
        _py = Start.Y;
        _dir = (1, 0);
        _nextDir = (0, 0);
        _ghosts.Clear();
        var tints = new[] { Bad, Warn, Accent };
        for (int i = 0; i < 3; i++)
        {
            _ghosts.Add(new Ghost
            {
                X = LairCells[i].X,
                Y = LairCells[i].Y,
                Dx = i == 0 ? -1 : 1,
                Dy = 0,
                Tint = tints[i],
            });
        }
        _frightIn = 0;
        _freezeIn = 0;
        _playerIn = PlayerStepMs();
        _ghostIn = GhostStepMs();
        Redraw();
    }

    private int PlayerStepMs() => Math.Max(120, PlayerMs - 10 * (_level - 1));

    private int GhostStepMs() => Math.Max(145, GhostMs - 12 * (_level - 1));

    private bool Walkable(int x, int y) => _grid[x, y] != '#';

    public override bool Tick(TimeSpan elapsed)
    {
        if (_over) return false;
        _acc += elapsed;
        bool changed = false;
        int guard = 0;
        while (_acc.TotalMilliseconds >= FrameMs && guard++ < MaxFrames)
        {
            _acc -= TimeSpan.FromMilliseconds(FrameMs);
            FrameStep();
            changed = true;
            if (_over) break;
        }
        if (_acc.TotalMilliseconds > FrameMs * MaxFrames) _acc = TimeSpan.FromMilliseconds(FrameMs * MaxFrames);
        if (changed) Redraw();
        return changed;
    }

    private void FrameStep()
    {
        if (_freezeIn > 0)
        {
            _freezeIn -= FrameMs;
            return;
        }

        if (_frightIn > 0) _frightIn -= FrameMs;

        _playerIn -= FrameMs;
        if (_playerIn <= 0)
        {
            _playerIn += PlayerStepMs();
            StepPlayer();
            if (_over) return;
        }

        _ghostIn -= FrameMs;
        if (_ghostIn <= 0)
        {
            _ghostIn += _frightIn > 0 ? FrightMs : GhostStepMs();
            foreach (var ghost in _ghosts)
            {
                if (ghost.InLair) continue;
                StepGhost(ghost);
                if (_over) return;
            }
        }

        foreach (var ghost in _ghosts)
        {
            if (!ghost.InLair) continue;
            ghost.LairIn -= FrameMs;
            if (ghost.LairIn <= 0)
            {
                ghost.InLair = false;
                ghost.X = 7;
                ghost.Y = 7;
                ghost.Dx = Random.Shared.Next(2) == 0 ? -1 : 1;
                ghost.Dy = 0;
            }
        }
    }

    private void StepPlayer()
    {
        if (_nextDir != (0, 0) && Walkable(_px + _nextDir.dx, _py + _nextDir.dy)) _dir = _nextDir;
        if (!Walkable(_px + _dir.dx, _py + _dir.dy)) return;

        _px += _dir.dx;
        _py += _dir.dy;
        char cell = _grid[_px, _py];
        if (cell == '.')
        {
            _score += DotPoints;
            _dots--;
        }
        else if (cell == 'o')
        {
            _score += PowerPoints;
            _dots--;
            _frightIn = FrightenFor;
        }

        if (cell is '.' or 'o') _grid[_px, _py] = ' ';
        if (_dots <= 0)
        {
            _score += LevelPoints;
            _level++;
            LoadLevel();
            return;
        }

        CheckCollisions();
    }

    private void StepGhost(Ghost ghost)
    {
        // At each junction pick the direction that best closes on the player —
        // or opens the gap when frightened. Reversing is only allowed in a dead end.
        var options = new List<(int dx, int dy)>();
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            if (Walkable(ghost.X + dx, ghost.Y + dy)) options.Add((dx, dy));
        if (options.Count == 0) return;

        var forward = options.Where(o => (o.dx, o.dy) != (-ghost.Dx, -ghost.Dy)).ToList();
        if (forward.Count == 0) forward = options;

        bool frightened = _frightIn > 0;
        var best = forward[0];
        int bestScore = int.MinValue;
        foreach (var o in forward)
        {
            int d = Math.Abs(ghost.X + o.dx - _px) + Math.Abs(ghost.Y + o.dy - _py);
            int s = (frightened ? d : -d) + Random.Shared.Next(2); // jitter breaks lockstep
            if (s > bestScore) { bestScore = s; best = o; }
        }

        ghost.Dx = best.dx;
        ghost.Dy = best.dy;
        ghost.X += best.dx;
        ghost.Y += best.dy;

        CheckCollisions();
    }

    private void CheckCollisions()
    {
        for (int i = 0; i < _ghosts.Count; i++)
        {
            var ghost = _ghosts[i];
            if (ghost.InLair || ghost.X != _px || ghost.Y != _py) continue;
            if (_frightIn > 0)
            {
                _score += GhostPoints;
                ghost.InLair = true; // eaten ghosts respawn from the lair
                ghost.LairIn = LairFor;
                ghost.X = LairCells[i].X;
                ghost.Y = LairCells[i].Y;
            }
            else
            {
                _lives--;
                if (_lives <= 0)
                {
                    _over = true;
                    ScoreCounts = _score > 0;
                    return;
                }
                // Reset the chase after a caught breath.
                _px = Start.X;
                _py = Start.Y;
                _dir = (1, 0);
                _nextDir = (0, 0);
                for (int g = 0; g < _ghosts.Count; g++)
                {
                    _ghosts[g].X = LairCells[g].X;
                    _ghosts[g].Y = LairCells[g].Y;
                    _ghosts[g].InLair = false;
                    _ghosts[g].LairIn = 0;
                }
                _freezeIn = FreezeFor;
                _frightIn = 0;
                return;
            }
        }
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        var dir = key switch
        {
            GameKey.Up => (0, -1),
            GameKey.Down => (0, 1),
            GameKey.Left => (-1, 0),
            GameKey.Right => (1, 0),
            _ => (0, 0),
        };
        if (dir == (0, 0)) return false;
        _nextDir = dir;
        return true;
    }

    public override bool OnTap(int index)
    {
        // Tapping steers toward the tapped cell, absolute like the web client.
        if (_over || index < 0 || index >= _cells.Count) return false;
        int tx = index % Size, ty = index / Size;
        int vx = tx - _px, vy = ty - _py;
        if (Math.Abs(vx) >= Math.Abs(vy)) return OnKey(vx > 0 ? GameKey.Right : GameKey.Left);
        return OnKey(vy > 0 ? GameKey.Down : GameKey.Up);
    }

    public override void Redraw()
    {
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            var c = _cells[y * Size + x];
            char cell = _grid[x, y];
            c.Text = "";
            c.Background = cell == '#' ? Ink : Board;
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 14;
            c.Bold = false;

            if (cell == '.')
            {
                c.Text = "●";
                c.Foreground = Muted;
                c.FontSize = 8;
            }
            else if (cell == 'o')
            {
                c.Text = "●";
                c.Foreground = Warn;
                c.FontSize = 13;
                c.Bold = true;
            }
        }

        bool frightened = _frightIn > 0;
        foreach (var ghost in _ghosts)
        {
            var c = _cells[ghost.Y * Size + ghost.X];
            c.Text = "○";
            if (ghost.InLair)
            {
                c.Background = Board;
                c.Foreground = Muted;
            }
            else if (frightened)
            {
                c.Background = Muted;
                c.Foreground = Cell;
            }
            else
            {
                c.Background = ghost.Tint;
                c.Foreground = Cell;
            }
            c.FontSize = 13;
            c.Bold = true;
        }

        var pc = _cells[_py * Size + _px];
        pc.Text = "●";
        pc.Background = Accent;
        pc.Foreground = Cell;
        pc.FontSize = 13;
        pc.Bold = true;
    }
}
