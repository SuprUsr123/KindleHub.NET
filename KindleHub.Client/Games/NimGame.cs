using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Nim — take any number from one row; whoever takes the last counter wins.
/// The board says whether the position is winning or losing (the XOR rule), which
/// is the lesson the official game is built around.
/// </summary>
public sealed class NimGame : GameBase
{
    private const int Rows = 3;
    public const int Cols = 8;

    private readonly List<GameCell> _cells = new();
    private readonly int[] _piles = new int[Rows];
    private int _taken;
    private bool _won;
    private bool _lost;

    public NimGame()
    {
        for (int i = 0; i < Rows * Cols; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "nim";
    public override string Name => "Nim";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override string? ResultText =>
        _won ? $"You took the last counter — {_taken} moves." :
        _lost ? "The computer took the last counter." : null;

    public override string StatusText
    {
        get
        {
            if (_won) return $"You win — took {_taken} moves.";
            if (_lost) return "Computer wins. New game reshuffles.";
            return $"{_taken} moves · this position is {(Winning ? "winning" : "losing")} for whoever moves";
        }
    }

    /// <summary>The Nim invariant: a position is losing exactly when every pile XORs to zero.</summary>
    public bool Winning => _piles.Aggregate(0, (a, b) => a ^ b) != 0;

    public override void Reset()
    {
        for (int i = 0; i < Rows; i++) _piles[i] = Random.Shared.Next(1, Cols + 1);
        _taken = 0;
        _won = _lost = false;
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        // Cells are laid out row-major, so a cell index has to be mapped down to a
        // row before it means anything.
        int row = index < 0 ? -1 : index / Cols;
        if (_won || _lost || row < 0 || row >= _piles.Length) return false;
        if (_piles[row] == 0) return true;

        // Tapping takes one counter from that row.
        _piles[row]--;
        _taken++;
        CheckOver();
        Redraw();
        return true;
    }

    /// <summary>Take the rest of a pile in one go — the move Nim is actually about.</summary>
    public void TakeAll(int row)
    {
        if (_won || _lost || row < 0 || row >= _piles.Length || _piles[row] == 0) return;
        _taken++;
        _piles[row] = 0;
        CheckOver();
        Redraw();
    }

    private void CheckOver()
    {
        if (_piles.Any(p => p > 0)) return;
        _won = true;
        ScoreCounts = true;
    }

    public override void Redraw()
    {
        for (int r = 0; r < Rows; r++)
        for (int c = 0; c < Cols; c++)
        {
            var cell = _cells[r * Cols + c];
            bool filled = c < _piles[r];
            cell.Text = filled ? "●" : "";
            cell.Background = filled ? Tiles4[2] : Board;
            cell.Foreground = filled ? Ink : Muted;
            cell.IsEnabled = filled;
            cell.FontSize = 20;
            cell.Bold = false;
        }
    }
}
