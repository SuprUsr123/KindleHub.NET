using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Nim — take any positive number of counters from exactly one row; whoever takes
/// the last counter wins. Tapping a counter removes it and every counter to its
/// right in that row, so one tap can make the whole legal move. The computer always
/// returns to a zero-nim-sum position when one exists.
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
    public override int Score => _won ? 10 : 0;
    public override string? ResultText =>
        _won ? $"You took the last counter — {_taken} moves." :
        _lost ? "The computer took the last counter." : null;

    public override string StatusText
    {
        get
        {
            if (_won) return $"You win — {_taken} moves.";
            if (_lost) return "Computer wins. New game reshuffles.";
            return $"{_taken} moves · {(Winning ? "winning" : "losing")} position · tap a counter; it takes that counter and everything to its right";
        }
    }

    /// <summary>The Nim invariant: a position is winning exactly when every pile XORs to zero.</summary>
    public bool Winning => _piles.Aggregate(0, (a, b) => a ^ b) != 0;
    public IReadOnlyList<int> Piles => _piles;

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
        if (_won || _lost || index < 0 || index >= _cells.Count) return false;

        int row = index / Cols;
        int col = index % Cols;
        if (row < 0 || row >= Rows || col >= _piles[row] || _piles[row] == 0) return false;

        // A tap on column c leaves c counters in the row, so the player takes
        // exactly pile-c counters. The rightmost counter therefore means "take 1"
        // and the leftmost means "take the whole row".
        _piles[row] = col;
        _taken++;
        if (CheckOver())
        {
            Redraw();
            return true;
        }

        ComputerMove();
        Redraw();
        return true;
    }

    /// <summary>Compatibility helper: take the entire requested pile as one move.</summary>
    public void TakeAll(int row)
    {
        if (_won || _lost || row < 0 || row >= Rows || _piles[row] == 0) return;
        _piles[row] = 0;
        _taken++;
        if (!CheckOver()) ComputerMove();
        Redraw();
    }

    private bool CheckOver()
    {
        if (_piles.Any(p => p > 0)) return false;
        _won = true;
        ScoreCounts = true;
        return true;
    }

    private void ComputerMove()
    {
        if (_piles.All(p => p == 0)) return;

        int nim = _piles.Aggregate(0, (a, b) => a ^ b);
        if (nim != 0)
        {
            for (int row = 0; row < Rows; row++)
            {
                int target = _piles[row] ^ nim;
                if (target < _piles[row])
                {
                    _piles[row] = target;
                    break;
                }
            }
        }
        else
        {
            // A zero-nim-sum position is losing for the player to move, so there
            // is no forced winning move. Make a legal move and leave the mistake
            // to the player.
            var rows = Enumerable.Range(0, Rows).Where(r => _piles[r] > 0).ToArray();
            int row = rows[Random.Shared.Next(rows.Length)];
            _piles[row] = Random.Shared.Next(_piles[row]);
        }

        if (_piles.All(p => p == 0))
        {
            _lost = true;
            ScoreCounts = false;
        }
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
            cell.IsEnabled = filled && !_won && !_lost;
            cell.FontSize = 20;
            cell.Bold = false;
        }
    }
}
