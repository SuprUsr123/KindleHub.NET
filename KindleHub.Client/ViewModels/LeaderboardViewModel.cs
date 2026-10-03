using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using KindleHub.Client.Models;
using KindleHub.Core;
using Microsoft.Extensions.Logging;
using Avalonia.Threading;

namespace KindleHub.Client.ViewModels;

/// <summary>
/// Global leaderboards: read scores per game from kh_scores. The picker lists the
/// whole official arcade (<see cref="GameCatalog"/>) so a leaderboard is reachable
/// before anyone has posted to it, unioned with any game the server does know about
/// that the catalog does not. Also exposes the online-players panel from kh_presence
/// heartbeats.
/// </summary>
public class LeaderboardViewModel : ViewModelBase, IDisposable
{
    private readonly KindleHubCore _core;
    private readonly ILogger<LeaderboardViewModel> _logger;
    private readonly Timer _pingTimer;
    private bool _disposed;

    private ObservableCollection<LeaderboardRow> _entries = new();
    private string _selectedGame = "snake";
    private bool _isLoading;
    private bool _initialising = true;
    private string _statusText = "Loading leaderboard…";

    public ObservableCollection<LeaderboardRow> Entries { get => _entries; set => SetProperty(ref _entries, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public bool HasEntries => Entries.Count > 0;
    public bool HasOnline => OnlineNow.Count > 0;

    public string SelectedGame
    {
        get => _selectedGame;
        set { if (SetProperty(ref _selectedGame, value) && !_initialising) _ = LoadLeaderboardAsync(); }
    }

    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private ObservableCollection<GameCatalogEntry> _availableGames = new();
    public ObservableCollection<GameCatalogEntry> AvailableGames { get => _availableGames; set => SetProperty(ref _availableGames, value); }

    /// <summary>The catalog row the picker has on, kept in step with <see cref="SelectedGame"/>.</summary>
    private GameCatalogEntry? _selectedGameEntry;
    public GameCatalogEntry? SelectedGameEntry
    {
        get => _selectedGameEntry;
        set
        {
            if (SetProperty(ref _selectedGameEntry, value) && value != null)
                SelectedGame = value.Slug;
        }
    }

    private ObservableCollection<OnlinePlayerRow> _onlineNow = new();
    public ObservableCollection<OnlinePlayerRow> OnlineNow { get => _onlineNow; set => SetProperty(ref _onlineNow, value); }

    public RelayCommand RefreshCommand { get; }

    public LeaderboardViewModel(KindleHubCore core, ILogger<LeaderboardViewModel> logger)
    {
        _core = core;
        _logger = logger;
        RefreshCommand = new RelayCommand(async () =>
        {
            await LoadLeaderboardAsync();
            await LoadOnlineAsync();
        });
        _ = InitialiseAsync();
        _pingTimer = new Timer(_ => Dispatcher.UIThread.Post(async () => await HeartbeatAsync()), null,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
    }

    private async Task InitialiseAsync()
    {
        await RefreshGamesAsync();
        _initialising = false;
        await LoadLeaderboardAsync();
        await LoadOnlineAsync();
    }

    public async Task RefreshGamesAsync()
    {
        // The catalog first, so every official game is pickable even with zero scores
        // posted; then anything the server reports that the catalog doesn't know.
        var ordered = new List<GameCatalogEntry>(GameCatalog.All);
        var knownSlugs = new HashSet<string>(GameCatalog.All.Select(g => g.Slug), StringComparer.OrdinalIgnoreCase);

        try
        {
            var games = await _core.ListKnownGamesAsync(CancellationToken.None);
            foreach (var g in games)
            {
                var slug = g?.Trim();
                if (string.IsNullOrEmpty(slug) || !knownSlugs.Add(slug!)) continue;
                ordered.Add(new GameCatalogEntry(slug!, GameCatalog.NameFor(slug!), ""));
            }
        }
        catch (Exception ex)
        {
            // An unreachable API still leaves the full catalog on screen.
            _logger.LogWarning(ex, "Game list failed; showing the catalog only");
        }

        _availableGames.Clear();
        foreach (var g in ordered) _availableGames.Add(g);
        OnPropertyChanged(nameof(AvailableGames));

        SelectedGameEntry = _availableGames.FirstOrDefault(g =>
            string.Equals(g.Slug, SelectedGame, StringComparison.OrdinalIgnoreCase))
            ?? _availableGames.FirstOrDefault();
    }

    public async Task LoadLeaderboardAsync()
    {
        if (string.IsNullOrEmpty(SelectedGame)) return;
        IsLoading = true;
        StatusText = "Loading scores…";
        _entries.Clear();
        OnPropertyChanged(nameof(HasEntries));
        try
        {
            var entries = await _core.FetchLeaderboardAsync(SelectedGame, 25, CancellationToken.None);
            _entries.Clear();
            for (var i = 0; i < entries.Count; i++) _entries.Add(new LeaderboardRow(i + 1, entries[i]));
            try
            {
                var avatars = await _core.FetchAvatarCodesAsync(entries.Select(e => e.UserId), CancellationToken.None);
                foreach (var entry in _entries)
                    if (avatars.TryGetValue(entry.UserId, out var avatar)) entry.SetAvatar(avatar);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Leaderboard profile pictures were unavailable"); }
            StatusText = entries.Count == 0
                ? $"No scores have been posted for {SelectedGameEntry?.DisplayName ?? SelectedGame} yet."
                : $"Top {entries.Count} scores · {SelectedGameEntry?.DisplayName ?? SelectedGame}";
            OnPropertyChanged(nameof(HasEntries));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leaderboard lookup failed for {Game}", SelectedGame);
            StatusText = "Couldn't load scores. Try refreshing.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadOnlineAsync()
    {
        try
        {
            var list = await _core.FetchPresenceAsync(10, 50, CancellationToken.None);
            _onlineNow.Clear();
            foreach (var p in list) _onlineNow.Add(new OnlinePlayerRow(p));
            OnPropertyChanged(nameof(HasOnline));
        }
        catch
        {
            // Presence is best-effort and may not be readable for all accounts.
        }
    }

    private async Task HeartbeatAsync()
    {
        if (_disposed) return;
        try
        {
            if (!_core.IsAuthenticated) return;
            var room = RoomRegistry.ActiveRoom?.Code;
            await _core.PingPresenceAsync(string.IsNullOrEmpty(room) ? "" : room, CancellationToken.None);
            await LoadOnlineAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Presence heartbeat failed");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pingTimer.Dispose();
    }
}

public sealed class LeaderboardRow : INotifyPropertyChanged
{
    public int Rank { get; }
    public string RankLabel => Rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => $"#{Rank}" };
    public string DisplayName => Entry.DisplayName;
    public string UserId => Entry.UserId;
    public string ScoreFormatted => Entry.ScoreFormatted;
    public string DateFormatted => Entry.DateFormatted;
    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[0].ToString().ToUpperInvariant();
    private string _avatarCode = "";
    public bool HasAvatar => ProfileAvatar.TryDecode(_avatarCode, out _, out _);
    public Avalonia.Media.DrawingImage? AvatarImage => ProfileAvatar.Render(_avatarCode);
    public event PropertyChangedEventHandler? PropertyChanged;
    public LeaderboardEntry Entry { get; }

    public LeaderboardRow(int rank, LeaderboardEntry entry) { Rank = rank; Entry = entry; }

    public void SetAvatar(string code)
    {
        _avatarCode = code ?? "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarImage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAvatar)));
    }
}

public sealed class OnlinePlayerRow
{
    private readonly PresenceEntry _entry;
    public string DisplayName => _entry.DisplayName;
    public string AgeFormatted => _entry.AgeFormatted;
    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[0].ToString().ToUpperInvariant();
    public bool HasAvatar => ProfileAvatar.TryDecode(_entry.Avatar, out _, out _);
    public Avalonia.Media.DrawingImage? AvatarImage => ProfileAvatar.Render(_entry.Avatar);

    public OnlinePlayerRow(PresenceEntry entry) => _entry = entry;
}
