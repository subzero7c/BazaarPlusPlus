#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.JbsServer.Game.TournamentRoom;

/// <summary>
/// Overlay panel for tournament room matching.
/// Creates its own UGUI hierarchy anchored to the Canvas root.
/// </summary>
internal sealed class TournamentRoomPanel : MonoBehaviour
{
    private const string LogCategory = "TournamentPanel";
    private const float PanelWidth = 580f;
    private const float PanelHeight = 500f;
    private const float HeaderHeight = 48f;
    private const float ToolbarHeight = 44f;
    private const float FooterHeight = 60f;
    private const float Padding = 16f;
    private const float RowHeight = 56f;
    private const float RowSpacing = 6f;

    private static TournamentRoomPanel? _instance;

    public static bool IsVisible => _instance != null && _instance._panelRoot != null
        && _instance._panelRoot.gameObject.activeSelf;

    private RectTransform? _panelRoot;
    private TextMeshProUGUI? _statusText;
    private RectTransform? _roomListContainer;
    private TextMeshProUGUI? _emptyLabel;
    private readonly List<RoomRowView> _roomRows = new();

    // Placeholder room data – replace with actual server responses
    private readonly List<RoomEntry> _rooms = new();

    internal static void OpenFromDockButton()
    {
        if (_instance == null)
        {
            JbsLog.Warn(LogCategory, "No panel instance found; creating one on Canvas");
            CreateInstance();
        }

        if (_instance != null)
            _instance.SetVisible(true);
    }

    internal static void Close()
    {
        _instance?.SetVisible(false);
    }

    private static void CreateInstance()
    {
        var canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            JbsLog.Warn(LogCategory, "No Canvas found in scene");
            return;
        }

        var host = new GameObject("JBS_TournamentRoomPanelHost");
        host.transform.SetParent(canvas.transform, worldPositionStays: false);
        _instance = host.AddComponent<TournamentRoomPanel>();
        _instance.Initialize(canvas);
    }

    private void Initialize(Canvas canvas)
    {
        try
        {
            BuildUi(canvas.transform as RectTransform);
            SetVisible(false);
        }
        catch (Exception ex)
        {
            JbsLog.Error(LogCategory, "Failed to build panel UI", ex);
        }
    }

    private void OnEnable()
    {
        _instance ??= this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void SetVisible(bool visible)
    {
        if (_panelRoot != null)
            _panelRoot.gameObject.SetActive(visible);

        if (visible)
            RefreshRoomList();
    }

    private void BuildUi(RectTransform? canvasRect)
    {
        if (canvasRect == null)
            return;

        // Root panel background
        var panelGo = new GameObject(
            "JBS_TournamentRoomPanel",
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline)
        );
        _panelRoot = panelGo.GetComponent<RectTransform>();
        _panelRoot.SetParent(canvasRect, worldPositionStays: false);
        _panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRoot.pivot = new Vector2(0.5f, 0.5f);
        _panelRoot.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        _panelRoot.anchoredPosition = Vector2.zero;

        var bg = panelGo.GetComponent<Image>();
        bg.color = new Color(0.09f, 0.09f, 0.11f, 0.97f);
        bg.raycastTarget = true;

        var outline = panelGo.GetComponent<Outline>();
        outline.effectColor = new Color(0.76f, 0.45f, 0.14f, 0.80f);
        outline.effectDistance = new Vector2(2f, -2f);

        // Header bar
        BuildHeader(_panelRoot);

        // Status row (connection / refresh info)
        BuildStatusRow(_panelRoot);

        // Room list scroll area
        BuildRoomListArea(_panelRoot);

        // Footer buttons (创建房间 / 加入房间)
        BuildFooter(_panelRoot);
    }

    private void BuildHeader(RectTransform parent)
    {
        var headerGo = new GameObject("Header", typeof(RectTransform), typeof(Image));
        var headerRect = headerGo.GetComponent<RectTransform>();
        headerRect.SetParent(parent, worldPositionStays: false);
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.offsetMin = new Vector2(0f, -HeaderHeight);
        headerRect.offsetMax = new Vector2(0f, 0f);

        var headerBg = headerGo.GetComponent<Image>();
        headerBg.color = new Color(0.15f, 0.12f, 0.08f, 1f);

        // Title label
        var title = CreateText("Title", headerRect, "锦标赛房间匹配", 22f,
            TextAlignmentOptions.Left, new Color(0.97f, 0.83f, 0.49f, 1f));
        if (title != null)
        {
            var tr = title.rectTransform;
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.offsetMin = new Vector2(Padding, 0f);
            tr.offsetMax = new Vector2(-50f, 0f);
        }

        // Close button
        var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var closeRect = closeGo.GetComponent<RectTransform>();
        closeRect.SetParent(headerRect, worldPositionStays: false);
        closeRect.anchorMin = new Vector2(1f, 0.5f);
        closeRect.anchorMax = new Vector2(1f, 0.5f);
        closeRect.pivot = new Vector2(1f, 0.5f);
        closeRect.sizeDelta = new Vector2(36f, 36f);
        closeRect.anchoredPosition = new Vector2(-8f, 0f);

        var closeBg = closeGo.GetComponent<Image>();
        closeBg.color = new Color(0.6f, 0.15f, 0.10f, 0.80f);
        closeBg.raycastTarget = true;

        var closeBtn = closeGo.GetComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        closeBtn.transition = Selectable.Transition.ColorTint;
        closeBtn.navigation = new Navigation { mode = Navigation.Mode.None };
        closeBtn.onClick.AddListener(Close);

        var closeLabel = CreateText("CloseLabel", closeRect, "✕", 18f,
            TextAlignmentOptions.Center, Color.white);
        if (closeLabel != null)
        {
            closeLabel.rectTransform.anchorMin = Vector2.zero;
            closeLabel.rectTransform.anchorMax = Vector2.one;
            closeLabel.rectTransform.offsetMin = Vector2.zero;
            closeLabel.rectTransform.offsetMax = Vector2.zero;
        }
    }

    private void BuildStatusRow(RectTransform parent)
    {
        var statusGo = new GameObject("StatusRow", typeof(RectTransform));
        var statusRect = statusGo.GetComponent<RectTransform>();
        statusRect.SetParent(parent, worldPositionStays: false);
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.offsetMin = new Vector2(Padding, -(HeaderHeight + 36f));
        statusRect.offsetMax = new Vector2(-Padding, -HeaderHeight);

        _statusText = CreateText("StatusText", statusRect, "正在连接服务器...", 14f,
            TextAlignmentOptions.Left, new Color(0.70f, 0.75f, 0.80f, 1f));
        if (_statusText != null)
        {
            _statusText.rectTransform.anchorMin = Vector2.zero;
            _statusText.rectTransform.anchorMax = Vector2.one;
            _statusText.rectTransform.offsetMin = Vector2.zero;
            _statusText.rectTransform.offsetMax = Vector2.zero;
        }

        // Refresh button
        var refreshGo = new GameObject("RefreshButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var refreshRect = refreshGo.GetComponent<RectTransform>();
        refreshRect.SetParent(statusRect, worldPositionStays: false);
        refreshRect.anchorMin = new Vector2(1f, 0.5f);
        refreshRect.anchorMax = new Vector2(1f, 0.5f);
        refreshRect.pivot = new Vector2(1f, 0.5f);
        refreshRect.sizeDelta = new Vector2(64f, 28f);
        refreshRect.anchoredPosition = Vector2.zero;

        var refreshBg = refreshGo.GetComponent<Image>();
        refreshBg.color = new Color(0.22f, 0.30f, 0.40f, 0.90f);
        refreshBg.raycastTarget = true;

        var refreshBtn = refreshGo.GetComponent<Button>();
        refreshBtn.targetGraphic = refreshBg;
        refreshBtn.transition = Selectable.Transition.ColorTint;
        refreshBtn.navigation = new Navigation { mode = Navigation.Mode.None };
        refreshBtn.onClick.AddListener(RefreshRoomList);

        var refreshLabel = CreateText("RefreshLabel", refreshRect, "刷新", 13f,
            TextAlignmentOptions.Center, Color.white);
        if (refreshLabel != null)
        {
            refreshLabel.rectTransform.anchorMin = Vector2.zero;
            refreshLabel.rectTransform.anchorMax = Vector2.one;
            refreshLabel.rectTransform.offsetMin = Vector2.zero;
            refreshLabel.rectTransform.offsetMax = Vector2.zero;
        }
    }

    private void BuildRoomListArea(RectTransform parent)
    {
        float listTop = HeaderHeight + 36f;
        float listBottom = FooterHeight + Padding;

        var listGo = new GameObject("RoomListArea", typeof(RectTransform), typeof(Image));
        var listRect = listGo.GetComponent<RectTransform>();
        listRect.SetParent(parent, worldPositionStays: false);
        listRect.anchorMin = new Vector2(0f, 0f);
        listRect.anchorMax = new Vector2(1f, 1f);
        listRect.offsetMin = new Vector2(Padding, listBottom);
        listRect.offsetMax = new Vector2(-Padding, -listTop);

        var listBg = listGo.GetComponent<Image>();
        listBg.color = new Color(0.06f, 0.06f, 0.08f, 0.80f);
        listBg.raycastTarget = true;

        // Scrollable content container
        var contentGo = new GameObject("RoomListContent", typeof(RectTransform));
        _roomListContainer = contentGo.GetComponent<RectTransform>();
        _roomListContainer.SetParent(listRect, worldPositionStays: false);
        _roomListContainer.anchorMin = new Vector2(0f, 1f);
        _roomListContainer.anchorMax = new Vector2(1f, 1f);
        _roomListContainer.pivot = new Vector2(0.5f, 1f);
        _roomListContainer.offsetMin = new Vector2(0f, 0f);
        _roomListContainer.offsetMax = new Vector2(0f, 0f);
        _roomListContainer.sizeDelta = new Vector2(0f, 0f);

        // "暂无房间" empty state label
        _emptyLabel = CreateText("EmptyLabel", listRect, "暂无可用房间，请创建或等待其他玩家", 15f,
            TextAlignmentOptions.Center, new Color(0.50f, 0.55f, 0.60f, 1f));
        if (_emptyLabel != null)
        {
            _emptyLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            _emptyLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _emptyLabel.rectTransform.offsetMin = Vector2.zero;
            _emptyLabel.rectTransform.offsetMax = Vector2.zero;
        }
    }

    private void BuildFooter(RectTransform parent)
    {
        var footerGo = new GameObject("Footer", typeof(RectTransform));
        var footerRect = footerGo.GetComponent<RectTransform>();
        footerRect.SetParent(parent, worldPositionStays: false);
        footerRect.anchorMin = new Vector2(0f, 0f);
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.pivot = new Vector2(0.5f, 0f);
        footerRect.offsetMin = new Vector2(Padding, Padding);
        footerRect.offsetMax = new Vector2(-Padding, Padding + FooterHeight - Padding);
        footerRect.sizeDelta = new Vector2(0f, FooterHeight - Padding);

        float buttonWidth = (PanelWidth - Padding * 3f) / 2f;

        // 创建房间 button
        BuildFooterButton(footerRect, "CreateRoomButton", "创建房间 ＋",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(Padding * 0f, 0f), new Vector2(buttonWidth, FooterHeight - Padding * 2f),
            new Color(0.18f, 0.38f, 0.20f, 0.95f),
            OnCreateRoomClicked);

        // 加入房间 button (with room code)
        BuildFooterButton(footerRect, "JoinRoomButton", "加入房间 →",
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0f, 0f), new Vector2(buttonWidth, FooterHeight - Padding * 2f),
            new Color(0.18f, 0.25f, 0.40f, 0.95f),
            OnJoinRoomClicked);
    }

    private void BuildFooterButton(
        RectTransform parent,
        string name,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 size,
        Color bgColor,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPos;

        var bg = go.GetComponent<Image>();
        bg.color = bgColor;
        bg.raycastTarget = true;

        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.15f);
        outline.effectDistance = new Vector2(1f, -1f);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(onClick);

        var text = CreateText("Label", rect, label, 16f, TextAlignmentOptions.Center, Color.white);
        if (text != null)
        {
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
        }
    }

    private void RefreshRoomList()
    {
        // TODO: fetch room list from JBS server
        // For now show placeholder state
        SetStatus("已连接 · 0 个房间可用");
        RebuildRoomRows();
    }

    private void SetStatus(string text)
    {
        if (_statusText != null)
            _statusText.text = text;
    }

    private void RebuildRoomRows()
    {
        if (_roomListContainer == null)
            return;

        // Clear old rows
        foreach (var row in _roomRows)
        {
            if (row.Root != null)
                Destroy(row.Root.gameObject);
        }
        _roomRows.Clear();

        // Show/hide empty label
        if (_emptyLabel != null)
            _emptyLabel.gameObject.SetActive(_rooms.Count == 0);

        // Build a row per room
        for (var i = 0; i < _rooms.Count; i++)
        {
            var room = _rooms[i];
            var rowRect = BuildRoomRow(_roomListContainer, i, room);
            if (rowRect != null)
                _roomRows.Add(new RoomRowView(rowRect, room));
        }

        // Resize content to fit
        var totalHeight = _rooms.Count * (RowHeight + RowSpacing);
        _roomListContainer.sizeDelta = new Vector2(0f, totalHeight);
    }

    private RectTransform? BuildRoomRow(RectTransform parent, int index, RoomEntry room)
    {
        var rowGo = new GameObject($"RoomRow_{index}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        var rowRect = rowGo.GetComponent<RectTransform>();
        rowRect.SetParent(parent, worldPositionStays: false);
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.offsetMin = new Vector2(4f, -(index * (RowHeight + RowSpacing) + RowHeight));
        rowRect.offsetMax = new Vector2(-4f, -(index * (RowHeight + RowSpacing)));

        var bg = rowGo.GetComponent<Image>();
        bg.color = new Color(0.19f, 0.19f, 0.22f, 0.92f);
        bg.raycastTarget = true;

        var rowOutline = rowGo.GetComponent<Outline>();
        rowOutline.effectColor = new Color(0f, 0f, 0f, 0.45f);
        rowOutline.effectDistance = new Vector2(1f, -1f);

        var btn = rowGo.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(() => OnRoomRowClicked(room));

        // Room name
        var nameText = CreateText("RoomName", rowRect, room.Name, 16f,
            TextAlignmentOptions.Left, new Color(0.93f, 0.93f, 0.95f, 1f));
        if (nameText != null)
        {
            nameText.rectTransform.anchorMin = new Vector2(0f, 0f);
            nameText.rectTransform.anchorMax = new Vector2(0.6f, 1f);
            nameText.rectTransform.offsetMin = new Vector2(Padding, 0f);
            nameText.rectTransform.offsetMax = new Vector2(0f, 0f);
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
        }

        // Player count
        var countLabel = $"{room.CurrentPlayers}/{room.MaxPlayers}";
        var countText = CreateText("PlayerCount", rowRect, countLabel, 14f,
            TextAlignmentOptions.Right, new Color(0.75f, 0.78f, 0.82f, 0.98f));
        if (countText != null)
        {
            countText.rectTransform.anchorMin = new Vector2(0.6f, 0f);
            countText.rectTransform.anchorMax = new Vector2(1f, 1f);
            countText.rectTransform.offsetMin = new Vector2(0f, 0f);
            countText.rectTransform.offsetMax = new Vector2(-Padding, 0f);
        }

        return rowRect;
    }

    private void OnCreateRoomClicked()
    {
        // TODO: open create-room sub-dialog or send create-room request to JBS server
        SetStatus("功能开发中，敬请期待...");
        JbsLog.Info(LogCategory, "Create room clicked");
    }

    private void OnJoinRoomClicked()
    {
        // TODO: open join-room sub-dialog (room code input) or send join-room request
        SetStatus("功能开发中，敬请期待...");
        JbsLog.Info(LogCategory, "Join room clicked");
    }

    private void OnRoomRowClicked(RoomEntry room)
    {
        JbsLog.Info(LogCategory, $"Room selected: {room.Name}");
        // TODO: send join request to JBS server for this specific room
    }

    private static TextMeshProUGUI? CreateText(
        string objectName,
        Transform parent,
        string text,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.GetComponent<RectTransform>().SetParent(parent, worldPositionStays: false);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;

        // Resolve font from scene (same technique as BppSettingsDockController)
        var existingText = FindObjectOfType<TextMeshProUGUI>();
        if (existingText != null && existingText.font != null)
        {
            tmp.font = existingText.font;
            if (existingText.fontSharedMaterial != null)
                tmp.fontSharedMaterial = existingText.fontSharedMaterial;
        }

        return tmp;
    }

    // ---- Data types ----

    internal sealed class RoomEntry
    {
        internal RoomEntry(string id, string name, string hostName, int currentPlayers, int maxPlayers)
        {
            Id = id;
            Name = name;
            HostName = hostName;
            CurrentPlayers = currentPlayers;
            MaxPlayers = maxPlayers;
        }

        internal string Id { get; }
        internal string Name { get; }
        internal string HostName { get; }
        internal int CurrentPlayers { get; }
        internal int MaxPlayers { get; }
    }

    private sealed class RoomRowView
    {
        internal RoomRowView(RectTransform root, RoomEntry entry)
        {
            Root = root;
            Entry = entry;
        }

        internal RectTransform Root { get; }
        internal RoomEntry Entry { get; }
    }
}
