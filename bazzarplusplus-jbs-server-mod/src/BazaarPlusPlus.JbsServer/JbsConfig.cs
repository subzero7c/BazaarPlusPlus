#nullable enable
using System;
using BepInEx.Configuration;

namespace BazaarPlusPlus.JbsServer;

internal static class JbsConfig
{
    private static ConfigEntry<bool>? _enabled;
    private static ConfigEntry<string>? _serverWsUrl;
    private static ConfigEntry<bool>? _showDebugTestButton;

    internal static string ServerWsUrl => _serverWsUrl?.Value ?? "ws://localhost:8787";

    internal static bool Enabled => _enabled?.Value ?? true;

    internal static bool ShowDebugTestButton => _showDebugTestButton?.Value ?? false;

    internal static event Action<bool>? EnabledChanged;

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

        _showDebugTestButton = config.Bind(
            "TournamentRoom",
            "ShowDebugTestButton",
            false,
            "是否在锦标赛界面显示测试填码按钮 / Show debug test-fill button on tournament screen"
        );

        _enabled.SettingChanged += (_, _) => EnabledChanged?.Invoke(_enabled.Value);
    }

    internal static void SetEnabled(bool value)
    {
        if (_enabled == null)
            return;

        _enabled.Value = value;
    }
}
