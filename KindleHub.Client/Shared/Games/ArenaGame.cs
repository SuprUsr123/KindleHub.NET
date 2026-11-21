using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Arena Clash — a top-down brawl against three bots, ported from the official
/// client. Tap anywhere in the arena to send your fighter there; it auto-fires a
/// bolt at the nearest bot that shares its row or column while the bots wander
/// and shoot back.
/// </summary>
public sealed class ArenaGame : GameBase
{
    private const int Width = 15;
    private const int Height = 11;
    private const int BotCount = 3;
    private const int BotHp = 3;
    private const int PlayerHp = 5;

    private const int FrameMs = 50;        // fixed simulation frame
    private const int MaxFrames = 6;       // catch-up guard: at most 6 frames per Tick
    private const int PlayerStepMs = 250;  // one cell per step toward the tap target
    private const int PlayerFireMs = 700;  // player auto-fire cadence
    private const int BotStepMs = 450;     // bot wander cadence
    private const int BotFireMs = 1400;    // bot return-fire cadence
    private const int BoltMs = 100;        // bolt speed: one cell per step
    private const int KillBonus = 100;     // score for finishing a bot
    private const int DamagePoints = 10;   // score per point of damage dealt

    private sealed class Bot
    {
        public int X, Y, Hp;
        public int MoveIn, FireIn;
        public bool Alive => Hp > 0;
    }

    private sealed class Bolt
    {
        public int X, Y, Dx, Dy;
        public bool FromPlayer;
    }

    private readonly List<GameCell> _cells = new();
    private readonly List<Bot> _bots = new();
    private readonly List<Bolt> _bolts = new();
    private int _px, _py, _hp;
    private int _tx, _ty;
    private bool _hasTarget;
    private int _moveIn, _fireIn, _boltIn;
    private TimeSpan _acc;
    private int _score;
    private bool _over;
    private bool _won;

    public ArenaGame()
    {
        for (int i = 0; i < Width * Height; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "arena";
    public override string Name => "Arena Clash";
    public override int Columns => Width;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => !_over
        ? null
        : _won ? $"Arena cleared — {Score} points." : $"Game over — {Score} points.";

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"HP {_hp} · bots {_bots.Count(b => b.Alive)} · tap to move";

    public override void Reset()
    {
        _bots.Clear();
        _bolts.Clear();
        _px = Width / 2;
        _py = Height / 2;
        _hp = PlayerHp;
        _hasTarget = false;
        _moveIn = PlayerStepMs;
        _fireIn = PlayerFireMs;
        _boltIn = BoltMs;
        _acc = TimeSpan.Zero;
        _score = 0;
        _over = false;
        _won = false;
        ScoreCounts = false;
        // Bots start spread out in the corners of the floor, away from the player.
        var starts = new[] { (2, 2), (Width - 3, 2), (Width / 2, Height - 3) };
        for (int i = 0; i < BotCount; i++)
            _bots.Add(new Bot { X = starts[i].Item1, Y = starts[i].Item2, Hp = BotHp, MoveIn = BotStepMs, FireIn = BotFireMs });
        Redraw();
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
            FrameStep();
            changed = true;
            if (_over) break;
        }
        // A slow frame must not build up an unbounded backlog of frames.
        if (_acc.TotalMilliseconds > FrameMs * MaxFrames) _acc = TimeSpan.FromMilliseconds(FrameMs * MaxFrames);
        return changed;
    }

    private void FrameStep()
    {
        _moveIn -= FrameMs;
        if (_moveIn <= 0)
        {
            _moveIn += PlayerStepMs;
            if (_hasTarget) StepTowardTarget();
        }

        _fireIn -= FrameMs;
        if (_fireIn <= 0)
        {
            _fireIn += PlayerFireMs;
            TryFirePlayer();
        }

        foreach (var bot in _bots)
        {
            if (!bot.Alive) continue;
            bot.MoveIn -= FrameMs;
            if (bot.MoveIn <= 0)
            {
                bot.MoveIn += BotStepMs;
                BotWander(bot);
            }
            bot.FireIn -= FrameMs;
            if (bot.FireIn <= 0)
            {
                bot.FireIn += BotFireMs;
                TryFireBot(bot);
            }
        }

        _boltIn -= FrameMs;
        if (_boltIn <= 0)
        {
            _boltIn += BoltMs;
            MoveBolts();
        }

        Redraw();
    }

    private void StepTowardTarget()
    {
        int dx = _tx - _px, dy = _ty - _py;
        if (dx == 0 && dy == 0) { _hasTarget = false; return; }

        // Try the dominant axis first, then the other, so a tap always makes progress.
        bool horizontalFirst = Math.Abs(dx) >= Math.Abs(dy);
        var first = horizontalFirst ? (Math.Sign(dx), 0) : (0, Math.Sign(dy));
        var second = horizontalFirst ? (0, Math.Sign(dy)) : (Math.Sign(dx), 0);
        if (TryStep(first.Item1, first.Item2)) return;
        TryStep(second.Item1, second.Item2);
    }

    private bool TryStep(int sx, int sy)
    {
        if (sx == 0 && sy == 0) return false;
        if (!CanStand(_px + sx, _py + sy)) return false;
        _px += sx;
        _py += sy;
        return true;
    }

    private bool CanStand(int x, int y) =>
        x > 0 && y > 0 && x < Width - 1 && y < Height - 1 &&
        !_bots.Any(b => b.Alive && b.X == x && b.Y == y);

    private void TryFirePlayer()
    {
        Bot? best = null;
        int bestDist = int.MaxValue;
        foreach (var bot in _bots)
        {
            if (!bot.Alive) continue;
            if (bot.X != _px && bot.Y != _py) continue; // needs a shared row or column
            if (!LineClear(_px, _py, bot.X, bot.Y)) continue; // and nobody standing between
            int dist = Math.Abs(bot.X - _px) + Math.Abs(bot.Y - _py);
            if (dist < bestDist) { bestDist = dist; best = bot; }
        }
        if (best == null) return;
        _bolts.Add(new Bolt { X = _px, Y = _py, Dx = Math.Sign(best.X - _px), Dy = Math.Sign(best.Y - _py), FromPlayer = true });
    }

    private void TryFireBot(Bot bot)
    {
        if (bot.X != _px && bot.Y != _py) return;
        if (!LineClear(bot.X, bot.Y, _px, _py)) return;
        _bolts.Add(new Bolt { X = bot.X, Y = bot.Y, Dx = Math.Sign(_px - bot.X), Dy = Math.Sign(_py - bot.Y), FromPlayer = false });
    }

    private bool LineClear(int x1, int y1, int x2, int y2)
    {
        // Walk the shared row or column; a third bot standing between blocks the shot.
        int dx = Math.Sign(x2 - x1), dy = Math.Sign(y2 - y1);
        int x = x1 + dx, y = y1 + dy;
        while (x != x2 || y != y2)
        {
            if (_bots.Any(b => b.Alive && b.X == x && b.Y == y)) return false;
            x += dx;
            y += dy;
        }
        return true;
    }

    private void BotWander(Bot bot)
    {
        var dirs = new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) };
        var (dx, dy) = dirs[Random.Shared.Next(dirs.Length)];
        if (dx == 0 && dy == 0) return;
        int nx = bot.X + dx, ny = bot.Y + dy;
        if (CanStand(nx, ny) && !(nx == _px && ny == _py)) { bot.X = nx; bot.Y = ny; }
    }

    private void MoveBolts()
    {
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var bolt = _bolts[i];
            int nx = bolt.X + bolt.Dx, ny = bolt.Y + bolt.Dy;
            if (nx <= 0 || ny <= 0 || nx >= Width - 1 || ny >= Height - 1) { _bolts.RemoveAt(i); continue; }

            if (bolt.FromPlayer)
            {
                var hit = _bots.FirstOrDefault(b => b.Alive && b.X == nx && b.Y == ny);
                if (hit != null)
                {
                    hit.Hp--;
                    _score += DamagePoints;
                    if (!hit.Alive) _score += KillBonus;
                    _bolts.RemoveAt(i);
                    continue;
                }
            }
            else if (nx == _px && ny == _py)
            {
                _hp--;
                _bolts.RemoveAt(i);
                if (_hp <= 0) { GameOver(won: false); return; }
                continue;
            }

            bolt.X = nx;
            bolt.Y = ny;
        }

        if (_bots.All(b => !b.Alive)) GameOver(won: true);
    }

    private void GameOver(bool won)
    {
        if (_over) return;
        _over = true;
        _won = won;
        _hasTarget = false;
        _bolts.Clear();
        ScoreCounts = _score > 0;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        switch (key)
        {
            case GameKey.Up: return StepCommand(0, -1);
            case GameKey.Down: return StepCommand(0, 1);
            case GameKey.Left: return StepCommand(-1, 0);
            case GameKey.Right: return StepCommand(1, 0);
            case GameKey.Confirm:
                TryFirePlayer();
                Redraw();
                return true;
            default:
                return false;
        }
    }

    private bool StepCommand(int dx, int dy)
    {
        int tx = Math.Clamp(_px + dx, 1, Width - 2), ty = Math.Clamp(_py + dy, 1, Height - 2);
        if (tx == _px && ty == _py) return false;
        _tx = tx;
        _ty = ty;
        _hasTarget = true;
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        int x = index % Width, y = index / Width;
        if (x <= 0 || y <= 0 || x >= Width - 1 || y >= Height - 1) return false; // walls aren't targets
        _tx = x;
        _ty = y;
        _hasTarget = true;
        Redraw();
        return true;
    }

    public override void Redraw()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var c = _cells[y * Width + x];
            bool wall = x == 0 || y == 0 || x == Width - 1 || y == Height - 1;
            c.Text = "";
            c.Background = wall ? Ink : Board;
            c.Foreground = Ink;
            c.IsEnabled = !wall && !_over;
            c.FontSize = 16;
            c.Bold = false;
        }

        if (_hasTarget && !_over)
        {
            // A soft tint marks where the fighter is headed.
            _cells[_ty * Width + _tx].Background = Tiles4[2];
        }

        foreach (var bolt in _bolts)
        {
            var c = _cells[bolt.Y * Width + bolt.X];
            c.Text = "✱";
            c.Foreground = bolt.FromPlayer ? Accent : Bad;
            c.Bold = true;
        }

        foreach (var bot in _bots)
        {
            if (!bot.Alive) continue;
            var c = _cells[bot.Y * Width + bot.X];
            c.Text = "○";
            c.Background = bot.Hp > 1 ? Bad : Warn;
            c.Foreground = Cell;
            c.Bold = true;
        }

        var pc = _cells[_py * Width + _px];
        pc.Text = "●";
        pc.Background = Accent;
        pc.Foreground = Cell;
        pc.Bold = true;
    }
}
