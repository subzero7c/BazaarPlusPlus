#pragma warning disable CS0436
#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.JbsServer.Game.TournamentRoom;

/// <summary>
/// Enable / disable toggle button for the tournament room feature.
/// Placed BELOW the BPP settings dock button (or below the tournament dock button
/// when BPP is not installed), providing quick access to toggle the feature on/off.
///
/// Shows "锦标赛:ON" or "锦标赛:OFF" to indicate current state.
/// </summary>
internal sealed class TournamentEnableToggleController : MonoBehaviour
{
    private const string LogCategory = "TournamentEnableToggle";
    private const string BppSettingsDockButtonPrefix = "BPP_SettingsDockButton_";
    private const string JbsTournamentButtonPrefix = "JBS_TournamentButton_";
    private const float ButtonGap = 10f;

    private Button? _anchorButton;
    private Button? _toggleButton;
    private RectTransform? _toggleButtonRect;
    private TextMeshProUGUI? _stateLabel;
    private string _placementKey = string.Empty;

    internal static void Attach(Button nativeSettingsButton, string key)
    {
        if (nativeSettingsButton == null)
            return;

        var objectName = $"JBS_EnableToggle_{key}";
        var hostRect = nativeSettingsButton.transform.parent as RectTransform;
        if (hostRect == null)
            return;

        if (hostRect.Find(objectName) != null)
            return;

        var clone = CloneAsToggleButton(nativeSettingsButton, hostRect, objectName);
        if (clone == null)
            return;

        var existing =
            nativeSettingsButton.gameObject.GetComponent<TournamentEnableToggleController>()
            ?? nativeSettingsButton.gameObject.AddComponent<TournamentEnableToggleController>();
        existing.Initialize(nativeSettingsButton, clone, key);
    }

    private void Initialize(Button anchor, RectTransform toggleButton, string key)
    {
        _anchorButton = anchor;
        _toggleButtonRect = toggleButton;
        _toggleButton = toggleButton.GetComponent<Button>();
        _placementKey = key;

        if (_toggleButton == null)
        {
            JbsLog.Warn(LogCategory, "Cloned toggle button has no Button component");
            return;
        }

        _stateLabel = FindStateLabelIn(toggleButton.gameObject);

        _toggleButton.onClick.RemoveAllListeners();
        _toggleButton.onClick.AddListener(OnToggleClicked);

        JbsConfig.EnabledChanged += OnEnabledChanged;

        SyncPlacement();
        RefreshLabel();
    }

    private void OnDestroy()
    {
        JbsConfig.EnabledChanged -= OnEnabledChanged;
    }

    private void OnRectTransformDimensionsChange()
    {
        SyncPlacement();
    }

    private void OnToggleClicked()
    {
        JbsConfig.SetEnabled(!JbsConfig.Enabled);
    }

    private void OnEnabledChanged(bool _)
    {
        RefreshLabel();
        RefreshButtonColors();
    }

    private void RefreshLabel()
    {
        if (_stateLabel == null)
            return;

        _stateLabel.text = JbsConfig.Enabled ? "锦标赛\nON" : "锦标赛\nOFF";
        _stateLabel.color = JbsConfig.Enabled
            ? new Color(0.75f, 0.97f, 0.65f, 1f)
            : new Color(0.75f, 0.78f, 0.82f, 0.85f);
    }

    private void RefreshButtonColors()
    {
        if (_toggleButtonRect == null)
            return;

        var bg = _toggleButtonRect.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = JbsConfig.Enabled
                ? new Color(0.18f, 0.30f, 0.18f, 0.90f)
                : new Color(0.20f, 0.20f, 0.23f, 0.88f);
        }

        var outline = _toggleButtonRect.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = JbsConfig.Enabled
                ? new Color(0.55f, 0.80f, 0.40f, 0.70f)
                : new Color(0f, 0f, 0f, 0.40f);
        }
    }

    private void SyncPlacement()
    {
        if (_anchorButton == null || _toggleButtonRect == null)
            return;

        var hostRect = _toggleButtonRect.parent as RectTransform;
        var anchorRect = _anchorButton.transform as RectTransform;
        if (hostRect == null || anchorRect == null)
            return;

        // Find the reference button to place below:
        // prefer BPP_SettingsDockButton_{key}, then JBS_TournamentButton_{key}
        var referenceLocalPos = FindReferenceButtonLocalPos(hostRect, _placementKey);

        var corners = new Vector3[4];
        anchorRect.GetWorldCorners(corners);

        var anchorCenterWorld = (corners[0] + corners[2]) * 0.5f;
        var anchorTopWorld = (corners[1] + corners[2]) * 0.5f;
        var anchorBottomWorld = (corners[0] + corners[3]) * 0.5f;

        var anchorCenterLocal = hostRect.InverseTransformPoint(anchorCenterWorld);
        var anchorTopLocal = hostRect.InverseTransformPoint(anchorTopWorld);
        var anchorBottomLocal = hostRect.InverseTransformPoint(anchorBottomWorld);

        var buttonHeightLocal = Math.Abs(anchorTopLocal.y - anchorBottomLocal.y);
        var worldDownDir = Math.Sign(anchorBottomLocal.y - anchorTopLocal.y);
        if (worldDownDir == 0)
            worldDownDir = -1;

        float refX = referenceLocalPos?.x ?? anchorCenterLocal.x;
        float refY = referenceLocalPos?.y ?? anchorCenterLocal.y;

        // Place below the reference button
        _toggleButtonRect.localPosition = new Vector3(
            refX,
            refY + worldDownDir * (buttonHeightLocal + ButtonGap),
            _toggleButtonRect.localPosition.z
        );
        _toggleButtonRect.localRotation = Quaternion.identity;
        _toggleButtonRect.SetAsLastSibling();
    }

    private static Vector3? FindReferenceButtonLocalPos(RectTransform hostRect, string key)
    {
        // Prefer BPP settings dock button for this key
        var bppName = $"{BppSettingsDockButtonPrefix}{key}";
        var bppChild = hostRect.Find(bppName);
        if (bppChild != null)
            return bppChild.localPosition;

        // Fall back to our tournament button for this key
        var jbsName = $"{JbsTournamentButtonPrefix}{key}";
        var jbsChild = hostRect.Find(jbsName);
        if (jbsChild != null)
            return jbsChild.localPosition;

        return null;
    }

    private static RectTransform? CloneAsToggleButton(
        Button source,
        RectTransform hostRect,
        string objectName)
    {
        try
        {
            var cloneGo = Instantiate(source.gameObject, hostRect, worldPositionStays: false);
            cloneGo.name = objectName;

            // Strip native behaviour
            foreach (var custom in cloneGo.GetComponentsInChildren<ButtonCustom>(true))
                DestroyImmediate(custom);
            foreach (var native in cloneGo.GetComponentsInChildren<BazaarButtonController>(true))
                DestroyImmediate(native);
            foreach (var nested in cloneGo.GetComponentsInChildren<Button>(true))
            {
                if (nested.gameObject != cloneGo)
                    DestroyImmediate(nested);
            }

            // Hide native icon images – we'll draw text instead
            foreach (var img in cloneGo.GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject != cloneGo)
                    img.enabled = false;
            }

            var btn = cloneGo.GetComponent<Button>() ?? cloneGo.AddComponent<Button>();
            var frame = cloneGo.GetComponent<Image>() ?? cloneGo.AddComponent<Image>();

            // Initial background colour (enabled state)
            frame.color = new Color(0.18f, 0.30f, 0.18f, 0.90f);
            frame.raycastTarget = true;

            var outline = cloneGo.GetComponent<Outline>() ?? cloneGo.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.80f, 0.40f, 0.70f);
            outline.effectDistance = new Vector2(1f, -1f);

            btn.onClick.RemoveAllListeners();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.interactable = true;
            btn.targetGraphic = frame;
            btn.transition = Selectable.Transition.ColorTint;
            btn.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.85f, 0.95f, 0.85f, 1f),
                pressedColor = new Color(0.70f, 0.80f, 0.70f, 1f),
                selectedColor = new Color(0.85f, 0.95f, 0.85f, 1f),
                disabledColor = new Color(1f, 1f, 1f, 0.34f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };

            var rect = cloneGo.GetComponent<RectTransform>();
            if (rect == null)
                return null;

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var srcRect = source.transform as RectTransform;
            if (srcRect != null)
                rect.sizeDelta = srcRect.rect.size;

            // Add state label
            var labelGo = new GameObject("JBS_ToggleLabel", typeof(RectTransform));
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(cloneGo.transform, worldPositionStays: false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(2f, 2f);
            labelRect.offsetMax = new Vector2(-2f, -2f);

            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "锦标赛\nON";
            tmp.fontSize = 10f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.75f, 0.97f, 0.65f, 1f);
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            var existingText = FindObjectOfType<TextMeshProUGUI>();
            if (existingText?.font != null)
                tmp.font = existingText.font;

            return rect;
        }
        catch (Exception ex)
        {
            JbsLog.Error(LogCategory, $"Failed to clone toggle button '{objectName}'", ex);
            return null;
        }
    }

    private static TextMeshProUGUI? FindStateLabelIn(GameObject go)
    {
        foreach (var tmp in go.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (tmp.gameObject.name == "JBS_ToggleLabel")
                return tmp;
        }
        return null;
    }
}
