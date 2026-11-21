using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Connect 4 board rules, over the SAME flat array the official client uses:
/// <c>grid[row * 7 + col]</c>, rows 0..5 top-to-bottom, cols 0..6 left-to-right,
/// values 0 = empty, 1 = Red, 2 = Yellow. Sharing that layout (and this one
/// implementation) is what keeps an online match with the site correct, and keeps
/// the offline game from drifting away from the online one.
///
/// The online client names the two sides "R" and "Y"; see <see cref="Red"/> and
/// <see cref="Yellow"/>.
/// </summary>
public static class C4Rules
{
    public const int Cols = 7;
    public const int Rows = 6;
    public const int Win = 4;

    public const int Empty = 0;
    public const int Red = 1;
    public const int Yellow = 2;

    public const string RedTurn = "R";
    public const string YellowTurn = "Y";

    /// <summary>"R"/"Y" to a piece value.</summary>
    public static int PieceFor(string? turn) =>
        string.Equals(turn, YellowTurn, StringComparison.Ordinal) ? Yellow : Red;

    /// <summary>A piece value back to "R"/"Y".</summary>
    public static string TurnFor(int piece) => piece == Yellow ? YellowTurn : RedTurn;

    public static int Index(int col, int row) => row * Cols + col;

    /// <summary>Row a piece dropped in <paramref name="col"/> would land on, or -1 if full.</summary>
    public static int DropRow(int[] grid, int col)
    {
        if (grid is null || col < 0 || col >= Cols) return -1;
        for (int row = Rows - 1; row >= 0; row--)
            if (grid[Index(col, row)] == Empty) return row;
        return -1;
    }

    /// <summary>Applies a drop. Returns false when the column is full or the move is out of range.</summary>
    public static bool TryDrop(int[] grid, int col, int piece)
    {
        int row = DropRow(grid, col);
        if (row < 0) return false;
        grid[Index(col, row)] = piece;
        return true;
    }

    /// <summary>True when <paramref name="piece"/> has four in a row through its last landing spot.</summary>
    public static bool Wins(int[] grid, int col, int row, int piece)
    {
        if (grid is null || col < 0 || col >= Cols || row < 0 || row >= Rows) return false;
        if (grid[Index(col, row)] != piece) return false;

        foreach (var (dc, dr) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
        {
            int run = 1;
            foreach (var sign in new[] { 1, -1 })
                for (int step = 1; ; step++)
                {
                    int c = col + dc * step * sign;
                    int r = row + dr * step * sign;
                    if (c < 0 || c >= Cols || r < 0 || r >= Rows) break;
                    if (grid[Index(c, r)] != piece) break;
                    run++;
                }
            if (run >= Win) return true;
        }
        return false;
    }

    /// <summary>Columns that still accept a drop — what the official client shows as legal moves.</summary>
    public static IEnumerable<int> LegalMoves(int[] grid)
    {
        for (int col = 0; col < Cols; col++)
            if (DropRow(grid, col) >= 0) yield return col;
    }

    public static bool IsFull(int[] grid) => !LegalMoves(grid).Any();

    /// <summary>
    /// Applies a move and reports what it means, in one step — the same sequence
    /// the official client runs, so the two agree on when a game ends and who won.
    /// </summary>
    public static C4Outcome Apply(int[] grid, int col, int piece)
    {
        int row = DropRow(grid, col);
        if (row < 0) return new C4Outcome(false, piece == Red ? RedTurn : YellowTurn, false, false);
        grid[Index(col, row)] = piece;
        if (Wins(grid, col, row, piece)) return new C4Outcome(true, TurnFor(piece), true, false);
        if (IsFull(grid)) return new C4Outcome(true, TurnFor(piece), false, true);
        return new C4Outcome(true, TurnFor(piece == Red ? Yellow : Red), false, false);
    }
}

/// <summary>What a move did: was it legal, whose turn is next, and did the game end.</summary>
public readonly record struct C4Outcome(bool Moved, string NextTurn, bool Won, bool Draw);
