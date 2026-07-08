#pragma warning disable CS0436
#nullable enable
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BazaarPlusPlus.JbsServer.Diagnostics;
using BazaarPlusPlus.JbsServer.Game.Testing;
using BazaarPlusPlus.JbsServer.Game.TournamentRoom;
using HarmonyLib;

namespace BazaarPlusPlus.JbsServer;

[BepInPlugin("bazaarplusplus.jbsserver", "BazaarPlusPlus.JbsServer", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new("bazaarplusplus.jbsserver");
    private bool _patchesApplied;

    private void Awake()
    {
        try
        {
            JbsLog.Install(Logger);
            JbsLog.InitializeFileLogging(System.IO.Path.GetDirectoryName(Info.Location));
            JbsConfig.Initialize(Config);
            JbsLocalization.Initialize(System.IO.Path.GetDirectoryName(Info.Location));
            TournamentTitleToggleController.Ensure(gameObject);
            TournamentChatButtonController.Ensure(gameObject);
            ShopClickBlockerToggleController.Ensure(gameObject);
            TournamentRoomAutoCloseController.Ensure(gameObject);
            JbsNativeTournamentRoomWatcher.Ensure(gameObject);
            TournamentUiControlDumper.Ensure(
                gameObject,
                System.IO.Path.GetDirectoryName(Info.Location) ?? "."
            );

            _harmony.PatchAll(typeof(Plugin).Assembly);
            _patchesApplied = true;

            JbsLog.Info("Plugin", "BazaarPlusPlus.JbsServer loaded");
        }
        catch (Exception ex)
        {
            JbsLog.Error("Plugin", "Initialization failed", ex);
        }
    }

    private void OnDestroy()
    {
        if (_patchesApplied)
        {
            _harmony.UnpatchSelf();
            _patchesApplied = false;
        }
        JbsLog.Reset();
    }
}
