#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.Settings.Visual;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BazaarPlusPlus.SettingsDockPreview
{
public sealed class SettingsDockPreview : MonoBehaviour
{
    private readonly List<RowView> _rows = new();
    private RectTransform? _panelRoot;
    private Text? _headerText;
    private Text? _modeText;
    private bool _isChinese = true;
    private bool _isExpanded = true;
    private Font? _font;

    private readonly List<MockSetting> _settings = new()
    {
        new("GameHistory", "战绩面板", "History Panel", new[] { "查看", "打开" }, new[] { "VIEW", "OPEN" }, false),
        new("NameOverride", "改名显示", "Name Override", new[] { "关", "开" }, new[] { "OFF", "ON" }, true),
        new("LegendaryPosition", "传说位置", "Legendary Position", new[] { "默认", "置顶", "隐藏" }, new[] { "AUTO", "TOP", "HIDE" }, true),
        new("EnchantPreview", "附魔预览", "Enchant Preview", new[] { "关", "开" }, new[] { "OFF", "ON" }, true),
        new("PackageCardArtReplacement", "卡包美术替换", "Package Card Art", new[] { "关", "开" }, new[] { "OFF", "ON" }, true),
        new("CombatStatusBar", "战斗状态条", "Combat Status Bar", new[] { "关", "开" }, new[] { "OFF", "ON" }, true),
        new("ChineseLocaleMode", "中文模式", "Chinese Locale", new[] { "大陆", "台港", "自动" }, new[] { "CN", "TW", "AUTO" }, true),
        new("HotkeyTutorial", "快捷键教程", "Hotkey Tutorial", new[] { "打开" }, new[] { "OPEN" }, false),
        new("BazaarDbUpload", "上传图鉴数据", "Upload Bazaar DB", new[] { "上传" }, new[] { "SEND" }, false),
        new("TournamentRoom", "锦标赛房间匹配", "Tournament room", new[] { "OFF", "打开" }, new[] { "OFF", "OPEN" }, true),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreatePreviewOnPlay()
    {
        if (FindObjectOfType<SettingsDockPreview>() != null)
            return;

        var host = new GameObject("BPP Settings Dock Preview");
        host.AddComponent<SettingsDockPreview>();
    }

    private void Awake()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildCanvas();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            _isChinese = !_isChinese;
            RefreshView();
        }

        if (Input.GetKeyDown(KeyCode.Space))
            SetExpanded(!_isExpanded);
    }

    private void BuildCanvas()
    {
        var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var root = canvasObject.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var background = CreateRect("PreviewBackground", root, ColorFromRgb(13, 16, 21, 1f));
        background.anchorMin = Vector2.zero;
        background.anchorMax = Vector2.one;
        background.offsetMin = Vector2.zero;
        background.offsetMax = Vector2.zero;

        CreateHelpText(root);
        var anchor = CreateNativeSettingsButton(root);
        var dockButton = CreateDockButton(root, anchor);
        CreatePanel(dockButton);
        TournamentRoomPanelPreview.Create(root, _font!);
        RefreshView();
    }

    private RectTransform CreateNativeSettingsButton(RectTransform parent)
    {
        var buttonRect = CreateButtonShell("MockNativeSettingsButton", parent, new Color(0.18f, 0.20f, 0.24f, 1f));
        buttonRect.anchorMin = new Vector2(1f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(96f, 96f);
        buttonRect.anchoredPosition = new Vector2(-112f, 112f);

        var label = CreateText("Icon", buttonRect, "⚙", 44, TextAnchor.MiddleCenter, Color.white);
        Stretch(label.rectTransform, 0f);
        return buttonRect;
    }

    private RectTransform CreateDockButton(RectTransform parent, RectTransform anchor)
    {
        var dockRect = CreateButtonShell("BPP_SettingsDockButton_preview", parent, new Color(0.20f, 0.17f, 0.12f, 1f));
        dockRect.anchorMin = anchor.anchorMin;
        dockRect.anchorMax = anchor.anchorMax;
        dockRect.pivot = anchor.pivot;
        dockRect.sizeDelta = anchor.sizeDelta;
        dockRect.anchoredPosition = anchor.anchoredPosition + new Vector2(-(anchor.sizeDelta.x + 18f), 0f);

        var label = CreateText("Icon", dockRect, "B++", 24, TextAnchor.MiddleCenter, new Color(0.97f, 0.83f, 0.49f, 1f));
        label.fontStyle = FontStyle.Bold;
        Stretch(label.rectTransform, 0f);

        var button = dockRect.GetComponent<Button>();
        button.onClick.AddListener(() => SetExpanded(!_isExpanded));
        return dockRect;
    }

    private void CreatePanel(RectTransform dockButton)
    {
        var panelObject = new GameObject("BPP_SettingsDockPanel_preview", typeof(RectTransform), typeof(Image), typeof(Outline));
        panelObject.transform.SetParent(dockButton, false);
        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.localScale = new Vector3(BppSettingsDockVisualConstants.PanelExpandedScale, BppSettingsDockVisualConstants.PanelExpandedScale, 1f);
        panelRect.anchoredPosition = new Vector2(-8f, 0f);
        panelRect.sizeDelta = new Vector2(BppSettingsDockVisualConstants.PanelWidth, BppSettingsDockVisualConstants.CalculatePanelHeight(_settings.Count));
        _panelRoot = panelRect;

        var background = panelObject.GetComponent<Image>();
        background.color = BppSettingsDockVisualConstants.PanelBackground;
        background.raycastTarget = true;

        var outline = panelObject.GetComponent<Outline>();
        outline.effectColor = BppSettingsDockVisualConstants.PanelOutlineColor;
        outline.effectDistance = BppSettingsDockVisualConstants.PanelOutlineDistance;
        outline.useGraphicAlpha = true;

        _headerText = CreateText("BPP_SettingsDockHeader", panelRect, "BazaarPlusPlus", 21, TextAnchor.MiddleLeft, BppSettingsDockVisualConstants.HeaderTextColor);
        _headerText.fontStyle = FontStyle.Bold;
        BppSettingsDockVisualConstants.ConfigureHeaderRect(_headerText.rectTransform);

        for (var index = 0; index < _settings.Count; index++)
            _rows.Add(CreateRow(_settings[index], index));
    }

    private RowView CreateRow(MockSetting setting, int index)
    {
        if (_panelRoot == null)
            throw new InvalidOperationException("Panel root has not been created.");

        var rowObject = new GameObject($"BPP_SettingsDockRow_{setting.Key}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        rowObject.transform.SetParent(_panelRoot, false);
        var rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        BppSettingsDockVisualConstants.ConfigureRowRect(rowRect, index);

        var background = rowObject.GetComponent<Image>();
        background.raycastTarget = true;

        var outline = rowObject.GetComponent<Outline>();
        outline.effectDistance = BppSettingsDockVisualConstants.RowOutlineDistance;
        outline.useGraphicAlpha = true;

        var button = rowObject.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = background;
        button.onClick.AddListener(() =>
        {
            setting.Activate();
            if (setting.Key == "TournamentRoom")
                TournamentRoomPanelPreview.Toggle();
            RefreshView();
        });

        var label = CreateText("Label", rowRect, string.Empty, 19, TextAnchor.MiddleLeft, BppSettingsDockVisualConstants.RowLabelActiveColor);
        BppSettingsDockVisualConstants.ConfigureLabelRect(label.rectTransform);
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Truncate;

        var status = CreateText("Status", rowRect, string.Empty, 17, TextAnchor.MiddleCenter, Color.white);
        BppSettingsDockVisualConstants.ConfigureStatusRect(status.rectTransform);

        return new RowView(setting, background, outline, label, status);
    }

    private void RefreshView()
    {
        if (_headerText != null)
            _headerText.text = "BazaarPlusPlus";
        if (_modeText != null)
            _modeText.text = _isChinese
                ? "设置面板离线预览 · L 切换语言 · Space 展开/收起 · 点击行切换状态"
                : "Settings dock offline preview · L language · Space expand/collapse · Click rows to cycle";

        foreach (var row in _rows)
            ApplyRowState(row);
    }

    private void ApplyRowState(RowView row)
    {
        var enabled = row.Setting.IsActive;
        row.Label.text = _isChinese ? row.Setting.ChineseLabel : row.Setting.EnglishLabel;
        row.Status.text = _isChinese ? row.Setting.ChineseStatus : row.Setting.EnglishStatus;
        row.Background.color = enabled ? BppSettingsDockVisualConstants.RowEnabledBackground : BppSettingsDockVisualConstants.RowDisabledBackground;
        row.Outline.effectColor = enabled ? BppSettingsDockVisualConstants.RowEnabledOutlineColor : BppSettingsDockVisualConstants.RowDisabledOutlineColor;
        row.Status.color = enabled ? BppSettingsDockVisualConstants.RowStatusEnabledColor : BppSettingsDockVisualConstants.RowStatusDisabledColor;
    }

    private void SetExpanded(bool expanded)
    {
        _isExpanded = expanded;
        if (_panelRoot != null)
            _panelRoot.gameObject.SetActive(expanded);
    }

    private void CreateHelpText(RectTransform root)
    {
        _modeText = CreateText("PreviewHelp", root, string.Empty, 24, TextAnchor.UpperLeft, new Color(0.82f, 0.86f, 0.91f, 0.94f));
        var rect = _modeText.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(40f, -96f);
        rect.offsetMax = new Vector2(-40f, -32f);
    }

    private RectTransform CreateButtonShell(string name, RectTransform parent, Color color)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        buttonObject.transform.SetParent(parent, false);
        var image = buttonObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        var outline = buttonObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.76f, 0.45f, 0.14f, 0.55f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        var button = buttonObject.GetComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = image;
        return buttonObject.GetComponent<RectTransform>();
    }

    private RectTransform CreateRect(string name, RectTransform parent, Color color)
    {
        var rectObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        rectObject.transform.SetParent(parent, false);
        var image = rectObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rectObject.GetComponent<RectTransform>();
    }

    private Text CreateText(string name, RectTransform parent, string text, int fontSize, TextAnchor alignment, Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        var label = textObject.GetComponent<Text>();
        label.text = text;
        label.font = _font;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        return label;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
            return;

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventSystem);
    }

    private static Color ColorFromRgb(int r, int g, int b, float a)
    {
        return new Color(r / 255f, g / 255f, b / 255f, a);
    }

    private sealed class RowView
    {
        public RowView(MockSetting setting, Image background, Outline outline, Text label, Text status)
        {
            Setting = setting;
            Background = background;
            Outline = outline;
            Label = label;
            Status = status;
        }

        public MockSetting Setting { get; }
        public Image Background { get; }
        public Outline Outline { get; }
        public Text Label { get; }
        public Text Status { get; }
    }

    private sealed class MockSetting
    {
        private readonly string[] _chineseStatuses;
        private readonly string[] _englishStatuses;
        private readonly bool _cycleIsActive;
        private int _statusIndex;

        public MockSetting(string key, string chineseLabel, string englishLabel, string[] chineseStatuses, string[] englishStatuses, bool cycleIsActive)
        {
            Key = key;
            ChineseLabel = chineseLabel;
            EnglishLabel = englishLabel;
            _chineseStatuses = chineseStatuses;
            _englishStatuses = englishStatuses;
            _cycleIsActive = cycleIsActive;
            _statusIndex = cycleIsActive && chineseStatuses.Length > 1 ? 1 : 0;
        }

        public string Key { get; }
        public string ChineseLabel { get; }
        public string EnglishLabel { get; }
        public string ChineseStatus => _chineseStatuses[_statusIndex];
        public string EnglishStatus => _englishStatuses[_statusIndex];
        public bool IsActive => _cycleIsActive && _statusIndex != 0 || !_cycleIsActive;

        public void Activate()
        {
            _statusIndex = (_statusIndex + 1) % _chineseStatuses.Length;
        }
    }
}
}
