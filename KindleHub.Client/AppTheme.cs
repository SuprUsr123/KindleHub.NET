using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace KindleHub.Client;

/// <summary>
/// Applies the app's three colour themes. Light and Dark are plain Fluent theme
/// variants; Sepia is a Light variant with the palette keys the views actually use
/// overridden to warm paper tones, which keeps it legible on e-ink without
/// hand-rolling a second theme.
///
/// Only a handful of resource keys are themed — the views use four — so this
/// stays small. Add a key here if a new view starts hard-coding a colour.
/// </summary>
public static class AppTheme
{
    public const string Light = "Light";
    public const string Dark = "Dark";
    public const string Sepia = "Sepia";

    public static readonly string[] Options = { Light, Dark, Sepia };

    /// <summary>Normalises whatever the profile or UI handed us to a known name.</summary>
    public static string Normalise(string? name) =>
        (name ?? "").Trim() switch
        {
            "dark" or "Dark" => Dark,
            "sepia" or "Sepia" => Sepia,
            _ => Light,
        };

    // Sepia palette. Kept low-chroma and high-contrast, the way Fluent's own
    // Light variant is, so nothing ends up washed out on an e-ink panel.
    private static readonly IBrush SepiaWindow = new SolidColorBrush(Color.Parse("#F6EFE0"));
    private static readonly IBrush SepiaSurface = new SolidColorBrush(Color.Parse("#F0E7D3"));
    private static readonly IBrush SepiaSurfaceAlt = new SolidColorBrush(Color.Parse("#E9DDC4"));
    private static readonly IBrush SepiaBorder = new SolidColorBrush(Color.Parse("#CDBA93"));
    private static readonly IBrush SepiaAccent = new SolidColorBrush(Color.Parse("#9A5B25"));

    // Only present while Sepia is active; removed again on the way out.
    private static readonly Dictionary<string, IBrush> SepiaOverrides = new()
    {
        ["ControlFillColorDefault"] = SepiaSurface,
        ["ControlFillColorSecondary"] = SepiaSurfaceAlt,
        ["ControlBorderBrush"] = SepiaBorder,
        ["AccentFillColorDefaultBrush"] = SepiaAccent,
    };

    /// <summary>Window background resource, referenced by MainWindow so all three themes agree.</summary>
    public const string WindowBackgroundKey = "AppWindowBackground";

    /// <summary>
    /// Sets the theme. Safe to call before the window exists — the variant and
    /// resource overrides live on the Application, not the window.
    /// </summary>
    public static void Apply(string? name)
    {
        var app = Application.Current;
        if (app is null) return;

        string theme = Normalise(name);
        bool sepia = theme == Sepia;

        // Sepia is a light scheme; the warmth comes from the palette below.
        app.RequestedThemeVariant = theme == Dark ? ThemeVariant.Dark : ThemeVariant.Light;

        if (sepia) SetSepia(app, true);
        else SetSepia(app, false);

        // Keep the window background in step so the app isn't a dark sheet on
        // Sepia, or a light sheet on Dark.
        app.Resources[WindowBackgroundKey] = theme switch
        {
            Dark => new SolidColorBrush(Color.Parse("#1E1E22")),
            Sepia => SepiaWindow,
            _ => new SolidColorBrush(Color.Parse("#F3F3F6")),
        };
    }

    private static void SetSepia(Application app, bool on)
    {
        foreach (var (key, brush) in SepiaOverrides)
        {
            if (on) app.Resources[key] = brush;
            else if (app.Resources.ContainsKey(key)) app.Resources.Remove(key);
        }
    }
}
