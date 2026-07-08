#nullable enable
using System;
using BepInEx.Configuration;

namespace BazaarPlusPlus.JBSMatch;

internal static class JbsConfig
{
    private static ConfigEntry<bool>? _enabled;
    private static ConfigEntry<string>? _serverWsUrl;
    private static ConfigEntry<bool>? _blockShopClicksTestMode;

    internal static string ServerWsUrl => _serverWsUrl?.Value ?? "ws://localhost:8787";

    internal static bool Enabled => _enabled?.Value ?? true;

    internal static bool BlockShopClicksTestMode => _blockShopClicksTestMode?.Value ?? false;

    internal static event Action<bool>? EnabledChanged;
    internal static event Action<bool>? BlockShopClicksTestModeChanged;

    internal static void Initialize(ConfigFile config)
    {
        _enabled = config.Bind(
            "TournamentRoom",
            "Enabled",
            true,
            "是否启用锦标赛房间匹配功能 / Enable tournament room matching feature"
        );

        _serverWsUrl = config.Bind(
            "Server",
            "WebSocketUrl",
            "ws://localhost:8787",
            "JBS 服务器 WebSocket 地址 / JBS server WebSocket base URL"
        );

        _blockShopClicksTestMode = config.Bind(
            "Testing",
            "BlockShopClicksInGame",
            false,
            "测试功能：游戏中屏蔽进入商店的点击 / Test: block in-game clicks that enter merchant shops"
        );

        _enabled.SettingChanged += (_, _) => EnabledChanged?.Invoke(_enabled.Value);
        _blockShopClicksTestMode.SettingChanged += (_, _) =>
            BlockShopClicksTestModeChanged?.Invoke(_blockShopClicksTestMode.Value);
    }

    internal static void SetEnabled(bool value)
    {
        if (_enabled == null)
            return;

        _enabled.Value = value;
    }

    internal static void SetBlockShopClicksTestMode(bool value)
    {
        if (_blockShopClicksTestMode == null)
            return;

        _blockShopClicksTestMode.Value = value;
    }
}
