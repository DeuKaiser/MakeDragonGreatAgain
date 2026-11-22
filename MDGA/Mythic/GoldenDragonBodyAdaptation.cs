using System;
using System.Linq;
using BlueprintCore.Blueprints.CustomConfigurators.Classes;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.Enums.Damage;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic;
using MDGA.Loc;
using UnityEngine;

namespace MDGA.Mythic
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class GoldenDragonBodyAdaptation
    {
        internal static readonly BlueprintGuid FeatureGuid = BlueprintGuid.Parse("d6e1b4e0a3c14f6a9a6d6b6b002a8a11");

        private static readonly BlueprintGuid GoldenDragonProgressionGuid = BlueprintGuid.Parse("a6fbca43902c6194c947546e89af64bd");
        private static readonly BlueprintGuid DragonLevel1ImmunitiesGuid = BlueprintGuid.Parse("040bb87b39e4a7f4a8ddc011c5a50523");
        private static readonly BlueprintGuid GoldenDragonBreathFeatureGuid = BlueprintGuid.Parse("63bb12d87f99c4748a890956e5f6b4c7");

        private const string FeatureName = "MDGA_GoldenDragon_BodyAdaptation";
        private const string NameKey = "MDGA.GoldenDragon.BodyAdaptation.Name";
        private const string DescKey = "MDGA.GoldenDragon.BodyAdaptation.Description";
        private const string DisplayName = "龙躯适应";
        private const string Description = "金龙造成的所有火焰伤害视为神圣伤害，所有火焰元素伤害加成现在同样对神圣伤害生效。";

        private static bool _done;
        private static BlueprintFeature _cachedFeature;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return;
            if (!Main.Enabled) return;
            _done = true;

            try
            {
                var feature = EnsureFeature();
                InjectIntoGoldenDragonProgression(feature);
                Main.Log("[GoldenDragonBodyAdaptation] Initialized.");
            }
            catch (Exception ex)
            {
                Main.Log("[GoldenDragonBodyAdaptation] Init error: " + ex);
            }
        }

        internal static bool HasAdaptation(Kingmaker.EntitySystem.Entities.UnitEntityData unit)
        {
            if (!Main.Enabled || unit?.Descriptor == null) return false;

            try
            {
                var feature = GetFeature();
                if (feature != null && unit.Descriptor.HasFact(feature)) return true;

                // Existing saves may already have Golden Dragon level 1 before this feature is added to the progression.
                var level1 = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(DragonLevel1ImmunitiesGuid);
                return level1 != null && unit.Descriptor.HasFact(level1);
            }
            catch
            {
                return false;
            }
        }

        internal static bool FireBonusAppliesToHoly(SpellDescriptor descriptor, BaseDamage damage, Kingmaker.EntitySystem.Entities.UnitEntityData initiator)
        {
            return descriptor.HasAnyFlag(SpellDescriptor.Fire) && IsAdaptedHolyDamage(damage, initiator);
        }

        internal static bool IsAdaptedHolyDamage(BaseDamage damage, Kingmaker.EntitySystem.Entities.UnitEntityData initiator)
        {
            return HasAdaptation(initiator)
                   && damage is EnergyDamage energyDamage
                   && energyDamage.EnergyType == DamageEnergyType.Holy;
        }

        private static BlueprintFeature EnsureFeature()
        {
            var feature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(FeatureGuid);
            if (feature == null)
            {
                var icon = GetIcon();
                feature = FeatureConfigurator.New(FeatureName, FeatureGuid.ToString())
                    .SetIsClassFeature(true)
                    .SetDisplayName(NameKey)
                    .SetDescription(DescKey)
                    .SetIcon(icon)
                    .Configure();
            }

            _cachedFeature = feature;
            RegisterLocalization(feature);
            return feature;
        }

        private static BlueprintFeature GetFeature()
        {
            if (_cachedFeature != null) return _cachedFeature;
            _cachedFeature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(FeatureGuid);
            return _cachedFeature;
        }

        private static Sprite GetIcon()
        {
            try
            {
                var source = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(GoldenDragonBreathFeatureGuid);
                return source?.Icon;
            }
            catch
            {
                return null;
            }
        }

        private static void RegisterLocalization(BlueprintFeature feature)
        {
            LocalizationInjector.RegisterDynamicKey(NameKey, DisplayName);
            LocalizationInjector.RegisterDynamicKey(DescKey, Description);
            LocalizationInjector.BindKeyAndText(feature, "m_DisplayName", NameKey, DisplayName);
            LocalizationInjector.BindKeyAndText(feature, "m_Description", DescKey, Description);
            LocalizationInjector.BindKeyAndText(feature, "m_DescriptionShort", DescKey, Description);
            LocalizationInjector.EnsureInjected();
        }

        private static void InjectIntoGoldenDragonProgression(BlueprintFeature feature)
        {
            var progression = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(GoldenDragonProgressionGuid);
            if (progression == null || feature == null)
            {
                Main.Log("[GoldenDragonBodyAdaptation] Golden Dragon progression or feature missing.");
                return;
            }

            var level1 = progression.LevelEntries?.FirstOrDefault(le => le.Level == 1);
            if (level1 == null)
            {
                level1 = new LevelEntry { Level = 1 };
                progression.LevelEntries = (progression.LevelEntries ?? Array.Empty<LevelEntry>()).Append(level1).ToArray();
            }

            if (level1.m_Features == null)
            {
                level1.m_Features = new System.Collections.Generic.List<BlueprintFeatureBaseReference>();
            }

            if (!level1.m_Features.Any(reference => reference != null && reference.deserializedGuid == FeatureGuid))
            {
                level1.m_Features.Add(feature.ToReference<BlueprintFeatureBaseReference>());
                Main.Log("[GoldenDragonBodyAdaptation] Added feature to Golden Dragon progression level 1.");
            }
        }

        [HarmonyPatch(typeof(RulePrepareDamage), nameof(RulePrepareDamage.OnTrigger))]
        private static class ConvertFireDamageToHolyPatch
        {
            [HarmonyPostfix]
            private static void Postfix(RulePrepareDamage __instance)
            {
                try
                {
                    if (!HasAdaptation(__instance?.Initiator)) return;

                    var converted = 0;
                    foreach (var damage in __instance.DamageBundle)
                    {
                        if (damage is EnergyDamage energyDamage && energyDamage.EnergyType == DamageEnergyType.Fire)
                        {
                            energyDamage.ReplaceEnergy(DamageEnergyType.Holy);
                            converted++;
                        }
                    }

                    if (converted > 0 && Main.Settings?.VerboseLogging == true)
                    {
                        Main.Log("[GoldenDragonBodyAdaptation] Converted fire damage chunks to holy: " + converted);
                    }
                }
                catch (Exception ex)
                {
                    Main.Log("[GoldenDragonBodyAdaptation] Convert fire to holy error: " + ex.Message);
                }
            }
        }

        [HarmonyPatch(typeof(EnergyDamageBonus), nameof(EnergyDamageBonus.OnEventAboutToTrigger))]
        private static class EnergyDamageBonusPatch
        {
            [HarmonyPostfix]
            private static void Postfix(EnergyDamageBonus __instance, RuleCalculateDamage evt)
            {
                try
                {
                    if (__instance == null || evt == null || !HasAdaptation(evt.Initiator)) return;
                    if (!__instance.EnergyType.Contains(DamageEnergyType.Fire.ToFlag())) return;
                    if (__instance.EnergyType.Contains(DamageEnergyType.Holy.ToFlag())) return;

                    var bonusPercent = (int)((__instance.Multiplier - 1f) * 100f);
                    if (bonusPercent == 0) return;

                    if (__instance.WeaponDamageOnly)
                    {
                        if (evt.DamageBundle.WeaponDamage is EnergyDamage weaponDamage
                            && weaponDamage.EnergyType == DamageEnergyType.Holy)
                        {
                            evt.DamageBundle.WeaponDamage.BonusPercent += bonusPercent;
                        }
                    }
                    else
                    {
                        foreach (var damage in evt.DamageBundle)
                        {
                            if (damage is EnergyDamage energyDamage && energyDamage.EnergyType == DamageEnergyType.Holy)
                            {
                                damage.BonusPercent += bonusPercent;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.Log("[GoldenDragonBodyAdaptation] EnergyDamageBonus patch error: " + ex.Message);
                }
            }
        }
    }
}
