using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Deep Dig — a dig-and-smash descent over six chapters, ported from the official
/// client. Dig dirt, pocket gems and mind the rocks: unsupported rocks tumble and
/// crush whatever is underneath. One safe column always reaches the bottom.
/// </summary>
public sealed class DigQuestGame : GameBase
{
    private const int Width = 12;
    private const int Height = 14;
    private const int ChapterCount = 6;
    private const int GemsPerChapter = 10;
    private const int GemPoints = 20;
    private const int DeathPenalty = 50;

    private const int FrameMs = 50;         // fixed simulation frame
    private const int MaxFrames = 6;        // catch-up guard: at most 6 frames per Tick
    private const int GravityMs = 180;      // one cell of falling per gravity step
    private const int MoveCooldownMs = 140; // fastest dig/walk cadence

    private readonly List<GameCell> _cells = new();
    private readonly char[,] _grid = new char[Width, Height]; // ' ' empty, d dirt, g gem, r rock
    private int _px, _py;
    private int _chapter;
    private int _gems;
    private int _deaths;
    private int _score;
    private string _cause = "";
    private int _safeCol;
    private int _gravIn;
    private int _moveIn;
    private TimeSpan _acc;
    private bool _over;
    private bool _won;

    public DigQuestGame()
    {
        for (int i = 0; i < Width * Height; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "digquest";
    public override string Name => "Deep Dig";
    public override int Columns => Width;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_over ? _score : _gems * GemPoints, 0, 1_000_000);

    public override string? ResultText => !_over
        ? null
        : _won ? $"All six chapters — {Score} points." : $"{_cause} — {Score} points.";

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"Ch {_chapter}/{ChapterCount} · gems {_gems} · ←→↓ dig";

    public override void Reset()
    {
        _chapter = 1;
        _gems = 0;
        _deaths = 0;
        _score = 0;
        _cause = "";
        _gravIn = GravityMs;
        _moveIn = 0;
        _acc = TimeSpan.Zero;
        _over = false;
        _won = false;
        ScoreCounts = false;
        BuildShaft();
        Redraw();
    }

    private void BuildShaft()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            _grid[x, y] = y == 0 ? ' ' : 'd';

        // One rock-free column guarantees a way down through every chapter.
        _safeCol = 1 + Random.Shared.Next(Width - 2);
        var used = new HashSet<(int x, int y)>();

        int rocks = 3 + _chapter; // later chapters are rockier
        while (rocks > 0)
        {
            int x = Random.Shared.Next(Width), y = 2 + Random.Shared.Next(Height - 2);
            if (x == _safeCol || !used.Add((x, y))) continue;
            _grid[x, y] = 'r';
            rocks--;
        }

        int gems = GemsPerChapter;
        while (gems > 0)
        {
            int x = Random.Shared.Next(Width), y = 1 + Random.Shared.Next(Height - 1);
            if (!used.Add((x, y))) continue;
            _grid[x, y] = 'g';
            gems--;
        }

        _px = Width / 2;
        _py = 0;
        _gravIn = GravityMs;
        _moveIn = 0;
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_over) return false;
        _acc += elapsed;
        bool changed = false;
        int guard = 0;
        while (_acc.TotalMilliseconds >= FrameMs && guard++ < MaxFrames)
        {
            _acc -= TimeSpan.FromMilliseconds(FrameMs);
            _moveIn = Math.Max(0, _moveIn - FrameMs);
            _gravIn -= FrameMs;
            if (_gravIn <= 0)
            {
                _gravIn += GravityMs;
                GravityStep();
            }
            changed = true;
            if (_over) break;
        }
        if (_acc.TotalMilliseconds > FrameMs * MaxFrames) _acc = TimeSpan.FromMilliseconds(FrameMs * MaxFrames);
        if (changed) Redraw();
        return changed;
    }

    private void GravityStep()
    {
        // Rocks fall one cell at a time, bottom-up so a stack moves together.
        for (int y = Height - 2; y >= 0; y--)
        for (int x = 0; x < Width; x++)
        {
            if (_grid[x, y] != 'r' || _grid[x, y + 1] != ' ') continue;
            if (x == _px && y + 1 == _py) { Die("Crushed by a rock"); return; }
            _grid[x, y] = ' ';
            _grid[x, y + 1] = 'r';
        }

        if (_py < Height - 1 && _grid[_px, _py + 1] == ' ')
        {
            _py++;
            if (_py == Height - 1) ChapterDone();
        }
        else if (Trapped())
        {
            Die("Trapped");
        }
    }

    private bool Trapped()
    {
        if (_py >= Height - 1) return false;
        if (_grid[_px, _py + 1] != 'r') return false; // empty falls, dirt and gems can be dug
        bool left = _px > 0 && _grid[_px - 1, _py] != 'r';
        bool right = _px < Width - 1 && _grid[_px + 1, _py] != 'r';
        return !left && !right;
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        switch (key)
        {
            case GameKey.Left: return MoveSideways(-1);
            case GameKey.Right: return MoveSideways(1);
            case GameKey.Down:
            case GameKey.Confirm: return DigDown();
            default: return false; // no climbing back up
        }
    }

    public override bool OnTap(int index)
    {
        // Tapping digs toward the tapped cell: sideways or straight down.
        if (_over || index < 0 || index >= _cells.Count) return false;
        int tx = index % Width, ty = index / Width;
        int vx = tx - _px, vy = ty - _py;
        if (Math.Abs(vx) > Math.Abs(vy)) return MoveSideways(vx > 0 ? 1 : -1);
        return vy > 0 ? DigDown() : false;
    }

    private bool MoveSideways(int dx)
    {
        if (_moveIn > 0) return true; // rate-limited so falls interleave with digs
        int nx = _px + dx;
        if (nx < 0 || nx >= Width) return false;
        char cell = _grid[nx, _py];
        if (cell == 'r') return true; // rocks never budge

        if (cell == 'g') TakeGem();
        _grid[nx, _py] = ' ';
        _px = nx;
        _moveIn = MoveCooldownMs;
        AfterMove();
        return true;
    }

    private bool DigDown()
    {
        if (_moveIn > 0) return true;
        if (_py + 1 >= Height) return true;
        char below = _grid[_px, _py + 1];
        if (below == 'r') return true; // rocks never budge

        if (below == 'g') TakeGem();
        _grid[_px, _py + 1] = ' ';
        _py++; // you fall into the hole you just dug
        _moveIn = MoveCooldownMs;
        AfterMove();
        return true;
    }

    private void TakeGem()
    {
        _gems++;
    }

    private void AfterMove()
    {
        if (_py == Height - 1) ChapterDone();
        Redraw();
    }

    private void ChapterDone()
    {
        _chapter++;
        if (_chapter > ChapterCount)
        {
            _over = true;
            _won = true;
            _score = _gems > 0 ? Math.Max(10, _gems * GemPoints) : 1;
            ScoreCounts = true;
            Redraw();
            return;
        }
        BuildShaft();
        Redraw();
    }

    private void Die(string cause)
    {
        if (_over) return;
        _over = true;
        _won = false;
        _deaths++;
        _cause = cause;
        _score = _gems > 0 ? Math.Max(10, _gems * GemPoints - DeathPenalty * _deaths) : 1;
        ScoreCounts = true;
        Redraw();
    }

    public override void Redraw()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var c = _cells[y * Width + x];
            c.Text = "";
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 15;
            c.Bold = false;

            switch (_grid[x, y])
            {
                case 'd':
                    c.Background = Board;
                    break;
                case 'g':
                    c.Background = Board;
                    c.Text = "♦";
                    c.Foreground = Warn;
                    c.Bold = true;
                    break;
                case 'r':
                    c.Background = Board;
                    c.Text = "●";
                    c.Foreground = Ink;
                    c.Bold = true;
                    break;
                default:
                    c.Background = Cell;
                    break;
            }
        }

        var pc = _cells[_py * Width + _px];
        pc.Text = "●";
        pc.Background = Accent;
        pc.Foreground = Cell;
        pc.Bold = true;
    }
}
