using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.Utility;

namespace MDGA.GoldDragonMythic
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonUltimateMod
    {
        private static bool _done;

        private static readonly BlueprintGuid DragonUltimateApsuGuid = BlueprintGuid.Parse("cff9e3bf5ccf40c489023bf368c2c802");
        private static readonly BlueprintGuid DragonUltimateDahakGuid = BlueprintGuid.Parse("5b1984f4af00412eb0c0efb0ebb90189");

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;

            try
            {
                UpdateAbilityRadiusTo60ft(DragonUltimateApsuGuid, "DragonUltimateApsu");
                UpdateAbilityRadiusTo60ft(DragonUltimateDahakGuid, "DragonUltimateDahak");
            }
            catch (Exception ex)
            {
                Main.Log("[DragonUltimateMod] Exception: " + ex.Message);
            }
        }

        private static void UpdateAbilityRadiusTo60ft(BlueprintGuid abilityGuid, string tag)
        {
            var ability = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(abilityGuid);
            if (ability == null)
            {
                Main.Log($"[DragonUltimateMod] {tag} blueprint not found.");
                return;
            }

            var around = ability.GetComponents<AbilityTargetsAround>()?.FirstOrDefault();
            if (around == null)
            {
                Main.Log($"[DragonUltimateMod] {tag} has no AbilityTargetsAround; skipping.");
                return;
            }

            around.m_Radius = new Feet(80f);
            Main.Log($"[DragonUltimateMod] {tag} radius set to 80ft.");
        }
    }
}
