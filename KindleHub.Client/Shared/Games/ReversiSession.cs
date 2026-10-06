using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.Games;

/// <summary>Reversi multiplayer session following the web client's ST message
/// format (flat 64-character board, turn number, last move, game-over flag).</summary>
public sealed class ReversiSession : IDisposable
{
    public const string GameSlug = "reversi";
    private readonly KindleHubCore _core;
    private readonly ILogger _logger;
    private Timer? _timer;
    private ReversiSession? _debugPeer;
    private long _lastEventMs;
    private int _polling;
    private bool _disposed;

    public ReversiGame Game { get; } = new();
    public string? RoomCode { get; private set; }
    public bool IsHost { get; private set; }
    public bool IsOfflineDebug { get; private set; }
    public int MySide { get; private set; }
    public string OpponentName { get; private set; } = "";
    public string Status { get; private set; } = "";
    public event Action? Changed;

    private ReversiSession(KindleHubCore core, ILogger logger)
    { _core = core; _logger = logger; }

    public bool IsMyTurn => Game.IsOnlineTurn;

    public static async Task<ReversiSession> HostAsync(KindleHubCore core, ILogger logger)
    {
        var session = new ReversiSession(core, logger)
        {
            RoomCode = await core.OpenGameRoomAsync(GameSlug, CancellationToken.None),
            IsHost = true, MySide = 1, Status = "Waiting for an opponent. You play dark first."
        };
        session.Game.ConfigureOnline(session.MySide);
        session._lastEventMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await session.SendLobbyOpenAsync();
        session.StartPolling();
        return session;
    }

    public static async Task<ReversiSession> JoinAsync(KindleHubCore core, ILogger logger, string code)
    {
        var session = new ReversiSession(core, logger)
        {
            RoomCode = await core.JoinGameRoomAsync(code, GameSlug, CancellationToken.None),
            IsHost = false, MySide = 2, Status = "Joined — waiting for the board. You play light."
        };
        session.Game.ConfigureOnline(session.MySide);
        session._lastEventMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await core.SendGameEventAsync(session.RoomCode!, new { type = "JOIN", game = GameSlug }, CancellationToken.None);
        session.StartPolling();
        return session;
    }

    public static (ReversiSession You, ReversiSession Opponent) CreateOfflinePair(KindleHubCore core, ILogger logger)
    {
        var you = new ReversiSession(core, logger) { IsHost = true, IsOfflineDebug = true, MySide = 1, Status = "Offline debug — you play dark." };
        var foe = new ReversiSession(core, logger) { IsOfflineDebug = true, MySide = 2, Status = "Offline debug — opponent plays light." };
        you.Game.ConfigureOnline(1); foe.Game.ConfigureOnline(2);
        you._debugPeer = foe; foe._debugPeer = you;
        you.OpponentName = "Debug opponent"; foe.OpponentName = "You";
        return (you, foe);
    }

    public async Task<bool> PlayAtAsync(int index)
    {
        if (!IsMyTurn || index < 0 || index >= ReversiGame.Side * ReversiGame.Side) return false;
        int x = index % ReversiGame.Side, y = index / ReversiGame.Side;
        if (!Game.LegalMoves(Game.CurrentTurn).Contains((x, y)) || !Game.OnTap(index)) return false;
        UpdateStatus();
        Changed?.Invoke();
        await SendStateAsync(index);
        return true;
    }

    public async Task<bool> DebugOpponentMoveAsync()
    {
        if (!IsOfflineDebug || _debugPeer is null || !_debugPeer.IsMyTurn) return false;
        var moves = _debugPeer.Game.LegalMoves(_debugPeer.Game.CurrentTurn);
        if (moves.Count == 0) return false;
        var move = moves[0];
        return await _debugPeer.PlayAtAsync(move.Y * ReversiGame.Side + move.X);
    }

    private async Task SendStateAsync(int lastMove)
    {
        try { await SendAsync(new
        {
            type = "ST", t = "ST", b = Game.BoardState, tn = Game.CurrentTurn,
            lm = lastMove, ov = Game.IsFinished
        }); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reversi state send failed");
            Status = "Move shown locally; relay send failed. Check your connection.";
            Changed?.Invoke();
        }
    }

    private async Task SendAsync(object payload)
    {
        if (_debugPeer is { } peer)
        {
            await Task.Delay(80);
            peer.HandleLoopback(payload);
            return;
        }
        if (RoomCode is not { } code) return;
        await _core.SendGameEventAsync(code, payload, CancellationToken.None);
    }

    private async Task SendLobbyOpenAsync()
    {
        if (RoomCode is not { } room) return;
        try
        {
            await _core.SendGameEventAsync(KindleHubCore.OpenGamesLobby, new
            {
                type = "OPEN", game = GameSlug, roomShort = room.Length > 6 ? room[^6..] : room,
                host = _core.CurrentProfile?.DisplayName ?? "Reader",
                hostUserId = _core.CurrentProfile?.UserId ?? "",
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, CancellationToken.None);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Reversi lobby announce failed"); }
    }

    private void StartPolling() => _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(2.5));

    private async Task PollAsync()
    {
        if (_disposed || RoomCode is null || Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            foreach (var ev in await _core.PollGameEventsAsync(RoomCode, CancellationToken.None))
            {
                long stamp = ev.Message.Timestamp.ToUnixTimeMilliseconds();
                if (stamp <= _lastEventMs) continue;
                _lastEventMs = stamp;
                if (!ev.Message.IsMine) Handle(ev);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Reversi relay poll failed"); }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    private void Handle(RoomMessageEnvelope ev)
    {
        try
        {
            string type = string.IsNullOrEmpty(ev.Type) ? ev.Str("t") : ev.Type;
            if (type == "JOIN" && IsHost)
            {
                OpponentName = string.IsNullOrWhiteSpace(ev.Str("name")) ? "Opponent" : ev.Str("name");
                Status = $"{OpponentName} joined.";
                _ = SendStateAsync(-1);
            }
            else if (type == "ST" && ev.Data is { } data)
            {
                string board = ev.Str("b");
                int turn = (int)ev.Number("tn");
                bool over = data.RootElement.TryGetProperty("ov", out var ov) && ov.ValueKind == System.Text.Json.JsonValueKind.True;
                if (Game.LoadOnlineState(board, turn, over)) UpdateStatus();
            }
            Changed?.Invoke();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Invalid Reversi relay event ignored"); }
    }

    private void HandleLoopback(object payload)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(payload));
        var root = doc.RootElement;
        bool over = root.TryGetProperty("ov", out var ov) && ov.ValueKind == System.Text.Json.JsonValueKind.True;
        if (Game.LoadOnlineState(root.GetProperty("b").GetString() ?? "", root.GetProperty("tn").GetInt32(), over))
        { UpdateStatus(); Changed?.Invoke(); }
    }

    private void UpdateStatus() => Status = Game.IsFinished ? "Game over." : IsMyTurn ? "Your move." : "Opponent's move.";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose(); _timer = null;
    }
}
