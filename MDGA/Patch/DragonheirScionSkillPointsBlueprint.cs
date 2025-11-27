using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using UnityEngine;

namespace MDGA.Patch
{
    // 通过修改蓝图原型，直接为龙之贵胄增加 +2 技能点（总计 4）。
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    public static class DragonheirScionSkillPointsBlueprint
    {
        private static bool s_Initialized;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (s_Initialized) return;
            s_Initialized = true;

            try
            {
                var dragonheirGuid = new BlueprintGuid(System.Guid.Parse("8dff97413c63c1147be8a5ca229abefc"));
                var dragonheir = ResourcesLibrary.TryGetBlueprint<BlueprintArchetype>(dragonheirGuid);
                if (dragonheir == null)
                {
                    Debug.Log("[MDGA] DragonheirScionSkillPointsBlueprint: archetype not found.");
                    return;
                }

                // 将原型上的 AddSkillPoints 设为 +2（战士基础 2 → 合计 4）。
                dragonheir.AddSkillPoints = 2;
                Debug.Log("[MDGA] DragonheirScionSkillPointsBlueprint: Set AddSkillPoints = 2.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MDGA] DragonheirScionSkillPointsBlueprint: Error {e}");
            }
        }
    }
}
