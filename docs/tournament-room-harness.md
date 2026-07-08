# 锦标赛房间与聊天室 Harness 文档

## 目标

本文档定义一个面向工程开发的测试 harness，用来验证 BazaarPlusPlus 锦标赛房间功能的三条核心链路：

- 聊天室：WebSocket 连接、收发消息、历史消息、玩家加入/离开、解散房间通知。
- 创建锦标赛房间：输入房间名和游戏内房间码，向服务端注册房间，确认成功后进入聊天室。
- 加入锦标赛房间：从房间列表加入，或通过房间码加入，并进入对应聊天室。

Harness 的目的不是替代真实游戏集成测试，而是提供一个可控、可复现、可断言的开发环境，让后续需求可以先在协议和 UI 状态层面闭环，再进入游戏内人工验证。

## 当前实现边界

当前功能涉及三个运行面：

| 面 | 位置 | 职责 |
|---|---|---|
| 游戏内 UI | `bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/Game/TournamentRoom/TournamentRoomPanel.cs` | 房间列表、创建房间弹窗、加入房间、聊天室、玩家列表、离开/解散按钮 |
| Mod 网络客户端 | `bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/Infrastructure/TournamentChatClient.cs` | `POST /match/join`、WebSocket 连接、聊天消息解析、解散房间 |
| 调试 Web 客户端 | `bazzarplusplus-jbs-server-mod/web/chat.html` | 浏览器侧手动验证房间列表、手动加入和聊天室 |
| Go 服务端 | `bazzarplusplus-jbs-server-mod/jbs-server/main.go` | 房间元数据、加入/离开/解散接口、WebSocket 广播、聊天历史 |

当前实现状态：

- 游戏内创建房间会先通过 `POST /match/join` 等待服务端返回 `chatUrl`，成功后才切到聊天室。Harness 应继续验证“服务端失败时不能停留在假聊天室”。
- 游戏内离开房间会调用 `POST /match/leave`，随后关闭 WebSocket 并清理本地状态。Harness 应验证服务端房间人数和列表状态同步。
- 浏览器调试页和游戏内 UI 都支持手动输入 6 位房间码加入。

## 被测协议

### HTTP

`GET /rooms`

返回当前房间列表：

```json
[
  {
    "code": "ABC123",
    "name": "练习赛",
    "hostName": "Alice",
    "hostUserId": "u_1",
    "playerCount": 1,
    "maxPlayers": 30
  }
]
```

`POST /match/join`

创建房间和加入房间共用同一接口，通过 `isHost` 响应字段区分身份。`roomName` 字段的语义：
- 创建房间时为新房间的名称，由客户端提供；
- 加入已有房间时，客户端传入从 `/rooms` 列表里读取的房间名（用于服务端校验或日志），服务端不应以请求体 `roomName` 覆盖已有房间名。

请求：

```json
{
  "code": "ABC123",
  "name": "Alice",
  "roomName": "练习赛"
}
```

成功响应：

```json
{
  "userId": "u_1",
  "name": "Alice",
  "roomCode": "ABC123",
  "playerCount": 1,
  "isHost": true,
  "chatUrl": "ws://localhost:8787/chat/ABC123?userId=u_1&name=Alice"
}
```

`POST /match/leave`

请求：

```json
{
  "code": "ABC123",
  "userId": "u_1"
}
```

成功响应：

```json
{ "success": true }
```

`POST /match/disband`

请求：

```json
{
  "code": "ABC123",
  "userId": "u_1",
  "name": "Alice"
}
```

成功响应：

```json
{ "success": true, "roomCode": "ABC123" }
```

### WebSocket

客户端发送：

```json
{ "type": "chat", "text": "hello" }
```

服务端发送：

```json
{ "type": "welcome", "userId": "u_1", "name": "Alice", "roomCode": "ABC123", "count": 1, "max": 30, "isHost": true }
{ "type": "history", "messages": [{ "type": "chat", "userId": "u_1", "name": "Alice", "text": "hello", "timestamp": 0 }] }
{ "type": "join", "userId": "u_2", "name": "Bob", "count": 2 }
{ "type": "leave", "userId": "u_2", "name": "Bob", "count": 1 }
{ "type": "chat", "userId": "u_1", "name": "Alice", "text": "hello", "timestamp": 0 }
{ "type": "disband", "roomCode": "ABC123", "message": "房间已解散" }
{ "type": "error", "code": "INVALID_MESSAGE", "message": "消息内容不合法" }
```

> **说明**：`isHost` 字段在 `welcome` 消息中携带，是客户端判断是否显示解散按钮的依据。`TournamentChatClient.IsCurrentPlayerHost` 应从 `welcome.isHost` 更新，而不依赖 HTTP 响应的缓存。

## Harness 组成

### 1. FakeTournamentServer

用于替代真实 Go 服务端，提供可编程响应。

建议能力：

- 监听本地随机端口，暴露和真实服务端一致的 HTTP/WS 协议。
- 支持预置房间列表。
- 支持配置 `join` 成功、房间满员、无效房间码、服务端 500、返回缺失 `chatUrl` 等失败分支。
- 支持记录收到的 HTTP 请求，供测试断言；每次 `StartAsync` 或调用 `ResetRequests()` 后清零。
- 支持主动推送 `welcome`、`history`、`join`、`leave`、`chat`、`disband`、`error` 消息。
- 支持 WebSocket 分片消息，用于验证 `TournamentChatClient` 的分片聚合逻辑。

最小接口草案：

```csharp
using var server = await FakeTournamentServer.StartAsync();

server.SeedRoom(new HarnessRoom("ABC123", "练习赛", "Alice", 1, 30));
server.WhenJoin("ABC123").ReturnsSuccess(userId: "u_1", isHost: true);
server.WhenJoin("FULL01").ReturnsRoomFull();
server.WhenJoin("ERR500").ReturnsError(500, "SERVER_ERROR");

// 主动推送分片消息（每次推送按 fragmentSize 字节切分发送）
await server.PushFragmented("ABC123", payload: "{\"type\":\"chat\",\"userId\":\"u_2\",\"name\":\"Bob\",\"text\":\"hi\",\"timestamp\":0}", fragmentSize: 5);

// 断言
Assert.Contains(server.Requests, r => r.Path == "/match/join");

// 测试间隔离
server.ResetRequests();
```

**请求记录生命周期**：`server.Requests` 在 `StartAsync` 时为空；调用 `ResetRequests()` 后清零；fake server 实例在 `using` 块结束时销毁。同一个 fake server 实例跨多个场景复用时，每个场景开始前应调用 `ResetRequests()`。

### 2. TournamentChatClientHarness

用于直接测试 `TournamentChatClient`，不依赖 Unity UI。

可控输入：

- `wsBaseUrl`
- `roomCode`
- `playerName`
- `roomName`
- 服务端返回体和 WebSocket 消息序列

可观察输出：

- `ConnectAsync` 返回值
- `CurrentUserId`
- `IsCurrentPlayerHost`
- `IsConnected`
- `WaitForMessageAsync(predicate, timeout)` 异步等待队列中匹配的消息
- Fake 服务端收到的聊天发送 payload

> **注意**：不使用同步轮询的 `TryDequeue`，改用 async 等待接口，避免测试代码中出现 `Task.Delay` 轮询。示例：
> ```csharp
> var msg = await harness.WaitForMessageAsync(m => m.Type == "chat" && m.Text == "hello", timeout: TimeSpan.FromSeconds(2));
> Assert.NotNull(msg);
> ```

核心断言：

- `ConnectAsync` 必须先调用 `/match/join`，再连接返回的 `chatUrl`。
- 缺失 `chatUrl` 时返回 `false`，并入队本地化错误消息。
- `welcome` 应更新当前玩家加入事件、人数和 `IsCurrentPlayerHost`。
- `history` 应展开为多条 `ChatMessage.Chat`。
- `join`/`leave` 应保留 `userId`，避免同名玩家被错误合并。
- 发送聊天时 payload 必须是 `{ "type": "chat", "text": "..." }`。
- WebSocket 关闭或异常时应入队断开/连接错误消息。

### 3. TournamentRoomPanelHarness

用于测试游戏内 UI 状态机，尽量把 Unity 对象创建、按钮点击和输入框操作封装起来。

建议能力：

- 在测试场景里创建一个 Canvas，并挂载 `TournamentRoomPanel`。
- 用 fake server 地址覆盖 `JbsConfig.ServerWsUrl`。如果配置项不方便直接覆盖，建议增加一个内部可注入的 server URL provider。
- 提供高层操作方法：
  - `OpenPanel()`
  - `RefreshRooms()`
  - `Search("练习")`
  - `ClickCreateRoom()`
  - `FillCreateRoom(name, code)`
  - `ConfirmCreateRoom()`
  - `SelectRoom(code)`
  - `JoinSelectedRoom()`
  - `JoinByCode(code)` — 通过房间码直接加入（对应游戏内手动加入入口，待 UI 补齐后接入）
  - `SendChat("hello")`
  - `LeaveRoom()`
  - `DisbandRoom()`
  - `WaitForViewAsync(view, timeout)` — 等待 UI 切换到指定视图
- 提供状态读取方法：
  - `CurrentView`
  - `StatusText`
  - `RoomRows`
  - `CurrentRoomCode`
  - `ChatMessages`
  - `PlayerRows`
  - `IsDisbandVisible`

最小接口草案：

```csharp
using var server = await FakeTournamentServer.StartAsync();
using var h = await TournamentRoomPanelHarness.OpenAsync(server);

server.SeedRoom(new HarnessRoom("ABC123", "练习赛", "Alice", 1, 30));

await h.RefreshRooms();
h.JoinByCode("ABC123");
await h.WaitForViewAsync(PanelView.Chat, timeout: TimeSpan.FromSeconds(3));

h.SendChat("hello");
await h.WaitForMessageAsync("Alice: hello");
```

## 核心场景

### 场景 A：房间列表加载

输入：

- Fake server 返回两个房间。

操作：

1. 打开面板。
2. 触发刷新房间列表。

断言：

- UI 状态文本显示房间数量。
- 房间行包含名称、房间码、房主、人数。
- 空列表提示隐藏。
- 请求路径为 `GET /rooms`。

失败分支：

- `/rooms` 超时或返回错误时，列表为空，不崩溃，状态不应显示过期房间数量。

### 场景 B：搜索房间

输入：

- 房间 `ABC123 / 练习赛`
- 房间 `XYZ789 / 决赛桌`

操作：

1. 输入搜索词 `练习`。

断言：

- 只显示 `ABC123`。
- 状态显示搜索结果数量。
- 清空搜索后恢复完整列表。

### 场景 C：创建房间成功

输入：

- 房间名：`练习赛`
- 房间码：`ABC123`
- Fake server 对 `/match/join` 返回 `isHost: true` 和有效 `chatUrl`。
- WebSocket 返回 `welcome`（含 `isHost: true`）。

操作：

1. 点击创建房间。
2. 填入房间名和 6 位房间码。
3. 确认创建。
4. 等待聊天连接。

断言：

- 必须发送 `POST /match/join`，body 包含 `code`、`name`、`roomName`。
- 成功后进入聊天室。
- 房间标题显示房间名、人数、房间码。
- 本玩家被加入玩家列表。
- `welcome.isHost == true` 时显示解散按钮。
- `BppTournamentRoomBridgeClient.EnterRoom(roomCode, roomName)` 被调用，或通过可观测替身记录进入房间状态。

回归重点：

- Harness 应把“连接失败仍进入聊天室”的情况作为失败测试，防止服务端确认前进入聊天室的行为回归。

### 场景 D：创建房间输入校验

输入：

- 空房间名。
- 非 6 位房间码。
- 包含非法字符的房间码。
- 小写字母房间码（应被规范化为大写）。

操作：

1. 打开创建弹窗。
2. 填入非法值并确认。

断言：

- 不发送 `/match/join`。
- 不进入聊天室。
- 状态文本显示对应错误。
- 房间码应被规范化为大写字母/数字，非法字符不应通过。

### 场景 E：创建房间服务端失败

输入：

- `/match/join` 返回 403 `ROOM_FULL`、400 房间码错误、500 服务端错误，或 200 但缺失 `chatUrl`。

操作：

1. 填入合法房间名和房间码。
2. 确认创建。

断言：

- UI 不应停留在已连接聊天室状态。
- 不应调用 `EnterRoom`。
- 聊天区显示可理解的错误消息。
- 房间列表可刷新，且不会出现本地假房间残留。

### 场景 F：从房间列表加入成功

输入：

- `/rooms` 返回 `ABC123`。
- `/match/join` 返回 `isHost: false` 和有效 `chatUrl`。
- WebSocket 返回 `welcome`（含 `isHost: false`）。

操作：

1. 刷新房间列表。
2. 点击房间行加入按钮。
3. 等待聊天连接。

断言：

- 发送 `POST /match/join`，`roomName` 为列表里的房间名。
- 成功后进入聊天室。
- 解散按钮不可见（`welcome.isHost == false`）。
- 聊天消息队列出现连接成功系统消息。
- 玩家列表包含当前玩家，且人数跟 `welcome.count` 对齐。

### 场景 G：通过房间码加入

输入：

- 房间码：`ABC123`
- Fake server 允许加入不在 `/rooms` 列表中的房间。

操作：

1. 调用 harness 的 `JoinByCode("ABC123")`。

断言：

- 发送 `POST /match/join`。
- 成功后进入聊天室。
- 若服务端返回房间元数据，应以服务端数据刷新当前房间标题。

当前实现：

- Web 调试页和游戏内 UI 都支持手动输入房间码加入。该场景应同时纳入 Web harness 和 `TournamentRoomPanelHarness`。

### 场景 H：聊天消息收发

输入：

- 已连接房间。
- 当前玩家 `Alice / u_1`。
- 另一个玩家 `Bob / u_2`。

操作：

1. Alice 发送 `hello`。
2. Fake server 广播 Alice 的 `chat`。
3. Fake server 推送 Bob 的 `chat`。

断言：

- 客户端发送 payload 为 `{ "type": "chat", "text": "hello" }`。
- Alice 自己的消息通过服务端回声出现，不应本地重复 append。
- Bob 消息显示为 `Bob: ...`。
- 空消息不发送。
- 超长或非法消息收到 `error` 后显示系统错误。

### 场景 I：历史消息

输入：

- 新玩家连接后服务端发送 `history`，包含多条 `chat`。

操作：

1. 加入房间。
2. Fake server 在 `welcome` 后发送 `history`。

断言：

- 每条历史消息都被展开显示。
- 历史消息不会错误地改变玩家列表。
- 历史消息数量超过 UI 上限时，最多保留 `MaxChatMessages` 条。

### 场景 I-2：重连后历史消息不重复

输入：

- 已连接房间，收到过一批历史消息。
- Fake server 断开后重新允许连接。

操作：

1. Fake server 主动断开 WebSocket。
2. 客户端重新发起 `ConnectAsync`。
3. Fake server 再次发送相同内容的 `history`。

断言：

- 聊天消息列表不出现重复条目（历史消息只显示一份）。
- 重连后玩家列表重置为服务端当前状态，而不是本地叠加。

### 场景 J：玩家加入、离开和同名用户

输入：

- `join`：`userId=u_2,name=Alex`
- `join`：`userId=u_3,name=Alex`
- `leave`：`userId=u_2,name=Alex`

操作：

1. 依次推送两条同名加入。
2. 推送其中一个用户离开。

断言：

- 玩家列表里同名玩家不会被合并。
- 离开 `u_2` 后，`u_3` 仍保留。
- 顶部人数跟 `count` 字段一致。

### 场景 K：离开房间

输入：

- 当前玩家已加入 `ABC123`。

操作：

1. 点击离开房间。

断言：

- WebSocket 被正常关闭。
- UI 回到房间列表。
- 本地当前房间状态清空。
- `BppTournamentRoomBridgeClient.LeaveRoom()` 被调用。
- 服务端房间列表人数刷新。
- 目标行为：发送 `POST /match/leave`，body 包含 `code` 和 `userId`。

回归重点：

- Harness 应把 `/match/leave` 请求作为目标断言，防止离开房间退化成只关闭 WebSocket。

#### 场景 K-2：非正常退出（WebSocket 直接断开，无 leave 调用）

输入：

- 当前玩家已加入 `ABC123`。
- 模拟游戏崩溃或强制关闭：直接断开 WebSocket，不调用 `/match/leave`。

操作：

1. Fake server 记录 WebSocket 连接。
2. 在不调用 `LeaveRoom()` 的情况下直接关闭连接。
3. 等待服务端心跳/超时清理。

断言：

- 服务端在心跳超时后应将该玩家从房间移除。
- 服务端移除后，其他玩家收到 `leave` 消息，且 `count` 正确递减。
- 验证服务端配置的超时时长在可接受范围内（建议文档化具体值，例如 30s）。

> **注意**：此场景依赖服务端心跳机制，需要 fake server 支持模拟超时触发。

### 场景 L：房主解散房间

输入：

- 当前玩家是房主。
- `/match/disband` 成功。

操作：

1. 点击解散房间。

断言：

- 发送 `POST /match/disband`，包含 `code`、`userId`、`name`。
- 服务端删除房间。
- 房主 UI 回到房间列表。
- 非房主客户端收到 `disband` 后退出聊天室并显示房间已解散状态。
- 非房主不显示解散按钮。

### 场景 M：连接异常和重试

输入：

- `/match/join` 超时。
- WebSocket 握手失败。
- WebSocket 连接中断。

操作：

1. 尝试加入房间。
2. 或在已连接状态断开 fake server。

断言：

- UI 显示连接失败或已断开。
- 不调用 `EnterRoom`。
- 输入框禁用或发送无效。
- 可返回房间列表并重新加入。

### 场景 N：房间码大小写规范化（服务端容错）

输入：

- 客户端发送小写房间码 `abc123`。

断言：

- 若服务端做大写规范化（容错）：`/match/join` 成功，返回的 `roomCode` 为 `ABC123`。
- 若服务端严格校验：返回 400 错误，客户端显示错误提示。

> 当前服务端行为待确认，Harness 应覆盖两种分支之一，并与服务端实现对齐后固化为明确断言。

### 场景 O：WebSocket 分片消息聚合

输入：

- Fake server 把一条完整的 JSON 消息按 `fragmentSize=5` 字节切分后分多帧发送。

操作：

1. 加入房间。
2. Fake server 调用 `PushFragmented` 推送切分后的 `chat` 消息。

断言：

- 客户端正确聚合分片，解析出完整 `chat` 消息。
- 消息内容与原始 payload 一致，不出现截断或重复。

## 推荐测试分层

| 层级 | 范围 | 价值 |
|---|---|---|
| 协议单元测试 | `TournamentChatClient` + `FakeTournamentServer` | 快速覆盖 join、WS 消息、错误处理、分片消息 |
| UI harness 测试 | `TournamentRoomPanel` + fake Canvas + fake server | 验证按钮、输入、状态文本、玩家列表、聊天室切换 |
| Web 调试 harness | `web/chat.html` + fake/真实 server | 验证浏览器调试页和协议兼容性 |
| 真实集成验证 | Go server + 游戏 + BepInEx 插件 | 最终确认真实环境、反射玩家名、Canvas 挂载、输入焦点 |

## 实现里程碑

### 里程碑 1：协议单元测试（优先）

覆盖场景 C、E、H、I、I-2、O。完成条件：`TournamentChatClient` + `FakeTournamentServer` 可独立运行，无 Unity 依赖。

### 里程碑 2：UI Harness 基础流程

覆盖场景 A、B、C、D、F、G、K、L。完成条件：`TournamentRoomPanelHarness` 可在 Unity Test Runner 中运行，假 Canvas 可挂载。

### 里程碑 3：异常和边界场景

覆盖场景 J、K-2、M、N。完成条件：fake server 支持 `ResetRequests()`、`PushFragmented`、超时模拟；回归测试能稳定捕获协议闭环退化。

## 可观测性要求

为了让 harness 稳定，建议为当前代码增加少量测试缝隙：

- `TournamentChatClient` 支持注入 HTTP/WebSocket transport，或至少支持注入 base URL 和 fake server。
- `TournamentRoomPanel` 支持在测试中读取当前视图、状态文本、房间行、聊天消息、玩家列表。
- `BppTournamentRoomBridgeClient` 支持替换为可记录调用的接口，便于断言 `EnterRoom` 和 `LeaveRoom`。
- 创建房间流程应把"服务端确认成功"作为进入聊天室的前置条件。
- 离开房间流程应显式调用 `/match/leave`，并把失败显示为可理解状态。
- `IsCurrentPlayerHost` 应从 `welcome.isHost` 驱动，而不从 HTTP 响应缓存。

## 运行方式

> 本节为占位，待 harness 工程落地后补充具体命令。

协议单元测试（无 Unity 依赖）：

```bash
# 在 bazzarplusplus-jbs-server-mod 目录下
dotnet test --filter "Category=Protocol"
```

Unity UI harness 测试：

- 在 Unity Editor 中打开 Test Runner（Window → General → Test Runner）。
- 切换到 PlayMode Tests。
- 运行 `TournamentRoom` 测试集。

CI 集成：

- 协议单元测试应纳入 CI，每次 PR 自动运行。
- Unity harness 测试建议在本地或专用 Unity CI 环境运行，不要求在每次 PR 中全量执行。

## 完成标准

Harness 完成后，应能稳定覆盖以下结论：

- 房间列表来自服务端，不依赖写死数据。
- 创建房间必须经过服务端确认，失败不会进入假聊天室。
- 加入房间必须经过 `/match/join` 并连接返回的 `chatUrl`。
- 聊天消息由服务端回声驱动，不本地重复显示。
- 历史消息、同名玩家、人数更新、WebSocket 分片都能正确处理。
- 重连后历史消息不重复显示，玩家列表以服务端状态为准。
- 离开和解散会同步服务端状态；非正常断开由服务端心跳兜底。
- 游戏内 UI、浏览器调试页和 Go 服务端协议保持一致。
