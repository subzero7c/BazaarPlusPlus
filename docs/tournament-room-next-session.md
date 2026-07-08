# 锦标赛 Mod 下一次工作清单

本文档用于下一次会话直接接手锦标赛房间功能收口工作。当前状态是：UI 骨架、基础配置、本地化、房间列表、加入房间和 WebSocket 聊天链路已经有实现；JBS server mod 已可独立构建，JSON 解析、WebSocket 分片接收和基于 userId 的玩家状态已经补齐。协议闭环和部分入口整理还需要继续。

## 当前已实现

- 设置面板和战斗菜单入口已接入，入口补丁位于 `bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/Patches/JbsSettingsButtonPatch.cs`。
- 游戏内房间面板已包含房间列表、搜索、创建房间弹窗、手动房间码加入、加入房间、聊天室、玩家列表、复制房间 ID、离开房间等 UI，主要位于 `bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/Game/TournamentRoom/TournamentRoomPanel.cs`。
- JBS server mod Release 构建已通过：`~/.dotnet/dotnet build bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/BazaarPlusPlus.JbsServer.csproj -c Release --no-restore`。
- JBS 设置行已移除对主 Mod DLL 的编译期依赖；它本地保留与主 Mod 设置 Dock 一致的布局常量，因此不会被主 Mod 输出 DLL 是否刷新阻塞。
- 网络基础链路已覆盖 `GET /rooms`、`POST /match/join`、`POST /match/leave`、`POST /match/disband` 和 WebSocket 聊天收发，相关文件为：
  - `bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/Game/TournamentRoom/TournamentRoomPanel.cs`
  - `bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/Infrastructure/TournamentChatClient.cs`
- `/rooms`、`/match/join` 和 WebSocket 消息已改用 `JbsJson` 结构化解析，不再依赖按字符串扫描字段。
- WebSocket 接收已循环聚合到 `EndOfMessage`，支持服务端分片消息。
- 玩家列表已按 `userId` 维护；同名玩家可以同时存在，join/leave/welcome 的 `count` 会刷新顶部人数与玩家列表标题。
- Go 服务器已为每个聊天室缓存最近 50 条聊天消息，新玩家连接后会收到 `history` 消息并补齐历史聊天记录；游戏内 Mod、Unity 预览和 web 调试页都已支持解析。
- Unity 预览界面 `bazaarplusplus-mod/preview/settings-ui-preview/Assets/SettingsDockPreview/TournamentRoomPanelPreview.cs` 已同步上述 JSON 解析、WebSocket 分片和 `userId` 玩家状态逻辑，并通过 Unity Roslyn 响应文件编译。
- 配置项已包含功能开关和服务器地址，位于 `bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/JbsConfig.cs`。
- 中英文本地化文件已存在：
  - `bazzarplusplus-jbs-server-mod/locales/zh-CN.json`
  - `bazzarplusplus-jbs-server-mod/locales/en-US.json`
- 浏览器调试页已包含房间列表、手动加入、聊天、玩家列表、表情选择器，位于 `bazzarplusplus-jbs-server-mod/web/chat.html`。

## 优先级 1：协议闭环已收口

当前状态：

1. 离开房间会调用 `/match/leave`，随后关闭 WebSocket 并回到房间列表。
2. 创建房间会先通过 `POST /match/join` 等待服务端返回 `chatUrl`，成功后才进入聊天室；失败时停留在房间列表并显示错误。
3. 游戏内底部已支持手动输入 6 位房间码加入，可加入未展示在列表中的房间。

建议回归：

- 离开房间后服务端 `/rooms` 人数刷新。
- 创建房间失败不会进入假聊天室。
- 手动房间码加入未展示房间时，聊天室标题和连接状态正确。

## 优先级 2：整理开关入口和本地化

目标：避免存在“写了但不可见”的假功能，并消除硬编码文案。

需要处理：

1. 决定是否保留 `TournamentEnableToggleController`。
   - 文件：`bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/Game/TournamentRoom/TournamentEnableToggleController.cs`
   - 当前类存在，但 `JbsSettingsButtonPatch.cs` 没有挂载它。
   - 如果保留，需要在合适位置挂载；如果不保留，删除或合并到现有设置行，避免维护两套入口。
2. 硬编码文案接入本地化。
   - 例如“锦标赛 ON/OFF”和浮动按钮“赛”。
   - 本地化入口：`bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/JbsLocalization.cs`
   - 文案文件：`bazzarplusplus-jbs-server-mod/locales/zh-CN.json` 和 `bazzarplusplus-jbs-server-mod/locales/en-US.json`。
3. 对齐默认语言策略。
   - 代码未知语言默认英文，文档写默认简体中文。
   - 需要决定最终规则，并同步代码与文档。

完成标准：

- 设置入口没有重复或不可见的残留控制器。
- 面向用户的新增文案都走 locale。
- 默认语言策略在代码和文档中一致。

## 服务端内存风险已收口

当前状态：

1. `bazzarplusplus-jbs-server-mod/jbs-server/main.go` 的 `maxChatHistory` 已改为 `50`。
2. `/match/leave` 和 WebSocket 断开后会清理空房间：
   - `matchRooms[code]` 没有玩家时删除。
   - `chatRooms[code]` 没有 session 时删除，释放历史消息切片。
   - 动态创建的 `roomMetas[code]` 没有玩家和 session 时删除。
3. 系统预置房间使用 `System` 标记保留，避免首页默认房间被清空。

## 优先级 3：更新文档

目标：让文档准确反映当前实现状态，方便后续继续开发和测试。

需要处理：

1. 更新 `docs/tournament-room.md`。
   - 删除或改写“本地化系统尚未实现”等过期内容。
   - 标注当前已实现、待实现、协议假设和调试方式。
2. 如果协议字段有变更，同步更新文档中的接口说明。
3. 记录游戏内和 web 调试页能力差异。

完成标准：

- 文档不再和代码状态冲突。
- 下一次接手者可以通过文档知道哪些功能是完整的，哪些只是 UI 骨架或调试工具。

## 建议执行顺序

1. 整理 `TournamentEnableToggleController` 和本地化硬编码。
2. 用 `~/.dotnet/dotnet build bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/BazaarPlusPlus.JbsServer.csproj -c Release --no-restore` 回归 JBS 构建。
3. 如修改预览脚本，用 Unity 生成的 Roslyn 响应文件回归预览程序集编译，或直接在 Unity Editor 中 Play 验证。

## 注意事项

- 当前仓库有未提交改动，下一次会话不要回滚无关文件。
- 修改前先读对应文件，确认用户是否已经继续改过。
- 如果构建会产生 `bin/obj` 或复制 DLL，构建后只汇报结果，不要把生成物纳入提交。
- 优先保持现有 UI 风格和 `BppSettingsDockVisualConstants` 视觉规范。
