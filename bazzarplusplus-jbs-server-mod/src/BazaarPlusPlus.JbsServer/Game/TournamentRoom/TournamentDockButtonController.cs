#pragma warning disable CS0436
#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.JbsServer.Game.TournamentRoom;

/// <summary>
/// Dock button placed in the game's top-right button area (near the native settings button).
/// Click opens/closes the TournamentRoomPanel.
///
/// Placement: ABOVE the BPP Collection Panel button (卡牌图鉴 / 数据共建 button).
/// Falls back to above the native settings button when BPP is not installed.
/// </summary>
internal sealed class TournamentDockButtonController : MonoBehaviour
{
    private const string LogCategory = "TournamentDockButton";
    // BPP collection panel button name pattern: BPP_SettingsDockButton_CollectionPanel_{key}
    private const string BppCollectionPanelButtonPrefix = "BPP_SettingsDockButton_CollectionPanel_";
    private const float ButtonGap = 18f;

    private Button? _anchorButton;
    private Button? _dockButton;
    private RectTransform? _dockButtonRect;

    internal static void Attach(Button nativeSettingsButton, string key)
    {
        if (nativeSettingsButton == null)
            return;

        var objectName = $"JBS_TournamentButton_{key}";
        var hostRect = nativeSettingsButton.transform.parent as RectTransform;
        if (hostRect == null)
            return;

        var existingButton = hostRect.Find(objectName) as RectTransform;
        if (existingButton != null)
        {
            var existingController =
                nativeSettingsButton.gameObject.GetComponent<TournamentDockButtonController>()
                ?? nativeSettingsButton.gameObject.AddComponent<TournamentDockButtonController>();
            existingController.Initialize(nativeSettingsButton, existingButton, key);
            return;
        }

        var clone = CloneSettingsButton(nativeSettingsButton, hostRect, objectName);
        if (clone == null)
            return;

        var existing =
            nativeSettingsButton.gameObject.GetComponent<TournamentDockButtonController>()
            ?? nativeSettingsButton.gameObject.AddComponent<TournamentDockButtonController>();
        existing.Initialize(nativeSettingsButton, clone, key);
    }

    private void Initialize(Button anchor, RectTransform dockButton, string key)
    {
        _anchorButton = anchor;
        _dockButtonRect = dockButton;
        _dockButton = dockButton.GetComponent<Button>();

        if (_dockButton == null)
        {
            JbsLog.Warn(LogCategory, "Cloned button has no Button component");
            return;
        }

        _dockButton.onClick.RemoveAllListeners();
        _dockButton.onClick.AddListener(OnClicked);

        SyncPlacement(key);

        // Subscribe to enable state changes
        JbsConfig.EnabledChanged += OnEnabledChanged;
        ApplyEnabledState();
    }

    private void OnDestroy()
    {
        JbsConfig.EnabledChanged -= OnEnabledChanged;
    }

    private void OnRectTransformDimensionsChange()
    {
        // Re-sync when the parent layout changes
        if (_anchorButton != null)
            SyncPlacement(ResolvePlacementKey());
    }

    private void OnClicked()
    {
        if (!JbsConfig.Enabled)
            JbsConfig.SetEnabled(true);

        if (TournamentRoomPanel.IsVisibleFrom(_dockButtonRect))
        {
            TournamentRoomPanel.Close();
            return;
        }

        TournamentRoomPanel.OpenFromDockButton(_dockButtonRect);
    }

    private void OnEnabledChanged(bool enabled)
    {
        ApplyEnabledState();
    }

    private void ApplyEnabledState()
    {
        if (_dockButtonRect == null)
            return;

        _dockButtonRect.gameObject.SetActive(true);

        if (!JbsConfig.Enabled)
            TournamentRoomPanel.Close();
    }

    private string ResolvePlacementKey()
    {
        return _dockButtonRect?.name ?? "Unknown";
    }

    private void SyncPlacement(string key)
    {
        if (_anchorButton == null || _dockButtonRect == null)
            return;

        var hostRect = _dockButtonRect.parent as RectTransform;
        var anchorRect = _anchorButton.transform as RectTransform;
        if (hostRect == null || anchorRect == null)
            return;

        var corners = new Vector3[4];
        anchorRect.GetWorldCorners(corners);

        var anchorCenterWorld = (corners[0] + corners[2]) * 0.5f;
        var anchorTopWorld = (corners[1] + corners[2]) * 0.5f;
        var anchorBottomWorld = (corners[0] + corners[3]) * 0.5f;

        var anchorCenterLocal = hostRect.InverseTransformPoint(anchorCenterWorld);
        var anchorTopLocal = hostRect.InverseTransformPoint(anchorTopWorld);
        var anchorBottomLocal = hostRect.InverseTransformPoint(anchorBottomWorld);

        var buttonHeightLocal = Math.Abs(anchorTopLocal.y - anchorBottomLocal.y);
        var worldUpDir = Math.Sign(anchorTopLocal.y - anchorBottomLocal.y);
        if (worldUpDir == 0)
            worldUpDir = 1;

        // Find the BPP Collection Panel button (数据共建 / 卡牌图鉴) for this key.
        // It is named BPP_SettingsDockButton_CollectionPanel_{key}.
        var collectionButtonName = $"{BppCollectionPanelButtonPrefix}{key}";
        var collectionButtonTransform = hostRect.Find(collectionButtonName);

        float refX;
        float refY;

        if (collectionButtonTransform != null)
        {
            // Place ABOVE the collection panel button
            refX = collectionButtonTransform.localPosition.x;
            refY = collectionButtonTransform.localPosition.y;
        }
        else
        {
            // BPP not installed: place above the native settings button
            refX = anchorCenterLocal.x;
            refY = anchorCenterLocal.y;
        }

        _dockButtonRect.localPosition = new Vector3(
            refX,
            refY + worldUpDir * (buttonHeightLocal + ButtonGap),
            _dockButtonRect.localPosition.z
        );
        _dockButtonRect.localRotation = Quaternion.identity;
        _dockButtonRect.SetAsLastSibling();
    }

    private static RectTransform? CloneSettingsButton(
        Button source,
        RectTransform hostRect,
        string objectName)
    {
        try
        {
            var cloneGo = Instantiate(source.gameObject, hostRect, worldPositionStays: false);
            cloneGo.name = objectName;

            // Strip native game button behaviour so we fully own the click
            foreach (var custom in cloneGo.GetComponentsInChildren<ButtonCustom>(true))
                DestroyImmediate(custom);
            foreach (var native in cloneGo.GetComponentsInChildren<BazaarButtonController>(true))
                DestroyImmediate(native);
            foreach (var nested in cloneGo.GetComponentsInChildren<Button>(true))
            {
                if (nested.gameObject != cloneGo)
                    DestroyImmediate(nested);
            }

            var btn = cloneGo.GetComponent<Button>() ?? cloneGo.AddComponent<Button>();
            var frame = cloneGo.GetComponent<Image>();
            if (frame == null)
            {
                frame = cloneGo.AddComponent<Image>();
                frame.color = new Color(1f, 1f, 1f, 0f);
            }

            frame.raycastTarget = true;
            btn.onClick.RemoveAllListeners();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.interactable = true;
            btn.targetGraphic = frame;
            btn.transition = Selectable.Transition.ColorTint;

            // Tint: amber highlight to match BPP style for our tournament button
            btn.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.62f, 0.90f, 1f, 1f),
                pressedColor = new Color(0.40f, 0.70f, 0.85f, 1f),
                selectedColor = new Color(0.62f, 0.90f, 1f, 1f),
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

            // Overlay a label so the user can identify this button
            ApplyTournamentLabel(cloneGo);

            return rect;
        }
        catch (Exception ex)
        {
            JbsLog.Error(LogCategory, $"Failed to clone settings button '{objectName}'", ex);
            return null;
        }
    }

    private static void ApplyTournamentLabel(GameObject cloneGo)
    {
        // Find or create a child text object to show "🏆" or a short "JBS" label
        // We reuse any existing icon image child by hiding it and overlaying text.
        foreach (var img in cloneGo.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject == cloneGo)
                continue;
            img.enabled = false;
        }

        var labelGo = new GameObject("JBS_Label", typeof(RectTransform));
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.SetParent(cloneGo.transform, worldPositionStays: false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var tmp = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.text = "赛";
        tmp.fontSize = 14f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.62f, 0.90f, 1f, 1f);
        tmp.raycastTarget = false;
        tmp.fontStyle = TMPro.FontStyles.Bold;

        var existing = FindObjectOfType<TMPro.TextMeshProUGUI>();
        if (existing?.font != null)
            tmp.font = existing.font;
    }
}
