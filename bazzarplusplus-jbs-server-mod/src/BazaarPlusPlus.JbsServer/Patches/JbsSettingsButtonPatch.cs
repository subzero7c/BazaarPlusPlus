#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.JbsServer.Game.TournamentRoom;
using HarmonyLib;
using UnityEngine.UI;

namespace BazaarPlusPlus.JbsServer.Patches;

/// <summary>
/// Adds the tournament room dock button and enable/disable toggle
/// alongside the existing BPP mod buttons in the settings area.
///
/// Patching the same SettingDialogsView.Awake as BazaarPlusPlus (main mod)
/// ensures our buttons are placed after the native settings button is ready.
/// Both postfix patches run independently; ordering depends on BepInEx load order.
/// </summary>
[HarmonyPatch(typeof(SettingDialogsView), "Awake")]
internal static class JbsSettingsDialogPatch
{
    private static readonly System.Reflection.FieldInfo? MainMenuSettingOptionButtonField =
        AccessTools.Field(typeof(SettingDialogsView), "MainMenuSettingOptionButton");

    private static readonly System.Reflection.FieldInfo? HeroSelectSettingOptionButtonField =
        AccessTools.Field(typeof(SettingDialogsView), "HeroSelectSettingOptionButton");

    [HarmonyPostfix]
    private static void Postfix(SettingDialogsView __instance)
    {
        try
        {
            AttachButtons(
                MainMenuSettingOptionButtonField?.GetValue(__instance) as Button,
                "MainMenu"
            );
            AttachButtons(
                HeroSelectSettingOptionButtonField?.GetValue(__instance) as Button,
                "HeroSelect"
            );
        }
        catch (Exception ex)
        {
            JbsLog.Error("JbsPatch", "Failed to attach JBS tournament buttons", ex);
        }
    }

    private static void AttachButtons(Button? nativeSettingsButton, string key)
    {
        if (nativeSettingsButton == null)
            return;

        TournamentSettingsDockRowController.Attach(nativeSettingsButton, key);
        TournamentDockButtonController.Attach(nativeSettingsButton, key);
    }
}

/// <summary>
/// Same buttons for the in-fight menu (FightMenuDialog).
/// FightMenuDialog uses ButtonCustom which wraps a plain Button.
/// </summary>
[HarmonyPatch(typeof(FightMenuDialog), "Start")]
internal static class JbsFightMenuPatch
{
    private static readonly System.Reflection.FieldInfo? SettingButtonField = AccessTools.Field(
        typeof(FightMenuDialog),
        "SettingButton"
    );

    [HarmonyPostfix]
    private static void Postfix(FightMenuDialog __instance)
    {
        try
        {
            var settingButtonCustom = SettingButtonField?.GetValue(__instance) as ButtonCustom;
            var button = settingButtonCustom?.GetButton();
            if (button == null)
                return;

            TournamentSettingsDockRowController.Attach(button, "FightMenu");
            TournamentDockButtonController.Attach(button, "FightMenu");
        }
        catch (Exception ex)
        {
            JbsLog.Error("JbsPatch", "Failed to attach JBS tournament buttons in fight menu", ex);
        }
    }
}
