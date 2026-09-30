using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KindleHub.Client.Models;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

/// <summary>
/// Global leaderboards: read scores per game from kh_scores. The picker lists the
/// whole official arcade (<see cref="GameCatalog"/>) so a leaderboard is reachable
/// before anyone has posted to it, unioned with any game the server does know about
/// that the catalog does not. Also exposes the online-players panel from kh_presence
/// heartbeats.
/// </summary>
public class LeaderboardViewModel : ViewModelBase
{
    private readonly KindleHubCore _core;
    private readonly ILogger<LeaderboardViewModel> _logger;
    private readonly Timer _pingTimer;

    private ObservableCollection<LeaderboardEntry> _entries = new();
    private string _selectedGame = "snake";
    private bool _isLoading;

    public ObservableCollection<LeaderboardEntry> Entries { get => _entries; set => SetProperty(ref _entries, value); }

    public string SelectedGame
    {
        get => _selectedGame;
        set { if (SetProperty(ref _selectedGame, value)) _ = LoadLeaderboardAsync(); }
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

    private ObservableCollection<PresenceEntry> _onlineNow = new();
    public ObservableCollection<PresenceEntry> OnlineNow { get => _onlineNow; set => SetProperty(ref _onlineNow, value); }

    public RelayCommand RefreshGamesCommand { get; }

    public LeaderboardViewModel(KindleHubCore core, ILogger<LeaderboardViewModel> logger)
    {
        _core = core;
        _logger = logger;
        RefreshGamesCommand = new RelayCommand(async () => await RefreshGamesAsync());
        _ = InitialiseAsync();
        _pingTimer = new Timer(async _ => await HeartbeatAsync(), null,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
    }

    private async Task InitialiseAsync()
    {
        await RefreshGamesAsync();
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
        try
        {
            var entries = await _core.FetchLeaderboardAsync(SelectedGame, 25, CancellationToken.None);
            _entries.Clear();
            foreach (var e in entries) _entries.Add(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leaderboard lookup failed for {Game}", SelectedGame);
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
            foreach (var p in list) _onlineNow.Add(p);
        }
        catch
        {
            // Presence is best-effort and may not be readable for all accounts.
        }
    }

    private async Task HeartbeatAsync()
    {
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
}
