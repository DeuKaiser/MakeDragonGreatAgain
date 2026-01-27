using HarmonyLib;
using Kingmaker.Blueprints.JsonSystem;

namespace MDGA.Components
{
    // Ensure our True Dragon augmentation runs after all blueprints are loaded
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    [HarmonyAfter("DarkCodex")]
    [HarmonyPriority(Priority.Last)]
    internal static class TrueDragonBlueprintsCachePostfix
    {
        private static bool _applied;

        static void Postfix()
        {
            if (_applied) return;
            _applied = true;
            if (!MDGA.Mythic.TrueDragon.IsDarkCodexLoaded()) return;
            MDGA.Mythic.TrueDragon.ApplyIfDcEnabled();
        }
    }
}
