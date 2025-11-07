using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.Root;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Localization;

namespace MDGA.Patch
{
    // 在金龙道途 1（全局MR=8）额外给予一次「神话能力」选择
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonGodFavor
    {
        private static bool _done;
        private const string GoldenDragonProgressionGuid = "a6fbca43902c6194c947546e89af64bd"; // 金龙进阶
        private static readonly BlueprintGuid FavorFeatureGuid = BlueprintGuid.Parse("7f4a3d7c6ec54b6f9f3b4a1b2a6f11c1"); // 新建：龙神亲睐
        private static readonly BlueprintGuid MythicAbilitySelectionGuid = BlueprintGuid.Parse("ba0e5a900b775be4a99702f1ed08914d"); // 原版 神话能力 选择
        private static readonly BlueprintGuid FavorSelectionGuid = BlueprintGuid.Parse("f0d2a5a8c7d14e1e8c7f9a4a2b0d9e11"); // 新建：龙神亲睐（选择器）

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            try
            {
                var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(GoldenDragonProgressionGuid);
                if (prog == null) { Main.Log("[DragonGodFavor] Golden Dragon progression not found."); return; }

                // 直接用官方 GUID 取“神话能力选择”，找不到再启发式
                var mythicAbilitySel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(MythicAbilitySelectionGuid)
                                       ?? FindMythicAbilitySelection();
                if (mythicAbilitySel == null)
                {
                    Main.Log("[DragonGodFavor] Mythic Ability Selection not found. Abort.");
                    return;
                }

                // 准备“龙神亲睐”——作为一个选择器，展示我们自定义的名字/描述，但内部提供与原版神话能力选择相同的候选
                var favorSel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(FavorSelectionGuid);
                if (favorSel == null)
                {
                    favorSel = CreateFavorSelectionFrom(mythicAbilitySel);
                    Register(favorSel);
                    LocalizeSelection(favorSel);
                }

                // 注入到金龙 L1、L2、L3（每一级都给予一次）
                InjectSelectionIntoLevel(prog, favorSel, 1);
                InjectSelectionIntoLevel(prog, favorSel, 2);
                InjectSelectionIntoLevel(prog, favorSel, 3);
            }
            catch (Exception ex)
            {
                Main.Log("[DragonGodFavor] Exception: " + ex);
            }
        }

    private static void InjectSelectionIntoLevel(BlueprintProgression prog, BlueprintFeatureSelection favorSelection, int level)
        {
            try
            {
        var le = prog.LevelEntries?.FirstOrDefault(e => e.Level == level); if (le == null) return;
                var fi = typeof(LevelEntry).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var raw = fi?.GetValue(le);
                var list = raw is BlueprintFeatureBaseReference[] arr ? arr.ToList() : raw as System.Collections.Generic.List<BlueprintFeatureBaseReference> ?? new System.Collections.Generic.List<BlueprintFeatureBaseReference>();

                // 清理旧版本可能注入的占位特性；保留原版自带的神话能力选择（不要覆盖/删除它）
                list.RemoveAll(r => r?.Get()?.AssetGuid == FavorFeatureGuid);

                // 若尚未加入我们的选择器，则添加
                bool exists = list.Any(r => r?.Get()?.AssetGuid == favorSelection.AssetGuid);
                if (!exists)
                {
                    list.Add(favorSelection.ToReference<BlueprintFeatureBaseReference>());
                    if (fi.FieldType.IsArray) fi.SetValue(le, list.ToArray()); else fi.SetValue(le, list);
                    Main.Log($"[DragonGodFavor] Injected custom selection 'Dragon God's Favor' at Golden Dragon L{level}.");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonGodFavor] InjectSelectionIntoLevel error: " + ex.Message);
            }
        }

        private static BlueprintFeature CreateFavorFeature()
        {
            BlueprintFeature bp;
            try { bp = (BlueprintFeature)Activator.CreateInstance(typeof(BlueprintFeature)); }
            catch { bp = (BlueprintFeature)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintFeature)); }
            bp.name = "MDGA_DragonGodFavor";
            SetGuid(bp, FavorFeatureGuid);
            bp.IsClassFeature = true;
            bp.Ranks = 1;

            try
            {
                const string nameZh = "龙神亲睐";
                const string descZh = "随着进入金龙道途，灵魂中高贵的龙族精神越发闪耀，你额外获得一次神话能力的选择。";
                const string nameEn = "Dragon God's Favor";
                const string descEn = "As you tread the Golden Dragon path, the noble draconic spirit within your soul shines ever brighter, granting you one additional Mythic Ability selection.";

                LocalizationInjector.RegisterDynamicKey("MDGA_DragonGodFavor_Name_zh", nameZh);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonGodFavor_Desc_zh", descZh);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonGodFavor_Name_en", nameEn);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonGodFavor_Desc_en", descEn);

                var fName = typeof(BlueprintUnitFact).GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var fDesc = typeof(BlueprintUnitFact).GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var nameLoc = Activator.CreateInstance(fName.FieldType);
                var descLoc = Activator.CreateInstance(fDesc.FieldType);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                bool zh = IsChinese();
                nameLoc.GetType().GetField("m_Key", flags)?.SetValue(nameLoc, zh ? "MDGA_DragonGodFavor_Name_zh" : "MDGA_DragonGodFavor_Name_en");
                nameLoc.GetType().GetField("m_Text", flags)?.SetValue(nameLoc, zh ? nameZh : nameEn);
                descLoc.GetType().GetField("m_Key", flags)?.SetValue(descLoc, zh ? "MDGA_DragonGodFavor_Desc_zh" : "MDGA_DragonGodFavor_Desc_en");
                descLoc.GetType().GetField("m_Text", flags)?.SetValue(descLoc, zh ? descZh : descEn);
                fName.SetValue(bp, nameLoc);
                fDesc.SetValue(bp, descLoc);
                LocalizationInjector.EnsureInjected();
            }
            catch { }

            return bp;
        }

        private static BlueprintFeatureSelection CreateFavorSelectionFrom(BlueprintFeatureSelection src)
        {
            BlueprintFeatureSelection sel;
            try { sel = (BlueprintFeatureSelection)Activator.CreateInstance(typeof(BlueprintFeatureSelection)); }
            catch { sel = (BlueprintFeatureSelection)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintFeatureSelection)); }
            sel.name = "MDGA_DragonGodFavorSelection";
            SetGuid(sel, FavorSelectionGuid);
            sel.IsClassFeature = true;
            sel.Ranks = 1;
            try
            {
                // 复制候选项与图标
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var fIcon = typeof(BlueprintUnitFact).GetField("m_Icon", flags);
                fIcon?.SetValue(sel, fIcon?.GetValue(src));
                var f1 = typeof(BlueprintFeatureSelection).GetField("m_Features", flags);
                var f2 = typeof(BlueprintFeatureSelection).GetField("m_AllFeatures", flags);
                f1?.SetValue(sel, f1?.GetValue(src));
                f2?.SetValue(sel, f2?.GetValue(src));
            }
            catch { }
            return sel;
        }

        private static void Register(SimpleBlueprint bp)
        {
            try { ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp); }
            catch (Exception ex) { Main.Log("[DragonGodFavor] Register error: " + ex.Message); }
        }

        private static void LocalizeSelection(BlueprintFeatureSelection sel)
        {
            try
            {
                const string nameZh = "龙神亲睐";
                const string descZh = "随着进入金龙道途，灵魂中高贵的龙族精神越发闪耀，额外获得一次神话能力的选择。";
                const string nameEn = "Dragon God's Favor";
                const string descEn = "As you tread the Golden Dragon path, the noble draconic spirit within your soul shines ever brighter, granting you one additional Mythic Ability selection.";

                LocalizationInjector.RegisterDynamicKey("MDGA_DragonGodFavorSel_Name_zh", nameZh);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonGodFavorSel_Desc_zh", descZh);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonGodFavorSel_Name_en", nameEn);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonGodFavorSel_Desc_en", descEn);

                var fName = typeof(BlueprintUnitFact).GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var fDesc = typeof(BlueprintUnitFact).GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var nameLoc = Activator.CreateInstance(fName.FieldType);
                var descLoc = Activator.CreateInstance(fDesc.FieldType);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                bool zh = IsChinese();
                nameLoc.GetType().GetField("m_Key", flags)?.SetValue(nameLoc, zh ? "MDGA_DragonGodFavorSel_Name_zh" : "MDGA_DragonGodFavorSel_Name_en");
                nameLoc.GetType().GetField("m_Text", flags)?.SetValue(nameLoc, zh ? nameZh : nameEn);
                descLoc.GetType().GetField("m_Key", flags)?.SetValue(descLoc, zh ? "MDGA_DragonGodFavorSel_Desc_zh" : "MDGA_DragonGodFavorSel_Desc_en");
                descLoc.GetType().GetField("m_Text", flags)?.SetValue(descLoc, zh ? descZh : descEn);
                fName.SetValue(sel, nameLoc);
                fDesc.SetValue(sel, descLoc);
                LocalizationInjector.EnsureInjected();
            }
            catch { }
        }

        private static BlueprintFeatureSelection FindMythicAbilitySelection()
        {
            try
            {
                // 1) 尝试通过名称匹配
                var sel = FindSelectionByPredicate(s =>
                    (s.name != null && s.name.IndexOf("MythicAbilitySelection", StringComparison.OrdinalIgnoreCase) >= 0)
                );
                if (sel != null) return sel;

                // 2) 尝试通过显示名包含
                sel = FindSelectionByPredicate(s =>
                {
                    string title = GetDisplayNameText(s);
                    if (string.IsNullOrEmpty(title)) return false;
                    var t = title.ToLowerInvariant();
                    return t.Contains("mythic ability") || t.Contains("神话能力");
                });
                if (sel != null) return sel;

                // 3) 通过候选特征分组启发：选择器中包含大量分组为 MythicAbility 的要素
                sel = FindSelectionByPredicate(s =>
                {
                    try
                    {
                        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        var fAll = typeof(BlueprintFeatureSelection).GetField("m_AllFeatures", flags) ?? typeof(BlueprintFeatureSelection).GetField("AllFeatures", flags);
                        var arr = fAll?.GetValue(s) as BlueprintFeatureReference[];
                        if (arr == null || arr.Length == 0) return false;
                        int count = 0; int marked = 0;
                        foreach (var r in arr)
                        {
                            var feat = r?.Get();
                            if (feat == null) continue; count++;
                            var grpF = feat.GetType().GetField("Groups", flags) ?? feat.GetType().GetField("m_Groups", flags);
                            var groups = grpF?.GetValue(feat) as Array;
                            if (groups == null) continue;
                            foreach (var g in groups)
                            {
                                if (g != null && string.Equals(g.ToString(), "MythicAbility", StringComparison.OrdinalIgnoreCase)) { marked++; break; }
                            }
                        }
                        return count > 5 && marked >= count / 2; // 大部分要素属于神话能力
                    }
                    catch { return false; }
                });
                return sel;
            }
            catch { return null; }
        }

        private static BlueprintFeatureSelection FindSelectionByPredicate(Func<BlueprintFeatureSelection, bool> pred)
        {
            try
            {
                var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var fname in new[] { "m_LoadedBlueprints", "s_LoadedBlueprints", "m_Blueprints", "m_Cache", "m_LoadedBlueprintsByAssetId" })
                {
                    var f = typeof(BlueprintsCache).GetField(fname, flags);
                    if (f == null) continue;
                    var obj = f.GetValue(null);
                    if (obj is System.Collections.IDictionary dict)
                    {
                        foreach (System.Collections.DictionaryEntry kv in dict)
                        {
                            if (kv.Value is BlueprintFeatureSelection sel && pred(sel)) return sel;
                        }
                    }
                    else if (obj is System.Collections.IEnumerable en)
                    {
                        foreach (var v in en)
                        {
                            if (v is BlueprintFeatureSelection sel && pred(sel)) return sel;
                            if (v is System.Collections.Generic.KeyValuePair<BlueprintGuid, SimpleBlueprint> kv2 && kv2.Value is BlueprintFeatureSelection sel2 && pred(sel2)) return sel2;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string GetDisplayNameText(BlueprintUnitFact fact)
        {
            try
            {
                var fName = typeof(BlueprintUnitFact).GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var loc = fName?.GetValue(fact);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                return loc?.GetType().GetField("m_Text", flags)?.GetValue(loc)?.ToString();
            }
            catch { return null; }
        }

        private static void SetGuid(SimpleBlueprint bp, BlueprintGuid guid)
        {
            try
            {
                var f = bp.GetType().GetField("AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                      ?? bp.GetType().GetField("m_AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                      ?? bp.GetType().GetField("m_Guid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                f?.SetValue(bp, guid);
            }
            catch { }
        }

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
