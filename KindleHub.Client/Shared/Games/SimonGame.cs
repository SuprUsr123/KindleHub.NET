using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Simon — watch the pads flash, then tap them back in order.
///
/// The flash is a small explicit phase machine advanced by Tick rather than a
/// background thread, so it stalls with the window instead of racing it, and the
/// whole game stays testable by stepping time by hand.
/// </summary>
public sealed class SimonGame : GameBase
{
    private const int Pads = 4;
    public const int TotalColumns = 2;

    private static readonly IBrush[] PadColours =
    {
        new SolidColorBrush(Color.Parse("#b3261e")),
        new SolidColorBrush(Color.Parse("#2f6fed")),
        new SolidColorBrush(Color.Parse("#1f7a3d")),
        new SolidColorBrush(Color.Parse("#9a6700")),
    };
    private static readonly IBrush PadOff = new SolidColorBrush(Color.Parse("#d9d9e0"));

    private static readonly TimeSpan PadOnFor = TimeSpan.FromMilliseconds(360);
    private static readonly TimeSpan PadOffFor = TimeSpan.FromMilliseconds(170);
    private static readonly TimeSpan LeadIn = TimeSpan.FromMilliseconds(450);

    private enum Phase { LeadIn, PadOn, PadOff, Input, Dead }

    private readonly List<GameCell> _cells = new();
    private readonly List<int> _sequence = new();
    private Phase _phase = Phase.LeadIn;
    private TimeSpan _phaseFor;
    private int _showing;      // index into _sequence
    private int _litPad = -1;  // which pad is currently bright, -1 for none
    private int _inputPos;
    private int _round;
    private bool _dead;

    public SimonGame()
    {
        for (int i = 0; i < Pads; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "simon";
    public override string Name => "Simon";
    public override int Columns => TotalColumns;
    public override bool NeedsTicks => true;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => Math.Max(0, _round - 1);

    public override string? ResultText => _dead ? $"You reached round {_round}." : null;

    public override string StatusText => _dead
        ? $"Outlived at round {_round} — New game starts over."
        : _phase == Phase.Input
            ? $"Round {_round} · repeat all {_sequence.Count}"
            : $"Round {_round} · watch";

    public int Round => _round;

    public override void Reset()
    {
        _sequence.Clear();
        _round = 0;
        _dead = false;
        _inputPos = 0;
        _litPad = -1;
        ScoreCounts = false;
        _sequence.Add(Random.Shared.Next(Pads));
        StartRound();
        Redraw();
    }

    private void StartRound()
    {
        _round++;
        _showing = -1;         // next pad to flash
        _inputPos = 0;
        _litPad = -1;
        _phase = Phase.LeadIn;
        _phaseFor = LeadIn;
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_phase == Phase.Input || _phase == Phase.Dead) return false;
        _phaseFor -= elapsed;
        if (_phaseFor > TimeSpan.Zero) return false;

        switch (_phase)
        {
            case Phase.LeadIn:
            case Phase.PadOff:
                _showing++;
                if (_showing >= _sequence.Count)
                {
                    // Shown the whole round — hand control to the player.
                    _litPad = -1;
                    _phase = Phase.Input;
                    _inputPos = 0;
                }
                else
                {
                    _litPad = _sequence[_showing];
                    _phase = Phase.PadOn;
                    _phaseFor = PadOnFor;
                }
                break;

            case Phase.PadOn:
                _litPad = -1;
                _phase = Phase.PadOff;
                _phaseFor = PadOffFor;
                break;
        }
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_phase != Phase.Input || _dead || index < 0 || index >= Pads) return false;
        if (_sequence[_inputPos] != index)
        {
            _dead = true;
            _phase = Phase.Dead;
            ScoreCounts = _round > 1;
            Redraw();
            return true;
        }

        _inputPos++;
        if (_inputPos < _sequence.Count) { Redraw(); return true; }

        // Round cleared — grow the sequence and flash the next round.
        ScoreCounts = true;
        _sequence.Add(Random.Shared.Next(Pads));
        StartRound();
        Redraw();
        return true;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            bool lit = i == _litPad;
            c.Text = lit ? "●" : "";
            c.Background = lit ? PadColours[i] : PadOff;
            c.Foreground = lit ? Cell : PadColours[i];
            c.IsEnabled = _phase == Phase.Input;
            c.FontSize = 30;
            c.Bold = true;
        }
    }
}
