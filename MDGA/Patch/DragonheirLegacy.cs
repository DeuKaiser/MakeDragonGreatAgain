using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Localization;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.FactLogic; // AddFacts (kept for potential future use)

namespace MDGA.Patch
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonheirLegacy
    {
        internal static readonly BlueprintGuid FighterClassGuid = BlueprintGuid.Parse("48ac8db94d5de7645906c7d0ad3bcfbd");
        internal static readonly BlueprintGuid FighterFeatSelectionGuid = BlueprintGuid.Parse("41c8486641f7d6d4283ca9dae4147a9f");
        internal static readonly BlueprintGuid DragonheirScionArchetypeGuid = BlueprintGuid.Parse("8dff97413c63c1147be8a5ca229abefc");
        // 新建的说明用特性：龙族传承
        internal static readonly BlueprintGuid DragonLegacyFeatureGuid = BlueprintGuid.Parse("9f2f8c4a2b0f4a1d9d4b6f6c12345678");

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
                InjectFighterFeatIntoDragonheir();
                EnsureDragonLegacyFeatureOnBloodlines();
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy] InjectFighterFeatIntoDragonheir error: " + ex);
            }

            _completed = true;
        }

        /// <summary>
        /// 在 DragonheirScion 原型的 AddFeatures 里，为 1-20 级每级追加一次 FighterFeatSelection，
        /// 从而让该原型的战士在对应等级多获得一次战士奖励专长选择。
        /// 不新建任何蓝图，只修改现有 LevelEntry 数组，全部通过反射访问内部字段，避免编译期错误。
        /// </summary>
        private static void InjectFighterFeatIntoDragonheir()
        {
            var fighterSel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(FighterFeatSelectionGuid);
            var archetype = ResourcesLibrary.TryGetBlueprint<BlueprintArchetype>(DragonheirScionArchetypeGuid);
            if (fighterSel == null || archetype == null)
            {
                Main.Log("[DragonheirLegacy] InjectFighterFeatIntoDragonheir: required blueprints missing.");
                return;
            }

            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            // 直接通过公开属性 AddFeatures 访问/写回 LevelEntry 数组。
            var rawAdd = archetype.AddFeatures ?? Array.Empty<LevelEntry>();
            var addList = rawAdd.ToList();

            // LevelEntry 上的 m_Features 字段，通过反射访问。
            var fiFeatures = typeof(LevelEntry).GetField("m_Features", flags);
            if (fiFeatures == null)
            {
                Main.Log("[DragonheirLegacy] InjectFighterFeatIntoDragonheir: LevelEntry.m_Features field not found.");
                return;
            }

            // 1-20 级逐级处理。
            for (int lvl = 1; lvl <= 20; lvl++)
            {
                var entry = addList.FirstOrDefault(le => le.Level == lvl);
                if (entry == null)
                {
                    entry = new LevelEntry { Level = lvl };
                    fiFeatures.SetValue(entry, null); // 先置空，下面统一初始化
                    addList.Add(entry);
                }

                var rawFeat = fiFeatures.GetValue(entry);

                // 记录原始类型，用于回写时保持一致
                bool wasArray = rawFeat is BlueprintFeatureBaseReference[];

                var list = rawFeat switch
                {
                    BlueprintFeatureBaseReference[] arr => arr.ToList(),
                    System.Collections.Generic.List<BlueprintFeatureBaseReference> l => l,
                    _ => new System.Collections.Generic.List<BlueprintFeatureBaseReference>()
                };

                // 如果这一等级已经包含 FighterFeatSelection，就不重复添加。
                if (list.Any(fr => fr != null && fr.Guid == FighterFeatSelectionGuid))
                {
                    continue;
                }

                list.Add(fighterSel.ToReference<BlueprintFeatureBaseReference>());

                object toStore = wasArray ? (object)list.ToArray() : list;
                fiFeatures.SetValue(entry, toStore);
            }

            // 按等级排序后写回原型属性。
            var finalArray = addList.OrderBy(le => le.Level).ToArray();
            archetype.AddFeatures = finalArray;

            Main.Log("[DragonheirLegacy] FighterFeatSelection injected into Dragonheir archetype levels 1-20.");

            // 如启用详细日志，可输出最终布局，方便调试。
            DumpArchetypeFeatureLayout(archetype);
        }

        /// <summary>
        /// 确保创建一个纯说明用的“龙族传承”特性，并将其添加到 10 条龙脉血承 progression 的 1 级。
        /// </summary>
        private static void EnsureDragonLegacyFeatureOnBloodlines()
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                // 1. 获取说明用特性；如果不存在，就直接跳过说明注入逻辑（不影响功能）。
                // 1. 获取说明用特性；如果不存在就创建并注册。
                var legacy = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(DragonLegacyFeatureGuid);
                if (legacy == null)
                {
                    legacy = CreateDragonLegacyFeature();
                    Register(legacy);
                    Main.Log("[DragonheirLegacy] DragonLegacyFeature created and registered.");
                }

                // 2. 把这个特性挂到 10 条 progression 的 1 级
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
                        System.Collections.Generic.List<BlueprintFeatureBaseReference> l => l,
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

        /// <summary>
        /// 动态创建“龙族传承”说明特性，使用与 DragonBloodBoiling/DragonGodFavor 相同的本地化注入套路。
        /// </summary>
        private static BlueprintFeature CreateDragonLegacyFeature()
        {
            BlueprintFeature bp;
            try { bp = (BlueprintFeature)Activator.CreateInstance(typeof(BlueprintFeature)); }
            catch { bp = (BlueprintFeature)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintFeature)); }

            bp.name = "MDGA_DragonLegacy";
            AssignGuid(bp, DragonLegacyFeatureGuid);
            bp.IsClassFeature = true;
            bp.Ranks = 1;

            try
            {
                const string nameZh = "龙族传承";
                const string descZh = "龙血为战士们带来了新的力量：每一级龙之贵胄都会额外获得一次战斗专长。";
                const string nameEn = "Dragon Legacy";
                const string descEn = "Dragon blood empowers warriors: each Dragonheir Scion level grants one extra combat feat.";

                // 注册动态本地化 key，方便其他系统引用/覆盖
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonLegacy_Name_zh", nameZh);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonLegacy_Desc_zh", descZh);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonLegacy_Name_en", nameEn);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonLegacy_Desc_en", descEn);

                var fName = typeof(BlueprintUnitFact).GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var fDesc = typeof(BlueprintUnitFact).GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var nameLoc = Activator.CreateInstance(fName.FieldType);
                var descLoc = Activator.CreateInstance(fDesc.FieldType);
                var locFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                bool zh = IsChinese();
                nameLoc.GetType().GetField("m_Key", locFlags)?.SetValue(nameLoc, zh ? "MDGA_DragonLegacy_Name_zh" : "MDGA_DragonLegacy_Name_en");
                nameLoc.GetType().GetField("m_Text", locFlags)?.SetValue(nameLoc, zh ? nameZh : nameEn);
                descLoc.GetType().GetField("m_Key", locFlags)?.SetValue(descLoc, zh ? "MDGA_DragonLegacy_Desc_zh" : "MDGA_DragonLegacy_Desc_en");
                descLoc.GetType().GetField("m_Text", locFlags)?.SetValue(descLoc, zh ? descZh : descEn);

                fName.SetValue(bp, nameLoc);
                fDesc.SetValue(bp, descLoc);

                LocalizationInjector.EnsureInjected();
            }
            catch
            {
                // 本地化失败不影响功能
            }

            return bp;
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

        private static void AddComponent(BlueprintScriptableObject bp, BlueprintComponent comp)
        {
            try
            {
                var f = typeof(BlueprintScriptableObject).GetField("m_Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? typeof(BlueprintScriptableObject).GetField("Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var arr = (f?.GetValue(bp) as BlueprintComponent[]) ?? Array.Empty<BlueprintComponent>();
                f?.SetValue(bp, arr.Concat(new[] { comp }).ToArray());
            }
            catch { }
        }

        private static void SetFieldOrProp(object obj, string name, object value)
        {
            if (obj == null) return;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var f = obj.GetType().GetField(name, flags);
            if (f != null) { try { f.SetValue(obj, value); return; } catch { } }
            var p = obj.GetType().GetProperty(name, flags);
            if (p != null && p.CanWrite) { try { p.SetValue(obj, value); return; } catch { } }
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
    }
}
