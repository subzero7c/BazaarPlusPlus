#nullable enable
using System;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace BazaarPlusPlus.JBSMatch;

internal static class JbsLog
{
    private static ManualLogSource? _logger;
    private static readonly object ErrorFileLock = new();
    private static string? _errorLogPath;

    internal static void Install(ManualLogSource logger) => _logger = logger;

    internal static void InitializeFileLogging(string? pluginDir)
    {
        try
        {
            var root = string.IsNullOrWhiteSpace(pluginDir)
                ? "."
                : Path.Combine(pluginDir!, "JBSMatch");
            var logDir = Path.Combine(root, "logs");
            Directory.CreateDirectory(logDir);
            _errorLogPath = Path.Combine(logDir, "error.log");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"[JbsLog] Failed to initialize error log file: {ex.Message}");
            _errorLogPath = null;
        }
    }

    internal static void Reset()
    {
        _logger = null;
        _errorLogPath = null;
    }

    internal static void Info(string category, string message) =>
        _logger?.LogInfo($"[{category}] {message}");

    internal static void Warn(string category, string message) =>
        _logger?.LogWarning($"[{category}] {message}");

    internal static void Error(string category, string message, Exception? ex = null)
    {
        var line = ex != null ? $"[{category}] {message}: {ex}" : $"[{category}] {message}";
        _logger?.LogError(line);
        WriteErrorFile(line);
    }

    internal static void Debug(string category, string message) =>
        _logger?.LogDebug($"[{category}] {message}");

    internal static void ExternalError(string category, string message, string? details = null)
    {
        var line = string.IsNullOrWhiteSpace(details)
            ? $"[{category}] {message}"
            : $"[{category}] {message}{Environment.NewLine}{details}";
        _logger?.LogError(line);
        WriteErrorFile(line);
    }

    private static void WriteErrorFile(string line)
    {
        var path = _errorLogPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {line}{Environment.NewLine}";
            lock (ErrorFileLock)
                File.AppendAllText(path, entry, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"[JbsLog] Failed to write error log file: {ex.Message}");
        }
    }
}
