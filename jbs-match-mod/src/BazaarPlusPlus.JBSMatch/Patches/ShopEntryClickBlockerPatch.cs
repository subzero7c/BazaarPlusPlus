#pragma warning disable CS0436
#nullable enable
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.JBSMatch.Game.Testing;
using HarmonyLib;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus.JBSMatch.Patches;

[HarmonyPatch(typeof(EncounterController), nameof(EncounterController.Select))]
internal static class JbsBlockEncounterSelectPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EncounterController __instance, PointerEventData eventData)
    {
        if (!ShopClickBlockerRuntime.ShouldBlockShopEntryClick(__instance.CardData))
            return true;

        eventData.Use();
        ShopClickBlockerRuntime.NotifyBlocked("EncounterController.Select");
        return false;
    }
}

[HarmonyPatch(typeof(EncounterController), nameof(EncounterController.ProceedClick))]
internal static class JbsBlockEncounterProceedClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EncounterController __instance, PointerEventData eventData)
    {
        if (!ShopClickBlockerRuntime.ShouldBlockShopEntryClick(__instance.CardData))
            return true;

        eventData.Use();
        ShopClickBlockerRuntime.NotifyBlocked("EncounterController.ProceedClick");
        return false;
    }
}

[HarmonyPatch(typeof(TheBazaar.Cmd), nameof(TheBazaar.Cmd.SelectEncounter))]
internal static class JbsBlockCmdSelectEncounterPatch
{
    [HarmonyPrefix]
    private static bool Prefix(InstanceId instanceId)
    {
        if (!ShopClickBlockerRuntime.ShouldBlockShopEntryClick(instanceId))
            return true;

        ShopClickBlockerRuntime.NotifyBlocked("Cmd.SelectEncounter");
        return false;
    }
}
