using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.Class.LevelUp; // FeatureSelectionState / LevelUpState
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.Blueprints.Classes.Selection; // IFeatureSelection / IFeatureSelectionItem
using Kingmaker.UnitLogic; // UnitDescriptor
using System.Collections.Generic;

namespace MDGA.Patch
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class BloodlineRestrictionBypass_Install
    {
        private static bool _done;
        static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            try { BloodlineRestrictionBypass.Install(); } catch (Exception ex) { Main.Log("[BloodlineBypass] Install error: " + ex); }
        }
    }

    internal static class BloodlineRestrictionBypass
    {
        private static Harmony _harmony;
        private static BlueprintScriptableObject _crossSecondarySelection; // 交错血统的“次级血统”选择
        private static BlueprintScriptableObject _secondBloodlineSelection; // “第二血统”选择
        private static readonly string CrossSecondarySelectionGuid = "60c99d78a70e0b44f87ba01d02d909a6"; // CrossbloodedSecondaryBloodlineSelection
        private static readonly string SecondBloodlineSelectionGuid = "3cf2ab2c320b73347a7c21cf0d0995bd"; // SecondBloodline
        private static bool _itemsPatched;

        // 原生可用的龙族颜色后缀
        private static readonly string[] DraconicColors = new[] { "Black","Blue","Brass","Bronze","Copper","Gold","Green","Red","Silver","White" };

        internal static void Install()
        {
            _harmony = new Harmony("MDGA.BloodlineRestrictionBypass");
            TryResolve(CrossSecondarySelectionGuid, out _crossSecondarySelection);
            TryResolve(SecondBloodlineSelectionGuid, out _secondBloodlineSelection);
            PatchCanSelectAnything();          // 放宽“是否可选择任何项”的判定
            PatchIFeatureSelectionCanSelect();  // 细化逐项 CanSelect 逻辑
            Main.Log("[BloodlineBypass] Installed (targets: CrossSecondary + SecondBloodline; no duplicate draconic colors)");
        }

        private static void TryResolve(string guid, out BlueprintScriptableObject bp)
        {
            bp = null;
            if (Guid.TryParse(guid, out var g))
            {
                var bg = new BlueprintGuid(g);
                bp = ResourcesLibrary.TryGetBlueprint(bg) as BlueprintScriptableObject;
            }
        }

        private static void PatchCanSelectAnything()
        {
            var t = typeof(FeatureSelectionState);
            var mi = t.GetMethod("CanSelectAnything", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null)
            {
                Main.Log("[BloodlineBypass] CanSelectAnything not found");
                return;
            }
            _harmony.Patch(mi, postfix: new HarmonyMethod(typeof(BloodlineRestrictionBypass).GetMethod(nameof(CanSelectAnything_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
            Main.Log("[BloodlineBypass] Patched FeatureSelectionState.CanSelectAnything");
        }

        private static bool IsTargetSelection(IFeatureSelection selection)
        {
            if (selection == null) return false;
            return selection == (object)_crossSecondarySelection || selection == (object)_secondBloodlineSelection;
        }

        // 从名称中提取龙族颜色（基于特性命名约定）
        private static string GetDraconicColor(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var c in DraconicColors)
            {
                if (name.IndexOf("Draconic" + c, StringComparison.Ordinal) >= 0) return c;
            }
            return null;
        }

        private static bool UnitAlreadyHasColor(UnitDescriptor unit, string color)
        {
            if (unit?.Progression == null || string.IsNullOrEmpty(color)) return false;
            try
            {
                var features = unit.Progression.Features; // FeatureCollection
                if (features == null) return false;
                var fiFacts = features.GetType().GetField("m_Facts", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var raw = fiFacts?.GetValue(features) as System.Collections.IEnumerable;
                if (raw != null)
                {
                    foreach (var fact in raw)
                    {
                        try
                        {
                            var bpProp = fact?.GetType().GetProperty("Blueprint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            var bp = bpProp?.GetValue(fact) as BlueprintScriptableObject;
                            var n = bp?.name;
                            if (string.IsNullOrEmpty(n)) continue;
                            if (n.IndexOf("Draconic" + color, StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Progression", StringComparison.OrdinalIgnoreCase) >= 0)
                                return true;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool IsSpecialLocked(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // 排除“秘传/神秘/隐藏”等特殊或自定义锁定的血统（启发式关键字）
            return name.IndexOf("Esoteric", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Mystic", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Custom", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("秘", StringComparison.Ordinal) >= 0;
        }

        // 枚举 LevelUpState 中存放的所有 FeatureSelectionState，找到本次会话内的已选项目
        private static IEnumerable<FeatureSelectionState> EnumerateSelectionStates(LevelUpState state)
        {
            if (state == null) yield break;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            // 扫描字段
            foreach (var f in state.GetType().GetFields(flags))
            {
                object val = null;
                try { val = f.GetValue(state); } catch { }
                foreach (var s in ExtractStatesFromObject(val)) yield return s;
            }
            // 扫描属性
            foreach (var p in state.GetType().GetProperties(flags))
            {
                if (!p.CanRead) continue;
                object val = null;
                try { val = p.GetValue(state, null); } catch { }
                foreach (var s in ExtractStatesFromObject(val)) yield return s;
            }
        }

        private static IEnumerable<FeatureSelectionState> ExtractStatesFromObject(object obj)
        {
            if (obj == null) yield break;
            if (obj is FeatureSelectionState s) { yield return s; yield break; }
            if (obj is System.Collections.IEnumerable enumerable)
            {
                foreach (var it in enumerable)
                {
                    if (it is FeatureSelectionState ss) yield return ss;
                }
            }
        }

        private static bool HasColorInState(LevelUpState state, string color)
        {
            try
            {
                foreach (var s in EnumerateSelectionStates(state))
                {
                    try
                    {
                        var feat = s?.SelectedItem?.Feature as BlueprintFeature;
                        var n = feat?.name ?? string.Empty;
                        var c = GetDraconicColor(n);
                        if (!string.IsNullOrEmpty(c) && string.Equals(c, color, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        // 沿父/子链检查在同一链路中是否已选择过相同颜色（避免重复）
        private static bool HasColorInChain(FeatureSelectionState selectionState, string color)
        {
            if (selectionState == null || string.IsNullOrEmpty(color)) return false;
            var visited = new HashSet<object>();
            bool Check(FeatureSelectionState s)
            {
                if (s == null) return false;
                if (!visited.Add(s)) return false;
                try
                {
                    var sel = s.SelectedItem;
                    var feat = sel?.Feature as BlueprintFeature;
                    var n = feat?.name ?? string.Empty;
                    var c = GetDraconicColor(n);
                    if (!string.IsNullOrEmpty(c) && string.Equals(c, color, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
                // 双向链
                return Check(s.Parent) || Check(s.Next);
            }
            return Check(selectionState);
        }

        private static bool IsDraconicProgression(BlueprintFeature feat)
        {
            if (feat == null) return false;
            var name = feat.name ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return false;
            // 支持次级与主系/探索者/九尾等变体命名
            if (name.StartsWith("CrossbloodedSecondaryBloodlineDraconic", StringComparison.Ordinal)) return true;
            if (name.StartsWith("BloodlineDraconic", StringComparison.Ordinal)) return true;                 // main + NineTailed (Progression1)
            if (name.StartsWith("SeekerBloodlineDraconic", StringComparison.Ordinal)) return true;           // Seeker variant
            return false;
        }

        private static bool IsAllowedDraconicForSelection(BlueprintFeature feat, UnitDescriptor unit, FeatureSelectionState selectionState, LevelUpState state)
        {
            if (!IsDraconicProgression(feat)) return false;
            var name = feat.name ?? string.Empty;
            if (IsSpecialLocked(name)) return false;
            var color = GetDraconicColor(name);
            if (color == null) return false;
            // 角色已拥有该颜色的血统进阶/进度，禁止重复颜色
            if (UnitAlreadyHasColor(unit, color)) return false;
            // 在当前选择链中已有该颜色，禁止
            if (HasColorInChain(selectionState?.Parent, color) || HasColorInChain(selectionState?.Next, color)) return false;
            // 在 LevelUpState 的其它选择中已有该颜色，禁止
            if (HasColorInState(state, color)) return false;
            // 禁止完全相同的进阶条目（已有相同蓝图）
            if (unit?.Progression?.Features.HasFact(feat) == true) return false;
            return true;
        }

        private static void CanSelectAnything_Postfix(FeatureSelectionState __instance, LevelUpState state, UnitEntityData unit, bool isForce, ref bool __result)
        {
            if (!Main.Enabled) return;
            if (__result) return; // already true
            try
            {
                if (IsTargetSelection(__instance.Selection))
                {
                    var any = __instance.Selection.Items.Any(it => IsAllowedDraconicForSelection(it.Feature as BlueprintFeature, unit?.Descriptor, __instance, state));
                    if (any)
                    {
                        __result = true;
                        if (Main.Settings.VerboseLogging)
                            Main.Log("[BloodlineBypass] Allow draconic options (CanSelectAnything) - at least one new color");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging)
                    Main.Log("[BloodlineBypass] CanSelectAnything_Postfix error: " + ex.Message);
            }
        }

        private static void PatchIFeatureSelectionCanSelect()
        {
            if (_itemsPatched) return;
            _itemsPatched = true;
            Type iface = typeof(IFeatureSelection);
            int patched = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (!iface.IsAssignableFrom(t) || t.IsInterface || t.IsAbstract) continue;
                    try
                    {
                        var mi = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "CanSelect" && m.ReturnType == typeof(bool));
                        if (mi == null) continue;
                        var ps = mi.GetParameters();
                        if (ps.Length != 4) continue;
                        if (ps[0].ParameterType.FullName?.Contains("UnitDescriptor") != true) continue;
                        if (ps[1].ParameterType != typeof(LevelUpState)) continue;
                        if (ps[2].ParameterType != typeof(FeatureSelectionState)) continue;
                        if (!typeof(IFeatureSelectionItem).IsAssignableFrom(ps[3].ParameterType)) continue;
                        _harmony.Patch(mi, postfix: new HarmonyMethod(typeof(BloodlineRestrictionBypass).GetMethod(nameof(ItemCanSelect_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        patched++;
                    }
                    catch { }
                }
            }
            Main.Log($"[BloodlineBypass] Patched IFeatureSelection.CanSelect methods (restricted mode): {patched}");
        }

        private static void ItemCanSelect_Postfix(object __instance, UnitDescriptor unit, LevelUpState state, FeatureSelectionState selectionState, IFeatureSelectionItem item, ref bool __result)
        {
            if (!Main.Enabled) return;
            try
            {
                if (!IsTargetSelection(selectionState?.Selection)) return; // only target selections
                var feat = item?.Feature as BlueprintFeature;
                var name = feat?.name ?? string.Empty;
                var color = GetDraconicColor(name);

                if (__result)
                {
                    // 如果原逻辑允许，但颜色重复（链/会话）或为特殊锁定项，则阻止
                    if (IsSpecialLocked(name) || (color != null && (UnitAlreadyHasColor(unit, color) || HasColorInChain(selectionState?.Parent, color) || HasColorInChain(selectionState?.Next, color) || HasColorInState(state, color))))
                    {
                        __result = false;
                        if (Main.Settings.VerboseLogging)
                            Main.Log($"[BloodlineBypass] Block duplicate/special draconic color {feat?.name}");
                    }
                    return;
                }
                // 原逻辑拒绝：仅当通过我们精细检查且确为龙族血统时放行
                if (!IsAllowedDraconicForSelection(feat, unit, selectionState, state)) return;
                __result = true;
                if (Main.Settings.VerboseLogging)
                    Main.Log($"[BloodlineBypass] Allow draconic color {feat?.name}");
            }
            catch { }
        }
    }
}
