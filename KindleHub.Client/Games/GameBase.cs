using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Shared drawing helpers and the palette, so twenty games don't each invent their
/// own greys. Colours are defined once here; the Games page on the website uses the
/// same muted/ink contrast on e-ink, so these stay dark-on-light and low-chroma.
/// </summary>
public abstract class GameBase : IGame
{
    protected static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1b1b1f"));
    protected static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6b6b76"));
    protected static readonly IBrush Board = new SolidColorBrush(Color.Parse("#e9e9ee"));
    protected static readonly IBrush Cell = new SolidColorBrush(Color.Parse("#fbfbfd"));
    protected static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2f6fed"));
    protected static readonly IBrush Good = new SolidColorBrush(Color.Parse("#1f7a3d"));
    protected static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#b3261e"));
    protected static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#9a6700"));

    // 2048 / Snake / Simon tint sets — muted enough to stay readable on e-ink.
    protected static readonly IBrush[] Tiles4 =
    {
        new SolidColorBrush(Color.Parse("#eee4da")),
        new SolidColorBrush(Color.Parse("#ede0c8")),
        new SolidColorBrush(Color.Parse("#f2b179")),
        new SolidColorBrush(Color.Parse("#f59563")),
        new SolidColorBrush(Color.Parse("#f67c5f")),
        new SolidColorBrush(Color.Parse("#edc22e")),
    };

    public abstract string Slug { get; }
    public abstract string Name { get; }
    public abstract int Columns { get; }
    public abstract IReadOnlyList<GameCell> Cells { get; }
    public abstract string StatusText { get; }
    public virtual string? ResultText => null;
    public virtual bool ScoreCounts { get; protected set; }
    public virtual int Score => 0;
    public virtual bool IsRealTime => false;
    public virtual bool NeedsTicks => IsRealTime;

    public abstract void Reset();
    public virtual bool Tick(TimeSpan elapsed) => false;
    public abstract void Redraw();
    public virtual bool OnKey(GameKey key) => false;
    public virtual bool OnLetter(char letter) => false;
    public virtual bool OnBackspace() => false;
    public abstract bool OnTap(int index);
    public virtual bool OnSecondaryTap(int index) => false;

    // ─── construction helpers ────────────────────────────────────────────────
    protected static GameCell Blank(IBrush? bg = null) =>
        new() { Text = "", Background = bg ?? Board, Foreground = Ink, IsEnabled = false };

    protected static string Ellipsis(string s, int max) =>
        s.Length <= max ? s : s[..Math.Max(0, max - 1)] + "…";
}
