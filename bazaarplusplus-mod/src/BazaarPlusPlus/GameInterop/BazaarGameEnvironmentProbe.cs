#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;
using BazaarPlusPlus.Core.GameState;
using BepInEx;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop;

internal sealed class BazaarGameEnvironmentProbe : IBazaarGameEnvironmentProbe
{
    private static readonly string[] TournamentMarkers =
    {
        "tournament",
        "public-test-realm",
        "public_test_realm",
        "public test realm",
        "ptr",
        "jbs",
    };

    private BazaarGameEnvironment? _cachedEnvironment;

    public BazaarGameEnvironment CurrentEnvironment =>
        BppTournamentRoomBridge.IsInTournamentRoom()
            ? BazaarGameEnvironment.Tournament
            : _cachedEnvironment ??= Detect(
                SafeGetGameRootPath(),
                SafeGetUnityValue(() => Application.dataPath),
                SafeGetUnityValue(() => Application.productName),
                SafeGetUnityValue(() => Application.version)
            );

    public bool IsTournamentEnvironment() => CurrentEnvironment == BazaarGameEnvironment.Tournament;

    public bool IsOfficialEnvironment() => CurrentEnvironment == BazaarGameEnvironment.Official;

    internal static BazaarGameEnvironment DetectForTest(
        string? gameRootPath,
        string? dataPath = null,
        string? productName = null,
        string? version = null
    ) => Detect(gameRootPath, dataPath, productName, version);

    private static BazaarGameEnvironment Detect(
        string? gameRootPath,
        string? dataPath,
        string? productName,
        string? version
    )
    {
        if (ContainsTournamentMarker(gameRootPath)
            || ContainsTournamentMarker(dataPath)
            || ContainsTournamentMarker(productName)
            || ContainsTournamentMarker(version))
            return BazaarGameEnvironment.Tournament;

        var manifest = TryReadCurrentSteamManifest(gameRootPath);
        if (manifest != null && ContainsTournamentMarker(manifest))
            return BazaarGameEnvironment.Tournament;

        return BazaarGameEnvironment.Official;
    }

    private static bool ContainsTournamentMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var marker in TournamentMarkers)
        {
            if (value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

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

            foreach (var manifestPath in Directory.EnumerateFiles(
                steamApps.FullName,
                "appmanifest_*.acf"
            ))
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
        {
            if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                return current;
        }

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
