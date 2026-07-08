#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BazaarPlusPlus.JBSMatch.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.JBSMatch.Game.TournamentRoom;

internal sealed class TournamentRoomPanel : MonoBehaviour
{
    private const int DefaultRoomMaxPlayers = 30;
    private const int MaxChatMessages = 20;
    private const int PanelSortingOrder = 32760;
    private const float ButtonHeight = 64f;
    private const float TextFieldHeight = 50f;
    private const float DefaultPanelWidth = 1280f;
    private const float DefaultPanelHeight = 840f;
    private const float MinPanelWidth = 720f;
    private const float MinPanelHeight = 520f;
    private const float PanelEdgePadding = 18f;
    private const float ResizeHandleSize = 32f;
    private const float EdgeDockButtonWidth = 96f;
    private const float EdgeDockButtonHeight = 64f;
    private const float RoomListAutoRefreshInterval = 3f;
    private const string LogCategory = "TournamentPanel";

    private static readonly Color PanelBackground = Rgba(0.07f, 0.075f, 0.09f, 0.99f);
    private static readonly Color SectionBackground = Rgba(0.105f, 0.115f, 0.14f, 0.98f);
    private static readonly Color FrameBackground = Rgba(0.055f, 0.065f, 0.08f, 0.97f);
    private static readonly Color RowBackground = Rgba(0.12f, 0.13f, 0.15f, 0.98f);
    private static readonly Color RowSelectedBackground = Rgba(0.18f, 0.24f, 0.20f, 0.99f);
    private static readonly Color Border = Rgba(0.48f, 0.40f, 0.26f, 0.58f);
    private static readonly Color StrongBorder = Rgba(0.86f, 0.68f, 0.36f, 0.72f);
    private static readonly Color TitleText = Rgba(0.97f, 0.85f, 0.57f, 1f);
    private static readonly Color BodyText = Rgba(0.88f, 0.92f, 0.97f, 0.96f);
    private static readonly Color MutedText = Rgba(0.68f, 0.74f, 0.82f, 0.92f);
    private static readonly Color Accent = Rgba(0.46f, 0.70f, 0.92f, 0.96f);
    private static readonly Color WarmAccent = Rgba(0.96f, 0.70f, 0.28f, 0.96f);
    private static readonly Color ButtonBackground = Rgba(0.23f, 0.27f, 0.32f, 0.98f);
    private static readonly Color PrimaryButtonBackground = Rgba(0.19f, 0.31f, 0.39f, 0.98f);
    private static readonly Color GoodButtonBackground = Rgba(0.16f, 0.30f, 0.24f, 0.98f);
    private static readonly Color DangerButtonBackground = Rgba(0.40f, 0.20f, 0.19f, 0.98f);
    private static Font? _resolvedUiFont;

    private static TournamentRoomPanel? _instance;
    private static bool _applicationQuitting;

    public static bool IsVisible =>
        _instance != null && _instance._visible && _instance._panelObject != null;

    internal static bool IsVisibleFrom(Transform? preferredSource)
    {
        if (!IsVisible)
            return false;

        var preferredCanvas = ResolvePreferredCanvas(preferredSource);
        return preferredCanvas == null
            || _instance?._panelObject?.GetComponentInParent<Canvas>() == preferredCanvas;
    }

    private List<RoomEntry>? _pendingRooms;
    private bool _pendingRoomRefresh;
    private string _myName = "";

    private GameObject? _panelObject;
    private UIDocument? _document;
    private PanelSettings? _panelSettings;
    private VisualElement? _root;
    private VisualElement? _shell;
    private Button? _edgeDockButton;
    private Label? _headerTitle;
    private Label? _statusText;
    private VisualElement? _roomListView;
    private VisualElement? _roomListColumn;
    private ScrollView? _roomListScroll;
    private VisualElement? _roomListContainer;
    private Label? _emptyLabel;
    private TextField? _searchInput;
    private TextField? _manualRoomCodeInput;
    private VisualElement? _createRoomOverlay;
    private TextField? _roomCodeInput;
    private TextField? _roomNameInput;
    private TextField? _maxPlayersInput;
    private Label? _createDialogStatus;
    private Button? _limitPlayersButton;
    private Button? _autoStartButton;
    private Button? _chatToggleButton;
    private Button? _joinSelectedButton;
    private RoomRowView? _selectedRow;
    private string _searchQuery = string.Empty;
    private readonly List<RoomRowView> _roomRows = new();
    private readonly List<RoomEntry> _rooms = new();

    private VisualElement? _chatView;
    private Label? _chatRoomLabel;
    private Button? _copyRoomIdButton;
    private VisualElement? _playerListContainer;
    private Label? _playerCountLabel;
    private ScrollView? _chatScrollView;
    private VisualElement? _chatMsgContainer;
    private TextField? _chatInput;
    private readonly List<Label> _chatLabels = new();
    private readonly List<PlayerEntry> _players = new();
    private TournamentChatClient? _chatClient;
    private Button? _disbandRoomButton;
    private RoomEntry? _currentRoom;
    private int _currentPlayerCount;
    private int _currentMaxPlayers;
    private bool _currentPlayerIsHost;
    private int _joinAttemptId;
    private PendingRoomOpen? _pendingRoomOpen;
    private string? _pendingJoinFailure;
    private string? _pendingNativeCreatedRoomName;
    private bool _joinInFlight;
    private bool _visible;
    private bool _createLimitPlayers = true;
    private bool _createAutoStartOnFull;
    private bool _nativeCreateInFlight;
    private bool _roomListFetchInFlight;
    private float _nextRoomListAutoRefreshTime;
    private bool _panelBoundsInitialized;
    private bool _panelBoundsUserAdjusted;
    private bool _isEdgeDocked;
    private bool _isDraggingPanel;
    private bool _isResizingPanel;
    private int _activePointerId = -1;
    private Vector2 _pointerStart;
    private Vector2 _panelStartPosition;
    private Vector2 _panelStartSize;
    private float _panelLeft;
    private float _panelTop;
    private float _panelWidth = DefaultPanelWidth;
    private float _panelHeight = DefaultPanelHeight;

    internal static void OpenFromDockButton(Transform? preferredSource = null)
    {
        var preferredCanvas = ResolvePreferredCanvas(preferredSource);
        if (_instance != null
            && preferredCanvas != null
            && _instance._panelObject != null
            && _instance._panelObject.GetComponentInParent<Canvas>() != preferredCanvas
            && !_instance.HasActiveChatRoom())
        {
            JbsLog.Info(LogCategory, $"Recreating panel on active canvas '{preferredCanvas.name}'");
            Destroy(_instance._panelObject);
            Destroy(_instance.gameObject);
            _instance = null;
        }

        if (_instance == null)
            CreateInstance(preferredCanvas);

        if (_instance == null)
        {
            JbsLog.Warn(LogCategory, "Panel instance unavailable after create attempt");
            return;
        }

        _instance.SetVisible(true);
        _instance.BringToFront();
    }

    internal static void Close() => _instance?.SetVisible(false);

    internal static void LeaveForNativeLobbyCancelled()
    {
        if (_instance == null || _instance._currentRoom == null)
            return;

        _instance.LeaveRoomAndReturnToList("Native lobby cancel clicked");
    }

    internal static void HideForGameStarted()
    {
        if (_instance?._panelObject == null || !_instance._panelObject.activeSelf)
            return;

        _instance.SetVisible(false);
        JbsLog.Info(LogCategory, "Room selection panel hidden because game started.");
    }

    internal static void OpenNativeTournamentRoom(string roomCode, string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
            return;

        OpenFromDockButton();
        var pendingRoomName = _instance?._pendingNativeCreatedRoomName;
        if (_instance != null)
            _instance._pendingNativeCreatedRoomName = null;
        _instance?.BeginNativeTournamentRoom(
            roomCode.Trim(),
            string.IsNullOrWhiteSpace(pendingRoomName) ? roomName : pendingRoomName
        );
    }

    private static void CreateInstance(Canvas? preferredCanvas)
    {
        var canvas = preferredCanvas ?? FindBestCanvas();
        if (canvas == null)
        {
            JbsLog.Warn(LogCategory, "No Canvas found in scene");
            return;
        }

        var host = new GameObject("JBS_TournamentRoomPanelHost", typeof(RectTransform), typeof(Canvas));
        host.transform.SetParent(canvas.transform, worldPositionStays: false);
        var hostRect = host.GetComponent<RectTransform>();
        hostRect.anchorMin = Vector2.zero;
        hostRect.anchorMax = Vector2.one;
        hostRect.offsetMin = Vector2.zero;
        hostRect.offsetMax = Vector2.zero;
        hostRect.localScale = Vector3.one;

        var hostCanvas = host.GetComponent<Canvas>();
        hostCanvas.overrideSorting = true;
        hostCanvas.sortingOrder = Math.Max(canvas.sortingOrder + 5000, PanelSortingOrder);

        _instance = host.AddComponent<TournamentRoomPanel>();
        _instance.Initialize(hostRect);
    }

    private static Canvas? ResolvePreferredCanvas(Transform? preferredSource)
    {
        if (preferredSource == null)
            return null;

        var canvas = preferredSource.GetComponentInParent<Canvas>(includeInactive: true);
        return canvas != null && canvas.gameObject.activeInHierarchy ? canvas : null;
    }

    private static Canvas? FindBestCanvas()
    {
        var tournamentCanvas = FindTournamentCanvas();
        if (tournamentCanvas != null)
            return tournamentCanvas;

        Canvas? best = null;
        var bestScore = int.MinValue;
        foreach (var canvas in FindObjectsOfType<Canvas>(includeInactive: true))
        {
            if (canvas == null || !canvas.gameObject.activeInHierarchy)
                continue;

            var score = canvas.sortingOrder + (canvas.isRootCanvas ? 100 : 0);
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                score += 20;
            if (canvas.name.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 10;

            if (score > bestScore)
            {
                bestScore = score;
                best = canvas;
            }
        }

        return best ?? FindObjectOfType<Canvas>();
    }

    private static Canvas? FindTournamentCanvas()
    {
        TextMeshProUGUI? bestText = null;
        var bestScore = float.MinValue;
        foreach (var text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (text == null || !text.gameObject.activeInHierarchy)
                continue;

            var value = (text.text ?? string.Empty).Trim();
            if (!value.Contains("锦标赛", StringComparison.Ordinal))
                continue;

            var canvas = text.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.gameObject.activeInHierarchy)
                continue;

            var score = text.fontSize + canvas.sortingOrder;
            if (string.Equals(value, "锦标赛", StringComparison.Ordinal))
                score += 1000f;

            if (score > bestScore)
            {
                bestScore = score;
                bestText = text;
            }
        }

        return bestText != null ? bestText.GetComponentInParent<Canvas>() : null;
    }

    private void Initialize(RectTransform hostRect)
    {
        try
        {
            BuildUi(hostRect);
            SetVisible(false);
        }
        catch (Exception ex)
        {
            JbsLog.Error(LogCategory, "Failed to build panel UI", ex);
        }
    }

    private void OnEnable() => _instance ??= this;

    private void OnApplicationQuit()
    {
        _applicationQuitting = true;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
        if (_panelSettings != null)
            Destroy(_panelSettings);
        if (!_applicationQuitting)
            return;

        BppTournamentRoomBridgeClient.LeaveRoom();
        var client = _chatClient;
        _chatClient = null;
        if (client != null)
            _ = client.LeaveRoomAsync(JbsConfig.ServerWsUrl).ContinueWith(_ => client.Dispose());
    }

    private void Update()
    {
        if (_pendingRoomOpen != null)
        {
            var pending = _pendingRoomOpen;
            _pendingRoomOpen = null;
            if (pending.AttemptId == _joinAttemptId)
            {
                _joinInFlight = false;
                ShowConnectedChatView(pending.Room, pending.IsHost);
            }
        }

        if (!string.IsNullOrEmpty(_pendingJoinFailure))
        {
            _joinInFlight = false;
            SetStatus(_pendingJoinFailure);
            _pendingJoinFailure = null;
            ShowRoomListView();
            RefreshRoomList();
        }

        if (_chatClient != null)
            while (_chatClient.TryDequeue(out var msg))
                HandleIncomingMessage(msg);

        if (_pendingRoomRefresh && _pendingRooms != null)
        {
            _pendingRoomRefresh = false;
            var rooms = _pendingRooms;
            _pendingRooms = null;
            _rooms.Clear();
            _rooms.AddRange(rooms);
            var filtered = GetFilteredRooms();
            SetStatus(string.IsNullOrEmpty(_searchQuery)
                ? JbsLocalization.Get("tournament.status.room_count", _rooms.Count)
                : JbsLocalization.Get("tournament.status.search_result", filtered.Count, _rooms.Count));
            RebuildRoomRows(filtered);
        }

        if (_visible
            && _roomListView?.resolvedStyle.display != DisplayStyle.None
            && !_roomListFetchInFlight
            && Time.unscaledTime >= _nextRoomListAutoRefreshTime)
        {
            RefreshRoomList();
        }
    }

    private void SetVisible(bool visible)
    {
        if (_panelObject == null)
            return;

        _visible = visible;
        _root?.SetDisplay(visible);
        if (visible)
        {
            JbsLog.Info(LogCategory, "Showing UI Toolkit tournament room panel.");
            BringToFront();
            ExitEdgeDockState();
            if (!_panelBoundsUserAdjusted)
                _panelBoundsInitialized = false;
            EnsurePanelBounds();
            HideCreateRoomOverlay();
            if (HasActiveChatRoom())
            {
                ShowCurrentChatView();
            }
            else
            {
                ShowRoomListView();
                RefreshRoomList();
            }
        }
        else
        {
            _joinAttemptId++;
            _pendingRoomOpen = null;
            _pendingJoinFailure = null;
            _joinInFlight = false;
            ExitEdgeDockState();
        }
    }

    private void BringToFront()
    {
        if (_panelObject == null)
            return;

        _panelObject.transform.SetAsLastSibling();
        if (gameObject.TryGetComponent<Canvas>(out var canvas))
            canvas.sortingOrder = Math.Max(canvas.sortingOrder, PanelSortingOrder);
        transform.SetAsLastSibling();
    }

    private void ShowRoomListView()
    {
        _roomListView?.SetDisplay(true);
        _chatView?.SetDisplay(false);
        if (_headerTitle != null)
            _headerTitle.text = JbsLocalization.Get("tournament.title");
        _nextRoomListAutoRefreshTime = 0f;
        RefreshChatToggleButtonLabel();
    }

    private void ShowConnectedChatView(RoomEntry room, bool isHost)
    {
        _currentRoom = room;
        _currentPlayerCount = room.CurrentPlayers;
        _currentMaxPlayers = room.MaxPlayers;
        _currentPlayerIsHost = isHost;
        _roomListView?.SetDisplay(false);
        _chatView?.SetDisplay(true);
        if (_headerTitle != null)
            _headerTitle.text = JbsLocalization.Get("tournament.title");
        RefreshHostControls();
        RefreshChatToggleButtonLabel();
        RefreshChatRoomHeader();
        ClearChatMessages();

        _players.Clear();
        if (!string.IsNullOrWhiteSpace(room.HostName))
            _players.Add(PlayerEntry.Synthetic($"host:{room.HostName}", room.HostName));
        if (!string.Equals(room.HostName, _myName, StringComparison.Ordinal))
            _players.Add(PlayerEntry.Synthetic($"local:{_myName}", _myName));
        RebuildPlayerList();

        BppTournamentRoomBridgeClient.EnterRoom(room.Id, room.Name);
        JbsLog.Info(LogCategory, $"Chat connected: room={room.Id}");
    }

    private void ShowCurrentChatView()
    {
        _roomListView?.SetDisplay(false);
        _chatView?.SetDisplay(true);
        if (_headerTitle != null)
            _headerTitle.text = JbsLocalization.Get("tournament.title");
        RefreshHostControls();
        RefreshChatToggleButtonLabel();
        RefreshChatRoomHeader();
        RebuildPlayerList();
    }

    private bool HasActiveChatRoom() =>
        _currentRoom != null && _chatClient?.IsConnected == true;

    private void BeginJoinRoom(RoomEntry room)
    {
        if (_joinInFlight)
        {
            SetStatus(JbsLocalization.Get("tournament.status.connecting", room.Id));
            return;
        }

        _myName = GetPlayerName();
        var attemptId = ++_joinAttemptId;
        _pendingRoomOpen = null;
        _pendingJoinFailure = null;
        _joinInFlight = true;
        SetStatus(JbsLocalization.Get("tournament.status.connecting", room.Id));
        _ = JoinChatAsync(room, attemptId);
    }

    private void BeginNativeTournamentRoom(string roomCode, string roomName)
    {
        var normalizedCode = NormalizeRoomCode(roomCode);
        if (!IsValidRoomCode(normalizedCode))
        {
            JbsLog.Warn(LogCategory, $"Native tournament room code invalid: {roomCode}");
            return;
        }

        if (_currentRoom != null
            && string.Equals(_currentRoom.Id, normalizedCode, StringComparison.OrdinalIgnoreCase)
            && _chatClient?.IsConnected == true)
        {
            JbsLog.Info(LogCategory, $"Native tournament room already connected: {normalizedCode}");
            return;
        }

        var normalizedName = string.IsNullOrWhiteSpace(roomName) ? normalizedCode : roomName.Trim();
        var room = new RoomEntry(
            normalizedCode,
            normalizedName,
            GetPlayerName(),
            "",
            1,
            DefaultRoomMaxPlayers
        );

        JbsLog.Info(LogCategory, $"Opening native tournament chat room: {room.Name} ({room.Id})");
        BeginJoinRoom(room);
    }

    private async System.Threading.Tasks.Task JoinChatAsync(RoomEntry room, int attemptId)
    {
        _chatClient ??= new TournamentChatClient();
        try
        {
            if (await _chatClient.ConnectAsync(
                    JbsConfig.ServerWsUrl,
                    room.Id,
                    _myName,
                    room.Name,
                    room.LimitPlayers,
                    room.MaxPlayers,
                    room.AutoStartOnFull))
            {
                _pendingRoomOpen = new PendingRoomOpen(room, _chatClient.IsCurrentPlayerHost, attemptId);
                return;
            }

            _pendingJoinFailure = DrainChatClientSystemMessages()
                ?? JbsLocalization.Get("tournament.error.join_failed", JbsLocalization.Get("tournament.error.unknown"));
        }
        catch (Exception ex)
        {
            _pendingJoinFailure = JbsLocalization.Get("tournament.error.connect_failed", ex.Message);
            JbsLog.Error(LogCategory, "Chat connect failed", ex);
        }
    }

    private void BuildUi(RectTransform? hostRect)
    {
        if (hostRect == null)
            return;

        _panelObject = new GameObject("JBS_TournamentRoomPanelUiToolkitRoot", typeof(RectTransform));
        _panelObject.transform.SetParent(hostRect, worldPositionStays: false);
        var panelRect = _panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.sortingOrder = PanelSortingOrder;
        _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        _panelSettings.match = 1f;
        _panelSettings.clearColor = false;
        _panelSettings.targetDisplay = 0;

        _document = _panelObject.AddComponent<UIDocument>();
        _document.panelSettings = _panelSettings;
        _root = _document.rootVisualElement;
        _root.style.flexGrow = 1f;
        _root.style.position = Position.Absolute;
        _root.style.left = 0f;
        _root.style.right = 0f;
        _root.style.top = 0f;
        _root.style.bottom = 0f;
        _root.style.backgroundColor = Rgba(0f, 0f, 0f, 0f);
        _root.style.display = DisplayStyle.None;
        _root.style.color = BodyText;
        var uiFont = ResolveUiFont();
        if (uiFont != null)
            _root.style.unityFont = uiFont;
        _root.pickingMode = PickingMode.Ignore;

        BuildTree(_root);
        JbsLog.Info(LogCategory, "Built UI Toolkit tournament room panel.");
    }

    private void BuildTree(VisualElement root)
    {
        var shell = new VisualElement();
        _shell = shell;
        shell.pickingMode = PickingMode.Position;
        shell.style.position = Position.Absolute;
        shell.style.width = DefaultPanelWidth;
        shell.style.height = DefaultPanelHeight;
        shell.style.left = PanelEdgePadding;
        shell.style.top = PanelEdgePadding;
        shell.style.paddingLeft = 28f;
        shell.style.paddingRight = 28f;
        shell.style.paddingTop = 24f;
        shell.style.paddingBottom = 24f;
        shell.style.flexDirection = FlexDirection.Column;
        shell.style.backgroundColor = PanelBackground;
        ApplyBorder(shell, StrongBorder, 2f);
        ApplyRadius(shell, 6f);
        root.Add(shell);

        var header = new VisualElement();
        header.style.height = 64f;
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.marginBottom = 16f;
        header.style.paddingLeft = 16f;
        header.style.paddingRight = 12f;
        header.style.backgroundColor = Rgba(0.13f, 0.10f, 0.07f, 0.92f);
        ApplyBorder(header, Rgba(0.78f, 0.58f, 0.28f, 0.42f));
        ApplyRadius(header, 4f);
        header.pickingMode = PickingMode.Position;
        shell.Add(header);
        RegisterPanelDrag(header);

        var titleBlock = new VisualElement();
        titleBlock.style.flexGrow = 1f;
        titleBlock.style.minWidth = 0f;
        header.Add(titleBlock);

        _headerTitle = LabelText(JbsLocalization.Get("tournament.title"), 28, TitleText, FontStyle.Bold);
        titleBlock.Add(_headerTitle);
        var subtitle = LabelText(JbsLocalization.Get("tournament.status.loading"), 13, MutedText);
        subtitle.text = JbsLocalization.Get("tournament.manual.code_placeholder");
        titleBlock.Add(subtitle);

        var refreshButton = CreateButton(JbsLocalization.Get("tournament.button.refresh"), RefreshRoomList, PrimaryButtonBackground);
        refreshButton.style.width = 168f;
        header.Add(refreshButton);

        var minimizeButton = CreateButton("—", MinimizeToEdge, ButtonBackground);
        minimizeButton.style.width = 68f;
        minimizeButton.style.marginLeft = 8f;
        header.Add(minimizeButton);

        var closeButton = CreateButton("X", Close, DangerButtonBackground);
        closeButton.style.width = 68f;
        closeButton.style.marginLeft = 8f;
        header.Add(closeButton);

        var body = new VisualElement();
        body.style.flexGrow = 1f;
        body.style.minHeight = 0f;
        body.style.flexDirection = FlexDirection.Column;
        body.style.paddingLeft = 14f;
        body.style.paddingRight = 14f;
        body.style.paddingTop = 14f;
        body.style.paddingBottom = 14f;
        body.style.backgroundColor = SectionBackground;
        ApplyBorder(body, Rgba(0.30f, 0.24f, 0.16f, 0.74f));
        ApplyRadius(body, 4f);
        shell.Add(body);

        _roomListView = new VisualElement();
        _roomListView.style.flexGrow = 1f;
        _roomListView.style.minWidth = 0f;
        _roomListView.style.minHeight = 0f;
        _roomListView.style.flexDirection = FlexDirection.Column;
        body.Add(_roomListView);

        BuildRoomListView(_roomListView);

        _chatView = new VisualElement();
        _chatView.style.flexGrow = 1f;
        _chatView.style.minWidth = 0f;
        _chatView.style.minHeight = 0f;
        _chatView.style.flexDirection = FlexDirection.Column;
        _chatView.SetDisplay(false);
        body.Add(_chatView);
        BuildChatView(_chatView);

        BuildEdgeDockButton(root);

        var resizeHandle = new VisualElement();
        resizeHandle.style.position = Position.Absolute;
        resizeHandle.style.width = ResizeHandleSize;
        resizeHandle.style.height = ResizeHandleSize;
        resizeHandle.style.right = 0f;
        resizeHandle.style.bottom = 0f;
        resizeHandle.style.backgroundColor = Rgba(0.90f, 0.76f, 0.42f, 0.28f);
        resizeHandle.pickingMode = PickingMode.Position;
        shell.Add(resizeHandle);
        AddResizeGripLines(resizeHandle);
        RegisterPanelResize(resizeHandle);

        BuildCreateRoomOverlay(shell);

        root.RegisterCallback<GeometryChangedEvent>(_ => EnsurePanelBounds());
    }

    private static void AddResizeGripLines(VisualElement handle)
    {
        for (var i = 0; i < 3; i++)
        {
            var line = new VisualElement();
            line.style.position = Position.Absolute;
            line.style.width = 16f + i * 5f;
            line.style.height = 2f;
            line.style.right = 5f;
            line.style.bottom = 7f + i * 7f;
            line.style.backgroundColor = Rgba(0.98f, 0.88f, 0.58f, 0.82f);
            line.style.rotate = new Rotate(new Angle(135f));
            line.pickingMode = PickingMode.Ignore;
            handle.Add(line);
        }
    }

    private void BuildEdgeDockButton(VisualElement root)
    {
        _edgeDockButton = CreateButton("聊天", RestoreFromEdgeDock, PrimaryButtonBackground);
        _edgeDockButton.style.position = Position.Absolute;
        _edgeDockButton.style.width = EdgeDockButtonWidth;
        _edgeDockButton.style.height = EdgeDockButtonHeight;
        _edgeDockButton.style.right = 0f;
        _edgeDockButton.style.top = 240f;
        _edgeDockButton.style.borderTopRightRadius = 0f;
        _edgeDockButton.style.borderBottomRightRadius = 0f;
        _edgeDockButton.SetDisplay(false);
        root.Add(_edgeDockButton);
    }

    private void BuildRoomListView(VisualElement parent)
    {
        var tools = new VisualElement();
        tools.style.flexDirection = FlexDirection.Column;
        tools.style.marginBottom = 12f;
        parent.Add(tools);

        _searchInput = CreateTextField(JbsLocalization.Get("tournament.search.placeholder"));
        _searchInput.RegisterValueChangedCallback(evt => OnSearchChanged(evt.newValue));
        tools.Add(_searchInput);

        var joinTools = new VisualElement();
        joinTools.style.flexDirection = FlexDirection.Row;
        joinTools.style.flexWrap = Wrap.Wrap;
        joinTools.style.alignItems = Align.Center;
        joinTools.style.marginTop = 10f;
        tools.Add(joinTools);

        _manualRoomCodeInput = CreateTextField(JbsLocalization.Get("tournament.manual.code_placeholder"));
        _manualRoomCodeInput.style.flexGrow = 1f;
        _manualRoomCodeInput.style.minWidth = 220f;
        _manualRoomCodeInput.maxLength = 6;
        _manualRoomCodeInput.RegisterValueChangedCallback(evt =>
        {
            if (_manualRoomCodeInput == null)
                return;
            var normalized = NormalizeRoomCode(evt.newValue);
            if (!string.Equals(evt.newValue, normalized, StringComparison.Ordinal))
                _manualRoomCodeInput.SetValueWithoutNotify(normalized);
        });
        joinTools.Add(_manualRoomCodeInput);

        var manualJoin = CreateButton(JbsLocalization.Get("tournament.button.join_by_code"), OnManualJoinRoom, PrimaryButtonBackground);
        manualJoin.style.width = 180f;
        manualJoin.style.marginLeft = 10f;
        joinTools.Add(manualJoin);

        var create = CreateButton(JbsLocalization.Get("tournament.button.create_room"), OnCreateRoomClicked, GoodButtonBackground);
        create.style.width = 190f;
        create.style.marginLeft = 10f;
        joinTools.Add(create);

        var statusFrame = new VisualElement();
        statusFrame.style.minHeight = 44f;
        statusFrame.style.marginBottom = 12f;
        statusFrame.style.paddingLeft = 12f;
        statusFrame.style.paddingRight = 12f;
        statusFrame.style.justifyContent = Justify.Center;
        statusFrame.style.backgroundColor = Rgba(0.12f, 0.105f, 0.08f, 0.88f);
        ApplyBorder(statusFrame, Rgba(0.70f, 0.52f, 0.25f, 0.46f));
        ApplyRadius(statusFrame, 4f);
        parent.Add(statusFrame);
        _statusText = LabelText(JbsLocalization.Get("tournament.status.loading"), 14, BodyText);
        statusFrame.Add(_statusText);

        _roomListColumn = new VisualElement();
        _roomListColumn.style.flexGrow = 1f;
        _roomListColumn.style.minHeight = 0f;
        _roomListColumn.style.backgroundColor = FrameBackground;
        ApplyBorder(_roomListColumn, Border, 2f);
        ApplyRadius(_roomListColumn, 4f);
        parent.Add(_roomListColumn);

        _roomListScroll = new ScrollView(ScrollViewMode.Vertical);
        _roomListScroll.style.flexGrow = 1f;
        _roomListScroll.style.minHeight = 0f;
        _roomListScroll.mouseWheelScrollSize = 120f;
        _roomListColumn.Add(_roomListScroll);

        _roomListContainer = new VisualElement();
        _roomListContainer.style.paddingLeft = 8f;
        _roomListContainer.style.paddingRight = 8f;
        _roomListContainer.style.paddingTop = 8f;
        _roomListContainer.style.paddingBottom = 8f;
        _roomListScroll.Add(_roomListContainer);

        _emptyLabel = LabelText(JbsLocalization.Get("tournament.room.empty"), 16, MutedText);
        _emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _emptyLabel.style.paddingTop = 120f;
        _emptyLabel.SetDisplay(false);
        _roomListContainer.Add(_emptyLabel);

        var footer = new VisualElement();
        footer.style.minHeight = ButtonHeight;
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.alignItems = Align.Center;
        footer.style.justifyContent = Justify.FlexEnd;
        footer.style.marginTop = 10f;
        parent.Add(footer);

        _joinSelectedButton = CreateButton(JbsLocalization.Get("tournament.button.join_inline"), OnJoinRoomClicked, PrimaryButtonBackground);
        _joinSelectedButton.style.width = 180f;
        footer.Add(_joinSelectedButton);

        _chatToggleButton = CreateButton(JbsLocalization.Get("tournament.button.open_chat"), OnToggleChatClicked, ButtonBackground);
        _chatToggleButton.style.width = 180f;
        _chatToggleButton.style.marginLeft = 10f;
        footer.Add(_chatToggleButton);
    }

    private void BuildChatView(VisualElement parent)
    {
        parent.style.backgroundColor = SectionBackground;
        parent.style.paddingLeft = 14f;
        parent.style.paddingRight = 14f;
        parent.style.paddingTop = 14f;
        parent.style.paddingBottom = 14f;
        ApplyBorder(parent, Border, 2f);
        ApplyRadius(parent, 4f);

        var chatHeader = new VisualElement();
        chatHeader.style.flexDirection = FlexDirection.Row;
        chatHeader.style.alignItems = Align.Center;
        chatHeader.style.marginBottom = 12f;
        parent.Add(chatHeader);

        _chatRoomLabel = LabelText("", 17, BodyText, FontStyle.Bold);
        _chatRoomLabel.style.flexGrow = 1f;
        _chatRoomLabel.style.whiteSpace = WhiteSpace.Normal;
        chatHeader.Add(_chatRoomLabel);

        _copyRoomIdButton = CreateButton(JbsLocalization.Get("tournament.button.copy_id"), OnCopyRoomId, ButtonBackground);
        _copyRoomIdButton.style.width = 150f;
        chatHeader.Add(_copyRoomIdButton);

        var createButton = CreateButton(JbsLocalization.Get("tournament.button.create_room"), OnCreateRoomClicked, GoodButtonBackground);
        createButton.style.width = 170f;
        createButton.style.marginLeft = 8f;
        chatHeader.Add(createButton);

        _disbandRoomButton = CreateButton(JbsLocalization.Get("tournament.button.disband_room"), OnDisbandRoom, DangerButtonBackground);
        _disbandRoomButton.style.width = 150f;
        _disbandRoomButton.style.marginLeft = 8f;
        chatHeader.Add(_disbandRoomButton);

        var chatBody = new VisualElement();
        chatBody.style.flexGrow = 1f;
        chatBody.style.minHeight = 0f;
        chatBody.style.flexDirection = FlexDirection.Row;
        parent.Add(chatBody);

        var messagesFrame = new VisualElement();
        messagesFrame.style.flexGrow = 1f;
        messagesFrame.style.minWidth = 0f;
        messagesFrame.style.minHeight = 0f;
        messagesFrame.style.backgroundColor = FrameBackground;
        ApplyBorder(messagesFrame, Border, 2f);
        ApplyRadius(messagesFrame, 4f);
        chatBody.Add(messagesFrame);

        _chatScrollView = new ScrollView(ScrollViewMode.Vertical);
        _chatScrollView.style.flexGrow = 1f;
        _chatScrollView.style.minHeight = 0f;
        _chatScrollView.mouseWheelScrollSize = 80f;
        messagesFrame.Add(_chatScrollView);

        _chatMsgContainer = new VisualElement();
        _chatMsgContainer.style.paddingLeft = 10f;
        _chatMsgContainer.style.paddingRight = 10f;
        _chatMsgContainer.style.paddingTop = 10f;
        _chatMsgContainer.style.paddingBottom = 10f;
        _chatScrollView.Add(_chatMsgContainer);

        var playerFrame = new VisualElement();
        playerFrame.style.width = 200f;
        playerFrame.style.flexShrink = 0f;
        playerFrame.style.minHeight = 0f;
        playerFrame.style.marginLeft = 12f;
        playerFrame.style.backgroundColor = FrameBackground;
        ApplyBorder(playerFrame, Border, 2f);
        ApplyRadius(playerFrame, 4f);
        chatBody.Add(playerFrame);

        _playerCountLabel = LabelText(JbsLocalization.Get("tournament.chat.player_count", 0), 13, WarmAccent, FontStyle.Bold);
        _playerCountLabel.style.paddingLeft = 10f;
        _playerCountLabel.style.paddingTop = 10f;
        _playerCountLabel.style.paddingBottom = 8f;
        playerFrame.Add(_playerCountLabel);

        _playerListContainer = new VisualElement();
        _playerListContainer.style.paddingLeft = 10f;
        _playerListContainer.style.paddingRight = 10f;
        _playerListContainer.style.paddingBottom = 10f;
        playerFrame.Add(_playerListContainer);

        var inputRow = new VisualElement();
        inputRow.style.minHeight = ButtonHeight;
        inputRow.style.flexDirection = FlexDirection.Row;
        inputRow.style.alignItems = Align.Center;
        inputRow.style.marginTop = 12f;
        parent.Add(inputRow);

        _chatInput = CreateTextField(JbsLocalization.Get("tournament.chat.input_placeholder"));
        _chatInput.style.flexGrow = 1f;
        _chatInput.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                return;
            OnChatSend();
            evt.StopPropagation();
        });
        inputRow.Add(_chatInput);

        var send = CreateButton(JbsLocalization.Get("tournament.button.send"), OnChatSend, PrimaryButtonBackground);
        send.style.width = 142f;
        send.style.marginLeft = 10f;
        inputRow.Add(send);

        var leave = CreateButton(JbsLocalization.Get("tournament.button.leave_room"), OnLeaveRoom, DangerButtonBackground);
        leave.style.width = 142f;
        leave.style.marginLeft = 10f;
        inputRow.Add(leave);
    }

    private void BuildCreateRoomOverlay(VisualElement root)
    {
        _createRoomOverlay = new VisualElement();
        _createRoomOverlay.style.position = Position.Absolute;
        _createRoomOverlay.style.left = 0f;
        _createRoomOverlay.style.right = 0f;
        _createRoomOverlay.style.top = 0f;
        _createRoomOverlay.style.bottom = 0f;
        _createRoomOverlay.style.alignItems = Align.Center;
        _createRoomOverlay.style.justifyContent = Justify.Center;
        _createRoomOverlay.style.backgroundColor = Rgba(0f, 0f, 0f, 0.58f);
        _createRoomOverlay.pickingMode = PickingMode.Position;
        _createRoomOverlay.SetDisplay(false);
        root.Add(_createRoomOverlay);

        var dialog = new VisualElement();
        dialog.pickingMode = PickingMode.Position;
        dialog.style.width = 620f;
        dialog.style.paddingLeft = 24f;
        dialog.style.paddingRight = 24f;
        dialog.style.paddingTop = 22f;
        dialog.style.paddingBottom = 22f;
        dialog.style.backgroundColor = SectionBackground;
        ApplyBorder(dialog, Border);
        _createRoomOverlay.Add(dialog);

        dialog.Add(LabelText(JbsLocalization.Get("tournament.button.create_room"), 24, TitleText, FontStyle.Bold));

        _roomNameInput = CreateTextField(JbsLocalization.Get("tournament.dialog.room_name_placeholder"));
        _roomNameInput.style.marginTop = 18f;
        _roomNameInput.maxLength = 20;
        dialog.Add(_roomNameInput);

        _roomCodeInput = CreateTextField(JbsLocalization.Get("tournament.dialog.room_code_placeholder"));
        _roomCodeInput.style.marginTop = 10f;
        _roomCodeInput.maxLength = 6;
        _roomCodeInput.SetDisplay(false);
        _roomCodeInput.RegisterValueChangedCallback(evt =>
        {
            if (_roomCodeInput == null)
                return;
            var normalized = NormalizeRoomCode(evt.newValue);
            if (!string.Equals(evt.newValue, normalized, StringComparison.Ordinal))
                _roomCodeInput.SetValueWithoutNotify(normalized);
        });
        dialog.Add(_roomCodeInput);

        _createDialogStatus = LabelText("", 13, WarmAccent, FontStyle.Bold);
        _createDialogStatus.style.marginTop = 8f;
        _createDialogStatus.style.whiteSpace = WhiteSpace.Normal;
        _createDialogStatus.SetDisplay(false);
        dialog.Add(_createDialogStatus);

        var options = new VisualElement();
        options.style.flexDirection = FlexDirection.Row;
        options.style.marginTop = 10f;
        dialog.Add(options);

        _limitPlayersButton = CreateButton("", ToggleCreateLimitPlayers, ButtonBackground);
        _limitPlayersButton.style.flexGrow = 1f;
        options.Add(_limitPlayersButton);

        _autoStartButton = CreateButton("", ToggleCreateAutoStartOnFull, ButtonBackground);
        _autoStartButton.style.flexGrow = 1f;
        _autoStartButton.style.marginLeft = 10f;
        options.Add(_autoStartButton);

        _maxPlayersInput = CreateTextField(JbsLocalization.Get("tournament.dialog.max_players_placeholder"));
        _maxPlayersInput.style.marginTop = 10f;
        _maxPlayersInput.maxLength = 2;
        dialog.Add(_maxPlayersInput);

        var buttons = new VisualElement();
        buttons.style.minHeight = ButtonHeight;
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.style.marginTop = 18f;
        dialog.Add(buttons);

        var confirm = CreateButton(JbsLocalization.Get("tournament.button.confirm_create"), OnConfirmCreateRoom, GoodButtonBackground);
        confirm.style.flexGrow = 1f;
        buttons.Add(confirm);

        var cancel = CreateButton(JbsLocalization.Get("tournament.button.cancel"), OnCancelCreateRoom, DangerButtonBackground);
        cancel.style.flexGrow = 1f;
        cancel.style.marginLeft = 10f;
        buttons.Add(cancel);
    }

    private void RebuildPlayerList()
    {
        if (_playerListContainer == null)
            return;

        _playerListContainer.Clear();
        for (var i = 0; i < _players.Count; i++)
        {
            var player = _players[i];
            var isSelf = player.Name == _myName;
            var label = LabelText(
                isSelf ? JbsLocalization.Get("tournament.chat.player_self", player.Name) : player.Name,
                13,
                isSelf ? TitleText : BodyText,
                i == 0 ? FontStyle.Bold : FontStyle.Normal
            );
            label.style.marginBottom = 7f;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            _playerListContainer.Add(label);
        }

        if (_playerCountLabel != null)
            _playerCountLabel.text = JbsLocalization.Get("tournament.chat.player_count", GetDisplayedPlayerCount());
    }

    private void AddOrUpdatePlayer(string? userId, string name)
    {
        var key = PlayerEntry.MakeKey(userId, name);
        for (var i = 0; i < _players.Count; i++)
        {
            if (_players[i].Key == key)
            {
                _players[i] = new PlayerEntry(key, userId, name);
                RebuildPlayerList();
                return;
            }
        }

        if (!string.IsNullOrEmpty(userId))
            _players.RemoveAll(p => p.UserId == null && p.Name == name);

        _players.Add(new PlayerEntry(key, userId, name));
        RebuildPlayerList();
    }

    private void ReplacePlayers(IReadOnlyList<ChatPlayer> players)
    {
        if (players.Count == 0)
            return;

        _players.Clear();
        foreach (var player in players)
        {
            if (string.IsNullOrWhiteSpace(player.Name))
                continue;

            var key = PlayerEntry.MakeKey(player.UserId, player.Name);
            _players.Add(new PlayerEntry(key, player.UserId, player.Name));
        }
        RebuildPlayerList();
    }

    private void RemovePlayer(string? userId, string name)
    {
        var key = PlayerEntry.MakeKey(userId, name);
        var removed = _players.RemoveAll(p => p.Key == key) > 0;
        if (!removed && string.IsNullOrEmpty(userId))
            removed = _players.RemoveAll(p => p.Name == name) > 0;

        if (removed)
            RebuildPlayerList();
    }

    private int GetDisplayedPlayerCount() =>
        _currentPlayerCount > 0 ? _currentPlayerCount : _players.Count;

    private void UpdatePlayerCountFromMessage(ChatMessage msg)
    {
        if (msg.PlayerCount >= 0)
            _currentPlayerCount = msg.PlayerCount;
        if (msg.MaxPlayers > 0)
            _currentMaxPlayers = msg.MaxPlayers;

        RefreshChatRoomHeader();
        if (_playerCountLabel != null)
            _playerCountLabel.text = JbsLocalization.Get("tournament.chat.player_count", GetDisplayedPlayerCount());

        _chatClient?.ReportRoomCount(JbsConfig.ServerWsUrl, GetDisplayedPlayerCount());
    }

    private void RefreshChatRoomHeader()
    {
        if (_chatRoomLabel == null || _currentRoom == null)
            return;

        var count = GetDisplayedPlayerCount();
        var max = _currentMaxPlayers > 0 ? _currentMaxPlayers : _currentRoom.MaxPlayers;
        _chatRoomLabel.text = JbsLocalization.Get("tournament.chat.room_info", _currentRoom.Name, count, max)
            + "\n"
            + JbsLocalization.Get("tournament.chat.room_code", _currentRoom.Id);
    }

    private void RefreshHostControls()
    {
        _disbandRoomButton?.SetDisplay(_currentPlayerIsHost);
    }

    private void HandleIncomingMessage(ChatMessage msg)
    {
        if (msg.IsRoomDisbanded)
        {
            AppendChatMessage(msg);
            OnRoomDisbandedByHost();
            return;
        }

        if (msg.IsSystem && msg.EventPlayerName != null)
        {
            if (msg.Players.Count > 0)
                ReplacePlayers(msg.Players);
            else if (msg.IsJoinEvent)
                AddOrUpdatePlayer(msg.EventPlayerId, msg.EventPlayerName);
            else
                RemovePlayer(msg.EventPlayerId, msg.EventPlayerName);
        }
        UpdatePlayerCountFromMessage(msg);
        AppendChatMessage(msg);
    }

    private void AppendChatMessage(ChatMessage msg)
    {
        if (_chatMsgContainer == null)
            return;

        if (_chatLabels.Count >= MaxChatMessages)
        {
            var oldest = _chatLabels[0];
            _chatLabels.RemoveAt(0);
            oldest.RemoveFromHierarchy();
        }

        Color color;
        string text;
        FontStyle style;
        if (msg.IsSystem)
        {
            color = MutedText;
            text = JbsLocalization.Get("tournament.chat.system_prefix", msg.Text);
            style = FontStyle.Italic;
        }
        else if (msg.From == _myName)
        {
            color = TitleText;
            text = $"{_myName}: {msg.Text}";
            style = FontStyle.Normal;
        }
        else
        {
            color = BodyText;
            text = $"{msg.From}: {msg.Text}";
            style = FontStyle.Normal;
        }

        var label = LabelText(text, 14, color, style);
        label.style.marginBottom = 8f;
        label.style.whiteSpace = WhiteSpace.Normal;
        _chatMsgContainer.Add(label);
        _chatLabels.Add(label);
        _chatScrollView?.ScrollTo(label);
    }

    private void ClearChatMessages()
    {
        foreach (var label in _chatLabels)
            label.RemoveFromHierarchy();
        _chatLabels.Clear();
        _chatMsgContainer?.Clear();
    }

    private void RefreshRoomList()
    {
        if (_roomListFetchInFlight)
            return;

        _roomListFetchInFlight = true;
        _nextRoomListAutoRefreshTime = Time.unscaledTime + RoomListAutoRefreshInterval;
        _selectedRow = null;
        SetStatus(JbsLocalization.Get("tournament.status.loading"));
        _ = FetchRoomsAsync();
    }

    private async System.Threading.Tasks.Task FetchRoomsAsync()
    {
        var httpBase = JbsConfig.ServerWsUrl.TrimEnd('/')
            .Replace("wss://", "https://")
            .Replace("ws://", "http://");
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await http.GetStringAsync($"{httpBase}/rooms").ConfigureAwait(false);
            var rooms = ParseRoomsJson(json);
            JbsLog.Info(LogCategory, $"Fetched {rooms.Count} rooms from server");
            _pendingRooms = rooms;
            _pendingRoomRefresh = true;
        }
        catch (Exception ex)
        {
            JbsLog.Warn(LogCategory, $"Failed to fetch rooms: {ex.Message}");
            _pendingRooms = new List<RoomEntry>();
            _pendingRoomRefresh = true;
        }
        finally
        {
            _roomListFetchInFlight = false;
        }
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

            var code = JbsJson.String(obj, "code") ?? "";
            var name = JbsJson.String(obj, "name") ?? code;
            var host = JbsJson.String(obj, "hostName") ?? JbsJson.String(obj, "host") ?? "";
            var hostUserId = JbsJson.String(obj, "hostUserId")
                ?? JbsJson.String(obj, "hostUserID")
                ?? JbsJson.String(obj, "hostId")
                ?? "";
            var count = JbsJson.Int(obj, "playerCount", JbsJson.Int(obj, "count"));
            var max = JbsJson.Int(obj, "maxPlayers", JbsJson.Int(obj, "max"));
            var limitPlayers = JbsJson.Bool(obj, "limitPlayers");
            var autoStartOnFull = JbsJson.Bool(obj, "autoStartOnFull");
            if (max <= 0)
                max = DefaultRoomMaxPlayers;
            if (!string.IsNullOrEmpty(code))
                rooms.Add(new RoomEntry(code, name, host, hostUserId, count, max, limitPlayers, autoStartOnFull));
        }

        return rooms;
    }

    private static bool TryGetRoomArray(string json, out List<object?> rooms)
    {
        if (JbsJson.TryParseArray(json, out rooms))
            return true;

        if (JbsJson.TryParseObject(json, out var obj)
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
        if (string.IsNullOrEmpty(_searchQuery))
            return _rooms;

        var result = new List<RoomEntry>();
        foreach (var room in _rooms)
            if (room.Name.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0
                || room.Id.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                result.Add(room);
        return result;
    }

    private void RebuildRoomRows(List<RoomEntry> rooms)
    {
        if (_roomListContainer == null)
            return;

        _roomRows.Clear();
        _roomListContainer.Clear();
        _emptyLabel = LabelText(JbsLocalization.Get("tournament.room.empty"), 16, MutedText);
        _emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _emptyLabel.style.paddingTop = 120f;
        _emptyLabel.SetDisplay(rooms.Count == 0);
        _roomListContainer.Add(_emptyLabel);

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            var row = BuildRoomRow(i, room);
            _roomListContainer.Add(row.Root);
            _roomRows.Add(row);
        }
    }

    private RoomRowView BuildRoomRow(int index, RoomEntry room)
    {
        var row = new VisualElement();
        row.name = $"RoomRow_{index}";
        row.style.minHeight = 118f;
        row.style.marginBottom = 10f;
        row.style.paddingLeft = 16f;
        row.style.paddingRight = 16f;
        row.style.paddingTop = 12f;
        row.style.paddingBottom = 12f;
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.backgroundColor = RowBackground;
        ApplyBorder(row, Rgba(0.42f, 0.34f, 0.22f, 0.44f));
        ApplyRadius(row, 4f);
        RegisterHoverFeedback(row, RowBackground, Rgba(0.14f, 0.18f, 0.24f, 0.99f));

        var accent = new VisualElement();
        accent.style.width = 5f;
        accent.style.alignSelf = Align.Stretch;
        accent.style.marginRight = 12f;
        accent.style.backgroundColor = room.CurrentPlayers >= room.MaxPlayers ? DangerButtonBackground : Accent;
        row.Add(accent);

        var main = new VisualElement();
        main.style.flexGrow = 1f;
        main.style.minWidth = 0f;
        row.Add(main);

        var top = new VisualElement();
        top.style.flexDirection = FlexDirection.Row;
        top.style.alignItems = Align.Center;
        main.Add(top);

        var name = LabelText(room.Name, 18, BodyText, FontStyle.Bold);
        name.style.flexGrow = 1f;
        name.style.minWidth = 0f;
        name.style.whiteSpace = WhiteSpace.NoWrap;
        top.Add(name);

        var code = Pill(JbsLocalization.Get("tournament.room.code_inline", room.Id), Accent);
        top.Add(code);

        var bottom = new VisualElement();
        bottom.style.flexDirection = FlexDirection.Row;
        bottom.style.flexWrap = Wrap.Wrap;
        bottom.style.alignItems = Align.Center;
        bottom.style.marginTop = 8f;
        main.Add(bottom);

        var host = LabelText(JbsLocalization.Get("tournament.room.host", room.HostName), 13, MutedText);
        host.style.flexGrow = 1f;
        host.style.minWidth = 0f;
        host.style.whiteSpace = WhiteSpace.NoWrap;
        bottom.Add(host);

        bottom.Add(Pill(FormatHostUserId(room.HostUserId), Rgba(0.55f, 0.72f, 0.82f, 1f)));
        bottom.Add(Pill(FormatRoomOptions(room), WarmAccent));
        bottom.Add(Pill(JbsLocalization.Get("tournament.room.player_count", room.CurrentPlayers, room.MaxPlayers),
            room.CurrentPlayers >= room.MaxPlayers ? DangerButtonBackground : BodyText));

        var join = CreateButton(JbsLocalization.Get("tournament.button.join_inline"), () => JoinRoom(room), PrimaryButtonBackground);
        join.style.width = 140f;
        join.style.marginLeft = 12f;
        row.Add(join);

        var view = new RoomRowView(row, room);
        row.RegisterCallback<ClickEvent>(_ => OnRoomRowClicked(view));
        return view;
    }

    private static string FormatRoomOptions(RoomEntry room)
    {
        var limitText = room.LimitPlayers
            ? JbsLocalization.Get("tournament.room.limit_players", room.MaxPlayers)
            : JbsLocalization.Get("tournament.room.no_player_limit");
        var autoText = room.AutoStartOnFull
            ? JbsLocalization.Get("tournament.room.auto_start_on")
            : JbsLocalization.Get("tournament.room.auto_start_off");
        return $"{limitText} - {autoText}";
    }

    private void OnSearchChanged(string query)
    {
        _searchQuery = query?.Trim() ?? string.Empty;
        var filtered = GetFilteredRooms();
        SetStatus(string.IsNullOrEmpty(_searchQuery)
            ? JbsLocalization.Get("tournament.status.room_count", _rooms.Count)
            : JbsLocalization.Get("tournament.status.search_result", filtered.Count, _rooms.Count));
        RebuildRoomRows(filtered);
    }

    private void OnCreateRoomClicked()
    {
        if (_createRoomOverlay == null)
            return;

        _roomNameInput?.SetValueWithoutNotify(string.Empty);
        _roomCodeInput?.SetValueWithoutNotify(string.Empty);
        _maxPlayersInput?.SetValueWithoutNotify(DefaultRoomMaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetCreateDialogStatus(null);
        _createLimitPlayers = true;
        _createAutoStartOnFull = false;
        RefreshCreateRoomOptionLabels();
        _createRoomOverlay.SetDisplay(true);
        _roomNameInput?.schedule.Execute(() => _roomNameInput?.Focus()).StartingIn(0);
    }

    private void OnConfirmCreateRoom()
    {
        if (_nativeCreateInFlight)
        {
            SetCreateDialogStatus(JbsLocalization.Get("tournament.status.creating_native_room"));
            return;
        }

        var roomName = _roomNameInput?.value?.Trim();
        if (string.IsNullOrEmpty(roomName))
        {
            SetCreateDialogStatus(JbsLocalization.Get("tournament.error.room_name_empty"));
            return;
        }

        var maxPlayers = ResolveCreateMaxPlayers();
        if (_createLimitPlayers && maxPlayers <= 0)
        {
            SetCreateDialogStatus(JbsLocalization.Get("tournament.error.max_players_invalid", DefaultRoomMaxPlayers));
            return;
        }

        var request = new NativeCreateRoomRequest(
            roomName!,
            _createLimitPlayers,
            _createLimitPlayers ? maxPlayers : DefaultRoomMaxPlayers,
            _createAutoStartOnFull
        );
        HideCreateRoomOverlay();
        JbsLog.Info(LogCategory, $"Creating native tournament lobby: {roomName}");
        StartCoroutine(RunNativeCreateRoomFlow(request));
    }

    private void SetCreateDialogStatus(string? text)
    {
        if (_createDialogStatus == null)
            return;

        _createDialogStatus.text = text ?? string.Empty;
        _createDialogStatus.SetDisplay(!string.IsNullOrWhiteSpace(text));
    }

    private IEnumerator RunNativeCreateRoomFlow(NativeCreateRoomRequest request)
    {
        _nativeCreateInFlight = true;
        _pendingNativeCreatedRoomName = request.RoomName;
        SetStatus(JbsLocalization.Get("tournament.status.creating_native_room"));
        MinimizeToEdge();

        try
        {
            var hostRoot = FindNativeHostSettingsRoot();
            if (hostRoot == null)
            {
                var openHostButton = FindNativeOpenHostButton();
                if (openHostButton == null)
                {
                    RestoreFromEdgeDock();
                    _pendingNativeCreatedRoomName = null;
                    SetStatus(JbsLocalization.Get("tournament.error.native_create_entry_missing"));
                    yield break;
                }

                openHostButton.onClick.Invoke();
            }

            var deadline = Time.unscaledTime + 5f;
            while (Time.unscaledTime <= deadline)
            {
                hostRoot = FindNativeHostSettingsRoot();
                if (hostRoot != null)
                    break;
                yield return new WaitForSecondsRealtime(0.15f);
            }

            if (hostRoot == null)
            {
                RestoreFromEdgeDock();
                _pendingNativeCreatedRoomName = null;
                SetStatus(JbsLocalization.Get("tournament.error.native_host_settings_missing"));
                yield break;
            }

            FillNativeHostSettings(hostRoot, request);
            yield return null;

            var createButton = FindNativeCreateConfirmButton(hostRoot);
            if (createButton == null)
            {
                RestoreFromEdgeDock();
                _pendingNativeCreatedRoomName = null;
                SetStatus(JbsLocalization.Get("tournament.error.native_create_confirm_missing"));
                yield break;
            }

            JbsNativeTournamentRoomWatcher.ArmPendingCreateWindow();
            createButton.onClick.Invoke();
            SetStatus(JbsLocalization.Get("tournament.status.waiting_native_room_code"));
        }
        finally
        {
            _nativeCreateInFlight = false;
        }
    }

    private static UnityEngine.UI.Button? FindNativeOpenHostButton()
    {
        UnityEngine.UI.Button? best = null;
        var bestScore = float.MinValue;
        foreach (var custom in Resources.FindObjectsOfTypeAll<ButtonCustom>())
        {
            if (custom == null)
                continue;

            var button = custom.GetButton();
            if (!IsUsableNativeButton(button))
                continue;

            var path = BuildTransformPath(custom.transform);
            if (path.IndexOf("Section_HostOrJoin", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var text = BuildTextContext(custom.gameObject);
            var score = 0f;
            if (custom.name.Equals("Btn_Create", StringComparison.Ordinal))
                score += 1000f;
            if (ContainsAny(path, "Block_Host", "Btn_Create"))
                score += 300f;
            if (ContainsAny(text, "创建大厅", "主办锦标赛", "Create Lobby", "Host Tournament", "Host"))
                score += 300f;

            if (score > bestScore)
            {
                bestScore = score;
                best = button;
            }
        }

        return bestScore > 0f ? best : null;
    }

    private static RectTransform? FindNativeHostSettingsRoot()
    {
        RectTransform? best = null;
        var bestScore = float.MinValue;
        foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
                continue;

            var path = BuildTransformPath(rect);
            if (path.IndexOf("Section_HostSettings", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var score = 1000f;
            if (rect.name.Equals("Section_HostSettings", StringComparison.Ordinal))
                score += 500f;
            if (path.IndexOf("Tournament_Module", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 200f;

            if (score > bestScore)
            {
                bestScore = score;
                best = rect;
            }
        }

        return best;
    }

    private static UnityEngine.UI.Button? FindNativeCreateConfirmButton(RectTransform root)
    {
        UnityEngine.UI.Button? best = null;
        var bestScore = float.MinValue;
        foreach (var custom in root.GetComponentsInChildren<ButtonCustom>(true))
        {
            if (custom == null)
                continue;

            var button = custom.GetButton();
            if (!IsUsableNativeButton(button))
                continue;

            var path = BuildTransformPath(custom.transform);
            var text = BuildTextContext(custom.gameObject);
            var score = 0f;
            if (custom.name.Equals("Btn_Blue_Large_P", StringComparison.Ordinal))
                score += 1000f;
            if (ContainsAny(path, "Btn_Blue", "Create"))
                score += 250f;
            if (ContainsAny(text, "CREATE", "创建", "创建大厅"))
                score += 250f;

            if (score > bestScore)
            {
                bestScore = score;
                best = button;
            }
        }

        return bestScore > 0f ? best : null;
    }

    private static void FillNativeHostSettings(RectTransform root, NativeCreateRoomRequest request)
    {
        if (request.LimitPlayers)
            TryActivateNativeToggle(root, "Toggle_LobbySIze", "限制大厅人数", "Limit");

        if (request.AutoStartOnFull)
            TryActivateNativeToggle(root, "Toggle_AutoStart", "大厅满员时开始", "AutoStart");

        var maxInput = FindNativeMaxPlayersInput(root);
        if (maxInput != null && request.LimitPlayers)
            SetTmpInputValue(maxInput, request.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static TMP_InputField? FindNativeMaxPlayersInput(RectTransform root)
    {
        TMP_InputField? best = null;
        var bestScore = float.MinValue;
        foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (input == null || !input.gameObject.activeInHierarchy)
                continue;

            var path = BuildTransformPath(input.transform);
            var context = BuildTextContext(input.gameObject);
            var score = 0f;
            if (ContainsAny(path, "TextEntry_PlayerMax", "PlayerMax", "LimitGroup"))
                score += 500f;
            if (ContainsAny(context, "玩家上限", "Max", "Player"))
                score += 150f;

            if (score > bestScore)
            {
                bestScore = score;
                best = input;
            }
        }

        return bestScore > 0f ? best : null;
    }

    private static void SetTmpInputValue(TMP_InputField input, string value)
    {
        input.interactable = true;
        input.Select();
        input.ActivateInputField();
        input.SetTextWithoutNotify(value);
        input.text = value;
        input.onValueChanged.Invoke(value);
        input.onEndEdit.Invoke(value);
        input.MoveTextEnd(false);
    }

    private static bool TryActivateNativeToggle(RectTransform root, params string[] hints)
    {
        Component? best = null;
        var bestScore = float.MinValue;
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null || component is Transform)
                continue;

            var typeName = component.GetType().Name;
            if (typeName.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var path = BuildTransformPath(component.transform);
            var context = path + " " + BuildTextContext(component.gameObject);
            var score = 0f;
            foreach (var hint in hints)
                if (context.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 200f;

            if (score > bestScore)
            {
                bestScore = score;
                best = component;
            }
        }

        if (best == null || bestScore <= 0f)
            return false;

        if (TrySetToggleValue(best, true))
            return true;

        var button = best.GetComponentInChildren<UnityEngine.UI.Button>(true);
        if (IsUsableNativeButton(button))
        {
            button!.onClick.Invoke();
            return true;
        }

        best.gameObject.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
        best.gameObject.SendMessage("Toggle", SendMessageOptions.DontRequireReceiver);
        return true;
    }

    private static bool TrySetToggleValue(Component toggle, bool value)
    {
        var type = toggle.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var propertyName in new[] { "IsOn", "isOn", "Value", "value" })
        {
            var property = type.GetProperty(propertyName, flags);
            if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
            {
                property.SetValue(toggle, value);
                return true;
            }
        }

        foreach (var methodName in new[] { "SetIsOn", "SetValue", "Set", "SetToggleState" })
        {
            var method = type.GetMethod(methodName, flags, null, new[] { typeof(bool) }, null);
            if (method != null)
            {
                method.Invoke(toggle, new object[] { value });
                return true;
            }
        }

        if (toggle is UnityEngine.UI.Toggle unityToggle)
        {
            unityToggle.isOn = value;
            return true;
        }

        return false;
    }

    private static bool IsUsableNativeButton(UnityEngine.UI.Button? button) =>
        button != null && button.gameObject.activeInHierarchy && button.interactable;

    private static string BuildTransformPath(Transform transform)
    {
        var parts = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }

    private static string BuildTextContext(GameObject root)
    {
        var parts = new List<string>();
        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (text != null && text.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(text.text.Trim());
        foreach (var text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            if (text != null && text.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(text.text))
                parts.Add(text.text.Trim());
        return string.Join(" ", parts);
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
            if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private void ToggleCreateLimitPlayers()
    {
        _createLimitPlayers = !_createLimitPlayers;
        RefreshCreateRoomOptionLabels();
    }

    private void ToggleCreateAutoStartOnFull()
    {
        _createAutoStartOnFull = !_createAutoStartOnFull;
        RefreshCreateRoomOptionLabels();
    }

    private void RefreshCreateRoomOptionLabels()
    {
        if (_limitPlayersButton != null)
            _limitPlayersButton.text = _createLimitPlayers
                ? JbsLocalization.Get("tournament.dialog.limit_players_on")
                : JbsLocalization.Get("tournament.dialog.limit_players_off");

        if (_autoStartButton != null)
            _autoStartButton.text = _createAutoStartOnFull
                ? JbsLocalization.Get("tournament.dialog.auto_start_on")
                : JbsLocalization.Get("tournament.dialog.auto_start_off");

        if (_maxPlayersInput != null)
            _maxPlayersInput.SetEnabled(_createLimitPlayers);
    }

    private int ResolveCreateMaxPlayers()
    {
        var value = _maxPlayersInput?.value?.Trim();
        if (string.IsNullOrEmpty(value))
            return DefaultRoomMaxPlayers;
        if (!int.TryParse(value, out var parsed))
            return 0;
        if (parsed < 1 || parsed > DefaultRoomMaxPlayers)
            return 0;
        return parsed;
    }

    private void OnCancelCreateRoom() => HideCreateRoomOverlay();

    private void HideCreateRoomOverlay() => _createRoomOverlay?.SetDisplay(false);

    private void OnJoinRoomClicked()
    {
        if (_selectedRow == null)
        {
            SetStatus(JbsLocalization.Get("tournament.error.no_room_selected"));
            return;
        }
        JoinRoom(_selectedRow.Entry);
    }

    private void JoinRoom(RoomEntry room)
    {
        JbsLog.Info(LogCategory, $"Join room: {room.Name} ({room.Id})");
        BeginJoinRoom(room);
    }

    private void OnManualJoinRoom()
    {
        var roomCode = NormalizeRoomCode(_manualRoomCodeInput?.value);
        if (!IsValidRoomCode(roomCode))
        {
            SetStatus(JbsLocalization.Get("tournament.error.room_code_invalid"));
            return;
        }

        var knownRoom = _rooms.Find(room => string.Equals(room.Id, roomCode, StringComparison.OrdinalIgnoreCase));
        var room = knownRoom ?? new RoomEntry(roomCode, roomCode, "", "", 0, DefaultRoomMaxPlayers);
        JbsLog.Info(LogCategory, $"Join room by code: {room.Id}");
        BeginJoinRoom(room);
    }

    private void OnToggleChatClicked()
    {
        if (_currentRoom == null)
        {
            SetStatus(JbsLocalization.Get("tournament.error.no_active_room"));
            return;
        }

        _roomListView?.SetDisplay(false);
        _chatView?.SetDisplay(true);
        RefreshChatToggleButtonLabel();
    }

    private void RefreshChatToggleButtonLabel()
    {
        if (_chatToggleButton == null)
            return;

        var hasActiveRoom = _currentRoom != null;
        _chatToggleButton.SetDisplay(hasActiveRoom);
        _chatToggleButton.text = JbsLocalization.Get("tournament.button.return_chat");
    }

    private void OnCopyRoomId()
    {
        if (_currentRoom == null)
            return;
        GUIUtility.systemCopyBuffer = _currentRoom.Id;
        JbsLog.Info(LogCategory, $"Room ID copied: {_currentRoom.Id}");
    }

    private void OnDisbandRoom()
    {
        if (!_currentPlayerIsHost || _currentRoom == null || _chatClient == null)
            return;
        _ = DisbandRoomAsync();
    }

    private async System.Threading.Tasks.Task DisbandRoomAsync()
    {
        if (_chatClient == null || _currentRoom == null)
            return;

        var roomId = _currentRoom.Id;
        if (await _chatClient.DisbandRoomAsync(JbsConfig.ServerWsUrl))
        {
            JbsLog.Info(LogCategory, $"Room disbanded: {roomId}");
            LeaveCurrentRoomState();
            SetStatus(JbsLocalization.Get("tournament.status.room_disbanded"));
            ShowRoomListView();
            RefreshRoomList();
        }
    }

    private void OnLeaveRoom()
    {
        LeaveRoomAndReturnToList("Room leave button clicked");
    }

    private void OnRoomDisbandedByHost()
    {
        _ = _chatClient?.DisconnectAsync();
        LeaveCurrentRoomState();
        SetStatus(JbsLocalization.Get("tournament.status.room_disbanded"));
        ShowRoomListView();
        RefreshRoomList();
    }

    private void LeaveRoomAndReturnToList(string reason)
    {
        if (_currentRoom != null)
            JbsLog.Info(LogCategory, $"{reason}; leaving chat room {_currentRoom.Id}.");
        _ = LeaveAndDisconnectAsync(notifyServer: true);
        LeaveCurrentRoomState();
        if (_visible)
        {
            ShowRoomListView();
            RefreshRoomList();
        }
    }

    private void OnChatSend()
    {
        var text = _chatInput?.value?.Trim();
        if (string.IsNullOrEmpty(text))
            return;
        _chatInput?.SetValueWithoutNotify(string.Empty);
        _chatClient?.SendChatMessage(text!);
    }

    private void OnRoomRowClicked(RoomRowView rowView)
    {
        if (_selectedRow != null && _selectedRow != rowView)
            SetRowSelected(_selectedRow, false);

        if (_selectedRow == rowView)
        {
            SetRowSelected(rowView, false);
            _selectedRow = null;
            SetStatus(JbsLocalization.Get("tournament.status.room_count", _rooms.Count));
        }
        else
        {
            SetRowSelected(rowView, true);
            _selectedRow = rowView;
            var r = rowView.Entry;
            SetStatus(JbsLocalization.Get("tournament.status.selected", r.Name, r.CurrentPlayers, r.MaxPlayers));
        }
    }

    private static void SetRowSelected(RoomRowView rowView, bool selected)
    {
        rowView.Root.userData = selected;
        rowView.Root.style.backgroundColor = selected ? RowSelectedBackground : RowBackground;
        ApplyBorder(rowView.Root, selected ? Accent : Border);
    }

    private void SetStatus(string text)
    {
        if (_statusText != null)
            _statusText.text = text;
    }

    private void LeaveCurrentRoomState()
    {
        _joinAttemptId++;
        _pendingRoomOpen = null;
        _pendingJoinFailure = null;
        _joinInFlight = false;
        BppTournamentRoomBridgeClient.LeaveRoom();
        _currentRoom = null;
        _currentPlayerIsHost = false;
        RefreshHostControls();
    }

    private async System.Threading.Tasks.Task LeaveAndDisconnectAsync(bool notifyServer)
    {
        var client = _chatClient;
        if (client == null)
        {
            BppTournamentRoomBridgeClient.LeaveRoom();
            return;
        }

        if (notifyServer)
            await client.LeaveRoomAsync(JbsConfig.ServerWsUrl).ConfigureAwait(false);
        else
            await client.DisconnectAsync().ConfigureAwait(false);

        BppTournamentRoomBridgeClient.LeaveRoom();
    }

    private string? DrainChatClientSystemMessages()
    {
        if (_chatClient == null)
            return null;

        string? last = null;
        while (_chatClient.TryDequeue(out var msg))
            if (msg.IsSystem && !string.IsNullOrWhiteSpace(msg.Text))
                last = msg.Text;

        return last;
    }

    private static string GetPlayerName()
    {
        var playerName = JbsLocalization.Get("player.fallback");
        try
        {
            var bridge = Type.GetType("BazaarPlusPlus.GameInterop.BppClientCacheBridge, BazaarPlusPlus");
            if (bridge != null)
            {
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                var display = bridge.GetMethod("TryGetProfileDisplayUsername", flags)?.Invoke(null, null) as string;
                if (!string.IsNullOrWhiteSpace(display))
                    playerName = display.Trim();
                var username = bridge.GetMethod("TryGetProfileUsername", flags)?.Invoke(null, null) as string;
                if (string.IsNullOrWhiteSpace(display) && !string.IsNullOrWhiteSpace(username))
                    playerName = username.Trim();
            }
        }
        catch { }

        return AppendSelectedHero(playerName);
    }

    private static string AppendSelectedHero(string playerName)
    {
        var hero = GetSelectedHeroName();
        if (string.IsNullOrWhiteSpace(hero))
            return playerName;

        var trimmedName = playerName.Trim();
        if (trimmedName.EndsWith($@"({hero})", StringComparison.Ordinal))
            return trimmedName;

        return $"{trimmedName}({hero})";
    }

    private static string GetSelectedHeroName()
    {
        try
        {
            var dataType = Type.GetType("TheBazaar.Data, Assembly-CSharp");
            var selectedHero = dataType?.GetField("SelectedHero", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? dataType?.GetProperty("SelectedHero", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var heroName = selectedHero?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(heroName) || string.Equals(heroName, "Common", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return heroName;
        }
        catch (Exception ex)
        {
            JbsLog.Debug(LogCategory, $"Failed to resolve selected hero: {ex.Message}");
            return string.Empty;
        }
    }

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

    private static string GenerateRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = Guid.NewGuid().ToByteArray();
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    private static bool IsValidRoomCode(string value) => value.Length == 6;

    private void RegisterPanelDrag(VisualElement dragSurface)
    {
        dragSurface.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (_shell == null || IsInteractiveDragTarget(evt.target as VisualElement, dragSurface))
                return;

            _panelBoundsUserAdjusted = true;
            _isDraggingPanel = true;
            _isResizingPanel = false;
            _activePointerId = evt.pointerId;
            _pointerStart = new Vector2(evt.position.x, evt.position.y);
            _panelStartPosition = new Vector2(_panelLeft, _panelTop);
            dragSurface.CapturePointer(evt.pointerId);
            BringToFront();
            evt.StopPropagation();
        });
        dragSurface.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!_isDraggingPanel || evt.pointerId != _activePointerId)
                return;

            var current = new Vector2(evt.position.x, evt.position.y);
            var delta = current - _pointerStart;
            SetPanelBounds(_panelStartPosition.x + delta.x, _panelStartPosition.y + delta.y, _panelWidth, _panelHeight);
            evt.StopPropagation();
        });
        dragSurface.RegisterCallback<PointerUpEvent>(evt => EndPanelPointerAction(dragSurface, evt.pointerId));
        dragSurface.RegisterCallback<PointerCancelEvent>(evt => EndPanelPointerAction(dragSurface, evt.pointerId));
    }

    private void RegisterPanelResize(VisualElement resizeHandle)
    {
        resizeHandle.RegisterCallback<PointerDownEvent>(evt =>
        {
            _panelBoundsUserAdjusted = true;
            _isResizingPanel = true;
            _isDraggingPanel = false;
            _activePointerId = evt.pointerId;
            _pointerStart = new Vector2(evt.position.x, evt.position.y);
            _panelStartSize = new Vector2(_panelWidth, _panelHeight);
            resizeHandle.CapturePointer(evt.pointerId);
            BringToFront();
            evt.StopPropagation();
        });
        resizeHandle.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!_isResizingPanel || evt.pointerId != _activePointerId)
                return;

            var current = new Vector2(evt.position.x, evt.position.y);
            var delta = current - _pointerStart;
            SetPanelBounds(_panelLeft, _panelTop, _panelStartSize.x + delta.x, _panelStartSize.y + delta.y);
            evt.StopPropagation();
        });
        resizeHandle.RegisterCallback<PointerUpEvent>(evt => EndPanelPointerAction(resizeHandle, evt.pointerId));
        resizeHandle.RegisterCallback<PointerCancelEvent>(evt => EndPanelPointerAction(resizeHandle, evt.pointerId));
    }

    private void MinimizeToEdge()
    {
        if (_root == null || _shell == null)
            return;

        SetVisible(false);
    }

    private void RestoreFromEdgeDock()
    {
        if (!_isEdgeDocked || _shell == null)
            return;

        ExitEdgeDockState();
    }

    private void ExitEdgeDockState()
    {
        _isEdgeDocked = false;
        _edgeDockButton?.SetDisplay(false);
        _shell?.SetDisplay(true);
    }

    private void PositionEdgeDockButton()
    {
        if (_root == null || _edgeDockButton == null)
            return;

        var rootHeight = _root.resolvedStyle.height;
        if (rootHeight <= 1f)
            return;

        var top = Mathf.Clamp(
            _panelTop + 80f,
            PanelEdgePadding,
            Mathf.Max(PanelEdgePadding, rootHeight - EdgeDockButtonHeight - PanelEdgePadding)
        );
        _edgeDockButton.style.top = top;
    }

    private void EndPanelPointerAction(VisualElement owner, int pointerId)
    {
        if (pointerId != _activePointerId)
            return;

        _isDraggingPanel = false;
        _isResizingPanel = false;
        _activePointerId = -1;
        if (owner.HasPointerCapture(pointerId))
            owner.ReleasePointer(pointerId);
        EnsurePanelBounds();
    }

    private static bool IsInteractiveDragTarget(VisualElement? target, VisualElement dragSurface)
    {
        for (var current = target; current != null && current != dragSurface; current = current.parent)
            if (current is Button || current is TextField)
                return true;
        return false;
    }

    private void EnsurePanelBounds()
    {
        if (_root == null || _shell == null)
            return;

        var rootWidth = _root.resolvedStyle.width;
        var rootHeight = _root.resolvedStyle.height;
        if (rootWidth <= 1f || rootHeight <= 1f)
            return;

        if (!_panelBoundsInitialized)
        {
            var nativeBounds = ResolveTournamentMainPanelBounds(rootWidth, rootHeight);
            var width = Mathf.Min(
                nativeBounds?.width ?? DefaultPanelWidth,
                Mathf.Max(120f, rootWidth - PanelEdgePadding * 2f)
            );
            var height = Mathf.Min(
                nativeBounds?.height ?? DefaultPanelHeight,
                Mathf.Max(120f, rootHeight - PanelEdgePadding * 2f)
            );
            var left = nativeBounds?.x ?? (rootWidth - width) * 0.5f;
            var top = nativeBounds?.y ?? (rootHeight - height) * 0.5f;
            _panelBoundsInitialized = true;
            SetPanelBounds(left, top, width, height);
            return;
        }

        if (_isEdgeDocked)
            PositionEdgeDockButton();

        SetPanelBounds(_panelLeft, _panelTop, _panelWidth, _panelHeight);
    }

    private void SetPanelBounds(float left, float top, float width, float height)
    {
        if (_root == null || _shell == null)
            return;

        var rootWidth = _root.resolvedStyle.width;
        var rootHeight = _root.resolvedStyle.height;
        if (rootWidth <= 1f || rootHeight <= 1f)
            return;

        var minWidth = Mathf.Min(MinPanelWidth, Mathf.Max(120f, rootWidth - PanelEdgePadding * 2f));
        var minHeight = Mathf.Min(MinPanelHeight, Mathf.Max(120f, rootHeight - PanelEdgePadding * 2f));
        var maxWidth = Mathf.Max(minWidth, rootWidth - PanelEdgePadding * 2f);
        var maxHeight = Mathf.Max(minHeight, rootHeight - PanelEdgePadding * 2f);

        width = Mathf.Clamp(width, minWidth, maxWidth);
        height = Mathf.Clamp(height, minHeight, maxHeight);

        var minLeft = rootWidth > width + PanelEdgePadding * 2f ? PanelEdgePadding : 0f;
        var minTop = rootHeight > height + PanelEdgePadding * 2f ? PanelEdgePadding : 0f;
        var maxLeft = Mathf.Max(minLeft, rootWidth - width - minLeft);
        var maxTop = Mathf.Max(minTop, rootHeight - height - minTop);

        _panelLeft = Mathf.Clamp(left, minLeft, maxLeft);
        _panelTop = Mathf.Clamp(top, minTop, maxTop);
        _panelWidth = width;
        _panelHeight = height;

        ApplyPanelVisualBounds(_panelLeft, _panelTop, _panelWidth, _panelHeight);
    }

    private Rect? ResolveTournamentMainPanelBounds(float rootWidth, float rootHeight)
    {
        var tournamentRect = FindTournamentHostOrJoinRect();
        if (tournamentRect != null && _panelObject != null)
        {
            var hostRect = _panelObject.transform.parent as RectTransform;
            if (hostRect != null)
            {
                var corners = new Vector3[4];
                tournamentRect.GetWorldCorners(corners);
                var bottomLeft = hostRect.InverseTransformPoint(corners[0]);
                var topLeft = hostRect.InverseTransformPoint(corners[1]);
                var topRight = hostRect.InverseTransformPoint(corners[2]);
                var bottomRight = hostRect.InverseTransformPoint(corners[3]);

                var localLeft = Mathf.Min(bottomLeft.x, topLeft.x, topRight.x, bottomRight.x);
                var localRight = Mathf.Max(bottomLeft.x, topLeft.x, topRight.x, bottomRight.x);
                var localTop = Mathf.Max(bottomLeft.y, topLeft.y, topRight.y, bottomRight.y);
                var localBottom = Mathf.Min(bottomLeft.y, topLeft.y, topRight.y, bottomRight.y);

                var width = Mathf.Abs(localRight - localLeft);
                var height = Mathf.Abs(localTop - localBottom);
                if (width >= MinPanelWidth * 0.75f && height >= MinPanelHeight * 0.60f)
                {
                    var left = rootWidth * 0.5f + localLeft;
                    var top = rootHeight * 0.5f - localTop;
                    return new Rect(left, top, width, Mathf.Max(MinPanelHeight, height));
                }
            }
        }

        return null;
    }

    private static RectTransform? FindTournamentHostOrJoinRect()
    {
        RectTransform? best = null;
        var bestScore = float.MinValue;
        foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
                continue;
            if (!string.Equals(rect.gameObject.name, "Section_HostOrJoin", StringComparison.Ordinal))
                continue;

            var score = rect.rect.width;
            if (rect.GetComponentInParent<Canvas>() != null)
                score += 100f;
            if (rect.Find("Block_Join/Input_Code") != null)
                score += 250f;
            if (rect.Find("Text_Tournament") != null)
                score += 250f;

            if (score > bestScore)
            {
                bestScore = score;
                best = rect;
            }
        }

        return best;
    }

    private void ApplyPanelVisualBounds(float left, float top, float width, float height)
    {
        if (_shell == null)
            return;

        _panelLeft = left;
        _panelTop = top;
        _panelWidth = width;
        _panelHeight = height;

        _shell.style.left = _panelLeft;
        _shell.style.top = _panelTop;
        _shell.style.width = _panelWidth;
        _shell.style.height = _panelHeight;
    }

    private static Button CreateButton(string text, Action onClick, Color background)
    {
        var button = new Button(onClick) { text = text };
        button.style.minHeight = ButtonHeight;
        button.style.paddingLeft = 14f;
        button.style.paddingRight = 14f;
        button.style.paddingTop = 6f;
        button.style.paddingBottom = 6f;
        button.style.backgroundColor = background;
        button.style.color = BodyText;
        button.style.fontSize = 16;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.whiteSpace = WhiteSpace.Normal;
        button.style.marginLeft = 0f;
        button.style.marginRight = 0f;
        button.style.marginTop = 0f;
        button.style.marginBottom = 0f;
        ApplyBorder(button, Rgba(0.62f, 0.50f, 0.32f, 0.46f));
        ApplyRadius(button, 4f);
        var font = ResolveUiFont();
        if (font != null)
            button.style.unityFont = font;
        button.RegisterCallback<AttachToPanelEvent>(_ => StyleButtonInternals(button));
        RegisterButtonFeedback(button, background);
        return button;
    }

    private static TextField CreateTextField(string placeholder)
    {
        var field = new TextField();
        field.focusable = true;
        field.pickingMode = PickingMode.Position;
        field.style.minHeight = TextFieldHeight;
        field.style.fontSize = 16;
        field.style.color = BodyText;
        field.style.backgroundColor = Rgba(0.07f, 0.09f, 0.12f, 0.98f);
        field.style.unityFontStyleAndWeight = FontStyle.Normal;
        field.style.marginLeft = 0f;
        field.style.marginRight = 0f;
        field.style.marginTop = 0f;
        field.style.marginBottom = 0f;
        field.tooltip = placeholder;
        field.label = string.Empty;
        ApplyBorder(field, Rgba(0.50f, 0.42f, 0.30f, 0.42f));
        ApplyRadius(field, 4f);
        var font = ResolveUiFont();
        if (font != null)
            field.style.unityFont = font;
        field.RegisterCallback<AttachToPanelEvent>(_ => StyleTextFieldInternals(field));
        return field;
    }

    private static Label LabelText(string text, int size, Color color, FontStyle style = FontStyle.Normal)
    {
        var label = new Label(text);
        label.style.fontSize = size;
        label.style.color = color;
        label.style.unityFontStyleAndWeight = style;
        label.style.marginLeft = 0f;
        label.style.marginRight = 0f;
        label.style.marginTop = 0f;
        label.style.marginBottom = 0f;
        var font = ResolveUiFont();
        if (font != null)
            label.style.unityFont = font;
        return label;
    }

    private static Label Pill(string text, Color accent)
    {
        var pill = LabelText(text, 12, BodyText, FontStyle.Bold);
        pill.style.minHeight = 24f;
        pill.style.paddingLeft = 8f;
        pill.style.paddingRight = 8f;
        pill.style.paddingTop = 3f;
        pill.style.paddingBottom = 3f;
        pill.style.marginLeft = 8f;
        pill.style.marginTop = 3f;
        pill.style.unityTextAlign = TextAnchor.MiddleCenter;
        pill.style.whiteSpace = WhiteSpace.Normal;
        pill.style.backgroundColor = Rgba(
            Mathf.Lerp(0.14f, accent.r, 0.10f),
            Mathf.Lerp(0.16f, accent.g, 0.10f),
            Mathf.Lerp(0.20f, accent.b, 0.10f),
            0.98f
        );
        ApplyBorder(pill, Rgba(accent.r, accent.g, accent.b, 0.70f));
        return pill;
    }

    private static void ApplyBorder(VisualElement element, Color color, float width = 1f)
    {
        element.style.borderTopWidth = width;
        element.style.borderRightWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftWidth = width;
        element.style.borderTopColor = color;
        element.style.borderRightColor = color;
        element.style.borderBottomColor = color;
        element.style.borderLeftColor = color;
    }

    private static void ApplyRadius(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
    }

    private static void RegisterButtonFeedback(Button button, Color normal)
    {
        var hover = Lighten(normal, 0.18f);
        var pressed = Darken(normal, 0.16f);
        button.RegisterCallback<MouseEnterEvent>(_ => button.style.backgroundColor = hover);
        button.RegisterCallback<MouseLeaveEvent>(_ => button.style.backgroundColor = normal);
        button.RegisterCallback<MouseDownEvent>(_ => button.style.backgroundColor = pressed);
        button.RegisterCallback<MouseUpEvent>(_ => button.style.backgroundColor = hover);
        button.RegisterCallback<FocusInEvent>(_ => ApplyBorder(button, Accent));
        button.RegisterCallback<FocusOutEvent>(_ => ApplyBorder(button, Border));
    }

    private static void RegisterHoverFeedback(VisualElement element, Color normal, Color hover)
    {
        element.RegisterCallback<MouseEnterEvent>(_ =>
        {
            if (element.userData is true)
                return;
            element.style.backgroundColor = hover;
        });
        element.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            if (element.userData is true)
                return;
            element.style.backgroundColor = normal;
        });
    }

    private static void StyleButtonInternals(Button button)
    {
        var font = ResolveUiFont();
        foreach (var textElement in button.Query<TextElement>().ToList())
        {
            textElement.style.color = BodyText;
            textElement.style.fontSize = 16;
            textElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            textElement.style.whiteSpace = WhiteSpace.Normal;
            if (font != null)
                textElement.style.unityFont = font;
        }
    }

    private static void StyleTextFieldInternals(TextField field)
    {
        field.style.color = BodyText;
        foreach (var textInput in field.Query<VisualElement>(className: "unity-text-input").ToList())
        {
            textInput.style.color = BodyText;
            textInput.style.backgroundColor = Rgba(0.07f, 0.09f, 0.12f, 0.98f);
            textInput.style.fontSize = 16;
            textInput.style.minHeight = TextFieldHeight - 4f;
            textInput.style.unityTextAlign = TextAnchor.MiddleLeft;
            var font = ResolveUiFont();
            if (font != null)
                textInput.style.unityFont = font;
        }
    }

    private static Font? ResolveUiFont()
    {
        if (_resolvedUiFont != null)
            return _resolvedUiFont;

        try
        {
            foreach (var text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (text == null || text.font == null)
                    continue;

                var sourceFont = text.font.sourceFontFile;
                if (sourceFont != null)
                {
                    _resolvedUiFont = sourceFont;
                    return _resolvedUiFont;
                }
            }
        }
        catch
        {
            // Fall back to Unity's built-in dynamic font below.
        }

        try
        {
            _resolvedUiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        catch
        {
            _resolvedUiFont = null;
        }

        return _resolvedUiFont;
    }

    private static Color Lighten(Color color, float amount) =>
        Rgba(
            Mathf.Lerp(color.r, 1f, amount),
            Mathf.Lerp(color.g, 1f, amount),
            Mathf.Lerp(color.b, 1f, amount),
            color.a
        );

    private static Color Darken(Color color, float amount) =>
        Rgba(
            Mathf.Lerp(color.r, 0f, amount),
            Mathf.Lerp(color.g, 0f, amount),
            Mathf.Lerp(color.b, 0f, amount),
            color.a
        );

    private static string FormatHostUserId(string? hostUserId) =>
        string.IsNullOrWhiteSpace(hostUserId)
            ? JbsLocalization.Get("player.unknown")
            : JbsLocalization.Get("tournament.room.host_id", hostUserId.Trim());

    private static Color Rgba(float r, float g, float b, float a) => new(r, g, b, a);

    private sealed class PendingRoomOpen
    {
        internal PendingRoomOpen(RoomEntry room, bool isHost, int attemptId)
        {
            Room = room;
            IsHost = isHost;
            AttemptId = attemptId;
        }

        internal RoomEntry Room { get; }
        internal bool IsHost { get; }
        internal int AttemptId { get; }
    }

    private sealed class NativeCreateRoomRequest
    {
        internal NativeCreateRoomRequest(
            string roomName,
            bool limitPlayers,
            int maxPlayers,
            bool autoStartOnFull)
        {
            RoomName = roomName;
            LimitPlayers = limitPlayers;
            MaxPlayers = maxPlayers;
            AutoStartOnFull = autoStartOnFull;
        }

        internal string RoomName { get; }
        internal bool LimitPlayers { get; }
        internal int MaxPlayers { get; }
        internal bool AutoStartOnFull { get; }
    }

    internal sealed class RoomEntry
    {
        internal RoomEntry(
            string id,
            string name,
            string hostName,
            string hostUserId,
            int currentPlayers,
            int maxPlayers,
            bool limitPlayers = false,
            bool autoStartOnFull = false)
        {
            Id = id;
            Name = name;
            HostName = hostName;
            HostUserId = hostUserId;
            CurrentPlayers = currentPlayers;
            MaxPlayers = maxPlayers;
            LimitPlayers = limitPlayers;
            AutoStartOnFull = autoStartOnFull;
        }

        internal string Id { get; }
        internal string Name { get; }
        internal string HostName { get; }
        internal string HostUserId { get; }
        internal int CurrentPlayers { get; }
        internal int MaxPlayers { get; }
        internal bool LimitPlayers { get; }
        internal bool AutoStartOnFull { get; }
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

        internal static PlayerEntry Synthetic(string key, string name) => new(key, null, name);

        internal static string MakeKey(string? userId, string name) =>
            !string.IsNullOrEmpty(userId) ? $"id:{userId}" : $"name:{name}";
    }

    private sealed class RoomRowView
    {
        internal RoomRowView(VisualElement root, RoomEntry entry)
        {
            Root = root;
            Entry = entry;
        }

        internal VisualElement Root { get; }
        internal RoomEntry Entry { get; }
    }
}

internal static class TournamentRoomVisualElementExtensions
{
    internal static void SetDisplay(this VisualElement element, bool visible)
    {
        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
