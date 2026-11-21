using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Space Invaders — move left and right and let the auto-cannon fire at the
/// descending formation, ported from the official client. Four bunkers erode cell
/// by cell; clear each wave before the aliens reach the ground.
/// </summary>
public sealed class SpaceInvGame : GameBase
{
    private const int Width = 11;
    private const int Height = 13;
    private const int FormCols = 8;
    private const int FormRows = 5;
    private const int Lives = 3;
    private const int PlayerRow = Height - 1;
    private const int BunkerTop = 9;       // bunkers occupy rows 9-10

    private const int FrameMs = 50;        // fixed simulation frame
    private const int MaxFrames = 6;       // catch-up guard: at most 6 frames per Tick
    private const int FireMs = 450;        // auto-fire cadence
    private const int BoltMs = 80;         // bolt speed: one cell per step
    private const int BombMs = 160;        // bomb speed: one cell per step
    private const int BombDropMs = 1500;   // a bomb drops this often
    private const int MaxBolts = 3;
    private const int WaveBonus = 100;

    private static readonly int[] RowPoints = { 30, 20, 20, 10, 10 };
    private static readonly int[] BunkerCols = { 0, 1, 3, 4, 6, 7, 9, 10 };

    private sealed class Alien
    {
        public int SlotX, SlotRow;
        public bool Alive = true;
    }

    private readonly List<GameCell> _cells = new();
    private readonly List<Alien> _aliens = new();
    private readonly List<(int X, int Y)> _bolts = new();
    private readonly List<(int X, int Y)> _bombs = new();
    private readonly bool[,] _bunkers = new bool[Width, 2];
    private int _px;
    private int _formX, _formY, _formDir;
    private int _formIn, _fireIn, _boltIn, _bombIn, _bombMoveIn;
    private int _wave;
    private int _lives;
    private int _score;
    private string _cause = "";
    private TimeSpan _acc;
    private bool _over;

    public SpaceInvGame()
    {
        for (int i = 0; i < Width * Height; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "spaceinv";
    public override string Name => "Space Invaders";
    public override int Columns => Width;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => _over ? $"{_cause} — {Score} points." : null;

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"Score {_score} · wave {_wave} · lives {_lives}";

    public override void Reset()
    {
        _bolts.Clear();
        _bombs.Clear();
        _px = Width / 2;
        _wave = 1;
        _lives = Lives;
        _score = 0;
        _cause = "";
        _acc = TimeSpan.Zero;
        _over = false;
        ScoreCounts = false;
        BuildWave();
        Redraw();
    }

    private void BuildWave()
    {
        _aliens.Clear();
        for (int r = 0; r < FormRows; r++)
        for (int c = 0; c < FormCols; c++)
            _aliens.Add(new Alien { SlotX = c, SlotRow = r });
        _formX = 0;
        _formY = Math.Min(_wave - 1, 2); // each wave starts lower, never right on the bunkers
        _formDir = 1;
        _formIn = FormStepMs();
        _fireIn = FireMs;
        _boltIn = BoltMs;
        _bombIn = BombDropMs;
        _bombMoveIn = BombMs;
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < Width; x++)
            _bunkers[x, y] = BunkerCols.Contains(x);
    }

    private int FormStepMs()
    {
        // The formation quickens as its ranks thin.
        int alive = _aliens.Count(a => a.Alive);
        return Math.Clamp(800 * alive / (FormCols * FormRows), 150, 800);
    }

    private static int Ax(Alien a, int formX) => 1 + a.SlotX + formX;
    private static int Ay(Alien a, int formY) => 1 + a.SlotRow + formY;

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
        _formIn -= FrameMs;
        if (_formIn <= 0)
        {
            _formIn += FormStepMs();
            StepFormation();
            if (_over) return;
        }

        _fireIn -= FrameMs;
        if (_fireIn <= 0)
        {
            _fireIn += FireMs;
            Fire();
        }

        _boltIn -= FrameMs;
        if (_boltIn <= 0)
        {
            _boltIn += BoltMs;
            MoveBolts();
            if (_over) return;
        }

        _bombIn -= FrameMs;
        if (_bombIn <= 0)
        {
            _bombIn += BombDropMs;
            DropBomb();
        }

        _bombMoveIn -= FrameMs;
        if (_bombMoveIn <= 0)
        {
            _bombMoveIn += BombMs;
            MoveBombs();
            if (_over) return;
        }
    }

    private void StepFormation()
    {
        var alive = _aliens.Where(a => a.Alive).ToList();
        if (alive.Count == 0) return;

        int minX = alive.Min(a => Ax(a, _formX));
        int maxX = alive.Max(a => Ax(a, _formX));
        if (_formDir > 0 && maxX + 1 > Width - 1)
        {
            _formDir = -1;
            _formY++;
        }
        else if (_formDir < 0 && minX - 1 < 0)
        {
            _formDir = 1;
            _formY++;
        }
        else
        {
            _formX += _formDir;
        }

        if (alive.Any(a => Ay(a, _formY) >= BunkerTop)) GameOver("Invaded"); // reached the bunker line
    }

    private void Fire()
    {
        if (_bolts.Count >= MaxBolts) return;
        int y = PlayerRow - 1;
        if (HitAlien(_px, y) || BunkerAt(_px, y)) return;
        _bolts.Add((_px, y));
    }

    private bool HitAlien(int bx, int by)
    {
        var hit = _aliens.FirstOrDefault(a => a.Alive && Ay(a, _formY) == by && Ax(a, _formX) == bx);
        if (hit == null) return false;
        hit.Alive = false;
        _score += RowPoints[hit.SlotRow];
        if (_aliens.All(a => !a.Alive))
        {
            _score += WaveBonus;
            _wave++;
            BuildWave();
        }
        return true;
    }

    private bool BunkerAt(int x, int y) =>
        y >= BunkerTop && y <= BunkerTop + 1 && _bunkers[x, y - BunkerTop];

    private void MoveBolts()
    {
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var (bx, by) = _bolts[i];
            by--;
            if (by < 0)
            {
                _bolts.RemoveAt(i);
                continue;
            }

            if (HitAlien(bx, by))
            {
                _bolts.RemoveAt(i);
                continue;
            }

            if (BunkerAt(bx, by))
            {
                _bunkers[bx, by - BunkerTop] = false; // bolts erode one bunker cell
                _bolts.RemoveAt(i);
                continue;
            }

            _bolts[i] = (bx, by);
        }
    }

    private void DropBomb()
    {
        var shooters = _aliens.Where(a => a.Alive).ToList();
        if (shooters.Count == 0) return;
        var a = shooters[Random.Shared.Next(shooters.Count)];
        int y = Ay(a, _formY) + 1;
        if (y < Height) _bombs.Add((Ax(a, _formX), y));
    }

    private void MoveBombs()
    {
        for (int i = _bombs.Count - 1; i >= 0; i--)
        {
            var (bx, by) = _bombs[i];
            by++;
            if (by > PlayerRow)
            {
                _bombs.RemoveAt(i);
                continue;
            }

            if (BunkerAt(bx, by))
            {
                _bunkers[bx, by - BunkerTop] = false;
                _bombs.RemoveAt(i);
                continue;
            }

            if (by == PlayerRow && bx == _px)
            {
                _bombs.RemoveAt(i);
                _lives--;
                if (_lives <= 0)
                {
                    GameOver("Game over");
                    return;
                }
                continue;
            }

            _bombs[i] = (bx, by);
        }
    }

    private void GameOver(string cause)
    {
        if (_over) return;
        _over = true;
        _cause = cause;
        _bolts.Clear();
        _bombs.Clear();
        ScoreCounts = _score > 0;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        switch (key)
        {
            case GameKey.Left:
                _px = Math.Max(0, _px - 1);
                Redraw();
                return true;
            case GameKey.Right:
                _px = Math.Min(Width - 1, _px + 1);
                Redraw();
                return true;
            case GameKey.Confirm:
                Fire();
                Redraw();
                return true;
            default:
                return false;
        }
    }

    public override bool OnTap(int index)
    {
        // Tapping steers the cannon toward the tapped column; your own column fires.
        if (_over || index < 0 || index >= _cells.Count) return false;
        int tx = index % Width;
        if (tx < _px) return OnKey(GameKey.Left);
        if (tx > _px) return OnKey(GameKey.Right);
        return OnKey(GameKey.Confirm);
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 14;
            c.Bold = false;
        }

        foreach (var (bx, by) in _bolts)
        {
            var c = _cells[by * Width + bx];
            c.Text = "↑";
            c.Foreground = Good;
            c.Bold = true;
        }

        foreach (var (bx, by) in _bombs)
        {
            var c = _cells[by * Width + bx];
            c.Text = "↓";
            c.Foreground = Bad;
            c.Bold = true;
        }

        foreach (var a in _aliens)
        {
            if (!a.Alive) continue;
            int x = Ax(a, _formX), y = Ay(a, _formY);
            if (y < 0 || y >= Height) continue;
            var c = _cells[y * Width + x];
            c.Text = "▲";
            c.Foreground = a.SlotRow == 0 ? Warn : Ink; // top rank pays triple
            c.Bold = a.SlotRow == 0;
        }

        for (int y = 0; y < 2; y++)
        for (int x = 0; x < Width; x++)
        {
            if (!_bunkers[x, y]) continue;
            var c = _cells[(BunkerTop + y) * Width + x];
            c.Text = "▓";
            c.Foreground = Muted;
        }

        var pc = _cells[PlayerRow * Width + _px];
        pc.Text = "●";
        pc.Background = Accent;
        pc.Foreground = Cell;
        pc.Bold = true;
    }
}
