using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Prerequisites;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp;

namespace MDGA.Components
{
    // 仅用于定位“金龙合书”候选未出现/不可选的根因；不改变任何逻辑。
    internal static class GDMergeDiagnostics
    {
        // 针对常见“丢法术”个案的蓝图 GUID（用于快速存在性检查）
        private static readonly BlueprintGuid DivinePowerAbilityGuid = BlueprintGuid.Parse("ef16771cb05d1344989519e87f25b3c5");
        private static readonly BlueprintGuid EaglesoulAbilityGuid = BlueprintGuid.Parse("332ad68273db9704ab0e92518f2efd1c");

        // 反射取值工具
        private static T GetPrivateField<T>(object obj, string name)
        {
            if (obj == null) return default(T);
            var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) return default(T);
            try { return (T)f.GetValue(obj); } catch { return default(T); }
        }

        private static T GetPrivateStaticField<T>(Type t, string name)
        {
            if (t == null) return default(T);
            var f = t.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) return default(T);
            try { return (T)f.GetValue(null); } catch { return default(T); }
        }

        private static BlueprintGuid GetGDMergeGuid()
        {
            try
            {
                var t = Type.GetType("MDGA.AutoMerge.GoldDragonAutoMerge, MDGA", false);
                if (t != null)
                {
                    var g = GetPrivateStaticField<BlueprintGuid>(t, "GD_MergeFeatureGuid");
                    if (g != BlueprintGuid.Empty) return g;
                }
            }
            catch { }
            return BlueprintGuid.Empty;
        }

        private static BlueprintGuid[] GetDraconicGuidSet()
        {
            try
            {
                var t = Type.GetType("MDGA.AutoMerge.GoldDragonAutoMerge, MDGA", false);
                if (t != null)
                {
                    var arr = GetPrivateStaticField<BlueprintGuid[]>(t, "DraconicRequisiteGuids");
                    if (arr != null && arr.Length > 0) return arr;
                }
            }
            catch { }
            // 兜底：返回空则只做名称判断
            return Array.Empty<BlueprintGuid>();
        }

        private static bool NameLooksDraconic(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            if (n.IndexOf("Draconic", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Bloodline", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("CrossbloodedSecondaryBloodline", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Draconic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("SeekerBloodlineDraconic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool UnitHasDraconic(UnitDescriptor unit, HashSet<BlueprintGuid> guidSet, List<string> hits)
        {
            if (unit == null) return false;
            bool any = false;
            try
            {
                foreach (var fact in unit.Facts.List)
                {
                    var bp = fact?.Blueprint; if (bp == null) continue;
                    var n = bp.name ?? string.Empty;
                    if (guidSet.Contains(bp.AssetGuid)) { any = true; hits?.Add($"GUID:{n}:{bp.AssetGuid}"); }
                    else if (NameLooksDraconic(n)) { any = true; hits?.Add($"NAME:{n}:{bp.AssetGuid}"); }
                }
            }
            catch { }
            return any;
        }

        // 快照：转储单位拥有的具体 Spellbook 的详细信息，包括法表与已知法术（用于定位秘闻添加的神术是否仍在）
        private static void DumpUnitSpellbooks(UnitDescriptor unit, IEnumerable<BlueprintSpellbookReference> allow)
        {
            try
            {
                if (unit == null) return;
                var arr = allow?.ToArray() ?? Array.Empty<BlueprintSpellbookReference>();
                Main.Log($"[GDDiag] Spellbook snapshot: allow={arr.Length}");
                foreach (var r in arr)
                {
                    var bp = r?.Get(); if (bp == null) continue;
                    var sb = unit.GetSpellbook(bp);
                    var sbHas = sb != null;
                    var listGuid = bp.SpellList?.AssetGuid ?? BlueprintGuid.Empty;
                    // 反射取 IsArcane/IsDivine
                    bool isArcane = false, isDivine = false;
                    try { isArcane = (bool)(bp.GetType().GetProperty("IsArcane")?.GetValue(bp) ?? false); } catch { }
                    try { isDivine = (bool)(bp.GetType().GetProperty("IsDivine")?.GetValue(bp) ?? false); } catch { }

                    int knownCount = 0;
                    bool hasDivinePower = false;
                    bool hasEaglesoul = false;
                    if (sbHas)
                    {
                        try
                        {
                            // 聚合 0..10 环的已知法术
                            for (int lvl = 0; lvl <= 10; lvl++)
                            {
                                var knownLvl = sb.GetKnownSpells(lvl);
                                if (knownLvl == null) continue;
                                knownCount += knownLvl.Count;
                                foreach (var ad in knownLvl)
                                {
                                    var guid = ad?.Blueprint?.AssetGuid ?? BlueprintGuid.Empty;
                                    if (guid == DivinePowerAbilityGuid) hasDivinePower = true;
                                    if (guid == EaglesoulAbilityGuid) hasEaglesoul = true;
                                }
                            }
                        }
                        catch { }
                    }
                    Main.Log($"[GDDiag]   {bp.name}:{bp.AssetGuid} arcane={isArcane} divine={isDivine} list={(listGuid==BlueprintGuid.Empty?"null":listGuid.ToString())} has={(sbHas?"YES":"NO")} known={knownCount} DP={(hasDivinePower?"Y":"N")} ES={(hasEaglesoul?"Y":"N")}");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[GDDiag] DumpUnitSpellbooks ex: " + ex.Message);
            }
        }

        // 1) BlueprintsCache.Init 完成后，转储自定义合书特性的 AllowedSpellbooks、组件与前置清单
        [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
        private static class CacheInitDiag
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!Main.Enabled || Main.Settings == null || !Main.Settings.VerboseLogging) return;
                try
                {
                    var mergeGuid = GetGDMergeGuid();
                    var feat = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelectMythicSpellbook>(mergeGuid);
                    if (feat == null) { Main.Log("[GDDiag] Merge feature not found after cache init"); return; }

                    // Allowed spellbooks
                    var allowed = GetPrivateField<BlueprintSpellbookReference[]>(feat, "m_AllowedSpellbooks") ?? Array.Empty<BlueprintSpellbookReference>();
                    Main.Log($"[GDDiag] AllowedSpellbooks count={allowed.Length}");
                    foreach (var r in allowed)
                    {
                        var b = r?.Get(); if (b == null) continue;
                        var cls = b.CharacterClass;
                        Main.Log($"[GDDiag]   Allow: {b.name} guid={b.AssetGuid} class={(cls==null?"null":cls.name)} classGuid={(cls==null?"null":cls.AssetGuid.ToString())}");
                    }

                    // Components snapshot
                    var comps = GetPrivateField<BlueprintComponent[]>(feat, "m_Components") ?? Array.Empty<BlueprintComponent>();
                    Main.Log($"[GDDiag] Components count={comps.Length}");
                    foreach (var c in comps) Main.Log($"[GDDiag]   Comp: {c?.GetType().FullName}");

                    // If there is PrerequisiteFeaturesFromList – dump its feature GUIDs
                    foreach (var c in comps.OfType<PrerequisiteFeaturesFromList>())
                    {
                        var m = typeof(PrerequisiteFeaturesFromList).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        var arr = m?.GetValue(c) as BlueprintFeatureReference[];
                        int cnt = arr?.Length ?? 0;
                        Main.Log($"[GDDiag] PrerequisiteFeaturesFromList Amount={c.Amount} CheckInProgression={c.CheckInProgression} items={cnt}");
                        if (arr != null) foreach (var fr in arr)
                        {
                            var f = fr?.Get(); if (f == null) continue;
                            Main.Log($"[GDDiag]     Req: {f.name} guid={f.AssetGuid}");
                        }
                    }
                }
                catch (Exception ex) { Main.Log("[GDDiag] CacheInitDiag exception: " + ex.Message); }
            }
        }

        // 2) 在 CanSelect 前后记录：原始 __result、参数 item 的 Spellbook、单位是否具备龙血、以及最终 __result
        [HarmonyPatch(typeof(BlueprintFeatureSelectMythicSpellbook), nameof(BlueprintFeatureSelectMythicSpellbook.CanSelect))]
        private static class CanSelectDiag
        {
            [HarmonyPrefix]
            private static void Prefix(UnitDescriptor unit, object item, BlueprintFeatureSelectMythicSpellbook __instance, ref bool __result)
            {
                if (!Main.Enabled || Main.Settings == null || !Main.Settings.VerboseLogging) return;
                try
                {
                    if (__instance == null || __instance.AssetGuid != GetGDMergeGuid()) return;
                    var chosen = ExtractSpellbookFromParam(item);
                    var allow = GetPrivateField<BlueprintSpellbookReference[]>(__instance, "m_AllowedSpellbooks") ?? Array.Empty<BlueprintSpellbookReference>();
                    bool chosenAllowed = chosen != null && allow.Any(a => a?.Guid == chosen.AssetGuid);

                    var set = new HashSet<BlueprintGuid>(GetDraconicGuidSet());
                    var hits = new List<string>();
                    bool hasDraconic = UnitHasDraconic(unit, set, hits);

                    Main.Log($"[GDDiag] CanSelect PREFIX __origResult={__result} chosen={(chosen==null?"null":chosen.name)} guid={(chosen==null?"null":chosen.AssetGuid.ToString())} chosenAllowed={chosenAllowed} hasDraconic={hasDraconic} hits=[{string.Join(",", hits)}]");

                    // 当 chosen 为空（例如“独立神话书”占位条目）或用于首次探查时，打印单位是否拥有每个 AllowedSpellbook 的实例
                    try
                    {
                        var sbLines = new List<string>();
                        int hasCount = 0;
                        foreach (var r in allow)
                        {
                            var b = r?.Get(); if (b == null) continue;
                            bool has = unit?.GetSpellbook(b) != null;
                            if (has) hasCount++;
                            sbLines.Add($"{b.name}:{b.AssetGuid}:{(has ? "HAS" : "NO")}");
                        }
                        Main.Log($"[GDDiag] UnitSpellbooks snapshot: allow={allow.Length} has={hasCount}");
                        // 展开详细映射（仅在详细日志时展示）
                        Main.Log($"[GDDiag]   Map -> [\n  {string.Join(",\n  ", sbLines)}\n]");
                    }
                    catch { }

                    // 打印职业等级分布，确认单位是否确实拥有术士等基础职业
                    try
                    {
                        var classes = unit?.Progression?.Classes;
                        if (classes != null)
                        {
                            var lines = new List<string>();
                            foreach (var cd in classes)
                            {
                                var cc = cd?.CharacterClass;
                                int lvl = cd?.Level ?? 0;
                                if (cc != null) lines.Add($"{cc.name}:{cc.AssetGuid}:L{lvl}");
                            }
                            Main.Log($"[GDDiag] UnitClasses -> [ {string.Join(", ", lines)} ]");
                        }
                    }
                    catch { }

                    // 详细转储单位法术书（含已知法术与关键法术是否存在），用于定位博学士秘闻加入的跨表法术是否仍在
                    try { DumpUnitSpellbooks(unit, allow); } catch { }
                }
                catch (Exception ex) { Main.Log("[GDDiag] CanSelect PREFIX ex: " + ex.Message); }
            }

            [HarmonyPostfix]
            private static void Postfix(UnitDescriptor unit, object item, BlueprintFeatureSelectMythicSpellbook __instance, ref bool __result)
            {
                if (!Main.Enabled || Main.Settings == null || !Main.Settings.VerboseLogging) return;
                try
                {
                    if (__instance == null || __instance.AssetGuid != GetGDMergeGuid()) return;
                    var chosen = ExtractSpellbookFromParam(item);
                    Main.Log($"[GDDiag] CanSelect POSTFIX finalResult={__result} chosen={(chosen==null?"null":chosen.name)}");
                }
                catch (Exception ex) { Main.Log("[GDDiag] CanSelect POSTFIX ex: " + ex.Message); }
            }

            private static BlueprintSpellbook ExtractSpellbookFromParam(object item)
            {
                try
                {
                    var p = item?.GetType().GetProperty("Param", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
                    return p?.GetType().GetProperty("Blueprint")?.GetValue(p) as BlueprintSpellbook;
                }
                catch { }
                return null;
            }
        }
    }
}
