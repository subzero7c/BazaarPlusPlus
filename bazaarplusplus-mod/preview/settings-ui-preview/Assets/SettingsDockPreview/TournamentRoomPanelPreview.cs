#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace BazaarPlusPlus.SettingsDockPreview
{
/// <summary>
/// Full-screen offline preview of TournamentRoomPanel.
/// Uses Unity built-in UI (Text / InputField) instead of TMPro.
///
/// Chat view layout:
///   ┌─────────────────────────────────────────────────┐ ← InfoBar (52px)
///   │  RoomName · N/M 人                   [← 离开]  │
///   ├──────────────────┬──────────────────────────────┤
///   │  当前玩家 (N)    │  聊天记录                   │
///   │  ┌────────────┐  │  msg...                     │ ← body (stretch)
///   │  │ 玩家列表   │  │  msg...                     │
///   │  └────────────┘  │                             │
///   ├──────────────────┴──────────────────────────────┤
///   │  [输入框 ………………………………………… ]  [发送]        │ ← InputArea (68px)
///   └─────────────────────────────────────────────────┘
/// </summary>
public sealed class TournamentRoomPanelPreview : MonoBehaviour
{
    // ---- Layout constants (must match TournamentRoomPanel.cs) ----
    private const float HeaderHeight      = 80f;
    private const float StatusRowHeight   = 44f;
    private const float SearchBarHeight   = 48f;
    private const float FooterHeight      = 84f;
    private const float SectionGap        = 10f;
    private const float Padding           = 32f;
    private const float RowHeight         = 88f;
    private const float RowSpacing        = 8f;
    private const float FooterBottomPad   = 24f;
    private const float DialogWidth       = 600f;
    private const float DialogHeight      = 430f;

    private const float ChatInfoBarHeight  = 72f;
    private const float ChatInputAreaHeight = 68f;
    private const float ChatInputBotPad    = 20f;
    private const float ChatSendBtnWidth   = 110f;
    private const float PlayerPanelWidth   = 110f;
    private const float PlayerRowHeight    = 44f;
    private const float ChatSidebarWidth   = 260f;
    private const int   MaxChatMessages    = 20;
    private const float ButtonRadius       = 14f;
    private const float InputRadius        = 10f;

    // ---- Server config ----
    private const string ServerHttpUrl = "http://localhost:8787";

    private static TournamentRoomPanelPreview? _instance;

    public static bool IsVisible =>
        _instance != null && _instance._panelRoot != null && _instance._panelRoot.gameObject.activeSelf;

    // ---- UI refs: shared ----
    private RectTransform? _panelRoot;
    private Text?          _headerTitleText;

    // ---- UI refs: room list view ----
    private RectTransform? _roomListView;
    private Text?          _statusText;
    private RectTransform? _roomListContainer;
    private Text?          _emptyLabel;
    private InputField?    _searchInput;
    private RectTransform? _createOverlay;
    private InputField?    _roomNameInput;
    private InputField?    _roomCodeInput;
    private InputField?    _manualRoomCodeInput;
    private Text?          _chatToggleButtonLabel;
    private RowView?       _selectedRow;
    private string         _searchQuery = string.Empty;
    private readonly List<RowView>   _rowViews = new();
    private readonly List<RoomEntry> _rooms = new();

    // ---- UI refs: chat view ----
    private RectTransform? _chatView;
    private Text?          _chatRoomLabel;
    private Text?          _chatRoomIdLabel;
    private RectTransform? _playerListContainer;
    private Text?          _playerCountLabel;
    private readonly List<Text> _playerLabels = new();
    private RectTransform? _chatMsgContainer;
    private ScrollRect?    _chatScrollRect;
    private readonly List<Text> _chatLabels = new();
    private readonly Stack<Text> _chatLabelPool = new();
    private InputField?    _chatInputField;

    private Font? _font;

    // ---- Chat connection state ----
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private readonly ConcurrentQueue<ChatMessage> _incoming = new();
    private string _myName = "预览玩家";
    private readonly List<PlayerEntry> _chatPlayers = new();
    private RoomEntry? _currentRoom;
    private int _currentPlayerCount;
    private int _currentMaxPlayers;
    private bool _joinInFlight;

    // ---- Public API ----

    public static void Create(RectTransform canvasRect, Font font)
    {
        if (_instance != null) return;

        var host = new GameObject("JBS_TournamentRoomPanelPreviewHost");
        host.transform.SetParent(canvasRect, worldPositionStays: false);
        var preview = host.AddComponent<TournamentRoomPanelPreview>();
        preview._font = font;
        preview.Build(canvasRect);
        _instance = preview;
    }

    public static void Show()   => _instance?.SetVisible(true);
    public static void Hide()   => _instance?.SetVisible(false);
    public static void Toggle() { if (IsVisible) Hide(); else Show(); }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        DisconnectChat();
    }

    private void Update()
    {
        // Process incoming chat messages
        while (_incoming.TryDequeue(out var msg))
        {
            if (msg.IsSystem && msg.EventPlayerName != null)
            {
                if (msg.IsJoinEvent) AddOrUpdateChatPlayer(msg.EventPlayerId, msg.EventPlayerName);
                else RemoveChatPlayer(msg.EventPlayerId, msg.EventPlayerName);
            }
            UpdatePlayerCountFromMessage(msg);
            AppendChatMessage(msg);
        }
    }

    private void SetVisible(bool visible)
    {
        if (_panelRoot == null) return;
        _panelRoot.gameObject.SetActive(visible);
        if (visible) { ShowRoomListView(); RefreshRoomList(); }
        else DisconnectChat();
    }

    // ---- Build ----

    private void Build(RectTransform canvasRect)
    {
        var stalePanel = canvasRect.Find("JBS_TournamentRoomPanel");
        if (stalePanel != null)
            Destroy(stalePanel.gameObject);

        var panelGo = new GameObject("JBS_TournamentRoomPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
        _panelRoot = panelGo.GetComponent<RectTransform>();
        _panelRoot.SetParent(canvasRect, worldPositionStays: false);
        _panelRoot.anchorMin = Vector2.zero;
        _panelRoot.anchorMax = Vector2.one;
        _panelRoot.pivot     = new Vector2(0.5f, 0.5f);
        _panelRoot.offsetMin = Vector2.zero;
        _panelRoot.offsetMax = Vector2.zero;

        panelGo.GetComponent<Image>().color            = new Color(0.075f, 0.082f, 0.095f, 0.97f);
        panelGo.GetComponent<Image>().raycastTarget    = true;
        panelGo.GetComponent<Outline>().effectColor    = new Color(0.76f, 0.45f, 0.14f, 0.80f);
        panelGo.GetComponent<Outline>().effectDistance = new Vector2(2f, -2f);

        BuildHeader(_panelRoot);
        BuildRoomListView(_panelRoot);
        BuildChatView(_panelRoot);
        BuildCreateRoomOverlay(_panelRoot);

        SetVisible(false);
    }

    // ---- Header ----

    private void BuildHeader(RectTransform parent)
    {
        var headerGo   = new GameObject("Header", typeof(RectTransform), typeof(Image));
        var headerRect = headerGo.GetComponent<RectTransform>();
        headerRect.SetParent(parent, false);
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot     = new Vector2(0.5f, 1f);
        headerRect.offsetMin = new Vector2(0f, -HeaderHeight);
        headerRect.offsetMax = Vector2.zero;
        headerGo.GetComponent<Image>().color = new Color(0.17f, 0.13f, 0.075f, 1f);

        _headerTitleText = MakeText("Title", headerRect, "锦标赛房间匹配", 36,
            TextAnchor.MiddleLeft, new Color(0.97f, 0.83f, 0.49f, 1f));
        _headerTitleText.rectTransform.anchorMin = Vector2.zero;
        _headerTitleText.rectTransform.anchorMax = Vector2.one;
        _headerTitleText.rectTransform.offsetMin = new Vector2(Padding, 0f);
        _headerTitleText.rectTransform.offsetMax = new Vector2(-64f, 0f);

        var closeBtn = MakeButton("CloseButton", headerRect, "✕", 24,
            new Color(0.72f, 0.20f, 0.15f, 0.92f), Hide);
        closeBtn.anchorMin        = new Vector2(1f, 0.5f);
        closeBtn.anchorMax        = new Vector2(1f, 0.5f);
        closeBtn.pivot            = new Vector2(1f, 0.5f);
        closeBtn.sizeDelta        = new Vector2(48f, 48f);
        closeBtn.anchoredPosition = new Vector2(-12f, 0f);
    }

    // ---- Room list view ----

    private void BuildRoomListView(RectTransform parent)
    {
        var go = new GameObject("RoomListView", typeof(RectTransform));
        _roomListView = go.GetComponent<RectTransform>();
        _roomListView.SetParent(parent, false);
        _roomListView.anchorMin = Vector2.zero;
        _roomListView.anchorMax = Vector2.one;
        _roomListView.offsetMin = Vector2.zero;
        _roomListView.offsetMax = new Vector2(0f, -HeaderHeight);

        BuildStatusRow(_roomListView);
        BuildSearchBar(_roomListView);
        BuildRoomListArea(_roomListView);
        BuildRoomListFooter(_roomListView);
    }

    private void BuildStatusRow(RectTransform parent)
    {
        var statusGo   = new GameObject("StatusRow", typeof(RectTransform));
        var statusRect = statusGo.GetComponent<RectTransform>();
        statusRect.SetParent(parent, false);
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot     = new Vector2(0.5f, 1f);
        statusRect.offsetMin = new Vector2(Padding, -StatusRowHeight);
        statusRect.offsetMax = new Vector2(-Padding, 0f);

        _statusText = MakeText("StatusText", statusRect, "共 0 个房间可用", 18,
            TextAnchor.MiddleLeft, new Color(0.76f, 0.81f, 0.86f, 1f));
        _statusText.rectTransform.anchorMin = Vector2.zero;
        _statusText.rectTransform.anchorMax = Vector2.one;
        _statusText.rectTransform.offsetMin = Vector2.zero;
        _statusText.rectTransform.offsetMax = new Vector2(-96f, 0f);

        var refreshBtn = MakeButton("RefreshButton", statusRect, "刷新", 17,
            new Color(0.22f, 0.38f, 0.50f, 0.94f), RefreshRoomList);
        refreshBtn.anchorMin        = new Vector2(1f, 0.5f);
        refreshBtn.anchorMax        = new Vector2(1f, 0.5f);
        refreshBtn.pivot            = new Vector2(1f, 0.5f);
        refreshBtn.sizeDelta        = new Vector2(88f, 36f);
        refreshBtn.anchoredPosition = Vector2.zero;
    }

    private void BuildSearchBar(RectTransform parent)
    {
        float topY    = StatusRowHeight + SectionGap;
        float bottomY = topY + SearchBarHeight;

        var barGo   = new GameObject("SearchBar", typeof(RectTransform), typeof(Image));
        var barRect = barGo.GetComponent<RectTransform>();
        barRect.SetParent(parent, false);
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot     = new Vector2(0.5f, 1f);
        barRect.offsetMin = new Vector2(Padding, -bottomY);
        barRect.offsetMax = new Vector2(-Padding, -topY);
        barGo.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.15f, 0.95f);

        _searchInput = MakeInputField("SearchInput", barRect, "搜索房间名称...", 18);
        Stretch(_searchInput.GetComponent<RectTransform>(), new Vector2(10f, 4f));
        _searchInput.onValueChanged.AddListener(OnSearchChanged);
    }

    private void BuildRoomListArea(RectTransform parent)
    {
        float listTop    = StatusRowHeight + SectionGap + SearchBarHeight + SectionGap;
        float listBottom = FooterHeight + FooterBottomPad + SectionGap;

        var listGo   = new GameObject("RoomListArea", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        var listRect = listGo.GetComponent<RectTransform>();
        listRect.SetParent(parent, false);
        listRect.anchorMin = new Vector2(0f, 0f);
        listRect.anchorMax = new Vector2(1f, 1f);
        listRect.offsetMin = new Vector2(Padding, listBottom);
        listRect.offsetMax = new Vector2(-Padding, -listTop);
        listGo.GetComponent<Image>().color         = new Color(0.06f, 0.06f, 0.08f, 0.80f);
        listGo.GetComponent<Image>().raycastTarget = true;

        var scrollRect = listGo.GetComponent<ScrollRect>();
        scrollRect.horizontal        = false;
        scrollRect.vertical          = true;
        scrollRect.movementType      = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 20f;

        var viewportGo   = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        var viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.SetParent(listRect, false);
        Stretch(viewportRect, Vector2.zero);
        viewportGo.GetComponent<Image>().color          = Color.white;
        viewportGo.GetComponent<Image>().raycastTarget  = false;
        viewportGo.GetComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("RoomListContent", typeof(RectTransform));
        _roomListContainer = contentGo.GetComponent<RectTransform>();
        _roomListContainer.SetParent(viewportRect, false);
        _roomListContainer.anchorMin = new Vector2(0f, 1f);
        _roomListContainer.anchorMax = new Vector2(1f, 1f);
        _roomListContainer.pivot     = new Vector2(0.5f, 1f);
        _roomListContainer.offsetMin = _roomListContainer.offsetMax = Vector2.zero;
        _roomListContainer.sizeDelta = Vector2.zero;

        scrollRect.viewport = viewportRect;
        scrollRect.content  = _roomListContainer;

        _emptyLabel = MakeText("EmptyLabel", listRect, "暂无可用房间，请创建或等待其他玩家",
            20, TextAnchor.MiddleCenter, new Color(0.58f, 0.64f, 0.70f, 1f));
        Stretch(_emptyLabel.rectTransform, Vector2.zero);
    }

    private void BuildRoomListFooter(RectTransform parent)
    {
        var footerGo   = new GameObject("Footer", typeof(RectTransform));
        var footerRect = footerGo.GetComponent<RectTransform>();
        footerRect.SetParent(parent, false);
        footerRect.anchorMin = new Vector2(0f, 0f);
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.pivot     = new Vector2(0.5f, 0f);
        footerRect.offsetMin = new Vector2(Padding, FooterBottomPad);
        footerRect.offsetMax = new Vector2(-Padding, FooterBottomPad + FooterHeight);

        var createBtn = MakeButton("CreateRoomButton", footerRect, "创建房间 ＋", 18,
            new Color(0.20f, 0.48f, 0.28f, 0.96f), OnCreateRoomClicked);
        createBtn.anchorMin        = new Vector2(0f, 0f);
        createBtn.anchorMax        = new Vector2(0.28f, 1f);
        createBtn.pivot            = new Vector2(0.5f, 0.5f);
        createBtn.sizeDelta        = new Vector2(-4f, 0f);
        createBtn.anchoredPosition = new Vector2(-2f, 0f);

        _manualRoomCodeInput = MakeInputField("ManualRoomCodeInput", footerRect, "输入房间码", 16);
        var manualRect = _manualRoomCodeInput.GetComponent<RectTransform>();
        manualRect.anchorMin = new Vector2(0.30f, 0f);
        manualRect.anchorMax = new Vector2(0.54f, 1f);
        manualRect.pivot = new Vector2(0.5f, 0.5f);
        manualRect.offsetMin = Vector2.zero;
        manualRect.offsetMax = new Vector2(-6f, 0f);
        _manualRoomCodeInput.characterLimit = 6;
        _manualRoomCodeInput.onValueChanged.AddListener(value =>
        {
            var normalized = NormalizeRoomCode(value);
            if (_manualRoomCodeInput != null && !string.Equals(value, normalized, StringComparison.Ordinal))
                _manualRoomCodeInput.text = normalized;
        });

        var joinCodeBtn = MakeButton("ManualJoinButton", footerRect, "按码加入", 16,
            new Color(0.20f, 0.34f, 0.56f, 0.96f), OnManualJoinRoom);
        joinCodeBtn.anchorMin        = new Vector2(0.54f, 0f);
        joinCodeBtn.anchorMax        = new Vector2(0.72f, 1f);
        joinCodeBtn.pivot            = new Vector2(0.5f, 0.5f);
        joinCodeBtn.sizeDelta        = new Vector2(-4f, 0f);
        joinCodeBtn.anchoredPosition = new Vector2(2f, 0f);

        var chatBtn = MakeButton("ToggleChatButton", footerRect, "打开聊天", 18,
            new Color(0.22f, 0.30f, 0.42f, 0.95f), OnToggleChatClicked);
        chatBtn.anchorMin        = new Vector2(0.72f, 0f);
        chatBtn.anchorMax        = new Vector2(1f, 1f);
        chatBtn.pivot            = new Vector2(0.5f, 0.5f);
        chatBtn.sizeDelta        = new Vector2(-4f, 0f);
        chatBtn.anchoredPosition = new Vector2(2f, 0f);
        _chatToggleButtonLabel = chatBtn.GetComponentInChildren<Text>(true);
        RefreshChatToggleButtonLabel();
    }

    // ---- Chat view ----

    private void BuildChatView(RectTransform parent)
    {
        var go = new GameObject("ChatView", typeof(RectTransform));
        _chatView = go.GetComponent<RectTransform>();
        _chatView.SetParent(parent, false);
        _chatView.anchorMin = new Vector2(0f, 0f);
        _chatView.anchorMax = new Vector2(0f, 1f);
        _chatView.pivot = new Vector2(0f, 0.5f);
        _chatView.offsetMin = Vector2.zero;
        _chatView.offsetMax = new Vector2(ChatSidebarWidth, 0f);

        BuildChatInfoBar(_chatView);
        BuildChatBody(_chatView);
        BuildChatInputArea(_chatView);

        _chatView.gameObject.SetActive(false);
    }

    private void BuildChatInfoBar(RectTransform parent)
    {
        var barGo   = new GameObject("ChatInfoBar", typeof(RectTransform), typeof(Image));
        var barRect = barGo.GetComponent<RectTransform>();
        barRect.SetParent(parent, false);
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot     = new Vector2(0.5f, 1f);
        barRect.offsetMin = new Vector2(0f, -ChatInfoBarHeight);
        barRect.offsetMax = Vector2.zero;
        barGo.GetComponent<Image>().color = new Color(0.13f, 0.12f, 0.10f, 0.90f);

        // 上半行：房间名 + 人数
        _chatRoomLabel = MakeText("RoomLabel", barRect, "", 20,
            TextAnchor.MiddleLeft, new Color(0.92f, 0.92f, 0.95f, 1f));
        _chatRoomLabel.fontStyle                   = FontStyle.Bold;
        _chatRoomLabel.rectTransform.anchorMin     = new Vector2(0f, 0.5f);
        _chatRoomLabel.rectTransform.anchorMax     = new Vector2(1f, 1f);
        _chatRoomLabel.rectTransform.offsetMin     = new Vector2(Padding, 0f);
        _chatRoomLabel.rectTransform.offsetMax     = new Vector2(-180f, 0f);

        // 下半行：房间 ID
        _chatRoomIdLabel = MakeText("RoomIdLabel", barRect, "", 15,
            TextAnchor.MiddleLeft, new Color(0.62f, 0.72f, 0.82f, 1f));
        _chatRoomIdLabel.rectTransform.anchorMin   = new Vector2(0f, 0f);
        _chatRoomIdLabel.rectTransform.anchorMax   = new Vector2(1f, 0.5f);
        _chatRoomIdLabel.rectTransform.offsetMin   = new Vector2(Padding, 2f);
        _chatRoomIdLabel.rectTransform.offsetMax   = new Vector2(-180f, 0f);

        var leaveBtn = MakeButton("LeaveRoomButton", barRect, "← 离开房间", 17,
            new Color(0.48f, 0.22f, 0.16f, 0.94f), OnLeaveRoom);
        leaveBtn.anchorMin        = new Vector2(1f, 0.5f);
        leaveBtn.anchorMax        = new Vector2(1f, 0.5f);
        leaveBtn.pivot            = new Vector2(1f, 0.5f);
        leaveBtn.sizeDelta        = new Vector2(136f, 30f);
        leaveBtn.anchoredPosition = new Vector2(-16f, -18f);
    }

    // Body: left = player list (fixed width), right = chat messages (stretch)
    private void BuildChatBody(RectTransform parent)
    {
        float bodyTop = ChatInfoBarHeight + SectionGap;
        float bodyBot = ChatInputAreaHeight + ChatInputBotPad + SectionGap;

        var bodyGo   = new GameObject("ChatBody", typeof(RectTransform));
        var bodyRect = bodyGo.GetComponent<RectTransform>();
        bodyRect.SetParent(parent, false);
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.offsetMin = new Vector2(Padding, bodyBot);
        bodyRect.offsetMax = new Vector2(-Padding, -bodyTop);

        BuildPlayerListPanel(bodyRect);
        BuildChatMessagesPanel(bodyRect);
    }

    private void BuildPlayerListPanel(RectTransform bodyRect)
    {
        var panelGo   = new GameObject("PlayerListPanel", typeof(RectTransform), typeof(Image));
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.SetParent(bodyRect, false);
        // Fixed width on the left, full height
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot     = new Vector2(0f, 0.5f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panelRect.sizeDelta = new Vector2(PlayerPanelWidth, 0f);
        panelGo.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.92f);

        // Title bar
        var titleBarGo   = new GameObject("TitleBar", typeof(RectTransform), typeof(Image));
        var titleBarRect = titleBarGo.GetComponent<RectTransform>();
        titleBarRect.SetParent(panelRect, false);
        titleBarRect.anchorMin = new Vector2(0f, 1f);
        titleBarRect.anchorMax = new Vector2(1f, 1f);
        titleBarRect.pivot     = new Vector2(0.5f, 1f);
        titleBarRect.offsetMin = new Vector2(0f, -36f);
        titleBarRect.offsetMax = Vector2.zero;
        titleBarGo.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f, 0.95f);

        _playerCountLabel = MakeText("PlayerCount", titleBarRect, "当前玩家 (0)", 16,
            TextAnchor.MiddleCenter, new Color(0.76f, 0.45f, 0.14f, 1f));
        _playerCountLabel.fontStyle = FontStyle.Bold;
        Stretch(_playerCountLabel.rectTransform, Vector2.zero);

        // Scroll area for player rows
        var scrollGo   = new GameObject("PlayerScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        var scrollRect = scrollGo.GetComponent<RectTransform>();
        scrollRect.SetParent(panelRect, false);
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = new Vector2(0f, -36f);
        scrollGo.GetComponent<Image>().color = Color.clear;

        var sr = scrollGo.GetComponent<ScrollRect>();
        sr.horizontal        = false;
        sr.vertical          = true;
        sr.movementType      = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 16f;

        var viewportGo   = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        var viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.SetParent(scrollRect, false);
        Stretch(viewportRect, Vector2.zero);
        viewportGo.GetComponent<Image>().color          = Color.white;
        viewportGo.GetComponent<Image>().raycastTarget  = false;
        viewportGo.GetComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("PlayerListContent", typeof(RectTransform));
        _playerListContainer = contentGo.GetComponent<RectTransform>();
        _playerListContainer.SetParent(viewportRect, false);
        _playerListContainer.anchorMin = new Vector2(0f, 1f);
        _playerListContainer.anchorMax = new Vector2(1f, 1f);
        _playerListContainer.pivot     = new Vector2(0.5f, 1f);
        _playerListContainer.offsetMin = _playerListContainer.offsetMax = Vector2.zero;
        _playerListContainer.sizeDelta = Vector2.zero;

        sr.viewport = viewportRect;
        sr.content  = _playerListContainer;
    }

    private void BuildChatMessagesPanel(RectTransform bodyRect)
    {
        const float gap = 8f;

        var panelGo   = new GameObject("ChatMessagesPanel", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.SetParent(bodyRect, false);
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.offsetMin = new Vector2(PlayerPanelWidth + gap, 0f);
        panelRect.offsetMax = Vector2.zero;
        panelGo.GetComponent<Image>().color         = new Color(0.06f, 0.06f, 0.08f, 0.85f);
        panelGo.GetComponent<Image>().raycastTarget = true;

        _chatScrollRect = panelGo.GetComponent<ScrollRect>();
        _chatScrollRect.horizontal        = false;
        _chatScrollRect.vertical          = true;
        _chatScrollRect.movementType      = ScrollRect.MovementType.Clamped;
        _chatScrollRect.scrollSensitivity = 24f;

        var viewportGo   = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        var viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.SetParent(panelRect, false);
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(4f, 4f);
        viewportRect.offsetMax = new Vector2(-4f, -4f);
        viewportGo.GetComponent<Image>().color          = Color.white;
        viewportGo.GetComponent<Image>().raycastTarget  = false;
        viewportGo.GetComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("ChatContent", typeof(RectTransform));
        _chatMsgContainer = contentGo.GetComponent<RectTransform>();
        _chatMsgContainer.SetParent(viewportRect, false);
        _chatMsgContainer.anchorMin = new Vector2(0f, 1f);
        _chatMsgContainer.anchorMax = new Vector2(1f, 1f);
        _chatMsgContainer.pivot     = new Vector2(0.5f, 1f);
        _chatMsgContainer.offsetMin = _chatMsgContainer.offsetMax = Vector2.zero;
        _chatMsgContainer.sizeDelta = Vector2.zero;

        _chatScrollRect.viewport = viewportRect;
        _chatScrollRect.content  = _chatMsgContainer;
    }

    private void BuildChatInputArea(RectTransform parent)
    {
        var areaGo   = new GameObject("ChatInputArea", typeof(RectTransform));
        var areaRect = areaGo.GetComponent<RectTransform>();
        areaRect.SetParent(parent, false);
        areaRect.anchorMin = new Vector2(0f, 0f);
        areaRect.anchorMax = new Vector2(1f, 0f);
        areaRect.pivot     = new Vector2(0.5f, 0f);
        areaRect.offsetMin = new Vector2(Padding, ChatInputBotPad);
        areaRect.offsetMax = new Vector2(-Padding, ChatInputBotPad + ChatInputAreaHeight);

        // Input background
        var inputBgGo   = CreateRoundedGraphicObject("InputBg");
        var inputBgRect = inputBgGo.GetComponent<RectTransform>();
        inputBgRect.SetParent(areaRect, false);
        inputBgRect.anchorMin = Vector2.zero;
        inputBgRect.anchorMax = Vector2.one;
        inputBgRect.offsetMin = Vector2.zero;
        inputBgRect.offsetMax = new Vector2(-(ChatSendBtnWidth + 8f), 0f);
        var inputBg = inputBgGo.GetComponent<RoundedRectGraphic>();
        inputBg.Radius = InputRadius;
        inputBg.color = new Color(0.10f, 0.11f, 0.135f, 1f);

        _chatInputField = MakeInputField("ChatInput", inputBgRect, "输入消息，按 Enter 发送...", 20);
        Stretch(_chatInputField.GetComponent<RectTransform>(), new Vector2(12f, 4f));
        _chatInputField.onEndEdit.AddListener(text =>
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                OnPreviewChatSend();
        });

        var sendBtn = MakeButton("SendButton", areaRect, "发送", 20,
            new Color(0.24f, 0.44f, 0.68f, 0.96f), OnPreviewChatSend);
        sendBtn.anchorMin        = new Vector2(1f, 0.5f);
        sendBtn.anchorMax        = new Vector2(1f, 0.5f);
        sendBtn.pivot            = new Vector2(1f, 0.5f);
        sendBtn.sizeDelta        = new Vector2(ChatSendBtnWidth, ChatInputAreaHeight);
        sendBtn.anchoredPosition = Vector2.zero;
    }

    private void BuildCreateRoomOverlay(RectTransform parent)
    {
        var overlayGo = new GameObject("CreateRoomOverlay", typeof(RectTransform), typeof(Image));
        _createOverlay = overlayGo.GetComponent<RectTransform>();
        _createOverlay.SetParent(parent, false);
        Stretch(_createOverlay, Vector2.zero);
        overlayGo.GetComponent<Image>().color         = new Color(0f, 0f, 0f, 0.65f);
        overlayGo.GetComponent<Image>().raycastTarget = true;

        var dlgGo   = new GameObject("Dialog", typeof(RectTransform), typeof(Image), typeof(Outline));
        var dlgRect = dlgGo.GetComponent<RectTransform>();
        dlgRect.SetParent(_createOverlay, false);
        dlgRect.anchorMin        = new Vector2(0.5f, 0.5f);
        dlgRect.anchorMax        = new Vector2(0.5f, 0.5f);
        dlgRect.pivot            = new Vector2(0.5f, 0.5f);
        dlgRect.sizeDelta        = new Vector2(DialogWidth, DialogHeight);
        dlgRect.anchoredPosition = Vector2.zero;
        dlgGo.GetComponent<Image>().color            = new Color(0.12f, 0.12f, 0.15f, 0.98f);
        dlgGo.GetComponent<Outline>().effectColor    = new Color(0.76f, 0.45f, 0.14f, 0.70f);
        dlgGo.GetComponent<Outline>().effectDistance = new Vector2(2f, -2f);

        var dlgTitle = MakeText("Title", dlgRect, "创建新房间", 30,
            TextAnchor.MiddleCenter, new Color(0.97f, 0.83f, 0.49f, 1f));
        dlgTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
        dlgTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
        dlgTitle.rectTransform.pivot     = new Vector2(0.5f, 1f);
        dlgTitle.rectTransform.offsetMin = new Vector2(Padding, -72f);
        dlgTitle.rectTransform.offsetMax = new Vector2(-Padding, 0f);

        var nameLabel = MakeText("NameLabel", dlgRect, "房间名称：", 17,
            TextAnchor.MiddleLeft, new Color(0.76f, 0.81f, 0.86f, 1f));
        nameLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        nameLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        nameLabel.rectTransform.pivot     = new Vector2(0.5f, 1f);
        nameLabel.rectTransform.offsetMin = new Vector2(Padding, -112f);
        nameLabel.rectTransform.offsetMax = new Vector2(-Padding, -76f);

        _roomNameInput = MakeInputField("RoomNameInput", dlgRect, "请输入房间名称（最多 20 字）", 19);
        var inputRect = _roomNameInput.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0f, 1f);
        inputRect.anchorMax = new Vector2(1f, 1f);
        inputRect.pivot     = new Vector2(0.5f, 1f);
        inputRect.offsetMin = new Vector2(Padding, -200f);
        inputRect.offsetMax = new Vector2(-Padding, -116f);
        _roomNameInput.characterLimit = 20;

        var codeLabel = MakeText("CodeLabel", dlgRect, "锦标赛房间码：", 17,
            TextAnchor.MiddleLeft, new Color(0.76f, 0.81f, 0.86f, 1f));
        codeLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        codeLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        codeLabel.rectTransform.pivot     = new Vector2(0.5f, 1f);
        codeLabel.rectTransform.offsetMin = new Vector2(Padding, -236f);
        codeLabel.rectTransform.offsetMax = new Vector2(-Padding, -202f);

        _roomCodeInput = MakeInputField("RoomCodeInput", dlgRect, "输入游戏里的 6 位房间码", 19);
        var codeInputRect = _roomCodeInput.GetComponent<RectTransform>();
        codeInputRect.anchorMin = new Vector2(0f, 1f);
        codeInputRect.anchorMax = new Vector2(1f, 1f);
        codeInputRect.pivot     = new Vector2(0.5f, 1f);
        codeInputRect.offsetMin = new Vector2(Padding, -320f);
        codeInputRect.offsetMax = new Vector2(-Padding, -236f);
        _roomCodeInput.characterLimit = 6;
        _roomCodeInput.onValueChanged.AddListener(value =>
        {
            var normalized = NormalizeRoomCode(value);
            if (_roomCodeInput != null && !string.Equals(value, normalized, StringComparison.Ordinal))
                _roomCodeInput.text = normalized;
        });

        var btnRowGo   = new GameObject("ButtonRow", typeof(RectTransform));
        var btnRowRect = btnRowGo.GetComponent<RectTransform>();
        btnRowRect.SetParent(dlgRect, false);
        btnRowRect.anchorMin = new Vector2(0f, 0f);
        btnRowRect.anchorMax = new Vector2(1f, 0f);
        btnRowRect.pivot     = new Vector2(0.5f, 0f);
        btnRowRect.offsetMin = new Vector2(Padding, Padding);
        btnRowRect.offsetMax = new Vector2(-Padding, Padding + 56f);

        var confirmBtn = MakeButton("ConfirmButton", btnRowRect, "确认创建", 20,
            new Color(0.20f, 0.48f, 0.28f, 0.96f), OnConfirmCreateRoom);
        confirmBtn.anchorMin        = new Vector2(0f, 0f);
        confirmBtn.anchorMax        = new Vector2(0.5f, 1f);
        confirmBtn.pivot            = new Vector2(0.5f, 0.5f);
        confirmBtn.sizeDelta        = new Vector2(-4f, 0f);
        confirmBtn.anchoredPosition = new Vector2(-2f, 0f);

        var cancelBtn = MakeButton("CancelButton", btnRowRect, "取消", 20,
            new Color(0.42f, 0.18f, 0.18f, 0.94f), OnCancelCreateRoom);
        cancelBtn.anchorMin        = new Vector2(0.5f, 0f);
        cancelBtn.anchorMax        = new Vector2(1f, 1f);
        cancelBtn.pivot            = new Vector2(0.5f, 0.5f);
        cancelBtn.sizeDelta        = new Vector2(-4f, 0f);
        cancelBtn.anchoredPosition = new Vector2(2f, 0f);

        _createOverlay.gameObject.SetActive(false);
    }

    // ---- View switching ----

    private void ShowRoomListView()
    {
        ApplySplitLayout(false);
        _roomListView?.gameObject.SetActive(true);
        _chatView?.gameObject.SetActive(false);
        if (_headerTitleText != null) _headerTitleText.text = "锦标赛房间匹配";
        RefreshChatToggleButtonLabel();
    }

    private void ShowChatView(RoomEntry room)
    {
        if (_joinInFlight)
        {
            SetStatus($"正在连接房间 {room.Id}…");
            return;
        }

        _joinInFlight = true;
        _currentRoom = room;
        _currentPlayerCount = room.CurrentPlayers;
        _currentMaxPlayers = room.MaxPlayers;
        ApplySplitLayout(true);
        _roomListView?.gameObject.SetActive(true);
        _chatView?.gameObject.SetActive(true);
        if (_headerTitleText != null) _headerTitleText.text = "锦标赛房间匹配";
        RefreshChatRoomHeader();

        ClearChatMessages();

        // Seed player list with host
        _chatPlayers.Clear();
        if (!string.IsNullOrWhiteSpace(room.HostName))
            _chatPlayers.Add(PlayerEntry.Synthetic($"host:{room.HostName}", room.HostName));
        if (!string.Equals(room.HostName, _myName, StringComparison.Ordinal))
            _chatPlayers.Add(PlayerEntry.Synthetic($"local:{_myName}", _myName));
        RebuildPlayerList();

        // Connect to chat server
        _ = JoinChatAsync(room);
        RefreshChatToggleButtonLabel();
    }

    private void ApplySplitLayout(bool chatVisible)
    {
        var left = chatVisible ? ChatSidebarWidth : 0f;
        if (_roomListView != null)
        {
            _roomListView.offsetMin = new Vector2(left, 0f);
            _roomListView.offsetMax = new Vector2(0f, -HeaderHeight);
        }
        RefreshChatToggleButtonLabel();
    }

    // ---- Room list logic ----

    private void RefreshRoomList()
    {
        _selectedRow = null;
        SetStatus("加载中…");
        StartCoroutine(FetchRoomsCoroutine());
    }

    private IEnumerator FetchRoomsCoroutine()
    {
        using var req = UnityWebRequest.Get($"{ServerHttpUrl}/rooms");
        req.timeout = 5;
        yield return req.SendWebRequest();

        List<RoomEntry> rooms;
        if (req.result == UnityWebRequest.Result.Success)
        {
            rooms = ParseRoomsJson(req.downloadHandler.text);
        }
        else
        {
            Debug.LogWarning($"[TournamentPreview] Failed to fetch rooms: {req.error}");
            rooms = new List<RoomEntry>();
        }

        _rooms.Clear();
        _rooms.AddRange(rooms);
        var filtered = GetFilteredRooms();
        SetStatus(string.IsNullOrEmpty(_searchQuery)
            ? $"共 {_rooms.Count} 个房间可用"
            : $"搜索结果：{filtered.Count}/{_rooms.Count} 个房间");
        RebuildRows(filtered);
    }

    private static List<RoomEntry> ParseRoomsJson(string json)
    {
        var rooms = new List<RoomEntry>();

        if (!TryGetRoomArray(json, out var roomArray))
            return rooms;

        foreach (var item in roomArray)
        {
            if (item is not Dictionary<string, object?> obj)
                continue;

            var code = PreviewJson.String(obj, "code") ?? "";
            var name = PreviewJson.String(obj, "name") ?? code;
            var host = PreviewJson.String(obj, "hostName")
                ?? PreviewJson.String(obj, "host")
                ?? "";
            var count = PreviewJson.Int(obj, "playerCount", PreviewJson.Int(obj, "count"));
            var max = PreviewJson.Int(obj, "maxPlayers", PreviewJson.Int(obj, "max"));
            if (max <= 0) max = 30;
            if (!string.IsNullOrEmpty(code))
                rooms.Add(new RoomEntry(code, name, host, count, max));
        }

        return rooms;
    }

    private static bool TryGetRoomArray(string json, out List<object?> rooms)
    {
        if (PreviewJson.TryParseArray(json, out rooms))
            return true;

        if (PreviewJson.TryParseObject(json, out var obj)
            && obj.TryGetValue("rooms", out var roomsValue)
            && roomsValue is List<object?> parsedRooms)
        {
            rooms = parsedRooms;
            return true;
        }

        rooms = new List<object?>();
        return false;
    }

    private List<RoomEntry> GetFilteredRooms()
    {
        if (string.IsNullOrEmpty(_searchQuery)) return _rooms;
        var result = new List<RoomEntry>();
        foreach (var r in _rooms)
            if (r.Name.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0
                || r.Id.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                result.Add(r);
        return result;
    }

    private void RebuildRows(List<RoomEntry> rooms)
    {
        if (_roomListContainer == null) return;

        foreach (var v in _rowViews)
            if (v.Root != null) Destroy(v.Root.gameObject);
        _rowViews.Clear();

        if (_emptyLabel != null)
            _emptyLabel.gameObject.SetActive(rooms.Count == 0);

        for (var i = 0; i < rooms.Count; i++)
        {
            var rowRect = BuildRow(_roomListContainer, i, rooms[i]);
            if (rowRect == null) continue;
            var view = new RowView(rowRect, rooms[i]);
            _rowViews.Add(view);
            var btn = rowRect.GetComponent<Button>();
            if (btn != null)
            {
                var captured = view;
                btn.onClick.AddListener(() => OnRowClicked(captured));
            }
        }

        _roomListContainer.sizeDelta = new Vector2(0f,
            rooms.Count > 0 ? rooms.Count * (RowHeight + RowSpacing) : 0f);
    }

    private RectTransform? BuildRow(RectTransform parent, int index, RoomEntry room)
    {
        var rowGo = CreateRoundedGraphicObject($"RoomRow_{index}");
        rowGo.AddComponent<Button>();
        rowGo.AddComponent<Outline>();
        var rowRect = rowGo.GetComponent<RectTransform>();
        rowRect.SetParent(parent, false);
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot     = new Vector2(0.5f, 1f);

        float top = index * (RowHeight + RowSpacing);
        rowRect.offsetMin = new Vector2(6f,  -(top + RowHeight));
        rowRect.offsetMax = new Vector2(-6f, -top);

        var bg = rowGo.GetComponent<RoundedRectGraphic>();
        bg.Radius = ButtonRadius;
        bg.color         = new Color(0.17f, 0.18f, 0.22f, 0.94f);
        bg.raycastTarget = true;
        rowGo.GetComponent<Outline>().effectColor    = new Color(0f, 0f, 0f, 0.45f);
        rowGo.GetComponent<Outline>().effectDistance = new Vector2(1f, -1f);

        var btn = rowGo.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition    = Selectable.Transition.ColorTint;
        btn.navigation    = new Navigation { mode = Navigation.Mode.None };

        var nameText = MakeText("RoomName", rowRect, room.Name, 22,
            TextAnchor.MiddleLeft, new Color(0.93f, 0.93f, 0.95f, 1f));
        nameText.fontStyle         = FontStyle.Bold;
        nameText.rectTransform.anchorMin = new Vector2(0f,    0.45f);
        nameText.rectTransform.anchorMax = new Vector2(0.65f, 1f);
        nameText.rectTransform.offsetMin = new Vector2(Padding, 0f);
        nameText.rectTransform.offsetMax = Vector2.zero;

        var hostText = MakeText("HostName", rowRect, $"房主：{room.HostName}", 16,
            TextAnchor.MiddleLeft, new Color(0.66f, 0.72f, 0.78f, 1f));
        hostText.rectTransform.anchorMin = new Vector2(0f,    0f);
        hostText.rectTransform.anchorMax = new Vector2(0.65f, 0.50f);
        hostText.rectTransform.offsetMin = new Vector2(Padding, 0f);
        hostText.rectTransform.offsetMax = Vector2.zero;

        bool isFull = room.CurrentPlayers >= room.MaxPlayers;
        var countColor = isFull
            ? new Color(0.85f, 0.40f, 0.30f, 1f)
            : new Color(0.75f, 0.78f, 0.82f, 0.98f);
        var countText = MakeText("PlayerCount", rowRect,
            $"{room.CurrentPlayers}/{room.MaxPlayers} 人", 20,
            TextAnchor.MiddleRight, countColor);
        countText.fontStyle             = FontStyle.Bold;
        countText.rectTransform.anchorMin = new Vector2(0.65f, 0f);
        countText.rectTransform.anchorMax = new Vector2(1f,    1f);
        countText.rectTransform.offsetMin = Vector2.zero;
        countText.rectTransform.offsetMax = new Vector2(-Padding, 0f);

        return rowRect;
    }

    // ---- Handlers ----

    private void OnSearchChanged(string query)
    {
        _searchQuery = query?.Trim() ?? string.Empty;
        RefreshRoomList();
    }

    private void OnCreateRoomClicked()
    {
        if (_createOverlay == null) return;
        if (_roomNameInput != null) _roomNameInput.text = string.Empty;
        if (_roomCodeInput != null) _roomCodeInput.text = string.Empty;
        _createOverlay.gameObject.SetActive(true);
    }

    private void OnConfirmCreateRoom()
    {
        var name = _roomNameInput?.text?.Trim();
        if (string.IsNullOrEmpty(name)) { SetStatus("请先填写房间名称"); return; }
        var code = NormalizeRoomCode(_roomCodeInput?.text);
        if (!IsValidRoomCode(code)) { SetStatus("请输入游戏锦标赛里的 6 位房间码"); return; }
        var newRoom = new RoomEntry(code, name!, "你", 1, 30);
        _createOverlay?.gameObject.SetActive(false);
        _searchQuery = string.Empty;
        if (_searchInput != null) _searchInput.text = string.Empty;
        ShowChatView(newRoom);
    }

    private void OnCancelCreateRoom() => _createOverlay?.gameObject.SetActive(false);

    private void OnJoinRoomClicked()
    {
        if (_selectedRow == null) { SetStatus("请先从列表中选择一个房间"); return; }
        ShowChatView(_selectedRow.Entry);
    }

    private void OnManualJoinRoom()
    {
        var code = NormalizeRoomCode(_manualRoomCodeInput?.text);
        if (!IsValidRoomCode(code))
        {
            SetStatus("请输入游戏锦标赛里的 6 位房间码");
            return;
        }

        var knownRoom = _rooms.Find(room => string.Equals(room.Id, code, StringComparison.OrdinalIgnoreCase));
        ShowChatView(knownRoom ?? new RoomEntry(code, code, "", 0, 30));
    }

    private void OnToggleChatClicked()
    {
        if (_currentRoom == null)
        {
            SetStatus("请先创建或加入一个房间");
            return;
        }

        var nextVisible = _chatView == null || !_chatView.gameObject.activeSelf;
        _chatView?.gameObject.SetActive(nextVisible);
        ApplySplitLayout(nextVisible);
    }

    private void RefreshChatToggleButtonLabel()
    {
        if (_chatToggleButtonLabel == null)
            return;

        _chatToggleButtonLabel.text = _chatView != null && _chatView.gameObject.activeSelf
            ? "关闭聊天"
            : "打开聊天";
    }

    private void OnPreviewChatSend()
    {
        var text = _chatInputField?.text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (_chatInputField != null) _chatInputField.text = string.Empty;
        SendChatMessage(text!);
    }

    private void OnLeaveRoom()
    {
        _joinInFlight = false;
        DisconnectChat();
        ShowRoomListView();
        RefreshRoomList();
    }

    private void OnRowClicked(RowView view)
    {
        if (_selectedRow != null && _selectedRow != view)
        {
            var prevBg = _selectedRow.Root?.GetComponent<Graphic>();
            if (prevBg != null) prevBg.color = new Color(0.17f, 0.18f, 0.22f, 0.94f);
        }

        if (_selectedRow == view)
        {
            var bg = view.Root?.GetComponent<Graphic>();
            if (bg != null) bg.color = new Color(0.17f, 0.18f, 0.22f, 0.94f);
            _selectedRow = null;
            SetStatus($"共 {_rooms.Count} 个房间可用");
        }
        else
        {
            var bg = view.Root?.GetComponent<Graphic>();
            if (bg != null) bg.color = new Color(0.20f, 0.38f, 0.56f, 0.96f);
            _selectedRow = view;
            var r = view.Entry;
            SetStatus($"已选择：{r.Name}（{r.CurrentPlayers}/{r.MaxPlayers} 人）");
        }
    }

    // ---- WebSocket chat connection ----

    private async Task JoinChatAsync(RoomEntry room)
    {
        DisconnectChat();
        _wsCts = new CancellationTokenSource();

        var httpBase = ServerHttpUrl;
        var joinJson =
            $"{{\"code\":{PreviewJson.Quote(room.Id)},\"name\":{PreviewJson.Quote(_myName)},\"roomName\":{PreviewJson.Quote(room.Name)}}}";

        string chatUrl;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var resp = await http.PostAsync($"{httpBase}/match/join",
                new StringContent(joinJson, Encoding.UTF8, "application/json"),
                _wsCts.Token).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var err = ParseErrorMessage(body) ?? $"HTTP {(int)resp.StatusCode}";
                _incoming.Enqueue(ChatMessage.System($"加入失败：{err}"));
                _joinInFlight = false;
                return;
            }
            chatUrl = ParseJoinChatUrl(body);
            if (string.IsNullOrEmpty(chatUrl))
            {
                _incoming.Enqueue(ChatMessage.System("服务器未返回聊天地址"));
                _joinInFlight = false;
                return;
            }
        }
        catch (Exception ex)
        {
            _incoming.Enqueue(ChatMessage.System($"无法连接服务器：{ex.Message}"));
            _joinInFlight = false;
            return;
        }

        _ws = new ClientWebSocket();
        try
        {
            await _ws.ConnectAsync(new Uri(chatUrl), _wsCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _incoming.Enqueue(ChatMessage.System($"WebSocket 连接失败：{ex.Message}"));
            _joinInFlight = false;
            return;
        }

        _joinInFlight = false;
        _ = Task.Run(() => WsReceiveLoop(_wsCts.Token));
    }

    private async Task WsReceiveLoop(CancellationToken ct)
    {
        var buf = new byte[8192];
        try
        {
            while (_ws != null && _ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _incoming.Enqueue(ChatMessage.System("连接已断开"));
                        return;
                    }

                    if (result.Count > 0)
                        message.Write(buf, 0, result.Count);
                }
                while (!result.EndOfMessage && !ct.IsCancellationRequested);

                var json = Encoding.UTF8.GetString(message.ToArray());
                EnqueueParsedMessages(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _incoming.Enqueue(ChatMessage.System($"连接错误：{ex.Message}"));
        }
    }

    private static string ParseJoinChatUrl(string json)
    {
        return PreviewJson.TryParseObject(json, out var obj)
            ? PreviewJson.String(obj, "chatUrl") ?? string.Empty
            : string.Empty;
    }

    private static string? ParseErrorMessage(string json)
    {
        if (!PreviewJson.TryParseObject(json, out var obj))
            return null;

        return PreviewJson.String(obj, "error")
            ?? PreviewJson.String(obj, "message")
            ?? PreviewJson.String(obj, "code");
    }

    private void EnqueueParsedMessages(string json)
    {
        if (!PreviewJson.TryParseObject(json, out var obj))
        {
            _incoming.Enqueue(ChatMessage.System(json));
            return;
        }

        var type = PreviewJson.String(obj, "type") ?? string.Empty;
        if (type == "history")
        {
            if (obj.TryGetValue("messages", out var messagesValue)
                && messagesValue is List<object?> messages)
            {
                foreach (var item in messages)
                    if (item is Dictionary<string, object?> messageObj)
                        _incoming.Enqueue(ParseWsMessageObject(messageObj));
            }
            return;
        }

        _incoming.Enqueue(ParseWsMessageObject(obj));
    }

    private ChatMessage ParseWsMessageObject(Dictionary<string, object?> obj)
    {
        var type = PreviewJson.String(obj, "type") ?? string.Empty;
        var userId = PreviewJson.String(obj, "userId");
        var name = PreviewJson.String(obj, "name");
        var count = PreviewJson.Int(obj, "count", -1);
        var max = PreviewJson.Int(obj, "max", -1);

        switch (type)
        {
            case "welcome":
            {
                var code = PreviewJson.String(obj, "roomCode") ?? "";
                return ChatMessage.Welcome(
                    userId,
                    _myName,
                    count,
                    max,
                    $"已连接到房间 {code}（{Mathf.Max(count, 0)}/{Mathf.Max(max, 0)} 人）");
            }
            case "join":
            {
                name ??= "玩家";
                return ChatMessage.Join(userId, name, count, $"{name} 加入了房间");
            }
            case "leave":
            {
                name ??= "玩家";
                return ChatMessage.Leave(userId, name, count, $"{name} 离开了房间");
            }
            case "chat":
            {
                name ??= "未知";
                var text = PreviewJson.String(obj, "text") ?? "";
                return ChatMessage.Chat(userId, name, text);
            }
            case "error":
            {
                var msg = PreviewJson.String(obj, "message") ?? "未知错误";
                return ChatMessage.System($"错误：{msg}");
            }
            default:
                return ChatMessage.System(type);
        }
    }

    private void SendChatMessage(string text)
    {
        if (_ws?.State != WebSocketState.Open || _wsCts == null) return;
        var json = $"{{\"type\":\"chat\",\"text\":{PreviewJson.Quote(text)}}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        _ = _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _wsCts.Token);
    }

    private void DisconnectChat()
    {
        _joinInFlight = false;
        _wsCts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving", CancellationToken.None); }
            catch { }
        }
        _ws?.Dispose();
        _ws = null;
        _wsCts?.Dispose();
        _wsCts = null;
    }

    // ---- Player list ----

    private void RebuildPlayerList()
    {
        if (_playerListContainer == null) return;
        foreach (var lbl in _playerLabels) if (lbl != null) Destroy(lbl.gameObject);
        _playerLabels.Clear();

        for (var i = 0; i < _chatPlayers.Count; i++)
        {
            var player = _chatPlayers[i];
            var isSelf = player.Name == _myName;
            var color  = isSelf ? new Color(0.97f, 0.83f, 0.49f, 1f) : new Color(0.87f, 0.87f, 0.90f, 1f);
            var lbl    = MakeText($"Player_{i}", _playerListContainer,
                isSelf ? $"{player.Name}（我）" : player.Name, 17, TextAnchor.MiddleLeft, color);
            if (i == 0) lbl.fontStyle = FontStyle.Bold;
            lbl.rectTransform.anchorMin = new Vector2(0f, 1f);
            lbl.rectTransform.anchorMax = new Vector2(1f, 1f);
            lbl.rectTransform.pivot     = new Vector2(0.5f, 1f);
            lbl.rectTransform.offsetMin = new Vector2(12f, -(i + 1) * PlayerRowHeight);
            lbl.rectTransform.offsetMax = new Vector2(-12f, -i * PlayerRowHeight);
            _playerLabels.Add(lbl);
        }

        _playerListContainer.sizeDelta = new Vector2(0f, _chatPlayers.Count * PlayerRowHeight);
        if (_playerCountLabel != null)
            _playerCountLabel.text = $"当前玩家 ({GetDisplayedPlayerCount()})";
    }

    private void AddOrUpdateChatPlayer(string? userId, string name)
    {
        var key = PlayerEntry.MakeKey(userId, name);
        for (var i = 0; i < _chatPlayers.Count; i++)
        {
            if (_chatPlayers[i].Key == key)
            {
                _chatPlayers[i] = new PlayerEntry(key, userId, name);
                RebuildPlayerList();
                return;
            }
        }

        if (!string.IsNullOrEmpty(userId))
            _chatPlayers.RemoveAll(p => p.UserId == null && p.Name == name);

        _chatPlayers.Add(new PlayerEntry(key, userId, name));
        RebuildPlayerList();
    }

    private void RemoveChatPlayer(string? userId, string name)
    {
        var key = PlayerEntry.MakeKey(userId, name);
        var removed = _chatPlayers.RemoveAll(p => p.Key == key) > 0;
        if (!removed && string.IsNullOrEmpty(userId))
            removed = _chatPlayers.RemoveAll(p => p.Name == name) > 0;

        if (removed) RebuildPlayerList();
    }

    private int GetDisplayedPlayerCount()
        => _currentPlayerCount > 0 ? _currentPlayerCount : _chatPlayers.Count;

    private void UpdatePlayerCountFromMessage(ChatMessage msg)
    {
        if (msg.PlayerCount >= 0)
            _currentPlayerCount = msg.PlayerCount;
        if (msg.MaxPlayers > 0)
            _currentMaxPlayers = msg.MaxPlayers;

        RefreshChatRoomHeader();
        if (_playerCountLabel != null)
            _playerCountLabel.text = $"当前玩家 ({GetDisplayedPlayerCount()})";
    }

    private void RefreshChatRoomHeader()
    {
        if (_currentRoom == null)
            return;

        var count = GetDisplayedPlayerCount();
        var max = _currentMaxPlayers > 0 ? _currentMaxPlayers : _currentRoom.MaxPlayers;
        if (_chatRoomLabel != null)
            _chatRoomLabel.text = $"{_currentRoom.Name}  ·  {count}/{max} 人";
        if (_chatRoomIdLabel != null)
            _chatRoomIdLabel.text = $"房间码：{_currentRoom.Id}";
    }

    // ---- Chat message display ----

    private void AppendChatMessage(ChatMessage msg)
    {
        if (_chatMsgContainer == null) return;

        if (_chatLabels.Count >= MaxChatMessages)
        {
            var oldest = _chatLabels[0];
            _chatLabels.RemoveAt(0);
            RecycleChatLabel(oldest);
        }

        const float lineH = 32f;
        var idx = _chatLabels.Count;

        Color color;
        string display;
        if (msg.IsSystem)
        {
            color   = new Color(0.55f, 0.60f, 0.65f, 1f);
            display = $"[系统] {msg.Text}";
        }
        else if (msg.From == _myName)
        {
            color   = new Color(0.97f, 0.83f, 0.49f, 1f);
            display = $"{_myName}: {msg.Text}";
        }
        else
        {
            color   = new Color(0.90f, 0.90f, 0.93f, 1f);
            display = $"{msg.From}: {msg.Text}";
        }

        var lbl = RentChatLabel(idx, display, color, msg.IsSystem);
        _chatLabels.Add(lbl);
        LayoutChatLabels();

        _chatMsgContainer.sizeDelta = new Vector2(0f, (_chatLabels.Count + 1) * lineH);
        if (_chatScrollRect != null)
            _chatScrollRect.verticalNormalizedPosition = 0f;
    }

    private Text RentChatLabel(int index, string text, Color color, bool isSystem)
    {
        Text? lbl = null;
        while (_chatLabelPool.Count > 0 && lbl == null)
            lbl = _chatLabelPool.Pop();

        lbl ??= MakeText($"Msg_{index}", _chatMsgContainer!, text, 17, TextAnchor.MiddleLeft, color);
        lbl.name = $"Msg_{index}";
        lbl.transform.SetParent(_chatMsgContainer, false);
        lbl.gameObject.SetActive(true);
        lbl.text = text;
        lbl.color = color;
        lbl.fontStyle = isSystem ? FontStyle.Italic : FontStyle.Normal;
        lbl.alignment = TextAnchor.MiddleLeft;
        return lbl;
    }

    private void RecycleChatLabel(Text? lbl)
    {
        if (lbl == null)
            return;

        lbl.text = string.Empty;
        lbl.gameObject.SetActive(false);
        _chatLabelPool.Push(lbl);
    }

    private void ClearChatMessages()
    {
        foreach (var lbl in _chatLabels)
            RecycleChatLabel(lbl);
        _chatLabels.Clear();

        if (_chatMsgContainer != null)
            _chatMsgContainer.sizeDelta = Vector2.zero;
    }

    private void LayoutChatLabels()
    {
        if (_chatMsgContainer == null)
            return;

        const float lineH = 32f;
        for (var i = 0; i < _chatLabels.Count; i++)
        {
            var lbl = _chatLabels[i];
            if (lbl == null)
                continue;

            lbl.name = $"Msg_{i}";
            lbl.rectTransform.anchorMin = new Vector2(0f, 1f);
            lbl.rectTransform.anchorMax = new Vector2(1f, 1f);
            lbl.rectTransform.pivot     = new Vector2(0.5f, 1f);
            lbl.rectTransform.offsetMin = new Vector2(8f, -(i + 1) * lineH);
            lbl.rectTransform.offsetMax = new Vector2(-8f, -i * lineH);
        }

        _chatMsgContainer.sizeDelta = new Vector2(0f, (_chatLabels.Count + 1) * lineH);
    }

    // ---- Helpers ----

    private void SetStatus(string text) { if (_statusText != null) _statusText.text = text; }

    private static string NormalizeRoomCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = new List<char>(6);
        foreach (var c in value.Trim().ToUpperInvariant())
        {
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                chars.Add(c);
            if (chars.Count == 6)
                break;
        }

        return new string(chars.ToArray());
    }

    private static bool IsValidRoomCode(string value) => value.Length == 6;

    private Text MakeText(string name, RectTransform parent, string text, int fontSize,
        TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text            = text;
        t.font            = _font;
        t.fontSize        = fontSize;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = Mathf.Max(12, fontSize - 5);
        t.resizeTextMaxSize = fontSize;
        t.alignment       = alignment;
        t.color           = color;
        t.raycastTarget   = false;
        t.supportRichText = false;
        return t;
    }

    private RectTransform MakeButton(string name, RectTransform parent, string label,
        int fontSize, Color bgColor, UnityEngine.Events.UnityAction onClick)
    {
        var go = CreateRoundedGraphicObject(name);
        go.AddComponent<Button>();
        go.AddComponent<Outline>();
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        var bg = go.GetComponent<RoundedRectGraphic>();
        bg.Radius = ButtonRadius;
        bg.color         = bgColor;
        bg.raycastTarget = true;
        go.GetComponent<Outline>().effectColor    = new Color(1f, 1f, 1f, 0.15f);
        go.GetComponent<Outline>().effectDistance = new Vector2(1f, -1f);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition    = Selectable.Transition.ColorTint;
        btn.navigation    = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(onClick);

        var lbl = MakeText("Label", rect, label, fontSize, TextAnchor.MiddleCenter, Color.white);
        Stretch(lbl.rectTransform, Vector2.zero);

        return rect;
    }

    private InputField MakeInputField(string name, RectTransform parent, string placeholder, int fontSize)
    {
        var go = CreateRoundedGraphicObject(name);
        go.AddComponent<InputField>();
        go.AddComponent<Outline>();
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        var bg = go.GetComponent<RoundedRectGraphic>();
        bg.Radius = InputRadius;
        bg.color = new Color(0.105f, 0.115f, 0.14f, 1f);
        bg.raycastTarget = true;
        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.10f);
        outline.effectDistance = new Vector2(1f, -1f);

        var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        phGo.transform.SetParent(rect, false);
        Stretch((RectTransform)phGo.transform, new Vector2(8f, 2f));
        var phText = phGo.GetComponent<Text>();
        phText.text          = placeholder;
        phText.font          = _font;
        phText.fontSize      = fontSize;
        phText.color         = new Color(0.52f, 0.56f, 0.62f, 0.86f);
        phText.fontStyle     = FontStyle.Italic;
        phText.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(rect, false);
        Stretch((RectTransform)textGo.transform, new Vector2(8f, 2f));
        var inputText = textGo.GetComponent<Text>();
        inputText.font            = _font;
        inputText.fontSize        = fontSize;
        inputText.color           = new Color(0.94f, 0.96f, 0.98f, 1f);
        inputText.raycastTarget   = false;
        inputText.supportRichText = false;

        var field = go.GetComponent<InputField>();
        field.textComponent = inputText;
        field.placeholder   = phText;
        field.targetGraphic = bg;
        field.transition    = Selectable.Transition.ColorTint;
        field.navigation    = new Navigation { mode = Navigation.Mode.None };

        return field;
    }

    private GameObject CreateRoundedGraphicObject(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        EnsureCanvasRenderer(go);
        go.AddComponent<RoundedRectGraphic>();
        EnsureCanvasRenderer(go);
        return go;
    }

    private static void EnsureCanvasRenderer(GameObject go)
    {
        if (go.GetComponent<CanvasRenderer>() == null)
            go.AddComponent<CanvasRenderer>();
    }

    private static void Stretch(RectTransform rect, Vector2 inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = inset;
        rect.offsetMax = -inset;
    }

    // ---- Data ----

    internal sealed class RoomEntry
    {
        internal RoomEntry(string id, string name, string hostName, int currentPlayers, int maxPlayers)
        {
            Id = id; Name = name; HostName = hostName;
            CurrentPlayers = currentPlayers; MaxPlayers = maxPlayers;
        }
        internal string Id             { get; }
        internal string Name           { get; }
        internal string HostName       { get; }
        internal int    CurrentPlayers { get; }
        internal int    MaxPlayers     { get; }
    }

    private readonly struct PlayerEntry
    {
        internal PlayerEntry(string key, string? userId, string name)
        {
            Key = key;
            UserId = string.IsNullOrEmpty(userId) ? null : userId;
            Name = name;
        }

        internal string Key { get; }
        internal string? UserId { get; }
        internal string Name { get; }

        internal static PlayerEntry Synthetic(string key, string name) => new PlayerEntry(key, null, name);

        internal static string MakeKey(string? userId, string name)
            => !string.IsNullOrEmpty(userId)
                ? $"id:{userId}"
                : $"name:{name}";
    }

    private readonly struct ChatMessage
    {
        private ChatMessage(string? fromUserId, string from, string text, bool isSystem, string? eventPlayerId, string? eventPlayerName, bool isJoin, int playerCount, int maxPlayers)
        {
            FromUserId = fromUserId;
            From = from;
            Text = text;
            IsSystem = isSystem;
            EventPlayerId = eventPlayerId;
            EventPlayerName = eventPlayerName;
            IsJoinEvent = isJoin;
            PlayerCount = playerCount;
            MaxPlayers = maxPlayers;
        }

        internal static ChatMessage System(string text) =>
            new ChatMessage(null, "", text, true, null, null, false, -1, -1);

        internal static ChatMessage Welcome(string? userId, string playerName, int count, int max, string displayText) =>
            new ChatMessage(null, "", displayText, true, userId, playerName, true, count, max);

        internal static ChatMessage Join(string? userId, string playerName, int count, string displayText) =>
            new ChatMessage(null, "", displayText, true, userId, playerName, true, count, -1);

        internal static ChatMessage Leave(string? userId, string playerName, int count, string displayText) =>
            new ChatMessage(null, "", displayText, true, userId, playerName, false, count, -1);

        internal static ChatMessage Chat(string? fromUserId, string from, string text) =>
            new ChatMessage(fromUserId, from, text, false, null, null, false, -1, -1);

        internal string? FromUserId { get; }
        internal string From { get; }
        internal string Text { get; }
        internal bool IsSystem { get; }
        internal string? EventPlayerId { get; }
        internal string? EventPlayerName { get; }
        internal bool IsJoinEvent { get; }
        internal int PlayerCount { get; }
        internal int MaxPlayers { get; }
    }

    private sealed class RowView
    {
        internal RowView(RectTransform root, RoomEntry entry) { Root = root; Entry = entry; }
        internal RectTransform Root  { get; }
        internal RoomEntry     Entry { get; }
    }

    private static class PreviewJson
    {
        internal static bool TryParseObject(string json, out Dictionary<string, object?> obj)
        {
            obj = new Dictionary<string, object?>(StringComparer.Ordinal);
            try
            {
                var reader = new Reader(json);
                var value = reader.ReadValue();
                reader.SkipWhitespace();
                if (!reader.IsDone || value is not Dictionary<string, object?> parsed)
                    return false;

                obj = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryParseArray(string json, out List<object?> array)
        {
            array = new List<object?>();
            try
            {
                var reader = new Reader(json);
                var value = reader.ReadValue();
                reader.SkipWhitespace();
                if (!reader.IsDone || value is not List<object?> parsed)
                    return false;

                array = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string? String(Dictionary<string, object?> obj, string key)
            => obj.TryGetValue(key, out var value) ? value as string : null;

        internal static int Int(Dictionary<string, object?> obj, string key, int fallback = 0)
        {
            if (!obj.TryGetValue(key, out var value) || value == null)
                return fallback;

            if (value is int i)
                return i;
            if (value is long l && l <= int.MaxValue && l >= int.MinValue)
                return (int)l;
            if (value is double d && d <= int.MaxValue && d >= int.MinValue)
                return (int)d;
            if (value is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return fallback;
        }

        internal static string Quote(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (char.IsControl(c))
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private sealed class Reader
        {
            private readonly string _json;
            private int _pos;

            internal Reader(string json) => _json = json ?? string.Empty;
            internal bool IsDone => _pos >= _json.Length;

            internal void SkipWhitespace()
            {
                while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos]))
                    _pos++;
            }

            internal object? ReadValue()
            {
                SkipWhitespace();
                if (_pos >= _json.Length)
                    throw new FormatException("Unexpected end of JSON.");

                var c = _json[_pos];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't') return ReadLiteral("true", true);
                if (c == 'f') return ReadLiteral("false", false);
                if (c == 'n') return ReadLiteral("null", null);
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw new FormatException($"Unexpected JSON token at {_pos}.");
            }

            private Dictionary<string, object?> ReadObject()
            {
                var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
                _pos++;
                SkipWhitespace();
                if (TryConsume('}'))
                    return obj;

                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _json.Length || _json[_pos] != '"')
                        throw new FormatException("Expected object key.");

                    var key = ReadString();
                    SkipWhitespace();
                    Consume(':');
                    obj[key] = ReadValue();
                    SkipWhitespace();

                    if (TryConsume('}'))
                        return obj;
                    Consume(',');
                }
            }

            private List<object?> ReadArray()
            {
                var array = new List<object?>();
                _pos++;
                SkipWhitespace();
                if (TryConsume(']'))
                    return array;

                while (true)
                {
                    array.Add(ReadValue());
                    SkipWhitespace();

                    if (TryConsume(']'))
                        return array;
                    Consume(',');
                }
            }

            private string ReadString()
            {
                Consume('"');
                var sb = new StringBuilder();
                while (_pos < _json.Length)
                {
                    var c = _json[_pos++];
                    if (c == '"')
                        return sb.ToString();

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (_pos >= _json.Length)
                        throw new FormatException("Unexpected end of escaped string.");

                    var esc = _json[_pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _json.Length)
                                throw new FormatException("Invalid unicode escape.");
                            var hex = _json.Substring(_pos, 4);
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default:
                            throw new FormatException($"Invalid escape sequence: {esc}");
                    }
                }

                throw new FormatException("Unterminated JSON string.");
            }

            private object ReadNumber()
            {
                var start = _pos;
                if (_json[_pos] == '-')
                    _pos++;

                while (_pos < _json.Length && char.IsDigit(_json[_pos]))
                    _pos++;

                var isFloat = false;
                if (_pos < _json.Length && _json[_pos] == '.')
                {
                    isFloat = true;
                    _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos]))
                        _pos++;
                }

                if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
                {
                    isFloat = true;
                    _pos++;
                    if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-'))
                        _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos]))
                        _pos++;
                }

                var raw = _json.Substring(start, _pos - start);
                if (isFloat)
                    return double.Parse(raw, CultureInfo.InvariantCulture);

                return long.Parse(raw, CultureInfo.InvariantCulture);
            }

            private object? ReadLiteral(string literal, object? value)
            {
                if (_pos + literal.Length > _json.Length
                    || string.CompareOrdinal(_json, _pos, literal, 0, literal.Length) != 0)
                    throw new FormatException($"Expected {literal}.");

                _pos += literal.Length;
                return value;
            }

            private bool TryConsume(char expected)
            {
                if (_pos >= _json.Length || _json[_pos] != expected)
                    return false;

                _pos++;
                return true;
            }

            private void Consume(char expected)
            {
                if (!TryConsume(expected))
                    throw new FormatException($"Expected '{expected}'.");
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    private sealed class RoundedRectGraphic : Graphic
    {
        [SerializeField] private float _radius = 12f;
        [SerializeField] private int _cornerSegments = 8;

        internal float Radius
        {
            get => _radius;
            set
            {
                _radius = Mathf.Max(0f, value);
                SetVerticesDirty();
            }
        }

        protected override void OnEnable()
        {
            if (GetComponent<CanvasRenderer>() == null)
                gameObject.AddComponent<CanvasRenderer>();

            base.OnEnable();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var rect = GetPixelAdjustedRect();
            var radius = Mathf.Min(_radius, Mathf.Min(rect.width, rect.height) * 0.5f);
            if (radius <= 0.01f)
            {
                AddQuad(vh, rect.min, rect.max);
                return;
            }

            var points = new List<Vector2>(4 * (_cornerSegments + 1));
            AddCorner(points, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f);
            AddCorner(points, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f);
            AddCorner(points, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f);
            AddCorner(points, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f);

            var center = rect.center;
            var centerIndex = vh.currentVertCount;
            vh.AddVert(center, color, Vector2.zero);
            for (var i = 0; i < points.Count; i++)
                vh.AddVert(points[i], color, Vector2.zero);

            for (var i = 0; i < points.Count; i++)
            {
                var next = (i + 1) % points.Count;
                vh.AddTriangle(centerIndex, centerIndex + 1 + i, centerIndex + 1 + next);
            }
        }

        private void AddCorner(List<Vector2> points, Vector2 center, float radius, float startDeg, float endDeg)
        {
            var segments = Mathf.Max(2, _cornerSegments);
            for (var i = 0; i <= segments; i++)
            {
                var t = i / (float)segments;
                var angle = Mathf.Lerp(startDeg, endDeg, t) * Mathf.Deg2Rad;
                points.Add(new Vector2(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius));
            }
        }

        private void AddQuad(VertexHelper vh, Vector2 min, Vector2 max)
        {
            var start = vh.currentVertCount;
            vh.AddVert(new Vector2(min.x, min.y), color, Vector2.zero);
            vh.AddVert(new Vector2(min.x, max.y), color, Vector2.zero);
            vh.AddVert(new Vector2(max.x, max.y), color, Vector2.zero);
            vh.AddVert(new Vector2(max.x, min.y), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
}
