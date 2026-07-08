#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.JBSMatch.Infrastructure;

/// <summary>
/// WebSocket client for tournament room chat.
/// Protocol (Go server):
///   Join flow: POST /match/join {code, name} → {chatUrl}  → WS chatUrl
///   Client → Server  {"type":"chat","text":"..."}
///   Server → Client  {"type":"welcome","roomCode":"...","count":N,"max":N}
///   Server → Client  {"type":"join","name":"...","count":N}
///   Server → Client  {"type":"leave","name":"...","count":N}
///   Server → Client  {"type":"chat","name":"...","text":"...","timestamp":N}
///   Server → Client  {"type":"error","code":"...","message":"..."}
/// </summary>
internal sealed class TournamentChatClient : IDisposable
{
    private readonly ConcurrentQueue<ChatMessage> _incoming = new();
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private string _roomCode = "";
    private string _playerName = "";

    internal bool IsConnected => _ws?.State == WebSocketState.Open;
    internal string CurrentUserId { get; private set; } = "";
    internal bool IsCurrentPlayerHost { get; private set; }

    internal async Task<bool> ConnectAsync(
        string wsBaseUrl,
        string roomCode,
        string playerName,
        string roomName = "",
        bool limitPlayers = false,
        int maxPlayers = 30,
        bool autoStartOnFull = false)
    {
        if (!string.IsNullOrWhiteSpace(_roomCode) && !string.IsNullOrWhiteSpace(CurrentUserId))
            await LeaveRoomAsync(wsBaseUrl).ConfigureAwait(false);
        else
            await DisconnectAsync().ConfigureAwait(false);

        _cts = new CancellationTokenSource();
        _roomCode = roomCode;
        _playerName = playerName;
        CurrentUserId = "";
        IsCurrentPlayerHost = false;

        // Step 1: HTTP POST /match/join to obtain the WS chatUrl
        var httpBase = wsBaseUrl.TrimEnd('/')
            .Replace("wss://", "https://")
            .Replace("ws://",  "http://");

        var joinJson =
            $"{{\"code\":{JbsJson.Quote(roomCode)},\"name\":{JbsJson.Quote(playerName)},\"roomName\":{JbsJson.Quote(roomName)},\"limitPlayers\":{JsonBool(limitPlayers)},\"maxPlayers\":{maxPlayers},\"autoStartOnFull\":{JsonBool(autoStartOnFull)}}}";

        string chatUrl;
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
        {
            HttpResponseMessage resp;
            try
            {
                resp = await http.PostAsync(
                    $"{httpBase}/match/join",
                    new StringContent(joinJson, Encoding.UTF8, "application/json"),
                    _cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.error.connect_failed", ex.Message)));
                return false;
            }

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var errMsg = ParseErrorMessage(body) ?? $"HTTP {(int)resp.StatusCode}";
                _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.error.join_failed", errMsg)));
                return false;
            }

            chatUrl = ParseJoinChatUrl(body);
            CurrentUserId = ParseJoinUserId(body);
            IsCurrentPlayerHost = ParseJoinIsHost(body);
            if (string.IsNullOrEmpty(chatUrl))
            {
                _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.error.no_chat_url")));
                return false;
            }
        }

        // Step 2: Connect WebSocket (userId and name are embedded in chatUrl query string)
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(chatUrl), _cts.Token).ConfigureAwait(false);
        _ = Task.Run(() => ReceiveLoop(_cts.Token));
        return true;
    }

    private static string JsonBool(bool value) => value ? "true" : "false";

    internal async Task<bool> DisbandRoomAsync(string wsBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(_roomCode) || string.IsNullOrWhiteSpace(CurrentUserId))
            return false;

        var httpBase = wsBaseUrl.TrimEnd('/')
            .Replace("wss://", "https://")
            .Replace("ws://",  "http://");
        var json =
            $"{{\"code\":{JbsJson.Quote(_roomCode)},\"userId\":{JbsJson.Quote(CurrentUserId)},\"name\":{JbsJson.Quote(_playerName)}}}";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var resp = await http.PostAsync(
                $"{httpBase}/match/disband",
                new StringContent(json, Encoding.UTF8, "application/json")
            ).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
                return true;

            var errMsg = ParseErrorMessage(body) ?? $"HTTP {(int)resp.StatusCode}";
            _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.error.disband_failed", errMsg)));
            return false;
        }
        catch (Exception ex)
        {
            _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.error.disband_failed", ex.Message)));
            return false;
        }
    }

    internal async Task<bool> LeaveRoomAsync(string wsBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(_roomCode) || string.IsNullOrWhiteSpace(CurrentUserId))
        {
            await DisconnectAsync().ConfigureAwait(false);
            return false;
        }

        var roomCode = _roomCode;
        var userId = CurrentUserId;
        var httpBase = wsBaseUrl.TrimEnd('/')
            .Replace("wss://", "https://")
            .Replace("ws://",  "http://");
        var json =
            $"{{\"code\":{JbsJson.Quote(roomCode)},\"userId\":{JbsJson.Quote(userId)}}}";

        var ok = false;
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
        {
            try
            {
                var resp = await http.PostAsync(
                    $"{httpBase}/match/leave",
                    new StringContent(json, Encoding.UTF8, "application/json")
                ).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    ok = true;
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var errMsg = ParseErrorMessage(body) ?? $"HTTP {(int)resp.StatusCode}";
                    _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.error.leave_failed", errMsg)));
                }
            }
            catch (Exception ex)
            {
                _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.error.leave_failed", ex.Message)));
            }
        }

        await DisconnectAsync().ConfigureAwait(false);
        return ok;
    }

    internal void SendChatMessage(string text)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var json = $"{{\"type\":\"chat\",\"text\":{JbsJson.Quote(text)}}}";
        _ = SendRawAsync(json).ContinueWith(t =>
        {
            if (t.IsFaulted)
                _incoming.Enqueue(ChatMessage.System(
                    JbsLocalization.Get("tournament.error.send_failed", t.Exception?.InnerException?.Message ?? "")));
        });
    }

    internal void ReportRoomCount(string wsBaseUrl, int playerCount)
    {
        if (string.IsNullOrWhiteSpace(_roomCode))
            return;

        if (playerCount < 0)
            return;

        _ = ReportRoomCountAsync(wsBaseUrl, playerCount);
    }

    private async Task ReportRoomCountAsync(string wsBaseUrl, int playerCount)
    {
        var httpBase = wsBaseUrl.TrimEnd('/')
            .Replace("wss://", "https://")
            .Replace("ws://", "http://");
        var json =
            $"{{\"code\":{JbsJson.Quote(_roomCode)},\"userId\":{JbsJson.Quote(CurrentUserId)},\"count\":{playerCount}}}";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            await http.PostAsync(
                $"{httpBase}/match/report-count",
                new StringContent(json, Encoding.UTF8, "application/json")
            ).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: chat state is still authoritative locally.
        }
    }

    internal bool TryDequeue(out ChatMessage message) => _incoming.TryDequeue(out message);

    // ---- Private ----

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (_ws != null && _ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.chat.disconnected")));
                        return;
                    }

                    if (result.Count > 0)
                        message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage && !ct.IsCancellationRequested);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.chat.disconnected")));
                    break;
                }

                var json = Encoding.UTF8.GetString(message.ToArray());
                EnqueueParsedMessages(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _incoming.Enqueue(ChatMessage.System(JbsLocalization.Get("tournament.error.connection_error", ex.Message)));
        }
    }

    private async Task SendRawAsync(string json)
    {
        if (_ws?.State != WebSocketState.Open || _cts == null) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token)
                 .ConfigureAwait(false);
    }

    private static string ParseJoinChatUrl(string json)
    {
        return JbsJson.TryParseObject(json, out var obj)
            ? JbsJson.String(obj, "chatUrl") ?? string.Empty
            : string.Empty;
    }

    private static string ParseJoinUserId(string json)
    {
        return JbsJson.TryParseObject(json, out var obj)
            ? JbsJson.String(obj, "userId") ?? string.Empty
            : string.Empty;
    }

    private static bool ParseJoinIsHost(string json)
    {
        return JbsJson.TryParseObject(json, out var obj)
            && obj.TryGetValue("isHost", out var value)
            && value is bool isHost
            && isHost;
    }

    private static string? ParseErrorMessage(string json)
    {
        if (!JbsJson.TryParseObject(json, out var obj))
            return null;

        return JbsJson.String(obj, "error")
            ?? JbsJson.String(obj, "message")
            ?? JbsJson.String(obj, "code");
    }

    private void EnqueueParsedMessages(string json)
    {
        if (!JbsJson.TryParseObject(json, out var obj))
        {
            _incoming.Enqueue(ChatMessage.System(json));
            return;
        }

        var type = JbsJson.String(obj, "type") ?? string.Empty;
        if (type == "history")
        {
            if (obj.TryGetValue("messages", out var messagesValue)
                && messagesValue is System.Collections.Generic.List<object?> messages)
            {
                foreach (var item in messages)
                    if (item is System.Collections.Generic.Dictionary<string, object?> messageObj)
                        _incoming.Enqueue(ParseMessageObject(messageObj));
            }
            return;
        }

        _incoming.Enqueue(ParseMessageObject(obj));
    }

    private ChatMessage ParseMessageObject(System.Collections.Generic.Dictionary<string, object?> obj)
    {
        var type = JbsJson.String(obj, "type") ?? string.Empty;
        var userId = JbsJson.String(obj, "userId");
        var name = JbsJson.String(obj, "name");
        var count = JbsJson.Int(obj, "count", -1);
        var max = JbsJson.Int(obj, "max", -1);

        switch (type)
        {
            case "welcome":
            {
                var roomCode = JbsJson.String(obj, "roomCode") ?? _roomCode;
                if (obj.TryGetValue("isHost", out var isHostValue) && isHostValue is bool isHost)
                    IsCurrentPlayerHost = isHost;
                return ChatMessage.Welcome(
                    userId,
                    _playerName,
                    count,
                    max,
                    ParsePlayers(obj),
                    JbsLocalization.Get("tournament.chat.connected", roomCode, Math.Max(count, 0), Math.Max(max, 0)));
            }
            case "join":
            {
                name ??= JbsLocalization.Get("player.fallback");
                return ChatMessage.Join(userId, name, count, JbsLocalization.Get("tournament.chat.system_joined", name));
            }
            case "leave":
            {
                name ??= JbsLocalization.Get("player.fallback");
                return ChatMessage.Leave(userId, name, count, JbsLocalization.Get("tournament.chat.system_left", name));
            }
            case "chat":
            {
                name ??= JbsLocalization.Get("player.unknown");
                var text = JbsJson.String(obj, "text") ?? "";
                return ChatMessage.Chat(userId, name, text);
            }
            case "error":
            {
                var message = JbsJson.String(obj, "message") ?? JbsLocalization.Get("tournament.error.unknown");
                return ChatMessage.System(JbsLocalization.Get("tournament.error.server_error", message));
            }
            case "disband":
            {
                var message = JbsJson.String(obj, "message")
                    ?? JbsLocalization.Get("tournament.chat.room_disbanded");
                return ChatMessage.RoomDisbanded(message);
            }
            default:
                return ChatMessage.System(type);
        }
    }

    private static IReadOnlyList<ChatPlayer> ParsePlayers(Dictionary<string, object?> obj)
    {
        if (!obj.TryGetValue("players", out var value) || value is not List<object?> rawPlayers)
            return Array.Empty<ChatPlayer>();

        var players = new List<ChatPlayer>(rawPlayers.Count);
        foreach (var item in rawPlayers)
        {
            if (item is not Dictionary<string, object?> playerObj)
                continue;

            var userId = JbsJson.String(playerObj, "userId") ?? string.Empty;
            var name = JbsJson.String(playerObj, "name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(name))
                continue;

            players.Add(new ChatPlayer(userId, name));
        }

        return players;
    }

    internal async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch { /* best-effort */ }
        }
        _ws?.Dispose();
        _ws = null;
        _cts?.Dispose();
        _cts = null;
        CurrentUserId = "";
        IsCurrentPlayerHost = false;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _ws?.Dispose();
    }
}

internal readonly struct ChatPlayer
{
    internal ChatPlayer(string userId, string name)
    {
        UserId = string.IsNullOrWhiteSpace(userId) ? null : userId;
        Name = name;
    }

    internal string? UserId { get; }
    internal string Name { get; }
}

internal readonly struct ChatMessage
{
    private ChatMessage(string? fromUserId, string from, string text, bool isSystem, string? eventPlayerId, string? eventPlayerName, bool isJoin, int playerCount, int maxPlayers, bool isRoomDisbanded, IReadOnlyList<ChatPlayer>? players = null)
    {
        FromUserId = fromUserId;
        From = from;
        Text = text;
        IsSystem = isSystem;
        EventPlayerId = eventPlayerId;
        EventPlayerName = eventPlayerName;
        IsJoinEvent = isJoin;
        PlayerCount = playerCount;
        MaxPlayers = maxPlayers;
        IsRoomDisbanded = isRoomDisbanded;
        Players = players ?? Array.Empty<ChatPlayer>();
    }

    internal static ChatMessage System(string text) =>
        new(null, "", text, true, null, null, false, -1, -1, false);

    internal static ChatMessage Welcome(string? userId, string playerName, int count, int max, IReadOnlyList<ChatPlayer> players, string displayText) =>
        new(null, "", displayText, true, userId, playerName, true, count, max, false, players);

    internal static ChatMessage Join(string? userId, string playerName, int count, string displayText) =>
        new(null, "", displayText, true, userId, playerName, true, count, -1, false);

    internal static ChatMessage Leave(string? userId, string playerName, int count, string displayText) =>
        new(null, "", displayText, true, userId, playerName, false, count, -1, false);

    internal static ChatMessage Chat(string? fromUserId, string from, string text) =>
        new(fromUserId, from, text, false, null, null, false, -1, -1, false);

    internal static ChatMessage RoomDisbanded(string displayText) =>
        new(null, "", displayText, true, null, null, false, -1, -1, true);

    internal string? FromUserId { get; }
    internal string From { get; }
    internal string Text { get; }
    internal bool IsSystem { get; }

    /// <summary>Non-null for join/leave events; use IsJoinEvent to distinguish.</summary>
    internal string? EventPlayerId { get; }
    internal string? EventPlayerName { get; }

    /// <summary>true = player joined, false = player left. Only meaningful when EventPlayerName != null.</summary>
    internal bool IsJoinEvent { get; }
    internal int PlayerCount { get; }
    internal int MaxPlayers { get; }
    internal bool IsRoomDisbanded { get; }
    internal IReadOnlyList<ChatPlayer> Players { get; }
}
