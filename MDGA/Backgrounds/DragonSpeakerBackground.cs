using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Localization;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.FactLogic;
using MDGA.Loc;

namespace MDGA.Backgrounds
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonSpeakerBackground
    {
        internal static readonly BlueprintGuid FeatureGuid = BlueprintGuid.Parse("31c8f3616d4b4d40a94f42654a7e7ef1");

        private static readonly BlueprintGuid ScholarSelectionGuid = BlueprintGuid.Parse("273fab44409035f42a7e2af0858a463d");
        private static bool _done;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return;
            _done = true;
            if (!Main.Enabled) return;

            try
            {
                var feature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(FeatureGuid);
                if (feature == null)
                {
                    feature = CreateFeature();
                    Register(feature);
                }
                else
                {
                    EnsureFeatureShape(feature);
                }

                InjectIntoScholarSelection(feature);
            }
            catch (Exception ex)
            {
                Main.Log("[DragonSpeaker] Init exception: " + ex);
            }
        }

        private static BlueprintFeature CreateFeature()
        {
            BlueprintFeature feature;
            try { feature = (BlueprintFeature)Activator.CreateInstance(typeof(BlueprintFeature)); }
            catch { feature = (BlueprintFeature)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintFeature)); }

            feature.name = "MDGA_DragonSpeakerBackground";
            SetGuid(feature, FeatureGuid);
            EnsureFeatureShape(feature);
            return feature;
        }

        private static void EnsureFeatureShape(BlueprintFeature feature)
        {
            feature.IsClassFeature = true;
            feature.Ranks = 1;
            feature.HideInUI = false;
            feature.HideInCharacterSheetAndLevelUp = false;
            feature.HideNotAvailibleInUI = false;
            feature.Groups = Array.Empty<FeatureGroup>();
            feature.ReapplyOnLevelUp = false;

            ApplyLocalization(feature);
            feature.ComponentsArray = EnsureComponentNames(new BlueprintComponent[]
            {
                NewComponent<AddClassSkill>("UseMagicDeviceClassSkill", c => c.Skill = StatType.SkillUseMagicDevice),
                NewComponent<AddClassSkill>("KnowledgeArcanaClassSkill", c => c.Skill = StatType.SkillKnowledgeArcana),
                NewComponent<AddBackgroundClassSkill>("UseMagicDeviceBackgroundSkill", c => c.Skill = StatType.SkillUseMagicDevice),
                NewComponent<AddBackgroundClassSkill>("KnowledgeArcanaBackgroundSkill", c => c.Skill = StatType.SkillKnowledgeArcana),
                NewComponent<AddCasterLevel>("CasterLevelTraitBonus", c =>
                {
                    c.Bonus = 2;
                    c.Descriptor = ModifierDescriptor.Trait;
                })
            });
        }

        private static T NewComponent<T>(string nameSuffix, Action<T> configure) where T : BlueprintComponent
        {
            T component;
            try { component = (T)Activator.CreateInstance(typeof(T)); }
            catch { component = (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T)); }

            component.name = "$" + typeof(T).Name + "$MDGA_DragonSpeaker_" + nameSuffix;
            configure?.Invoke(component);
            return component;
        }

        private static void InjectIntoScholarSelection(BlueprintFeature feature)
        {
            var selection = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(ScholarSelectionGuid);
            if (selection == null)
            {
                Main.Log("[DragonSpeaker] Scholar background selection not found.");
                return;
            }

            var featureRef = feature.ToReference<BlueprintFeatureReference>();
            bool changed = false;
            changed |= AppendFeatureReference(selection, "m_Features", featureRef);
            changed |= AppendFeatureReference(selection, "m_AllFeatures", featureRef);

            if (changed)
            {
                Main.Log("[DragonSpeaker] Injected Dragon Speaker into Scholar background selection.");
            }
        }

        private static bool AppendFeatureReference(BlueprintFeatureSelection selection, string fieldName, BlueprintFeatureReference featureRef)
        {
            var field = typeof(BlueprintFeatureSelection).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) return false;

            var current = (BlueprintFeatureReference[])field.GetValue(selection) ?? Array.Empty<BlueprintFeatureReference>();
            if (current.Any(r => r != null && r.Guid == FeatureGuid)) return false;

            field.SetValue(selection, current.Append(featureRef).ToArray());
            return true;
        }

        private static void Register(SimpleBlueprint blueprint)
        {
            try
            {
                ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(blueprint.AssetGuid, blueprint);
                if (Main.Settings?.VerboseLogging == true)
                    Main.Log("[DragonSpeaker] Registered background feature guid=" + blueprint.AssetGuid);
            }
            catch (Exception ex)
            {
                Main.Log("[DragonSpeaker] Register error: " + ex.Message);
            }
        }

        private static void ApplyLocalization(BlueprintFeature feature)
        {
            const string nameZh = "龙语者";
            const string descZh = "龙语者使用龙语施法，超魔瞬发占用的施法者等级比正常状况降低3级，此外，龙语者的施法者等级获得+2特性加值。";
            const string nameEn = "Dragon Speaker";
            const string descEn = "Dragon Speakers cast through Draconic. Quicken Spell increases the spell slot level by 3 less than normal, and the Dragon Speaker gains a +2 trait bonus to caster level.";

            const string nameKeyZh = "MDGA_DragonSpeakerBackground_Name_zh";
            const string descKeyZh = "MDGA_DragonSpeakerBackground_Desc_zh";
            const string nameKeyEn = "MDGA_DragonSpeakerBackground_Name_en";
            const string descKeyEn = "MDGA_DragonSpeakerBackground_Desc_en";

            LocalizationInjector.RegisterDynamicKey(nameKeyZh, nameZh);
            LocalizationInjector.RegisterDynamicKey(descKeyZh, descZh);
            LocalizationInjector.RegisterDynamicKey(nameKeyEn, nameEn);
            LocalizationInjector.RegisterDynamicKey(descKeyEn, descEn);

            bool zh = IsChinese();
            SetLocalizedField(feature, "m_DisplayName", zh ? nameKeyZh : nameKeyEn, zh ? nameZh : nameEn);
            SetLocalizedField(feature, "m_Description", zh ? descKeyZh : descKeyEn, zh ? descZh : descEn);
            try { LocalizationInjector.EnsureInjected(); } catch { }
        }

        private static void SetLocalizedField(BlueprintFeature feature, string fieldName, string key, string text)
        {
            var field = typeof(BlueprintUnitFact).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) return;

            object loc;
            try { loc = Activator.CreateInstance(typeof(LocalizedString)); }
            catch { loc = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(LocalizedString)); }

            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            loc.GetType().GetField("m_Key", flags)?.SetValue(loc, key);
            loc.GetType().GetField("m_Text", flags)?.SetValue(loc, text);
            loc.GetType().GetField("Shared", flags)?.SetValue(loc, null);
            field.SetValue(feature, loc);
        }

        private static BlueprintComponent[] EnsureComponentNames(BlueprintComponent[] components)
        {
            if (components == null) return Array.Empty<BlueprintComponent>();
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null) continue;
                if (string.IsNullOrEmpty(component.name))
                {
                    component.name = "$" + component.GetType().Name + "$MDGA_DragonSpeaker_" + i;
                }
            }
            return components;
        }

        private static void SetGuid(SimpleBlueprint blueprint, BlueprintGuid guid)
        {
            try
            {
                var field = blueprint.GetType().GetField("AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? blueprint.GetType().GetField("m_AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? blueprint.GetType().GetField("m_Guid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                field?.SetValue(blueprint, guid);
            }
            catch { }
        }

        internal static BlueprintFeature GetFeature()
        {
            return ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(FeatureGuid);
        }

        internal static bool HasDragonSpeaker(UnitDescriptor descriptor)
        {
            try
            {
                var feature = GetFeature();
                return feature != null && descriptor != null && descriptor.HasFact(feature);
            }
            catch { return false; }
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
                    var language = locale.GetType().GetProperty("Language", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(locale, null);
                    if (language != null && language.ToString().StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }

            try
            {
                return System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(RuleApplyMetamagic), nameof(RuleApplyMetamagic.OnTrigger))]
    internal static class DragonSpeakerRuleApplyMetamagicPatch
    {
        [HarmonyPrefix]
        private static void Prefix(RuleApplyMetamagic __instance)
        {
            try
            {
                if (!Main.Enabled) return;
                if (__instance == null || !__instance.AppliedMetamagics.Contains(Metamagic.Quicken)) return;
                if (!DragonSpeakerBackground.HasDragonSpeaker(__instance.Spellbook?.Owner)) return;

                __instance.ReduceCost(3);
            }
            catch (Exception ex)
            {
                if (Main.Settings?.VerboseLogging == true)
                    Main.Log("[DragonSpeaker] RuleApplyMetamagic patch error: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(RuleCollectMetamagic), nameof(RuleCollectMetamagic.AddMetamagic))]
    internal static class DragonSpeakerRuleCollectMetamagicPatch
    {
        private static readonly FieldInfo SpellLevelField = AccessTools.Field(typeof(RuleCollectMetamagic), "m_SpellLevel");

        [HarmonyPostfix]
        private static void Postfix(RuleCollectMetamagic __instance, Feature metamagicFeature)
        {
            try
            {
                if (!Main.Enabled) return;
                if (__instance == null || metamagicFeature == null) return;
                if (!DragonSpeakerBackground.HasDragonSpeaker(__instance.Spellbook?.Owner)) return;

                var component = metamagicFeature.GetComponent<AddMetamagicFeat>();
                if (component == null || component.Metamagic != Metamagic.Quicken) return;
                if (__instance.Spell == null) return;
                if ((__instance.Spell.AvailableMetamagic & Metamagic.Quicken) != Metamagic.Quicken) return;
                if (__instance.SpellMetamagics.Contains(metamagicFeature)) return;

                int spellLevel = (int)(SpellLevelField?.GetValue(__instance) ?? -1);
                if (spellLevel < 0 || spellLevel >= 10) return;

                int cost = Metamagic.Quicken.DefaultCost();
                if (__instance.Spellbook?.Owner?.Unit?.Descriptor?.State?.Features?.FavoriteMetamagicQuicken == true)
                {
                    cost--;
                }
                cost = Math.Max(0, cost - 3);

                if (spellLevel + cost <= 10)
                {
                    __instance.SpellMetamagics.Add(metamagicFeature);
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings?.VerboseLogging == true)
                    Main.Log("[DragonSpeaker] RuleCollectMetamagic patch error: " + ex.Message);
            }
        }
    }
}
