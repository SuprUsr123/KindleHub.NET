using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Chess against the computer, ported from the official client. Tap a piece, then
/// tap a highlighted square; the engine answers synchronously with a two-ply
/// alpha-beta search over material and piece-square tables. Castling, en passant
/// and promotion are all in — a pawn reaching the last rank opens a Q/R/B/N picker
/// (Enter defaults to the queen).
/// </summary>
public sealed class ChessGame : GameBase
{
    private const int Side = 8;
    private const int TotalRows = 9; // 8 board rows + the promotion picker row

    private readonly record struct Move(int Fx, int Fy, int Tx, int Ty, char Promo);

    private static readonly (int Dx, int Dy)[] Ortho = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int Dx, int Dy)[] Diag = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    private static readonly (int Dx, int Dy)[] Leaps =
        { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) };
    private static readonly (int Dx, int Dy)[] All8 =
        { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    private readonly List<GameCell> _cells = new();
    private readonly char[,] _board = new char[Side, Side]; // [x, y]; y = 7 is your back rank
    private bool[] _castle = new bool[4];                     // your K, your Q, CPU K, CPU Q
    private readonly HashSet<int> _dests = new();
    private int _epX = -1, _epY = -1;
    private int _selX = -1, _selY = -1;
    private Move _pending;
    private bool _promoPending;
    private int _lastFrom = -1, _lastTo = -1;
    private bool _over, _win, _draw;
    private bool _check;
    private string _msg = "Your move.";

    public ChessGame()
    {
        for (int i = 0; i < Side * TotalRows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "chess";
    public override string Name => "Chess";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _win ? 500 + 10 * Math.Max(0, MaterialAdvantage()) : 0;

    public override string? ResultText => !_over ? null
        : _win ? $"Checkmate — you win with {Score} points."
        : _draw ? "Stalemate — a draw."
        : "Checkmate — the computer wins.";

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            if (_promoPending) return "Promote: tap Q, R, B or N — Enter picks Q.";
            if (_check) return "Check! Tap a piece.";
            return _msg;
        }
    }

    public override void Reset()
    {
        Array.Clear(_board, 0, _board.Length);
        const string back = "rnbqkbnr";
        for (int x = 0; x < Side; x++)
        {
            _board[x, 0] = back[x];
            _board[x, 1] = 'p';
            _board[x, 6] = 'P';
            _board[x, 7] = char.ToUpperInvariant(back[x]);
        }
        Array.Fill(_castle, true);
        _epX = _epY = -1;
        _selX = _selY = -1;
        _dests.Clear();
        _promoPending = false;
        _lastFrom = _lastTo = -1;
        _over = _win = _draw = false;
        _check = false;
        ScoreCounts = false;
        _msg = "Your move · tap a piece, then a square.";
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count) return false;

        if (_promoPending)
        {
            int col = index % Side, row = index / Side;
            if (row == 8 && col <= 3)
            {
                CompletePromotion("QRBN"[col]);
                return true;
            }
            return false;
        }

        if (_over) return false;
        int x = index % Side, y = index / Side;
        if (y >= Side) return false;

        if (_selX >= 0 && _dests.Contains(index))
        {
            bool promo = _board[_selX, _selY] == 'P' && y == 0;
            if (promo)
            {
                _pending = new Move(_selX, _selY, x, y, 'Q');
                _promoPending = true;
                _msg = "Promote to?";
                Redraw();
                return true;
            }
            PlayerMove(new Move(_selX, _selY, x, y, '\0'));
            Redraw();
            return true;
        }

        char piece = _board[x, y];
        if (piece != '.' && IsWhite(piece))
        {
            _selX = x;
            _selY = y;
            _dests.Clear();
            foreach (var m in LegalMoves(_board, true, _castle, _epX, _epY))
                if (m.Fx == x && m.Fy == y) _dests.Add(m.Ty * Side + m.Tx);
            _msg = _dests.Count == 0 ? "That piece can't move." : "Pick a square.";
            Redraw();
            return true;
        }

        if (_selX >= 0)
        {
            _selX = _selY = -1;
            _dests.Clear();
            Redraw();
            return true;
        }
        return false;
    }

    public override bool OnKey(GameKey key)
    {
        if (key != GameKey.Confirm || !_promoPending) return false;
        CompletePromotion('Q');
        return true;
    }

    private void CompletePromotion(char promo)
    {
        _promoPending = false;
        PlayerMove(_pending with { Promo = promo });
        Redraw();
    }

    private void PlayerMove(Move m)
    {
        (_castle, _epX, _epY) = ApplyMove(_board, m, true, _castle, _epX, _epY);
        _selX = _selY = -1;
        _dests.Clear();

        var cpu = LegalMoves(_board, false, _castle, _epX, _epY);
        if (cpu.Count == 0)
        {
            _over = true;
            if (InCheck(false))
            {
                _win = true;
                ScoreCounts = true;
            }
            else _draw = true;
            return;
        }

        AiMove();

        var mine = LegalMoves(_board, true, _castle, _epX, _epY);
        if (mine.Count == 0)
        {
            _over = true;
            if (InCheck(true)) _win = false;
            else _draw = true;
            return;
        }

        _check = InCheck(true);
        _msg = _check ? "Check!" : "Your move.";
    }

    /// <summary>Two-ply search: engine move, your best reply, material + piece squares.</summary>
    private void AiMove()
    {
        var root = LegalMoves(_board, false, _castle, _epX, _epY)
            .OrderByDescending(m => m.Promo != '\0' ? 900 : ValueOf(_board[m.Tx, m.Ty]))
            .ToList();
        if (root.Count == 0) return;

        Move best = root[0];
        int bestScore = int.MinValue;
        foreach (var m in root)
        {
            var b = (char[,])_board.Clone();
            var (castle, epX, epY) = ApplyMove(b, m, false, _castle, _epX, _epY);

            int score;
            var replies = LegalMoves(b, true, castle, epX, epY);
            if (replies.Count == 0)
                score = InCheckOn(b, true) ? 500000 : 0; // mate delivered, or stalemate
            else
            {
                score = int.MaxValue;
                foreach (var r in replies)
                {
                    var b2 = (char[,])b.Clone();
                    ApplyMove(b2, r, true, castle, epX, epY);
                    int v = EvalBlack(b2);
                    if (v < score) score = v;
                    if (score <= bestScore) break; // cannot beat the best so far
                }
            }
            score += Random.Shared.Next(3);
            if (score > bestScore)
            {
                bestScore = score;
                best = m;
            }
        }

        _lastFrom = best.Fy * Side + best.Fx;
        _lastTo = best.Ty * Side + best.Tx;
        (_castle, _epX, _epY) = ApplyMove(_board, best, false, _castle, _epX, _epY);
    }

    // ─── rules ───────────────────────────────────────────────────────────────

    private static bool IsWhite(char p) => p is >= 'A' and <= 'Z';
    private static bool In(int x, int y) => x >= 0 && x < Side && y >= 0 && y < Side;

    private static int ValueOf(char p) => p switch
    {
        'P' or 'p' => 100,
        'N' or 'n' => 320,
        'B' or 'b' => 330,
        'R' or 'r' => 500,
        'Q' or 'q' => 900,
        _ => 0,
    };

    private static List<Move> GenMoves(char[,] b, bool white, bool[] castle, int epX, int epY)
    {
        var moves = new List<Move>();
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                char p = b[x, y];
                if (p == '.' || IsWhite(p) != white) continue;
                char kind = char.ToLowerInvariant(p);
                int dir = white ? -1 : 1;
                int start = white ? 6 : 1;
                int last = white ? 0 : 7;

                switch (kind)
                {
                    case 'p':
                    {
                        int fy = y + dir;
                        if (In(x, fy) && b[x, fy] == '.')
                        {
                            AddPawn(moves, x, y, x, fy, last);
                            int dy2 = y + 2 * dir;
                            if (y == start && In(x, dy2) && b[x, dy2] == '.')
                                moves.Add(new Move(x, y, x, dy2, '\0'));
                        }
                        foreach (int dx in new[] { -1, 1 })
                        {
                            int cx = x + dx, cy = y + dir;
                            if (!In(cx, cy)) continue;
                            char t = b[cx, cy];
                            if (t != '.' && IsWhite(t) != white) AddPawn(moves, x, y, cx, cy, last);
                            else if (cx == epX && cy == epY && t == '.')
                                moves.Add(new Move(x, y, cx, cy, '\0'));
                        }
                        break;
                    }
                    case 'n':
                        foreach (var (dx, dy) in Leaps)
                        {
                            int tx = x + dx, ty = y + dy;
                            if (In(tx, ty) && (b[tx, ty] == '.' || IsWhite(b[tx, ty]) != white))
                                moves.Add(new Move(x, y, tx, ty, '\0'));
                        }
                        break;
                    case 'b':
                    case 'r':
                    case 'q':
                    {
                        var dirs = kind == 'b' ? Diag : kind == 'r' ? Ortho : All8;
                        foreach (var (dx, dy) in dirs)
                            for (int tx = x + dx, ty = y + dy; In(tx, ty); tx += dx, ty += dy)
                            {
                                if (b[tx, ty] == '.')
                                {
                                    moves.Add(new Move(x, y, tx, ty, '\0'));
                                    continue;
                                }
                                if (IsWhite(b[tx, ty]) != white) moves.Add(new Move(x, y, tx, ty, '\0'));
                                break;
                            }
                        break;
                    }
                    case 'k':
                    {
                        foreach (var (dx, dy) in All8)
                        {
                            int tx = x + dx, ty = y + dy;
                            if (In(tx, ty) && (b[tx, ty] == '.' || IsWhite(b[tx, ty]) != white))
                                moves.Add(new Move(x, y, tx, ty, '\0'));
                        }
                        int home = white ? 7 : 0;
                        int ki = white ? 0 : 2, qi = white ? 1 : 3;
                        char rook = white ? 'R' : 'r';
                        if (x == 4 && y == home && !Attacked(b, 4, home, !white))
                        {
                            if (castle[ki] && b[7, home] == rook
                                && b[5, home] == '.' && b[6, home] == '.'
                                && !Attacked(b, 5, home, !white) && !Attacked(b, 6, home, !white))
                                moves.Add(new Move(4, home, 6, home, '\0'));
                            if (castle[qi] && b[0, home] == rook
                                && b[1, home] == '.' && b[2, home] == '.' && b[3, home] == '.'
                                && !Attacked(b, 3, home, !white) && !Attacked(b, 2, home, !white))
                                moves.Add(new Move(4, home, 2, home, '\0'));
                        }
                        break;
                    }
                }
            }
        return moves;
    }

    private static void AddPawn(List<Move> moves, int fx, int fy, int tx, int ty, int last)
    {
        if (ty == last) moves.Add(new Move(fx, fy, tx, ty, 'Q'));
        else moves.Add(new Move(fx, fy, tx, ty, '\0'));
    }

    private static bool Attacked(char[,] b, int x, int y, bool byWhite)
    {
        char pawn = byWhite ? 'P' : 'p';
        int pdy = byWhite ? 1 : -1;
        foreach (int dx in new[] { -1, 1 })
        {
            int px = x + dx, py = y + pdy;
            if (In(px, py) && b[px, py] == pawn) return true;
        }

        char knight = byWhite ? 'N' : 'n';
        foreach (var (dx, dy) in Leaps)
        {
            int tx = x + dx, ty = y + dy;
            if (In(tx, ty) && b[tx, ty] == knight) return true;
        }

        char king = byWhite ? 'K' : 'k';
        foreach (var (dx, dy) in All8)
        {
            int tx = x + dx, ty = y + dy;
            if (In(tx, ty) && b[tx, ty] == king) return true;
        }

        char rook = byWhite ? 'R' : 'r';
        char queen = byWhite ? 'Q' : 'q';
        char bishop = byWhite ? 'B' : 'b';
        foreach (var (dx, dy) in Ortho)
            for (int tx = x + dx, ty = y + dy; In(tx, ty); tx += dx, ty += dy)
            {
                char t = b[tx, ty];
                if (t == '.') continue;
                if (t == rook || t == queen) return true;
                break;
            }
        foreach (var (dx, dy) in Diag)
            for (int tx = x + dx, ty = y + dy; In(tx, ty); tx += dx, ty += dy)
            {
                char t = b[tx, ty];
                if (t == '.') continue;
                if (t == bishop || t == queen) return true;
                break;
            }
        return false;
    }

    /// <summary>Applies a move to <paramref name="b"/> and returns the new rights and ep square.</summary>
    private static (bool[] Castle, int EpX, int EpY) ApplyMove(
        char[,] b, Move m, bool white, bool[] castle, int epX, int epY)
    {
        char p = b[m.Fx, m.Fy];
        b[m.Fx, m.Fy] = '.';

        bool enPassant = (p == 'P' || p == 'p') && m.Tx == epX && m.Ty == epY && b[m.Tx, m.Ty] == '.';
        if (enPassant) b[m.Tx, m.Fy] = '.';

        b[m.Tx, m.Ty] = m.Promo != '\0' ? (white ? m.Promo : char.ToLowerInvariant(m.Promo)) : p;

        if ((p == 'K' || p == 'k') && Math.Abs(m.Tx - m.Fx) == 2)
        {
            int home = m.Fy;
            if (m.Tx == 6)
            {
                b[5, home] = b[7, home];
                b[7, home] = '.';
            }
            else
            {
                b[3, home] = b[0, home];
                b[0, home] = '.';
            }
        }

        var rights = (bool[])castle.Clone();
        if (p == 'K') rights[0] = rights[1] = false;
        if (p == 'k') rights[2] = rights[3] = false;
        foreach (var (sx, sy, i) in new[] { (7, 7, 0), (0, 7, 1), (7, 0, 2), (0, 0, 3) })
            if ((m.Fx == sx && m.Fy == sy) || (m.Tx == sx && m.Ty == sy)) rights[i] = false;

        int newEpX = -1, newEpY = -1;
        if ((p == 'P' || p == 'p') && Math.Abs(m.Ty - m.Fy) == 2)
        {
            newEpX = m.Fx;
            newEpY = (m.Fy + m.Ty) / 2;
        }
        return (rights, newEpX, newEpY);
    }

    private static List<Move> LegalMoves(char[,] b, bool white, bool[] castle, int epX, int epY)
    {
        var legal = new List<Move>();
        foreach (var m in GenMoves(b, white, castle, epX, epY))
        {
            var probe = (char[,])b.Clone();
            ApplyMove(probe, m, white, castle, epX, epY);
            if (!InCheckOn(probe, white)) legal.Add(m);
        }
        return legal;
    }

    private static bool InCheckOn(char[,] b, bool white)
    {
        char k = white ? 'K' : 'k';
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                if (b[x, y] == k) return Attacked(b, x, y, !white);
        return false;
    }

    private bool InCheck(bool white) => InCheckOn(_board, white);

    private int MaterialAdvantage()
    {
        int adv = 0;
        foreach (char p in _board)
        {
            int v = p switch
            {
                'P' or 'p' => 1,
                'N' or 'n' => 3,
                'B' or 'b' => 3,
                'R' or 'r' => 5,
                'Q' or 'q' => 9,
                _ => 0,
            };
            if (v == 0) continue;
            adv += IsWhite(p) ? v : -v;
        }
        return adv;
    }

    // ─── evaluation (positive = good for the computer) ───────────────────────

    private static readonly int[] PawnPst =
    {
         0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
         5,  5, 10, 25, 25, 10,  5,  5,
         0,  0,  0, 20, 20,  0,  0,  0,
         5, -5,-10,  0,  0,-10, -5,  5,
         5, 10, 10,-20,-20, 10, 10,  5,
         0,  0,  0,  0,  0,  0,  0,  0,
    };
    private static readonly int[] KnightPst =
    {
        -50,-40,-30,-30,-30,-30,-40,-50,
        -40,-20,  0,  0,  0,  0,-20,-40,
        -30,  0, 10, 15, 15, 10,  0,-30,
        -30,  5, 15, 20, 20, 15,  5,-30,
        -30,  0, 15, 20, 20, 15,  0,-30,
        -30,  5, 10, 15, 15, 10,  5,-30,
        -40,-20,  0,  5,  5,  0,-20,-40,
        -50,-40,-30,-30,-30,-30,-40,-50,
    };
    private static readonly int[] BishopPst =
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    };
    private static readonly int[] RookPst =
    {
          0,  0,  0,  0,  0,  0,  0,  0,
          5, 10, 10, 10, 10, 10, 10,  5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
          0,  0,  0,  5,  5,  0,  0,  0,
    };
    private static readonly int[] QueenPst =
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
         -5,  0,  5,  5,  5,  5,  0, -5,
          0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20,
    };
    private static readonly int[] KingPst =
    {
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -20,-30,-30,-40,-40,-30,-30,-20,
        -10,-20,-20,-20,-20,-20,-20,-10,
         20, 20,  0,  0,  0,  0, 20, 20,
         20, 30, 10,  0,  0, 10, 30, 20,
    };

    private static int EvalBlack(char[,] b)
    {
        int score = 0;
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                char p = b[x, y];
                if (p == '.') continue;
                bool white = IsWhite(p);
                var table = char.ToLowerInvariant(p) switch
                {
                    'p' => PawnPst,
                    'n' => KnightPst,
                    'b' => BishopPst,
                    'r' => RookPst,
                    'q' => QueenPst,
                    _ => KingPst,
                };
                int pst = white ? table[y * Side + x] : table[(Side - 1 - y) * Side + x];
                int v = (ValueOf(p) == 0 ? 20000 : ValueOf(p)) + pst;
                score += white ? -v : v;
            }
        return score;
    }

    // ─── drawing ─────────────────────────────────────────────────────────────

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 20;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                var c = _cells[y * Side + x];
                int at = y * Side + x;
                c.Background = (x + y) % 2 == 0 ? Cell : Board;
                char p = _board[x, y];
                if (p != '.')
                {
                    c.Text = p.ToString();
                    c.Foreground = IsWhite(p) ? Ink : Bad;
                    c.Bold = true;
                    c.IsEnabled = !_over && IsWhite(p);
                }
                else if (_dests.Contains(at))
                {
                    c.Text = "·";
                    c.Foreground = Accent;
                    c.IsEnabled = !_over;
                }

                if (_selX == x && _selY == y)
                {
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(2);
                }
                else if (_dests.Contains(at))
                {
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(1);
                }
                else if (!_over && (at == _lastFrom || at == _lastTo))
                {
                    c.BorderBrush = Warn;
                    c.BorderThickness = new Avalonia.Thickness(1);
                }
            }

        // Promotion picker on row 8.
        for (int col = 0; col < 4; col++)
        {
            var c = _cells[Side * Side + col];
            c.Text = _promoPending ? "QRBN"[col].ToString() : "";
            c.Background = _promoPending ? Cell : Board;
            c.Foreground = Ink;
            c.IsEnabled = _promoPending;
            c.FontSize = 18;
            c.Bold = true;
            c.BorderBrush = _promoPending ? Accent : null;
            c.BorderThickness = _promoPending ? new Avalonia.Thickness(2) : new Avalonia.Thickness(0);
        }
    }
}
