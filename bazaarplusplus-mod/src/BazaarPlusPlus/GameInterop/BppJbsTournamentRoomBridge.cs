#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.GameInterop;

internal static class BppJbsTournamentRoomBridge
{
    private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;
    private const string BridgeTypeName =
        "BazaarPlusPlus.JBSMatch.Game.TournamentRoom.JbsTournamentRoomBridge, BazaarPlusPlus.JBSMatch";
    private const string LogCategory = "JbsTournamentRoomBridge";

    internal static void OpenNativeTournamentRoom(string roomCode, string roomName)
    {
        try
        {
            var bridge = Type.GetType(BridgeTypeName);
            var method = bridge?.GetMethod("OpenNativeTournamentRoom", StaticPublic);
            if (method == null)
                return;

            method.Invoke(null, new object[] { roomCode, roomName });
        }
        catch (Exception ex)
        {
            BppLog.Error(LogCategory, "Failed to notify JBS native tournament room", ex);
            BppJbsErrorLogBridge.WriteTournamentError(
                "Failed to notify JBS native tournament room",
                ex
            );
        }
    }
}
