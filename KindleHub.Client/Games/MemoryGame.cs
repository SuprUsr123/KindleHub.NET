using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>Memory — flip two cards at a time and clear the board in as few moves as you can.</summary>
public sealed class MemoryGame : GameBase
{
    private const int Pairs = 8;
    private const int Side = 4; // 4x4 = 8 pairs
    private static readonly TimeSpan RevealFor = TimeSpan.FromMilliseconds(700);

    private readonly List<GameCell> _cells = new();
    private readonly string[] _deck = new string[Pairs * 2];
    private readonly bool[] _up = new bool[Pairs * 2];
    private readonly bool[] _gone = new bool[Pairs * 2];
    private int _first = -1;
    private int _pendingA = -1, _pendingB = -1;
    private TimeSpan _pendingFor;
    private TimeSpan _elapsed;
    private int _moves;
    private int _matched;

    public MemoryGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "memory";
    public override string Name => "Memory";
    public override int Columns => Side;
    public override bool NeedsTicks => true;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => Math.Max(0, Pairs * 120 - _moves * 3 - (int)_elapsed.TotalSeconds);
    public int Moves => _moves;
    public override string? ResultText => _matched == Pairs ? $"Board cleared in {_moves} moves." : null;

    public override string StatusText => _matched == Pairs
        ? $"Cleared in {_moves} moves — lower is better."
        : $"Moves {_moves} · matched {_matched}/{Pairs} pairs";

    public override void Reset()
    {
        // Each face has to go in twice, otherwise the "pairs" are eight singletons
        // and the board can never be cleared.
        var deck = new string[Pairs * 2];
        for (int i = 0; i < Pairs; i++)
        {
            string face = (i + 1).ToString("00");
            deck[i * 2] = face;
            deck[i * 2 + 1] = face;
        }
        for (int i = deck.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        Array.Copy(deck, _deck, deck.Length);
        Array.Clear(_up, 0, _up.Length);
        Array.Clear(_gone, 0, _gone.Length);
        _first = _pendingA = _pendingB = -1;
        _pendingFor = TimeSpan.Zero;
        _elapsed = TimeSpan.Zero;
        _moves = 0;
        _matched = 0;
        ScoreCounts = false;
        Redraw();
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_matched < Pairs) _elapsed += elapsed;
        if (_pendingA < 0) return false;
        _pendingFor -= elapsed;
        if (_pendingFor > TimeSpan.Zero) return false;
        _up[_pendingA] = _up[_pendingB] = false;
        _pendingA = _pendingB = -1;
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_pendingA >= 0 || index < 0 || index >= _deck.Length) return false;
        if (_gone[index] || _up[index]) return true;

        _up[index] = true;

        if (_first < 0) { _first = index; Redraw(); return true; }

        _moves++;
        if (_deck[_first] == _deck[index])
        {
            _gone[_first] = _gone[index] = true;
            _up[_first] = _up[index] = false;
            _first = -1;
            _matched++;
            if (_matched == Pairs) ScoreCounts = true;
        }
        else
        {
            // Leave the mismatch face-up until the reveal timer expires, so the
            // player gets to see it — that's the whole feedback loop of the game.
            _pendingA = _first;
            _pendingB = index;
            _pendingFor = RevealFor;
            _first = -1;
        }
        Redraw();
        return true;
    }

    /// <summary>
    /// The face under each card, face-up or not. Exposed so a headless test can
    /// play a perfect game instead of guessing pairs; the view never reads it.
    /// </summary>
    public IReadOnlyList<string> Deck => _deck;

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            if (_gone[i]) { c.Text = ""; c.Background = Board; c.IsEnabled = false; c.Foreground = Ink; c.FontSize = 20; c.Bold = false; }
            else if (_up[i]) { c.Text = _deck[i]; c.Background = Tiles4[2]; c.Bold = true; c.IsEnabled = false; c.FontSize = 26; c.Foreground = Ink; }
            else { c.Text = "?"; c.Background = Cell; c.IsEnabled = true; c.FontSize = 26; c.Bold = true; c.Foreground = Muted; }
        }
    }
}
