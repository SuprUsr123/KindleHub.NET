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
    private bool _playerTurn = true;
    /// <summary>Row tapped last, so a second tap on the same row takes the rest.</summary>
    private int _lastRow = -1;

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
            if (!_playerTurn) return "Computer is thinking…";
            return $"{_taken} moves · take any number from one row";
        }
    }

    /// <summary>The Nim invariant: a position is losing exactly when every pile XORs to zero.</summary>
    public bool Winning => _piles.Aggregate(0, (a, b) => a ^ b) != 0;

    /// <summary>Current pile sizes, exposed so the self-test can play a perfect game.</summary>
    public int[] Piles => _piles;

    public override void Reset()
    {
        for (int i = 0; i < Rows; i++) _piles[i] = Random.Shared.Next(1, Cols + 1);
        _taken = 0;
        _won = _lost = false;
        _playerTurn = true;
        _lastRow = -1;
        ScoreCounts = false;
        Redraw();
    }

    /// <summary>Take a pile down to <paramref name="left"/> — the real Nim move.</summary>
    public void TakeTo(int row, int left)
    {
        if (!_playerTurn || _won || _lost || row < 0 || row >= _piles.Length || _piles[row] == 0) return;
        if (left < 0) left = 0;
        if (left >= _piles[row]) return;
        _piles[row] = left;
        _taken++;
        CheckOver();
        if (_won || _lost) return;
        _playerTurn = false;
        BotMove();
    }

    /// <summary>Take one counter from a row. Convenient, but the game is about taking a whole pile.</summary>
    public void TakeOne(int row) => TakeTo(row, _piles[row] - 1);

    public override bool OnTap(int index)
    {
        // Tapping takes one counter from that row. Tapping the same row again
        // takes the rest — the move Nim is actually about — which is how a touch
        // screen reaches it without a separate button.
        int row = index < 0 ? -1 : index / Cols;
        if (_won || _lost || !_playerTurn || row < 0 || row >= _piles.Length) return false;
        if (_piles[row] == 0) return true;
        if (_lastRow == row) { _lastRow = -1; TakeAll(row); Redraw(); return true; }
        _lastRow = row;
        TakeOne(row);
        Redraw();
        return true;
    }

    /// <summary>Take the rest of a pile in one go — the move Nim is actually about.</summary>
    public void TakeAll(int row)
    {
        if (!_playerTurn || _won || _lost || row < 0 || row >= _piles.Length || _piles[row] == 0) return;
        _taken++;
        _piles[row] = 0;
        CheckOver();
        if (_won || _lost) return;
        _playerTurn = false;
        BotMove();
    }

    private void BotMove()
    {
        // XOR strategy: leave a position whose piles XOR to zero. If that is not
        // possible, the position is lost and the bot takes one to stay in the game.
        int xor = _piles.Aggregate(0, (a, b) => a ^ b);
        for (int r = 0; r < Rows; r++)
        {
            int target = _piles[r] ^ xor;
            if (target < _piles[r]) { _piles[r] = target; _taken++; break; }
        }
        // The XOR branch is always available unless xor == 0, in which case no
        // pile can be reduced to make it zero — so take one from any pile.
        if (xor == 0)
        {
            for (int r = 0; r < Rows; r++)
                if (_piles[r] > 0) { _piles[r]--; _taken++; break; }
        }
        CheckOver();
        _playerTurn = true;
        Redraw();
    }

    private void CheckOver()
    {
        if (_piles.Any(p => p > 0)) return;
        _won = _playerTurn;
        _lost = !_playerTurn;
        ScoreCounts = _won;
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
            cell.IsEnabled = filled && _playerTurn && !_won && !_lost;
            cell.FontSize = 20;
            cell.Bold = false;
        }
    }
}
