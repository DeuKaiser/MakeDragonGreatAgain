using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.Mechanics;
using UnityEngine;

namespace MDGA.GeneralClasses.DragonheirScion
{
    // 翻倍“DragonicDefences*”特性中的天然护甲与能量抗性数值。
    // 实现方式：
    // 1) 对 AddContextStatBonus(AC / NaturalArmor) 设置 Multiplier = 2
    // 2) 对 AddDamageResistanceEnergy 启用 UseValueMultiplier = true 且 ValueMultiplier=2
    // （不修改原 Rank Progression，保持兼容性）
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonheirDefenseBoost
    {
        private static bool _applied;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_applied) return;
            _applied = true;
            if (!Main.Enabled) return;
            try
            {
                var progGuids = new[]
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

                int featCount = 0, acChanged = 0, resistChanged = 0;
                foreach (var gid in progGuids)
                {
                    var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(gid);
                    if (prog == null) continue;
                    foreach (var le in prog.LevelEntries)
                    {
                        if (le == null) continue;
                        // DragonicDefences 在 3 级授予；若未来不同可移除此过滤
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
                            featCount++;
                            try
                            {
                                var comps = feat.ComponentsArray ?? Array.Empty<BlueprintComponent>();
                                foreach (var c in comps)
                                {
                                    if (c == null) continue;
                                    var ctName = c.GetType().Name;
                                    // 天然护甲 AC 翻倍
                                    if (ctName.Contains("AddContextStatBonus"))
                                    {
                                        var multField = c.GetType().GetField("Multiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        var statField = c.GetType().GetField("Stat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        var descField = c.GetType().GetField("Descriptor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (multField != null && statField != null && descField != null)
                                        {
                                            try
                                            {
                                                var statVal = statField.GetValue(c)?.ToString();
                                                var descVal = descField.GetValue(c)?.ToString();
                                                if (string.Equals(statVal, "AC", StringComparison.OrdinalIgnoreCase) && descVal == "NaturalArmor")
                                                {
                                                    multField.SetValue(c, 2); // 原为 1
                                                    acChanged++;
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                    // 能量抗性翻倍：启用乘数
                                    else if (ctName.Contains("AddDamageResistanceEnergy"))
                                    {
                                        var useMultField = c.GetType().GetField("UseValueMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        var valueMultField = c.GetType().GetField("ValueMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (useMultField != null && valueMultField != null)
                                        {
                                            try
                                            {
                                                useMultField.SetValue(c, true);
                                                // ValueMultiplier 是 ContextValue 结构体或类
                                                var cvType = valueMultField.FieldType;
                                                object cv = valueMultField.GetValue(c);
                                                if (cv == null) cv = Activator.CreateInstance(cvType);
                                                // 设定 ValueType=Simple, Value=2
                                                var vtField = cvType.GetField("ValueType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                                var valField = cvType.GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                                var rankField = cvType.GetField("ValueRank", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                                if (vtField != null) try { vtField.SetValue(cv, Enum.Parse(vtField.FieldType, "Simple")); } catch { }
                                                if (rankField != null) try { rankField.SetValue(cv, Enum.Parse(rankField.FieldType, "Default")); } catch { }
                                                if (valField != null) try { valField.SetValue(cv, 2); } catch { }
                                                valueMultField.SetValue(c, cv);
                                                resistChanged++;
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[MDGA] DragonheirDefenseBoost error {feat.name}: {ex.Message}");
                            }
                        }
                    }
                }

                Main.Log($"[DragonheirDefenseBoost] Applied: features={featCount}, AC doubled={acChanged}, resist doubled={resistChanged}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MDGA] DragonheirDefenseBoost global error: {e.Message}");
            }
        }
    }
}
