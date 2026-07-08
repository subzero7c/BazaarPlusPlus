package main

import (
	"bufio"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"net/url"
	"os"
	"regexp"
	"runtime"
	"runtime/debug"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/gorilla/websocket"
)

const (
	maxPlayersPerRoom = 30
	maxNameLength     = 20
	maxTextLength     = 500
	maxChatHistory    = 50
	maxErrorLogBytes  = 64 * 1024
	roomListGrace     = 5 * time.Second
)

var roomCodeRe = regexp.MustCompile(`^[A-Z0-9]{6}$`)
var globalErrLog *log.Logger

func errorfGlobal(format string, args ...interface{}) {
	if globalErrLog != nil {
		globalErrLog.Printf(format, args...)
		return
	}
	log.Printf(format, args...)
}

// ── Data types ────────────────────────────────────────────────

type Player struct {
	UserID string `json:"userId"`
	Name   string `json:"name"`
}

type roomMeta struct {
	Code            string
	Name            string
	HostName        string
	HostUserID      string
	LimitPlayers    bool
	MaxPlayers      int
	AutoStartOnFull bool
	System          bool
	ReportedCount   int
	CreatedAt       time.Time
}

func (m *roomMeta) effectiveMaxPlayers() int {
	if m != nil && m.LimitPlayers && m.MaxPlayers > 0 && m.MaxPlayers <= maxPlayersPerRoom {
		return m.MaxPlayers
	}
	return maxPlayersPerRoom
}

// ── Match room (HTTP, tracks player list per room) ────────────

type matchRoom struct {
	mu      sync.RWMutex
	players map[string]Player
}

func (r *matchRoom) join(name string, maxPlayers int) (Player, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if maxPlayers <= 0 || maxPlayers > maxPlayersPerRoom {
		maxPlayers = maxPlayersPerRoom
	}
	if len(r.players) >= maxPlayers {
		return Player{}, fmt.Errorf("ROOM_FULL")
	}
	b := make([]byte, 4)
	if _, err := rand.Read(b); err != nil {
		return Player{}, fmt.Errorf("USER_ID_RANDOM_FAILED: %w", err)
	}
	p := Player{UserID: hex.EncodeToString(b), Name: name}
	r.players[p.UserID] = p
	return p, nil
}

func (r *matchRoom) leave(userID string) bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, ok := r.players[userID]; !ok {
		return false
	}
	delete(r.players, userID)
	return true
}

func (r *matchRoom) count() int {
	r.mu.RLock()
	defer r.mu.RUnlock()
	return len(r.players)
}

func (r *matchRoom) snapshot() (int, []Player) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	ps := make([]Player, 0, len(r.players))
	for _, p := range r.players {
		ps = append(ps, p)
	}
	return len(ps), ps
}

// ── Chat room (WebSocket) ─────────────────────────────────────

type chatSession struct {
	conn   *websocket.Conn
	player Player
}

type chatMessage struct {
	Type      string `json:"type"`
	UserID    string `json:"userId"`
	Name      string `json:"name"`
	Text      string `json:"text"`
	Timestamp int64  `json:"timestamp"`
}

type chatRoom struct {
	mu       sync.Mutex
	sessions map[string]*chatSession
	history  []chatMessage
}

func (r *chatRoom) add(s *chatSession) int {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.sessions[s.player.UserID] = s
	return len(r.sessions)
}

func (r *chatRoom) remove(userID string) (Player, int, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	s, ok := r.sessions[userID]
	if !ok {
		return Player{}, 0, false
	}
	delete(r.sessions, userID)
	return s.player, len(r.sessions), true
}

func (r *chatRoom) count() int {
	r.mu.Lock()
	defer r.mu.Unlock()
	return len(r.sessions)
}

func (r *chatRoom) playerSnapshot() []Player {
	r.mu.Lock()
	defer r.mu.Unlock()
	players := make([]Player, 0, len(r.sessions))
	for _, s := range r.sessions {
		players = append(players, s.player)
	}
	return players
}

func (r *chatRoom) appendHistory(msg chatMessage) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.history = append(r.history, msg)
	if len(r.history) > maxChatHistory {
		copy(r.history, r.history[len(r.history)-maxChatHistory:])
		r.history = r.history[:maxChatHistory]
	}
}

func (r *chatRoom) historySnapshot() []chatMessage {
	r.mu.Lock()
	defer r.mu.Unlock()
	history := make([]chatMessage, len(r.history))
	copy(history, r.history)
	return history
}

func (r *chatRoom) broadcast(v interface{}, excludeID string) {
	data, err := json.Marshal(v)
	if err != nil {
		errorfGlobal("broadcast marshal failed: %v", err)
		return
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	for uid, s := range r.sessions {
		if uid == excludeID {
			continue
		}
		if err := s.conn.WriteMessage(websocket.TextMessage, data); err != nil {
			errorfGlobal("broadcast write failed user=%s: %v", uid, err)
		}
	}
}

func (r *chatRoom) disband(code string) int {
	r.mu.Lock()
	sessions := make([]*chatSession, 0, len(r.sessions))
	for _, s := range r.sessions {
		sessions = append(sessions, s)
	}
	r.sessions = make(map[string]*chatSession)
	r.mu.Unlock()

	msg := map[string]interface{}{
		"type":     "disband",
		"roomCode": code,
		"message":  "房间已由房主解散",
	}
	for _, s := range sessions {
		_ = s.conn.WriteJSON(msg)
		_ = s.conn.Close()
	}
	return len(sessions)
}

// ── Server ────────────────────────────────────────────────────

type server struct {
	mu               sync.Mutex
	matchRooms       map[string]*matchRoom
	chatRooms        map[string]*chatRoom
	roomMetas        map[string]*roomMeta
	upgrader         websocket.Upgrader
	errLog           *log.Logger
	errorLogPath     string
	startedAt        time.Time
	totalRequests    atomic.Uint64
	inFlightRequests atomic.Int64
}

func newServer(errLog *log.Logger, errorLogPath string) *server {
	return &server{
		matchRooms:   make(map[string]*matchRoom),
		chatRooms:    make(map[string]*chatRoom),
		roomMetas:    make(map[string]*roomMeta),
		upgrader:     websocket.Upgrader{CheckOrigin: func(*http.Request) bool { return true }},
		errLog:       errLog,
		errorLogPath: strings.TrimSpace(errorLogPath),
		startedAt:    time.Now(),
	}
}

func (s *server) errorf(format string, args ...interface{}) {
	if s.errLog != nil {
		s.errLog.Printf(format, args...)
		return
	}
	log.Printf(format, args...)
}

func (s *server) getOrCreateMatchRoom(code string) *matchRoom {
	s.mu.Lock()
	defer s.mu.Unlock()
	if r, ok := s.matchRooms[code]; ok {
		return r
	}
	r := &matchRoom{players: make(map[string]Player)}
	s.matchRooms[code] = r
	return r
}

func (s *server) getOrCreateChatRoom(code string) *chatRoom {
	s.mu.Lock()
	defer s.mu.Unlock()
	if r, ok := s.chatRooms[code]; ok {
		return r
	}
	r := &chatRoom{sessions: make(map[string]*chatSession)}
	s.chatRooms[code] = r
	return r
}

func (s *server) cleanupRoomIfEmpty(code string) {
	s.mu.Lock()
	defer s.mu.Unlock()

	chatCount := 0
	if cr := s.chatRooms[code]; cr != nil {
		chatCount = cr.count()
	}

	if chatCount > 0 {
		return
	}

	delete(s.matchRooms, code)
	delete(s.chatRooms, code)
	delete(s.roomMetas, code)
	log.Printf("[%s] room reclaimed: no chat participants", code)
}

// ── HTTP handlers ─────────────────────────────────────────────

func writeJSON(w http.ResponseWriter, status int, v interface{}) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	enc := json.NewEncoder(w)
	enc.SetEscapeHTML(false)
	enc.Encode(v)
}

type statusRecorder struct {
	http.ResponseWriter
	status int
}

func (r *statusRecorder) WriteHeader(status int) {
	r.status = status
	r.ResponseWriter.WriteHeader(status)
}

func (r *statusRecorder) Write(data []byte) (int, error) {
	if r.status == 0 {
		r.status = http.StatusOK
	}
	return r.ResponseWriter.Write(data)
}

func (r *statusRecorder) Hijack() (net.Conn, *bufio.ReadWriter, error) {
	hijacker, ok := r.ResponseWriter.(http.Hijacker)
	if !ok {
		return nil, nil, fmt.Errorf("wrapped response writer does not support hijacking")
	}
	return hijacker.Hijack()
}

func (r *statusRecorder) Flush() {
	if flusher, ok := r.ResponseWriter.(http.Flusher); ok {
		flusher.Flush()
	}
}

func (s *server) handleJoin(w http.ResponseWriter, r *http.Request) {
	var body struct {
		Code            string `json:"code"`
		Name            string `json:"name"`
		RoomName        string `json:"roomName"`
		LimitPlayers    bool   `json:"limitPlayers"`
		MaxPlayers      int    `json:"maxPlayers"`
		AutoStartOnFull bool   `json:"autoStartOnFull"`
	}
	if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
		s.errorf("join invalid_json remote=%s error=%v", r.RemoteAddr, err)
		writeJSON(w, 400, map[string]string{"error": "Invalid JSON"})
		return
	}
	body.Code = strings.ToUpper(strings.TrimSpace(body.Code))
	body.Name = strings.TrimSpace(body.Name)

	if !roomCodeRe.MatchString(body.Code) {
		s.errorf("join invalid_room_code remote=%s code=%q name=%q", r.RemoteAddr, body.Code, body.Name)
		writeJSON(w, 400, map[string]string{"error": "房间码格式错误，需为 6 位大写字母或数字"})
		return
	}
	if body.Name == "" {
		s.errorf("join empty_name remote=%s code=%q", r.RemoteAddr, body.Code)
		writeJSON(w, 400, map[string]string{"error": "昵称不能为空"})
		return
	}
	if len([]rune(body.Name)) > maxNameLength {
		s.errorf("join name_too_long remote=%s code=%q name_len=%d", r.RemoteAddr, body.Code, len([]rune(body.Name)))
		writeJSON(w, 400, map[string]string{"error": fmt.Sprintf("昵称最长 %d 个字符", maxNameLength)})
		return
	}

	requestedMaxPlayers := body.MaxPlayers
	if requestedMaxPlayers <= 0 {
		requestedMaxPlayers = maxPlayersPerRoom
	}
	if requestedMaxPlayers > maxPlayersPerRoom {
		requestedMaxPlayers = maxPlayersPerRoom
	}

	s.mu.Lock()
	existingMeta := s.roomMetas[body.Code]
	effectiveMaxPlayers := maxPlayersPerRoom
	if existingMeta != nil {
		effectiveMaxPlayers = existingMeta.effectiveMaxPlayers()
	} else if body.LimitPlayers {
		effectiveMaxPlayers = requestedMaxPlayers
	}
	s.mu.Unlock()

	mr := s.getOrCreateMatchRoom(body.Code)
	player, err := mr.join(body.Name, effectiveMaxPlayers)
	if err != nil {
		s.errorf("join failed remote=%s code=%s name=%q error=%v", r.RemoteAddr, body.Code, body.Name, err)
		writeJSON(w, 403, map[string]interface{}{"error": "房间已满", "code": "ROOM_FULL"})
		return
	}

	count, _ := mr.snapshot()
	now := time.Now()
	isHost := false
	s.mu.Lock()
	if _, exists := s.roomMetas[body.Code]; !exists {
		name := strings.TrimSpace(body.RoomName)
		if name == "" {
			name = body.Code
		}
		s.roomMetas[body.Code] = &roomMeta{
			Code:            body.Code,
			Name:            name,
			HostName:        body.Name,
			HostUserID:      player.UserID,
			LimitPlayers:    body.LimitPlayers,
			MaxPlayers:      requestedMaxPlayers,
			AutoStartOnFull: body.AutoStartOnFull,
			ReportedCount:   count,
			CreatedAt:       now,
		}
		isHost = true
	} else if meta := s.roomMetas[body.Code]; meta != nil {
		if meta.CreatedAt.IsZero() {
			meta.CreatedAt = now
		}
		meta.ReportedCount = count
		if meta.HostUserID == player.UserID {
			isHost = true
		}
	}
	s.mu.Unlock()

	writeJSON(w, 200, map[string]interface{}{
		"userId":      player.UserID,
		"name":        player.Name,
		"roomCode":    body.Code,
		"playerCount": count,
		"isHost":      isHost,
		"maxPlayers":  effectiveMaxPlayers,
		"chatUrl":     buildChatURL(r, body.Code, player.UserID, player.Name),
	})
}

func (s *server) handleLeave(w http.ResponseWriter, r *http.Request) {
	var body struct {
		Code   string `json:"code"`
		UserID string `json:"userId"`
	}
	if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
		s.errorf("leave invalid_json remote=%s error=%v", r.RemoteAddr, err)
		writeJSON(w, 400, map[string]string{"error": "Invalid JSON"})
		return
	}
	body.Code = strings.ToUpper(strings.TrimSpace(body.Code))

	s.mu.Lock()
	mr, ok := s.matchRooms[body.Code]
	s.mu.Unlock()

	if !ok || !mr.leave(body.UserID) {
		s.errorf("leave user_not_found remote=%s code=%s userId=%s", r.RemoteAddr, body.Code, body.UserID)
		writeJSON(w, 404, map[string]string{"error": "用户不存在"})
		return
	}
	count, _ := mr.snapshot()
	s.mu.Lock()
	if meta := s.roomMetas[body.Code]; meta != nil {
		meta.ReportedCount = count
	}
	s.mu.Unlock()
	s.cleanupRoomIfEmpty(body.Code)
	writeJSON(w, 200, map[string]bool{"success": true})
}

func (s *server) handleDisband(w http.ResponseWriter, r *http.Request) {
	var body struct {
		Code   string `json:"code"`
		UserID string `json:"userId"`
		Name   string `json:"name"`
	}
	if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
		s.errorf("disband invalid_json remote=%s error=%v", r.RemoteAddr, err)
		writeJSON(w, 400, map[string]string{"error": "Invalid JSON"})
		return
	}
	body.Code = strings.ToUpper(strings.TrimSpace(body.Code))
	body.UserID = strings.TrimSpace(body.UserID)
	body.Name = strings.TrimSpace(body.Name)

	if !roomCodeRe.MatchString(body.Code) {
		s.errorf("disband invalid_room_code remote=%s code=%q userId=%s", r.RemoteAddr, body.Code, body.UserID)
		writeJSON(w, 400, map[string]string{"error": "房间码格式错误"})
		return
	}
	if body.UserID == "" {
		s.errorf("disband empty_user_id remote=%s code=%s", r.RemoteAddr, body.Code)
		writeJSON(w, 400, map[string]string{"error": "用户 ID 不能为空"})
		return
	}

	s.mu.Lock()
	meta, ok := s.roomMetas[body.Code]
	if !ok {
		s.mu.Unlock()
		s.errorf("disband room_not_found remote=%s code=%s userId=%s", r.RemoteAddr, body.Code, body.UserID)
		writeJSON(w, 404, map[string]string{"error": "房间不存在"})
		return
	}
	if meta.HostUserID == "" || meta.HostUserID != body.UserID {
		s.mu.Unlock()
		s.errorf("disband not_host remote=%s code=%s userId=%s hostUserId=%s", r.RemoteAddr, body.Code, body.UserID, meta.HostUserID)
		writeJSON(w, 403, map[string]string{"error": "只有房主可以解散房间", "code": "NOT_ROOM_HOST"})
		return
	}

	cr := s.chatRooms[body.Code]
	delete(s.roomMetas, body.Code)
	delete(s.matchRooms, body.Code)
	delete(s.chatRooms, body.Code)
	s.mu.Unlock()

	closed := 0
	if cr != nil {
		closed = cr.disband(body.Code)
	}
	log.Printf("[%s] room disbanded by %s (%s), closed %d session(s)", body.Code, meta.HostName, body.UserID, closed)
	writeJSON(w, 200, map[string]interface{}{"success": true, "roomCode": body.Code})
}

func (s *server) handleRoomInfo(w http.ResponseWriter, r *http.Request) {
	code := strings.ToUpper(strings.TrimPrefix(r.URL.Path, "/match/room/"))
	if !roomCodeRe.MatchString(code) {
		s.errorf("room_info invalid_room_code remote=%s code=%q", r.RemoteAddr, code)
		writeJSON(w, 400, map[string]string{"error": "房间码格式错误"})
		return
	}

	s.mu.Lock()
	mr, ok := s.matchRooms[code]
	cr := s.chatRooms[code]
	meta := s.roomMetas[code]
	s.mu.Unlock()

	var count int
	var players []Player
	if cr != nil {
		count = cr.count()
		players = cr.playerSnapshot()
	} else if meta != nil && meta.ReportedCount >= 0 {
		count = meta.ReportedCount
	}
	if count == 0 && ok {
		count, players = mr.snapshot()
	}
	if players == nil {
		players = []Player{}
	}
	writeJSON(w, 200, map[string]interface{}{
		"roomCode":        code,
		"playerCount":     count,
		"maxPlayers":      meta.effectiveMaxPlayers(),
		"limitPlayers":    meta != nil && meta.LimitPlayers,
		"autoStartOnFull": meta != nil && meta.AutoStartOnFull,
		"players":         players,
	})
}

func (s *server) handleReportRoomCount(w http.ResponseWriter, r *http.Request) {
	var body struct {
		Code   string `json:"code"`
		UserID string `json:"userId"`
		Count  int    `json:"count"`
	}
	if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
		s.errorf("report_count invalid_json remote=%s error=%v", r.RemoteAddr, err)
		writeJSON(w, 400, map[string]string{"error": "请求格式错误"})
		return
	}

	body.Code = strings.ToUpper(strings.TrimSpace(body.Code))
	if !roomCodeRe.MatchString(body.Code) {
		s.errorf("report_count invalid_room_code remote=%s code=%q", r.RemoteAddr, body.Code)
		writeJSON(w, 400, map[string]string{"error": "房间码格式错误"})
		return
	}
	if body.Count < 0 {
		body.Count = 0
	}
	if body.Count > maxPlayersPerRoom {
		body.Count = maxPlayersPerRoom
	}

	s.mu.Lock()
	meta := s.roomMetas[body.Code]
	if meta == nil {
		meta = &roomMeta{Code: body.Code, Name: body.Code, MaxPlayers: maxPlayersPerRoom, CreatedAt: time.Now()}
		s.roomMetas[body.Code] = meta
	} else if meta.CreatedAt.IsZero() {
		meta.CreatedAt = time.Now()
	}
	meta.ReportedCount = body.Count
	s.mu.Unlock()

	if body.Count == 0 {
		s.cleanupRoomIfEmpty(body.Code)
	}

	log.Printf("[%s] reported room count=%d userId=%s", body.Code, body.Count, body.UserID)
	writeJSON(w, 200, map[string]interface{}{"success": true, "roomCode": body.Code, "playerCount": body.Count})
}

func (s *server) handleListRooms(w http.ResponseWriter, r *http.Request) {
	// Snapshot metadata under lock, then query chatRooms without holding lock
	s.mu.Lock()
	type entry struct {
		code string
		meta *roomMeta
		cr   *chatRoom
	}
	entries := make([]entry, 0, len(s.roomMetas))
	for code, meta := range s.roomMetas {
		cr := s.chatRooms[code] // nil if no WS connections yet
		entries = append(entries, entry{code, meta, cr})
	}
	s.mu.Unlock()

	type roomResponse struct {
		Code            string `json:"code"`
		Name            string `json:"name"`
		HostName        string `json:"hostName"`
		HostUserID      string `json:"hostUserId"`
		PlayerCount     int    `json:"playerCount"`
		MaxPlayers      int    `json:"maxPlayers"`
		LimitPlayers    bool   `json:"limitPlayers"`
		AutoStartOnFull bool   `json:"autoStartOnFull"`
	}
	rooms := make([]roomResponse, 0, len(entries))
	staleCodes := make([]string, 0)
	now := time.Now()
	for _, e := range entries {
		var count int
		if e.cr == nil {
			withinJoinGrace := e.meta.ReportedCount > 0 &&
				!e.meta.CreatedAt.IsZero() &&
				now.Sub(e.meta.CreatedAt) < roomListGrace
			if withinJoinGrace {
				count = e.meta.ReportedCount
			} else {
				staleCodes = append(staleCodes, e.code)
				continue
			}
		} else {
			count = e.cr.count()
		}
		if count <= 0 {
			staleCodes = append(staleCodes, e.code)
			continue
		}
		rooms = append(rooms, roomResponse{
			Code:            e.meta.Code,
			Name:            e.meta.Name,
			HostName:        e.meta.HostName,
			HostUserID:      e.meta.HostUserID,
			PlayerCount:     count,
			MaxPlayers:      e.meta.effectiveMaxPlayers(),
			LimitPlayers:    e.meta.LimitPlayers,
			AutoStartOnFull: e.meta.AutoStartOnFull,
		})
	}
	for _, code := range staleCodes {
		s.cleanupRoomIfEmpty(code)
	}
	writeJSON(w, 200, rooms)
}

func (s *server) handleStats(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	type entry struct {
		meta *roomMeta
		mr   *matchRoom
		cr   *chatRoom
	}
	entries := make([]entry, 0, len(s.roomMetas))
	for code, meta := range s.roomMetas {
		entries = append(entries, entry{
			meta: meta,
			mr:   s.matchRooms[code],
			cr:   s.chatRooms[code],
		})
	}
	s.mu.Unlock()

	type roomStats struct {
		Code              string `json:"code"`
		Name              string `json:"name"`
		HostName          string `json:"hostName"`
		OnlinePlayers     int    `json:"onlinePlayers"`
		RegisteredPlayers int    `json:"registeredPlayers"`
		MaxPlayers        int    `json:"maxPlayers"`
		LimitPlayers      bool   `json:"limitPlayers"`
		AutoStartOnFull   bool   `json:"autoStartOnFull"`
		System            bool   `json:"system"`
	}

	rooms := make([]roomStats, 0, len(entries))
	onlinePlayers := 0
	registeredPlayers := 0
	for _, e := range entries {
		online := 0
		if e.cr != nil {
			online = e.cr.count()
		}
		registered := 0
		if e.mr != nil {
			registered = e.mr.count()
		}
		onlinePlayers += online
		registeredPlayers += registered
		rooms = append(rooms, roomStats{
			Code:              e.meta.Code,
			Name:              e.meta.Name,
			HostName:          e.meta.HostName,
			OnlinePlayers:     online,
			RegisteredPlayers: registered,
			MaxPlayers:        e.meta.effectiveMaxPlayers(),
			LimitPlayers:      e.meta.LimitPlayers,
			AutoStartOnFull:   e.meta.AutoStartOnFull,
			System:            e.meta.System,
		})
	}

	var mem runtime.MemStats
	runtime.ReadMemStats(&mem)
	now := time.Now()
	writeJSON(w, 200, map[string]interface{}{
		"timestamp":            now.Format(time.RFC3339),
		"uptimeSeconds":        int64(now.Sub(s.startedAt).Seconds()),
		"roomCount":            len(rooms),
		"onlinePlayers":        onlinePlayers,
		"registeredPlayers":    registeredPlayers,
		"inFlightRequests":     s.inFlightRequests.Load(),
		"totalRequests":        s.totalRequests.Load(),
		"goroutines":           runtime.NumGoroutine(),
		"memoryAllocBytes":     mem.Alloc,
		"memorySysBytes":       mem.Sys,
		"memoryHeapInuseBytes": mem.HeapInuse,
		"rooms":                rooms,
	})
}

func (s *server) handleDashboard(w http.ResponseWriter, r *http.Request) {
	path := "../web/dashboard.html"
	if _, err := os.Stat(path); err != nil {
		http.Error(w, "dashboard.html not found", http.StatusNotFound)
		return
	}
	http.ServeFile(w, r, path)
}

func (s *server) handleErrorLog(w http.ResponseWriter, r *http.Request) {
	path := strings.TrimSpace(s.errorLogPath)
	if path == "" {
		writeJSON(w, 200, map[string]interface{}{
			"configured": false,
			"exists":     false,
			"path":       "",
			"sizeBytes":  0,
			"truncated":  false,
			"content":    "",
		})
		return
	}

	file, err := os.Open(path)
	if err != nil {
		if os.IsNotExist(err) {
			writeJSON(w, 200, map[string]interface{}{
				"configured": true,
				"exists":     false,
				"path":       path,
				"sizeBytes":  0,
				"truncated":  false,
				"content":    "",
			})
			return
		}
		s.errorf("read_error_log_failed path=%q error=%v", path, err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "读取错误日志失败"})
		return
	}
	defer file.Close()

	info, err := file.Stat()
	if err != nil {
		s.errorf("stat_error_log_failed path=%q error=%v", path, err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "读取错误日志状态失败"})
		return
	}

	size := info.Size()
	offset := int64(0)
	truncated := false
	if size > maxErrorLogBytes {
		offset = size - maxErrorLogBytes
		truncated = true
	}

	if _, err := file.Seek(offset, io.SeekStart); err != nil {
		s.errorf("seek_error_log_failed path=%q error=%v", path, err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "读取错误日志失败"})
		return
	}

	content, err := io.ReadAll(file)
	if err != nil {
		s.errorf("read_error_log_failed path=%q error=%v", path, err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "读取错误日志失败"})
		return
	}

	writeJSON(w, 200, map[string]interface{}{
		"configured": true,
		"exists":     true,
		"path":       path,
		"sizeBytes":  size,
		"truncated":  truncated,
		"content":    string(content),
	})
}

// ── WebSocket handler ─────────────────────────────────────────

func (s *server) handleChat(w http.ResponseWriter, r *http.Request) {
	code := strings.ToUpper(strings.TrimPrefix(r.URL.Path, "/chat/"))
	if !roomCodeRe.MatchString(code) {
		s.errorf("chat invalid_room_code remote=%s code=%q", r.RemoteAddr, code)
		http.Error(w, "Invalid room code", 400)
		return
	}

	userID := r.URL.Query().Get("userId")
	name := r.URL.Query().Get("name")
	if userID == "" || name == "" {
		s.errorf("chat missing_identity remote=%s code=%s userId=%q name=%q", r.RemoteAddr, code, userID, name)
		http.Error(w, "Missing userId or name", 400)
		return
	}

	s.mu.Lock()
	meta := s.roomMetas[code]
	if meta == nil {
		meta = &roomMeta{Code: code, Name: code, MaxPlayers: maxPlayersPerRoom, CreatedAt: time.Now()}
		s.roomMetas[code] = meta
	} else if meta.CreatedAt.IsZero() {
		meta.CreatedAt = time.Now()
	}
	maxPlayers := meta.effectiveMaxPlayers()
	s.mu.Unlock()

	cr := s.getOrCreateChatRoom(code)
	if cr.count() >= maxPlayers {
		s.errorf("chat room_full remote=%s code=%s userId=%s", r.RemoteAddr, code, userID)
		http.Error(w, `{"error":"房间已满","code":"ROOM_FULL"}`, 403)
		return
	}

	conn, err := s.upgrader.Upgrade(w, r, nil)
	if err != nil {
		s.errorf("chat ws_upgrade_failed remote=%s code=%s userId=%s error=%v", r.RemoteAddr, code, userID, err)
		return
	}

	sess := &chatSession{conn: conn, player: Player{UserID: userID, Name: name}}
	count := cr.add(sess)
	players := cr.playerSnapshot()
	s.mu.Lock()
	if meta := s.roomMetas[code]; meta != nil {
		meta.ReportedCount = count
	}
	s.mu.Unlock()

	if err := conn.WriteJSON(map[string]interface{}{
		"type":     "welcome",
		"userId":   userID,
		"name":     name,
		"roomCode": code,
		"count":    count,
		"max":      maxPlayers,
		"players":  players,
	}); err != nil {
		s.errorf("chat welcome_write_failed code=%s userId=%s error=%v", code, userID, err)
	}
	if history := cr.historySnapshot(); len(history) > 0 {
		if err := conn.WriteJSON(map[string]interface{}{
			"type":     "history",
			"messages": history,
		}); err != nil {
			s.errorf("chat history_write_failed code=%s userId=%s error=%v", code, userID, err)
		}
	}
	cr.broadcast(map[string]interface{}{
		"type":   "join",
		"userId": userID,
		"name":   name,
		"count":  count,
	}, userID)

	log.Printf("[%s] %s joined (%d/%d)", code, name, count, maxPlayersPerRoom)

	defer func() {
		player, remaining, ok := cr.remove(userID)
		if ok {
			s.mu.Lock()
			if meta := s.roomMetas[code]; meta != nil {
				meta.ReportedCount = remaining
			}
			s.mu.Unlock()
			log.Printf("[%s] %s left (%d/%d)", code, player.Name, remaining, maxPlayersPerRoom)
			cr.broadcast(map[string]interface{}{
				"type":   "leave",
				"userId": player.UserID,
				"name":   player.Name,
				"count":  remaining,
			}, "")
		}
		conn.Close()
		s.cleanupRoomIfEmpty(code)
	}()

	for {
		_, raw, err := conn.ReadMessage()
		if err != nil {
			break
		}
		var msg struct {
			Type string `json:"type"`
			Text string `json:"text"`
		}
		if err := json.Unmarshal(raw, &msg); err != nil {
			s.errorf("chat invalid_message_json code=%s userId=%s error=%v", code, userID, err)
			if err := conn.WriteJSON(map[string]string{"type": "error", "code": "INVALID_MESSAGE", "message": "消息格式错误"}); err != nil {
				s.errorf("chat error_write_failed code=%s userId=%s error=%v", code, userID, err)
			}
			continue
		}
		if msg.Type != "chat" {
			continue
		}
		text := strings.TrimSpace(msg.Text)
		if text == "" || len([]rune(text)) > maxTextLength {
			s.errorf("chat invalid_message_content code=%s userId=%s text_len=%d", code, userID, len([]rune(text)))
			if err := conn.WriteJSON(map[string]string{"type": "error", "code": "INVALID_MESSAGE", "message": "消息内容不合法"}); err != nil {
				s.errorf("chat error_write_failed code=%s userId=%s error=%v", code, userID, err)
			}
			continue
		}
		chat := chatMessage{
			Type:      "chat",
			UserID:    userID,
			Name:      name,
			Text:      text,
			Timestamp: time.Now().UnixMilli(),
		}
		cr.appendHistory(chat)
		cr.broadcast(chat, "")
		log.Printf("[%s] %s: %s", code, name, text)
	}
}

// ── Router ────────────────────────────────────────────────────

func (s *server) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	start := time.Now()
	s.totalRequests.Add(1)
	s.inFlightRequests.Add(1)
	rec := &statusRecorder{ResponseWriter: w, status: http.StatusOK}
	defer func() {
		s.inFlightRequests.Add(-1)
		if recovered := recover(); recovered != nil {
			s.errorf("panic method=%s path=%s remote=%s error=%v\n%s",
				r.Method, r.URL.Path, r.RemoteAddr, recovered, debug.Stack())
			if !strings.HasPrefix(r.URL.Path, "/chat/") {
				http.Error(rec, "Internal Server Error", http.StatusInternalServerError)
			}
			return
		}

		if rec.status >= http.StatusInternalServerError {
			s.errorf("http_error method=%s path=%s remote=%s status=%d duration=%s",
				r.Method, r.URL.Path, r.RemoteAddr, rec.status, time.Since(start))
		}
	}()

	w = rec
	w.Header().Set("Access-Control-Allow-Origin", "*")
	w.Header().Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
	w.Header().Set("Access-Control-Allow-Headers", "Content-Type")
	if r.Method == http.MethodOptions {
		w.WriteHeader(204)
		return
	}

	p := r.URL.Path
	switch {
	case (p == "/" || p == "/dashboard" || p == "/dashboard.html") && r.Method == http.MethodGet:
		s.handleDashboard(w, r)
	case p == "/stats" && r.Method == http.MethodGet:
		s.handleStats(w, r)
	case p == "/logs/error" && r.Method == http.MethodGet:
		s.handleErrorLog(w, r)
	case p == "/rooms" && r.Method == http.MethodGet:
		s.handleListRooms(w, r)
	case p == "/match/join" && r.Method == http.MethodPost:
		s.handleJoin(w, r)
	case p == "/match/leave" && r.Method == http.MethodPost:
		s.handleLeave(w, r)
	case p == "/match/disband" && r.Method == http.MethodPost:
		s.handleDisband(w, r)
	case p == "/match/report-count" && r.Method == http.MethodPost:
		s.handleReportRoomCount(w, r)
	case strings.HasPrefix(p, "/match/room/") && r.Method == http.MethodGet:
		s.handleRoomInfo(w, r)
	case strings.HasPrefix(p, "/chat/"):
		s.handleChat(w, r)
	case p == "/health":
		writeJSON(w, 200, map[string]string{"status": "ok"})
	default:
		http.NotFound(w, r)
	}
}

// ── Helpers ───────────────────────────────────────────────────

func buildChatURL(r *http.Request, code, userID, name string) string {
	scheme := "ws"
	if r.TLS != nil || r.Header.Get("X-Forwarded-Proto") == "https" {
		scheme = "wss"
	}
	return fmt.Sprintf("%s://%s/chat/%s?userId=%s&name=%s",
		scheme, r.Host, code,
		url.QueryEscape(userID),
		url.QueryEscape(name))
}

func initErrorLogger(path string) (*os.File, *log.Logger, error) {
	if strings.TrimSpace(path) == "" {
		logger := log.New(os.Stderr, "ERROR ", log.LstdFlags|log.Lmicroseconds|log.Lshortfile)
		return nil, logger, nil
	}

	if err := os.MkdirAll(filepathDir(path), 0755); err != nil {
		return nil, nil, err
	}

	file, err := os.OpenFile(path, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0644)
	if err != nil {
		return nil, nil, err
	}

	writer := io.MultiWriter(os.Stderr, file)
	logger := log.New(writer, "ERROR ", log.LstdFlags|log.Lmicroseconds|log.Lshortfile)
	log.SetFlags(log.LstdFlags | log.Lmicroseconds)
	return file, logger, nil
}

func filepathDir(path string) string {
	idx := strings.LastIndexAny(path, `/\`)
	if idx <= 0 {
		return "."
	}
	return path[:idx]
}

// ── Entry point ───────────────────────────────────────────────

func main() {
	addr := flag.String("addr", ":8787", "listen address")
	errorLogPath := flag.String("error-log", "logs/error.log", "error log file path")
	flag.Parse()

	errorLogFile, errLog, err := initErrorLogger(*errorLogPath)
	if err != nil {
		log.Fatalf("failed to initialize error log %q: %v", *errorLogPath, err)
	}
	if errorLogFile != nil {
		defer errorLogFile.Close()
	}
	globalErrLog = errLog

	s := newServer(errLog, *errorLogPath)

	// Pre-seed lobby rooms so the list is never empty on a fresh start
	for _, m := range []roomMeta{
		{Code: "TEST01", Name: "测试房间", HostName: "系统", System: true},
	} {
		m := m
		s.roomMetas[m.Code] = &m
	}

	log.Printf("JBS server listening on %s", *addr)
	if err := http.ListenAndServe(*addr, s); err != nil {
		log.Fatal(err)
	}
}
