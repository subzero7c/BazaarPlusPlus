#nullable enable
using System;
using System.Reflection;

namespace BazaarPlusPlus.JbsServer.Game.TournamentRoom;

internal static class BppTournamentRoomBridgeClient
{
    private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;
    private const string BridgeTypeName =
        "BazaarPlusPlus.GameInterop.BppTournamentRoomBridge, BazaarPlusPlus";

    internal static void EnterRoom(string roomCode, string roomName)
    {
        try
        {
            var bridge = Type.GetType(BridgeTypeName);
            var method = bridge?.GetMethod("EnterRoom", StaticPublic);
            method?.Invoke(null, new object[] { roomCode, roomName });
        }
        catch (Exception ex)
        {
            JbsLog.Warn("TournamentBridge", $"Failed to sync room enter to BazaarPlusPlus: {ex.Message}");
        }
    }

    internal static void LeaveRoom()
    {
        try
        {
            var bridge = Type.GetType(BridgeTypeName);
            var method = bridge?.GetMethod("LeaveRoom", StaticPublic);
            method?.Invoke(null, null);
        }
        catch (Exception ex)
        {
            JbsLog.Warn("TournamentBridge", $"Failed to sync room leave to BazaarPlusPlus: {ex.Message}");
        }
    }
}
