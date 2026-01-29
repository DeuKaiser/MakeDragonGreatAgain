using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.RuleSystem;
using System.Collections.Generic;
using BlueprintCore.Blueprints.CustomConfigurators.Classes;
using BlueprintCore.Utils.Types;
using BlueprintCore.Utils;
using MDGA.Loc;

namespace MDGA.GeneralClasses.DraconicBloodline
{
    //魅力至臻（蒙卡雷计划）
    //金龙蒙卡雷的计划虽然被中止了，但是一部分研究成果流传了出来，对于特别的金龙血脉的术士来说，这份特殊的研究成果进一步激发了金龙血承的力量，它会强化术士的灵魂。当术士的魅力不足24时，将补足至24。
    internal class CharismaAdd
    {
        private static bool _implemented;

        // New feature GUID (deterministic). Change if collision occurs.
        private static readonly BlueprintGuid FeatureGuid = BlueprintGuid.Parse("0f9b7e55b1a8440bb6c3f6b8d9f9e6b1");
        private const string FeatureName = "MDGA_SorcGold_CharismaFloor24";

        // Existing assets
        private static readonly BlueprintGuid GoldProgressionGuid = BlueprintGuid.Parse("6c67ef823db8d7d45bb0ef82f959743d");
        private static readonly BlueprintGuid GoldWyrmsIconFeatureGuid = BlueprintGuid.Parse("3247396087a747148b17e1a0e37a3e67");

        [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
        private static class BlueprintsCache_Init_Patch
        {
            private static void Postfix()
            {
                try
                {
                    if (_implemented) return;
                    _implemented = true;
                    Implement();
                }
                catch (Exception ex)
                {
                    Main.Log("[CharismaAdd] Exception: " + ex);
                }
            }
        }

        private static void Implement()
        {
            // Create feature if missing
            var existing = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(FeatureGuid);
            if (existing == null)
            {
                var iconSource = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(GoldWyrmsIconFeatureGuid);

                // Localization: follow TrueDragon pattern
                bool isZh = IsChineseLocaleSafe();
                string dispZh = "蒙卡雷计划";
                string descZh = "蒙卡雷计划：虽然蒙卡雷被秘密逮捕后其计划被迫终止与封存，但是其研究成果还是随着参与者的离去而带到了葛拉里昂各地，对于特别的金龙血脉的术士，这份研究进一步强化了术士的灵魂与守护。当术士的防御（AC）不足24时，将补足至24；同时术士的魅力+4。";
                string dispEn = "Mengkare Project";
                string descEn = "Mengkare Project: Though Mengkare was secretly arrested and his plan was terminated and sealed, fragments of his research still spread across Golarion with the departure of its participants. For sorcerers bearing a special strain of the golden draconic bloodline, this research further strengthens their soul and protection. If the sorcerer’s Armor Class (AC) is below 24, it is raised to 24; additionally, the sorcerer gains +4 Charisma.";
                string nameText = Sanitize(isZh ? dispZh : dispEn);
                string descText = Sanitize(isZh ? descZh : descEn);

                // Build LocalizedString objects and assign during construction
                var nameLS = new Kingmaker.Localization.LocalizedString();
                var descLS = new Kingmaker.Localization.LocalizedString();
                try
                {
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                    nameLS.GetType().GetField("m_Text", flags)?.SetValue(nameLS, nameText);
                    descLS.GetType().GetField("m_Text", flags)?.SetValue(descLS, descText);
                    // keep m_Key empty to avoid external overwrites; rely on embedded text
                }
                catch { }

                var cfg = FeatureConfigurator.New(FeatureName, FeatureGuid.ToString())
                    .SetIsClassFeature(true)
                    .SetIcon(iconSource != null ? iconSource.Icon : null)
                    .SetDisplayName(nameLS)
                    .SetDescription(descLS)
                    // Rank from BaseStat Charisma, clamped to 24
                    .AddContextRankConfig(new ContextRankConfig
                    {
                        m_Type = AbilityRankType.Default,
                        m_BaseValueType = ContextRankBaseValueType.BaseStat,
                        m_Stat = StatType.AC,
                        m_UseMax = true,
                        m_Max = 24
                    })
                    // Shared value = Rank + (-24)
                    .AddContextCalculateSharedValue(valueType: AbilitySharedValue.Damage, modifier: 1.0,
                        value: new ContextDiceValue
                        {
                            DiceType = DiceType.One,
                            DiceCountValue = ContextValues.Rank(),
                            BonusValue = ContextValues.Constant(-24)
                        })
                    // Apply AC bonus = -(Rank - 24) = 24 - Rank, UntypedStackable
                    .AddContextStatBonus(StatType.AC, ContextValues.Shared(AbilitySharedValue.Damage), ModifierDescriptor.UntypedStackable, minimal: 0, multiplier: -1)
                    // Flat +4 Charisma
                    .AddStatBonus(stat: StatType.Charisma, value: 4, descriptor: ModifierDescriptor.UntypedStackable)
                    // Auto-recalc on AC change
                    .AddComponent<RecalculateOnStatChange>(c => { c.Stat = StatType.AC; c.UseKineticistMainStat = false; })
                    .Configure();
                // Display name and description embedded above
            }

            // Final localization enforcement: bind distinct keys and texts on the blueprint instance
            try
            {
                var feat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(FeatureGuid);
                if (feat != null)
                {
                    bool isZh = IsChineseLocaleSafe();
                    string tZh = "蒙卡雷计划";
                    string dZh = "蒙卡雷计划：虽然蒙卡雷被秘密逮捕后其计划被迫终止与封存，但是其研究成果还是随着参与者的离去而带到了葛拉里昂各地，对于特别的金龙血脉的术士，这份研究进一步强化了术士的灵魂与守护。当术士的防御（AC）不足24时，将补足至24；同时术士的魅力+4。";
                    string nameText = Sanitize(isZh ? tZh : "Mengkare Project");
                    string descText = Sanitize(isZh ? dZh : "Mengkare Project: Though Mengkare was secretly arrested and his plan was terminated and sealed, fragments of his research still spread across Golarion with the departure of its participants. For sorcerers bearing a special strain of the golden draconic bloodline, this research further strengthens their soul and protection. If the sorcerer’s Armor Class (AC) is below 24, it is raised to 24; additionally, the sorcerer gains +4 Charisma.");
                    var nameKey = "MDGA.SorcGold.CharismaFloor24.Name";
                    var descKey = "MDGA.SorcGold.CharismaFloor24.Desc";
                    LocalizationInjector.RegisterDynamicKey(nameKey, nameText);
                    LocalizationInjector.RegisterDynamicKey(descKey, descText);

                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                    System.Reflection.FieldInfo fDisp = null, fDesc = null; System.Type t = feat.GetType();
                    while (t != null && (fDisp == null || fDesc == null)) { fDisp ??= t.GetField("m_DisplayName", flags); fDesc ??= t.GetField("m_Description", flags); t = t.BaseType; }
                    var disp = fDisp?.GetValue(feat);
                    var desc = fDesc?.GetValue(feat);
                    var keyF = disp?.GetType().GetField("m_Key", flags);
                    var textF = disp?.GetType().GetField("m_Text", flags);
                    keyF?.SetValue(disp, nameKey);
                    textF?.SetValue(disp, nameText);
                    var keyF2 = desc?.GetType().GetField("m_Key", flags);
                    var textF2 = desc?.GetType().GetField("m_Text", flags);
                    keyF2?.SetValue(desc, descKey);
                    textF2?.SetValue(desc, descText);
                }
            }
            catch { }

            // Attach to Gold Draconic Bloodline progression level 1
            var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(GoldProgressionGuid);
            if (prog == null)
            {
                Main.Log("[CharismaAdd] Gold progression not found: " + GoldProgressionGuid);
                return;
            }

            var featureBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(FeatureGuid);
            var featureRef = featureBlueprint != null ? featureBlueprint.ToReference<BlueprintFeatureBaseReference>() : null;
            var level1 = prog.LevelEntries?.FirstOrDefault(le => le.Level == 1);
            if (level1 == null)
            {
                level1 = new LevelEntry { Level = 1 };
                prog.LevelEntries = prog.LevelEntries.Append(level1).ToArray();
            }

            if (featureRef != null && !level1.m_Features.Any(f => f?.deserializedGuid == FeatureGuid))
            {
                if (level1.m_Features == null)
                {
                    level1.m_Features = new List<BlueprintFeatureBaseReference>();
                }
                level1.m_Features.Add(featureRef);
                Main.Log("[CharismaAdd] Injected charisma floor feature into Gold Draconic Bloodline L1.");
            }
        }

        private static bool IsChineseLocaleSafe()
        {
            try
            {
                return (bool)typeof(DragonMightOverrideHelper)
                    .GetMethod("LocalizationInjectorExtension_IsChinese", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .Invoke(null, null);
            }
            catch { }
            return false;
        }

        private static string Sanitize(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return s;
                // strip zero-width and BOM-like characters that can break glyph rendering
                char[] invalid = new[] { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u2060', '\u00A0' };
                return new string(s.Where(ch => Array.IndexOf(invalid, ch) < 0).ToArray());
            }
            catch { return s; }
        }
    }
}
