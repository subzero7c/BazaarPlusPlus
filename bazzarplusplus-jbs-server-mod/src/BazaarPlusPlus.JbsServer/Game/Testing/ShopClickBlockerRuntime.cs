#pragma warning disable CS0436
#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BepInEx;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.JbsServer.Game.Testing;

internal static class ShopClickBlockerRuntime
{
    private const string LogCategory = "ShopClickBlocker";
    private static readonly string[] TournamentMarkers =
    {
        "tournament",
        "public-test-realm",
        "public_test_realm",
        "public test realm",
        "ptr",
        "jbs",
    };

    private static float _lastBlockedLogTime;
    private static bool? _isTournamentMode;

    internal static bool ShouldBlockShopEntryClick(Card? card)
    {
        return JbsConfig.BlockShopClicksTestMode && IsInGameRun() && IsMerchantEncounter(card);
    }

    internal static bool ShouldBlockShopEntryClick(InstanceId instanceId)
    {
        if (!JbsConfig.BlockShopClicksTestMode || !IsInGameRun())
            return false;

        try
        {
            return Data.Entities.TryGetValue(instanceId, out var card)
                && IsMerchantEncounter(card as Card);
        }
        catch (Exception ex)
        {
            JbsLog.Debug(LogCategory, $"Failed to inspect encounter selection: {ex.Message}");
            return false;
        }
    }

    internal static bool IsInGameRun()
    {
        try
        {
            return Data.Run != null;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsTournamentMode()
    {
        if (IsInMainModTournamentRoom())
            return true;

        return _isTournamentMode ??= DetectTournamentMode();
    }

    internal static void NotifyBlocked(string action)
    {
        try
        {
            var now = UnityEngine.Time.unscaledTime;
            if (now - _lastBlockedLogTime < 1f)
                return;

            _lastBlockedLogTime = now;
            JbsLog.Info(LogCategory, $"Blocked shop entry click: {action}");
        }
        catch (Exception ex)
        {
            JbsLog.Debug(LogCategory, $"Blocked shop click log failed: {ex.Message}");
        }
    }

    private static bool IsMerchantEncounter(Card? card)
    {
        if (card == null)
            return false;

        try
        {
            if (card.Type is not (
                    ECardType.EncounterStep
                    or ECardType.EventEncounter
                    or ECardType.PedestalEncounter
                    or ECardType.CombatEncounter
                    or ECardType.PvpEncounter
                ))
                return false;

            if (card.Tags != null && card.Tags.Contains(ECardTag.Merchant))
                return true;

            if (card.HiddenTags != null && card.HiddenTags.Contains(EHiddenTag.Merchant))
                return true;

            var name = card.Name ?? string.Empty;
            return name.Contains("Merchant", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Shop", StringComparison.OrdinalIgnoreCase)
                || name.Contains("商店", StringComparison.Ordinal)
                || name.Contains("商人", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            JbsLog.Debug(LogCategory, $"Failed to inspect merchant encounter: {ex.Message}");
            return false;
        }
    }

    private static bool IsInMainModTournamentRoom()
    {
        try
        {
            var bridge = Type.GetType("BazaarPlusPlus.GameInterop.BppTournamentRoomBridge, BazaarPlusPlus");
            var method = bridge?.GetMethod(
                "IsInTournamentRoom",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            );
            return method?.Invoke(null, null) is true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectTournamentMode()
    {
        if (ContainsTournamentMarker(SafeGetGameRootPath())
            || ContainsTournamentMarker(SafeGetUnityValue(() => Application.dataPath))
            || ContainsTournamentMarker(SafeGetUnityValue(() => Application.productName))
            || ContainsTournamentMarker(SafeGetUnityValue(() => Application.version)))
            return true;

        var manifest = TryReadCurrentSteamManifest(SafeGetGameRootPath());
        return ContainsTournamentMarker(manifest);
    }

    private static bool ContainsTournamentMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var marker in TournamentMarkers)
            if (value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

        return false;
    }

    private static string? TryReadCurrentSteamManifest(string? gameRootPath)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath))
            return null;

        try
        {
            var gameRoot = new DirectoryInfo(gameRootPath);
            if (!gameRoot.Exists)
                return null;

            var steamApps = FindSteamAppsDirectory(gameRoot);
            if (steamApps == null)
                return null;

            foreach (var manifestPath in Directory.EnumerateFiles(steamApps.FullName, "appmanifest_*.acf"))
            {
                var content = File.ReadAllText(manifestPath);
                if (ManifestMatchesGameRoot(content, gameRoot.Name))
                    return content;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static DirectoryInfo? FindSteamAppsDirectory(DirectoryInfo start)
    {
        for (var current = start; current != null; current = current.Parent)
            if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                return current;

        return null;
    }

    private static bool ManifestMatchesGameRoot(string manifestContent, string gameRootName)
    {
        var installDir = TryReadAcfValue(manifestContent, "installdir");
        if (!string.IsNullOrWhiteSpace(installDir))
            return string.Equals(installDir, gameRootName, StringComparison.OrdinalIgnoreCase);

        var name = TryReadAcfValue(manifestContent, "name");
        return string.Equals(name, gameRootName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadAcfValue(string content, string key)
    {
        var match = Regex.Match(
            content,
            $"\"{Regex.Escape(key)}\"\\s+\"(?<value>[^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? SafeGetGameRootPath()
    {
        try
        {
            return Paths.GameRootPath;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeGetUnityValue(Func<string?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }
}
