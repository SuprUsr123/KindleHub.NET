using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.Games;

/// <summary>Desktop Dots & Boxes session using the web client's DB_INIT/DB_MOVE
/// relay messages. The debug pair uses this same event handler in a loopback so
/// joins, state sync, turn changes, and scoring can be exercised offline.</summary>
public sealed class DotsBoxesSession : IDisposable
{
    public const string GameSlug = "dotsboxes";
    private readonly KindleHubCore _core;
    private readonly ILogger _logger;
    private Timer? _timer;
    private DotsBoxesSession? _debugPeer;
    private long _lastEventMs;
    private int _polling;
    private bool _disposed;

    public DotsBoxesGame Game { get; } = new();
    public string? RoomCode { get; private set; }
    public bool IsHost { get; private set; }
    public bool IsOfflineDebug { get; private set; }
    public int MyPlayer { get; private set; }
    public string OpponentName { get; private set; } = "";
    public string Status { get; private set; } = "";
    public event Action? Changed;

    private DotsBoxesSession(KindleHubCore core, ILogger logger)
    { _core = core; _logger = logger; }

    public bool IsMyTurn => Game.CurrentPlayer == MyPlayer && !Game.IsFinished;

    public static async Task<DotsBoxesSession> HostAsync(KindleHubCore core, ILogger logger)
    {
        var session = new DotsBoxesSession(core, logger)
        {
            RoomCode = await core.OpenGameRoomAsync(GameSlug, CancellationToken.None),
            IsHost = true,
            MyPlayer = 1,
            Status = "Waiting for an opponent. You are Player 1."
        };
        session._lastEventMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await session.SendLobbyOpenAsync();
        session.StartPolling();
        return session;
    }

    public static async Task<DotsBoxesSession> JoinAsync(KindleHubCore core, ILogger logger, string code)
    {
        var session = new DotsBoxesSession(core, logger)
        {
            RoomCode = await core.JoinGameRoomAsync(code, GameSlug, CancellationToken.None),
            IsHost = false,
            MyPlayer = 2,
            Status = "Joined — waiting for the host's board."
        };
        session._lastEventMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await core.SendGameEventAsync(session.RoomCode!, new { type = "JOIN", game = GameSlug }, CancellationToken.None);
        session.StartPolling();
        return session;
    }

    public static (DotsBoxesSession You, DotsBoxesSession Opponent) CreateOfflinePair(KindleHubCore core, ILogger logger)
    {
        var you = new DotsBoxesSession(core, logger) { IsHost = true, IsOfflineDebug = true, MyPlayer = 1, Status = "Offline debug — you are Player 1." };
        var foe = new DotsBoxesSession(core, logger) { IsOfflineDebug = true, MyPlayer = 2, Status = "Offline debug — opponent is Player 2." };
        you._debugPeer = foe;
        foe._debugPeer = you;
        you.OpponentName = "Debug opponent";
        foe.OpponentName = "You";
        return (you, foe);
    }

    public async Task<bool> PlayAtAsync(int cellIndex)
    {
        if (!IsMyTurn || !Game.TryGetEdge(cellIndex, out char kind, out int row, out int col)) return false;
        if (!Game.TryPlayEdge(kind, row, col)) return false;
        Status = Game.IsFinished ? "Game over." : Game.CurrentPlayer == MyPlayer ? "You completed a box — play again." : "Opponent's turn.";
        Changed?.Invoke();
        await SendAsync(new { type = "DB_MOVE", kind = kind.ToString(), r = row, c = col });
        return true;
    }

    public async Task<bool> DebugOpponentMoveAsync()
    {
        if (!IsOfflineDebug || _debugPeer is null || !_debugPeer.IsMyTurn
            || !_debugPeer.Game.TryGetDebugMove(out char kind, out int row, out int col)) return false;
        return await _debugPeer.PlayEdgeAsync(kind, row, col);
    }

    private async Task<bool> PlayEdgeAsync(char kind, int row, int col)
    {
        if (!IsMyTurn || !Game.TryPlayEdge(kind, row, col)) return false;
        Status = Game.IsFinished ? "Game over." : Game.CurrentPlayer == MyPlayer ? "You completed a box — play again." : "Opponent's turn.";
        Changed?.Invoke();
        await SendAsync(new { type = "DB_MOVE", kind = kind.ToString(), r = row, c = col });
        return true;
    }

    private async Task SendAsync(object payload)
    {
        if (_debugPeer is { } peer)
        {
            await Task.Delay(80);
            peer.Handle(Text(payload));
            return;
        }
        if (RoomCode is not { } code) return;
        try { await _core.SendGameEventAsync(code, payload, CancellationToken.None); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dots & Boxes move send failed");
            Status = "Move shown locally; relay send failed. Check your connection.";
            Changed?.Invoke();
        }
    }

    private async Task SendInitStateAsync()
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Game.CreateRelayState()));
        var root = doc.RootElement;
        try { await SendAsync(new
        {
            type = "DB_INIT", grid = 5,
            hLines = root.GetProperty("hLines").Clone(),
            vLines = root.GetProperty("vLines").Clone(),
            boxes = root.GetProperty("boxes").Clone(),
            turn = root.GetProperty("turn").Clone(),
            scores = new Dictionary<string, int> { ["1"] = Game.PlayerOneScore, ["2"] = Game.PlayerTwoScore }
        }); }
        catch (Exception ex) { _logger.LogWarning(ex, "Dots & Boxes state sync failed"); }
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
        catch (Exception ex) { _logger.LogDebug(ex, "Dots & Boxes lobby announce failed"); }
    }

    private void StartPolling()
    {
        _timer?.Dispose();
        _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(2.5));
    }

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
                if (ev.Message.IsMine) continue;
                Handle(ev);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Dots & Boxes relay poll failed"); }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    private void Handle(RoomMessageEnvelope ev)
    {
        try
        {
            var data = ev.Data?.RootElement;
            switch (ev.Type)
            {
                case "JOIN" when IsHost:
                    OpponentName = ev.Str("name") is { Length: > 0 } name ? name : "Opponent";
                    Status = $"{OpponentName} joined. Your move.";
                    _ = SendInitStateAsync();
                    break;
                case "DB_INIT" when !IsHost && data.HasValue:
                    if (Game.LoadRelayState(data.Value)) Status = IsMyTurn ? "Your move." : "Opponent's move.";
                    break;
                case "DB_MOVE":
                    string kind = ev.Str("kind");
                    long r = ev.Number("r"), c = ev.Number("c");
                    if ((kind is "h" or "v") && r is >= 0 and <= 4 && c is >= 0 and <= 4
                        && Game.TryPlayEdge(kind[0], (int)r, (int)c))
                        Status = Game.IsFinished ? "Game over." : Game.CurrentPlayer == MyPlayer ? "Your move." : "Opponent's move.";
                    break;
            }
            Changed?.Invoke();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Invalid Dots & Boxes relay event ignored"); }
    }

    private void Handle((string Type, string Kind, int Row, int Col, string Name) message)
    {
        if (message.Type == "DB_MOVE")
        {
            if (Game.TryPlayEdge(message.Kind[0], message.Row, message.Col))
            {
                Status = Game.IsFinished ? "Game over." : Game.CurrentPlayer == MyPlayer ? "Your move." : "Opponent's turn.";
                Changed?.Invoke();
            }
        }
    }

    private static (string Type, string Kind, int Row, int Col, string Name) Text(object payload)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;
        return (root.GetProperty("type").GetString() ?? "", root.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "" : "",
            root.TryGetProperty("r", out var r) ? r.GetInt32() : 0, root.TryGetProperty("c", out var c) ? c.GetInt32() : 0,
            root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
