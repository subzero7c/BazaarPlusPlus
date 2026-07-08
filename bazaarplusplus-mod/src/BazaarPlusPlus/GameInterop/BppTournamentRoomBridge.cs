#nullable enable
namespace BazaarPlusPlus.GameInterop;

public static class BppTournamentRoomBridge
{
    private static readonly object SyncRoot = new();
    private static string? _roomCode;
    private static string? _roomName;

    public static bool IsInTournamentRoom()
    {
        lock (SyncRoot)
            return !string.IsNullOrWhiteSpace(_roomCode);
    }

    public static void EnterRoom(string roomCode, string roomName)
    {
        var normalizedRoomCode = string.IsNullOrWhiteSpace(roomCode) ? null : roomCode.Trim();
        var normalizedRoomName = string.IsNullOrWhiteSpace(roomName) ? null : roomName.Trim();
        lock (SyncRoot)
        {
            _roomCode = normalizedRoomCode;
            _roomName = normalizedRoomName;
        }

        if (normalizedRoomCode != null)
            BppNativeTournamentLobbyJoiner.JoinLobbyWithFallback(normalizedRoomCode);
    }

    internal static void EnterNativeRoomFromGame(string roomCode, string roomName)
    {
        var normalizedRoomCode = string.IsNullOrWhiteSpace(roomCode) ? null : roomCode.Trim();
        var normalizedRoomName = string.IsNullOrWhiteSpace(roomName) ? null : roomName.Trim();
        lock (SyncRoot)
        {
            _roomCode = normalizedRoomCode;
            _roomName = normalizedRoomName;
        }
    }

    public static void LeaveRoom()
    {
        lock (SyncRoot)
        {
            _roomCode = null;
            _roomName = null;
        }
    }

    public static string? TryGetCurrentRoomCode()
    {
        lock (SyncRoot)
            return _roomCode;
    }

    public static string? TryGetCurrentRoomName()
    {
        lock (SyncRoot)
            return _roomName;
    }
}
