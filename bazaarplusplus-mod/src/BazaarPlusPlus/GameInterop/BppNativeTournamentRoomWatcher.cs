#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.GameInterop;

internal sealed class BppNativeTournamentRoomWatcher : MonoBehaviour
{
    private const string LogCategory = "NativeTournamentRoomWatcher";
    private const float ScanIntervalSeconds = 0.5f;
    private static readonly Regex RoomCodeRegex = new(@"\b[A-Z0-9]{6}\b", RegexOptions.Compiled);

    private float _nextScanTime;
    private string _lastNotifiedRoomCode = string.Empty;

    internal static void Ensure(GameObject host)
    {
        if (host.GetComponent<BppNativeTournamentRoomWatcher>() == null)
            host.AddComponent<BppNativeTournamentRoomWatcher>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + ScanIntervalSeconds;
        try
        {
            var detected = TryDetectLobbyRoom();
            if (detected == null)
                return;

            if (string.Equals(_lastNotifiedRoomCode, detected.Value.RoomCode, StringComparison.Ordinal))
                return;

            _lastNotifiedRoomCode = detected.Value.RoomCode;
            BppTournamentRoomBridge.EnterNativeRoomFromGame(detected.Value.RoomCode, detected.Value.RoomName);
            BppJbsTournamentRoomBridge.OpenNativeTournamentRoom(
                detected.Value.RoomCode,
                detected.Value.RoomName
            );
            BppLog.Info(
                LogCategory,
                $"Detected native tournament lobby room: {detected.Value.RoomCode}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error(LogCategory, "Native tournament room scan failed", ex);
            BppJbsErrorLogBridge.WriteTournamentError("Native tournament room scan failed", ex);
        }
    }

    private static DetectedRoom? TryDetectLobbyRoom()
    {
        var root = FindLobbyRoot();
        if (root == null || !root.gameObject.activeInHierarchy)
            return null;

        var textParts = CollectTexts(root);
        var context = string.Join(" ", textParts);
        if (
            !ContainsAny(
                context,
                "大厅代码",
                "大厅码",
                "大厅ID",
                "房间码",
                "房间代码",
                "Lobby Code",
                "Lobby ID",
                "Room Code"
            )
            && !BuildPath(root).Contains("Tournament", StringComparison.OrdinalIgnoreCase)
        )
            return null;

        foreach (var part in textParts)
        {
            var candidate = NormalizeRoomCode(part);
            if (candidate != null)
                return new DetectedRoom(candidate, candidate);
        }

        var match = RoomCodeRegex.Match(context.ToUpperInvariant());
        return match.Success ? new DetectedRoom(match.Value, match.Value) : null;
    }

    private static RectTransform? FindLobbyRoot()
    {
        RectTransform? best = null;
        var bestScore = float.MinValue;
        foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
                continue;

            var score = 0f;
            var path = BuildPath(rect);
            if (rect.name.Equals("Tournament_Module_LobbyAssigned_PV", StringComparison.Ordinal))
                score += 1000f;
            if (rect.name.Equals("Section_Lobby", StringComparison.Ordinal))
                score += 800f;
            if (path.IndexOf("Tournament_Module", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 100f;

            if (score <= bestScore)
                continue;

            var context = string.Join(" ", CollectTexts(rect));
            if (
                !ContainsAny(
                    context,
                    "大厅代码",
                    "大厅码",
                    "大厅ID",
                    "房间码",
                    "房间代码",
                    "Lobby Code",
                    "Lobby ID",
                    "Room Code"
                )
                && path.IndexOf("Tournament", StringComparison.OrdinalIgnoreCase) < 0
            )
                continue;

            best = rect;
            bestScore = score;
        }

        return best;
    }

    private static List<string> CollectTexts(RectTransform root)
    {
        var parts = new List<string>();
        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text != null && text.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(CleanText(text.text));
        }

        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            if (text != null && text.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(CleanText(text.text));
        }

        return parts;
    }

    private static string? NormalizeRoomCode(string value)
    {
        var cleaned = CleanText(value).Trim().ToUpperInvariant();
        if (RoomCodeRegex.IsMatch(cleaned))
        {
            var match = RoomCodeRegex.Match(cleaned);
            if (string.Equals(match.Value, cleaned, StringComparison.Ordinal))
                return cleaned;
        }

        return null;
    }

    private static string CleanText(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
    }

    private static string BuildPath(Transform transform)
    {
        var parts = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private readonly struct DetectedRoom
    {
        internal DetectedRoom(string roomCode, string roomName)
        {
            RoomCode = roomCode;
            RoomName = roomName;
        }

        internal string RoomCode { get; }
        internal string RoomName { get; }
    }
}
