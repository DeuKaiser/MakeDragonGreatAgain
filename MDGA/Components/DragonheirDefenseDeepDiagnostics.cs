using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using UnityEngine;

namespace MDGA.Components
{
    // 深度诊断：枚举 DragonicDefences* 特性中的 AddContextStatBonus 与 ContextRankConfig 的所有字段/属性，
    // 以确定可安全修改的位置实现自然护甲与能量抗性翻倍。
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    public static class DragonheirDefenseDeepDiagnostics
    {
        private static bool s_Initialized;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            try
            {
                var guids = new[]
                {
                    BlueprintGuid.Parse("5172cdce55b2455f878ff8c74c964a1e"), // Gold
                    BlueprintGuid.Parse("db69413e69184b099f7825092c5dbc4f"), // Red
                    BlueprintGuid.Parse("267dbd4789fa4b75a44294c7d1625bba"), // Silver
                    BlueprintGuid.Parse("b6a90286e6894c7b8fbd52a29dda9f48"), // White
                    BlueprintGuid.Parse("1bdad91f7210419b9e5b4801d084e14b"), // Green
                    BlueprintGuid.Parse("6127949882384a5cb75074e4d77ceae3"), // Black
                    BlueprintGuid.Parse("3da0972ad5574bfd8a7de9bc5460e7e9"), // Blue
                    BlueprintGuid.Parse("47f3f1a3349349fe9a7f68f8d2b6da5d"), // Brass
                    BlueprintGuid.Parse("df2807f0649c4237ab7c4d62a2acaaee"), // Bronze
                    BlueprintGuid.Parse("416ee6e6b4834bb8bd5afe8b08a69865"), // Copper
                };

                foreach (var gid in guids)
                {
                    var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(gid);
                    if (prog == null) continue;
                    foreach (var le in prog.LevelEntries)
                    {
                        if (le == null) continue;
                        // 只需一次（L3 即可获取组件形态），但若未来结构不同可扩展：
                        if (le.Level != 3) continue;
                        var fiFeatures = typeof(LevelEntry).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic);
                        var featuresObj = fiFeatures?.GetValue(le);
                        if (featuresObj == null) continue;
                        foreach (var fref in (System.Collections.IEnumerable)featuresObj)
                        {
                            BlueprintFeature feat = null;
                            var miGet = fref?.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (miGet != null)
                                feat = miGet.Invoke(fref, null) as BlueprintFeature;
                            if (feat == null) continue;
                            if (!feat.name.StartsWith("DragonicDefences", StringComparison.OrdinalIgnoreCase)) continue;
                            try
                            {
                                var comps = feat.ComponentsArray ?? Array.Empty<Kingmaker.Blueprints.BlueprintComponent>();
                                foreach (var c in comps)
                                {
                                    if (c == null) continue;
                                    var ct = c.GetType();
                                    var cname = ct.Name;
                                    if (!cname.Contains("AddContextStatBonus") && !cname.Contains("ContextRankConfig") && !cname.Contains("AddDamageResistanceEnergy")) continue;
                                    Debug.Log($"[MDGA] DeepDiag {feat.name} component {cname}");
                                    // 列出属性
                                    foreach (var pi in ct.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                    {
                                        if (!pi.CanRead) continue;
                                        object val = null;
                                        try { val = pi.GetValue(c); } catch { }
                                        Debug.Log($"[MDGA] DeepDiag {cname}.prop {pi.Name} = {FormatVal(val)}");
                                    }
                                    // 列出字段
                                    foreach (var fi in ct.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                    {
                                        object val = null;
                                        try { val = fi.GetValue(c); } catch { }
                                        Debug.Log($"[MDGA] DeepDiag {cname}.field {fi.Name} = {FormatVal(val)}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[MDGA] DeepDiag error {feat.name}: {ex.Message}");
                            }
                        }
                    }
                }
                Debug.Log("[MDGA] DragonheirDefenseDeepDiagnostics: completed.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MDGA] DragonheirDefenseDeepDiagnostics Error: {e}");
            }
        }

        private static string FormatVal(object val)
        {
            if (val == null) return "<null>";
            if (val is string s) return s;
            if (val is Enum en) return en.ToString();
            if (val is int || val is bool || val is float || val is double) return val.ToString();
            var type = val.GetType();
            // 尝试简化 ContextValue 输出
            if (type.Name.Contains("ContextValue"))
            {
                var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(p => p.CanRead && (p.PropertyType == typeof(int) || p.PropertyType.IsEnum));
                var parts = props.Select(p => {
                    object v = null; try { v = p.GetValue(val); } catch { }
                    return p.Name + ":" + (v?.ToString() ?? "?");
                });
                return "ContextValue{" + string.Join(",", parts) + "}";
            }
            return type.Name;
        }
    }
}
