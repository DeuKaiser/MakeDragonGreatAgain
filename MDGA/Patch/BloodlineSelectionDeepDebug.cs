using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem; // 供 BlueprintsCache 初始化时挂载

namespace MDGA.Patch
{
    // 在蓝图缓存初始化后安装“血统选择深度调试”补丁：
    // 通过前后缀记录 FeatureSelectionState.CanSelect(feature) 的输入与结果，定位为何某些血统被阻止/允许。
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class BloodlineSelectionDeepDebug_Init
    {
        private static bool _installed;
        static void Postfix()
        {
            if (_installed) return; _installed = true;
            // 仅在开启统一的详细日志时安装深度调试补丁
            if (!Main.Enabled || Main.Settings == null || !Main.Settings.VerboseLogging) return;
            try { BloodlineSelectionDeepDebug.Install(); } catch (Exception ex) { Main.Log("[BloodlineDeepDebug] Install error: " + ex.Message); }
        }
    }

    internal static class BloodlineSelectionDeepDebug
    {
        private static Harmony _harmony;
        internal static void Install()
        {
            _harmony = new Harmony("MDGA.BloodlineSelectionDeepDebug");
            var t = AccessTools.TypeByName("Kingmaker.UnitLogic.Class.LevelUp.Selections.FeatureSelectionState");
            if (t == null) { Main.Log("[BloodlineDeepDebug] FeatureSelectionState type not found - skipping."); return; }
            var mi = AccessTools.Method(t, "CanSelect", new Type[] { typeof(BlueprintFeature) });
            if (mi == null) { Main.Log("[BloodlineDeepDebug] CanSelect method not found - skipping."); return; }
            var pre = new HarmonyMethod(typeof(BloodlineSelectionDeepDebug).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));
            var post = new HarmonyMethod(typeof(BloodlineSelectionDeepDebug).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
            _harmony.Patch(mi, pre, post);
            Main.Log("[BloodlineDeepDebug] Patched FeatureSelectionState.CanSelect");
        }

        // 反射缓存
        private static FieldInfo _fiSelection; // m_Selection / Selection
        private static PropertyInfo _piSelection;
        private static FieldInfo _fiItems; // m_Items（已选项集合）

        private static object GetSelection(object state)
        {
            if (state == null) return null;
            try
            {
                if (_fiSelection == null && _piSelection == null)
                {
                    var t = state.GetType();
                    _fiSelection = t.GetField("m_Selection", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    _piSelection = t.GetProperty("Selection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _fiItems = t.GetField("m_Items", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                object sel = _fiSelection?.GetValue(state) ?? _piSelection?.GetValue(state, null);
                return sel;
            }
            catch { return null; }
        }

        private static bool IsBloodlineSelection(object sel)
        {
            try
            {
                var bp = sel as BlueprintScriptableObject;
                if (bp == null) return false;
                string n = bp.name ?? string.Empty;
                return n.IndexOf("Bloodline", StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Dragon", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static int GetPickedCount(object state)
        {
            try
            {
                var raw = _fiItems?.GetValue(state);
                if (raw is System.Collections.ICollection col) return col.Count;
            }
            catch { }
            return -1;
        }

        private static void Prefix(object __instance, BlueprintFeature feature)
        {
            try
            {
                var sel = GetSelection(__instance);
                if (!IsBloodlineSelection(sel)) return;
                if (feature == null) return;
                Main.Log($"[BloodlineDeepDebug] CanSelect? sel={sel?.GetType().Name}:{(sel as BlueprintScriptableObject)?.name}:{(sel as SimpleBlueprint)?.AssetGuid.ToString().Substring(0,8)} feature={feature.name}:{feature.AssetGuid.ToString().Substring(0,8)} picked={GetPickedCount(__instance)} ...");
            }
            catch { }
        }

        private static void Postfix(object __instance, BlueprintFeature feature, bool __result)
        {
            try
            {
                var sel = GetSelection(__instance);
                if (!IsBloodlineSelection(sel)) return;
                if (feature == null) return;
                Main.Log($"[BloodlineDeepDebug] => {__result} sel={(sel as BlueprintScriptableObject)?.name} feature={feature.name}");
            }
            catch { }
        }
    }
}
