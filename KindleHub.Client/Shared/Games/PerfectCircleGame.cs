using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Perfect Circle — tap the 24 points around the centre in clockwise order to
/// trace a circle in one stroke. The roundness score is 1000 × (1 − spread of
/// the 24 radii); one wrong point ends the attempt, one attempt per game.
/// </summary>
public sealed class PerfectCircleGame : GameBase
{
    private const int Size = 13;
    private const int Center = 6;
    private const int PointCount = 24;

    private static readonly Avalonia.Media.IBrush GuideTint =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#d8d8de"));

    private readonly List<GameCell> _cells = new();
    private readonly List<(int idx, double angle, double radius)> _pts = new();
    private readonly int[] _pointAt = new int[Size * Size];
    private readonly bool[] _tapped = new bool[PointCount];
    private double _ringRadius;
    private int _first = -1;
    private int _count;
    private bool _failed;
    private bool _complete;
    private int _scoreValue;

    public PerfectCircleGame()
    {
        for (int i = 0; i < Size * Size; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "perfectcircle";
    public override string Name => "Perfect Circle";
    public override int Columns => Size;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _scoreValue;
    public override string? ResultText => _complete
        ? $"{_scoreValue / 10}% round — {_scoreValue} points."
        : _failed ? "Out of order — attempt over." : null;

    public override string StatusText
    {
        get
        {
            if (_complete) return $"{_scoreValue / 10}% roundness.";
            if (_failed) return "Attempt over — New game to retry.";
            return $"Tapped {_count}/{PointCount} · go clockwise from any point";
        }
    }

    public override void Reset()
    {
        for (int attempt = 0; attempt < 20; attempt++)
            if (TryGenerate()) break;
        if (_pts.Count != PointCount) BuildFallback();

        Array.Clear(_tapped, 0, _tapped.Length);
        _first = -1;
        _count = 0;
        _failed = false;
        _complete = false;
        _scoreValue = 0;
        ScoreCounts = false;
        Redraw();
    }

    private bool TryGenerate()
    {
        _pts.Clear();
        double r0 = 4.6 + Random.Shared.NextDouble() * 1.2;
        double offset = Random.Shared.NextDouble() * 2 * Math.PI;
        var taken = new HashSet<int> { Center * Size + Center };

        for (int k = 0; k < PointCount; k++)
        {
            double theta = offset + k * 2 * Math.PI / PointCount;
            int idx = FindFree(theta, r0, taken);
            if (idx < 0)
            {
                _pts.Clear();
                return false;
            }
            taken.Add(idx);
            _pts.Add((idx, 0.0, 0.0));
        }

        // Re-derive angle and radius from the grid cells, then verify the taps
        // still run clockwise in generation order.
        for (int i = 0; i < _pts.Count; i++)
        {
            var (idx, _, _) = _pts[i];
            int x = idx % Size, y = idx / Size;
            double radius = Math.Sqrt((x - Center) * (x - Center * 1.0) + (y - Center) * (y - Center * 1.0));
            _pts[i] = (idx, Math.Atan2(y - Center, x - Center), radius);
        }
        for (int i = 0; i < PointCount; i++)
        {
            double gap = _pts[(i + 1) % PointCount].angle - _pts[i].angle;
            if (gap <= 0) gap += 2 * Math.PI;
            if (gap < 0.10 || gap > 2 * Math.PI - 0.10)
            {
                _pts.Clear();
                return false;
            }
        }

        _ringRadius = r0;
        return true;
    }

    /// <summary>Finds a free cell near the ideal position, jiggling angle then radius.</summary>
    private static int FindFree(double theta, double r0, HashSet<int> taken)
    {
        for (int j = 0; j <= 8; j++)
        {
            foreach (double dt in (j == 0 ? new[] { 0.0 } : new[] { j * 0.02, -j * 0.02 }))
                foreach (double dr in new[] { 0.0, 0.3, -0.3, 0.6, -0.6 })
                {
                    double rad = r0 + dr;
                    int x = (int)Math.Round(Center + rad * Math.Cos(theta + dt));
                    int y = (int)Math.Round(Center + rad * Math.Sin(theta + dt));
                    if (x < 1 || y < 1 || x > 11 || y > 11) continue;
                    int idx = y * Size + x;
                    if (!taken.Contains(idx)) return idx;
                }
        }
        return -1;
    }

    /// <summary>Deterministic fallback: exact 15° spokes at radius 5.5, all distinct.</summary>
    private void BuildFallback()
    {
        _pts.Clear();
        var taken = new HashSet<int> { Center * Size + Center };
        for (int k = 0; k < PointCount; k++)
        {
            double theta = k * 2 * Math.PI / PointCount;
            for (int j = 0; j <= 4; j++)
            {
                double rad = 5.5 + j * 0.15;
                int x = (int)Math.Round(Center + rad * Math.Cos(theta));
                int y = (int)Math.Round(Center + rad * Math.Sin(theta));
                x = Math.Clamp(x, 1, 11);
                y = Math.Clamp(y, 1, 11);
                int idx = y * Size + x;
                if (taken.Contains(idx)) continue;
                taken.Add(idx);
                double radius = Math.Sqrt((x - Center) * (x - Center * 1.0) + (y - Center) * (y - Center * 1.0));
                _pts.Add((idx, Math.Atan2(y - Center, x - Center), radius));
                break;
            }
        }
        _ringRadius = 5.5;
    }

    public override bool OnTap(int index)
    {
        if (_failed || _complete || index < 0 || index >= _cells.Count) return false;
        int point = _pointAt[index];
        if (point < 0) return false;
        if (_tapped[point]) return true;   // an already-tapped point is a no-op

        if (_count == 0)
        {
            _first = point;
        }
        else
        {
            // The next point must be the next one clockwise from the first tap.
            int expected = (_first + _count) % _pts.Count;
            if (point != expected)
            {
                _failed = true;
                Redraw();
                return true;
            }
        }

        _tapped[point] = true;
        _count++;
        if (_count == _pts.Count) Complete();
        Redraw();
        return true;
    }

    private void Complete()
    {
        _complete = true;
        double mean = _pts.Average(p => p.radius);
        double variance = _pts.Average(p => (p.radius - mean) * (p.radius - mean));
        double deviation = Math.Sqrt(variance);
        // Roundness: 1000 × (1 − coefficient of variation of the 24 radii).
        _scoreValue = (int)Math.Round(1000 * Math.Max(0, 1 - deviation / mean));
        _scoreValue = Math.Clamp(_scoreValue, 50, 1000);
        ScoreCounts = true;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _pointAt.Length; i++) _pointAt[i] = -1;
        for (int i = 0; i < _pts.Count; i++) _pointAt[_pts[i].idx] = i;

        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 18;
            c.Bold = false;
        }

        // A faint guide ring around the ideal circle.
        for (int y = 1; y <= 11; y++)
            for (int x = 1; x <= 11; x++)
            {
                double d = Math.Sqrt((x - Center) * (x - Center * 1.0) + (y - Center) * (y - Center * 1.0));
                if (Math.Abs(d - _ringRadius) <= 1.05)
                    _cells[y * Size + x].Background = GuideTint;
            }

        _cells[Center * Size + Center].Text = "●";
        _cells[Center * Size + Center].FontSize = 22;
        _cells[Center * Size + Center].Bold = true;

        for (int i = 0; i < _pts.Count; i++)
        {
            var c = _cells[_pts[i].idx];
            if (_tapped[i])
            {
                c.Text = "✓";
                c.Background = Good;
                c.Foreground = Cell;
                c.IsEnabled = false;
            }
            else
            {
                c.Text = "●";
                c.Background = Cell;
                c.Foreground = Ink;
                c.IsEnabled = !_failed && !_complete;
                c.Bold = true;
            }
        }

        // After a failed attempt, show which point was next.
        if (_failed && _count < _pts.Count)
        {
            int expected = _count == 0 ? -1 : (_first + _count) % _pts.Count;
            if (expected >= 0)
            {
                var c = _cells[_pts[expected].idx];
                c.Text = "✗";
                c.Background = Bad;
                c.Foreground = Cell;
            }
        }
    }
}
