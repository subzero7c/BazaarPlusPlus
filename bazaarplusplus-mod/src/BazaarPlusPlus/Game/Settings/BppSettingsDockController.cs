#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Game.Settings.Visual;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed partial class BppSettingsDockController
    : MonoBehaviour,
        IBppNativeSettingsButtonCloneOwner
{
    private const string LogCategory = "BppSettingsDock";
    private const string HeaderObjectName = BppSettingsDockVisualConstants.HeaderObjectName;

    private readonly List<DockSettingRowView> _rows = [];

    private Button? _anchorButton;
    private Button? _dockButton;
    private RectTransform? _dockButtonRect;
    private RectTransform? _panelRoot;
    private TextMeshProUGUI? _headerLabel;
    private TMP_FontAsset? _uiFont;
    private Material? _uiFontMaterial;
    private bool _isExpanded;
    private int _screenshotSuppressionCount;
    private BppSettingsDockPlacement _placement;
    private readonly BazaarGameEnvironmentProbe _gameEnvironmentProbe = new();
    private static bool _fontResolutionLogged;

    internal static void Attach(Button anchorButton, BppSettingsDockPlacement placement)
    {
        if (anchorButton == null)
            return;

        var existingController = anchorButton.GetComponent<BppSettingsDockController>();
        if (existingController != null && existingController._dockButtonRect != null)
        {
            existingController.SyncDockButtonPlacement();
            existingController.ApplyScreenshotSuppressionVisibility();
            return;
        }

        var dockButton = BppNativeSettingsButtonClone.FindOrCreate(anchorButton, placement);
        if (dockButton == null)
            return;

        var controller =
            existingController ?? anchorButton.gameObject.AddComponent<BppSettingsDockController>();
        controller.Initialize(anchorButton, placement, dockButton);
    }

    internal static IDisposable? BeginScreenshotSuppression()
    {
        var controllers = UnityEngine.Object.FindObjectsOfType<BppSettingsDockController>(
            includeInactive: true
        );
        if (controllers.Length == 0)
            return null;

        var suppressionActions = new Func<IDisposable?>[controllers.Length];
        for (var index = 0; index < controllers.Length; index++)
            suppressionActions[index] = controllers[index].BeginInstanceScreenshotSuppression;

        return UiSuppressionScope.Begin(suppressionActions);
    }

    internal static void RefreshAll()
    {
        foreach (
            var controller in UnityEngine.Object.FindObjectsOfType<BppSettingsDockController>(
                includeInactive: true
            )
        )
            controller.RefreshView();
    }

    private void Initialize(
        Button anchorButton,
        BppSettingsDockPlacement placement,
        RectTransform dockButton
    )
    {
        _anchorButton = anchorButton;
        _placement = placement;
        _dockButtonRect = dockButton;
        _dockButton = dockButton.GetComponent<Button>();
        if (_dockButton == null)
            return;

        ResolveTextStyle();
        _dockButton.onClick.RemoveAllListeners();
        _dockButton.onClick.AddListener(OnDockButtonClicked);

        SyncDockButtonPlacement();
        ApplyScreenshotSuppressionVisibility();

        if (!TryEnsurePanel())
            return;

        RefreshView();
        SetExpanded(false);
    }

    private void OnEnable()
    {
        ApplyScreenshotSuppressionVisibility();
        RefreshView();
    }

    private void OnDisable()
    {
        SetExpanded(false);
    }

    private IDisposable BeginInstanceScreenshotSuppression()
    {
        _screenshotSuppressionCount++;
        ApplyScreenshotSuppressionVisibility();
        return new ScreenshotSuppressionLease(this);
    }

    private void EndInstanceScreenshotSuppression()
    {
        if (_screenshotSuppressionCount > 0)
            _screenshotSuppressionCount--;

        ApplyScreenshotSuppressionVisibility();
    }

    private void ApplyScreenshotSuppressionVisibility()
    {
        var shouldBeVisible = _screenshotSuppressionCount == 0;
        if (_dockButtonRect != null && _dockButtonRect.gameObject.activeSelf != shouldBeVisible)
            _dockButtonRect.gameObject.SetActive(shouldBeVisible);
    }

    private bool TryEnsurePanel()
    {
        if (_dockButtonRect == null)
            return false;

        var existingPanel = _dockButtonRect.Find(_placement.PanelObjectName) as RectTransform;
        if (existingPanel != null)
        {
            _panelRoot = existingPanel;
            ConfigurePanelRect(existingPanel, _placement);
            ConfigurePanelVisual(existingPanel.gameObject);
            _headerLabel = existingPanel.Find(HeaderObjectName)?.GetComponent<TextMeshProUGUI>();
            if (_headerLabel == null)
            {
                BppLog.Warn(LogCategory, "BPP settings panel header was missing.");
                return false;
            }

            EnsureRows();
            ConfigureHeaderRect(_headerLabel.rectTransform);
            RefreshRowLayouts();
            return true;
        }

        var panelObject = new GameObject(
            _placement.PanelObjectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline)
        );
        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(_dockButtonRect, worldPositionStays: false);
        ConfigurePanelRect(panelRect, _placement);
        ConfigurePanelVisual(panelObject);

        _headerLabel = CreateText(
            HeaderObjectName,
            panelRect,
            BppSettingsDockVisualConstants.HeaderFontSize,
            TextAlignmentOptions.Left,
            BppSettingsDockVisualConstants.HeaderTextColor
        );
        if (_headerLabel == null)
        {
            BppLog.Warn(LogCategory, "Failed to create BPP settings dock header.");
            return false;
        }

        _panelRoot = panelRect;
        EnsureRows();
        ConfigureHeaderRect(_headerLabel.rectTransform);
        RefreshRowLayouts();
        return true;
    }

    private void EnsureRows()
    {
        if (_panelRoot == null)
            return;

        while (_rows.Count < BppSettingsDockCatalog.Definitions.Count)
        {
            var rowIndex = _rows.Count;
            _rows.Add(CreateRow(BppSettingsDockCatalog.Definitions[rowIndex], rowIndex));
        }
    }

    private void RefreshRowLayouts()
    {
        for (var index = 0; index < _rows.Count; index++)
            ConfigureRowRect(_rows[index].RectTransform, index);
    }

    private DockSettingRowView CreateRow(BppSettingsDockDefinition definition, int index)
    {
        if (_panelRoot == null)
            throw new InvalidOperationException("Panel root was unavailable.");

        var rowObject = new GameObject(
            $"BPP_SettingsDockRow_{definition.Key}",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(Outline)
        );
        var rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.SetParent(_panelRoot, worldPositionStays: false);
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        ConfigureRowRect(rowRect, index);

        var background = rowObject.GetComponent<Image>();
        background.raycastTarget = true;

        var outline = rowObject.GetComponent<Outline>();
        outline.effectDistance = BppSettingsDockVisualConstants.RowOutlineDistance;
        outline.useGraphicAlpha = true;

        var button = rowObject.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = background;
        button.onClick.AddListener(() => ActivateDefinition(definition));

        var label = CreateText(
            "Label",
            rowRect,
            BppSettingsDockVisualConstants.RowLabelFontSize,
            TextAlignmentOptions.Left,
            BppSettingsDockVisualConstants.RowLabelActiveColor
        );
        if (label == null)
            throw new InvalidOperationException(
                $"Failed to create row label for {definition.Key}."
            );

        BppSettingsDockVisualConstants.ConfigureLabelRect(label.rectTransform);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        var status = CreateText("Status", rowRect, BppSettingsDockVisualConstants.RowStatusFontSize, TextAlignmentOptions.Center, Color.white);
        if (status == null)
            throw new InvalidOperationException(
                $"Failed to create row status label for {definition.Key}."
            );

        BppSettingsDockVisualConstants.ConfigureStatusRect(status.rectTransform);
        status.textWrappingMode = TextWrappingModes.NoWrap;

        return new DockSettingRowView(definition, rowRect, background, outline, label, status);
    }

    private void OnDockButtonClicked()
    {
        SetExpanded(!_isExpanded);
    }

    private void OnRectTransformDimensionsChange()
    {
        SyncDockButtonPlacement();
    }

    private void SetExpanded(bool expanded)
    {
        _isExpanded = expanded;
        if (_panelRoot != null)
        {
            if (expanded)
                RefreshView();

            _panelRoot.gameObject.SetActive(expanded);
        }
    }

    private void ActivateDefinition(BppSettingsDockDefinition definition)
    {
        definition.Activate();
        if (definition.CollapseAfterActivate)
            SetExpanded(false);

        RefreshAll();
    }

    private void RefreshView()
    {
        if (_headerLabel != null)
        {
            var headerText = ResolveHeader(PlayerPreferences.Data.LanguageCode);
            _headerLabel.text = headerText;
            ApplyTextStyle(_headerLabel, headerText);
        }

        foreach (var row in _rows)
            ApplyRowState(row);
    }

    private void ApplyRowState(DockSettingRowView row)
    {
        var enabled = row.Definition.IsActive();
        row.Label.text = row.Definition.ResolveLabel(PlayerPreferences.Data.LanguageCode);
        row.Status.text = row.Definition.ResolveStatus(PlayerPreferences.Data.LanguageCode);
        ApplyTextStyle(row.Label, row.Label.text);
        ApplyTextStyle(row.Status, row.Status.text);
        row.Background.color = enabled
            ? BppSettingsDockVisualConstants.RowEnabledBackground
            : BppSettingsDockVisualConstants.RowDisabledBackground;
        row.Outline.effectColor = enabled
            ? BppSettingsDockVisualConstants.RowEnabledOutlineColor
            : BppSettingsDockVisualConstants.RowDisabledOutlineColor;
        row.Status.color = enabled
            ? BppSettingsDockVisualConstants.RowStatusEnabledColor
            : BppSettingsDockVisualConstants.RowStatusDisabledColor;
    }

    private void SyncDockButtonPlacement()
    {
        if (_anchorButton == null || _dockButtonRect == null)
            return;

        var parentRect = _dockButtonRect.parent as RectTransform;
        var anchorRect = _anchorButton.transform as RectTransform;
        if (parentRect == null || anchorRect == null)
            return;

        if (_gameEnvironmentProbe.IsTournamentEnvironment())
        {
            _dockButtonRect.localPosition = BppTournamentDockButtonLayout.CalculateLocalPosition(
                parentRect,
                _dockButtonRect,
                BppTournamentDockButtonSlot.SettingsDock
            );
            _dockButtonRect.localRotation = Quaternion.identity;
            _dockButtonRect.SetAsLastSibling();
            return;
        }

        var corners = new Vector3[4];
        anchorRect.GetWorldCorners(corners);

        var centerWorld = (corners[0] + corners[2]) * 0.5f;
        var leftWorld = (corners[0] + corners[1]) * 0.5f;
        var rightWorld = (corners[2] + corners[3]) * 0.5f;
        var topWorld = (corners[1] + corners[2]) * 0.5f;
        var bottomWorld = (corners[0] + corners[3]) * 0.5f;

        var centerLocal = parentRect.InverseTransformPoint(centerWorld);
        var leftLocal = parentRect.InverseTransformPoint(leftWorld);
        var rightLocal = parentRect.InverseTransformPoint(rightWorld);
        var topLocal = parentRect.InverseTransformPoint(topWorld);
        var bottomLocal = parentRect.InverseTransformPoint(bottomWorld);

        var dockPosition = BppSettingsDockGeometry.CalculateDockButtonLocalPosition(
            centerLocal.x,
            centerLocal.y,
            leftLocal.x,
            rightLocal.x,
            topLocal.y,
            bottomLocal.y,
            _dockButtonRect.localPosition.z,
            _placement
        );
        _dockButtonRect.localPosition = new Vector3(dockPosition.X, dockPosition.Y, dockPosition.Z);
        _dockButtonRect.localRotation = Quaternion.identity;
        _dockButtonRect.SetAsLastSibling();
    }

    private sealed class DockSettingRowView
    {
        internal DockSettingRowView(
            BppSettingsDockDefinition definition,
            RectTransform rectTransform,
            Image background,
            Outline outline,
            TextMeshProUGUI label,
            TextMeshProUGUI status
        )
        {
            Definition = definition;
            RectTransform = rectTransform;
            Background = background;
            Outline = outline;
            Label = label;
            Status = status;
        }

        internal BppSettingsDockDefinition Definition { get; }

        internal RectTransform RectTransform { get; }

        internal Image Background { get; }

        internal Outline Outline { get; }

        internal TextMeshProUGUI Label { get; }

        internal TextMeshProUGUI Status { get; }
    }

    private sealed class ScreenshotSuppressionLease(BppSettingsDockController controller)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            controller.EndInstanceScreenshotSuppression();
        }
    }
}
