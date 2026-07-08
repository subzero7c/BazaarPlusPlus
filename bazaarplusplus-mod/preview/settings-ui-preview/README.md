# BazaarPlusPlus Settings UI Preview

这是一个 **不启动 The Bazaar 游戏** 的 Unity 独立预览工程，用来快速查看 BazaarPlusPlus 设置 Dock 的视觉布局。

第一版只预览设置面板，不预览图鉴、历史面板或真实卡牌。

## 如何运行

1. 打开 Unity Hub。
2. Add / Open project，选择这个目录：

   ```text
   bazaarplusplus-mod/preview/settings-ui-preview
   ```

3. 使用 Unity `2022.3.40f1` 或相近的 `2022.3 LTS` 版本打开。
4. 新建一个空 Scene，或者直接在默认空场景里点击 Play。
5. 进入 Play Mode 后，脚本会自动创建 Canvas、假原生设置按钮、BPP 设置按钮和展开的设置面板。

## 快捷键

- `L`：切换中文/英文标签。
- `Space`：展开/收起设置面板。
- 点击任意设置行：切换或循环该行的 mock 状态。

## 这个预览是真实的部分

- 设置 Dock 面板宽度、高度、padding、row height、row spacing 等布局常量直接引用生产代码的 `BppSettingsDockVisualConstants`。
- 背景、边框、启用/禁用行的颜色直接引用生产代码的 `BppSettingsDockVisualConstants`。
- 布局逻辑（ConfigureHeaderRect、ConfigureRowRect、CalculatePanelHeight）直接使用生产代码的共享方法。
- 行顺序按当前设置 Dock 顺序 mock：历史、改名、传说位置、附魔预览、卡包美术、战斗状态条、中文模式、快捷键教程、Bazaar DB 上传、锦标赛房间匹配。
- 锦标赛房间预览同步游戏内协议和交互逻辑；视觉细节允许先在预览工程中打磨，再按需要回 port 到 Mod。
- 不需要启动游戏，不需要 BepInEx，不需要 The Bazaar 资源。

## 这个预览是假的部分

- 不引用 BazaarPlusPlus 插件 DLL。
- 不读取真实 BepInEx 配置。
- 不调用 The Bazaar 的 `PlayerPreferences`、`TheBazaar.Data` 或任何游戏服务。
- 按钮点击只修改 mock 状态，不会影响真实插件配置。
- 字体使用 Unity 内置 Arial，不是游戏内 TMP 字体，也不是 BPP 嵌入字体。
- 原生设置按钮只是一个占位按钮，不是从游戏 UI 克隆出来的真实按钮。

## 适合用来验证

- 设置 Dock 是否太宽/太高。
- 行间距、边距、字号是否舒服。
- 中文/英文标签是否挤爆。
- 展开位置和整体视觉是否大致正确。

## 不适合用来验证

- 游戏里是否成功 patch 设置菜单。
- `BppNativeSettingsButtonClone` 是否能从真实设置按钮克隆出 BPP 按钮。
- TextMeshPro 字体解析是否成功。
- BepInEx 配置读写是否成功。
- 游戏更新导致的真实 UI 层级变化。

这些仍然需要进 The Bazaar 游戏内测试。

## 维护说明

这个预览工程现在直接引用生产插件的共享视觉常量文件：

- `Assets/SettingsDockPreview/BppSettingsDockVisualConstants.cs` 是从生产代码复制过来的共享常量文件

如果修改了生产代码里的设置 Dock 面板布局，需要同步更新：

1. 更新生产代码里的共享常量文件：
   - `bazaarplusplus-mod/src/BazaarPlusPlus/Game/Settings/Visual/BppSettingsDockVisualConstants.cs`

2. 将更新后的文件复制到预览工程：
   ```bash
   cp bazaarplusplus-mod/src/BazaarPlusPlus/Game/Settings/Visual/BppSettingsDockVisualConstants.cs \
      bazaarplusplus-mod/preview/settings-ui-preview/Assets/SettingsDockPreview/
   ```

生产参考文件：

- `bazaarplusplus-mod/src/BazaarPlusPlus/Game/Settings/Visual/BppSettingsDockVisualConstants.cs` (共享常量和布局方法)
- `bazaarplusplus-mod/src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.cs` (游戏侧主控制器)
- `bazaarplusplus-mod/src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.Presentation.cs` (游戏侧视觉呈现)
- `bazzarplusplus-jbs-server-mod/src/BazaarPlusPlus.JbsServer/Game/TournamentRoom/TournamentSettingsDockRowController.cs` (JBS 插件锦标赛房间行)
