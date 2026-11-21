using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Connect 4 against the computer. Tapping a column drops your disc there; the
/// computer replies with a blocking move. First to four in a row wins.
///
/// The rules live in <see cref="C4Rules"/> — the same code the online session
/// uses — so the offline and online games can never disagree about what counts as
/// a win or which column a drop lands in.
/// </summary>
public sealed class Connect4Game : GameBase
{
    private readonly List<GameCell> _cells = new();
    private readonly int[] _grid = new int[C4Rules.Cols * C4Rules.Rows];
    private readonly Random _rng = new();
    private int _winner;
    private bool _full;

    public Connect4Game()
    {
        for (int i = 0; i < C4Rules.Cols * C4Rules.Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "connect4";
    public override string Name => "Connect 4";
    public override int Columns => C4Rules.Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override string? ResultText =>
        _winner == C4Rules.Red ? "You win!" :
        _winner == C4Rules.Yellow ? "The computer wins." :
        _full ? "Board full — a draw." : null;

    public override int Score => _winner == C4Rules.Red ? 1 : 0;

    public override string StatusText => _winner != 0 || _full
        ? (ResultText ?? "")
        : $"Your turn · heights left to right: {string.Join(" ", _heightHeights)}";

    private readonly List<int> _heightHeights = new();

    public override void Reset()
    {
        Array.Clear(_grid, 0, _grid.Length);
        _heightHeights.Clear();
        for (int c = 0; c < C4Rules.Cols; c++) _heightHeights.Add(0);
        _winner = 0;
        _full = false;
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_winner != 0 || _full || index < 0 || index >= _cells.Count) return false;
        int col = index % C4Rules.Cols;
        if (!Drop(col, C4Rules.Red)) return true;
        if (_winner == 0 && !_full) ComputerMove();
        Redraw();
        return true;
    }

    private bool Drop(int col, int piece)
    {
        int row = C4Rules.DropRow(_grid, col);
        if (row < 0) return false;
        _grid[C4Rules.Index(col, row)] = piece;
        _heightHeights[col]++;
        if (C4Rules.Wins(_grid, col, row, piece))
        {
            _winner = piece;
            // A win over the computer is worth a leaderboard entry, like the other games.
            if (piece == C4Rules.Red) ScoreCounts = true;
        }
        else if (C4Rules.IsFull(_grid)) _full = true;
        return true;
    }

    /// <summary>Take a win, else a block, else a near-win, else the middle.</summary>
    private void ComputerMove()
    {
        int candidate = FindWinningMove(C4Rules.Yellow) ?? FindWinningMove(C4Rules.Red) ?? -1;
        if (candidate < 0) candidate = BestMove();
        if (candidate >= 0) Drop(candidate, C4Rules.Yellow);
    }

    private int? FindWinningMove(int piece)
    {
        foreach (int c in C4Rules.LegalMoves(_grid))
        {
            int row = C4Rules.DropRow(_grid, c);
            _grid[C4Rules.Index(c, row)] = piece;
            bool win = C4Rules.Wins(_grid, c, row, piece);
            _grid[C4Rules.Index(c, row)] = 0;
            if (win) return c;
        }
        return null;
    }

    /// <summary>
    /// Otherwise play the column that leaves the fewest threats and sits nearer the
    /// middle — crude, but it blocks and builds instead of scattering.
    /// </summary>
    private int BestMove()
    {
        int best = -1, bestScore = int.MinValue;
        foreach (int c in C4Rules.LegalMoves(_grid))
        {
            int score = -Math.Abs(c - (C4Rules.Cols - 1) / 2) * 2;
            int row = C4Rules.DropRow(_grid, c);

            _grid[C4Rules.Index(c, row)] = C4Rules.Yellow;
            foreach (int d in C4Rules.LegalMoves(_grid))
            {
                int r2 = C4Rules.DropRow(_grid, d);
                _grid[C4Rules.Index(d, r2)] = C4Rules.Red;
                if (C4Rules.Wins(_grid, d, r2, C4Rules.Red)) score -= 10;
                _grid[C4Rules.Index(d, r2)] = 0;
            }
            _grid[C4Rules.Index(c, row)] = 0;

            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    public override void Redraw()
    {
        for (int r = 0; r < C4Rules.Rows; r++)
            for (int c = 0; c < C4Rules.Cols; c++)
            {
                int v = _grid[C4Rules.Index(c, r)];
                var cell = _cells[r * C4Rules.Cols + c];
                cell.Text = v == C4Rules.Empty ? "" : "●";
                cell.Background = v == C4Rules.Empty ? Cell : v == C4Rules.Red ? Bad : Warn;
                cell.Foreground = v == C4Rules.Empty ? Muted : Cell;
                cell.IsEnabled = v == C4Rules.Empty && _winner == 0 && !_full;
                cell.FontSize = 26;
                cell.Bold = true;
            }
    }
}
