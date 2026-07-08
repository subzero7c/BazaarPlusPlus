using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop;
using Xunit;

namespace BazaarPlusPlus.Mod.Tests;

public sealed class BazaarGameEnvironmentProbeTests
{
    [Fact]
    public void DetectForTest_returns_tournament_for_matching_steam_beta_key()
    {
        using var fixture = new SteamGameFixture("The Bazaar");
        fixture.WriteManifest(
            """
            "AppState"
            {
                "appid"      "1617400"
                "name"       "The Bazaar"
                "installdir" "The Bazaar"
                "UserConfig"
                {
                    "betakey" "public-test-realm"
                }
            }
            """
        );

        var environment = BazaarGameEnvironmentProbe.DetectForTest(fixture.GameRootPath);

        Assert.Equal(BazaarGameEnvironment.Tournament, environment);
    }

    [Fact]
    public void DetectForTest_returns_tournament_for_path_marker()
    {
        var environment = BazaarGameEnvironmentProbe.DetectForTest(
            "/Games/The Bazaar PTR",
            productName: "The Bazaar"
        );

        Assert.Equal(BazaarGameEnvironment.Tournament, environment);
    }

    [Fact]
    public void DetectForTest_returns_official_without_tournament_markers()
    {
        using var fixture = new SteamGameFixture("The Bazaar");
        fixture.WriteManifest(
            """
            "AppState"
            {
                "appid"      "1617400"
                "name"       "The Bazaar"
                "installdir" "The Bazaar"
            }
            """
        );

        var environment = BazaarGameEnvironmentProbe.DetectForTest(
            fixture.GameRootPath,
            dataPath: fixture.GameRootPath,
            productName: "The Bazaar",
            version: "1.0.0"
        );

        Assert.Equal(BazaarGameEnvironment.Official, environment);
    }

    [Fact]
    public void CurrentEnvironment_returns_tournament_after_entering_tournament_room()
    {
        try
        {
            BppTournamentRoomBridge.EnterRoom("ABC123", "Test Room");

            var probe = new BazaarGameEnvironmentProbe();

            Assert.Equal(BazaarGameEnvironment.Tournament, probe.CurrentEnvironment);
            Assert.True(probe.IsTournamentEnvironment());
            Assert.False(probe.IsOfficialEnvironment());
        }
        finally
        {
            BppTournamentRoomBridge.LeaveRoom();
        }
    }

    private sealed class SteamGameFixture : IDisposable
    {
        private readonly string _rootPath;
        private readonly string _steamAppsPath;

        public SteamGameFixture(string installDir)
        {
            _rootPath = Path.Combine(
                Path.GetTempPath(),
                $"bpp-env-probe-{Guid.NewGuid():N}"
            );
            _steamAppsPath = Path.Combine(_rootPath, "steamapps");
            GameRootPath = Path.Combine(_steamAppsPath, "common", installDir);
            Directory.CreateDirectory(GameRootPath);
        }

        public string GameRootPath { get; }

        public void WriteManifest(string content)
        {
            Directory.CreateDirectory(_steamAppsPath);
            File.WriteAllText(Path.Combine(_steamAppsPath, "appmanifest_1617400.acf"), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, recursive: true);
        }
    }
}
