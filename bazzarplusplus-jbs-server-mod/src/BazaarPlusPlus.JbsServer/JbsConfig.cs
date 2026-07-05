#nullable enable
using System;
using BepInEx.Configuration;

namespace BazaarPlusPlus.JbsServer;

internal static class JbsConfig
{
    private static ConfigEntry<bool>? _enabled;

    internal static bool Enabled => _enabled?.Value ?? true;

    internal static event Action<bool>? EnabledChanged;

    internal static void Initialize(ConfigFile config)
    {
        _enabled = config.Bind(
            "TournamentRoom",
            "Enabled",
            true,
            "是否启用锦标赛房间匹配功能 / Enable tournament room matching feature"
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
