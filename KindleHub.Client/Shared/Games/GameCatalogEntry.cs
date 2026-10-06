using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KindleHub.Client.Models;

/// <summary>
/// A single game in the KindleHub arcade catalog. The slug is the deep-link /
/// relay identifier (e.g. "ttt", "snake", "crazy8"); the display name and how-to
/// string come straight from the official client's GAME_HELP table so the
/// desktop client and kindlehub.pro describe each game identically.
/// </summary>
public sealed class GameCatalogEntry : INotifyPropertyChanged
{
    private bool _isDescriptionVisible;

    public GameCatalogEntry(string slug, string displayName, string howTo)
    {
        Slug = slug;
        DisplayName = displayName;
        HowTo = howTo;
    }

    /// <summary>The deep-link / relay id. Appending it to the site root opens this game.</summary>
    public string Slug { get; }

    public string DisplayName { get; }

    /// <summary>How to play, or empty when the official client carries no help text for this game.</summary>
    public string HowTo { get; }

    /// <summary>False for the handful of games the official client ships without help text.</summary>
    public bool HasDescription => !string.IsNullOrEmpty(HowTo);

    /// <summary>
    /// The official cards hide the blurb until you tap "?" — 83 full paragraphs up
    /// front makes the page a wall of text, so we keep that behaviour. The view
    /// binds this straight to IsVisible, so a hidden blurb takes no layout space.
    /// </summary>
    public bool IsDescriptionVisible
    {
        get => _isDescriptionVisible;
        set
        {
            if (_isDescriptionVisible == value) return;
            _isDescriptionVisible = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
