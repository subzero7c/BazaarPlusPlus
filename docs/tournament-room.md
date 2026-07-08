# 锦标赛房间匹配功能 — 项目文档

## 项目目标

为 BazaarPlusPlus（The Bazaar 游戏 BepInEx Mod）提供锦标赛房间匹配和房间内聊天功能。

- 玩家在游戏内通过 BazaarPlusPlus 设置面板进入锦标赛模式
- 房间由 The Bazaar 游戏本身提供（通过游戏 API 进行对局匹配），Mod 负责帮助玩家**创建房间**、**查看房间列表**、**在房间内聊天**
- 配套 Go 服务器负责房间信息管理和 WebSocket 实时聊天

---

## 目录结构

```
BazaarPlusPlus/
├── docs/
│   └── tournament-room.md          # 本文档
├── bazaarplusplus-mod/             # 主 BazaarPlusPlus Mod（C#/BepInEx）
│   └── src/BazaarPlusPlus/
│       └── Game/Settings/
│           ├── BppSettingsDockController.cs
│           └── BppSettingsDockController.Presentation.cs
├── bazzarplusplus-jbs-server-mod/  # 锦标赛聊天 Mod（独立 BepInEx 插件）
│   ├── web/
│   │   └── chat.html               # 浏览器调试客户端
│   └── src/BazaarPlusPlus.JbsServer/
│       ├── Plugin.cs               # BepInEx 插件入口
│       ├── JbsConfig.cs            # 配置（服务器地址等）
│       ├── Game/TournamentRoom/
│       │   ├── TournamentRoomPanel.cs           # 房间列表 + 聊天 UI（游戏内）
│       │   └── TournamentSettingsDockRowController.cs  # 设置面板入口行
│       ├── Infrastructure/
│       │   └── TournamentChatClient.cs          # WebSocket 聊天客户端
│       └── Patches/
│           └── JbsSettingsButtonPatch.cs        # Harmony 补丁
└── preview/                        # Unity Editor 预览工程（仅用于开发）
    └── settings-ui-preview/
        └── Assets/SettingsDockPreview/
            ├── TournamentRoomPanelPreview.cs    # 预览版 UI（需与 Mod 保持同步）
            └── SettingsDockPreview.cs

# Go 服务器
bazzarplusplus-jbs-server-mod/jbs-server/
├── main.go       # 服务器全部逻辑
├── go.mod
├── go.sum
└── start.sh      # 启动脚本（自动释放端口，需手动重新编译后运行）
```

---

## 技术架构

### 服务器（Go）

| 端点 | 方法 | 说明 |
|------|------|------|
| `/rooms` | GET | 返回当前所有房间列表（含在线人数） |
| `/match/join` | POST | 加入或创建房间，返回 WebSocket 地址 |
| `/match/leave` | POST | 离开房间 |
| `/match/room/:code` | GET | 获取单个房间信息 |
| `/chat/:code` | WS | WebSocket 聊天连接 |
| `/health` | GET | 健康检查 |

**WebSocket 消息协议：**

```jsonc
// 服务器 → 客户端
{ "type": "welcome", "userId": "...", "roomCode": "...", "count": 1, "max": 30 }
{ "type": "history", "messages": [
  { "type": "chat", "userId": "...", "name": "...", "text": "...", "timestamp": 0 }
] }
{ "type": "join",    "userId": "...", "name": "...", "count": 2 }
{ "type": "leave",   "userId": "...", "name": "...", "count": 1 }
{ "type": "chat",    "userId": "...", "name": "...", "text": "...", "timestamp": 0 }
{ "type": "error",   "code": "...",   "message": "..." }

// 客户端 → 服务器
{ "type": "chat", "text": "..." }
```

**加入流程：**
1. `POST /match/join { code, name, roomName }` → 获取 `chatUrl`
2. 连接 `chatUrl`（WebSocket，含 `userId` 和 `name` 查询参数）

### Mod（C#/BepInEx）

- **框架**：BepInEx 5 + HarmonyLib
- **UI**：Unity UGUI（TextMeshPro）
- **HTTP 客户端**：`System.Net.Http.HttpClient`
- **WebSocket 客户端**：`System.Net.WebSockets.ClientWebSocket`
- **线程安全**：异步结果通过 `_pendingRooms`/`_pendingRoomRefresh` 标志在 `Update()` 主线程应用

### Unity 预览工程

- 路径：`bazaarplusplus-mod/preview/settings-ui-preview/`
- 使用 `UnityEngine.UI`（非 TMPro，因为是纯 Editor 工程）
- 房间数据从 `http://localhost:8787/rooms` 实时拉取（`UnityWebRequest`），不使用写死数据
- **必须与 Mod 的布局常量、交互逻辑保持一致**，见下方同步规范

---

## 本地化规范

> 所有面向用户的文本**禁止硬编码**，统一通过本地化系统管理。

### 支持语言
- `zh-CN`：简体中文（默认）
- `en-US`：英文

### 目录结构

```
bazzarplusplus-jbs-server-mod/
└── locales/
    ├── zh-CN.json
    └── en-US.json
```

构建时自动复制到游戏插件目录：
```
$(GamePath)/BepInEx/plugins/JbsServer/locales/
```

运行时通过 `JbsLocalization.Get(key, args...)` 访问，语言由 `PlayerPreferences.Data.LanguageCode` 决定，不识别时默认 zh-CN。

### 文件格式（JSON）

```json
{
  "tournament.title": "锦标赛房间匹配",
  "tournament.status.loading": "加载中…",
  "tournament.status.room_count": "共 {0} 个房间可用",
  "tournament.room.host": "房主：{0}",
  "tournament.chat.system_joined": "{0} 加入了房间",
  "tournament.chat.system_left": "{0} 离开了房间",
  "tournament.error.connect_failed": "无法连接服务器：{0}"
}
```

### 使用规范

- 占位符使用 `{0}`、`{1}` 格式（对应 `string.Format` 参数）
- 语言根据游戏 `PlayerPreferences.Data.LanguageCode` 自动切换
- 默认语言为简体中文

> **当前状态**：本地化系统已实现并接入主要 UI 文案。后续新增面向用户的文案仍需同步更新 `zh-CN.json` 和 `en-US.json`。

---

## 预览与 Mod 同步规范

开发过程中，**Unity 预览界面必须与游戏 Mod 保持一致**：

| 需同步的内容 | Mod 文件 | 预览文件 |
|-------------|----------|----------|
| 布局常量（`HeaderHeight` 等） | `TournamentRoomPanel.cs` | `TournamentRoomPanelPreview.cs` |
| 房间行结构（名称/房主/人数） | `TournamentRoomPanel.cs` | `TournamentRoomPanelPreview.cs` |
| 聊天消息格式 | `TournamentRoomPanel.cs` | `TournamentRoomPanelPreview.cs` |
| 服务器 API 地址 | `JbsConfig.cs` (默认 `ws://localhost:8787`) | `TournamentRoomPanelPreview.cs` (常量 `ServerHttpUrl`) |

**同步检查点**：每次修改 Mod UI 后，同步更新预览文件，在 Unity Editor 中确认视觉效果正确后再提交。

---

## 服务器运维

```bash
# 首次构建并启动
cd bazzarplusplus-jbs-server-mod/jbs-server
go build -o jbs-server .
bash start.sh

# 有代码修改后，需手动重新编译
go build -o jbs-server . && bash start.sh

# 查看普通日志和错误日志
tail -f server.log
tail -f logs/error.log

# 验证房间列表
curl http://localhost:8787/rooms
```

> `start.sh` 会自动释放 8787 端口，但**不会**自动重新编译，修改 `main.go` 后需先 `go build`。

服务端支持 `-error-log` 参数，默认写入 `logs/error.log`。HTTP 4xx/5xx、WebSocket 握手/写入失败、无效消息、panic recover 都会写入错误日志。

---

## Mod 构建与部署

```bash
# 构建（Debug 模式自动复制 DLL 到游戏 BepInEx/plugins/）
cd /Users/starunion/zhanzhang/BazaarPlusPlus
~/.dotnet/dotnet build bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/BazaarPlusPlus.JbsServer.csproj

# 部署生效需重启游戏
```

**DLL 输出路径：**
```
bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/bin/Debug/netstandard2.1/BazaarPlusPlus.JbsServer.dll
```

**游戏插件路径（macOS）：**
```
~/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/plugins/BazaarPlusPlus.JbsServer.dll
```

---

## 待办与已知问题

| 状态 | 项目 |
|------|------|
| ✅ | 实现本地化系统（JSON 配置文件 + 读取器） |
| ✅ | JBS server mod 可独立构建，不再编译期依赖主 Mod 输出 DLL |
| ✅ | `/rooms`、`/match/join`、WebSocket 消息使用结构化 JSON 解析 |
| ✅ | WebSocket 接收支持分片消息 |
| ✅ | 玩家列表按 `userId` 维护，同名玩家不会被合并 |
| ✅ | 离开房间调用 `/match/leave` |
| ✅ | 创建房间等待服务端确认后再进入聊天室 |
| ✅ | 游戏内支持手动输入房间码加入 |
| ⬜ | 房间数据持久化（服务器重启后预置房间恢复） |
| ✅ | 聊天历史（晚加入的玩家补发最近 50 条消息） |
| ⬜ | `start.sh` 支持检测源码变动后自动重新编译 |
| ⬜ | 服务器支持通过配置文件设置预置房间 |
| ✅ | 玩家名使用真实游戏账号名（反射调用 `BppClientCacheBridge`） |
| ✅ | 房间在线人数使用 WebSocket 实际连接数 |
| ✅ | 预览界面从服务器实时拉取房间数据 |

---

## 开放问题（待确认）

1. **本地化支持语言范围**：中文 + 英文，还是跟随游戏支持的全部语言？
2. **房间持久化**：服务器重启是否需要恢复房间，还是清空是预期行为？
3. **聊天历史**：晚加入的玩家是否能看到历史消息？需要多少条？
4. **房间码冲突**：当前为随机 6 位码，是否需要防碰撞校验？
