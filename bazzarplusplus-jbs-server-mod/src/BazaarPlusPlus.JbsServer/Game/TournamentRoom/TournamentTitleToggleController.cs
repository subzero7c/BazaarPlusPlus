#pragma warning disable CS0436
#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.JbsServer.Game.TournamentRoom;

internal sealed class TournamentTitleToggleController : MonoBehaviour
{
    private const string LogCategory = "TournamentTitleToggle";
    private const string ObjectName = "JBS_TournamentTitleToggle";
    private const string TestButtonObjectName = "JBS_TournamentTitleTestButton";
    private const float ScanInterval = 0.4f;
    private const float ButtonWidth = 136f;
    private const float ButtonHeight = 58f;
    private const float TestButtonWidth = 116f;
    private const float TestButtonHeight = 58f;
    private const float ButtonGap = 18f;
    private const string TestRoomCode = "abcde";

    private RectTransform? _buttonRect;
    private RectTransform? _testButtonRect;
    private RectTransform? _currentParent;
    private TextMeshProUGUI? _label;
    private TextMeshProUGUI? _testLabel;
    private float _nextScanTime;

    internal static void Ensure(GameObject host)
    {
        if (host.GetComponent<TournamentTitleToggleController>() == null)
            host.AddComponent<TournamentTitleToggleController>();
    }

    private void OnEnable()
    {
        JbsConfig.EnabledChanged += OnEnabledChanged;
    }

    private void OnDisable()
    {
        JbsConfig.EnabledChanged -= OnEnabledChanged;
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + ScanInterval;
        EnsureToggle();
        RefreshVisual();
    }

    private void OnEnabledChanged(bool _)
    {
        RefreshVisual();
    }

    private void EnsureToggle()
    {
        var placement = FindTournamentHostOrJoinPlacement();
        if (placement == null)
        {
            if (_buttonRect != null)
                _buttonRect.gameObject.SetActive(false);
            if (_testButtonRect != null)
                _testButtonRect.gameObject.SetActive(false);
            _currentParent = null;
            return;
        }

        var parent = placement.Parent;
        var title = placement.Title;

        if (_buttonRect == null || _buttonRect.parent != parent || _currentParent != parent)
            CreateButton(parent, title);
        if (JbsConfig.ShowDebugTestButton && (_testButtonRect == null || _testButtonRect.parent != parent || _currentParent != parent))
            CreateTestButton(parent, title);

        if (_buttonRect == null)
            return;

        _currentParent = parent;
        var active = parent.gameObject.activeInHierarchy;
        _buttonRect.gameObject.SetActive(active);
        _buttonRect.SetAsLastSibling();
        if (_testButtonRect != null)
        {
            _testButtonRect.gameObject.SetActive(active && JbsConfig.ShowDebugTestButton);
            if (JbsConfig.ShowDebugTestButton)
                _testButtonRect.SetAsLastSibling();
        }

        var anchor = placement.JoinInput != null
            ? placement.JoinInput
            : title.rectTransform;
        var anchorCorners = new Vector3[4];
        anchor.GetWorldCorners(anchorCorners);
        var parentTopRight = parent.InverseTransformPoint(anchorCorners[2]);
        var parentBottomRight = parent.InverseTransformPoint(anchorCorners[3]);
        var parentLeft = parent.InverseTransformPoint(anchorCorners[0]).x;
        var anchorCenterY = (parentTopRight.y + parentBottomRight.y) * 0.5f;

        var testWidth = JbsConfig.ShowDebugTestButton ? TestButtonWidth + ButtonGap : 0f;
        var totalWidth = testWidth + ButtonWidth;
        var rightX = parentTopRight.x;
        var startX = Mathf.Max(parentLeft, rightX - totalWidth);
        var toggleCenterX = startX + testWidth + ButtonWidth * 0.5f;

        if (_testButtonRect != null && JbsConfig.ShowDebugTestButton)
        {
            var testCenterX = startX + TestButtonWidth * 0.5f;
            _testButtonRect.localPosition = new Vector3(testCenterX, anchorCenterY, 0f);
            _testButtonRect.localRotation = Quaternion.identity;
            _testButtonRect.sizeDelta = new Vector2(TestButtonWidth, TestButtonHeight);
        }

        _buttonRect.localPosition = new Vector3(toggleCenterX, anchorCenterY, 0f);
        _buttonRect.localRotation = Quaternion.identity;
        _buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
    }

    private void CreateButton(RectTransform parent, TextMeshProUGUI title)
    {
        if (_buttonRect != null)
            Destroy(_buttonRect.gameObject);

        var go = new GameObject(ObjectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        _buttonRect = go.GetComponent<RectTransform>();
        _buttonRect.SetParent(parent, worldPositionStays: false);
        _buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        _buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        _buttonRect.pivot = new Vector2(0.5f, 0.5f);
        _buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

        var image = go.GetComponent<Image>();
        image.color = new Color(0.18f, 0.25f, 0.40f, 0.96f);
        image.raycastTarget = true;

        var outline = go.GetComponent<Outline>();
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;

        var button = go.GetComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = image;
        button.interactable = true;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.95f, 0.84f, 0.54f, 1f),
            pressedColor = new Color(0.72f, 0.60f, 0.34f, 1f),
            selectedColor = new Color(0.95f, 0.84f, 0.54f, 1f),
            disabledColor = new Color(1f, 1f, 1f, 0.34f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => JbsConfig.SetEnabled(!JbsConfig.Enabled));

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.SetParent(_buttonRect, worldPositionStays: false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        _label = labelGo.GetComponent<TextMeshProUGUI>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 24f;
        _label.fontStyle = FontStyles.Bold;
        _label.raycastTarget = false;
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        if (title.font != null)
        {
            _label.font = title.font;
            _label.fontSharedMaterial = title.fontSharedMaterial;
        }

        JbsLog.Info(LogCategory, $"Attached tournament toggle inside '{parent.name}'");
        RefreshVisual();
    }

    private void CreateTestButton(RectTransform parent, TextMeshProUGUI title)
    {
        if (_testButtonRect != null)
            Destroy(_testButtonRect.gameObject);

        var go = new GameObject(TestButtonObjectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        _testButtonRect = go.GetComponent<RectTransform>();
        _testButtonRect.SetParent(parent, worldPositionStays: false);
        _testButtonRect.anchorMin = new Vector2(0.5f, 0.5f);
        _testButtonRect.anchorMax = new Vector2(0.5f, 0.5f);
        _testButtonRect.pivot = new Vector2(0.5f, 0.5f);
        _testButtonRect.sizeDelta = new Vector2(TestButtonWidth, TestButtonHeight);

        var image = go.GetComponent<Image>();
        image.color = new Color(0.22f, 0.30f, 0.42f, 0.96f);
        image.raycastTarget = true;

        var outline = go.GetComponent<Outline>();
        outline.effectDistance = new Vector2(2f, -2f);
        outline.effectColor = new Color(0.85f, 0.72f, 0.42f, 0.85f);
        outline.useGraphicAlpha = true;

        var button = go.GetComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = image;
        button.interactable = true;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.95f, 0.84f, 0.54f, 1f),
            pressedColor = new Color(0.72f, 0.60f, 0.34f, 1f),
            selectedColor = new Color(0.95f, 0.84f, 0.54f, 1f),
            disabledColor = new Color(1f, 1f, 1f, 0.34f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(FillTestRoomCode);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.SetParent(_testButtonRect, worldPositionStays: false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        _testLabel = labelGo.GetComponent<TextMeshProUGUI>();
        _testLabel.text = "测试";
        _testLabel.alignment = TextAlignmentOptions.Center;
        _testLabel.fontSize = 24f;
        _testLabel.fontStyle = FontStyles.Bold;
        _testLabel.color = new Color(0.95f, 0.92f, 0.82f, 1f);
        _testLabel.raycastTarget = false;
        _testLabel.textWrappingMode = TextWrappingModes.NoWrap;
        if (title.font != null)
        {
            _testLabel.font = title.font;
            _testLabel.fontSharedMaterial = title.fontSharedMaterial;
        }

        JbsLog.Info(LogCategory, $"Attached test room-code button inside '{parent.name}'");
    }

    private void RefreshVisual()
    {
        if (_buttonRect == null || _label == null)
            return;

        var image = _buttonRect.GetComponent<Image>();
        var outline = _buttonRect.GetComponent<Outline>();
        if (JbsConfig.Enabled)
        {
            _label.text = "ON";
            _label.text = "匹配开";
            _label.color = new Color(0.88f, 1f, 0.78f, 1f);
            if (image != null) image.color = new Color(0.18f, 0.38f, 0.20f, 0.96f);
            if (outline != null) outline.effectColor = new Color(0.55f, 0.95f, 0.45f, 0.9f);
        }
        else
        {
            _label.text = "匹配关";
            _label.color = new Color(0.88f, 0.88f, 0.92f, 1f);
            if (image != null) image.color = new Color(0.28f, 0.28f, 0.32f, 0.94f);
            if (outline != null) outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
        }
    }

    private static TextMeshProUGUI? FindTournamentTitle()
    {
        TextMeshProUGUI? best = null;
        var bestScore = float.MinValue;
        foreach (var text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (text == null || !text.gameObject.activeInHierarchy)
                continue;

            var value = (text.text ?? string.Empty).Trim();
            if (!value.Contains("锦标赛", StringComparison.Ordinal))
                continue;

            var score = text.fontSize;
            if (string.Equals(value, "锦标赛", StringComparison.Ordinal))
                score += 1000f;
            if (text.transform.GetComponentInParent<Canvas>() != null)
                score += 100f;

            if (score > bestScore)
            {
                bestScore = score;
                best = text;
            }
        }

        return best;
    }

    private static TournamentHostOrJoinPlacement? FindTournamentHostOrJoinPlacement()
    {
        RectTransform? bestRoot = null;
        var bestScore = float.MinValue;
        foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
                continue;
            if (!string.Equals(rect.gameObject.name, "Section_HostOrJoin", StringComparison.Ordinal))
                continue;

            var score = 1000f;
            if (rect.GetComponentInParent<Canvas>() != null)
                score += 100f;
            if (rect.Find("Block_Join/Input_Code") != null)
                score += 250f;
            if (rect.Find("Text_Tournament") != null)
                score += 250f;
            if (rect.GetHierarchyPath().Contains("Tournament_Module_P", StringComparison.Ordinal))
                score += 500f;

            if (score > bestScore)
            {
                bestScore = score;
                bestRoot = rect;
            }
        }

        if (bestRoot == null)
            return null;

        var title = FindChildText(bestRoot, "Text_Tournament")
            ?? FindTournamentTitleInside(bestRoot);
        if (title == null)
            return null;

        var joinInput = bestRoot.Find("Block_Join/Input_Code") as RectTransform;
        return new TournamentHostOrJoinPlacement(bestRoot, title, joinInput);
    }

    private static TextMeshProUGUI? FindChildText(RectTransform root, string childName)
    {
        var child = root.Find(childName);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private static TextMeshProUGUI? FindTournamentTitleInside(RectTransform root)
    {
        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: false))
        {
            var value = (text.text ?? string.Empty).Trim();
            if (string.Equals(value, "锦标赛", StringComparison.Ordinal)
                || string.Equals(value, "Tournament", StringComparison.OrdinalIgnoreCase))
                return text;
        }

        return null;
    }

    private void FillTestRoomCode()
    {
        var placement = FindTournamentHostOrJoinPlacement();
        var canvas = placement?.Parent.GetComponentInParent<Canvas>();
        var tmpInput = FindBestRoomCodeTmpInput(canvas);
        if (tmpInput != null)
        {
            tmpInput.text = TestRoomCode;
            tmpInput.ActivateInputField();
            tmpInput.MoveTextEnd(false);
            JbsLog.Info(LogCategory, $"Filled TMP room-code input with '{TestRoomCode}'");
            return;
        }

        var legacyInput = FindBestRoomCodeLegacyInput(canvas);
        if (legacyInput != null)
        {
            legacyInput.text = TestRoomCode;
            legacyInput.ActivateInputField();
            JbsLog.Info(LogCategory, $"Filled legacy room-code input with '{TestRoomCode}'");
            return;
        }

        JbsLog.Warn(LogCategory, "No visible room-code input found for test fill");
    }

    private static TMP_InputField? FindBestRoomCodeTmpInput(Canvas? canvas)
    {
        var label = FindRoomCodeLabel(canvas);
        TMP_InputField? best = null;
        var bestScore = float.MaxValue;
        foreach (var input in Resources.FindObjectsOfTypeAll<TMP_InputField>())
        {
            if (!IsUsableInput(input, canvas))
                continue;

            var score = ScoreInput(input.transform as RectTransform, label);
            if (score < bestScore)
            {
                bestScore = score;
                best = input;
            }
        }

        return best;
    }

    private static InputField? FindBestRoomCodeLegacyInput(Canvas? canvas)
    {
        var label = FindRoomCodeLabel(canvas);
        InputField? best = null;
        var bestScore = float.MaxValue;
        foreach (var input in Resources.FindObjectsOfTypeAll<InputField>())
        {
            if (!IsUsableInput(input, canvas))
                continue;

            var score = ScoreInput(input.transform as RectTransform, label);
            if (score < bestScore)
            {
                bestScore = score;
                best = input;
            }
        }

        return best;
    }

    private static TextMeshProUGUI? FindRoomCodeLabel(Canvas? canvas)
    {
        TextMeshProUGUI? best = null;
        var bestScore = float.MinValue;
        foreach (var text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (text == null || !text.gameObject.activeInHierarchy)
                continue;
            if (canvas != null && text.GetComponentInParent<Canvas>() != canvas)
                continue;

            var value = (text.text ?? string.Empty).Trim();
            if (!LooksLikeRoomCodeLabel(value))
                continue;

            var score = text.fontSize;
            if (value.Contains("房间代码", StringComparison.Ordinal)
                || value.Contains("房间码", StringComparison.Ordinal)
                || value.Contains("Room Code", StringComparison.OrdinalIgnoreCase))
                score += 1000f;

            if (score > bestScore)
            {
                bestScore = score;
                best = text;
            }
        }

        return best;
    }

    private static bool LooksLikeRoomCodeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("房间代码", StringComparison.Ordinal)
            || value.Contains("房间码", StringComparison.Ordinal)
            || value.Contains("代码", StringComparison.Ordinal)
            || value.Contains("Room Code", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Code", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsableInput(Selectable input, Canvas? canvas)
    {
        if (input == null || !input.gameObject.activeInHierarchy || !input.interactable)
            return false;
        if (canvas != null && input.GetComponentInParent<Canvas>() != canvas)
            return false;
        var rect = input.transform as RectTransform;
        return rect == null || rect.rect.width > 0.01f && rect.rect.height > 0.01f;
    }

    private static float ScoreInput(RectTransform? inputRect, TextMeshProUGUI? label)
    {
        if (inputRect == null)
            return 1_000_000f;
        if (label == null)
            return inputRect.GetSiblingIndex();

        var labelRect = label.rectTransform;
        var distance = Vector3.Distance(inputRect.position, labelRect.position);
        var directionBonus = inputRect.position.x >= labelRect.position.x ? -120f : 0f;
        return distance + directionBonus;
    }

    private sealed class TournamentHostOrJoinPlacement
    {
        internal TournamentHostOrJoinPlacement(
            RectTransform parent,
            TextMeshProUGUI title,
            RectTransform? joinInput)
        {
            Parent = parent;
            Title = title;
            JoinInput = joinInput;
        }

        internal RectTransform Parent { get; }
        internal TextMeshProUGUI Title { get; }
        internal RectTransform? JoinInput { get; }
    }
}

internal static class TournamentTitleTransformExtensions
{
    internal static string GetHierarchyPath(this Transform transform)
    {
        var path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
