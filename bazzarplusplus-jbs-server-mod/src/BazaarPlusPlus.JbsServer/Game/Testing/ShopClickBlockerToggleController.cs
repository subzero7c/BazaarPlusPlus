#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.JbsServer.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.JbsServer.Game.Testing;

internal sealed class ShopClickBlockerToggleController : MonoBehaviour
{
    private const string LogCategory = "ShopClickBlockerToggle";
    private const string CanvasObjectName = "JBS_ShopClickBlockerToggleCanvas";
    private const string ButtonObjectName = "JBS_ShopClickBlockerToggleButton";
    private const float ScanInterval = 0.5f;

    private GameObject? _canvasObject;
    private RectTransform? _buttonRect;
    private TextMeshProUGUI? _label;
    private float _nextScanTime;

    internal static void Ensure(GameObject host)
    {
        if (host.GetComponent<ShopClickBlockerToggleController>() == null)
            host.AddComponent<ShopClickBlockerToggleController>();
    }

    private void OnEnable()
    {
        JbsConfig.BlockShopClicksTestModeChanged += OnBlockShopClicksChanged;
    }

    private void OnDisable()
    {
        JbsConfig.BlockShopClicksTestModeChanged -= OnBlockShopClicksChanged;
    }

    private void OnDestroy()
    {
        if (_canvasObject != null)
            Destroy(_canvasObject);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + ScanInterval;
        EnsureButton();
        RefreshActiveState();
        RefreshVisual();
    }

    private void OnBlockShopClicksChanged(bool _)
    {
        RefreshVisual();
    }

    private void EnsureButton()
    {
        if (_buttonRect != null)
            return;

        _canvasObject = new GameObject(CanvasObjectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(_canvasObject);

        var canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32700;

        var scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var go = new GameObject(ButtonObjectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        _buttonRect = go.GetComponent<RectTransform>();
        _buttonRect.SetParent(_canvasObject.transform, worldPositionStays: false);
        _buttonRect.anchorMin = new Vector2(1f, 1f);
        _buttonRect.anchorMax = new Vector2(1f, 1f);
        _buttonRect.pivot = new Vector2(1f, 1f);
        _buttonRect.anchoredPosition = new Vector2(-24f, -142f);
        _buttonRect.sizeDelta = new Vector2(154f, 50f);

        var image = go.GetComponent<Image>();
        image.raycastTarget = true;

        var outline = go.GetComponent<Outline>();
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = true;

        var button = go.GetComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = image;
        button.interactable = true;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(1f, 0.92f, 0.68f, 1f),
            pressedColor = new Color(0.74f, 0.62f, 0.38f, 1f),
            selectedColor = new Color(1f, 0.92f, 0.68f, 1f),
            disabledColor = new Color(1f, 1f, 1f, 0.35f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
            JbsConfig.SetBlockShopClicksTestMode(!JbsConfig.BlockShopClicksTestMode)
        );

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.SetParent(_buttonRect, worldPositionStays: false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 2f);
        labelRect.offsetMax = new Vector2(-4f, -2f);

        _label = labelGo.GetComponent<TextMeshProUGUI>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 20f;
        _label.fontStyle = FontStyles.Bold;
        _label.raycastTarget = false;
        _label.textWrappingMode = TextWrappingModes.NoWrap;

        var existingText = FindObjectOfType<TextMeshProUGUI>();
        if (existingText?.font != null)
        {
            _label.font = existingText.font;
            _label.fontSharedMaterial = existingText.fontSharedMaterial;
        }
        JbsTmpFont.TryApply(_label, "商店锁 ONOFF");

        JbsLog.Info(LogCategory, "Attached in-game shop-click blocker test toggle");
    }

    private void RefreshActiveState()
    {
        if (_canvasObject == null)
            return;

        _canvasObject.SetActive(
            ShopClickBlockerRuntime.IsTournamentMode()
            && ShopClickBlockerRuntime.IsInGameRun()
        );
    }

    private void RefreshVisual()
    {
        if (_buttonRect == null || _label == null)
            return;

        var enabled = JbsConfig.BlockShopClicksTestMode;
        _label.text = enabled ? "商店锁 ON" : "商店锁 OFF";
        _label.color = enabled
            ? new Color(1f, 0.94f, 0.76f, 1f)
            : new Color(0.86f, 0.88f, 0.92f, 0.92f);

        var image = _buttonRect.GetComponent<Image>();
        if (image != null)
        {
            image.color = enabled
                ? new Color(0.48f, 0.18f, 0.14f, 0.96f)
                : new Color(0.24f, 0.27f, 0.32f, 0.92f);
        }

        var outline = _buttonRect.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = enabled
                ? new Color(1f, 0.62f, 0.34f, 0.9f)
                : new Color(0f, 0f, 0f, 0.55f);
        }
    }
}
