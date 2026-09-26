using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

/// <summary>
/// Games: multiplayer Tic-Tac-Toe over the official encrypted relay (open-games lobby
/// room 800000777777 announces OPEN events; each game room is 900000&lt;6 digits&gt; and
/// carries MOVE_TTT / TTT_STATE / JOIN envelopes; wire format matches kh-games.js so
/// desktop and Kindle players interoperate), plus a local vs-computer board whose wins
/// post to the shared global leaderboard ("ttt").
/// </summary>
public class GamesViewModel : ViewModelBase, IDisposable
{
    private readonly KindleHubCore _core;
    private readonly ILogger<GamesViewModel> _logger;
    private Timer? _pollTimer;
    private bool _disposed;
    private int _polling;

    private ObservableCollection<OpenGameListing> _openGames = new();
    private ObservableCollection<string> _knownGames = new();
    private string _hostGameName = "ttt";
    private string _joinRoomShort = "";
    private bool _isLoading;
    private string _statusText = "";
    private OpenGameListing? _selectedOpen;

    // Active match
    private string? _roomCode;
    private bool _isHost;
    private char _myPiece = 'X';
    private char _turn = 'X';
    private bool _matchLive;
    private string _oppName = "";
    private string _resultText = "";
    private string _matchTitle = "";
    private long _lastEventMs;
    private readonly string?[] _cells = new string?[9];
    private LocalGame? _local;

    public ObservableCollection<OpenGameListing> OpenGames { get => _openGames; set => SetProperty(ref _openGames, value); }
    public ObservableCollection<string> KnownGames { get => _knownGames; set => SetProperty(ref _knownGames, value); }
    public string HostGameName { get => _hostGameName; set => SetProperty(ref _hostGameName, value); }
    public string JoinRoomShort { get => _joinRoomShort; set { if (SetProperty(ref _joinRoomShort, value)) JoinCommand.RaiseCanExecuteChanged(); } }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public OpenGameListing? SelectedOpen
    {
        get => _selectedOpen;
        set
        {
            if (SetProperty(ref _selectedOpen, value))
            {
                if (value != null) JoinRoomShort = value.RoomShort;
                JoinCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<TttCell> Cells { get; } = new();

    public bool InMatch { get => _matchLive; private set { _matchLive = value; OnPropertyChanged(); OnPropertyChanged(nameof(NotInMatch)); } }
    public bool NotInMatch => !_matchLive;
    public string MatchTitle { get => _matchTitle; private set { _matchTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(InMatch)); } }
    public char MyPiece => _myPiece;
    public string YouAreLabel { get => _local != null ? "You are X" : _matchLive ? $"You are {_myPiece}" : ""; }
    public string TurnLabel
    {
        get
        {
            if (_local is { Done: true }) return "Game over — New game to play again.";
            if (_local is { Done: false }) return _local.Turn == 'X' ? "Your move" : "Computer is thinking…";
            if (!_matchLive) return "";
            if (!string.IsNullOrEmpty(_resultText)) return _resultText;
            return _turn == _myPiece ? "Your move" : $"{(_oppName.Length > 0 ? _oppName : "Opponent")} to move";
        }
    }
    public string ResultText
    {
        get => _resultText;
        private set { _resultText = value; OnPropertyChanged(); OnPropertyChanged(nameof(TurnLabel)); }
    }
    public string JoinDisplay { get => _roomCode ?? ""; }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand HostCommand { get; }
    public RelayCommand JoinCommand { get; }
    public RelayCommand LeaveCommand { get; }
    public RelayCommand NewLocalGameCommand { get; }
    public RelayCommand<TttCell> CellCommand { get; }

    public GamesViewModel(KindleHubCore core, ILogger<GamesViewModel> logger)
    {
        _core = core;
        _logger = logger;
        for (int i = 0; i < 9; i++) Cells.Add(new TttCell(i));

        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        HostCommand = new RelayCommand(async () => await HostOnlineAsync(), () => _core.IsAuthenticated && !_matchLive && _local == null);
        JoinCommand = new RelayCommand(async () => await JoinOnlineAsync(),
            () => _core.IsAuthenticated && !_matchLive && _local == null && JoinRoomShort.Trim().Length > 0);
        LeaveCommand = new RelayCommand(async () => await LeaveAsync(), () => _matchLive);
        NewLocalGameCommand = new RelayCommand(() => StartLocalGame(), () => !_matchLive);
        CellCommand = new RelayCommand<TttCell>(async cell => await OnCellAsync(cell), _ => !_matchLive || true);

        _ = RefreshAsync();
    }

    // ─── Lobby ──────────────────────────────────────────────────────────────
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var games = await _core.ListKnownGamesAsync(CancellationToken.None);
            _knownGames.Clear();
            foreach (var g in games) _knownGames.Add(g);

            var lobby = await _core.PollLobbyForOpenGamesAsync("ttt", CancellationToken.None);
            _openGames.Clear();
            foreach (var o in lobby) _openGames.Add(o);

            StatusText = lobby.Count > 0
                ? $"{lobby.Count} open tic-tac-toe room(s). Select one and Join, or Host your own."
                : "No open rooms right now. Host one and share the code (or just play the computer).";

            // A light presence heartbeat so "online now" lists see us.
            if (_core.IsAuthenticated)
                await _core.PingPresenceAsync(KindleHubCore.OpenGamesLobby, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lobby refresh failed");
            StatusText = "Couldn't reach the lobby.";
        }
        finally { IsLoading = false; }
    }

    // ─── Online host / join ─────────────────────────────────────────────────
    public async Task HostOnlineAsync()
    {
        if (!_core.IsAuthenticated) { StatusText = "Sign in to host a game."; return; }
        LeaveLocalGame();
        try
        {
            _roomCode = await _core.OpenGameRoomAsync("ttt", CancellationToken.None);
            _isHost = true;
            _myPiece = 'X';
            _turn = 'X';
            _oppName = "";
            _lastEventMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000; // ignore backlog on join
            ResetGrid();
            InMatch = true;
            MatchTitle = $"You are hosting  ·  room {_roomCode} ({RoomShort(_roomCode)})";
            ResultText = "";
            AnnounceOpen();
            StartPoll();
            UpdateCommands();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Host failed");
            StatusText = "Couldn't host a room.";
        }
    }

    private async void AnnounceOpen()
    {
        if (_roomCode == null) return;
        try
        {
            await _core.SendGameEventAsync(KindleHubCore.OpenGamesLobby, new
            {
                type = "OPEN",
                game = "ttt",
                roomShort = RoomShort(_roomCode),
                host = _core.CurrentProfile?.DisplayName ?? "Reader",
                hostUserId = _core.CurrentProfile?.UserId ?? "",
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lobby announce failed (room still usable via code)");
        }
    }

    public async Task JoinOnlineAsync()
    {
        if (!_core.IsAuthenticated) { StatusText = "Sign in to join a game."; return; }
        LeaveLocalGame();
        var shortCode = new string((JoinRoomShort ?? "").Where(char.IsDigit).ToArray());
        if (shortCode.Length == 0) return;
        if (shortCode.Length != 6) shortCode = shortCode.PadLeft(6, '0')[..6];
        try
        {
            _roomCode = await _core.JoinGameRoomAsync(shortCode, "ttt", CancellationToken.None);
            _isHost = false;
            _myPiece = 'O';
            _turn = 'X';
            _oppName = SelectedOpen?.Host ?? "";
            _lastEventMs = 0;
            ResetGrid();
            InMatch = true;
            MatchTitle = $"Joined {_roomCode}  ·  you are O";
            ResultText = "";
            await _core.SendGameEventAsync(_roomCode, new { type = "JOIN", game = "ttt" }, CancellationToken.None);
            StartPoll();
            UpdateCommands();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Join failed");
            StatusText = "Couldn't join that room.";
        }
    }

    public async Task LeaveAsync()
    {
        StopPoll();
        InMatch = false;
        _roomCode = null;
        MatchTitle = "";
        ResultText = "";
        UpdateCommands();
        await Task.CompletedTask;
    }

    // ─── Polling loop (KH_MP.subscribe equivalent) ──────────────────────────
    private void StartPoll()
    {
        StopPoll();
        _pollTimer = new Timer(_ => _ = PollTickAsync(), null,
            TimeSpan.FromMilliseconds(2500), TimeSpan.FromMilliseconds(2500));
    }

    private void StopPoll()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private async Task PollTickAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var code = _roomCode;
            if (code == null || !InMatch) return;
            var events = await _core.PollGameEventsAsync(code, CancellationToken.None);
            foreach (var ev in events)
            {
                var ts = ev.Message.Timestamp.ToUnixTimeMilliseconds();
                if (ts <= _lastEventMs) continue;
                _lastEventMs = ts;
                if (ev.Message.IsMine && ev.Type != "OPEN") continue; // our own echo
                if (ts > _lastEventMs) { } // (guard keeps newest only; harmless)
                await HandleRemoteEventAsync(ev);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "game poll failed");
        }
        finally { _polling = 0; }

        // keep the ts gate monotonic after the loop advances it
        Interlocked.MemoryBarrier();
    }

    private async Task HandleRemoteEventAsync(RoomMessageEnvelope ev)
    {
        switch (ev.Type)
        {
            case "JOIN":
                if (_isHost)
                {
                    _oppName = ev.Data != null && ev.Data.RootElement.TryGetProperty("name", out var nm) && nm.ValueKind == System.Text.Json.JsonValueKind.String
                        ? nm.GetString() ?? "" : "Opponent";
                    if (string.IsNullOrEmpty(_oppName)) _oppName = ev.Message.DisplayName ?? "Opponent";
                    ResultText = $"{_oppName} joined. You are X — make the first move.";
                    // Send them the board state so a mid-match joiner can sync.
                    try
                    {
                        await _core.SendGameEventAsync(_roomCode!, new
                        {
                            type = "TTT_STATE",
                            board = _cells.Select(c => c ?? "").ToArray(),
                            currentTurn = _turn.ToString(),
                            active = string.IsNullOrEmpty(ResultText) || !ResultText.Contains("joined") ? true : true
                        }, CancellationToken.None);
                    }
                    catch { }
                    OnPropertyChanged(nameof(TurnLabel));
                }
                break;

            case "TTT_STATE":
                if (!_isHost && !_localActive)
                {
                    var arr = ev.Array("board");
                    for (int i = 0; i < 9 && i < arr.Count; i++)
                    {
                        var val = arr[i];
                        _cells[i] = string.IsNullOrEmpty(val) ? null : val;
                        Cells[i].Value = _cells[i];
                    }
                    var t = ev.Str("currentTurn");
                    if (t == "X" || t == "O") _turn = t[0];
                    OnPropertyChanged(nameof(TurnLabel));
                }
                break;

            case "MOVE_TTT":
                if (_localActive) break;
                var idx = (int)ev.Number("idx");
                var piece = ev.Str("piece");
                if (idx is >= 0 and <= 8 && (piece == "X" || piece == "O") && _cells[idx] == null)
                {
                    _cells[idx] = piece;
                    Cells[idx].Value = piece;
                    _turn = piece == "X" ? 'O' : 'X';
                    OnPropertyChanged(nameof(TurnLabel));
                    var (winner, line) = TttJudge.Evaluate(_cells);
                    if (winner != null)
                    {
                        foreach (var i in line) Cells[i].IsWinning = true;
                        ResultText = winner.Value == _myPiece ? "You win! 🏆" : "You lost.";
                        PostWinScoreIfNeeded(local: false, won: winner.Value == _myPiece, moves: 9);
                    }
                    else if (_cells.All(c => c != null))
                    {
                        ResultText = "Draw.";
                    }
                }
                break;
        }
    }

    // ─── Moves ──────────────────────────────────────────────────────────────
    public async Task OnCellAsync(TttCell? cell)
    {
        if (cell == null) return;
        await HandleCellMove(cell.Index);
    }

    /// <summary>Board click dispatched from code-behind with the 0-8 index.</summary>
    public async Task CellClickedAsync(int index)
    {
        if (index < 0 || index > 8) return;
        await HandleCellMove(index);
    }

    private async Task HandleCellMove(int i)
    {
        if (_localActive) { PlayLocal(i); return; }
        if (!InMatch || _roomCode == null) return;
        if (_cells[i] != null) return;
        if (_turn != _myPiece || !string.IsNullOrEmpty(ResultText)) return;

        _cells[i] = _myPiece.ToString();
        Cells[i].Value = _myPiece.ToString();
        var (winner, line) = TttJudge.Evaluate(_cells);
        if (winner != null)
        {
            foreach (var x in line) Cells[x].IsWinning = true;
            ResultText = "You win!";
            OnPropertyChanged(nameof(TurnLabel));
            return;
        }
        if (_cells.All(c => c != null)) { ResultText = "Draw."; OnPropertyChanged(nameof(TurnLabel)); return; }
        _turn = _myPiece == 'X' ? 'O' : 'X';
        OnPropertyChanged(nameof(TurnLabel));
        try
        {
            await _core.SendGameEventAsync(_roomCode, new { type = "MOVE_TTT", idx = i, piece = _myPiece.ToString() }, CancellationToken.None);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "move send failed"); }
    }

    // ─── Local game vs computer ─────────────────────────────────────────────
    private bool _localActive => _local != null;

    public void StartLocalGame()
    {
        LeaveLocalGame();
        StopPoll();
        InMatch = false;
        _roomCode = null;
        ResetGrid();
        ResultText = "Beat the computer to post a global score. You are X, first move.";
        _local = new LocalGame { Turn = 'X', Moves = 0, Done = false };
        UpdateCommands();
        OnPropertyChanged(nameof(TurnLabel));
        OnPropertyChanged(nameof(YouAreLabel));
    }

    private void LeaveLocalGame()
    {
        if (_local == null) return;
        _local = null;
        UpdateCommands();
    }

    private void PlayLocal(int i)
    {
        if (_local is not { Done: false } g || _roomCode != null) return;
        if (!g.MyTurn || _cells[i] != null) return;

        _cells[i] = "X";
        Cells[i].Value = "X";
        g.Moves++;
        var (w1, l1) = TttJudge.Evaluate(_cells);
        if (w1 != null) { EndLocal(g, l1, winner: "X"); return; }
        if (_cells.All(c => c != null)) { EndLocal(g, Array.Empty<int>(), winner: null); return; }

        g.Turn = 'O';
        OnPropertyChanged(nameof(TurnLabel));
        var reply = ComputerChoose(_cells);
        Task.Delay(700).ContinueWith(_ =>
        {
            if (_local != g || g.Done) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_local != g || g.Done) return;
                _cells[reply] = "O";
                Cells[reply].Value = "O";
                g.Moves++;
                var (w2, l2) = TttJudge.Evaluate(_cells);
                if (w2 != null) { EndLocal(g, l2, winner: "O"); return; }
                if (_cells.All(c => c != null)) { EndLocal(g, Array.Empty<int>(), winner: null); return; }
                g.Turn = 'X';
                OnPropertyChanged(nameof(TurnLabel));
            });
        });
    }

    private void EndLocal(LocalGame g, int[] line, string? winner)
    {
        foreach (var x in line) Cells[x].IsWinning = true;
        g.Done = true;
        var won = winner == "X";
        var lost = winner == "O";
        ResultText = won ? "You win!" : lost ? "The computer wins — try again to post a score." : "Draw.";
        OnPropertyChanged(nameof(TurnLabel));
        PostWinScoreIfNeeded(local: true, won, moves: g.Moves);
        UpdateCommands();
    }

    /// <summary>win/block/center/corner heuristic matching a simple web rival (kh_ai move style).</summary>
    private static int ComputerChoose(string?[] cells)
    {
        var cs = cells.Select(c => c ?? "").ToArray();
        for (int i = 0; i < 9; i++)
        {
            if (cs[i] != "") continue;
            var trial = (string[])cs.Clone();
            // block player win
            trial[i] = "X";
            if (TttJudge.Evaluate(trial).winner != null) { trial[i] = "O"; return i; }
            // win if possible
            trial[i] = "O";
            if (TttJudge.Evaluate(trial).winner != null) return i;
        }
        if (cs[4] == "") return 4;
        foreach (var c in new[] { 0, 2, 6, 8 })
            if (cs[c] == "") return c;
        var sfree = new List<int>();
        for (int i = 0; i < 9; i++) if (cs[i] == "") sfree.Add(i);
        if (sfree.Count == 0) return -1;
        return sfree[Random.Shared.Next(sfree.Count)];
    }

    private async void PostWinScoreIfNeeded(bool local, bool won, int moves)
    {
        if (!local || !won) return;
        if (!_core.IsAuthenticated) return;
        try
        {
            var s = Math.Clamp(9 - moves + 1, 1, 9);
            await _core.SubmitScoreAsync("ttt", 9 + (9 - moves), CancellationToken.None);
            StatusText = $"Score {9 + (9 - moves)} submitted to the Tic-Tac-Toe leaderboard.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Leaderboard submit failed");
            StatusText = "Couldn't reach the leaderboard.";
        }
    }

    // ─── helpers ────────────────────────────────────────────────────────────
    private static string RoomShort(string roomCode) =>
        roomCode.Length >= 6 ? roomCode[^6..] : roomCode.PadLeft(6, '0');

    private void ResetGrid()
    {
        for (int i = 0; i < 9; i++) { _cells[i] = null; Cells[i].Value = null; Cells[i].IsWinning = false; }
        _turn = _myPiece;
    }

    private void UpdateCommands()
    {
        HostCommand.RaiseCanExecuteChanged();
        JoinCommand.RaiseCanExecuteChanged();
        LeaveCommand.RaiseCanExecuteChanged();
        NewLocalGameCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPoll();
    }

    private sealed class LocalGame
    {
        public char Turn = 'X';
        public int Moves;
        public bool Done;
        public bool MyTurn => Turn == 'X';
    }

    internal static class TttJudge
    {
        public static (char? winner, int[] line) Evaluate(string?[] cells)
            => EvaluateStrings(cells.Select(c => c ?? "").ToArray());

        public static (char? winner, int[] line) EvaluateStrings(string[] c)
        {
            int[][] lines = { new[]{0,1,2}, new[]{3,4,5}, new[]{6,7,8}, new[]{0,3,6}, new[]{1,4,7}, new[]{2,5,8}, new[]{0,4,8}, new[]{2,4,6} };
            foreach (var ln in lines)
            {
                var (a, b, d) = (c[ln[0]], c[ln[1]], c[ln[2]]);
                if (a != "" && a == b && b == d) return (a[0], ln);
            }
            return (null, Array.Empty<int>());
        }
    }
}
