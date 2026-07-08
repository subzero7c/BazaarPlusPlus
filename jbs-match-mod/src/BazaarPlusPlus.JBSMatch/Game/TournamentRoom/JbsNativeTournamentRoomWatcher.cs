#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.JBSMatch.Game.TournamentRoom;

internal sealed class JbsNativeTournamentRoomWatcher : MonoBehaviour
{
    private const string LogCategory = "NativeTournamentRoomWatcher";
    private const float ScanIntervalSeconds = 0.5f;
    private const float PendingCreateWindowSeconds = 20f;
    private static readonly Regex RoomCodeRegex = new(@"\b[A-Z0-9]{6}\b", RegexOptions.Compiled);

    private readonly HashSet<int> _observedCreateButtons = new();
    private readonly HashSet<int> _observedLobbyCancelButtons = new();
    private static JbsNativeTournamentRoomWatcher? _instance;
    private float _nextScanTime;
    private float _pendingCreateUntil;
    private string _lastNotifiedRoomCode = string.Empty;

    internal static void Ensure(GameObject host)
    {
        if (host.GetComponent<JbsNativeTournamentRoomWatcher>() == null)
            host.AddComponent<JbsNativeTournamentRoomWatcher>();
    }

    internal static void ArmPendingCreateWindow()
    {
        if (_instance == null)
            return;

        _instance.OnNativeCreateConfirmClicked();
    }

    private void OnEnable()
    {
        _instance = this;
    }

    private void OnDisable()
    {
        if (_instance == this)
            _instance = null;
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + ScanIntervalSeconds;
        try
        {
            AttachNativeButtonListeners();
            if (Time.unscaledTime > _pendingCreateUntil)
                return;

            var detected = TryDetectLobbyRoom();
            if (detected == null)
                return;

            if (string.Equals(_lastNotifiedRoomCode, detected.Value.RoomCode, StringComparison.Ordinal))
                return;

            _lastNotifiedRoomCode = detected.Value.RoomCode;
            _pendingCreateUntil = 0f;
            JbsLog.Info(LogCategory, $"Native create flow produced tournament room: {detected.Value.RoomCode}");
            TournamentRoomPanel.OpenNativeTournamentRoom(
                detected.Value.RoomCode,
                detected.Value.RoomName
            );
        }
        catch (Exception ex)
        {
            JbsLog.Error(LogCategory, "Native tournament room scan failed", ex);
        }
    }

    private void AttachNativeButtonListeners()
    {
        foreach (var customButton in Resources.FindObjectsOfTypeAll<ButtonCustom>())
        {
            if (customButton == null)
                continue;

            var id = customButton.GetInstanceID();
            var transform = customButton.transform;
            var path = BuildPath(transform);
            var button = customButton.GetButton();
            if (button == null)
                continue;

            if (!_observedCreateButtons.Contains(id)
                && IsNativeCreateConfirmButton(customButton, path))
            {
                _observedCreateButtons.Add(id);
                button.onClick.AddListener(OnNativeCreateConfirmClicked);
                JbsLog.Info(LogCategory, $"Listening native create button: {path}");
            }

            if (!_observedLobbyCancelButtons.Contains(id)
                && IsNativeLobbyCancelButton(customButton, path))
            {
                _observedLobbyCancelButtons.Add(id);
                button.onClick.AddListener(OnNativeLobbyCancelClicked);
                JbsLog.Info(LogCategory, $"Listening native lobby cancel button: {path}");
            }
        }
    }

    private void OnNativeCreateConfirmClicked()
    {
        _pendingCreateUntil = Time.unscaledTime + PendingCreateWindowSeconds;
        JbsLog.Info(LogCategory, "Native create button clicked; waiting for lobby code.");
    }

    private void OnNativeLobbyCancelClicked()
    {
        _pendingCreateUntil = 0f;
        _lastNotifiedRoomCode = string.Empty;
        JbsLog.Info(LogCategory, "Native lobby cancel button clicked; leaving tournament chat room.");
        TournamentRoomPanel.LeaveForNativeLobbyCancelled();
    }

    private static bool IsNativeCreateConfirmButton(ButtonCustom button, string path)
    {
        if (path.IndexOf("Section_HostSettings", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (button.name.Equals("Btn_Blue_Large_P", StringComparison.Ordinal))
            return true;

        foreach (var text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            var value = CleanText(text?.text);
            if (string.Equals(value, "CREATE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "创建", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "创建大厅", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsNativeLobbyCancelButton(ButtonCustom button, string path)
    {
        if (path.IndexOf("Section_Lobby", StringComparison.OrdinalIgnoreCase) < 0
            && path.IndexOf("Tournament_Module_LobbyAssigned_PV", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        foreach (var text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            var value = CleanText(text?.text);
            if (string.Equals(value, "取消", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "退出", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "离开", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "CANCEL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "LEAVE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "EXIT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "BACK", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var text in button.GetComponentsInChildren<Text>(true))
        {
            var value = CleanText(text?.text);
            if (string.Equals(value, "取消", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "退出", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "离开", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "CANCEL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "LEAVE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "EXIT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "BACK", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static DetectedRoom? TryDetectLobbyRoom()
    {
        var root = FindLobbyRoot();
        if (root == null || !root.gameObject.activeInHierarchy)
            return null;

        var textParts = CollectTexts(root);
        var context = string.Join(" ", textParts);
        var match = RoomCodeRegex.Match(context.ToUpperInvariant());
        if (!match.Success)
            return null;

        return new DetectedRoom(match.Value, ResolveRoomName(textParts, match.Value));
    }

    private static RectTransform? FindLobbyRoot()
    {
        RectTransform? best = null;
        var bestScore = float.MinValue;
        foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
                continue;

            var path = BuildPath(rect);
            var score = ScoreCandidate(rect, path);
            if (score <= bestScore)
                continue;

            if (path.IndexOf("Section_Lobby", StringComparison.OrdinalIgnoreCase) < 0
                && path.IndexOf("Tournament_Module_LobbyAssigned_PV", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var context = string.Join(" ", CollectTexts(rect));
            if (!RoomCodeRegex.IsMatch(context.ToUpperInvariant()))
                continue;

            var hasTournamentPath =
                path.IndexOf("Tournament_Module", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Tournament", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasRoomKeyword = ContainsAny(
                context,
                "大厅代码",
                "大厅码",
                "大厅ID",
                "房间码",
                "房间代码",
                "Lobby Code",
                "Lobby ID",
                "Room Code"
            );

            if (!hasTournamentPath && !hasRoomKeyword)
                continue;

            best = rect;
            bestScore = score;
        }

        return best;
    }

    private static float ScoreCandidate(RectTransform rect, string path)
    {
        var score = 0f;
        if (rect.name.Equals("Tournament_Module_LobbyAssigned_PV", StringComparison.Ordinal))
            score += 1000f;
        if (rect.name.Equals("Section_Lobby", StringComparison.Ordinal))
            score += 800f;
        if (path.IndexOf("Tournament_Module", StringComparison.OrdinalIgnoreCase) >= 0)
            score += 180f;
        if (path.IndexOf("Lobby", StringComparison.OrdinalIgnoreCase) >= 0)
            score += 80f;
        if (path.IndexOf("Tournament", StringComparison.OrdinalIgnoreCase) >= 0)
            score += 60f;
        return score;
    }

    private static List<string> CollectTexts(RectTransform root)
    {
        var parts = new List<string>();
        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (text != null && text.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(CleanText(text.text));

        foreach (var text in root.GetComponentsInChildren<Text>(true))
            if (text != null && text.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(CleanText(text.text));

        return parts;
    }

    private static string ResolveRoomName(IReadOnlyList<string> textParts, string fallback)
    {
        foreach (var part in textParts)
        {
            var value = CleanText(part);
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (string.Equals(value, fallback, StringComparison.OrdinalIgnoreCase))
                continue;
            if (RoomCodeRegex.IsMatch(value.ToUpperInvariant()))
                continue;
            if (ContainsAny(value, "大厅", "房间", "Lobby", "Code", "ID"))
                continue;
            return value;
        }

        return fallback;
    }

    private static string CleanText(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

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
            if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
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
