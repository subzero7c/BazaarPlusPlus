#nullable enable
using System;
using BepInEx.Logging;

namespace BazaarPlusPlus.JbsServer;

internal static class JbsLog
{
    private static ManualLogSource? _logger;

    internal static void Install(ManualLogSource logger) => _logger = logger;

    internal static void Reset() => _logger = null;

    internal static void Info(string category, string message) =>
        _logger?.LogInfo($"[{category}] {message}");

    internal static void Warn(string category, string message) =>
        _logger?.LogWarning($"[{category}] {message}");

    internal static void Error(string category, string message, Exception? ex = null)
    {
        if (ex != null)
            _logger?.LogError($"[{category}] {message}: {ex}");
        else
            _logger?.LogError($"[{category}] {message}");
    }

    internal static void Debug(string category, string message) =>
        _logger?.LogDebug($"[{category}] {message}");
}
