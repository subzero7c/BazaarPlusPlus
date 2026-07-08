#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.GameInterop;

internal static class BppTournamentLobbyAutomation
{
    private const string LogCategory = "TournamentLobbyAutomation";
    private const int OpenScreenRetryEveryAttempts = 8;

    internal static void JoinLobbyWithRoomCode(string roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
            return;

        try
        {
            BppTournamentLobbyAutomationRunner.Ensure().Run(roomCode.Trim());
        }
        catch (Exception ex)
        {
            BppLog.Error(LogCategory, "Failed to start lobby join automation", ex);
            BppJbsErrorLogBridge.WriteTournamentError(
                "Failed to start lobby join automation",
                ex
            );
        }
    }

    private sealed class BppTournamentLobbyAutomationRunner : MonoBehaviour
    {
        private const float RetrySeconds = 5f;
        private const float RetryIntervalSeconds = 0.2f;

        private Coroutine? _active;
        private string _latestRoomCode = string.Empty;
        private int _openScreenAttempts;

        internal static BppTournamentLobbyAutomationRunner Ensure()
        {
            var existing = FindObjectOfType<BppTournamentLobbyAutomationRunner>();
            if (existing != null)
                return existing;

            var host = new GameObject("BppTournamentLobbyAutomation");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            return host.AddComponent<BppTournamentLobbyAutomationRunner>();
        }

        internal void Run(string roomCode)
        {
            _latestRoomCode = roomCode;
            _openScreenAttempts = 0;
            if (_active != null)
                StopCoroutine(_active);

            _active = StartCoroutine(RunJoinRoutine());
        }

        private IEnumerator RunJoinRoutine()
        {
            yield return null;

            var deadline = Time.unscaledTime + RetrySeconds;
            var attempt = 0;
            AutomationResult result = AutomationResult.NotReady("not-started");
            while (Time.unscaledTime <= deadline)
            {
                attempt++;
                result = TryJoinNow(_latestRoomCode, attempt, ref _openScreenAttempts);
                if (result.Success)
                {
                    BppLog.Info(
                        LogCategory,
                        $"Filled tournament lobby code and triggered join: room={_latestRoomCode}, attempts={attempt}"
                    );
                    _active = null;
                    yield break;
                }

                yield return new WaitForSecondsRealtime(RetryIntervalSeconds);
            }

            BppLog.Warn(
                LogCategory,
                $"Could not auto-join tournament lobby for room={_latestRoomCode}: {result.Reason}"
            );
            BppJbsErrorLogBridge.WriteTournamentError(
                $"Could not auto-join tournament lobby for room={_latestRoomCode}",
                BuildFailureDetails(result.Reason)
            );
            _active = null;
        }
    }

    private static AutomationResult TryJoinNow(
        string roomCode,
        int attempt,
        ref int openScreenAttempts)
    {
        try
        {
            var target = FindBestInput();
            if (!target.HasValue
                && ShouldTryOpenLobbyCodeScreen(attempt, openScreenAttempts)
                && TryOpenLobbyCodeScreen(out var openedBy))
            {
                openScreenAttempts++;
                return AutomationResult.NotReady($"opened lobby code screen via {openedBy}");
            }

            if (!target.HasValue)
                return AutomationResult.NotReady("lobby code input not found");

            FillInput(target.Value, roomCode);

            var button = FindBestJoinButton(target.Value);
            if (button == null)
                return AutomationResult.NotReady("join lobby button not found");

            button.onClick.Invoke();
            return AutomationResult.Done();
        }
        catch (Exception ex)
        {
            BppLog.Error(LogCategory, "Auto-join attempt failed", ex);
            BppJbsErrorLogBridge.WriteTournamentError("Auto-join attempt failed", ex);
            return AutomationResult.NotReady(ex.Message);
        }
    }

    private static bool ShouldTryOpenLobbyCodeScreen(int attempt, int openScreenAttempts)
    {
        return openScreenAttempts == 0
            || attempt % OpenScreenRetryEveryAttempts == 0;
    }

    private static bool TryOpenLobbyCodeScreen(out string openedBy)
    {
        openedBy = string.Empty;
        var button = FindBestOpenLobbyCodeButton();
        if (button == null)
            return false;

        button.onClick.Invoke();
        openedBy = $"{button.gameObject.name} ({BuildPath(button.transform)})";
        BppLog.Info(LogCategory, $"Triggered native tournament join screen via '{openedBy}'.");
        return true;
    }

    private static InputCandidate? FindBestInput()
    {
        InputCandidate? best = null;
        var bestScore = float.MinValue;

        foreach (var input in Resources.FindObjectsOfTypeAll<TMP_InputField>())
        {
            if (input == null || !input.gameObject.activeInHierarchy || !input.interactable)
                continue;

            var candidate = InputCandidate.From(input);
            var score = ScoreInput(candidate);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        foreach (var input in Resources.FindObjectsOfTypeAll<InputField>())
        {
            if (input == null || !input.gameObject.activeInHierarchy || !input.interactable)
                continue;

            var candidate = InputCandidate.From(input);
            var score = ScoreInput(candidate);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return bestScore >= 40f ? best : null;
    }

    private static float ScoreInput(InputCandidate candidate)
    {
        var score = 0f;
        var path = BuildPath(candidate.Transform);
        var context = BuildContext(candidate.GameObject);

        if (ContainsAny(path, "Tournament_Module", "Section_Lobby", "LobbyAssigned", "LobbyCode", "RoomCode"))
            score += 30f;
        if (ContainsAny(context, "大厅代码", "大厅码", "房间码", "房间代码", "代码", "Lobby Code", "Lobby ID", "Room Code", "Code"))
            score += 45f;
        if (ContainsAny(context, "Join", "加入"))
            score += 10f;
        if (ContainsAny(context, "Host", "主办", "Create", "创建"))
            score -= 25f;

        var rect = candidate.Transform as RectTransform;
        if (rect != null && rect.rect.width > 80f)
            score += 5f;

        return score;
    }

    private static void FillInput(InputCandidate candidate, string roomCode)
    {
        if (candidate.TmpInput != null)
        {
            candidate.TmpInput.Select();
            candidate.TmpInput.ActivateInputField();
            candidate.TmpInput.SetTextWithoutNotify(roomCode);
            candidate.TmpInput.text = roomCode;
            candidate.TmpInput.onValueChanged.Invoke(roomCode);
            candidate.TmpInput.onEndEdit.Invoke(roomCode);
            candidate.TmpInput.MoveTextEnd(false);
            return;
        }

        if (candidate.LegacyInput != null)
        {
            candidate.LegacyInput.Select();
            candidate.LegacyInput.ActivateInputField();
            candidate.LegacyInput.text = roomCode;
            candidate.LegacyInput.onValueChanged.Invoke(roomCode);
            candidate.LegacyInput.onEndEdit.Invoke(roomCode);
            candidate.LegacyInput.MoveTextEnd(false);
        }
    }

    private static Button? FindBestJoinButton(InputCandidate input)
    {
        Button? best = null;
        var bestScore = float.MinValue;
        var inputPath = BuildPath(input.Transform);

        foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
                continue;

            var score = ScoreJoinButton(button, inputPath);
            if (score > bestScore)
            {
                best = button;
                bestScore = score;
            }
        }

        return bestScore >= 55f ? best : null;
    }

    private static Button? FindBestOpenLobbyCodeButton()
    {
        Button? best = null;
        var bestScore = float.MinValue;

        foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
                continue;

            var score = ScoreOpenLobbyCodeButton(button);
            if (score > bestScore)
            {
                best = button;
                bestScore = score;
            }
        }

        return bestScore >= 55f ? best : null;
    }

    private static float ScoreOpenLobbyCodeButton(Button button)
    {
        var score = 0f;
        var path = BuildPath(button.transform);
        var text = BuildButtonContext(button.gameObject);

        if (ContainsAny(path, "Tournament_Module", "Section_HostOrJoin", "Join", "Lobby"))
            score += 35f;
        if (ContainsAny(text, "加入锦标赛", "加入", "进入", "Join Tournament", "Join", "Enter"))
            score += 45f;
        if (ContainsAny(text, "大厅代码", "大厅码", "房间码", "代码", "Lobby Code", "Lobby ID", "Room Code", "Code"))
            score += 20f;
        if (IsLikelyRightSideAction(button.transform as RectTransform))
            score += 12f;
        if (ContainsAny(path, "JBS_", "BppTournamentLobbyAutomation")
            || ContainsAny(text, "JBS", "刷新", "Refresh", "创建", "Create", "主办", "Host", "返回", "Back", "取消", "Cancel", "关闭", "Close", "设置", "Settings"))
            score -= 100f;

        return score;
    }

    private static float ScoreJoinButton(Button button, string inputPath)
    {
        var score = 0f;
        var path = BuildPath(button.transform);
        var text = BuildButtonContext(button.gameObject);

        if (ContainsAny(path, "Tournament_Module", "Section_Lobby", "LobbyAssigned", "LobbyCode", "RoomCode"))
            score += 25f;
        if (HasCommonTournamentAncestor(path, inputPath))
            score += 25f;
        if (ContainsAny(text, "加入", "进入", "Join", "Enter", "Confirm", "确认"))
            score += 45f;
        if (ContainsAny(text, "大厅", "Lobby", "Code", "代码"))
            score += 8f;
        if (IsNear(inputPath, path))
            score += 12f;
        if (ContainsAny(path, "JBS_")
            || ContainsAny(text, "主办", "Host", "Create", "创建", "返回", "Back", "取消", "Cancel", "离开", "Leave", "关闭", "Close", "刷新", "Refresh"))
            score -= 80f;

        return score;
    }

    private static bool IsNear(string leftPath, string rightPath)
    {
        var leftSegments = leftPath.Split('/');
        var rightSegments = rightPath.Split('/');
        var common = 0;
        var count = Math.Min(leftSegments.Length, rightSegments.Length);
        for (var i = 0; i < count; i++)
        {
            if (!string.Equals(leftSegments[i], rightSegments[i], StringComparison.Ordinal))
                break;

            common++;
        }

        return common >= Math.Max(1, Math.Min(leftSegments.Length, rightSegments.Length) - 2);
    }

    private static bool IsLikelyRightSideAction(RectTransform? rect)
    {
        if (rect == null)
            return false;

        var viewport = rect.GetComponentInParent<Canvas>()?.transform as RectTransform;
        if (viewport == null)
            return false;

        var worldCenter = rect.TransformPoint(rect.rect.center);
        var localCenter = viewport.InverseTransformPoint(worldCenter);
        return localCenter.x > viewport.rect.center.x;
    }

    private static string BuildFailureDetails(string reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine(reason);
        AppendInputCandidates(sb);
        AppendButtonCandidates(sb);
        return sb.ToString();
    }

    private static void AppendInputCandidates(StringBuilder sb)
    {
        sb.AppendLine("Visible input candidates:");
        var count = 0;
        foreach (var input in Resources.FindObjectsOfTypeAll<TMP_InputField>())
        {
            if (input == null || !input.gameObject.activeInHierarchy)
                continue;

            var candidate = InputCandidate.From(input);
            sb.AppendLine($"- score={ScoreInput(candidate):0.#} interactable={input.interactable} path={BuildPath(input.transform)} context={TrimForLog(BuildContext(input.gameObject))}");
            if (++count >= 12)
                break;
        }

        foreach (var input in Resources.FindObjectsOfTypeAll<InputField>())
        {
            if (input == null || !input.gameObject.activeInHierarchy)
                continue;

            var candidate = InputCandidate.From(input);
            sb.AppendLine($"- score={ScoreInput(candidate):0.#} interactable={input.interactable} path={BuildPath(input.transform)} context={TrimForLog(BuildContext(input.gameObject))}");
            if (++count >= 12)
                break;
        }
    }

    private static void AppendButtonCandidates(StringBuilder sb)
    {
        sb.AppendLine("Visible button candidates:");
        var rows = new List<string>();
        foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button == null || !button.gameObject.activeInHierarchy)
                continue;

            var path = BuildPath(button.transform);
            var text = BuildButtonContext(button.gameObject);
            if (!ContainsAny(path, "Tournament", "Lobby", "JBS")
                && !ContainsAny(text, "锦标赛", "加入", "大厅", "房间", "代码", "Tournament", "Join", "Lobby", "Room", "Code"))
                continue;

            rows.Add($"- openScore={ScoreOpenLobbyCodeButton(button):0.#} interactable={button.interactable} path={path} text={TrimForLog(text)}");
        }

        rows.Sort(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count && i < 16; i++)
            sb.AppendLine(rows[i]);
    }

    private static string TrimForLog(string value)
    {
        value = value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return value.Length <= 240 ? value : value.Substring(0, 240);
    }

    private static string BuildButtonContext(GameObject gameObject)
    {
        var parts = new List<string> { gameObject.name };
        foreach (var text in gameObject.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(text.text);
        }

        foreach (var text in gameObject.GetComponentsInChildren<Text>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(text.text);
        }

        return string.Join(" ", parts);
    }

    private static string BuildContext(GameObject gameObject)
    {
        var parts = new List<string> { gameObject.name, BuildPath(gameObject.transform) };
        var parent = gameObject.transform.parent;
        var hops = 0;
        while (parent != null && hops < 3)
        {
            parts.Add(parent.name);
            AddTextParts(parent.gameObject, parts);
            parent = parent.parent;
            hops++;
        }

        foreach (var text in gameObject.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(text.text);
        }

        foreach (var text in gameObject.GetComponentsInChildren<Text>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(text.text);
        }

        var tmpInput = gameObject.GetComponent<TMP_InputField>();
        if (tmpInput != null)
        {
            parts.Add(tmpInput.text);
            AddPlaceholder(parts, tmpInput.placeholder);
        }

        var legacyInput = gameObject.GetComponent<InputField>();
        if (legacyInput != null)
        {
            parts.Add(legacyInput.text);
            AddPlaceholder(parts, legacyInput.placeholder);
        }

        return string.Join(" ", parts);
    }

    private static void AddTextParts(GameObject gameObject, List<string> parts)
    {
        foreach (var text in gameObject.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(text.text);
        }

        foreach (var text in gameObject.GetComponentsInChildren<Text>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(text.text);
        }
    }

    private static void AddPlaceholder(List<string> parts, Graphic? placeholder)
    {
        if (placeholder is TextMeshProUGUI tmp && !string.IsNullOrWhiteSpace(tmp.text))
            parts.Add(tmp.text);
        else if (placeholder is Text text && !string.IsNullOrWhiteSpace(text.text))
            parts.Add(text.text);
    }

    private static bool HasCommonTournamentAncestor(string leftPath, string rightPath)
    {
        foreach (var token in new[] { "Section_Lobby", "Tournament_Module_LobbyAssigned_PV", "Tournament_Module" })
        {
            if (leftPath.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                && rightPath.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
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

    private readonly struct InputCandidate
    {
        private InputCandidate(TMP_InputField? tmpInput, InputField? legacyInput)
        {
            TmpInput = tmpInput;
            LegacyInput = legacyInput;
            GameObject = tmpInput != null ? tmpInput.gameObject : legacyInput!.gameObject;
            Transform = GameObject.transform;
        }

        internal TMP_InputField? TmpInput { get; }
        internal InputField? LegacyInput { get; }
        internal GameObject GameObject { get; }
        internal Transform Transform { get; }

        internal static InputCandidate From(TMP_InputField input) => new(input, null);
        internal static InputCandidate From(InputField input) => new(null, input);
    }

    private readonly struct AutomationResult
    {
        private AutomationResult(bool success, string reason)
        {
            Success = success;
            Reason = reason;
        }

        internal bool Success { get; }
        internal string Reason { get; }

        internal static AutomationResult Done() => new(true, string.Empty);
        internal static AutomationResult NotReady(string reason) => new(false, reason);
    }
}
