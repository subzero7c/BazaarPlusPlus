#nullable enable
using System;

namespace BazaarPlusPlus.JBSMatch.Game.TournamentRoom;

public static class JbsTournamentRoomBridge
{
    public static void OpenNativeTournamentRoom(string roomCode, string roomName)
    {
        try
        {
            TournamentRoomPanel.OpenNativeTournamentRoom(roomCode, roomName);
        }
        catch (Exception ex)
        {
            JbsLog.Error("TournamentBridge", "Failed to open native tournament room", ex);
        }
    }
}
