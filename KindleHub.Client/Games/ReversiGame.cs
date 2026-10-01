using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Reversi (Othello) against a greedy computer. You play the dark discs; a legal
/// move must trap a run of light discs, which then flip.
/// </summary>
public sealed class ReversiGame : GameBase
{
    public const int Side = 8;

    private const int Empty = 0, Dark = 1, Light = 2;

    private readonly List<GameCell> _cells = new();
    private readonly int[,] _board = new int[Side, Side];
    private int _turn = Dark;
    private int _winsDark, _winsLight;
    private bool _over;

    public ReversiGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "reversi";
    public override string Name => "Reversi";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override string? ResultText => _over
        ? (_winsDark == _winsLight ? "A draw." : $"You {_winsDark} — computer {_winsLight}")
        : null;

    /// <summary>Your final disc count — more discs is the better result.</summary>
    public override int Score => _winsDark;

    public override string StatusText => _over
        ? (ResultText ?? "")
        : _turn == Dark
            ? $"You play dark · dark {_winsDark} · light {_winsLight}"
            : "Computer is thinking…";

    public override void Reset()
    {
        Array.Clear(_board, 0, _board.Length);
        _board[3, 3] = _board[4, 4] = Light;
        _board[4, 3] = _board[3, 4] = Dark;
        _turn = Dark;
        _over = false;
        ScoreCounts = false;
        Tally();
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_over || _turn != Dark || index < 0 || index >= _cells.Count) return false;
        int x = index % Side, y = index / Side;
        if (_board[x, y] != Empty) return true;

        var flips = FlipsFor(x, y, Dark);
        if (flips.Count == 0) return true;

        _board[x, y] = Dark;
        foreach (var (fx, fy) in flips) _board[fx, fy] = Dark;
        Tally();
        _turn = Light;
        AdvanceTurns();
        Redraw();
        return true;
    }

    private void AdvanceTurns()
    {
        while (!_over)
        {
            var darkMoves = LegalMoves(Dark);
            var lightMoves = LegalMoves(Light);
            if (darkMoves.Count == 0 && lightMoves.Count == 0)
            {
                _over = true;
                Tally();
                ScoreCounts = true;
                return;
            }

            if (_turn == Dark)
            {
                if (darkMoves.Count > 0) return;
                _turn = Light; // human has no legal move, so pass
                continue;
            }

            if (lightMoves.Count == 0)
            {
                _turn = Dark; // computer passes
                continue;
            }

            MakeComputerMove(lightMoves);
            _turn = Dark;
        }
    }

    private void MakeComputerMove(List<(int X, int Y)> moves)
    {

        // Greedy: take the move that flips the most discs, breaking ties toward corners.
        (int X, int Y) best = moves[0];
        int bestScore = int.MinValue;
        foreach (var m in moves)
        {
            int flips = FlipsFor(m.X, m.Y, Light).Count;
            int corner = (m.X is 0 or 7 && m.Y is 0 or 7) ? 6 : 0;
            int score = flips * 2 + corner;
            if (score > bestScore) { bestScore = score; best = m; }
        }

        _board[best.X, best.Y] = Light;
        foreach (var (fx, fy) in FlipsFor(best.X, best.Y, Light)) _board[fx, fy] = Light;
        Tally();

        // Turn advancement is handled by AdvanceTurns so passes on either side
        // cannot strand the board with no clickable legal move.
    }

    /// <summary>Discs of <paramref name="who"/> that a move at (x,y) would flip.</summary>
    private List<(int X, int Y)> FlipsFor(int x, int y, int who)
    {
        var outp = new List<(int, int)>();
        int other = who == Dark ? Light : Dark;
        foreach (var (dx, dy) in Directions)
        {
            var run = new List<(int, int)>();
            int cx = x + dx, cy = y + dy;
            while (cx >= 0 && cx < Side && cy >= 0 && cy < Side && _board[cx, cy] == other)
            {
                run.Add((cx, cy));
                cx += dx; cy += dy;
            }
            if (run.Count > 0 && cx >= 0 && cx < Side && cy >= 0 && cy < Side && _board[cx, cy] == who)
                outp.AddRange(run);
        }
        return outp;
    }

    private static readonly (int dx, int dy)[] Directions =
    {
        (-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1),
    };

    public List<(int X, int Y)> LegalMoves(int who)
    {
        var moves = new List<(int, int)>();
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                if (_board[x, y] == Empty && FlipsFor(x, y, who).Count > 0)
                    moves.Add((x, y));
        return moves;
    }

    private void Tally()
    {
        _winsDark = _winsLight = 0;
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                if (_board[x, y] == Dark) _winsDark++;
                else if (_board[x, y] == Light) _winsLight++;
            }
    }

    public override void Redraw()
    {
        var legal = !_over && _turn == Dark
            ? LegalMoves(Dark).ToHashSet()
            : new HashSet<(int X, int Y)>();

        for (int y = 0; y < Side; y++)
        for (int x = 0; x < Side; x++)
        {
            var c = _cells[y * Side + x];
            int v = _board[x, y];
            bool isLegal = legal.Contains((x, y));
            c.Text = v == Empty ? (isLegal ? "·" : "") : "●";
            c.Background = isLegal ? Tiles4[1] : v == Dark ? Ink : v == Light ? Cell : Good;
            c.Foreground = v == Light ? Ink : isLegal ? Ink : Cell;
            c.IsEnabled = isLegal;
            c.FontSize = 26;
            c.Bold = true;
        }
    }
}
