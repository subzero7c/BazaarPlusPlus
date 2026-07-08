#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.JbsServer.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.JbsServer.Game.TournamentRoom;

internal sealed class TournamentTitleToggleController : MonoBehaviour
{
    private const string LogCategory = "TournamentTitleToggle";
    private const string ObjectName = "JBS_TournamentRoomMatchingToggle";
    private const float ScanInterval = 0.4f;
    private const float ButtonWidth = 280f;
    private const float ButtonHeight = 72f;
    private const float ButtonGap = 24f;

    private RectTransform? _buttonRect;
    private RectTransform? _currentParent;
    private TextMeshProUGUI? _label;
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
            _currentParent = null;
            return;
        }

        var parent = placement.Parent;
        if (_buttonRect == null || _buttonRect.parent != parent || _currentParent != parent)
            CreateButton(parent);

        if (_buttonRect == null)
            return;

        _currentParent = parent;
        _buttonRect.gameObject.SetActive(parent.gameObject.activeInHierarchy);
        _buttonRect.SetAsLastSibling();

        var anchor = placement.Title.rectTransform;
        var anchorCorners = new Vector3[4];
        anchor.GetWorldCorners(anchorCorners);
        var titleTopRight = parent.InverseTransformPoint(anchorCorners[2]);
        var titleBottomRight = parent.InverseTransformPoint(anchorCorners[3]);
        var anchorCenterY = (titleTopRight.y + titleBottomRight.y) * 0.5f;
        var desiredCenterX = titleTopRight.x + ButtonGap + ButtonWidth * 0.5f;
        var maxCenterX = parent.rect.xMax - ButtonWidth * 0.5f - 8f;
        var centerX = Mathf.Min(desiredCenterX, maxCenterX);

        _buttonRect.localPosition = new Vector3(centerX, anchorCenterY, 0f);
        _buttonRect.localRotation = Quaternion.identity;
        _buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
    }

    private void CreateButton(RectTransform parent)
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
        button.onClick.AddListener(OnToggleClicked);

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
        JbsTmpFont.TryApply(_label, "房间匹配打开关闭");

        JbsLog.Info(LogCategory, $"Attached tournament room matching toggle inside '{parent.name}'");
        RefreshVisual();
    }

    private void OnToggleClicked()
    {
        var next = !JbsConfig.Enabled;
        JbsConfig.SetEnabled(next);
        if (next)
            TournamentRoomPanel.OpenFromDockButton(_buttonRect);
        else
            TournamentRoomPanel.Close();

        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (_buttonRect == null || _label == null)
            return;

        var enabled = JbsConfig.Enabled;
        _label.text = enabled ? "房间匹配  打开" : "房间匹配  关闭";
        _label.color = enabled
            ? new Color(0.88f, 1f, 0.78f, 1f)
            : new Color(0.88f, 0.88f, 0.92f, 1f);

        var image = _buttonRect.GetComponent<Image>();
        if (image != null)
        {
            image.color = enabled
                ? new Color(0.18f, 0.38f, 0.20f, 0.96f)
                : new Color(0.28f, 0.28f, 0.32f, 0.94f);
        }

        var outline = _buttonRect.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = enabled
                ? new Color(0.55f, 0.95f, 0.45f, 0.9f)
                : new Color(0f, 0f, 0f, 0.55f);
        }
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
            if (GetHierarchyPath(rect).Contains("Tournament_Module_P", StringComparison.Ordinal))
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

    private static string GetHierarchyPath(Transform transform)
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
