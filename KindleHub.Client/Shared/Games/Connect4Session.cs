using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.Games;

/// <summary>
/// Multiplayer Connect 4 over the official encrypted relay, speaking the same wire
/// format as the web client so a Kindle and a desktop player can finish the same
/// game. That format, lifted from kh-app.js:
///
///   lobby  : OPEN { game:"connect4", roomShort, host, hostUserId, ts }
///   room   : JOIN { game:"connect4" }
///   host-&gt;guest on JOIN : C4_STATE { grid, p2Turn, active }
///   either side          : MOVE_C4 { col, piece }      (piece 1=Red, 2=Yellow)
///
/// The board is the flat <c>grid[row*7+col]</c> array from <see cref="C4Rules"/> and
/// <c>p2Turn</c> flips between "R" and "Y".
/// </summary>
public sealed class Connect4Session : IDisposable
{
    public const string GameSlug = "connect4";

    private readonly KindleHubCore _core;
    private readonly ILogger _logger;
    private readonly int[] _grid = new int[C4Rules.Cols * C4Rules.Rows];
    private Timer? _pollTimer;
    private int _polling;
    private long _lastEventMs;
    private bool _disposed;

    public string? RoomCode { get; private set; }
    public bool IsHost { get; private set; }
    public bool IsActive { get; private set; }
    public string MyPiece { get; private set; } = C4Rules.RedTurn;
    public string Turn { get; private set; } = C4Rules.RedTurn;
    public string OpponentName { get; private set; } = "";
    public string Status { get; private set; } = "";
    public string? Result { get; private set; }

    /// <summary>Raised whenever the board or the turn changes, so the view can redraw.</summary>
    public event Action? Changed;

    /// <summary>
    /// Offline debug link. When set, this session delivers its outbound protocol
    /// messages straight into <see cref="DebugPeer"/> instead of the relay, so the
    /// exact same join/state/move handling runs with no account and no server.
    /// Null in normal play.
    /// </summary>
    internal Connect4Session? DebugPeer { get; set; }

    /// <summary>True for a session created by <see cref="CreateOfflinePair"/>.</summary>
    public bool IsOfflineDebug { get; private init; }

    /// <summary>
    /// Builds a host/guest pair wired straight together, plus a simple opponent
    /// brain for the guest side. This exists so the whole Connect 4 protocol —
    /// JOIN, C4_STATE, MOVE_C4, turn passing, win detection and the end-of-game
    /// transitions — can be exercised and played by hand without touching the
    /// network. It is the same code path; only the transport is swapped.
    /// </summary>
    public static (Connect4Session You, Connect4Session Opponent) CreateOfflinePair(
        KindleHubCore core, ILogger logger)
    {
        var you = new Connect4Session(core, logger) { IsHost = true, MyPiece = C4Rules.RedTurn, Turn = C4Rules.RedTurn, IsActive = true, IsOfflineDebug = true, Status = "Offline debug — you are Red." };
        var foe = new Connect4Session(core, logger) { IsHost = false, MyPiece = C4Rules.YellowTurn, Turn = C4Rules.RedTurn, IsActive = true, IsOfflineDebug = true, Status = "Offline debug — the opponent is Yellow." };
        you.DebugPeer = foe;
        foe.DebugPeer = you;
        foe.OpponentName = "You";
        return (you, foe);
    }

    /// <summary>Delivers an outbound message to the debug peer, or to the relay.</summary>
    private async Task SendToPeerAsync(string type, object payload)
    {
        var peer = DebugPeer;
        if (peer is null)
        {
            // Real transport. Only reached when not in debug mode.
            if (RoomCode is null) return;
            await _core.SendGameEventAsync(RoomCode, payload, CancellationToken.None);
            return;
        }

        // Offline: pull the fields back out of the same anonymous object shape the
        // relay would have serialised, so both paths format events identically.
        var probe = System.Text.Json.JsonSerializer.SerializeToElement(payload);
        string typeName = Text(probe, "type") is { Length: > 0 } tn ? tn : type;
        int? col = Number(probe, "col");
        int? piece = Number(probe, "piece");
        int[]? grid = null;
        if (probe.TryGetProperty("grid", out var ge) && ge.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var list = new List<int>();
            foreach (var el in ge.EnumerateArray())
                list.Add(el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetInt32() : 0);
            grid = list.ToArray();
        }
        bool? active = probe.TryGetProperty("active", out var ae)
                       && ae.ValueKind == System.Text.Json.JsonValueKind.True;
        string p2Turn = Text(probe, "p2Turn");
        string name = Text(probe, "name");

        // A touch of latency, so the UI behaves like a real opponent rather than
        // snapping back instantly — this is still the same Handle path.
        await Task.Delay(120);
        peer.Handle(new Incoming(typeName, name, col, piece, grid, p2Turn, active));

        static string Text(System.Text.Json.JsonElement el, string field) =>
            el.TryGetProperty(field, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                ? v.GetString() ?? "" : "";

        // A field may arrive as a JSON number or a string, so accept both — the
        // relay path does the same through RoomMessageEnvelope.Number.
        static int? Number(System.Text.Json.JsonElement el, string field)
        {
            if (!el.TryGetProperty(field, out var v)) return null;
            if (v.ValueKind == System.Text.Json.JsonValueKind.Number) return v.GetInt32();
            if (v.ValueKind == System.Text.Json.JsonValueKind.String
                && int.TryParse(v.GetString(), out int p)) return p;
            return null;
        }
    }

    public bool IsMyTurn => IsActive && Turn == MyPiece;

    /// <summary>The board as the flat array the wire uses.</summary>
    public IReadOnlyList<int> Grid => _grid;

    public Connect4Session(KindleHubCore core, ILogger logger)
    {
        _core = core;
        _logger = logger;
    }

    private static string RoomShort(string room) => room.Length > 6 ? room[^6..] : room;

    public static async Task<Connect4Session> HostAsync(KindleHubCore core, ILogger logger)
    {
        var s = new Connect4Session(core, logger);
        s.RoomCode = await core.OpenGameRoomAsync(GameSlug, CancellationToken.None);
        s.IsHost = true;
        s.MyPiece = C4Rules.RedTurn;
        s.Turn = C4Rules.RedTurn;
        s.IsActive = true;
        s.Status = "Waiting for an opponent to join.";
        // Ignore anything already in the room so a late joiner doesn't inherit backlog.
        s._lastEventMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000;
        s.Changed?.Invoke();
        await s.AnnounceOpenAsync();
        s.StartPoll();
        return s;
    }

    public static async Task<Connect4Session> JoinAsync(KindleHubCore core, ILogger logger, string shortCode, string hostName)
    {
        var s = new Connect4Session(core, logger);
        s.RoomCode = await core.JoinGameRoomAsync(shortCode, GameSlug, CancellationToken.None);
        s.IsHost = false;
        s.MyPiece = C4Rules.YellowTurn;   // the guest is always second
        s.OpponentName = hostName;
        s.IsActive = true;
        s.Status = "Joined — waiting for the board.";
        s._lastEventMs = 0;
        s.Changed?.Invoke();
        await core.SendGameEventAsync(s.RoomCode, new { type = "JOIN", game = GameSlug }, CancellationToken.None);
        s.StartPoll();
        return s;
    }

    private async Task AnnounceOpenAsync()
    {
        if (RoomCode is null) return;
        try
        {
            await _core.SendGameEventAsync(KindleHubCore.OpenGamesLobby, new
            {
                type = "OPEN",
                game = GameSlug,
                roomShort = RoomShort(RoomCode),
                host = _core.CurrentProfile?.DisplayName ?? "Reader",
                hostUserId = _core.CurrentProfile?.UserId ?? "",
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Connect 4 lobby announce failed (room still usable by code)");
        }
    }

    /// <summary>Drops a piece. Relays it as MOVE_C4 and applies the same rules the
    /// web client does, so both sides reach the same verdict.</summary>
    public async Task<bool> DropAsync(int col)
    {
        // The offline pair has no room; the transport decides where a message goes.
        if (!IsActive) return false;
        if (RoomCode is null && DebugPeer is null) return false;
        if (!IsMyTurn) { Status = "Not your turn."; Changed?.Invoke(); return false; }

        int piece = C4Rules.PieceFor(MyPiece);
        int row = C4Rules.DropRow(_grid, col);
        if (row < 0) { Status = "That column is full."; Changed?.Invoke(); return false; }

        try
        {
            await SendToPeerAsync("MOVE_C4", new { type = "MOVE_C4", col, piece });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MOVE_C4 send failed");
        }

        Apply(col, piece);
        return true;
    }

    private void Apply(int col, int piece)
    {
        var outcome = C4Rules.Apply(_grid, col, piece);
        if (!outcome.Moved) return;
        Turn = outcome.NextTurn;
        if (outcome.Won)
        {
            IsActive = false;
            Result = piece == C4Rules.PieceFor(MyPiece) ? "You win!" : "Opponent wins.";
            Status = Result;
        }
        else if (outcome.Draw)
        {
            IsActive = false;
            Result = "Draw — board full.";
            Status = Result;
        }
        Changed?.Invoke();
    }

    private void StartPoll()
    {
        StopPoll();
        _pollTimer = new Timer(_ => _ = PollTickAsync(), null,
            TimeSpan.FromMilliseconds(2500), TimeSpan.FromMilliseconds(2500));
    }

    private void StopPoll() { _pollTimer?.Dispose(); _pollTimer = null; }

    private async Task PollTickAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var code = RoomCode;
            if (code is null) return;
            var events = await _core.PollGameEventsAsync(code, CancellationToken.None);
            foreach (var ev in events)
            {
                long ts = ev.Message.Timestamp.ToUnixTimeMilliseconds();
                if (ts <= _lastEventMs) continue;
                _lastEventMs = ts;
                if (ev.Message.IsMine && ev.Type != "OPEN") continue;   // our own echo
                Handle(ev);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Connect 4 poll failed");
        }
        finally { _polling = 0; }
    }

    private void Handle(RoomMessageEnvelope ev) =>
        Handle(FromEnvelope(ev));

    /// <summary>
    /// Flattens a relay envelope into the fields the protocol actually reads, so
    /// the game logic can also be fed by the offline loopback without inventing
    /// envelope objects. Keeping this split is what makes the whole protocol
    /// testable without a server.
    /// </summary>
    private static Incoming FromEnvelope(RoomMessageEnvelope ev) => new(
        ev.Type ?? "",
        ev.Str("name"),
        Num(ev, "col"),
        Num(ev, "piece"),
        ReadGrid(ev),
        ev.Str("p2Turn"),
        ReadActive(ev));

    /// <summary>
    /// Reads a numeric field. The envelope also has Str(), but MOVE_C4 sends col
    /// and piece as NUMBERS, so Str() would return "" and the move would be
    /// silently dropped — the move simply never appeared.
    /// </summary>
    private static int? Num(RoomMessageEnvelope ev, string field)
    {
        var n = ev.Number(field);
        return n > 0 || ev.Str(field).Length > 0 ? (int)n : null;
    }

    private static int[]? ReadGrid(RoomMessageEnvelope ev)
    {
        var arr = ev.Array("grid");
        if (arr.Count == 0) return null;
        var g = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            g[i] = int.TryParse(arr[i], out int v) ? v : 0;
        return g;
    }

    private static bool? ReadActive(RoomMessageEnvelope ev)
    {
        if (ev.Data is null) return null;
        return ev.Data.RootElement.TryGetProperty("active", out var a)
               && a.ValueKind == System.Text.Json.JsonValueKind.True;
    }

    /// <summary>The fields of one protocol message, independent of where it came from.</summary>
    private readonly record struct Incoming(
        string Type, string Name, int? Col, int? Piece, int[]? Grid, string? P2Turn, bool? Active);

    private void Handle(Incoming m)
    {
        switch (m.Type)
        {
            case "JOIN" when IsHost:
                OpponentName = string.IsNullOrEmpty(m.Name) ? "Opponent" : m.Name;
                Status = $"{OpponentName} joined. You are Red — drop first.";
                Changed?.Invoke();
                // Send the board so a mid-match joiner syncs to it.
                _ = SendStateAsync();
                break;

            case "C4_STATE" when !IsHost:
                {
                    if (m.Grid is { } g)
                        for (int i = 0; i < _grid.Length && i < g.Length; i++) _grid[i] = g[i];
                    if (!string.IsNullOrEmpty(m.P2Turn)) Turn = m.P2Turn!;
                    if (m.Active.HasValue) IsActive = m.Active.Value;
                    Status = !IsActive ? "The game is over." : IsMyTurn ? "Your move." : "Opponent's move.";
                    Changed?.Invoke();
                    break;
                }

            case "MOVE_C4":
                {
                    if (m.Col is not { } col || m.Piece is not { } piece) break;
                    if (col < 0 || col >= C4Rules.Cols) break;
                    // If our own move came back, the local board already has it.
                    if (piece == C4Rules.PieceFor(MyPiece)) break;
                    if (C4Rules.DropRow(_grid, col) < 0) break;
                    Apply(col, piece);
                    break;
                }
        }
    }

    private async Task SendStateAsync()
    {
        if (RoomCode is null) return;
        try
        {
            await SendToPeerAsync("C4_STATE", new
            {
                type = "C4_STATE",
                grid = _grid.ToArray(),
                p2Turn = Turn,
                active = IsActive
            });
        }
        catch (Exception ex) { _logger.LogDebug(ex, "C4_STATE send failed"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPoll();
    }

    /// <summary>
    /// Debug opponent: makes the guest side play a move of its own, so a single
    /// person can drive a whole game offline. Only valid on the offline pair, and
    /// only when it is actually that side's turn — which is exactly the condition
    /// the real guest would act under.
    /// </summary>
    public bool DebugOpponentMove()
    {
        if (!IsOfflineDebug || !IsActive || !IsMyTurn) return false;
        if (DebugPeer is null) return false;

        int piece = C4Rules.PieceFor(MyPiece);
        int col = C4Rules.LegalMoves(_grid).FirstOrDefault(-1);
        if (col < 0) return false;

        // Take a win, block the other side's win, else play the middle.
        foreach (int c in C4Rules.LegalMoves(_grid))
        {
            int row = C4Rules.DropRow(_grid, c);
            _grid[C4Rules.Index(c, row)] = piece;
            bool win = C4Rules.Wins(_grid, c, row, piece);
            _grid[C4Rules.Index(c, row)] = 0;
            if (win) { col = c; break; }

            _grid[C4Rules.Index(c, row)] = C4Rules.PieceFor(DebugPeer.MyPiece);
            int r2 = C4Rules.DropRow(_grid, c);
            bool block = C4Rules.Wins(_grid, c, r2, C4Rules.PieceFor(DebugPeer.MyPiece));
            _grid[C4Rules.Index(c, r2)] = 0;
            _grid[C4Rules.Index(c, row)] = 0;
            if (block) { col = c; break; }

            if (Math.Abs(c - (C4Rules.Cols - 1) / 2) < Math.Abs(col - (C4Rules.Cols - 1) / 2)) col = c;
        }

        _ = DropAsync(col);
        return true;
    }
}
