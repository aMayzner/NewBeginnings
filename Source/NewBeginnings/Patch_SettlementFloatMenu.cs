using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace NewBeginnings
{
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetFloatMenuOptions))]
    public static class Patch_SettlementFloatMenu
    {
        public static void Postfix(Settlement __instance, Caravan caravan, ref IEnumerable<FloatMenuOption> __result)
        {
            __result = AddSetFreeOption(__result, caravan, __instance);
        }

        private static IEnumerable<FloatMenuOption> AddSetFreeOption(IEnumerable<FloatMenuOption> original, Caravan caravan, Settlement settlement)
        {
            foreach (FloatMenuOption option in original)
                yield return option;

            foreach (FloatMenuOption option in CaravanArrivalAction_SetFree.GetFloatMenuOptions(caravan, settlement))
                yield return option;
        }
    }
}
