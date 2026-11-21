using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Checkers against the computer, ported from the official client. Move diagonally
/// and jump enemy pieces to capture them — captures are forced and chains keep
/// jumping. Reach the far row to crown a king that steps both ways.
/// </summary>
public sealed class CheckersGame : GameBase
{
    private const int Side = 8;
    private const int Empty = 0, PMan = 1, PKing = 2, CMan = 3, CKing = 4;

    private readonly record struct Mv(int Tx, int Ty, int MidX, int MidY); // Mid < 0 = plain step

    private static readonly (int Dx, int Dy)[] PlayerDirs = { (-1, -1), (1, -1) };
    private static readonly (int Dx, int Dy)[] CpuDirs = { (-1, 1), (1, 1) };
    private static readonly (int Dx, int Dy)[] AllDirs = { (-1, -1), (1, -1), (-1, 1), (1, 1) };

    private readonly List<GameCell> _cells = new();
    private readonly int[,] _b = new int[Side, Side]; // [x, y]; y = 0 is your crown row
    private int _selX = -1, _selY = -1;
    private bool _chain; // player is mid multi-jump
    private bool _over, _win;
    private string _msg = "Your move.";

    public CheckersGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "checkers";
    public override string Name => "Checkers";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _win ? 300 + 10 * CountPieces(true) : 0;

    public override string? ResultText => !_over ? null
        : _win ? "You win — every computer piece is gone."
        : "The computer wins.";

    public override string StatusText => _over ? (ResultText ?? "") : _msg;

    public override void Reset()
    {
        Array.Clear(_b, 0, _b.Length);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < Side; x++)
                if (Dark(x, y)) _b[x, y] = CMan;
        for (int y = 5; y < Side; y++)
            for (int x = 0; x < Side; x++)
                if (Dark(x, y)) _b[x, y] = PMan;
        _selX = _selY = -1;
        _chain = false;
        _over = _win = false;
        ScoreCounts = false;
        _msg = "Your move · captures are forced.";
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count || _over) return false;
        int x = index % Side, y = index / Side;

        if (_chain)
        {
            // Mid multi-jump: only further jumps with the same piece count.
            foreach (var m in MovesFrom(_selX, _selY))
                if (m.MidX >= 0 && m.Tx == x && m.Ty == y)
                {
                    PlayerJump(m);
                    return true;
                }
            _msg = "Keep jumping with the same piece.";
            Redraw();
            return true;
        }

        int p = _b[x, y];
        if (IsPlayer(p))
        {
            if (_selX == x && _selY == y)
            {
                _selX = _selY = -1;
                Redraw();
                return true;
            }
            bool forced = SideHasCapture(true);
            bool can = MovesFrom(x, y).Any(m => !forced || m.MidX >= 0);
            if (!can)
            {
                _msg = forced ? "Captures are forced." : "That piece can't move.";
                Redraw();
                return true;
            }
            _selX = x;
            _selY = y;
            _msg = forced ? "Pick a jump." : "Pick a square.";
            Redraw();
            return true;
        }

        if (_selX >= 0)
        {
            bool forced = SideHasCapture(true);
            foreach (var m in MovesFrom(_selX, _selY))
                if (m.Tx == x && m.Ty == y && (!forced || m.MidX >= 0))
                {
                    if (m.MidX >= 0) PlayerJump(m);
                    else PlayerStep(m);
                    return true;
                }
        }
        return false;
    }

    private void PlayerStep(Mv mv)
    {
        _b[mv.Tx, mv.Ty] = _b[_selX, _selY];
        _b[_selX, _selY] = Empty;
        PromoteIf(mv.Tx, mv.Ty);
        _selX = _selY = -1;
        _msg = "You moved.";
        AfterPlayerMove();
    }

    private void PlayerJump(Mv mv)
    {
        _b[mv.Tx, mv.Ty] = _b[_selX, _selY];
        _b[_selX, _selY] = Empty;
        _b[mv.MidX, mv.MidY] = Empty;
        bool promoted = PromoteIf(mv.Tx, mv.Ty);

        // A crowned man ends its move; otherwise keep the chain going.
        if (!promoted && MovesFrom(mv.Tx, mv.Ty).Any(m => m.MidX >= 0))
        {
            _selX = mv.Tx;
            _selY = mv.Ty;
            _chain = true;
            _msg = "Jump again!";
            Redraw();
            return;
        }
        _chain = false;
        _selX = _selY = -1;
        _msg = "You captured!";
        AfterPlayerMove();
    }

    private void AfterPlayerMove()
    {
        if (CountPieces(false) == 0 || !SideCanMove(false))
        {
            _over = _win = true;
            ScoreCounts = true;
            Redraw();
            return;
        }
        ComputerMove();
        if (CountPieces(true) == 0 || !SideCanMove(true)) _over = true;
        Redraw();
    }

    /// <summary>Takes the longest capture chain, else a random safe step.</summary>
    private void ComputerMove()
    {
        var captures = AllCaptures(false);
        if (captures.Count > 0)
        {
            int bestLen = -1;
            (int X, int Y, Mv M) best = captures[0];
            foreach (var c in captures)
            {
                int len = CpuChainLen(c.X, c.Y, c.M);
                if (len > bestLen || (len == bestLen && Random.Shared.Next(2) == 0))
                {
                    bestLen = len;
                    best = c;
                }
            }
            ExecuteCpuChain(best.X, best.Y, best.M);
            return;
        }

        var moves = new List<(int X, int Y, Mv M)>();
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                if (IsCpu(_b[x, y]))
                    foreach (var m in MovesFrom(x, y))
                        if (m.MidX < 0) moves.Add((x, y, m));
        if (moves.Count == 0) return;

        var safe = moves.Where(m => !LeavesBlunder(m.X, m.Y, m.M)).ToList();
        var pool = safe.Count > 0 ? safe : moves;
        var pick = pool[Random.Shared.Next(pool.Count)];
        _b[pick.M.Tx, pick.M.Ty] = _b[pick.X, pick.Y];
        _b[pick.X, pick.Y] = Empty;
        PromoteIf(pick.M.Tx, pick.M.Ty);
        _msg = "Computer moved.";
    }

    private void ExecuteCpuChain(int x, int y, Mv first)
    {
        int cx = x, cy = y;
        Mv cm = first;
        while (true)
        {
            int p = _b[cx, cy];
            _b[cx, cy] = Empty;
            _b[cm.MidX, cm.MidY] = Empty;
            bool promoted = p == CMan && cm.Ty == Side - 1;
            _b[cm.Tx, cm.Ty] = promoted ? CKing : p;
            _msg = "Computer captured!";
            if (promoted) return;
            cx = cm.Tx;
            cy = cm.Ty;

            Mv next = default;
            int bestLen = 0;
            foreach (var j in MovesFrom(cx, cy))
            {
                if (j.MidX < 0) continue;
                int len = CpuChainLen(cx, cy, j);
                if (len > bestLen)
                {
                    bestLen = len;
                    next = j;
                }
            }
            if (bestLen == 0) return;
            cm = next;
        }
    }

    /// <summary>Length of the longest capture run that starts with this jump.</summary>
    private int CpuChainLen(int x, int y, Mv m)
    {
        var nb = (int[,])_b.Clone();
        int p = nb[x, y];
        nb[x, y] = Empty;
        nb[m.MidX, m.MidY] = Empty;
        bool promoted = p == CMan && m.Ty == Side - 1;
        nb[m.Tx, m.Ty] = promoted ? CKing : p;
        return 1 + (promoted ? 0 : MaxChain(nb, m.Tx, m.Ty));
    }

    private static int MaxChain(int[,] b, int x, int y)
    {
        int p = b[x, y];
        var dirs = p is PKing or CKing ? AllDirs : IsPlayer(p) ? PlayerDirs : CpuDirs;
        int best = 0;
        foreach (var (dx, dy) in dirs)
        {
            int mx = x + dx, my = y + dy, lx = x + 2 * dx, ly = y + 2 * dy;
            if (!In(lx, ly) || b[lx, ly] != Empty) continue;
            if (!In(mx, my)) continue;
            if (!(IsPlayer(p) ? IsCpu(b[mx, my]) : IsPlayer(b[mx, my]))) continue;
            var nb = (int[,])b.Clone();
            int moved = nb[x, y];
            nb[x, y] = Empty;
            nb[mx, my] = Empty;
            bool promoted = (moved == CMan && ly == Side - 1) || (moved == PMan && ly == 0);
            nb[lx, ly] = promoted ? (IsPlayer(moved) ? PKing : CKing) : moved;
            best = Math.Max(best, 1 + (promoted ? 0 : MaxChain(nb, lx, ly)));
        }
        return best;
    }

    /// <summary>True when a plain move leaves the moved piece immediately capturable.</summary>
    private bool LeavesBlunder(int x, int y, Mv m)
    {
        var nb = (int[,])_b.Clone();
        nb[m.Tx, m.Ty] = nb[x, y];
        nb[x, y] = Empty;
        foreach (var (dx, dy) in AllDirs)
        {
            int ax = m.Tx - dx, ay = m.Ty - dy;
            int lx = m.Tx + dx, ly = m.Ty + dy;
            if (!In(ax, ay) || !In(lx, ly)) continue;
            int a = nb[ax, ay];
            if (!IsPlayer(a) || nb[lx, ly] != Empty) continue;
            if (a == PMan && dy != -1) continue; // your men only jump upwards
            return true;
        }
        return false;
    }

    private bool PromoteIf(int x, int y)
    {
        if (_b[x, y] == PMan && y == 0) { _b[x, y] = PKing; return true; }
        if (_b[x, y] == CMan && y == Side - 1) { _b[x, y] = CKing; return true; }
        return false;
    }

    private List<Mv> MovesFrom(int x, int y)
    {
        var result = new List<Mv>();
        int p = _b[x, y];
        if (p == Empty) return result;
        var dirs = p is PKing or CKing ? AllDirs : IsPlayer(p) ? PlayerDirs : CpuDirs;
        foreach (var (dx, dy) in dirs)
        {
            int tx = x + dx, ty = y + dy;
            if (!In(tx, ty)) continue;
            if (_b[tx, ty] == Empty)
            {
                result.Add(new Mv(tx, ty, -1, -1));
                continue;
            }
            if (IsPlayer(p) ? IsCpu(_b[tx, ty]) : IsPlayer(_b[tx, ty]))
            {
                int lx = x + 2 * dx, ly = y + 2 * dy;
                if (In(lx, ly) && _b[lx, ly] == Empty) result.Add(new Mv(lx, ly, tx, ty));
            }
        }
        return result;
    }

    private List<(int X, int Y, Mv M)> AllCaptures(bool player)
    {
        var result = new List<(int, int, Mv)>();
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                if (player ? IsPlayer(_b[x, y]) : IsCpu(_b[x, y]))
                    foreach (var m in MovesFrom(x, y))
                        if (m.MidX >= 0) result.Add((x, y, m));
        return result;
    }

    private bool SideHasCapture(bool player) => AllCaptures(player).Count > 0;

    private bool SideCanMove(bool player)
    {
        if (SideHasCapture(player)) return true;
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                if (player ? IsPlayer(_b[x, y]) : IsCpu(_b[x, y]))
                    if (MovesFrom(x, y).Count > 0) return true;
        return false;
    }

    private int CountPieces(bool player)
    {
        int n = 0;
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                if (player ? IsPlayer(_b[x, y]) : IsCpu(_b[x, y])) n++;
        return n;
    }

    private static bool In(int x, int y) => x >= 0 && x < Side && y >= 0 && y < Side;
    private static bool Dark(int x, int y) => (x + y) % 2 == 1;
    private static bool IsPlayer(int p) => p is PMan or PKing;
    private static bool IsCpu(int p) => p is CMan or CKing;

    public override void Redraw()
    {
        bool forced = !_over && SideHasCapture(true);
        var dests = new List<Mv>();
        if (!_over && _selX >= 0)
            dests = MovesFrom(_selX, _selY)
                .Where(m => _chain ? m.MidX >= 0 : !forced || m.MidX >= 0)
                .ToList();

        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                var c = _cells[y * Side + x];
                int p = _b[x, y];
                c.Background = Dark(x, y) ? Board : Cell;
                c.Text = p == Empty ? "" : p is PKing or CKing ? "◉" : IsPlayer(p) ? "●" : "○";
                c.Foreground = p != Empty && IsCpu(p) ? Bad : Ink;
                c.FontSize = 22;
                c.Bold = p != Empty;
                c.BorderBrush = null;
                c.BorderThickness = new Avalonia.Thickness(0);
                c.IsEnabled = false;

                if (_over) continue;
                bool selected = _selX == x && _selY == y;
                if (selected)
                {
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(2);
                }
                if (dests.Any(m => m.Tx == x && m.Ty == y))
                {
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(1);
                    c.IsEnabled = true;
                    if (p == Empty) { c.Text = "·"; c.Foreground = Muted; }
                    continue;
                }
                if (!_chain && IsPlayer(p) && MovesFrom(x, y).Any(m => !forced || m.MidX >= 0))
                    c.IsEnabled = true;
            }
    }
}
