using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Draw dominoes against the computer, ported from the official client. A double-
/// six set is dealt seven and seven with fourteen in the boneyard; a tile only
/// fits an open end of the chain when one of its pips matches. Empty your hand to
/// win the round — three rounds make a game.
/// </summary>
public sealed class DominoesGame : GameBase
{
    private const int Cols = 12;
    private const int Rows = 5;
    private const int HandSlots = 24;
    private const int TotalRounds = 3;

    private const int LeftBtn = 12, RightBtn = 13, DrawBtn = 14, PassBtn = 15, NextBtn = 16;
    private const int HandBase = 24;
    private const int InfoRow = 48;

    private readonly record struct Tile(int A, int B);

    private readonly List<GameCell> _cells = new();
    private readonly List<(int A, int B)> _chain = new(); // oriented left → right
    private readonly List<Tile> _hand = new();
    private readonly List<Tile> _cpu = new();
    private readonly List<Tile> _bone = new();
    private int _sel = -1;
    private int _passStreak;
    private int _round = 1;
    private int _matches;
    private int _roundsWon;
    private bool _roundOver;
    private bool _over;
    private string _msg = "Play any tile to start.";

    public DominoesGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "dominoes";
    public override string Name => "Dominoes";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _over ? Math.Max(1, 50 * _matches + 10 * _roundsWon) : 0;

    public override string? ResultText => !_over ? null
        : $"3 rounds done — {_matches} tiles played, {_roundsWon} rounds won.";

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            if (_roundOver) return $"{_msg} · tap Next for round {_round + 1}.";
            return _msg;
        }
    }

    public override void Reset()
    {
        _matches = 0;
        _roundsWon = 0;
        _round = 1;
        _over = false;
        ScoreCounts = false;
        NewRound();
    }

    private void NewRound()
    {
        _chain.Clear();
        _hand.Clear();
        _cpu.Clear();
        _bone.Clear();
        for (int a = 0; a <= 6; a++)
            for (int b = a; b <= 6; b++)
                _bone.Add(new Tile(a, b));
        for (int i = _bone.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_bone[i], _bone[j]) = (_bone[j], _bone[i]);
        }
        for (int i = 0; i < 7; i++)
        {
            _hand.Add(_bone[^1]);
            _bone.RemoveAt(_bone.Count - 1);
        }
        for (int i = 0; i < 7; i++)
        {
            _cpu.Add(_bone[^1]);
            _bone.RemoveAt(_bone.Count - 1);
        }
        _sel = -1;
        _passStreak = 0;
        _roundOver = false;
        _msg = "Play any tile to start.";
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count) return false;
        if (_over) return false;

        if (_roundOver)
        {
            if (index != NextBtn) return false;
            _round++;
            if (_round > TotalRounds)
            {
                _over = true;
                ScoreCounts = true;
            }
            else
            {
                NewRound();
                _msg = $"Round {_round} — your lead.";
            }
            Redraw();
            return true;
        }

        if (index >= HandBase && index < HandBase + HandSlots)
        {
            int k = index - HandBase;
            if (k >= _hand.Count) return false;
            _sel = k;
            _msg = Fits(_hand[k], out _, out _) || _chain.Count == 0
                ? "Tap Left or Right to play."
                : "That tile fits neither end.";
            Redraw();
            return true;
        }

        if (index is LeftBtn or RightBtn)
        {
            if (_sel < 0 || _sel >= _hand.Count)
            {
                _msg = "Tap one of your tiles first.";
                Redraw();
                return true;
            }
            var tile = _hand[_sel];
            bool left = index == LeftBtn;
            if (_chain.Count > 0)
            {
                bool fitsLeft = tile.A == _chain[0].A || tile.B == _chain[0].A;
                bool fitsRight = tile.A == _chain[^1].B || tile.B == _chain[^1].B;
                if (left ? !fitsLeft : !fitsRight)
                {
                    _msg = left ? "No match on the left end." : "No match on the right end.";
                    Redraw();
                    return true;
                }
            }
            if (left) PlayLeft(tile);
            else PlayRight(tile);
            _hand.RemoveAt(_sel);
            _sel = -1;
            _matches++;
            _passStreak = 0;
            if (_hand.Count == 0)
            {
                RoundEnd(true);
                Redraw();
                return true;
            }
            CpuTurn();
            Redraw();
            return true;
        }

        if (index == DrawBtn)
        {
            if (HasPlayable())
            {
                _msg = "You already have a playable tile.";
                Redraw();
                return true;
            }
            if (_bone.Count == 0)
            {
                _msg = "The boneyard is empty — pass.";
                Redraw();
                return true;
            }
            var tile = _bone[^1];
            _bone.RemoveAt(_bone.Count - 1);
            _hand.Add(tile);
            _msg = $"Drew {TileText(tile)}.";
            Redraw();
            return true;
        }

        if (index == PassBtn)
        {
            if (HasPlayable())
            {
                _msg = "You have a playable tile.";
                Redraw();
                return true;
            }
            if (_bone.Count > 0)
            {
                _msg = "Draw first — the boneyard isn't empty.";
                Redraw();
                return true;
            }
            _passStreak++;
            _msg = "You passed.";
            CpuTurn();
            Redraw();
            return true;
        }

        return false;
    }

    private bool HasPlayable() => _chain.Count == 0
        ? _hand.Count > 0
        : _hand.Any(t => Fits(t, out _, out _));

    private bool Fits(Tile t, out bool left, out bool right)
    {
        left = right = false;
        if (_chain.Count == 0)
        {
            left = right = true;
            return true;
        }
        left = t.A == _chain[0].A || t.B == _chain[0].A;
        right = t.A == _chain[^1].B || t.B == _chain[^1].B;
        return left || right;
    }

    private void PlayLeft(Tile t)
    {
        if (_chain.Count == 0)
        {
            _chain.Add((t.A, t.B));
            return;
        }
        int end = _chain[0].A;
        _chain.Insert(0, t.A == end ? (t.B, t.A) : (t.A, t.B));
    }

    private void PlayRight(Tile t)
    {
        if (_chain.Count == 0)
        {
            _chain.Add((t.A, t.B));
            return;
        }
        int end = _chain[^1].B;
        _chain.Add(t.A == end ? (t.A, t.B) : (t.B, t.A));
    }

    /// <summary>Greedy computer: the fattest tile that fits, else draw until one does.</summary>
    private void CpuTurn()
    {
        if (_roundOver || _over) return;
        while (true)
        {
            int pick = -1, bestPips = -1;
            for (int i = 0; i < _cpu.Count; i++)
            {
                if (!Fits(_cpu[i], out _, out _)) continue;
                int pips = _cpu[i].A + _cpu[i].B;
                if (pips > bestPips)
                {
                    bestPips = pips;
                    pick = i;
                }
            }
            if (pick >= 0)
            {
                var tile = _cpu[pick];
                _cpu.RemoveAt(pick);
                Fits(tile, out bool left, out bool right);
                if (left && right)
                {
                    if (Random.Shared.Next(2) == 0) PlayLeft(tile);
                    else PlayRight(tile);
                }
                else if (left) PlayLeft(tile);
                else PlayRight(tile);
                _passStreak = 0;
                _msg = $"CPU played {TileText(tile)}.";
                if (_cpu.Count == 0) RoundEnd(false);
                return;
            }

            if (_bone.Count == 0)
            {
                _passStreak++;
                _msg = "CPU passed.";
                if (_passStreak >= 2) BlockedEnd();
                return;
            }
            var drawn = _bone[^1];
            _bone.RemoveAt(_bone.Count - 1);
            _cpu.Add(drawn);
        }
    }

    private void RoundEnd(bool playerWon)
    {
        _roundOver = true;
        if (playerWon)
        {
            _roundsWon++;
            _msg = "You emptied your hand — round won!";
        }
        else _msg = "The computer emptied its hand.";
    }

    private void BlockedEnd()
    {
        _roundOver = true;
        int mine = _hand.Sum(t => t.A + t.B);
        int theirs = _cpu.Sum(t => t.A + t.B);
        if (mine < theirs)
        {
            _roundsWon++;
            _msg = $"Blocked — you {mine} pips vs {theirs}: round won!";
        }
        else if (theirs < mine) _msg = $"Blocked — computer {theirs} pips vs {mine}: round lost.";
        else _msg = "Blocked — equal pips, no round winner.";
    }

    private static string TileText(Tile t) => $"{t.A}|{t.B}";

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 14;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        // The chain, centred and capped; the two open ends carry a green border.
        int n = _chain.Count;
        if (n > 0)
        {
            if (n <= Cols)
            {
                int start = (Cols - n) / 2;
                for (int i = 0; i < n; i++) DrawChainCell(start + i, i, n);
            }
            else
            {
                for (int i = 0; i < 6; i++) DrawChainCell(i, i, n);
                var gap = _cells[6];
                gap.Text = "…";
                gap.Foreground = Muted;
                gap.FontSize = 16;
                for (int i = 0; i < 5; i++) DrawChainCell(7 + i, n - 5 + i, n);
            }
        }

        bool playable = HasPlayable();
        bool selValid = !_roundOver && !_over && _sel >= 0 && _sel < _hand.Count;
        bool leftOk = false, rightOk = false;
        if (selValid)
        {
            if (_chain.Count == 0) leftOk = rightOk = true;
            else Fits(_hand[_sel], out leftOk, out rightOk);
        }
        DrawButton(LeftBtn, "← Left", selValid && leftOk);
        DrawButton(RightBtn, "Right →", selValid && rightOk);
        DrawButton(DrawBtn, "Draw", !_roundOver && !_over && !playable && _bone.Count > 0);
        DrawButton(PassBtn, "Pass", !_roundOver && !_over && !playable && _bone.Count == 0);
        DrawButton(NextBtn, "Next →", _roundOver && !_over);

        // Your tiles: green border when they fit an end, accent when selected.
        for (int k = 0; k < HandSlots; k++)
        {
            var c = _cells[HandBase + k];
            if (k >= _hand.Count) continue;
            var t = _hand[k];
            c.Text = TileText(t);
            c.FontSize = 16;
            c.Background = Cell;
            c.IsEnabled = !_roundOver && !_over;
            if (k == _sel)
            {
                c.BorderBrush = Accent;
                c.BorderThickness = new Avalonia.Thickness(2);
            }
            else if (_chain.Count == 0 || Fits(t, out _, out _))
            {
                c.BorderBrush = Good;
                c.BorderThickness = new Avalonia.Thickness(1);
            }
        }

        string ends = _chain.Count == 0
            ? "no chain yet"
            : $"open ends {_chain[0].A} and {_chain[^1].B}";
        Spread(InfoRow,
            $"You {_hand.Count} · CPU {_cpu.Count} · bone {_bone.Count} · {ends} · round {_round}/{TotalRounds}",
            Cols, 10);
    }

    private void DrawChainCell(int cellIndex, int chainIndex, int total)
    {
        var c = _cells[cellIndex];
        var (a, b) = _chain[chainIndex];
        c.Text = $"{a}|{b}";
        c.FontSize = 15;
        c.Background = Cell;
        bool isEnd = chainIndex == 0 || chainIndex == total - 1;
        c.Bold = isEnd;
        if (isEnd)
        {
            c.BorderBrush = Good;
            c.BorderThickness = new Avalonia.Thickness(1);
        }
    }

    private void DrawButton(int index, string text, bool enabled)
    {
        var c = _cells[index];
        c.Text = text;
        c.FontSize = 11;
        c.Bold = true;
        c.Background = enabled ? Cell : Board;
        c.IsEnabled = enabled;
    }

    /// <summary>Splits text evenly across adjacent cells so one row carries a sentence.</summary>
    private void Spread(int baseIndex, string text, int parts, int fontSize)
    {
        text = text.Trim();
        if (text.Length > parts * 7) text = Ellipsis(text, parts * 7);
        int chunk = (text.Length + parts - 1) / parts;
        for (int i = 0; i < parts; i++)
        {
            int at = i * chunk;
            _cells[baseIndex + i].Text = at < text.Length ? text[at..Math.Min(at + chunk, text.Length)] : "";
            _cells[baseIndex + i].FontSize = fontSize;
        }
    }
}
