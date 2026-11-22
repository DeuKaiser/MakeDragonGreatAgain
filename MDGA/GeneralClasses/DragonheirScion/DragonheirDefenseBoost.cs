using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using UnityEngine;

namespace MDGA.GeneralClasses.DragonheirScion
{
    // Doubles the numeric bonuses granted by the shared Dragonic Defences features:
    // natural armor 1/2/3 -> 2/4/6, energy resistance 5/10/20 -> 10/20/40.
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonheirDefenseBoost
    {
        private static readonly BlueprintGuid[] DefenseFeatureGuids =
        {
            BlueprintGuid.Parse("b24ffb93a4fce9f4e9790b202d09ebd9"), // DragonicDefencesAcid
            BlueprintGuid.Parse("d2a31af5bd02f9a4a87d6015fdba56bb"), // DragonicDefencesFire
            BlueprintGuid.Parse("3add20ed60e4e4344afbfa3e3ff4635a"), // DragonicDefencesCold
            BlueprintGuid.Parse("af0027e0c0432bc418e813e32de0460b"), // DragonicDefencesElectricity
        };

        private static bool _applied;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_applied) return;
            _applied = true;
            if (!Main.Enabled) return;

            try
            {
                int featuresFound = 0;
                int armorComponents = 0;
                int resistanceComponents = 0;

                foreach (var guid in DefenseFeatureGuids)
                {
                    var feature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(guid);
                    if (feature == null)
                    {
                        Main.Log("[DragonheirDefenseBoost] Missing defence feature: " + guid);
                        continue;
                    }

                    featuresFound++;
                    PatchFeature(feature, ref armorComponents, ref resistanceComponents);
                }

                Main.Log("[DragonheirDefenseBoost] Applied: features=" + featuresFound
                    + ", natural armor components=" + armorComponents
                    + ", energy resistance components=" + resistanceComponents);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MDGA] DragonheirDefenseBoost global error: " + ex);
            }
        }

        private static void PatchFeature(BlueprintFeature feature, ref int armorComponents, ref int resistanceComponents)
        {
            foreach (var armor in (feature.ComponentsArray ?? Array.Empty<BlueprintComponent>()).OfType<AddContextStatBonus>())
            {
                if (armor.Stat != StatType.AC || armor.Descriptor != ModifierDescriptor.NaturalArmor) continue;

                armor.Multiplier = 2;
                armorComponents++;
            }

            foreach (var resistance in (feature.ComponentsArray ?? Array.Empty<BlueprintComponent>()).OfType<AddDamageResistanceEnergy>())
            {
                resistance.UseValueMultiplier = true;
                resistance.ValueMultiplier = new ContextValue
                {
                    ValueType = ContextValueType.Simple,
                    Value = 2,
                };
                resistanceComponents++;
            }
        }
    }
}
