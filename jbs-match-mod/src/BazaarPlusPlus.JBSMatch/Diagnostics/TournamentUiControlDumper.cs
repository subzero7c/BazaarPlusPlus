#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.JBSMatch.Diagnostics;

internal sealed class TournamentUiControlDumper : MonoBehaviour
{
    private const string LogCategory = "TournamentUiDump";
    private const float ScanIntervalSeconds = 1.0f;
    private static readonly ScreenSpec[] Screens =
    [
        new("锦标赛界面", "tournament", "Section_HostOrJoin", IsTournamentTitle),
        new("主办锦标赛界面", "host-tournament", "Section_HostSettings", text => Contains(text, "主办锦标赛")),
        new("大厅代码界面", "lobby-code", "Section_Lobby", IsLobbyCodeTitle),
    ];

    private string _outputPath = string.Empty;
    private float _nextScanTime;
    private string _lastSignature = string.Empty;

    internal static void Ensure(GameObject host, string pluginDir)
    {
        var dumper =
            host.GetComponent<TournamentUiControlDumper>()
            ?? host.AddComponent<TournamentUiControlDumper>();
        dumper.Initialize(pluginDir);
    }

    private void Initialize(string pluginDir)
    {
        var dumpDir = Path.Combine(pluginDir, "JBSMatch", "ui-dumps");
        Directory.CreateDirectory(dumpDir);
        _outputPath = Path.Combine(dumpDir, "tournament-ui-controls.md");
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(_outputPath) || Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + ScanIntervalSeconds;
        try
        {
            var captures = CaptureScreens();
            var signature = BuildSignature(captures);
            if (signature == _lastSignature)
                return;

            _lastSignature = signature;
            File.WriteAllText(_outputPath, RenderMarkdown(captures), Encoding.UTF8);
            JbsLog.Info(LogCategory, $"Wrote UI control dump: {_outputPath}");
        }
        catch (Exception ex)
        {
            JbsLog.Warn(LogCategory, $"Failed to dump tournament UI controls: {ex.Message}");
        }
    }

    private static List<ScreenCapture> CaptureScreens()
    {
        var result = new List<ScreenCapture>(Screens.Length);
        foreach (var spec in Screens)
        {
            var root = FindKnownScreenRoot(spec);
            if (root == null)
            {
                var title = FindTitle(spec);
                if (title != null)
                    root = ResolveScreenRoot(title);
            }

            if (root == null && spec.Key == "lobby-code")
                root = FindFirstRectByName("Tournament_Module_LobbyAssigned_PV");

            if (root == null)
            {
                result.Add(new ScreenCapture(spec.DisplayName, spec.Key, null, new List<ControlCapture>()));
                continue;
            }

            var controls = CaptureControls(root);
            result.Add(new ScreenCapture(spec.DisplayName, spec.Key, BuildPath(root), controls));
        }

        return result;
    }

    private static TextMeshProUGUI? FindTitle(ScreenSpec spec)
    {
        TextMeshProUGUI? best = null;
        var bestScore = float.MinValue;
        foreach (var text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (text == null || !text.gameObject.activeInHierarchy)
                continue;

            var value = CleanText(text.text);
            if (!spec.Matches(value))
                continue;

            var score = text.fontSize;
            if (value.Equals("锦标赛", StringComparison.Ordinal) && spec.Key == "tournament")
                score += 5000f;
            if (value.Equals("主办锦标赛", StringComparison.Ordinal) && spec.Key == "host-tournament")
                score += 5000f;
            if (value.Equals("大厅代码", StringComparison.Ordinal) && spec.Key == "lobby-code")
                score += 5000f;
            if (text.GetComponentInParent<Canvas>() != null)
                score += 100f;

            if (score > bestScore)
            {
                bestScore = score;
                best = text;
            }
        }

        return best;
    }

    private static RectTransform? FindKnownScreenRoot(ScreenSpec spec)
    {
        RectTransform? best = null;
        var bestScore = float.MinValue;
        foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rect == null || !rect.name.Equals(spec.RootObjectName, StringComparison.Ordinal))
                continue;

            var path = BuildPath(rect);
            if (!path.Contains("Tournament_Module", StringComparison.Ordinal))
                continue;

            var score = rect.gameObject.activeInHierarchy ? 1000f : 0f;
            score += rect.gameObject.activeSelf ? 100f : 0f;
            if (path.Contains("Tournament_Module_P", StringComparison.Ordinal))
                score += 50f;

            if (score > bestScore)
            {
                bestScore = score;
                best = rect;
            }
        }

        return best;
    }

    private static RectTransform? FindFirstRectByName(string name)
    {
        foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rect != null && rect.name.Equals(name, StringComparison.Ordinal))
                return rect;
        }

        return null;
    }

    private static RectTransform ResolveScreenRoot(TextMeshProUGUI title)
    {
        var current = title.transform as RectTransform;
        var canvas = title.GetComponentInParent<Canvas>();
        RectTransform? bestNamed = null;

        while (current != null)
        {
            if (canvas != null && current == canvas.transform)
                break;

            if (LooksLikeScreenRootName(current.name))
                bestNamed = current;

            current = current.parent as RectTransform;
        }

        if (bestNamed != null)
            return bestNamed;

        return canvas?.transform as RectTransform
            ?? title.rectTransform;
    }

    private static bool LooksLikeScreenRootName(string name)
    {
        return Contains(name, "Screen")
            || Contains(name, "View")
            || Contains(name, "Panel")
            || Contains(name, "Dialog")
            || Contains(name, "Window")
            || Contains(name, "Page")
            || Contains(name, "Modal")
            || name.Contains("界面", StringComparison.Ordinal);
    }

    private static List<ControlCapture> CaptureControls(RectTransform root)
    {
        var controls = new List<ControlCapture>();
        var seen = new HashSet<int>();

        foreach (var selectable in root.GetComponentsInChildren<Selectable>(includeInactive: true))
            AddControl(selectable.gameObject, root, controls, seen);

        foreach (var scrollRect in root.GetComponentsInChildren<ScrollRect>(includeInactive: true))
            AddControl(scrollRect.gameObject, root, controls, seen);

        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true))
            AddControl(text.gameObject, root, controls, seen);

        foreach (var legacyText in root.GetComponentsInChildren<Text>(includeInactive: true))
            AddControl(legacyText.gameObject, root, controls, seen);

        controls.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return controls;
    }

    private static void AddControl(
        GameObject go,
        RectTransform root,
        List<ControlCapture> controls,
        HashSet<int> seen)
    {
        var id = go.GetInstanceID();
        if (!seen.Add(id))
            return;

        var path = BuildPath(go.transform);
        controls.Add(new ControlCapture(
            go.name,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StablePathUuid(path),
            path,
            BuildRelativePath(root, go.transform),
            go.activeSelf,
            go.activeInHierarchy,
            LayerMask.LayerToName(go.layer),
            DescribeComponents(go),
            ExtractText(go),
            ExtractInteractable(go),
            DescribeRect(go.transform as RectTransform)
        ));
    }

    private static string DescribeComponents(GameObject go)
    {
        var names = new List<string>();
        foreach (var component in go.GetComponents<Component>())
        {
            if (component == null)
                continue;

            var typeName = component.GetType().Name;
            if (typeName is "RectTransform" or "CanvasRenderer")
                continue;

            names.Add(typeName);
        }

        return names.Count == 0 ? "-" : string.Join(", ", names);
    }

    private static string ExtractText(GameObject go)
    {
        var parts = new List<string>();
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text))
            parts.Add($"text={CleanText(tmp.text)}");

        var legacyText = go.GetComponent<Text>();
        if (legacyText != null && !string.IsNullOrWhiteSpace(legacyText.text))
            parts.Add($"text={CleanText(legacyText.text)}");

        var tmpInput = go.GetComponent<TMP_InputField>();
        if (tmpInput != null)
        {
            if (!string.IsNullOrEmpty(tmpInput.text))
                parts.Add($"value={CleanText(tmpInput.text)}");
            var placeholder = tmpInput.placeholder switch
            {
                TextMeshProUGUI tmpPlaceholder => CleanText(tmpPlaceholder.text),
                Text textPlaceholder => CleanText(textPlaceholder.text),
                _ => string.Empty,
            };
            if (!string.IsNullOrWhiteSpace(placeholder))
                parts.Add($"placeholder={placeholder}");
        }

        var input = go.GetComponent<InputField>();
        if (input != null)
        {
            if (!string.IsNullOrEmpty(input.text))
                parts.Add($"value={CleanText(input.text)}");
            if (input.placeholder is Text placeholder && !string.IsNullOrWhiteSpace(placeholder.text))
                parts.Add($"placeholder={CleanText(placeholder.text)}");
        }

        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string ExtractInteractable(GameObject go)
    {
        var selectable = go.GetComponent<Selectable>();
        return selectable == null ? "-" : selectable.interactable ? "true" : "false";
    }

    private static string DescribeRect(RectTransform? rect)
    {
        if (rect == null)
            return "-";

        var size = rect.rect.size;
        return $"pos=({rect.position.x:0.##},{rect.position.y:0.##}), size=({size.x:0.##},{size.y:0.##})";
    }

    private static string RenderMarkdown(List<ScreenCapture> captures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Tournament UI Controls Dump");
        sb.AppendLine();
        sb.AppendLine($"Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("说明：`runtime uuid` 使用 Unity 运行时 `GameObject.GetInstanceID()`；`path uuid` 是根据层级路径生成的稳定哈希，便于跨运行对照。");
        sb.AppendLine();

        foreach (var capture in captures)
        {
            sb.AppendLine($"## {capture.DisplayName}");
            sb.AppendLine();
            sb.AppendLine($"- Screen key: `{capture.Key}`");
            sb.AppendLine($"- Root path: `{capture.RootPath ?? "<not found>"}`");
            sb.AppendLine($"- Controls found: `{capture.Controls.Count}`");
            sb.AppendLine();

            if (capture.Controls.Count == 0)
            {
                sb.AppendLine("_未检测到该界面，或该界面当前未打开。_");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine("| # | GameObject | runtime uuid | path uuid | type/components | interactable | text/value | relative path | rect | active |");
            sb.AppendLine("|---:|---|---:|---|---|---|---|---|---|---|");
            for (var index = 0; index < capture.Controls.Count; index++)
            {
                var control = capture.Controls[index];
                sb.Append("| ")
                    .Append(index + 1)
                    .Append(" | `").Append(EscapePipe(control.Name)).Append("`")
                    .Append(" | `").Append(control.RuntimeUuid).Append("`")
                    .Append(" | `").Append(control.PathUuid).Append("`")
                    .Append(" | ").Append(EscapePipe(control.Components))
                    .Append(" | ").Append(control.Interactable)
                    .Append(" | ").Append(EscapePipe(control.Text))
                    .Append(" | `").Append(EscapePipe(control.RelativePath)).Append("`")
                    .Append(" | ").Append(EscapePipe(control.Rect))
                    .Append(" | ").Append(control.ActiveSelf ? "self:on" : "self:off")
                    .Append('/').Append(control.ActiveInHierarchy ? "hier:on" : "hier:off")
                    .AppendLine(" |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildSignature(List<ScreenCapture> captures)
    {
        var sb = new StringBuilder();
        foreach (var capture in captures)
        {
            sb.Append(capture.Key).Append(':').Append(capture.RootPath).Append(':').Append(capture.Controls.Count).Append('|');
            foreach (var control in capture.Controls)
                sb.Append(control.PathUuid).Append(',').Append(control.RuntimeUuid).Append(';');
        }

        return sb.ToString();
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

    private static string BuildRelativePath(RectTransform root, Transform transform)
    {
        var full = BuildPath(transform);
        var rootPath = BuildPath(root);
        return full.StartsWith(rootPath, StringComparison.Ordinal)
            ? full.Substring(rootPath.Length).TrimStart('/')
            : full;
    }

    private static string StablePathUuid(string path)
    {
        unchecked
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var c in path)
            {
                hash ^= c;
                hash *= prime;
            }

            return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static string CleanText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Trim();
    }

    private static string EscapePipe(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static bool IsTournamentTitle(string text)
    {
        if (!Contains(text, "锦标赛"))
            return false;

        return !Contains(text, "主办锦标赛")
            && !Contains(text, "房间匹配")
            && !Contains(text, "房间码")
            && !Contains(text, "房间代码");
    }

    private static bool IsLobbyCodeTitle(string text)
        => Contains(text, "大厅代码")
            || Contains(text, "大厅码")
            || Contains(text, "Lobby Code")
            || Contains(text, "Lobby ID");

    private static bool Contains(string value, string needle)
        => value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private readonly struct ScreenSpec
    {
        internal ScreenSpec(
            string displayName,
            string key,
            string rootObjectName,
            Func<string, bool> matches)
        {
            DisplayName = displayName;
            Key = key;
            RootObjectName = rootObjectName;
            Matches = matches;
        }

        internal string DisplayName { get; }
        internal string Key { get; }
        internal string RootObjectName { get; }
        internal Func<string, bool> Matches { get; }
    }

    private readonly struct ScreenCapture
    {
        internal ScreenCapture(
            string displayName,
            string key,
            string? rootPath,
            List<ControlCapture> controls)
        {
            DisplayName = displayName;
            Key = key;
            RootPath = rootPath;
            Controls = controls;
        }

        internal string DisplayName { get; }
        internal string Key { get; }
        internal string? RootPath { get; }
        internal List<ControlCapture> Controls { get; }
    }

    private readonly struct ControlCapture
    {
        internal ControlCapture(
            string name,
            string runtimeUuid,
            string pathUuid,
            string path,
            string relativePath,
            bool activeSelf,
            bool activeInHierarchy,
            string layer,
            string components,
            string text,
            string interactable,
            string rect)
        {
            Name = name;
            RuntimeUuid = runtimeUuid;
            PathUuid = pathUuid;
            Path = path;
            RelativePath = relativePath;
            ActiveSelf = activeSelf;
            ActiveInHierarchy = activeInHierarchy;
            Layer = layer;
            Components = components;
            Text = text;
            Interactable = interactable;
            Rect = rect;
        }

        internal string Name { get; }
        internal string RuntimeUuid { get; }
        internal string PathUuid { get; }
        internal string Path { get; }
        internal string RelativePath { get; }
        internal bool ActiveSelf { get; }
        internal bool ActiveInHierarchy { get; }
        internal string Layer { get; }
        internal string Components { get; }
        internal string Text { get; }
        internal string Interactable { get; }
        internal string Rect { get; }
    }
}
