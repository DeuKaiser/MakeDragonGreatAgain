using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Localization;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using MDGA.Loc;
using UnityEngine;

namespace MDGA.GeneralClasses.DraconicBloodline
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DraconicBloodlineResistanceBoost
    {
        private static readonly BlueprintGuid[] ResistanceFeatureGuids =
        {
            BlueprintGuid.Parse("5623c02b343f59a46a4227f32f44ba8b"), // BloodlineDraconicBlackResistancesAbilityLevel1
            BlueprintGuid.Parse("307544d817c7d5f4883f1400918d07fb"), // BloodlineDraconicBlackResistancesAbilityLevel2
            BlueprintGuid.Parse("586f50b2310239647b8c054e9d12a3e3"), // BloodlineDraconicBlackResistancesAbilityLevel3
            BlueprintGuid.Parse("73916f082fbb1b843a55dec6671da447"), // BloodlineDraconicBlueResistancesAbilityLevel1
            BlueprintGuid.Parse("9fc2e77baef2ee5429d6918678102d80"), // BloodlineDraconicBlueResistancesAbilityLevel2
            BlueprintGuid.Parse("c4f5015c46c12ab40b6274ee5e49f33e"), // BloodlineDraconicBlueResistancesAbilityLevel3
            BlueprintGuid.Parse("3930769e8ee7e0848afdf1f6fb55f020"), // BloodlineDraconicBrassResistancesAbilityLevel1
            BlueprintGuid.Parse("b39740c2f25412145963ae28b51bddb6"), // BloodlineDraconicBrassResistancesAbilityLevel2
            BlueprintGuid.Parse("5fcd4542dc32af146a7121ef8a3853f0"), // BloodlineDraconicBrassResistancesAbilityLevel3
            BlueprintGuid.Parse("920ae03f1b4ed1b4bb5217099103e54a"), // BloodlineDraconicBronzeResistancesAbilityLevel1
            BlueprintGuid.Parse("57b9d5a7cce17b846a9a1b6ec4b73648"), // BloodlineDraconicBronzeResistancesAbilityLevel2
            BlueprintGuid.Parse("2698841e4b5974e4f94968d61cf64b2f"), // BloodlineDraconicBronzeResistancesAbilityLevel3
            BlueprintGuid.Parse("64c443cd6c84bd946a29fafa874a3c24"), // BloodlineDraconicCopperResistancesAbilityLevel1
            BlueprintGuid.Parse("1652a1b9d85fd9d478f1cc28624db64e"), // BloodlineDraconicCopperResistancesAbilityLevel2
            BlueprintGuid.Parse("e67d06fab10bf39438afba1f9663d328"), // BloodlineDraconicCopperResistancesAbilityLevel3
            BlueprintGuid.Parse("0c9c41ffc4471b64f8d72a12362a70f9"), // BloodlineDraconicGoldResistancesAbilityLevel1
            BlueprintGuid.Parse("90f8fc30b79672844b10dc371a5ac5da"), // BloodlineDraconicGoldResistancesAbilityLevel2
            BlueprintGuid.Parse("3fcb5d4f201cfba4da2095214b999bef"), // BloodlineDraconicGoldResistancesAbilityLevel3
            BlueprintGuid.Parse("2603c92ec7d5c564fac40a7d34099289"), // BloodlineDraconicGreenResistancesAbilityLevel1
            BlueprintGuid.Parse("89e1bae9246c12d4bb1578ec1724202f"), // BloodlineDraconicGreenResistancesAbilityLevel2
            BlueprintGuid.Parse("9e0ace7ba157ee14bacc866aa1cef64a"), // BloodlineDraconicGreenResistancesAbilityLevel3
            BlueprintGuid.Parse("67979ecc80c4fed40954d62e3844583e"), // BloodlineDraconicRedResistancesAbilityLevel1
            BlueprintGuid.Parse("e353df68a1bb5894ea1d33b0bda1400d"), // BloodlineDraconicRedResistancesAbilityLevel2
            BlueprintGuid.Parse("d102a33c03260a6458b7508a243e1958"), // BloodlineDraconicRedResistancesAbilityLevel3
            BlueprintGuid.Parse("7e873e39082c78a4b87ad081cb59ef14"), // BloodlineDraconicSilverResistancesAbilityLevel1
            BlueprintGuid.Parse("8b30236008a0d5a49bacb2d38ce04b31"), // BloodlineDraconicSilverResistancesAbilityLevel2
            BlueprintGuid.Parse("3bf5f1e83c542e640bb3e7fb66b1104c"), // BloodlineDraconicSilverResistancesAbilityLevel3
            BlueprintGuid.Parse("f0808daee57206645a2b23b17d5b63f0"), // BloodlineDraconicWhiteResistancesAbilityLevel1
            BlueprintGuid.Parse("64b243d2b7d780e459779c446af558e4"), // BloodlineDraconicWhiteResistancesAbilityLevel2
            BlueprintGuid.Parse("66d8598b2c9c69f468c2efcdc9ad0e2d"), // BloodlineDraconicWhiteResistancesAbilityLevel3
        };

        private static readonly BlueprintGuid[] DescriptionOnlyFeatureGuids =
        {
            BlueprintGuid.Parse("c97e9ec3c7687bc488094d13807cbee0"), // BloodlineDraconicBlackResistancesAbilityAddLevel1
            BlueprintGuid.Parse("3e484a0391c250448bd0d6f6a0081e94"), // BloodlineDraconicBlackResistancesAbilityAddLevel2
            BlueprintGuid.Parse("b28fd00f894fd2846bd77a26d1344425"), // BloodlineDraconicBlueResistancesAbilityAddLevel1
            BlueprintGuid.Parse("6b13b37a4b4263c46a55b0e213eb1e78"), // BloodlineDraconicBlueResistancesAbilityAddLevel2
            BlueprintGuid.Parse("66ac2480fcd5b2c41b886e1bdd65f5de"), // BloodlineDraconicBrassResistancesAbilityAddLevel1
            BlueprintGuid.Parse("849eccece1191cd49bf073864bc30596"), // BloodlineDraconicBrassResistancesAbilityAddLevel2
            BlueprintGuid.Parse("71bea6cd9a9e9ad469bc459f1b18f9c3"), // BloodlineDraconicBronzeResistancesAbilityAddLevel1
            BlueprintGuid.Parse("4256841e2968af34ab396c07ae212ac4"), // BloodlineDraconicBronzeResistancesAbilityAddLevel2
            BlueprintGuid.Parse("89d90c28dcad6704096f34d8782fb944"), // BloodlineDraconicCopperResistancesAbilityAddLevel1
            BlueprintGuid.Parse("7a985ce3ed6888644b0e12746de241f2"), // BloodlineDraconicCopperResistancesAbilityAddLevel2
            BlueprintGuid.Parse("c540433df26c8454e80161e7532064ac"), // BloodlineDraconicGoldResistancesAbilityAddLevel1
            BlueprintGuid.Parse("5d6fa86bf7196554997a2275e47c7096"), // BloodlineDraconicGoldResistancesAbilityAddLevel2
            BlueprintGuid.Parse("68009d99ecfdcf84191071feebe853f4"), // BloodlineDraconicGreenResistancesAbilityAddLevel1
            BlueprintGuid.Parse("2b2030bac835e5044a41d4d18b6d635e"), // BloodlineDraconicGreenResistancesAbilityAddLevel2
            BlueprintGuid.Parse("e3f7c6de540a3ed478fa32be32c7fc02"), // BloodlineDraconicRedResistancesAbilityAddLevel1
            BlueprintGuid.Parse("915c31bf91a8d8e4f8009fc487ac1786"), // BloodlineDraconicRedResistancesAbilityAddLevel2
            BlueprintGuid.Parse("48e5aff05dfe73b4e9574364fbde2a20"), // BloodlineDraconicSilverResistancesAbilityAddLevel1
            BlueprintGuid.Parse("47babe9a51e698640a6d8dce6fd092c5"), // BloodlineDraconicSilverResistancesAbilityAddLevel2
            BlueprintGuid.Parse("2e8c53c30eefe11439c81b28ae58eb2a"), // BloodlineDraconicWhiteResistancesAbilityAddLevel1
            BlueprintGuid.Parse("a5f295f4f80fab946af61a2f68c31c61"), // BloodlineDraconicWhiteResistancesAbilityAddLevel2
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
                int armorChanged = 0;
                int resistanceChanged = 0;
                int descriptionsPatched = 0;

                foreach (var guid in ResistanceFeatureGuids)
                {
                    var feature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(guid);
                    if (feature == null)
                    {
                        Main.Log("[DraconicBloodlineResistanceBoost] Missing feature: " + guid);
                        continue;
                    }

                    featuresFound++;
                    PatchFeature(feature, ref armorChanged, ref resistanceChanged);
                    PatchDescription(feature);
                    descriptionsPatched++;
                }

                foreach (var guid in DescriptionOnlyFeatureGuids)
                {
                    var feature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(guid);
                    if (feature == null)
                    {
                        Main.Log("[DraconicBloodlineResistanceBoost] Missing description feature: " + guid);
                        continue;
                    }

                    PatchDescription(feature);
                    descriptionsPatched++;
                }

                Main.Log("[DraconicBloodlineResistanceBoost] Applied: features=" + featuresFound
                    + ", natural armor changed=" + armorChanged
                    + ", energy resistance changed=" + resistanceChanged
                    + ", descriptions=" + descriptionsPatched);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MDGA] DraconicBloodlineResistanceBoost error: " + ex);
            }
        }

        private static void PatchFeature(BlueprintFeature feature, ref int armorChanged, ref int resistanceChanged)
        {
            foreach (var armor in (feature.ComponentsArray ?? Array.Empty<BlueprintComponent>()).OfType<AddStatBonus>())
            {
                if (armor.Stat != StatType.AC || armor.Descriptor != ModifierDescriptor.NaturalArmor) continue;

                int doubled = DoubleKnownValue(armor.Value);
                if (doubled == armor.Value) continue;

                armor.Value = doubled;
                armorChanged++;
            }

            foreach (var resistance in (feature.ComponentsArray ?? Array.Empty<BlueprintComponent>()).OfType<AddDamageResistanceEnergy>())
            {
                var value = resistance.Value;
                if (value == null || value.ValueType != ContextValueType.Simple) continue;

                int doubled = DoubleKnownValue(value.Value);
                if (doubled == value.Value) continue;

                value.Value = doubled;
                resistanceChanged++;
            }
        }

        private static void PatchDescription(BlueprintFeature feature)
        {
            string description = IsChinese()
                ? "达到3级后，你获得10点对应能量抗性，AC获得+2天生防御加值。达到9级后，对应能量抗性提高至20点，天生防御加值提高至+4。达到15级后，天生防御加值提高至+8。"
                : "At 3rd level, you gain energy resistance 10 against your energy type and a +2 natural armor bonus to AC. At 9th level, this resistance increases to 20 and the natural armor bonus increases to +4. At 15th level, the natural armor bonus increases to +8.";

            LocalizationInjector.RegisterFeatureLocalization(feature, feature.Name, description);
        }

        private static bool IsChinese()
        {
            try
            {
                var locale = LocalizationManager.CurrentLocale;
                if (locale != null)
                {
                    string localeText = locale.ToString();
                    if (!string.IsNullOrEmpty(localeText) && localeText.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var language = locale.GetType().GetProperty("Language")?.GetValue(locale, null);
                    if (language != null && language.ToString().StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }

            return false;
        }

        private static int DoubleKnownValue(int value)
        {
            switch (value)
            {
                case 1: return 2;
                case 2: return 4;
                case 4: return 8;
                case 5: return 10;
                case 10: return 20;
                default: return value;
            }
        }
    }
}
