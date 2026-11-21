using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Pair Up, ported from the official client — turn over two cards at a time and match
/// each word to what it means. Matched cards stay face up; a mismatch flips back after
/// about a second, so fewer turns is better.
/// </summary>
public sealed class PairUpGame : GameBase
{
    private const int Side = 4;          // 4x4 board
    private const int Pairs = 8;
    private static readonly TimeSpan FlipBackFor = TimeSpan.FromMilliseconds(900);

    // Word ↔ short meaning; eight of these are sampled and shuffled every reset.
    private static readonly (string Word, string Meaning)[] Bank =
    {
        ("HUGE", "very big"),
        ("COLD", "not warm"),
        ("BRAVE", "fearless"),
        ("RICH", "wealthy"),
        ("QUICK", "very fast"),
        ("SILENT", "no sound"),
        ("ANCIENT", "very old"),
        ("HAPPY", "joyful"),
        ("TIRED", "sleepy"),
        ("STRANGE", "odd"),
        ("SMART", "clever"),
        ("WEEP", "to cry"),
        ("FICTION", "not true"),
        ("SUMMIT", "very top"),
    };

    private readonly List<GameCell> _cells = new();
    private readonly string[] _face = new string[Side * Side];
    private readonly int[] _pair = new int[Side * Side];
    private readonly bool[] _up = new bool[Side * Side];
    private readonly bool[] _gone = new bool[Side * Side];
    private int _first = -1;
    private int _pendingA = -1, _pendingB = -1;
    private TimeSpan _pendingFor;
    private int _turns;
    private int _matched;

    public PairUpGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "pairup";
    public override string Name => "Pair Up";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool NeedsTicks => true;

    public override int Score => _matched == Pairs ? Math.Max(50, 200 - 5 * (_turns - Pairs)) : 0;
    public override string? ResultText => _matched == Pairs ? $"All {Pairs} pairs in {_turns} turns." : null;

    public override string StatusText => _matched == Pairs
        ? $"Cleared in {_turns} turns — lower is better."
        : $"Turns {_turns} · matched {_matched}/{Pairs} pairs · match words to meanings";

    public override void Reset()
    {
        // Sample eight pairs, then lay one word card and one meaning card per pair.
        var picked = Bank.OrderBy(_ => Random.Shared.Next()).Take(Pairs).ToArray();
        var deck = new (string Face, int Pair)[Pairs * 2];
        int d = 0;
        for (int p = 0; p < Pairs; p++)
        {
            deck[d++] = (picked[p].Word, p);
            deck[d++] = (picked[p].Meaning, p);
        }
        for (int i = deck.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        for (int i = 0; i < deck.Length; i++)
        {
            _face[i] = deck[i].Face;
            _pair[i] = deck[i].Pair;
        }
        Array.Clear(_up, 0, _up.Length);
        Array.Clear(_gone, 0, _gone.Length);
        _first = _pendingA = _pendingB = -1;
        _pendingFor = TimeSpan.Zero;
        _turns = 0;
        _matched = 0;
        ScoreCounts = false;
        Redraw();
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_pendingA < 0) return false;
        // Leave a mismatch face-up long enough to read it, then flip both back.
        _pendingFor -= elapsed;
        if (_pendingFor > TimeSpan.Zero) return false;
        _up[_pendingA] = _up[_pendingB] = false;
        _pendingA = _pendingB = -1;
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_matched == Pairs || index < 0 || index >= _cells.Count) return false;
        if (_pendingA >= 0) return false;   // wait for the mismatch to flip back
        if (_gone[index] || _up[index]) return true;

        _up[index] = true;

        if (_first < 0)
        {
            _first = index;
            Redraw();
            return true;
        }

        _turns++;
        if (_pair[_first] == _pair[index])
        {
            // A word and its meaning both stay revealed in a good tint.
            _gone[_first] = _gone[index] = true;
            _up[_first] = _up[index] = false;
            _first = -1;
            _matched++;
            if (_matched == Pairs) ScoreCounts = true;
        }
        else
        {
            _pendingA = _first;
            _pendingB = index;
            _pendingFor = FlipBackFor;
            _first = -1;
        }
        Redraw();
        return true;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            if (_gone[i])
            {
                c.Text = _face[i];
                c.Background = Good;
                c.Foreground = Cell;
                c.Bold = true;
                c.IsEnabled = false;
                c.FontSize = 10;
            }
            else if (_up[i])
            {
                c.Text = _face[i];
                c.Background = Cell;
                c.Foreground = Ink;
                c.Bold = true;
                c.IsEnabled = false;
                c.FontSize = 10;
            }
            else
            {
                c.Text = "?";
                c.Background = Board;
                c.Foreground = Muted;
                c.Bold = true;
                c.IsEnabled = true;
                c.FontSize = 22;
            }
            c.Span = 1;
        }
    }
}
