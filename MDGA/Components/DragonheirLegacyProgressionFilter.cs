using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.UnitLogic;                               // ← 新增
using Kingmaker.UnitLogic.Class.LevelUp;                 // ← 新增 ProgressionData
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main;

namespace MDGA.Components
{
    /// <summary>
    /// 过滤职业进度表中的额外战士奖励专长图标：
    /// 对战士/龙之贵胄，只保留每级第一条 FighterFeatSelection，其余同级重复条目不再显示。
    /// 不影响实际获得的 selection，只是 UI 去重。
    /// </summary>
    //// 暂时停用职业进度表 UI 过滤，留待以后需要时再启用。
    ////[HarmonyPatch(typeof(ProgressionVM), MethodType.Constructor,
    ////    new Type[] { typeof(ProgressionData), typeof(Kingmaker.UnitLogic.UnitDescriptor), typeof(int?), typeof(bool) })]
    internal static class DragonheirLegacyProgressionFilter
    {
        // MDGA 里已使用的常量 GUID，保持一致以免写错。
        private static readonly BlueprintGuid FighterClassGuid = BlueprintGuid.Parse("48ac8db94d5de7645906c7d0ad3bcfbd");
        private static readonly BlueprintGuid FighterFeatSelectionGuid = BlueprintGuid.Parse("41c8486641f7d6d4283ca9dae4147a9f");
        private static readonly BlueprintGuid DragonheirScionArchetypeGuid = BlueprintGuid.Parse("8dff97413c63c1147be8a5ca229abefc");

        [HarmonyPostfix]
        private static void Postfix(ProgressionVM __instance, ProgressionData progressionData)
        {
            try
            {
                // 只针对正常职业 progression（排除通过 FeatureEntry 构造出来的“单一 progression”）。
                if (progressionData == null)
                    return;

                // progressionData.Blueprint 是这条进度线对应的 BlueprintProgression。
                var progressionBlueprint = progressionData.Blueprint;
                if (progressionBlueprint == null)
                    return;

                // archetype 需要包含龙之贵胄；ProgressionData.Archetypes 可能为空。
                bool hasDragonheir = progressionData.Archetypes != null &&
                                     progressionData.Archetypes.Any(a => a != null && a.AssetGuid == DragonheirScionArchetypeGuid);
                if (!hasDragonheir)
                    return;

                // 当前 progression 如果本身就是 FighterFeatSelection 的 progression，则保持显示；
                // 我们只在“职业主 progression”层面做去重，因此仍旧只按“同级多条 FighterFeatSelection 只留一条”处理。

                // 通过反射拿到内部 m_ProgressionLines 结构，逐级去重。
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var fLines = typeof(ProgressionVM).GetField("m_ProgressionLines", flags);
                var raw = fLines?.GetValue(__instance) as Dictionary<int, Dictionary<int, ProgressionVM.FeatureEntry>>;
                if (raw == null || raw.Count == 0)
                    return;

                // 对每个等级，保证最多只有一条 FighterFeatSelection FeatureEntry。
                // 战斗专长 selection 是 BlueprintFeatureSelection，guid 匹配 FighterFeatSelectionGuid。

                // level -> 列表(行号, FeatureEntry)
                var byLevel = new Dictionary<int, List<(int line, ProgressionVM.FeatureEntry entry)>>();

                foreach (var kvLine in raw)
                {
                    int line = kvLine.Key;
                    foreach (var kv in kvLine.Value)
                    {
                        int level = kv.Key;
                        var fe = kv.Value;
                        var sel = fe.Feature as BlueprintFeatureSelection;
                        if (sel == null || sel.AssetGuid != FighterFeatSelectionGuid)
                            continue;

                        if (!byLevel.TryGetValue(level, out var list))
                        {
                            list = new List<(int, ProgressionVM.FeatureEntry)>();
                            byLevel[level] = list;
                        }
                        list.Add((line, fe));
                    }
                }

                if (byLevel.Count == 0)
                    return;

                // 调试：确认过滤逻辑被激活以及涉及的等级。
                try
                {
                    Main.Log($"[DragonheirLegacy][ProgFilter] Active for progression {progressionBlueprint.name}, levels={string.Join(",", byLevel.Keys)}");
                }
                catch
                {
                    // 忽略日志中的任何异常，避免影响 UI。
                }

                foreach (var kv in byLevel)
                {
                    var entries = kv.Value;
                    if (entries.Count <= 1)
                        continue; // 同级本来就只有一条，无需处理

                    // 保留第一条，其余从对应行中移除
                    for (int i = 1; i < entries.Count; i++)
                    {
                        int line = entries[i].line;
                        var fe = entries[i].entry;
                        if (!raw.TryGetValue(line, out var lineDict))
                            continue;

                        // 在该行该等级上，如果还是同一个 FeatureEntry 就移除
                        if (lineDict.TryGetValue(fe.Level, out var existing) && ReferenceEquals(existing, fe))
                        {
                            lineDict.Remove(fe.Level);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirLegacy][ProgFilter] error: " + ex);
            }
        }
    }
}
