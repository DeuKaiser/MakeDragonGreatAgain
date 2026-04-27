using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.Localization;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp;
using Kingmaker.UnitLogic.Class.LevelUp.Actions;
using MDGA.Loc;

namespace MDGA.GeneralClasses.DragonheirScion
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonheirLegacy
    {
        internal static readonly BlueprintGuid FighterClassGuid = BlueprintGuid.Parse("48ac8db94d5de7645906c7d0ad3bcfbd");
        internal static readonly BlueprintGuid FighterFeatSelectionGuid = BlueprintGuid.Parse("41c8486641f7d6d4283ca9dae4147a9f");
        internal static readonly BlueprintGuid DragonheirScionArchetypeGuid = BlueprintGuid.Parse("8dff97413c63c1147be8a5ca229abefc");
        // 显示用血脉特性：龙族传承
        internal static readonly BlueprintGuid DragonLegacyFeatureGuid = BlueprintGuid.Parse("9f2f8c4a2b0f4a1d9d4b6f6c12345678");
        // 旧版隐藏进度仅保留为兼容/清理标记；新版不再把它挂到角色身上，避免存档序列化风险。
        internal static readonly BlueprintGuid DragonLegacyProgressionGuid = BlueprintGuid.Parse("9f2f8c4a2b0f4a1d9d4b6f6c12345679");
        internal static readonly BlueprintGuid DragonLegacyFeatSelectionGuid = BlueprintGuid.Parse("9f2f8c4a2b0f4a1d9d4b6f6c1234567a");

        // 14 条龙脉血承的 progression GUID
        internal static readonly BlueprintGuid DragonheirProgressionFireGuid         = BlueprintGuid.Parse("8e30b4dab152d4549bf9c0dbf901aadf");
        internal static readonly BlueprintGuid DragonheirProgressionColdGuid         = BlueprintGuid.Parse("ff7eb5969525b5b41b2c68328bc9bb7c");
        internal static readonly BlueprintGuid DragonheirAcidProgressionGuid         = BlueprintGuid.Parse("f09074860cc87fd4ebf6bf69ddd20d10");
        internal static readonly BlueprintGuid DragonheirProgressionElectricityGuid  = BlueprintGuid.Parse("07f6ba0f63d8d414f92b5d0a559455e1");
        internal static readonly BlueprintGuid DragonheirGoldProgressionGuid         = BlueprintGuid.Parse("5172cdce55b2455f878ff8c74c964a1e");
        internal static readonly BlueprintGuid DragonheirRedProgressionGuid          = BlueprintGuid.Parse("267dbd4789fa4b75a44294c7d1625bba");
        internal static readonly BlueprintGuid DragonheirSilverProgressionGuid       = BlueprintGuid.Parse("b6a90286e6894c7b8fbd52a29dda9f48");
        internal static readonly BlueprintGuid DragonheirWhiteProgressionGuid        = BlueprintGuid.Parse("1bdad91f7210419b9e5b4801d084e14b");
        internal static readonly BlueprintGuid DragonheirGreenProgressionGuid        = BlueprintGuid.Parse("db69413e69184b099f7825092c5dbc4f");
        internal static readonly BlueprintGuid DragonheirBlackProgressionGuid        = BlueprintGuid.Parse("6127949882384a5cb75074e4d77ceae3");
        internal static readonly BlueprintGuid DragonheirBlueProgressionGuid         = BlueprintGuid.Parse("3da0972ad5574bfd8a7de9bc5460e7e9");
        internal static readonly BlueprintGuid DragonheirBrassProgressionGuid        = BlueprintGuid.Parse("47f3f1a3349349fe9a7f68f8d2b6da5d");
        internal static readonly BlueprintGuid DragonheirBronzeProgressionGuid       = BlueprintGuid.Parse("df2807f0649c4237ab7c4d62a2acaaee");
        internal static readonly BlueprintGuid DragonheirCopperProgressionGuid       = BlueprintGuid.Parse("416ee6e6b4834bb8bd5afe8b08a69865");

        private static bool _completed;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (!Main.Enabled) return;
            TryImplementOrSchedule();
        }

        private static void TryImplementOrSchedule()
        {
            if (_completed) return;
            bool ready = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(FighterClassGuid) != null &&
                         ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(FighterFeatSelectionGuid) != null &&
                         ResourcesLibrary.TryGetBlueprint<BlueprintArchetype>(DragonheirScionArchetypeGuid) != null;
            if (!ready)
            {
                Main.Log("[DragonheirLegacy] Blueprints not ready; Dragonheir injection skipped.");
                return;
            }

            // 蓝图已就绪；在此处对龙之贵胄原型的 AddFeatures 做一次性注入。
            try
            {
                EnsureDragonLegacyBloodlineFeature();
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy] EnsureDragonLegacyBloodlineFeature error: " + ex);
            }

            _completed = true;
        }

        /// <summary>
        /// 将旧的“直接塞 1-20 级战斗专长槽”改成：
        /// 血脉 progression 只显示一个可见特性；真正的战斗专长选择在升级流程中临时加入 LevelUpState。
        /// </summary>
        private static void EnsureDragonLegacyBloodlineFeature()
        {
            var fighterSel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(FighterFeatSelectionGuid);
            var archetype = ResourcesLibrary.TryGetBlueprint<BlueprintArchetype>(DragonheirScionArchetypeGuid);
            if (fighterSel == null || archetype == null)
            {
                Main.Log("[DragonheirLegacy] EnsureDragonLegacyBloodlineFeature: required blueprints missing.");
                return;
            }

            RemoveDirectFighterFeatFromDragonheir(archetype);

            EnsureDragonLegacyFeatSelection(fighterSel);
            EnsureDragonLegacyProgression(fighterSel);
            var legacy = EnsureDragonLegacyFeature(fighterSel);
            EnsureDragonLegacyFeatureOnBloodlines(legacy);

            Main.Log("[DragonheirLegacy] DragonLegacy bloodline feature now queues save-stable per-level combat feat selections.");

            // 如启用详细日志，可输出最终布局，方便调试。
            DumpArchetypeFeatureLayout(archetype);
        }

        /// <summary>
        /// 清理早期实现向 DragonheirScion 原型 AddFeatures 直接添加的 FighterFeatSelection。
        /// 这里只碰原型 AddFeatures，不影响战士职业进度本身的普通奖励专长。
        /// </summary>
        private static void RemoveDirectFighterFeatFromDragonheir(BlueprintArchetype archetype)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var fiFeatures = typeof(LevelEntry).GetField("m_Features", flags);
                if (fiFeatures == null) return;

                var changed = false;
                var entries = (archetype.AddFeatures ?? Array.Empty<LevelEntry>()).ToList();
                foreach (var entry in entries.ToList())
                {
                    var raw = fiFeatures.GetValue(entry);
                    bool wasArray = raw is BlueprintFeatureBaseReference[];
                    var list = raw switch
                    {
                        BlueprintFeatureBaseReference[] arr => arr.ToList(),
                        System.Collections.Generic.List<BlueprintFeatureBaseReference> l => l.ToList(),
                        _ => new System.Collections.Generic.List<BlueprintFeatureBaseReference>()
                    };

                    int oldCount = list.Count;
                    list = list.Where(r => r == null || r.Guid != FighterFeatSelectionGuid).ToList();
                    if (list.Count == oldCount) continue;

                    changed = true;
                    if (list.Count == 0)
                    {
                        entries.Remove(entry);
                    }
                    else
                    {
                        object toStore = wasArray ? (object)list.ToArray() : list;
                        fiFeatures.SetValue(entry, toStore);
                    }
                }

                if (changed)
                {
                    archetype.AddFeatures = entries.OrderBy(le => le.Level).ToArray();
                    Main.Log("[DragonheirLegacy] Removed direct FighterFeatSelection entries from Dragonheir archetype AddFeatures.");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy] RemoveDirectFighterFeatFromDragonheir error: " + ex.Message);
            }
        }

        private static BlueprintFeatureSelection EnsureDragonLegacyFeatSelection(BlueprintFeatureSelection fighterSel)
        {
            var selection = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(DragonLegacyFeatSelectionGuid);
            if (selection == null)
            {
                selection = CreateBlueprint<BlueprintFeatureSelection>("MDGA_DragonLegacyFeatSelection", DragonLegacyFeatSelectionGuid);
                Register(selection);
                Main.Log("[DragonheirLegacy] Hidden DragonLegacy feat selection created and registered.");
            }

            selection.name = "MDGA_DragonLegacyFeatSelection";
            selection.IsClassFeature = true;
            selection.Ranks = 1;
            selection.HideInUI = false;
            selection.HideInCharacterSheetAndLevelUp = false;
            selection.HideNotAvailibleInUI = fighterSel.HideNotAvailibleInUI;
            selection.IgnorePrerequisites = fighterSel.IgnorePrerequisites;
            selection.ExceptWhiteListed = fighterSel.ExceptWhiteListed;
            selection.Obligatory = fighterSel.Obligatory;
            selection.Mode = fighterSel.Mode;
            selection.Group = fighterSel.Group;
            selection.Group2 = fighterSel.Group2;
            selection.ShowThisSelection = fighterSel.ShowThisSelection;
            CopySelectionOptions(fighterSel, selection);
            CopyIcon(fighterSel, selection);
            SetLocalizedStrings(
                selection,
                "MDGA_DragonLegacyFeatSelection_Name",
                "龙族传承专长",
                "Dragon Legacy Feat",
                "MDGA_DragonLegacyFeatSelection_Desc",
                "选择一项战斗专长。龙之贵胄每一级都会获得一次此选择。",
                "Select one combat feat. A Dragonheir Scion gains this choice at every level.");
            return selection;
        }

        private static BlueprintProgression EnsureDragonLegacyProgression(BlueprintFeatureSelection iconSource)
        {
            var progression = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(DragonLegacyProgressionGuid);
            if (progression == null)
            {
                progression = CreateBlueprint<BlueprintProgression>("MDGA_DragonLegacyProgression", DragonLegacyProgressionGuid);
                Register(progression);
                Main.Log("[DragonheirLegacy] Deprecated hidden DragonLegacy progression marker created and registered.");
            }

            progression.name = "MDGA_DragonLegacyProgression";
            progression.IsClassFeature = true;
            progression.Ranks = 1;
            progression.HideInUI = true;
            progression.HideInCharacterSheetAndLevelUp = true;
            progression.GiveFeaturesForPreviousLevels = false;
            CopyIcon(iconSource, progression);
            SetProgressionArchetype(progression, null);
            progression.LevelEntries = Array.Empty<LevelEntry>();
            SetLocalizedStrings(
                progression,
                "MDGA_DragonLegacyProgression_Name",
                "龙族传承奖励",
                "Dragon Legacy Rewards",
                "MDGA_DragonLegacyProgression_Desc",
                "兼容标记：旧版隐藏进度已停用，奖励专长现在由升级选择流程发放。",
                "Compatibility marker: the deprecated hidden progression is inactive; feat choices are now granted by the level-up flow.");
            return progression;
        }

        private static BlueprintFeature EnsureDragonLegacyFeature(BlueprintFeature iconSource)
        {
            var legacy = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(DragonLegacyFeatureGuid);
            if (legacy == null)
            {
                legacy = CreateBlueprint<BlueprintFeature>("MDGA_DragonLegacy", DragonLegacyFeatureGuid);
                Register(legacy);
                Main.Log("[DragonheirLegacy] DragonLegacy feature created and registered.");
            }

            legacy.name = "MDGA_DragonLegacy";
            legacy.IsClassFeature = true;
            legacy.Ranks = 1;
            legacy.HideInUI = false;
            legacy.HideInCharacterSheetAndLevelUp = false;
            CopyIcon(iconSource, legacy);
            SetLocalizedStrings(
                legacy,
                "MDGA_DragonLegacy_Name",
                "龙族传承",
                "Dragon Legacy",
                "MDGA_DragonLegacy_Desc",
                "龙血为战士们带来了新的力量：每一级龙之贵胄都会额外获得一次战斗专长。",
                "Dragon blood empowers warriors: each Dragonheir Scion level grants one extra combat feat.");
            RemoveDeprecatedAddFeatureOnApply(legacy);
            return legacy;
        }

        /// <summary>
        /// 确保把可见的“龙族传承”血脉特性添加到所有龙裔血承 progression 的 1 级。
        /// </summary>
        private static void EnsureDragonLegacyFeatureOnBloodlines(BlueprintFeature legacy)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var allProgressions = new[]
                {
                    DragonheirProgressionFireGuid,
                    DragonheirProgressionColdGuid,
                    DragonheirAcidProgressionGuid,
                    DragonheirProgressionElectricityGuid,
                    DragonheirGoldProgressionGuid,
                    DragonheirRedProgressionGuid,
                    DragonheirSilverProgressionGuid,
                    DragonheirWhiteProgressionGuid,
                    DragonheirGreenProgressionGuid,
                    DragonheirBlackProgressionGuid,
                    DragonheirBlueProgressionGuid,
                    DragonheirBrassProgressionGuid,
                    DragonheirBronzeProgressionGuid,
                    DragonheirCopperProgressionGuid
                };

                foreach (var guid in allProgressions)
                {
                    var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(guid);
                    if (prog == null) continue;

                    var entries = prog.LevelEntries ?? Array.Empty<LevelEntry>();
                    var entryList = entries.ToList();
                    var entry = entryList.FirstOrDefault(le => le.Level == 1);
                    if (entry == null)
                    {
                        entry = new LevelEntry { Level = 1 };
                        entryList.Add(entry);
                    }

                    var fiFeat = typeof(LevelEntry).GetField("m_Features", flags);
                    if (fiFeat == null) continue;

                    var raw = fiFeat.GetValue(entry);
                    bool wasArray = raw is BlueprintFeatureBaseReference[];
                    var featureList = raw switch
                    {
                        BlueprintFeatureBaseReference[] arr => arr.ToList(),
                        System.Collections.Generic.List<BlueprintFeatureBaseReference> l => l.ToList(),
                        _ => new System.Collections.Generic.List<BlueprintFeatureBaseReference>()
                    };

                    if (!featureList.Any(r => r != null && r.Guid == DragonLegacyFeatureGuid))
                    {
                        featureList.Add(legacy.ToReference<BlueprintFeatureBaseReference>());
                        object toStore = wasArray ? (object)featureList.ToArray() : featureList;
                        fiFeat.SetValue(entry, toStore);
                    }

                    prog.LevelEntries = entryList.OrderBy(le => le.Level).ToArray();
                }

                Main.Log("[DragonheirLegacy] DragonLegacy feature ensured on all dragon bloodlines.");
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy] EnsureDragonLegacyFeatureOnBloodlines error: " + ex);
            }
        }

        private static T CreateBlueprint<T>(string name, BlueprintGuid guid) where T : SimpleBlueprint
        {
            T bp;
            try { bp = (T)Activator.CreateInstance(typeof(T)); }
            catch { bp = (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T)); }
            bp.name = name;
            AssignGuid(bp, guid);
            return bp;
        }

        private static void SetProgressionArchetype(BlueprintProgression progression, BlueprintArchetype archetype)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                typeof(BlueprintProgression).GetField("m_Classes", flags)
                    ?.SetValue(progression, Array.Empty<BlueprintProgression.ClassWithLevel>());
                typeof(BlueprintProgression).GetField("m_Archetypes", flags)
                    ?.SetValue(progression, archetype == null
                        ? Array.Empty<BlueprintProgression.ArchetypeWithLevel>()
                        : new[]
                        {
                            new BlueprintProgression.ArchetypeWithLevel
                            {
                                m_Archetype = archetype.ToReference<BlueprintArchetypeReference>(),
                                AdditionalLevel = 0
                            }
                        });
                typeof(BlueprintProgression).GetField("m_AlternateProgressionClasses", flags)
                    ?.SetValue(progression, Array.Empty<BlueprintProgression.ClassWithLevel>());
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy] SetProgressionArchetype error: " + ex.Message);
            }
        }

        private static void RemoveDeprecatedAddFeatureOnApply(BlueprintFeature owner)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var f = typeof(BlueprintScriptableObject).GetField("m_Components", flags)
                    ?? typeof(BlueprintScriptableObject).GetField("Components", flags);
                if (f == null) return;

                var arr = (f.GetValue(owner) as BlueprintComponent[]) ?? Array.Empty<BlueprintComponent>();
                var filtered = arr
                    .Where(c => !(c is AddFeatureOnApply add && add.Feature != null && add.Feature.AssetGuid == DragonLegacyProgressionGuid))
                    .ToArray();
                if (filtered.Length == arr.Length) return;

                f.SetValue(owner, filtered);
                Main.Log("[DragonheirLegacy] Removed deprecated AddFeatureOnApply from DragonLegacy feature.");
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy] RemoveDeprecatedAddFeatureOnApply error: " + ex.Message);
            }
        }

        private static void SetLocalizedStrings(BlueprintUnitFact bp, string nameKeyBase, string nameZh, string nameEn, string descKeyBase, string descZh, string descEn)
        {
            try
            {
                string nameKeyZh = nameKeyBase + "_zh";
                string nameKeyEn = nameKeyBase + "_en";
                string descKeyZh = descKeyBase + "_zh";
                string descKeyEn = descKeyBase + "_en";

                LocalizationInjector.RegisterDynamicKey(nameKeyZh, nameZh);
                LocalizationInjector.RegisterDynamicKey(nameKeyEn, nameEn);
                LocalizationInjector.RegisterDynamicKey(descKeyZh, descZh);
                LocalizationInjector.RegisterDynamicKey(descKeyEn, descEn);

                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var fName = typeof(BlueprintUnitFact).GetField("m_DisplayName", flags);
                var fDesc = typeof(BlueprintUnitFact).GetField("m_Description", flags);
                if (fName == null || fDesc == null) return;

                var nameLoc = Activator.CreateInstance(fName.FieldType);
                var descLoc = Activator.CreateInstance(fDesc.FieldType);
                var locFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                bool zh = IsChinese();

                nameLoc.GetType().GetField("m_Key", locFlags)?.SetValue(nameLoc, zh ? nameKeyZh : nameKeyEn);
                nameLoc.GetType().GetField("m_Text", locFlags)?.SetValue(nameLoc, zh ? nameZh : nameEn);
                descLoc.GetType().GetField("m_Key", locFlags)?.SetValue(descLoc, zh ? descKeyZh : descKeyEn);
                descLoc.GetType().GetField("m_Text", locFlags)?.SetValue(descLoc, zh ? descZh : descEn);

                fName.SetValue(bp, nameLoc);
                fDesc.SetValue(bp, descLoc);
                LocalizationInjector.EnsureInjected();
            }
            catch
            {
                // 本地化失败不影响功能
            }
        }

        private static void CopyIcon(BlueprintUnitFact src, BlueprintUnitFact dst)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var iconField = typeof(BlueprintUnitFact).GetField("m_Icon", flags);
                iconField?.SetValue(dst, iconField.GetValue(src));
            }
            catch { }
        }

        private static void CopySelectionOptions(BlueprintFeatureSelection src, BlueprintFeatureSelection dst)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                foreach (var fieldName in new[] { "m_AllFeatures", "m_Features" })
                {
                    var f = typeof(BlueprintFeatureSelection).GetField(fieldName, flags);
                    if (f == null) continue;
                    var val = f.GetValue(src);
                    if (val is BlueprintFeatureReference[] arr) f.SetValue(dst, arr.ToArray());
                    else if (val is System.Collections.Generic.List<BlueprintFeatureReference> list) f.SetValue(dst, list.ToList());
                }
                typeof(BlueprintFeatureSelection).GetField("m_Mode", flags)?.SetValue(dst, typeof(BlueprintFeatureSelection).GetField("m_Mode", flags)?.GetValue(src));
                var pGroup = typeof(BlueprintFeature).GetProperty("Group", flags);
                if (pGroup != null && pGroup.CanWrite) pGroup.SetValue(dst, pGroup.GetValue(src));
            }
            catch (Exception ex) { Main.Log("[DragonheirLegacy] CopySelectionOptions error: " + ex.Message); }
        }

        private static void Register(SimpleBlueprint bp)
        {
            try { ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp); }
            catch (Exception ex) { Main.Log("[DragonheirLegacy] Register error: " + ex.Message); }
        }

        private static void AssignGuid(SimpleBlueprint bp, BlueprintGuid guid)
        {
            try
            {
                var f = bp.GetType().GetField("AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? bp.GetType().GetField("m_AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                f?.SetValue(bp, guid);
            }
            catch { }
        }

        private static void DumpArchetypeFeatureLayout(BlueprintArchetype archetype)
        {
            if (archetype == null || Main.Settings == null || !Main.Settings.VerboseLogging) return;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var fAdd = typeof(BlueprintArchetype).GetField("m_AddFeatures", flags);
                var adds = fAdd?.GetValue(archetype) as LevelEntry[] ?? Array.Empty<LevelEntry>();
                Main.Log("[DragonheirLegacy][Dump] Archetype AddFeatures count=" + adds.Length);
                foreach (var le in adds.OrderBy(l => l.Level))
                {
                    var fiFeat = typeof(LevelEntry).GetField("m_Features", flags);
                    var raw = fiFeat?.GetValue(le);
                    var list = raw is BlueprintFeatureBaseReference[] arr ? arr : raw is System.Collections.Generic.List<BlueprintFeatureBaseReference> l ? l.ToArray() : Array.Empty<BlueprintFeatureBaseReference>();
                    Main.Log($"[DragonheirLegacy][Dump] Add L{le.Level}: " + string.Join(",", list.Select(r => r?.Get()?.name ?? "<null>")));
                }
            }
            catch (Exception ex) { Main.Log("[DragonheirLegacy][Dump] Archetype layout error: " + ex.Message); }
        }

        // 本地语言判断：与其他 patch 中实现保持一致
        private static bool IsChinese()
        {
            try
            {
                var loc = LocalizationManager.CurrentLocale;
                if (loc != null)
                {
                    string locStr = loc.ToString();
                    if (!string.IsNullOrEmpty(locStr) && locStr.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var langProp = loc.GetType().GetProperty("Language", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var langObj = langProp?.GetValue(loc, null);
                    if (langObj != null && langObj.ToString().ToLower().StartsWith("zh")) return true;
                }
            }
            catch { }
            try
            {
                var ci = System.Globalization.CultureInfo.CurrentUICulture;
                if (ci != null && ci.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            try
            {
                if (UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.ChineseSimplified ||
                    UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.Chinese ||
                    UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.ChineseTraditional)
                    return true;
            }
            catch { }
            return false;
        }

        internal static void CleanupKnownRuntimeState()
        {
            try
            {
                var player = Game.Instance?.Player;
                if (player == null) return;

                foreach (var ch in player.AllCharacters ?? Enumerable.Empty<UnitEntityData>())
                {
                    var descriptor = ch?.Descriptor;
                    if (descriptor != null)
                        RemoveDeprecatedHiddenProgression(descriptor);
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy] CleanupKnownRuntimeState error: " + ex.Message);
            }
        }

        internal static void EnsureLevelUpSelection(UnitDescriptor unit, LevelUpState state)
        {
            try
            {
                if (!Main.Enabled || unit == null || state == null) return;

                RemoveDeprecatedHiddenProgression(unit);

                var selectedClass = state.SelectedClass;
                if (selectedClass == null || selectedClass.AssetGuid != FighterClassGuid) return;
                if (state.NextClassLevel < 1 || state.NextClassLevel > 20) return;

                var classData = unit.Progression.GetClassData(selectedClass);
                if (classData == null || !classData.Archetypes.Any(a => a != null && a.AssetGuid == DragonheirScionArchetypeGuid))
                    return;

                var selection = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(DragonLegacyFeatSelectionGuid);
                if (selection == null) return;

                if (state.Selections.Any(s => s != null && s.Selection == selection && s.Level == state.NextClassLevel))
                    return;

                state.AddSelection(null, selectedClass, selection, state.NextClassLevel);
                if (Main.Settings != null && Main.Settings.VerboseLogging)
                    Main.Log("[DragonheirLegacy] Queued DragonLegacy feat selection for Dragonheir level " + state.NextClassLevel + ".");
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy] EnsureLevelUpSelection error: " + ex.Message);
            }
        }

        private static void RemoveDeprecatedHiddenProgression(UnitDescriptor unit)
        {
            try
            {
                if (unit == null) return;

                var hiddenProgression = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(DragonLegacyProgressionGuid);
                if (hiddenProgression == null) return;

                var progressionData = unit.Progression;
                if (progressionData == null) return;

                var fact = progressionData.Features?.GetFact(hiddenProgression);
                if (fact != null)
                {
                    progressionData.Features.RemoveFact(fact);
                    if (Main.Settings != null && Main.Settings.VerboseLogging)
                        Main.Log("[DragonheirLegacy] Removed deprecated hidden DragonLegacy progression fact from unit.");
                }

                var field = typeof(UnitProgressionData).GetField("m_Progressions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field?.GetValue(progressionData) is System.Collections.IDictionary dict && dict.Contains(hiddenProgression))
                {
                    dict.Remove(hiddenProgression);
                    if (Main.Settings != null && Main.Settings.VerboseLogging)
                        Main.Log("[DragonheirLegacy] Removed deprecated hidden DragonLegacy progression data from unit.");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy] RemoveDeprecatedHiddenProgression error: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(ApplyClassMechanics), nameof(ApplyClassMechanics.Apply))]
    internal static class DragonheirLegacyLevelUpSelectionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(LevelUpState state, UnitDescriptor unit)
        {
            DragonheirLegacy.EnsureLevelUpSelection(unit, state);
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRoutine))]
    internal static class DragonheirLegacyBeforeSaveCleanupPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            DragonheirLegacy.CleanupKnownRuntimeState();
        }
    }
}
