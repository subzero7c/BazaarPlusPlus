#nullable enable
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop;

internal static class BppNativeTournamentLobbyJoiner
{
    private const string LogCategory = "NativeTournamentLobbyJoiner";
    private const BindingFlags InstanceAny =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string StateMachineTypeName =
        "TheBazaar.UI.Tournaments.TournamentPanelStateMachineController, TheBazaarRuntime";
    private const string PanelStateTypeName =
        "TheBazaar.UI.Tournaments.ETournamentPanelState, TheBazaarRuntime";

    internal static void JoinLobbyWithFallback(string roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
            return;

        try
        {
            Runner.Ensure().Run(roomCode.Trim());
        }
        catch (Exception ex)
        {
            BppLog.Error(LogCategory, "Failed to start native lobby joiner", ex);
            BppJbsErrorLogBridge.WriteTournamentError("Failed to start native lobby joiner", ex);
            BppTournamentLobbyAutomation.JoinLobbyWithRoomCode(roomCode);
        }
    }

    private sealed class Runner : MonoBehaviour
    {
        private string _latestRoomCode = string.Empty;
        private int _generation;

        internal static Runner Ensure()
        {
            var existing = FindObjectOfType<Runner>();
            if (existing != null)
                return existing;

            var host = new GameObject("BppNativeTournamentLobbyJoiner");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            return host.AddComponent<Runner>();
        }

        internal void Run(string roomCode)
        {
            _latestRoomCode = roomCode;
            var generation = ++_generation;
            _ = RunAsync(roomCode, generation);
        }

        private async Task RunAsync(string roomCode, int generation)
        {
            await Task.Yield();
            if (generation != _generation)
                return;

            try
            {
                var result = await TryJoinNativeLobbyAsync(roomCode);
                if (result.Success)
                {
                    BppLog.Info(LogCategory, $"Joined native tournament lobby directly: {roomCode}");
                    return;
                }

                BppLog.Warn(
                    LogCategory,
                    $"Direct native tournament join unavailable for {roomCode}: {result.Reason}; falling back to UI automation."
                );
                BppJbsErrorLogBridge.WriteTournamentError(
                    $"Direct native tournament join unavailable for {roomCode}",
                    result.Reason
                );
            }
            catch (Exception ex)
            {
                BppLog.Error(LogCategory, $"Direct native tournament join failed for {roomCode}", ex);
                BppJbsErrorLogBridge.WriteTournamentError(
                    $"Direct native tournament join failed for {roomCode}",
                    ex
                );
            }

            if (generation == _generation)
                BppTournamentLobbyAutomation.JoinLobbyWithRoomCode(_latestRoomCode);
        }
    }

    private static async Task<JoinResult> TryJoinNativeLobbyAsync(string roomCode)
    {
        var stateMachine = FindStateMachine();
        if (stateMachine == null)
            return JoinResult.NotReady("native tournament state machine not found");

        InvokeOptional(stateMachine, "EnsureInitialized");
        InvokeOptional(stateMachine, "InitializeDependencies");

        var client = GetPropertyValue(stateMachine, "Client");
        if (client == null)
            return JoinResult.NotReady("native tournament HTTP client not available");

        var ct = GetPropertyValue(stateMachine, "Ct") is CancellationToken token
            ? token
            : CancellationToken.None;
        var joinMethod = client.GetType().GetMethod("JoinLobby", InstanceAny);
        if (joinMethod == null)
            return JoinResult.NotReady("native JoinLobby method not found");

        if (joinMethod.Invoke(client, new object[] { roomCode, ct }) is not Task joinTask)
            return JoinResult.NotReady("native JoinLobby did not return a task");

        await joinTask;

        var result = GetPropertyValue(joinTask, "Result");
        if (result == null)
            return JoinResult.NotReady("native JoinLobby returned no result");

        if (GetPropertyValue(result, "IsOk") is not true)
        {
            var error =
                GetPropertyValue(result, "ErrorMessage") as string
                ?? GetPropertyValue(result, "ErrorCode") as string
                ?? "native JoinLobby returned failure";
            return JoinResult.NotReady(error);
        }

        var lobby = GetPropertyValue(result, "Data");
        if (lobby == null)
            return JoinResult.NotReady("native JoinLobby returned no lobby data");

        RunOnMainThread(stateMachine, lobby);
        return JoinResult.Done();
    }

    private static object? FindStateMachine()
    {
        var type = Type.GetType(StateMachineTypeName);
        if (type == null)
            return null;

        foreach (var obj in Resources.FindObjectsOfTypeAll(type))
        {
            if (obj == null)
                continue;

            if (obj is Component component && !component.gameObject.activeInHierarchy)
                continue;

            return obj;
        }

        return null;
    }

    private static void RunOnMainThread(object stateMachine, object lobby)
    {
        InvokeOptional(stateMachine, "SetLobby", lobby);

        var stateType = Type.GetType(PanelStateTypeName);
        if (stateType != null)
        {
            var inLobby = Enum.Parse(stateType, "InLobby");
            InvokeOptional(stateMachine, "GoToNextState", inLobby);
        }

        InvokeOptional(stateMachine, "Maximize");
        InvokeOptional(stateMachine, "BroadcastTournamentLobbyPresence");
    }

    private static object? GetPropertyValue(object target, string name)
    {
        return target.GetType().GetProperty(name, InstanceAny)?.GetValue(target);
    }

    private static void InvokeOptional(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, InstanceAny);
        method?.Invoke(target, args);
    }

    private readonly struct JoinResult
    {
        private JoinResult(bool success, string reason)
        {
            Success = success;
            Reason = reason;
        }

        internal bool Success { get; }
        internal string Reason { get; }

        internal static JoinResult Done() => new(true, string.Empty);
        internal static JoinResult NotReady(string reason) => new(false, reason);
    }
}
