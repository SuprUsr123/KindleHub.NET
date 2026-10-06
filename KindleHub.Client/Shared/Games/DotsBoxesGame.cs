using System;
using System.Collections.Generic;
using System.Text.Json;

namespace KindleHub.Client.Games;

/// <summary>Dots & Boxes board. Uses a 9x9 display matrix: alternating cells are
/// dots, selectable edges, and claimed boxes. Edges can be selected by tapping
/// their cells; a completed box gives the same player another move.</summary>
public sealed class DotsBoxesGame : GameBase
{
    private const int Dots = 5;
    private readonly GameCell[] _cells = new GameCell[81];
    private readonly bool[,] _horizontal = new bool[Dots, Dots - 1];
    private readonly bool[,] _vertical = new bool[Dots - 1, Dots];
    private readonly int[,] _horizontalOwner = new int[Dots, Dots - 1];
    private readonly int[,] _verticalOwner = new int[Dots - 1, Dots];
    private readonly int[,] _boxes = new int[Dots - 1, Dots - 1];
    private int _turn = 1;
    private int _p1;
    private int _p2;
    private int _moves;
    private bool _finished;

    public override string Slug => "dotsboxes";
    public override string Name => "Dots & Boxes";
    public override int Columns => Dots * 2 - 1;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public bool IsPlayerOneTurn => _turn == 1;
    public int CurrentPlayer => _turn;
    public int PlayerOneScore => _p1;
    public int PlayerTwoScore => _p2;
    public bool IsFinished => _finished;
    public override string StatusText => _finished
        ? $"Final score — Player 1 {_p1}, Player 2 {_p2}."
        : $"Player 1 {_p1} · Player 2 {_p2} · Player {_turn}'s turn · tap an edge";
    public override string? ResultText => _finished
        ? _p1 > _p2 ? "Player 1 wins!" : _p1 < _p2 ? "Player 2 wins." : "Draw."
        : null;
    public DotsBoxesGame()
    {
        for (int i = 0; i < _cells.Length; i++) _cells[i] = Blank();
        Reset();
    }

    public override void Reset()
    {
        Array.Clear(_horizontal, 0, _horizontal.Length);
        Array.Clear(_vertical, 0, _vertical.Length);
        Array.Clear(_horizontalOwner, 0, _horizontalOwner.Length);
        Array.Clear(_verticalOwner, 0, _verticalOwner.Length);
        Array.Clear(_boxes, 0, _boxes.Length);
        _turn = 1; _p1 = _p2 = _moves = 0; _finished = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        return TryGetEdge(index, out char kind, out int row, out int col) && TryPlayEdge(kind, row, col);
    }

    public bool TryGetEdge(int index, out char kind, out int row, out int col)
    {
        kind = '\0'; row = col = -1;
        if (_finished || index < 0 || index >= _cells.Length) return false;
        int cellRow = index / Columns, cellCol = index % Columns;
        if ((cellRow & 1) == 0 && (cellCol & 1) == 1)
        { kind = 'h'; row = cellRow / 2; col = cellCol / 2; return !_horizontal[row, col]; }
        if ((cellRow & 1) == 1 && (cellCol & 1) == 0)
        { kind = 'v'; row = cellRow / 2; col = cellCol / 2; return !_vertical[row, col]; }
        return false;
    }

    public bool TryPlayEdge(char kind, int row, int col)
    {
        if (_finished) return false;
        if (kind == 'h' && row >= 0 && row < Dots && col >= 0 && col < Dots - 1)
            return PlayHorizontal(row, col);
        if (kind == 'v' && row >= 0 && row < Dots - 1 && col >= 0 && col < Dots)
            return PlayVertical(row, col);
        return false;
    }

    public object CreateRelayState()
    {
        bool[][] h = new bool[Dots][];
        bool[][] v = new bool[Dots - 1][];
        int[][] boxes = new int[Dots - 1][];
        for (int r = 0; r < h.Length; r++)
        { h[r] = new bool[Dots - 1]; for (int c = 0; c < h[r].Length; c++) h[r][c] = _horizontal[r, c]; }
        for (int r = 0; r < v.Length; r++)
        {
            v[r] = new bool[Dots]; boxes[r] = new int[Dots - 1];
            for (int c = 0; c < v[r].Length; c++) v[r][c] = _vertical[r, c];
            for (int c = 0; c < boxes[r].Length; c++) boxes[r][c] = _boxes[r, c];
        }
        return new { grid = Dots, hLines = h, vLines = v, boxes, turn = _turn, scores = new { one = _p1, two = _p2 } };
    }

    public bool LoadRelayState(JsonElement state)
    {
        if (!ReadBoolMatrix(state, "hLines", _horizontal) || !ReadBoolMatrix(state, "vLines", _vertical)
            || !ReadIntMatrix(state, "boxes", _boxes)) return false;
        int turn = ReadInt(state, "turn", 1);
        if (turn is not (1 or 2)) return false;
        _turn = turn;
        _p1 = _p2 = 0;
        for (int r = 0; r < Dots - 1; r++)
            for (int c = 0; c < Dots - 1; c++)
                if (_boxes[r, c] == 1) _p1++; else if (_boxes[r, c] == 2) _p2++; else if (_boxes[r, c] != 0) return false;
        if (state.TryGetProperty("scores", out var scores) && scores.ValueKind == JsonValueKind.Object)
        {
            _p1 = ReadInt(scores, "1", ReadInt(scores, "one", _p1));
            _p2 = ReadInt(scores, "2", ReadInt(scores, "two", _p2));
        }
        _moves = 0;
        foreach (bool edge in _horizontal) if (edge) _moves++;
        foreach (bool edge in _vertical) if (edge) _moves++;
        _finished = _p1 + _p2 == (Dots - 1) * (Dots - 1);
        Array.Clear(_horizontalOwner, 0, _horizontalOwner.Length);
        Array.Clear(_verticalOwner, 0, _verticalOwner.Length);
        Redraw();
        return true;
    }

    private static int ReadInt(JsonElement parent, string name, int fallback)
    {
        if (!parent.TryGetProperty(name, out var value)) return fallback;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) ? n : fallback;
    }

    private static bool ReadBoolMatrix(JsonElement state, string name, bool[,] destination)
    {
        if (!state.TryGetProperty(name, out var matrix) || matrix.ValueKind != JsonValueKind.Array || matrix.GetArrayLength() != destination.GetLength(0)) return false;
        int r = 0;
        foreach (var row in matrix.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != destination.GetLength(1)) return false;
            int c = 0;
            foreach (var cell in row.EnumerateArray())
            {
                if (cell.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                destination[r, c++] = cell.GetBoolean();
            }
            r++;
        }
        return true;
    }

    private static bool ReadIntMatrix(JsonElement state, string name, int[,] destination)
    {
        if (!state.TryGetProperty(name, out var matrix) || matrix.ValueKind != JsonValueKind.Array || matrix.GetArrayLength() != destination.GetLength(0)) return false;
        int r = 0;
        foreach (var row in matrix.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != destination.GetLength(1)) return false;
            int c = 0;
            foreach (var cell in row.EnumerateArray())
            {
                if (cell.ValueKind != JsonValueKind.Number || !cell.TryGetInt32(out int value)) return false;
                destination[r, c++] = value;
            }
            r++;
        }
        return true;
    }

    /// <summary>Deterministic local opponent for protocol and turn testing. It
    /// takes a box when possible; otherwise it picks a move that does not leave an
    /// immediate box, falling back to the first open edge near the endgame.</summary>
    public bool DebugOpponentMove()
    {
        if (!TryGetDebugMove(out char kind, out int row, out int col)) return false;
        return ApplyMove(kind, row, col);
    }

    public bool TryGetDebugMove(out char kind, out int row, out int col)
    {
        kind = '\0'; row = col = -1;
        if (_finished || _turn != 2) return false;
        var moves = GetOpenMoves();
        if (moves.Count == 0) return false;
        var choice = moves[0];
        foreach (var move in moves)
            if (WouldClaimBox(move.Kind, move.Row, move.Col)) { choice = move; goto selected; }
        foreach (var move in moves)
            if (!WouldGiveBox(move.Kind, move.Row, move.Col)) { choice = move; goto selected; }
        choice = moves[(moves.Count - 1) / 2];
    selected:
        kind = choice.Kind; row = choice.Row; col = choice.Col;
        return true;
    }

    private List<(char Kind, int Row, int Col)> GetOpenMoves()
    {
        var moves = new List<(char, int, int)>();
        for (int r = 0; r < Dots; r++)
            for (int c = 0; c < Dots - 1; c++)
                if (!_horizontal[r, c]) moves.Add(('h', r, c));
        for (int r = 0; r < Dots - 1; r++)
            for (int c = 0; c < Dots; c++)
                if (!_vertical[r, c]) moves.Add(('v', r, c));
        return moves;
    }

    private bool WouldClaimBox(char kind, int row, int col)
    {
        var boxes = AdjacentBoxes(kind, row, col);
        foreach (var (r, c) in boxes)
            if (IsOpenBox(r, c) && CountSides(r, c) == 3) return true;
        return false;
    }

    private bool WouldGiveBox(char kind, int row, int col)
    {
        var boxes = AdjacentBoxes(kind, row, col);
        foreach (var (r, c) in boxes)
            if (IsOpenBox(r, c) && CountSides(r, c) == 2) return true;
        return false;
    }

    private static List<(int Row, int Col)> AdjacentBoxes(char kind, int row, int col) => kind == 'h'
        ? new() { (row - 1, col), (row, col) }
        : new() { (row, col - 1), (row, col) };

    private bool IsOpenBox(int r, int c) => r >= 0 && r < Dots - 1 && c >= 0 && c < Dots - 1 && _boxes[r, c] == 0;

    private int CountSides(int r, int c) =>
        (_horizontal[r, c] ? 1 : 0) + (_horizontal[r + 1, c] ? 1 : 0)
        + (_vertical[r, c] ? 1 : 0) + (_vertical[r, c + 1] ? 1 : 0);

    private bool ApplyMove(char kind, int row, int col)
    {
        if (kind == 'h') return PlayHorizontal(row, col);
        return PlayVertical(row, col);
    }

    private bool PlayHorizontal(int row, int col)
    {
        if (_horizontal[row, col]) return false;
        _horizontal[row, col] = true;
        _horizontalOwner[row, col] = _turn;
        CompleteBoxes(row, col, horizontal: true);
        return true;
    }

    private bool PlayVertical(int row, int col)
    {
        if (_vertical[row, col]) return false;
        _vertical[row, col] = true;
        _verticalOwner[row, col] = _turn;
        CompleteBoxes(row, col, horizontal: false);
        return true;
    }

    private void CompleteBoxes(int row, int col, bool horizontal)
    {
        int claimed = 0;
        if (horizontal) { claimed += ClaimIfClosed(row - 1, col); claimed += ClaimIfClosed(row, col); }
        else { claimed += ClaimIfClosed(row, col - 1); claimed += ClaimIfClosed(row, col); }
        if (_turn == 1) _p1 += claimed; else _p2 += claimed;
        _moves++;
        if (_moves == 40) _finished = true;
        else if (claimed == 0) _turn = 3 - _turn;
        Redraw();
    }

    private int ClaimIfClosed(int r, int c)
    {
        if (r < 0 || r >= Dots - 1 || c < 0 || c >= Dots - 1 || _boxes[r, c] != 0) return 0;
        if (_horizontal[r, c] && _horizontal[r + 1, c] && _vertical[r, c] && _vertical[r, c + 1])
        { _boxes[r, c] = _turn; return 1; }
        return 0;
    }

    public override void Redraw()
    {
        int width = Columns;
        for (int r = 0; r < width; r++)
            for (int c = 0; c < width; c++)
            {
                var cell = _cells[r * width + c];
                if ((r & 1) == 0 && (c & 1) == 0)
                {
                    cell.Text = "•"; cell.Background = Board; cell.Foreground = Ink; cell.FontSize = 22; cell.IsEnabled = false;
                }
                else if ((r & 1) == 0)
                {
                    bool drawn = _horizontal[r / 2, c / 2];
                    cell.Text = drawn ? "━━" : "━"; cell.Background = drawn ? (_horizontalOwner[r / 2, c / 2] == 1 ? Accent : Bad) : Board;
                    cell.Foreground = drawn ? Cell : Muted; cell.IsEnabled = !_finished && !drawn; cell.FontSize = 16;
                }
                else if ((c & 1) == 0)
                {
                    bool drawn = _vertical[r / 2, c / 2];
                    cell.Text = drawn ? "┃" : "│"; cell.Background = drawn ? (_verticalOwner[r / 2, c / 2] == 1 ? Accent : Bad) : Board;
                    cell.Foreground = drawn ? Cell : Muted; cell.IsEnabled = !_finished && !drawn; cell.FontSize = 16;
                }
                else
                {
                    int owner = _boxes[r / 2, c / 2];
                    cell.Text = owner == 0 ? "" : owner == 1 ? "1" : "2";
                    cell.Background = owner == 1 ? Accent : owner == 2 ? Bad : Board;
                    cell.Foreground = owner == 0 ? Muted : Cell; cell.IsEnabled = false; cell.FontSize = 20; cell.Bold = true;
                }
            }
    }
}
