#nullable enable
using System;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.JbsServer.Game.TournamentRoom;

internal sealed class TournamentRoomAutoCloseController : MonoBehaviour
{
    private const string LogCategory = "TournamentAutoClose";
    private const float PollIntervalSeconds = 0.25f;

    private bool _wasInGameRun;
    private float _nextPollTime;

    internal static void Ensure(GameObject host)
    {
        if (host.GetComponent<TournamentRoomAutoCloseController>() == null)
            host.AddComponent<TournamentRoomAutoCloseController>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextPollTime)
            return;

        _nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        try
        {
            var isInGameRun = ComputeIsInGameRun();
            if (isInGameRun && !_wasInGameRun)
                TournamentRoomPanel.HideForGameStarted();

            _wasInGameRun = isInGameRun;
        }
        catch (Exception ex)
        {
            JbsLog.Error(LogCategory, "Failed to evaluate game-start state", ex);
        }
    }

    private static bool ComputeIsInGameRun()
    {
        if (Data.IsInCombat)
            return true;

        var currentAppState = AppState.CurrentState;
        if (currentAppState is RunAppState)
            return !currentAppState.IsEndOfRunState();

        if (currentAppState is ReplayState)
            return true;

        return currentAppState is StartRunAppState;
    }
}
