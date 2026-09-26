using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

/// <summary>
/// Global leaderboards: read scores per game from kh_scores (the game list is the live
/// union of recent scores and the multiplayer lobby, so it updates automatically). Also
/// exposes the online-players panel from kh_presence heartbeats.
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

    private ObservableCollection<string> _availableGames = new();
    public ObservableCollection<string> AvailableGames { get => _availableGames; set => SetProperty(ref _availableGames, value); }

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
        try
        {
            var games = await _core.ListKnownGamesAsync(CancellationToken.None);
            _availableGames.Clear();
            foreach (var g in games) _availableGames.Add(g);
            if (!games.Contains(SelectedGame) && _availableGames.Count > 0)
                SelectedGame = games[0];
            OnPropertyChanged(nameof(AvailableGames));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Game list failed");
            _availableGames.Clear();
            foreach (var g in new[] { "snake", "2048", "ttt", "memory", "wordle" }) _availableGames.Add(g);
            OnPropertyChanged(nameof(AvailableGames));
        }
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
