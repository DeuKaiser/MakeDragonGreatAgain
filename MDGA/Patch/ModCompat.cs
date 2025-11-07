using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker;
using Kingmaker.Localization;
using UnityEngine;
using Kingmaker.Blueprints.JsonSystem; // 用于 BlueprintsCache

namespace MDGA.Patch
{
    // 延迟兼容执行器，确保在其它模组的延迟注入之后执行（例如 Prestige-Plus 的 StartGameLoader 补丁）
    internal static class ModCompat
    {
        private static bool _scheduled;
        private const int MaxScanAttempts = 5;
        private static int _scanAttempts = 0;
        // 扩展关键字
        private static readonly string[] DragonKeywords = new[] {"????","??","Esoteric","Secret","Hidden","??????","??????","EsotericDragon","DragonSecret","??","??"};
        private static readonly string[] DragonFeatureFragments = new[] {"BreathWeapon","FormOfTheDragon","BloodlineDraconic","DragonDisciple","Dragonkind"};

        internal static void Schedule()
        {
            if (_scheduled) return; _scheduled = true;
            try
            {
                var go = new GameObject("MDGA_ModCompatRunner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<CompatRunner>();
                if (Main.Settings.VerboseLogging) Main.Log("[Compat] Scheduled compatibility runner.");
            }
            catch (Exception ex) { Main.Log("[Compat] Schedule error: " + ex.Message); }
        }

        private class CompatRunner : MonoBehaviour
        {
            private int _frames;
            void Update()
            {
                _frames++;
                // 每 180 帧（约 3 秒）尝试一次，直到成功或达到最大次数
                if (_frames % 180 != 0) return;
                if (_scanAttempts >= MaxScanAttempts) { Destroy(this.gameObject); return; }
                _scanAttempts++;
                try { var added = ApplyPrestigePlusBloodlineRecovery(); if (added > 0) { if (Main.Settings.VerboseLogging) Main.Log("[Compat] Success after attempt " + _scanAttempts + ", stopping runner."); Destroy(this.gameObject); return; } }
                catch (Exception ex) { if (Main.Settings.VerboseLogging) Main.Log("[Compat] Exception (attempt " + _scanAttempts + "): " + ex.Message); }
                if (_scanAttempts >= MaxScanAttempts && Main.Settings.VerboseLogging) Main.Log("[Compat] Gave up after attempts: " + _scanAttempts);
            }
        }

        // 已知选择 GUID（原版）
        private static readonly BlueprintGuid SorcSel = BlueprintGuid.Parse("24bef8d1bee12274686f6da6ccbc8914");
        private static readonly BlueprintGuid EldScionSel = BlueprintGuid.Parse("94c29f69cdc34594a6a4677441ed7375");
        private static readonly BlueprintGuid NineTailSel = BlueprintGuid.Parse("7c813fb495d74246918a690ba86f9c86");
        private static readonly BlueprintGuid SecondBloodlineSel = BlueprintGuid.Parse("3cf2ab2c320b73347a7c21cf0d0995bd");
        private static readonly BlueprintGuid BloodlineAscendanceSel = BlueprintGuid.Parse("ce85aee1726900641ab53ede61ac5c19");

        // Prestige-Plus 专用的已知 GUID（用于龙门徒的“秘龙”）
        private static readonly BlueprintGuid DD_ProgressionGuid = BlueprintGuid.Parse("69fc2bad2eb331346a6c777423e0d0f7"); // vanilla dragon disciple progression
        private static readonly BlueprintGuid PP_EsotericDragonsSelectionGuid = BlueprintGuid.Parse("2D1E7F39-EAF1-4099-ADB7-9D4144A34BEB");
        private static readonly BlueprintGuid PP_VariantBloodlineProgressionGuid = BlueprintGuid.Parse("D8F534B2-F81C-45A5-9CEE-2D4CA8B30980");

        private static bool _ppUiPrefixRemoved = false;
        private static bool _loggedDisposePatchList = false;
        private static void DumpDisposeImplementationPatches(string phase)
        {
            try
            {
                var target = typeof(Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main.ClassProgressionVM)
                    .GetMethod("DisposeImplementation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (target == null) { if (Main.Settings.VerboseLogging) Main.Log("[Compat][Dump] target DisposeImplementation not found."); return; }
                var info = HarmonyLib.Harmony.GetPatchInfo(target);
                if (info == null) { if (Main.Settings.VerboseLogging) Main.Log("[Compat][Dump] No Harmony patch info for DisposeImplementation (phase=" + phase + ")"); return; }
                if (_loggedDisposePatchList && !Main.Settings.VerboseLogging) return; // only once unless verbose
                _loggedDisposePatchList = true;
                void logList(string kind, IEnumerable<HarmonyLib.Patch> patches)
                {
                    if (patches == null) return;
                    foreach (var p in patches.OrderBy(p => p.priority))
                    {
                        try
                        {
                            var m = p.PatchMethod;
                            string mname = m == null ? "<null>" : (m.DeclaringType?.FullName + "." + m.Name);
                            if (Main.Settings.VerboseLogging) Main.Log($"[Compat][Dump][{phase}] {kind} owner={p.owner} prio={p.priority} method={mname}");
                        }
                        catch (Exception exSub)
                        {
                            if (Main.Settings.VerboseLogging) Main.Log("[Compat][Dump] sub-error: " + exSub.Message);
                        }
                    }
                }
                logList("Prefix", info.Prefixes);
                logList("Postfix", info.Postfixes);
                logList("Transpiler", info.Transpilers);
                logList("Finalizer", info.Finalizers);
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[Compat][Dump] error: " + ex.Message);
            }
        }

        private static void RemovePrestigePlusFixNoToybox2Prefix()
        {
            if (!_ppUiPrefixRemoved) DumpDisposeImplementationPatches("before-unpatch");
            if (_ppUiPrefixRemoved) return;
            try
            {
                var target = typeof(Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main.ClassProgressionVM)
                    .GetMethod("DisposeImplementation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (target == null) { if (Main.Settings.VerboseLogging) Main.Log("[Compat] UI unpatch: target method not found."); return; }
                var patchInfo = HarmonyLib.Harmony.GetPatchInfo(target);
                if (patchInfo == null) { if (Main.Settings.VerboseLogging) Main.Log("[Compat] UI unpatch: no patch info."); return; }
                int removed = 0;
                foreach (var pre in patchInfo.Prefixes)
                {
                    try
                    {
                        var m = pre.PatchMethod;
                        if (m == null) continue;
                        var owner = pre.owner ?? "";
                        if (owner.IndexOf("Prestige", System.StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("FixNoToybox2", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            new HarmonyLib.Harmony("MDGA.UIUnpatch").Unpatch(target, m);
                            removed++;
                        }
                    }
                    catch (System.Exception exSub)
                    {
                        if (Main.Settings.VerboseLogging) Main.Log("[Compat] UI unpatch sub-error: " + exSub.Message);
                    }
                }
                if (removed > 0)
                {
                    _ppUiPrefixRemoved = true;
                    if (Main.Settings.VerboseLogging) Main.Log("[Compat] Removed PrestigePlus FixNoToybox2 UI prefix count=" + removed + ".");
                }
                else if (Main.Settings.VerboseLogging) Main.Log("[Compat] UI unpatch: no matching prefixes to remove.");
                DumpDisposeImplementationPatches("after-unpatch");
            }
            catch (System.Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[Compat] UI unpatch error: " + ex.Message);
            }
        }

        // return number of candidates actually added
        private static int ApplyPrestigePlusBloodlineRecovery()
        {
            if (!Main.Enabled) return 0;
            bool prestigeLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => { var n = a.GetName().Name ?? string.Empty; return n.IndexOf("Prestige", StringComparison.OrdinalIgnoreCase) >= 0; });
            if (!prestigeLoaded) { if (Main.Settings.VerboseLogging) Main.Log("[Compat] Prestige-Plus not detected – skip bloodline recovery."); return 0; }
            RemovePrestigePlusFixNoToybox2Prefix();
            EnsureEsotericDragonsSelection();
            // If setting forbids esoteric in main bloodline selections, proactively remove
            if (!Main.Settings.AllowEsotericInMainBloodlineSelections)
            {
                TryRemoveFromSelection(SorcSel, PP_EsotericDragonsSelectionGuid);
                TryRemoveFromSelection(EldScionSel, PP_EsotericDragonsSelectionGuid);
                TryRemoveFromSelection(NineTailSel, PP_EsotericDragonsSelectionGuid);
                TryRemoveFromSelection(SecondBloodlineSel, PP_EsotericDragonsSelectionGuid);
                TryRemoveFromSelection(BloodlineAscendanceSel, PP_EsotericDragonsSelectionGuid);
            }

            if (Main.Settings.VerboseLogging) Main.Log("[Compat] Scan attempt " + _scanAttempts + ": scanning for custom dragon bloodlines...");

            var progressions = EnumerateAll<BlueprintProgression>();
            var candidates = new List<BlueprintProgression>();
            int scanned = 0;
            foreach (var bp in progressions)
            {
                if (bp == null) continue; scanned++;
                try
                {
                    if (!LikelyDragonBloodline(bp)) continue;
                    // blacklist esoteric-like
                    var nm = bp.name ?? string.Empty;
                    if (nm.IndexOf("Esoteric", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("秘", StringComparison.Ordinal) >= 0)
                        continue;
                    candidates.Add(bp);
                }
                catch { }
            }
            if (Main.Settings.VerboseLogging) Main.Log($"[Compat] Heuristic scan over {scanned} progressions => {candidates.Count} raw dragon candidates.");
            if (candidates.Count == 0) return 0;
            var distinct = candidates.Distinct().ToList();
            if (Main.Settings.VerboseLogging) Main.Log("[Compat] Candidates: " + string.Join(", ", distinct.Select(c => c.name + ":" + c.AssetGuid.ToString().Substring(0,8))));
            int addedTotal = InjectIntoSelections(distinct);
            if (Main.Settings.VerboseLogging) Main.Log("[Compat] Added (or already present) count=" + addedTotal);
            return addedTotal;
        }

        private static bool LikelyDragonBloodline(BlueprintProgression bp)
        {
            string internalName = bp.name ?? string.Empty;
            string disp = GetDisplayName(bp) ?? string.Empty;
            bool nameMatch = DragonKeywords.Any(k => internalName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 || disp.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
            // 基线条件：必须至少包含 1 与 3 级的等级条目，且最大不超过 20 级
            if (!(bp.LevelEntries?.Any(le => le.Level == 1) ?? false)) return false;
            if (!(bp.LevelEntries?.Any(le => le.Level == 3) ?? false)) return false;
            // 启发式：任一级的特性名包含龙相关片段
            bool featureFragment = false;
            if (bp.LevelEntries != null)
            {
                foreach (var le in bp.LevelEntries)
                {
                    foreach (var f in le.Features)
                    {
                        var fname = f?.name ?? string.Empty;
                        if (DragonFeatureFragments.Any(fr => fname.IndexOf(fr, StringComparison.OrdinalIgnoreCase) >= 0)) { featureFragment = true; break; }
                    }
                    if (featureFragment) break;
                }
            }
            if (nameMatch || featureFragment)
            {
                return true;
            }
            return false;
        }

        private static int InjectIntoSelections(List<BlueprintProgression> candidates)
        {
            var selections = new BlueprintFeatureSelection[] {
                ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(SorcSel),
                ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(EldScionSel),
                ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(NineTailSel),
                ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(SecondBloodlineSel),
                ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(BloodlineAscendanceSel)
            }.Where(s => s != null).ToList();
            int touched = 0;
            foreach (var sel in selections)
            {
                try
                {
                    var allField = typeof(BlueprintFeatureSelection).GetField("m_AllFeatures", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var featsField = typeof(BlueprintFeatureSelection).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var allArr = (BlueprintFeatureReference[])allField?.GetValue(sel) ?? Array.Empty<BlueprintFeatureReference>();
                    var featsArr = (BlueprintFeatureReference[])featsField?.GetValue(sel) ?? Array.Empty<BlueprintFeatureReference>();
                    bool changed = false;
                    foreach (var prog in candidates)
                    {
                        // Respect setting: esoteric not allowed in main selections
                        if (!Main.Settings.AllowEsotericInMainBloodlineSelections)
                        {
                            var nm = prog.name ?? string.Empty;
                            if (nm.IndexOf("Esoteric", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("秘", StringComparison.Ordinal) >= 0)
                                continue;
                        }
                        if (!allArr.Any(r => r?.Guid == prog.AssetGuid)) { allArr = allArr.Append(prog.ToReference<BlueprintFeatureReference>()).ToArray(); changed = true; }
                        if (!featsArr.Any(r => r?.Guid == prog.AssetGuid)) { featsArr = featsArr.Append(prog.ToReference<BlueprintFeatureReference>()).ToArray(); changed = true; }
                    }
                    if (changed)
                    {
                        allField?.SetValue(sel, allArr);
                        featsField?.SetValue(sel, featsArr);
                        touched++;
                        if (Main.Settings.VerboseLogging) Main.Log("[Compat] Updated selection " + sel.name + " with candidates.");
                    }
                }
                catch (Exception ex) { if (Main.Settings.VerboseLogging) Main.Log("[Compat] Selection update error: " + ex.Message); }
            }
            return touched;
        }

        private static string GetDisplayName(BlueprintProgression bp)
        {
            try
            {
                var prop = bp.GetType().GetProperty("DisplayName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.PropertyType == typeof(string)) return prop.GetValue(bp, null) as string;
                var fi = bp.GetType().GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var ls = fi?.GetValue(bp);
                if (ls != null)
                {
                    var textProp = ls.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (textProp != null && textProp.CanRead) return textProp.GetValue(ls, null) as string;
                }
            }
            catch { }
            return null;
        }

        private static void EnsureEsotericDragonsSelection()
        {
            try
            {
                var ddProg = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(DD_ProgressionGuid);
                if (ddProg == null) { Main.Log("[Compat] DD progression not found (cannot inject EsotericDragons)." ); return; }
                var esotericSel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(PP_EsotericDragonsSelectionGuid);
                if (esotericSel == null)
                {
                    // maybe VariantBloodline exists; log for diagnostics
                    var variant = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(PP_VariantBloodlineProgressionGuid);
                    if (variant != null && Main.Settings.VerboseLogging) Main.Log("[Compat] Found VariantBloodline progression but missing selection EsotericDragons – waiting.");
                    else if (Main.Settings.VerboseLogging) Main.Log("[Compat] Prestige-Plus EsotericDragons blueprints not yet loaded.");
                    return;
                }
                // Inject into DD level 1 (always), but only inject into main selections if setting allows
                var level1 = ddProg.LevelEntries.FirstOrDefault(le => le.Level == 1);
                if (level1 == null) { if (Main.Settings.VerboseLogging) Main.Log("[Compat] DD progression missing level 1 entry?!"); return; }
                var fi = typeof(LevelEntry).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var raw = fi?.GetValue(level1);
                List<BlueprintFeatureBaseReference> list = raw switch
                {
                    BlueprintFeatureBaseReference[] arr => arr.ToList(),
                    List<BlueprintFeatureBaseReference> l => l,
                    _ => new List<BlueprintFeatureBaseReference>()
                };
                bool present = list.Any(r => r?.Guid == PP_EsotericDragonsSelectionGuid);
                if (!present)
                {
                    list.Add(esotericSel.ToReference<BlueprintFeatureBaseReference>());
                    if (fi != null)
                    {
                        if (fi.FieldType.IsArray) fi.SetValue(level1, list.ToArray()); else fi.SetValue(level1, list);
                        Main.Log("[Compat] Injected EsotericDragons selection into DragonDisciple L1.");
                    }
                }
                else
                {
                    if (Main.Settings.VerboseLogging) Main.Log("[Compat] EsotericDragons already present at L1 – no action.");
                }
                if (Main.Settings.AllowEsotericInMainBloodlineSelections)
                {
                    TryAddToSelection(SorcSel, esotericSel);
                    TryAddToSelection(EldScionSel, esotericSel);
                    TryAddToSelection(NineTailSel, esotericSel);
                    TryAddToSelection(SecondBloodlineSel, esotericSel);
                    TryAddToSelection(BloodlineAscendanceSel, esotericSel);
                }
            }
            catch (Exception ex)
            {
                Main.Log("[Compat] EnsureEsotericDragonsSelection error: " + ex.Message);
            }
        }

        private static void TryAddToSelection(BlueprintGuid selectionGuid, BlueprintFeatureSelection esotericSel)
        {
            try
            {
                var sel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(selectionGuid);
                if (sel == null) return;
                var allField = typeof(BlueprintFeatureSelection).GetField("m_AllFeatures", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var featsField = typeof(BlueprintFeatureSelection).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var allArr = (BlueprintFeatureReference[])allField?.GetValue(sel) ?? Array.Empty<BlueprintFeatureReference>();
                var featsArr = (BlueprintFeatureReference[])featsField?.GetValue(sel) ?? Array.Empty<BlueprintFeatureReference>();
                bool changed = false;
                if (!allArr.Any(r => r?.Guid == esotericSel.AssetGuid)) { allArr = allArr.Append(esotericSel.ToReference<BlueprintFeatureReference>()).ToArray(); changed = true; }
                if (!featsArr.Any(r => r?.Guid == esotericSel.AssetGuid)) { featsArr = featsArr.Append(esotericSel.ToReference<BlueprintFeatureReference>()).ToArray(); changed = true; }
                if (changed)
                {
                    allField?.SetValue(sel, allArr);
                    featsField?.SetValue(sel, featsArr);
                    if (Main.Settings.VerboseLogging) Main.Log("[Compat] Injected EsotericDragons into selection " + sel.name + ".");
                }
                else
                {
                    if (Main.Settings.VerboseLogging) Main.Log("[Compat] EsotericDragons already in selection " + sel.name + ".");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[Compat] TryAddToSelection error: " + ex.Message);
            }
        }

        private static void TryRemoveFromSelection(BlueprintGuid selectionGuid, BlueprintGuid featureGuid)
        {
            try
            {
                var sel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(selectionGuid);
                if (sel == null) return;
                var allField = typeof(BlueprintFeatureSelection).GetField("m_AllFeatures", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var featsField = typeof(BlueprintFeatureSelection).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var allArr = (BlueprintFeatureReference[])allField?.GetValue(sel) ?? Array.Empty<BlueprintFeatureReference>();
                var featsArr = (BlueprintFeatureReference[])featsField?.GetValue(sel) ?? Array.Empty<BlueprintFeatureReference>();
                var newAll = allArr.Where(r => r != null && r.Guid != featureGuid).ToArray();
                var newFeats = featsArr.Where(r => r != null && r.Guid != featureGuid).ToArray();
                if (newAll.Length != allArr.Length || newFeats.Length != featsArr.Length)
                {
                    allField?.SetValue(sel, newAll);
                    featsField?.SetValue(sel, newFeats);
                    Main.Log($"[Compat] Removed feature {featureGuid} from selection {sel.name}.");
                }
            }
            catch (Exception ex) { Main.Log("[Compat] TryRemoveFromSelection error: " + ex.Message); }
        }

        // 通过 BlueprintsCache 的字典通用枚举某一类型的全部蓝图
        private static IEnumerable<T> EnumerateAll<T>() where T : SimpleBlueprint
        {
            var result = new List<T>();
            try
            {
                var cache = ResourcesLibrary.BlueprintsCache;
                if (cache == null) return result;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                foreach (var f in cache.GetType().GetFields(flags))
                {
                    var ft = f.FieldType;
                    if (!ft.IsGenericType || ft.GetGenericTypeDefinition() != typeof(Dictionary<,>)) continue;
                    var args = ft.GetGenericArguments();
                    if (args.Length != 2 || args[0] != typeof(BlueprintGuid) || !typeof(SimpleBlueprint).IsAssignableFrom(args[1])) continue;
                    var dict = f.GetValue(cache) as System.Collections.IDictionary; if (dict == null) continue;
                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        if (entry.Value is T t) result.Add(t);
                    }
                }
            }
            catch { }
            return result;
        }
    }

    // 在 BlueprintsCache.Init 之后挂钩以安排运行器
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class ModCompatInitPatch
    {
        [HarmonyPostfix]
        static void Postfix() { try { ModCompat.Schedule(); } catch (Exception ex) { Main.Log("[Compat] schedule patch error: " + ex.Message); } }
    }
}
