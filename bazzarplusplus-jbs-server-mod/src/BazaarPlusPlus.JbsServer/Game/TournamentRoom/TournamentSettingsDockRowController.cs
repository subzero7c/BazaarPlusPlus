#pragma warning disable CS0436
#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.JbsServer.Game.TournamentRoom;

/// <summary>
/// Adds the tournament room entry into the BazaarPlusPlus settings dock list
/// (the list that contains BazaarDB 数据共建), instead of creating another
/// floating dock button.
/// </summary>
internal sealed class TournamentSettingsDockRowController : MonoBehaviour
{
    private const string LogCategory = "TournamentSettingsDockRow";
    private const string BppDockButtonPrefix = "BPP_SettingsDockButton_";
    private const string BppDockPanelPrefix = "BPP_SettingsDockPanel_";
    private const string BppRowObjectNamePrefix = "BPP_SettingsDockRow_";
    private const string RowObjectNamePrefix = "JBS_SettingsDockRow_TournamentRoom_";
    private const float PanelPadding = 18f;
    private const float PanelTopPadding = 16f;
    private const float PanelBottomPadding = 28f;
    private const float HeaderHeight = 24f;
    private const float HeaderSpacing = 16f;
    private const float RowHeight = 48f;
    private const float RowSpacing = 12f;
    private const float RowInnerPadding = 16f;
    private const float StatusWidth = 80f;

    private Button? _anchorButton;
    private RectTransform? _rowRect;
    private Image? _background;
    private Outline? _outline;
    private TextMeshProUGUI? _label;
    private TextMeshProUGUI? _status;
    private string _key = string.Empty;
    private float _nextEnsureTime;

    internal static void Attach(Button nativeSettingsButton, string key)
    {
        if (nativeSettingsButton == null)
            return;

        var controller =
            nativeSettingsButton.gameObject.GetComponent<TournamentSettingsDockRowController>()
            ?? nativeSettingsButton.gameObject.AddComponent<TournamentSettingsDockRowController>();
        controller.Initialize(nativeSettingsButton, key);
    }

    private void Initialize(Button anchorButton, string key)
    {
        _anchorButton = anchorButton;
        _key = key;
        EnsureRow();
        RefreshRowState();
    }

    private void OnEnable()
    {
        JbsConfig.EnabledChanged += OnEnabledChanged;
        EnsureRow();
        RefreshRowState();
    }

    private void OnDisable()
    {
        JbsConfig.EnabledChanged -= OnEnabledChanged;
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextEnsureTime)
        {
            _nextEnsureTime = Time.unscaledTime + 0.5f;
            EnsureRow();
        }

        RefreshRowState();
    }

    private void OnEnabledChanged(bool _)
    {
        RefreshRowState();
    }

    private void EnsureRow()
    {
        if (_anchorButton == null)
            return;

        var hostRect = _anchorButton.transform.parent as RectTransform;
        if (hostRect == null)
            return;

        var dockButton = hostRect.Find($"{BppDockButtonPrefix}{_key}") as RectTransform;
        var panel = dockButton?.Find($"{BppDockPanelPrefix}{_key}") as RectTransform;
        if (panel == null)
            return;

        var rowName = $"{RowObjectNamePrefix}{_key}";
        var existing = panel.Find(rowName) as RectTransform;
        if (existing != null)
        {
            CaptureRow(existing);
            RefreshPanelLayout(panel);
            return;
        }

        CreateRow(panel, rowName);
        RefreshPanelLayout(panel);
        RefreshRowState();
    }

    private void CreateRow(RectTransform panel, string rowName)
    {
        try
        {
            var rowObject = new GameObject(
                rowName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(Outline)
            );
            var rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.SetParent(panel, worldPositionStays: false);
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);

            var background = rowObject.GetComponent<Image>();
            background.raycastTarget = true;

            var outline = rowObject.GetComponent<Outline>();
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            var button = rowObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.targetGraphic = background;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnRowClicked);

            _rowRect = rowRect;
            _background = background;
            _outline = outline;
            _label = CreateText("Label", rowRect, 19f, TextAlignmentOptions.Left);
            _status = CreateText("Status", rowRect, 17f, TextAlignmentOptions.Center);

            ConfigureTextRects();
        }
        catch (Exception ex)
        {
            JbsLog.Error(LogCategory, $"Failed to create tournament row for '{_key}'", ex);
        }
    }

    private void CaptureRow(RectTransform rowRect)
    {
        _rowRect = rowRect;
        _background = rowRect.GetComponent<Image>();
        _outline = rowRect.GetComponent<Outline>();
        _label = rowRect.Find("Label")?.GetComponent<TextMeshProUGUI>();
        _status = rowRect.Find("Status")?.GetComponent<TextMeshProUGUI>();

        var button = rowRect.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnRowClicked);
        }
    }

    private TextMeshProUGUI CreateText(
        string objectName,
        RectTransform parent,
        float fontSize,
        TextAlignmentOptions alignment
    )
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, worldPositionStays: false);

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        ApplyTemplateFont(text);
        return text;
    }

    private void ConfigureTextRects()
    {
        if (_label != null)
            ConfigureLabelRect(_label.rectTransform);

        if (_status != null)
            ConfigureStatusRect(_status.rectTransform);
    }

    private void RefreshPanelLayout(RectTransform panel)
    {
        var rowIndex = 0;
        for (var index = 0; index < panel.childCount; index++)
        {
            var child = panel.GetChild(index) as RectTransform;
            if (child == null)
                continue;

            var childName = child.name;
            if (!childName.StartsWith(BppRowObjectNamePrefix, StringComparison.Ordinal)
                && !childName.StartsWith(RowObjectNamePrefix, StringComparison.Ordinal))
                continue;

            ConfigureRowRect(child, rowIndex);
            rowIndex++;
        }

        panel.sizeDelta = new Vector2(panel.sizeDelta.x, CalculatePanelHeight(rowIndex));
    }

    private static void ConfigureRowRect(RectTransform rowRect, int index)
    {
        var rowTop = PanelTopPadding + HeaderHeight + HeaderSpacing + (index * (RowHeight + RowSpacing));
        rowRect.offsetMin = new Vector2(PanelPadding, -(rowTop + RowHeight));
        rowRect.offsetMax = new Vector2(-PanelPadding, -rowTop);
    }

    private static float CalculatePanelHeight(int rowCount)
    {
        var rowsHeight = rowCount > 0
            ? (rowCount * RowHeight) + ((rowCount - 1) * RowSpacing)
            : 0f;
        return PanelTopPadding + HeaderHeight + HeaderSpacing + rowsHeight + PanelBottomPadding;
    }

    private static void ConfigureLabelRect(RectTransform labelRect)
    {
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.offsetMin = new Vector2(RowInnerPadding, 0f);
        labelRect.offsetMax = new Vector2(-(StatusWidth + RowInnerPadding + 8f), 0f);
    }

    private static void ConfigureStatusRect(RectTransform statusRect)
    {
        statusRect.anchorMin = new Vector2(1f, 0.5f);
        statusRect.anchorMax = new Vector2(1f, 0.5f);
        statusRect.pivot = new Vector2(1f, 0.5f);
        statusRect.sizeDelta = new Vector2(StatusWidth, RowHeight);
        statusRect.anchoredPosition = new Vector2(-RowInnerPadding, 0f);
    }

    private void RefreshRowState()
    {
        if (_label == null || _status == null)
            return;

        var enabled = JbsConfig.Enabled;
        var visible = TournamentRoomPanel.IsVisible;

        _label.text = JbsLocalization.Get("tournament.title");
        _status.text = enabled ? JbsLocalization.Get("tournament.status.enabled") : JbsLocalization.Get("tournament.status.disabled");

        if (_background != null)
        {
            _background.color = visible
                ? new Color(0.23f, 0.35f, 0.22f, 0.94f)
                : new Color(0.19f, 0.19f, 0.22f, 0.92f);
        }

        if (_outline != null)
        {
            _outline.effectColor = visible
                ? new Color(0.78f, 0.86f, 0.46f, 0.70f)
                : new Color(0f, 0f, 0f, 0.45f);
        }

        _label.color = enabled
            ? new Color(0.93f, 0.93f, 0.95f, 1f)
            : new Color(0.75f, 0.78f, 0.82f, 0.98f);
        _status.color = visible
            ? new Color(0.90f, 0.97f, 0.78f, 1f)
            : new Color(0.75f, 0.78f, 0.82f, 0.98f);
    }

    private void OnRowClicked()
    {
        if (!JbsConfig.Enabled)
            JbsConfig.SetEnabled(true);

        var source = _rowRect ?? _anchorButton?.transform;
        if (TournamentRoomPanel.IsVisibleFrom(source))
            TournamentRoomPanel.Close();
        else
            TournamentRoomPanel.OpenFromDockButton(source);

        HideParentPanel();
        RefreshRowState();
    }

    private void HideParentPanel()
    {
        if (_rowRect?.parent is RectTransform panel)
            panel.gameObject.SetActive(false);
    }

    private void ApplyTemplateFont(TextMeshProUGUI text)
    {
        if (_anchorButton != null)
        {
            var template = _anchorButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (template?.font != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
                return;
            }
        }

        var existingText = FindObjectOfType<TextMeshProUGUI>();
        if (existingText?.font != null)
        {
            text.font = existingText.font;
            text.fontSharedMaterial = existingText.fontSharedMaterial;
        }
    }

}
