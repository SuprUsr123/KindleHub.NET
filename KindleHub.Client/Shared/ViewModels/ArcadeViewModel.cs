using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using KindleHub.Client.Games;
using KindleHub.Client.Models;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

/// <summary>One row in the arcade list, and the whole of the "which game" state.</summary>
public sealed class ArcadeGame
{
    /// <summary>Tic-Tac-Toe is playable, but on its own relay screen rather than
    /// through the board window — so it counts as ported here, and is flagged so
    /// Play hands off to that screen instead of opening a board.</summary>
    public const string TicTacToeSlug = "ttt";

    public GameCatalogEntry Catalog { get; }
    public bool IsPorted => GameRegistry.IsPorted(Catalog.Slug) || UsesRelayScreen;
    public bool UsesRelayScreen => Catalog.Slug == TicTacToeSlug;
    public bool HasOnlinePlay => UsesRelayScreen || Catalog.Slug is "connect4" or "reversi" or "dotsboxes";
    public string Slug => Catalog.Slug;
    public string Name => Catalog.DisplayName;
    public string Description => Catalog.HowTo;
    public bool HasDescription => Catalog.HasDescription;

    public string Monogram
    {
        get
        {
            var parts = Name
                .Split(new[] { ' ', '-', '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.Length > 0)
                .Select(p => p[0])
                .Take(2)
                .ToArray();

            if (parts.Length >= 2) return new string(parts).ToUpperInvariant();
            return Name.Length <= 2 ? Name.ToUpperInvariant() : Name[..2].ToUpperInvariant();
        }
    }

    public ArcadeGame(GameCatalogEntry entry) => Catalog = entry;
}

/// <summary>
/// The arcade: the games ported from the official client, playable in-process
/// through one shared board view. The full official catalog is listed too, so the
/// page shows the same catalogue the site does, with a clear marker on the games
/// that are not ported yet rather than a card that does nothing.
/// </summary>
public class ArcadeViewModel : ViewModelBase
{
    private readonly KindleHubCore _core;
    private readonly ILogger<ArcadeViewModel> _logger;
    private readonly Func<string, int, CancellationToken, Task<bool>> _submitScore;
    private readonly bool _scoreSubmitWasInjected;
    private int _scoreSubmittingAttempt = -1;
    private int _scoreSubmittedAttempt = -1;
    private int _scoreFailedAttempt = -1;

    /// <summary>Lets the Tic-Tac-Toe card hand off to the relay view. Tic-Tac-Toe is
    /// a real game, so it belongs in the arcade rather than as its own nav entry —
    /// but it needs the dedicated relay screen, which this page doesn't host.</summary>
    private readonly System.Action<string>? _navigate;
    private readonly System.Action<ArcadeViewModel>? _showOnlinePage;

    private Connect4Session? _connect4;
    private Connect4Session? _debugFoe;
    private DotsBoxesSession? _dotsBoxes;
    private DotsBoxesSession? _dotsBoxesDebugFoe;
    private ReversiSession? _reversi;
    private ReversiSession? _reversiDebugFoe;
    private string _connect4Room = "";
    private string _onlineStatus = "";
    private string _dotsBoxesRoom = "";
    private string _reversiRoom = "";
    private bool _isLoadingLobbies;
    private string _lobbyStatus = "";

    /// <summary>Room code to type when joining a Connect 4 match.</summary>
    public string Connect4Room
    {
        get => _connect4Room;
        set => SetProperty(ref _connect4Room, value);
    }

    public bool IsInConnect4Online => _connect4 != null;
    public bool ShowConnect4Tools => _game?.Slug == "connect4" && _connect4 is null;
    public bool ShowDotsBoxesTools => _game?.Slug == "dotsboxes" && _dotsBoxes is null;
    public bool IsInDotsBoxesOnline => _dotsBoxes is not null;
    public bool IsDotsBoxesDebug => _dotsBoxes?.IsOfflineDebug == true;
    public string DotsBoxesRoom { get => _dotsBoxesRoom; set => SetProperty(ref _dotsBoxesRoom, value); }
    public bool ShowReversiTools => _game?.Slug == "reversi" && _reversi is null;
    public bool ShowLobbyTools => ShowConnect4Tools || ShowDotsBoxesTools || ShowReversiTools;
    public ObservableCollection<OpenGameListing> OpenLobbies { get; } = new();
    public bool HasOpenLobbies => OpenLobbies.Count > 0;
    public bool IsLoadingLobbies { get => _isLoadingLobbies; private set => SetProperty(ref _isLoadingLobbies, value); }
    public string LobbyStatus { get => _lobbyStatus; private set => SetProperty(ref _lobbyStatus, value); }
    public bool IsInReversiOnline => _reversi is not null;
    public bool IsReversiDebug => _reversi?.IsOfflineDebug == true;
    public string ReversiRoom { get => _reversiRoom; set => SetProperty(ref _reversiRoom, value); }
    /// <summary>True while the offline debug match is running.</summary>
    public bool IsConnect4Debug => _connect4?.IsOfflineDebug == true;
    public string OnlineStatus => _onlineStatus;
    private bool _isOnlineGamePage;
    public bool IsOnlineGamePage { get => _isOnlineGamePage; private set => SetProperty(ref _isOnlineGamePage, value); }
    public bool HasOnlineBoard => _connect4 is not null || _dotsBoxes is not null || _reversi is not null;
    public int OnlineBoardColumns => Columns;
    public int OnlineBoardRows => Rows;

    private IGame? _game;
    private ObservableCollection<GameCell> _cells = new();
    private string _searchText = "";
    private string _filterMode = "All games";
    private string _status = "";
    private string? _result;
    private bool _isRealTime;
    private bool _canGoLeft, _canGoRight, _canGoUp, _canGoDown;

    /// <summary>Every game in the official catalog, unfiltered. The two lists below
    /// are the filtered split of this.</summary>
    public ObservableCollection<ArcadeGame> All { get; } = new();

    /// <summary>Games ported out of the official client and playable in-process here.</summary>
    public ObservableCollection<ArcadeGame> PortedGames { get; } = new();

    /// <summary>Games the official client has that this client has not ported yet.</summary>
    public ObservableCollection<ArcadeGame> UnportedGames { get; } = new();

    /// <summary>Everything the filter matched, ported first. Kept so callers that
    /// just want "what's showing" don't have to walk both lists.</summary>
    public IReadOnlyList<ArcadeGame> Visible =>
        PortedGames.Count + UnportedGames.Count == 0
            ? Array.Empty<ArcadeGame>()
            : PortedGames.Concat(UnportedGames).ToList();

    public IReadOnlyList<GameCell> Cells => _cells;
    public IGame? CurrentGame => _game;
    public string GameName => _game?.Name ?? (_connect4 != null ? "Connect 4" : "");
    public string Slug => _game?.Slug ?? (_connect4 != null ? "connect4" : "");

    /// <summary>Board width in cells. The view binds this straight to its UniformGrid.</summary>
    public int Columns => _game?.Columns ?? (_connect4 != null ? C4Rules.Cols : 1);

    /// <summary>Board height in cells, so the grid never leaves a half-empty last row.</summary>
    public int Rows => _game != null
        ? Math.Max(1, (int)Math.Ceiling((double)_game.Cells.Count / _game.Columns))
        : _connect4 != null ? C4Rules.Rows : 1;

    /// <summary>Pixel scale for the shared game board. Small-grid games use larger,
    /// touch-friendly cells while dense games stay compact instead of stretching
    /// every tile across the whole window.</summary>
    private double CellSize => _game?.Slug switch
    {
        "hanoi" => 132,
        "minesweeper" => 42,
        "sudoku" => 42,
        "snake" => 22,
        "connect4" => 52,
        "memory" => 68,
        "lightsout" => 64,
        "reversi" => 52,
        "numslide" => 68,
        "g2048" => 76,
        "mastermind" => 48,
        "pegs" => 48,
        "hangman" => 34,
        "wordle" => 44,
        "nim" => 44,
        "simon" => 76,
        "dotsboxes" => 38,
        _ => 52,
    };

    public double BoardWidth => Columns * CellSize;
    public double BoardHeight => Rows * CellSize;
    public string Status => _status;
    public string? Result => _result;
    public bool HasResult => !string.IsNullOrEmpty(_result);
    /// <summary>True whenever a board is on screen — a ported game or a live
    /// Connect 4 match. This gates the game window, so it must not depend on
    /// _game alone.</summary>
    public bool InGame => _game != null || _connect4 != null;
    public bool NotInGame => !InGame;
    public bool IsRealTime => _isRealTime;
    public bool NeedsTicks => _game?.NeedsTicks ?? false;
    public bool CanSubmitMastermind => _game is MastermindGame mastermind && mastermind.CanSubmit;

    public bool CanGoLeft { get => _canGoLeft; private set => SetProperty(ref _canGoLeft, value); }
    public bool CanGoRight { get => _canGoRight; private set => SetProperty(ref _canGoRight, value); }
    public bool CanGoUp { get => _canGoUp; private set => SetProperty(ref _canGoUp, value); }
    public bool CanGoDown { get => _canGoDown; private set => SetProperty(ref _canGoDown, value); }

    public int PortedCount => All.Count(g => g.IsPorted);
    public int UnportedCount => All.Count - PortedCount;
    public int TotalCount => All.Count;
    public int VisibleCount => PortedGames.Count + UnportedGames.Count;
    public string[] FilterModes { get; } = { "All games", "Playable", "Coming soon" };

    public string FilterMode
    {
        get => _filterMode;
        set { if (SetProperty(ref _filterMode, value)) ApplyFilter(); }
    }

    public bool NoMatches => PortedGames.Count == 0 && UnportedGames.Count == 0;

    /// <summary>Drives each section's header, so an empty group collapses entirely.</summary>
    public bool HasPorted => PortedGames.Count > 0;
    public bool HasUnported => UnportedGames.Count > 0;

    /// <summary>
    /// Counts game attempts, and only ever increases. The view posts at most one
    /// score per attempt, so winning twice submits twice — a finished game that
    /// stays on screen must not re-post on every later tap.
    /// </summary>
    public int Attempt { get; private set; }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    /// <summary>Raised when a game is ready to play, so the view can open a window
    /// for it. The view model never creates UI.</summary>
    public event Action? GameReady;

    public RelayCommand<ArcadeGame> PlayCommand { get; }
    public RelayCommand<ArcadeGame> PlayOnlineCommand { get; }
    public RelayCommand RefreshLobbiesCommand { get; }
    public RelayCommand<OpenGameListing> JoinLobbyCommand { get; }
    public RelayCommand<ArcadeGame> ToggleHelpCommand { get; }
    public RelayCommand NewGameCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand RetryScoreCommand { get; }

    /// <summary>Opens the multiplayer Tic-Tac-Toe relay screen.</summary>
    public RelayCommand PlayTicTacToeCommand { get; }

    /// <summary>Hosts a Connect 4 match on the official relay.</summary>
    public RelayCommand HostConnect4Command { get; }

    /// <summary>Joins a Connect 4 match by room code.</summary>
    public RelayCommand JoinConnect4Command { get; }

    /// <summary>Leaves the Connect 4 relay match.</summary>
    public RelayCommand LeaveConnect4Command { get; }

    /// <summary>Starts Connect 4 against a local debug opponent — no account, no server.</summary>
    public RelayCommand DebugConnect4Command { get; }

    /// <summary>One step of the debug opponent, so the UI can drive it on a timer.</summary>
    public RelayCommand DebugStepCommand { get; }
    public RelayCommand StartDotsBoxesDebugCommand { get; }
    public RelayCommand DotsBoxesDebugStepCommand { get; }
    public RelayCommand HostDotsBoxesCommand { get; }
    public RelayCommand JoinDotsBoxesCommand { get; }
    public RelayCommand LeaveDotsBoxesCommand { get; }
    public RelayCommand HostReversiCommand { get; }
    public RelayCommand JoinReversiCommand { get; }
    public RelayCommand StartReversiDebugCommand { get; }
    public RelayCommand ReversiDebugStepCommand { get; }
    public RelayCommand LeaveReversiCommand { get; }
    public RelayCommand<int> TapCommand { get; }

    // Four commands rather than one taking a GameKey: XAML cannot convert a command
    // parameter string into an enum without a converter, and this stays converter-free.
    public RelayCommand LeftCommand { get; }
    public RelayCommand UpCommand { get; }
    public RelayCommand DownCommand { get; }
    public RelayCommand RightCommand { get; }
    public RelayCommand ConfirmCommand { get; }

    public ArcadeViewModel(KindleHubCore core, ILogger<ArcadeViewModel> logger,
                           System.Action<string>? navigate = null,
                           Func<string, int, CancellationToken, Task<bool>>? submitScore = null,
                           System.Action<ArcadeViewModel>? showOnlinePage = null)
    {
        _core = core;
        _logger = logger;
        _scoreSubmitWasInjected = submitScore is not null;
        _submitScore = submitScore ?? ((slug, score, token) => _core.SubmitScoreAsync(slug, score, token));
        _navigate = navigate;
        _showOnlinePage = showOnlinePage;

        foreach (var entry in GameCatalog.All) All.Add(new ArcadeGame(entry));
        ApplyFilter();

        PlayCommand = new RelayCommand<ArcadeGame>(g =>
        {
            if (g is null) return;
            // Tic-Tac-Toe lives on the relay screen; everything else plays on the board.
            if (g.UsesRelayScreen) { _navigate?.Invoke("Games"); return; }
            Start(g);
        });
        PlayOnlineCommand = new RelayCommand<ArcadeGame>(g =>
        {
            if (g is null || !g.HasOnlinePlay) return;
            if (g.UsesRelayScreen) { _navigate?.Invoke("Games"); return; }
            OpenOnlineGamePage(g);
        });
        RefreshLobbiesCommand = new RelayCommand(async () => await RefreshLobbiesAsync());
        JoinLobbyCommand = new RelayCommand<OpenGameListing>(async lobby => await JoinLobbyAsync(lobby));
        ToggleHelpCommand = new RelayCommand<ArcadeGame>(g =>
        {
            if (g != null) g.Catalog.IsDescriptionVisible = !g.Catalog.IsDescriptionVisible;
        });
        NewGameCommand = new RelayCommand(() =>
        {
            if (_game is null) return;
            _game.Reset();
            Attempt++;
            Refresh();
        });
        BackCommand = new RelayCommand(() =>
        {
            _dotsBoxes?.Dispose(); _dotsBoxes = null;
            _dotsBoxesDebugFoe?.Dispose(); _dotsBoxesDebugFoe = null;
            _reversi?.Dispose(); _reversi = null;
            _reversiDebugFoe?.Dispose(); _reversiDebugFoe = null;
            _connect4?.Dispose(); _connect4 = null;
            _debugFoe?.Dispose(); _debugFoe = null;
            _game = null;
            IsOnlineGamePage = false;
            Refresh();
        });
        RetryScoreCommand = new RelayCommand(async () => await SubmitScoreIfNeededAsync(), () => CanRetryScore);
        PlayTicTacToeCommand = new RelayCommand(() =>
        {
            _game = null;
            Refresh();
            _navigate?.Invoke("Games");
        });
        HostConnect4Command = new RelayCommand(async () => await HostConnect4Async());
        JoinConnect4Command = new RelayCommand(async () => await JoinConnect4Async());
        DebugConnect4Command = new RelayCommand(() =>
        {
            _connect4?.Dispose();
            (_connect4, _debugFoe) = Connect4Session.CreateOfflinePair(_core, _logger);
            _connect4.Changed += OnConnect4Changed;
            _onlineStatus = "Offline debug — you are Red. No account or server involved.";
            OnPropertyChanged(nameof(OnlineStatus));
            OnPropertyChanged(nameof(IsInConnect4Online));
            OnPropertyChanged(nameof(IsConnect4Debug));
            ShowConnect4();
            NavigateToOnlinePage();
        });
        DebugStepCommand = new RelayCommand(() =>
        {
            // The debug opponent only ever acts on its own turn, so pressing this
            // at the wrong time is a no-op rather than an error.
            if (_debugFoe is null) return;
            _ = _debugFoe.DebugOpponentMove();
        });
        StartDotsBoxesDebugCommand = new RelayCommand(() =>
        {
            _dotsBoxes?.Dispose();
            _dotsBoxesDebugFoe?.Dispose();
            (_dotsBoxes, _dotsBoxesDebugFoe) = DotsBoxesSession.CreateOfflinePair(_core, _logger);
            _dotsBoxes.Changed += OnDotsBoxesChanged;
            _dotsBoxesDebugFoe.Changed += OnDotsBoxesChanged;
            _dotsBoxesRoom = "";
            ShowDotsBoxes();
            NavigateToOnlinePage();
        });
        DotsBoxesDebugStepCommand = new RelayCommand(() =>
        {
            if (_dotsBoxes is not null) _ = _dotsBoxes.DebugOpponentMoveAsync();
        });
        HostDotsBoxesCommand = new RelayCommand(async () => await HostDotsBoxesAsync());
        JoinDotsBoxesCommand = new RelayCommand(async () => await JoinDotsBoxesAsync());
        LeaveDotsBoxesCommand = new RelayCommand(() =>
        {
            _dotsBoxes?.Dispose();
            _dotsBoxesDebugFoe?.Dispose();
            _dotsBoxes = _dotsBoxesDebugFoe = null;
            _onlineStatus = "";
            _game = null;
            IsOnlineGamePage = false;
            Refresh();
        });
        HostReversiCommand = new RelayCommand(async () => await HostReversiAsync());
        JoinReversiCommand = new RelayCommand(async () => await JoinReversiAsync());
        StartReversiDebugCommand = new RelayCommand(() =>
        {
            _reversi?.Dispose(); _reversiDebugFoe?.Dispose();
            (_reversi, _reversiDebugFoe) = ReversiSession.CreateOfflinePair(_core, _logger);
            _reversi.Changed += OnReversiChanged;
            _reversiDebugFoe.Changed += OnReversiChanged;
            _reversiRoom = "";
            ShowReversi();
            NavigateToOnlinePage();
        });
        ReversiDebugStepCommand = new RelayCommand(() =>
        {
            if (_reversi is not null) _ = _reversi.DebugOpponentMoveAsync();
        });
        LeaveReversiCommand = new RelayCommand(() =>
        {
            _reversi?.Dispose(); _reversiDebugFoe?.Dispose();
            _reversi = _reversiDebugFoe = null;
            _game = null;
            IsOnlineGamePage = false;
            Refresh();
        });
        LeaveConnect4Command = new RelayCommand(() =>
        {
            _connect4?.Dispose();
            _debugFoe?.Dispose();
            _connect4 = null;
            _debugFoe = null;
            _connect4Room = "";
            _onlineStatus = "";
            OnPropertyChanged(nameof(Connect4Room));
            OnPropertyChanged(nameof(IsInConnect4Online));
            OnPropertyChanged(nameof(IsConnect4Debug));
            _game = null;
            IsOnlineGamePage = false;
            Refresh();
        });
        TapCommand = new RelayCommand<int>(i =>
        {
            // A live Connect 4 match takes priority: a tap on the board is a drop.
            if (_connect4 != null)
            {
                _ = DropConnect4Async(i % C4Rules.Cols);
                return;
            }
            if (_dotsBoxes is not null)
            {
                _ = _dotsBoxes.PlayAtAsync(i);
                return;
            }
            if (_reversi is not null)
            {
                _ = _reversi.PlayAtAsync(i);
                return;
            }
            if (_game != null) { _game.OnTap(i); Refresh(); }
        });
        LeftCommand = new RelayCommand(() => Press(GameKey.Left));
        UpCommand = new RelayCommand(() => Press(GameKey.Up));
        DownCommand = new RelayCommand(() => Press(GameKey.Down));
        RightCommand = new RelayCommand(() => Press(GameKey.Right));
        ConfirmCommand = new RelayCommand(() => Press(GameKey.Confirm));
    }

    /// <summary>Feeds a key to the game and lets the view notice a finished score.</summary>
    public void Press(GameKey key)
    {
        if (_game == null) return;
        _game.OnKey(key);
        Refresh();
    }

    public bool PressLetter(char letter)
    {
        if (_game == null) return false;
        bool handled = _game.OnLetter(letter);
        if (handled) Refresh();
        return handled;
    }

    public bool PressBackspace()
    {
        if (_game == null) return false;
        bool handled = _game.OnBackspace();
        if (handled) Refresh();
        return handled;
    }

    public bool SecondaryTap(int index)
    {
        if (_game == null) return false;
        bool handled = _game.OnSecondaryTap(index);
        if (handled) Refresh();
        return handled;
    }

    /// <summary>
    /// Splits the catalog into the two lists the page shows. The filter matches on
    /// display name or slug, and a search can land in either group — filtering does
    /// not move a game between them.
    /// </summary>
    private void ApplyFilter()
    {
        var q = (_searchText ?? "").Trim();
        IEnumerable<ArcadeGame> matches = All;

        if (q.Length > 0)
        {
            matches = matches.Where(g =>
                g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || g.Slug.Contains(q, StringComparison.OrdinalIgnoreCase)
                || g.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        matches = _filterMode switch
        {
            "Playable" => matches.Where(g => g.IsPorted),
            "Coming soon" => matches.Where(g => !g.IsPorted),
            _ => matches,
        };

        PortedGames.Clear();
        UnportedGames.Clear();
        foreach (var g in matches)
        {
            if (g.IsPorted) PortedGames.Add(g);
            else UnportedGames.Add(g);
        }

        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(HasPorted));
        OnPropertyChanged(nameof(HasUnported));
        OnPropertyChanged(nameof(VisibleCount));
    }

    private void OpenOnlineGamePage(ArcadeGame game)
    {
        _game = GameRegistry.Create(game.Slug);
        Attempt++;
        Refresh();
        IsOnlineGamePage = true;
        OpenLobbies.Clear();
        OnPropertyChanged(nameof(HasOpenLobbies));
        LobbyStatus = "Looking for open rooms…";
        _showOnlinePage?.Invoke(this);
        _ = RefreshLobbiesAsync();
    }

    public async Task JoinFromCommandAsync(string gameTarget, string roomCode)
    {
        var query = (gameTarget ?? "").Trim();
        var normalized = new string(query.Where(char.IsLetterOrDigit).ToArray());
        var game = All.FirstOrDefault(candidate =>
            string.Equals(candidate.Slug, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase)
            || new string(candidate.Slug.Where(char.IsLetterOrDigit).ToArray()).Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || new string(candidate.Name.Where(char.IsLetterOrDigit).ToArray()).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (game == null)
        {
            LobbyStatus = $"No game matched '{query}'. Try its Arcade name or slug.";
            return;
        }
        if (game.UsesRelayScreen)
        {
            _navigate?.Invoke("Games");
            return;
        }
        var digits = new string((roomCode ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 6 && game.HasOnlinePlay)
        {
            OpenOnlineGamePage(game);
            switch (game.Slug)
            {
                case Connect4Session.GameSlug:
                    Connect4Room = digits;
                    await JoinConnect4Async();
                    break;
                case DotsBoxesSession.GameSlug:
                    DotsBoxesRoom = digits;
                    await JoinDotsBoxesAsync();
                    break;
                case ReversiSession.GameSlug:
                    ReversiRoom = digits;
                    await JoinReversiAsync();
                    break;
                default:
                    LobbyStatus = $"{game.Name} doesn't support direct room-code joining.";
                    break;
            }
            return;
        }
        if (!string.IsNullOrWhiteSpace(roomCode))
        {
            LobbyStatus = "Game room codes are six digits.";
            return;
        }
        if (game.HasOnlinePlay) OpenOnlineGamePage(game);
        else Start(game);
    }

    private async Task RefreshLobbiesAsync()
    {
        var slug = _game?.Slug;
        if (slug is not (Connect4Session.GameSlug or DotsBoxesSession.GameSlug or ReversiSession.GameSlug)) return;
        LobbyStatus = "Looking for open rooms…";
        IsLoadingLobbies = true;
        try
        {
            var rooms = await _core.PollLobbyForOpenGamesAsync(slug, CancellationToken.None);
            if (_game?.Slug != slug || !IsOnlineGamePage) return;
            OpenLobbies.Clear();
            foreach (var room in rooms) OpenLobbies.Add(room);
            LobbyStatus = rooms.Count == 0
                ? "No open rooms right now. Host a room or refresh to look again."
                : $"{rooms.Count} open {(_game?.Name ?? "game").ToLowerInvariant()} room(s).";
            OnPropertyChanged(nameof(HasOpenLobbies));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open game lobby refresh failed for {Game}", slug);
            LobbyStatus = "Couldn't reach the lobby. Check your connection and try again.";
        }
        finally { IsLoadingLobbies = false; }
    }

    private async Task JoinLobbyAsync(OpenGameListing? lobby)
    {
        if (lobby is null || lobby.Game != _game?.Slug) return;
        switch (lobby.Game)
        {
            case Connect4Session.GameSlug:
                Connect4Room = lobby.RoomShort;
                await JoinConnect4Async();
                break;
            case DotsBoxesSession.GameSlug:
                DotsBoxesRoom = lobby.RoomShort;
                await JoinDotsBoxesAsync();
                break;
            case ReversiSession.GameSlug:
                ReversiRoom = lobby.RoomShort;
                await JoinReversiAsync();
                break;
        }
    }

    private void NavigateToOnlinePage()
    {
        IsOnlineGamePage = true;
        OnPropertyChanged(nameof(HasOnlineBoard));
        OnPropertyChanged(nameof(OnlineBoardColumns));
        OnPropertyChanged(nameof(OnlineBoardRows));
        _showOnlinePage?.Invoke(this);
    }

    /// <summary>Hosts a Connect 4 relay match and shows the board it produces.</summary>
    private async Task HostConnect4Async()
    {
        if (!_core.IsAuthenticated) { _onlineStatus = "Sign in to host a game."; OnPropertyChanged(nameof(OnlineStatus)); return; }
        try
        {
            _connect4?.Dispose();
            _connect4 = await Connect4Session.HostAsync(_core, _logger);
            _connect4.Changed += OnConnect4Changed;
            _connect4Room = _connect4.RoomCode is { Length: > 6 } r ? r[^6..] : _connect4.RoomCode ?? "";
            OnPropertyChanged(nameof(Connect4Room));
            OnPropertyChanged(nameof(IsInConnect4Online));
        OnPropertyChanged(nameof(HasOnlineBoard));
        OnPropertyChanged(nameof(OnlineBoardColumns));
        OnPropertyChanged(nameof(OnlineBoardRows));
            ShowConnect4();
            NavigateToOnlinePage();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connect 4 host failed");
            _onlineStatus = "Couldn't host a Connect 4 room.";
            OnPropertyChanged(nameof(OnlineStatus));
        }
    }

    private async Task JoinConnect4Async()
    {
        if (!_core.IsAuthenticated) { _onlineStatus = "Sign in to join a game."; OnPropertyChanged(nameof(OnlineStatus)); return; }
        var code = new string((Connect4Room ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != 6)
        {
            _onlineStatus = "Room codes are six digits.";
            OnPropertyChanged(nameof(OnlineStatus));
            return;
        }
        try
        {
            _connect4?.Dispose();
            _connect4 = await Connect4Session.JoinAsync(_core, _logger, code, "");
            _connect4.Changed += OnConnect4Changed;
            OnPropertyChanged(nameof(IsInConnect4Online));
        OnPropertyChanged(nameof(HasOnlineBoard));
        OnPropertyChanged(nameof(OnlineBoardColumns));
        OnPropertyChanged(nameof(OnlineBoardRows));
            ShowConnect4();
            NavigateToOnlinePage();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connect 4 join failed");
            _onlineStatus = "Couldn't join that Connect 4 room.";
            OnPropertyChanged(nameof(OnlineStatus));
        }
    }

    private void OnConnect4Changed()
    {
        _onlineStatus = _connect4?.Status ?? "";
        OnPropertyChanged(nameof(OnlineStatus));
        ShowConnect4();
    }

    private async Task HostDotsBoxesAsync()
    {
        if (!_core.IsAuthenticated) { _onlineStatus = "Sign in to host a game."; OnPropertyChanged(nameof(OnlineStatus)); return; }
        try
        {
            _dotsBoxes?.Dispose();
            _dotsBoxesDebugFoe?.Dispose(); _dotsBoxesDebugFoe = null;
            _dotsBoxes = await DotsBoxesSession.HostAsync(_core, _logger);
            _dotsBoxes.Changed += OnDotsBoxesChanged;
            _dotsBoxesRoom = _dotsBoxes.RoomCode is { Length: > 6 } room ? room[^6..] : _dotsBoxes.RoomCode ?? "";
            OnPropertyChanged(nameof(DotsBoxesRoom));
            ShowDotsBoxes();
            NavigateToOnlinePage();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dots & Boxes host failed");
            _onlineStatus = "Couldn't host a Dots & Boxes room.";
            OnPropertyChanged(nameof(OnlineStatus));
        }
    }

    private async Task JoinDotsBoxesAsync()
    {
        if (!_core.IsAuthenticated) { _onlineStatus = "Sign in to join a game."; OnPropertyChanged(nameof(OnlineStatus)); return; }
        var code = new string((_dotsBoxesRoom ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != 6) { _onlineStatus = "Room codes are six digits."; OnPropertyChanged(nameof(OnlineStatus)); return; }
        try
        {
            _dotsBoxes?.Dispose();
            _dotsBoxesDebugFoe?.Dispose(); _dotsBoxesDebugFoe = null;
            _dotsBoxes = await DotsBoxesSession.JoinAsync(_core, _logger, code);
            _dotsBoxes.Changed += OnDotsBoxesChanged;
            ShowDotsBoxes();
            NavigateToOnlinePage();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dots & Boxes join failed");
            _onlineStatus = "Couldn't join that Dots & Boxes room.";
            OnPropertyChanged(nameof(OnlineStatus));
        }
    }

    private void OnDotsBoxesChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(ShowDotsBoxes);
    }

    private void ShowDotsBoxes()
    {
        if (_dotsBoxes is null) return;
        _game = _dotsBoxes.Game;
        _status = _dotsBoxes.Status;
        _result = _game.ResultText;
        _isRealTime = false;
        _cells = new ObservableCollection<GameCell>(_game.Cells);
        _onlineStatus = _dotsBoxes.Status;
        OnPropertyChanged(nameof(Cells));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(InGame));
        OnPropertyChanged(nameof(NotInGame));
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(Slug));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(BoardWidth));
        OnPropertyChanged(nameof(BoardHeight));
        OnPropertyChanged(nameof(OnlineStatus));
        OnPropertyChanged(nameof(IsInDotsBoxesOnline));
        OnPropertyChanged(nameof(ShowLobbyTools));
        OnPropertyChanged(nameof(HasOnlineBoard));
        OnPropertyChanged(nameof(OnlineBoardColumns));
        OnPropertyChanged(nameof(OnlineBoardRows));
        OnPropertyChanged(nameof(IsDotsBoxesDebug));
        OnPropertyChanged(nameof(ShowDotsBoxesTools));
    }

    private async Task HostReversiAsync()
    {
        if (!_core.IsAuthenticated) { _onlineStatus = "Sign in to host a game."; OnPropertyChanged(nameof(OnlineStatus)); return; }
        try
        {
            _reversi?.Dispose(); _reversiDebugFoe?.Dispose(); _reversiDebugFoe = null;
            _reversi = await ReversiSession.HostAsync(_core, _logger);
            _reversi.Changed += OnReversiChanged;
            _reversiRoom = _reversi.RoomCode is { Length: > 6 } room ? room[^6..] : _reversi.RoomCode ?? "";
            OnPropertyChanged(nameof(ReversiRoom));
            ShowReversi();
            NavigateToOnlinePage();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reversi host failed");
            _onlineStatus = "Couldn't host a Reversi room.";
            OnPropertyChanged(nameof(OnlineStatus));
        }
    }

    private async Task JoinReversiAsync()
    {
        if (!_core.IsAuthenticated) { _onlineStatus = "Sign in to join a game."; OnPropertyChanged(nameof(OnlineStatus)); return; }
        var code = new string((_reversiRoom ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != 6) { _onlineStatus = "Room codes are six digits."; OnPropertyChanged(nameof(OnlineStatus)); return; }
        try
        {
            _reversi?.Dispose(); _reversiDebugFoe?.Dispose(); _reversiDebugFoe = null;
            _reversi = await ReversiSession.JoinAsync(_core, _logger, code);
            _reversi.Changed += OnReversiChanged;
            ShowReversi();
            NavigateToOnlinePage();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reversi join failed");
            _onlineStatus = "Couldn't join that Reversi room.";
            OnPropertyChanged(nameof(OnlineStatus));
        }
    }

    private void OnReversiChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(ShowReversi);

    private void ShowReversi()
    {
        if (_reversi is null) return;
        _game = _reversi.Game;
        _status = _reversi.Status;
        _result = _game.ResultText;
        _isRealTime = false;
        _cells = new ObservableCollection<GameCell>(_game.Cells);
        _onlineStatus = _reversi.Status;
        OnPropertyChanged(nameof(Cells));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(InGame));
        OnPropertyChanged(nameof(NotInGame));
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(Slug));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(BoardWidth));
        OnPropertyChanged(nameof(BoardHeight));
        OnPropertyChanged(nameof(OnlineStatus));
        OnPropertyChanged(nameof(ShowReversiTools));
        OnPropertyChanged(nameof(ShowLobbyTools));
        OnPropertyChanged(nameof(IsInReversiOnline));
        OnPropertyChanged(nameof(HasOnlineBoard));
        OnPropertyChanged(nameof(OnlineBoardColumns));
        OnPropertyChanged(nameof(OnlineBoardRows));
        OnPropertyChanged(nameof(IsReversiDebug));
    }

    /// <summary>Renders the live relay board through the same GameCell pipeline the
    /// offline games use, so the app keeps a single board view.</summary>
    private void ShowConnect4()
    {
        var s = _connect4;
        if (s is null) return;
        _status = s.Status;
        _result = s.Result;
        _isRealTime = false;

        var grid = s.Grid as int[];
        var cells = new List<GameCell>(C4Rules.Cols * C4Rules.Rows);
        for (int r = 0; r < C4Rules.Rows; r++)
            for (int c = 0; c < C4Rules.Cols; c++)
            {
                int v = grid is null ? 0 : grid[C4Rules.Index(c, r)];
                bool canDrop = s.IsMyTurn && grid is not null && C4Rules.DropRow(grid, c) >= 0;
                cells.Add(new GameCell
                {
                    Text = v != C4Rules.Empty ? "●" : canDrop ? "↓" : "",
                    Background = v == C4Rules.Empty ? CellSurface : v == C4Rules.Red ? PieceRed : PieceYellow,
                    Foreground = v == C4Rules.Empty ? MutedInk : PieceInk,
                    IsEnabled = canDrop,
                    FontSize = 26,
                    Bold = true,
                });
            }
        _cells = new ObservableCollection<GameCell>(cells);
        OnPropertyChanged(nameof(Cells));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(InGame));
        OnPropertyChanged(nameof(NotInGame));
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(BoardWidth));
        OnPropertyChanged(nameof(BoardHeight));
        OnPropertyChanged(nameof(ShowConnect4Tools));
        OnPropertyChanged(nameof(ShowLobbyTools));
    }

    /// <summary>A drop in the live board, routed from the board view.</summary>
    public async Task DropConnect4Async(int col)
    {
        if (_connect4 is null) return;
        await _connect4.DropAsync(col);
    }

    // Palette copied from GameBase so the relay board matches the offline one.
    // (The shared brushes are protected, so they're mirrored rather than reached for.)
    private static readonly Avalonia.Media.IBrush CellSurface =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#fbfbfd"));
    private static readonly Avalonia.Media.IBrush MutedInk =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6b6b76"));
    private static readonly Avalonia.Media.IBrush PieceRed =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#b3261e"));
    private static readonly Avalonia.Media.IBrush PieceYellow =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9a6700"));
    private static readonly Avalonia.Media.IBrush PieceInk =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#fbfbfd"));

    private void Start(ArcadeGame entry)
    {
        var game = GameRegistry.Create(entry.Slug);
        if (game == null)
        {
            _status = $"{entry.Name} hasn't been ported to the desktop client yet.";
            _result = null;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(HasResult));
            return;
        }
        _game = game;
        _game.Reset();
        _isRealTime = _game.IsRealTime;
        _cells = new ObservableCollection<GameCell>(_game.Cells);
        Attempt++;
        // Cells is a new instance per game, so the view has to be told.
        OnPropertyChanged(nameof(Cells));
        Refresh();
        GameReady?.Invoke();
    }

    /// <summary>
    /// Re-reads the board and status from the game. Called after every input; the
    /// view also calls it on a timer while a real-time game is running.
    /// </summary>
    public void Refresh()
    {
        if (!InGame)
        {
            _status = "";
            _result = null;
            _isRealTime = false;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(Result));
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(InGame));
            OnPropertyChanged(nameof(NotInGame));
            OnPropertyChanged(nameof(GameName));
            OnPropertyChanged(nameof(BoardWidth));
            OnPropertyChanged(nameof(BoardHeight));
            OnPropertyChanged(nameof(IsRealTime));
            OnPropertyChanged(nameof(NeedsTicks));
            OnPropertyChanged(nameof(CanSubmitMastermind));
            OnPropertyChanged(nameof(ShowConnect4Tools));
            OnPropertyChanged(nameof(ShowLobbyTools));
            OnPropertyChanged(nameof(ShowDotsBoxesTools));
            OnPropertyChanged(nameof(IsInDotsBoxesOnline));
            OnPropertyChanged(nameof(IsDotsBoxesDebug));
            OnPropertyChanged(nameof(ShowReversiTools));
            OnPropertyChanged(nameof(IsInReversiOnline));
            OnPropertyChanged(nameof(IsReversiDebug));
            OnPropertyChanged(nameof(IsInConnect4Online));
            OnPropertyChanged(nameof(IsConnect4Debug));
            OnPropertyChanged(nameof(HasOnlineBoard));
            OnPropertyChanged(nameof(OnlineBoardColumns));
            OnPropertyChanged(nameof(OnlineBoardRows));
            return;
        }

        // A live Connect 4 match owns the board; otherwise the ported game does.
        // ShowConnect4 has already filled Status/Result for the relay case.
        if (_game != null)
        {
            _cells = new ObservableCollection<GameCell>(_game.Cells);
            _status = _game.StatusText;
            _result = _game.ResultText;
            _isRealTime = _game.IsRealTime;
        }
        else if (_connect4 != null)
        {
            _cells = new ObservableCollection<GameCell>(_connect4.Grid.Select((_, i) => new GameCell
            {
                Text = _connect4.Grid[i] == C4Rules.Empty ? "" : "●",
                Background = _connect4.Grid[i] == C4Rules.Empty ? CellSurface : _connect4.Grid[i] == C4Rules.Red ? PieceRed : PieceYellow,
                Foreground = _connect4.Grid[i] == C4Rules.Empty ? MutedInk : PieceInk,
                IsEnabled = _connect4.IsMyTurn && _connect4.Grid[i] == C4Rules.Empty,
                FontSize = 26,
                Bold = true,
            }));
        }
        CanGoLeft = CanGoRight = CanGoUp = CanGoDown = _isRealTime;

        OnPropertyChanged(nameof(Cells));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(InGame));
        OnPropertyChanged(nameof(NotInGame));
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(BoardWidth));
        OnPropertyChanged(nameof(BoardHeight));
        OnPropertyChanged(nameof(IsRealTime));
        OnPropertyChanged(nameof(NeedsTicks));
        OnPropertyChanged(nameof(CanSubmitMastermind));
        OnPropertyChanged(nameof(ShowConnect4Tools));
        OnPropertyChanged(nameof(ShowLobbyTools));
        OnPropertyChanged(nameof(ShowDotsBoxesTools));
        OnPropertyChanged(nameof(IsInDotsBoxesOnline));
        OnPropertyChanged(nameof(HasOnlineBoard));
        OnPropertyChanged(nameof(OnlineBoardColumns));
        OnPropertyChanged(nameof(OnlineBoardRows));
        OnPropertyChanged(nameof(ShowReversiTools));
        OnPropertyChanged(nameof(IsInReversiOnline));
        OnPropertyChanged(nameof(HasOnlineBoard));
        OnPropertyChanged(nameof(OnlineBoardColumns));
        OnPropertyChanged(nameof(OnlineBoardRows));
        OnPropertyChanged(nameof(IsReversiDebug));
        OnPropertyChanged(nameof(ShowDotsBoxesTools));
        OnPropertyChanged(nameof(IsDotsBoxesDebug));
        OnPropertyChanged(nameof(ScoreCounts));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(CanRetryScore));

        // Submit only after the game has produced its terminal result. The game
        // slug and score are the same values the leaderboard endpoint expects.
        _ = SubmitScoreIfNeededAsync();
    }

    /// <summary>Advances a real-time game. Called by the view's clock.</summary>
    public void Tick(TimeSpan elapsed)
    {
        if (_game == null || !_game.NeedsTicks) return;
        if (_game.Tick(elapsed)) Refresh();
    }

    /// <summary>Posts the finished game's score to kh_scores, at most once per attempt.</summary>
    public async Task SubmitScoreIfNeededAsync()
    {
        var game = _game;
        int attempt = Attempt;
        if (game is null || !game.ScoreCounts || string.IsNullOrEmpty(game.ResultText)) return;
        if (!_core.IsAuthenticated && !_scoreSubmitWasInjected) return;
        if (_scoreSubmittedAttempt == attempt || _scoreSubmittingAttempt == attempt) return;

        _scoreSubmittingAttempt = attempt;
        _scoreFailedAttempt = -1;
        OnPropertyChanged(nameof(CanRetryScore));
        RetryScoreCommand.RaiseCanExecuteChanged();
        try
        {
            bool posted = await _submitScore(game.Slug, game.Score, CancellationToken.None);
            if (!posted) throw new InvalidOperationException("The leaderboard did not accept the score.");
            _scoreSubmittedAttempt = attempt;
            if (Attempt == attempt && ReferenceEquals(_game, game))
            {
                _status = $"Score {game.Score} submitted to the {game.Name} leaderboard.";
                OnPropertyChanged(nameof(Status));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Score submit failed for {Slug}", game.Slug);
            _scoreFailedAttempt = attempt;
            if (Attempt == attempt && ReferenceEquals(_game, game))
            {
                _status = "Couldn't submit the score. You can retry while signed in.";
                OnPropertyChanged(nameof(Status));
            }
        }
        finally
        {
            if (_scoreSubmittingAttempt == attempt) _scoreSubmittingAttempt = -1;
            OnPropertyChanged(nameof(CanRetryScore));
            RetryScoreCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Exposed so the view can show the score once, then stop asking.</summary>
    public bool ScoreCounts => _game?.ScoreCounts ?? false;
    public int Score => _game?.Score ?? 0;
    public bool CanRetryScore => (_core.IsAuthenticated || _scoreSubmitWasInjected)
        && _game is { ScoreCounts: true, ResultText: not null }
        && _scoreFailedAttempt == Attempt
        && _scoreSubmittedAttempt != Attempt
        && _scoreSubmittingAttempt != Attempt;

    // Only used by the headless gametest to record the same operation without
    // making a live authenticated request.
}
