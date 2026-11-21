using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Farm - the arcade's idle farmer on a 4x4 plot of land. Seeds cost nothing and
/// grow in real time through Tick, so crops ripen while the app is open; tap a
/// ripe plot to bank the gold. "Day +10s" fast-forwards the clock, which is also
/// what keeps the game testable by hand. Reach 500g to win.
/// </summary>
public sealed class FarmGame : GameBase
{
    private const int PlotCount = 16;
    private const int GoalGold = 500;
    private const int SeedCell = 16;
    private const int DayCell = 17;
    private const int GoldCell = 18;
    private const int EndCell = 19;
    private const int EndMinGold = 50;

    // (name, plot letter, seconds to ripen, sale value)
    private static readonly (string Name, string Letter, int Seconds, int Value)[] Seeds =
    {
        ("Wheat", "W", 15, 10),
        ("Corn", "C", 35, 25),
        ("Pumpkin", "P", 70, 60),
    };

    // Six steps from bare soil to ripe green, precomputed so Redraw allocates nothing.
    private static readonly IBrush[] GrowTints = BuildTints();

    private readonly List<GameCell> _cells = new();
    private readonly int[] _crop = new int[PlotCount];       // 0 empty, else seed index + 1
    private readonly double[] _progress = new double[PlotCount];
    private int _gold;
    private int _seed;
    private bool _over;
    private bool _won;
    private string _msg = "";

    public FarmGame()
    {
        for (int i = 0; i < PlotCount + 4; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "farm";
    public override string Name => "Farm";
    public override int Columns => 4;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool NeedsTicks => !_over;

    public override int Score => _won ? _gold + 100 : _gold;

    public override string? ResultText => _over
        ? _won ? "Goal reached - the farm thrives!" : $"Sold up for {_gold}g."
        : null;

    public override string StatusText
    {
        get
        {
            if (_over) return _won ? $"500g reached · {Score} points." : $"Run banked · {Score} points.";
            string extra = _msg.Length > 0 ? _msg : "tap ripe plots to harvest";
            return $"Gold {_gold}/{GoalGold} · seed: {Seeds[_seed].Name} · {extra}";
        }
    }

    public override void Reset()
    {
        Array.Clear(_crop, 0, _crop.Length);
        Array.Clear(_progress, 0, _progress.Length);
        _gold = 0;
        _seed = 0;
        _over = false;
        _won = false;
        _msg = "";
        ScoreCounts = false;
        Redraw();
    }

    /// <summary>Crops grow on accumulated time; nothing here can loop or hang.</summary>
    public override bool Tick(TimeSpan elapsed)
    {
        if (_over) return false;
        double secs = elapsed.TotalSeconds;
        if (secs <= 0) return false;
        if (Grow(secs))
        {
            Redraw();
            return true;
        }
        return false;
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;

        if (index < PlotCount)
        {
            TapPlot(index);
            return true;
        }

        switch (index)
        {
            case SeedCell:
                _seed = (_seed + 1) % Seeds.Length;
                _msg = "";
                Redraw();
                return true;
            case DayCell:
                Grow(10);
                Redraw();
                return true;
            case EndCell:
                if (_gold >= EndMinGold)
                {
                    _over = true;
                    ScoreCounts = true;
                    Redraw();
                }
                else
                {
                    _msg = $"Need {EndMinGold}g to sell up";
                    Redraw();
                }
                return true;
            default:
                return false;
        }
    }

    private void TapPlot(int i)
    {
        if (_crop[i] == 0)
        {
            _crop[i] = _seed + 1;
            _progress[i] = 0;
            _msg = $"Planted {Seeds[_seed].Name}";
        }
        else if (IsRipe(i))
        {
            _gold += Seeds[_crop[i] - 1].Value;
            _msg = $"+{Seeds[_crop[i] - 1].Value}g";
            _crop[i] = 0;
            _progress[i] = 0;
            if (_gold >= GoalGold)
            {
                _over = true;
                _won = true;
                ScoreCounts = true;
            }
        }
        else
        {
            _msg = "Still growing";
        }
        Redraw();
    }

    private bool IsRipe(int i) =>
        _crop[i] != 0 && _progress[i] >= Seeds[_crop[i] - 1].Seconds;

    private bool Grow(double seconds)
    {
        bool any = false;
        for (int i = 0; i < PlotCount; i++)
        {
            if (_crop[i] == 0 || _progress[i] >= Seeds[_crop[i] - 1].Seconds) continue;
            _progress[i] = Math.Min(Seeds[_crop[i] - 1].Seconds, _progress[i] + seconds);
            any = true;
        }
        return any;
    }

    public override void Redraw()
    {
        for (int i = 0; i < PlotCount; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 18;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
            if (_crop[i] == 0) continue;

            var seed = Seeds[_crop[i] - 1];
            if (IsRipe(i))
            {
                c.Text = "✓";
                c.Background = Good;
                c.Foreground = Cell;
                c.Bold = true;
                continue;
            }
            c.Text = seed.Letter;
            int step = (int)(5 * _progress[i] / seed.Seconds);
            c.Background = GrowTints[Math.Clamp(step, 0, 5)];
        }

        var seedCell = _cells[SeedCell];
        seedCell.Text = $"Seed\n{Seeds[_seed].Name}";
        seedCell.Background = Cell;
        seedCell.Foreground = Ink;
        seedCell.IsEnabled = !_over;
        seedCell.FontSize = 13;
        seedCell.Bold = true;
        seedCell.BorderBrush = Accent;
        seedCell.BorderThickness = new Avalonia.Thickness(1);

        var day = _cells[DayCell];
        day.Text = "Day +10s";
        day.Background = Accent;
        day.Foreground = Cell;
        day.IsEnabled = !_over;
        day.FontSize = 13;
        day.Bold = true;

        var gold = _cells[GoldCell];
        gold.Text = $"Gold\n{_gold}g";
        gold.Background = Board;
        gold.Foreground = Ink;
        gold.IsEnabled = false;
        gold.FontSize = 13;
        gold.Bold = true;

        var end = _cells[EndCell];
        end.Text = "End";
        end.Background = Cell;
        end.Foreground = Ink;
        end.IsEnabled = !_over;
        end.FontSize = 13;
        end.Bold = true;
        end.BorderBrush = Accent;
        end.BorderThickness = new Avalonia.Thickness(1);
    }

    private static IBrush[] BuildTints()
    {
        var from = Color.Parse("#e9e9ee");
        var to = Color.Parse("#1f7a3d");
        var tints = new IBrush[6];
        for (int i = 0; i < 6; i++)
        {
            int r = from.R + (to.R - from.R) * i / 5;
            int g = from.G + (to.G - from.G) * i / 5;
            int b = from.B + (to.B - from.B) * i / 5;
            tints[i] = new SolidColorBrush(Color.FromArgb(255, (byte)r, (byte)g, (byte)b));
        }
        return tints;
    }
}
