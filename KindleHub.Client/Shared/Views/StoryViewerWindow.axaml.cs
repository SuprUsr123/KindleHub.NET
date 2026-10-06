using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KindleHub.Core;

namespace KindleHub.Client.Views;

/// <summary>Reads a KHSTORY1 story: its setting, theme, and the log lines in order.
/// Each entry becomes a chat-style bubble — "ai" lines tinted, human speakers
/// neutral.
///
/// Colours are derived from the theme's own foreground at runtime rather than
/// hardcoded, so the bubbles keep their contrast in both light and dark themes.
/// </summary>
public partial class StoryViewerWindow : Window
{
    private static readonly Color MutedFallback = Color.FromRgb(0x77, 0x77, 0x77);

    public StoryViewerWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    /// <summary>Populate the window from a parsed story. Safe to call with null.</summary>
    public void LoadStory(ChatMedia.Story? story)
    {
        Lines.Children.Clear();
        if (story == null)
        {
            SettingText.Text = "Story unavailable";
            ThemeText.IsVisible = false;
            return;
        }

        SettingText.Text = string.IsNullOrWhiteSpace(story.Setting) ? "Untitled story" : story.Setting;
        var theme = story.Theme ?? "";
        ThemeText.Text = string.IsNullOrWhiteSpace(theme) ? "" : "Theme: " + theme;
        ThemeText.IsVisible = !string.IsNullOrWhiteSpace(theme);

        if (story.Log is null || story.Log.Length == 0)
        {
            Lines.Children.Add(new TextBlock
            {
                Text = "This story has no lines.",
                Foreground = new SolidColorBrush(MutedFallback)
            });
            return;
        }

        var baseBrush = InheritedForeground();
        // Alphas chosen so the bubbles stay subtle but keep >=4.5:1 contrast for
        // body text in BOTH themes (measured: dark 4.72:1, light 6.90:1 for the
        // ai tint; the human tint is lighter still). A heavier tint was tried
        // first and dropped the dark theme to 3.40:1, which was unreadable.
        var aiBrush = Tint(baseBrush, 0.06);
        var humanBrush = Tint(baseBrush, 0.035);

        foreach (var entry in story.Log)
        {
            if (entry?.Text is not { Length: > 0 }) continue;
            var isAi = string.Equals(entry.Role, "ai", StringComparison.OrdinalIgnoreCase);

            var panel = new StackPanel { Spacing = 2 };
            if (!isAi && !string.IsNullOrWhiteSpace(entry.Role))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = entry.Role,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(MutedFallback)
                });
            }
            // No explicit Foreground on the body text: it inherits the theme's
            // default, which always contrasts with the tinted bubble behind it.
            panel.Children.Add(new TextBlock
            {
                Text = entry.Text,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            });

            Lines.Children.Add(new Border
            {
                Background = isAi ? aiBrush : humanBrush,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = panel
            });
        }
    }

    /// <summary>The window's effective foreground. A probe TextBlock is parented
    /// briefly so Avalonia's inheritance chain is actually resolved — reading a
    /// detached control would just return the property default.</summary>
    private IBrush InheritedForeground()
    {
        var probe = new TextBlock { Text = "0" };
        Lines.Children.Add(probe);
        var resolved = probe.Foreground;
        Lines.Children.Remove(probe);
        return resolved ?? new SolidColorBrush(Colors.White);
    }

    /// <summary>Overlay a translucent version of the base colour on the window
    /// background. Because the overlay follows the text colour, it lightens a
    /// dark theme and deepens a light one — contrast holds either way.</summary>
    private static IBrush Tint(IBrush baseBrush, double opacity)
    {
        var colour = baseBrush is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        return new SolidColorBrush(colour) { Opacity = opacity };
    }
}
