#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.GameInterop;

internal static class BppJbsErrorLogBridge
{
    private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;
    private const string BridgeTypeName =
        "BazaarPlusPlus.JbsServer.JbsErrorLogBridge, BazaarPlusPlus.JbsServer";
    private const string LogCategory = "JbsErrorLogBridge";

    internal static void WriteTournamentError(string message, Exception? ex = null)
    {
        WriteTournamentError(message, ex?.ToString());
    }

    internal static void WriteTournamentError(string message, string? details)
    {
        try
        {
            var bridge = Type.GetType(BridgeTypeName);
            var method = bridge?.GetMethod("WriteExternalError", StaticPublic);
            method?.Invoke(
                null,
                new object?[] { "MainModTournament", message, details }
            );
        }
        catch (Exception ex)
        {
            BppLog.Debug(LogCategory, $"Failed to write JBS error log: {ex.Message}");
        }
    }
}
