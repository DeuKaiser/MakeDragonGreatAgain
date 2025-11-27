using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Components; // ContextRankConfig
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Mechanics; // MechanicsContext

namespace MDGA.Patch
{
    // 方案B：仅当奥术打击 Buff 的 RankConfig 需要施法者等级时，对拥有龙之贵胄原型的角色改用该职业等级。
    // 不改蓝图，保持其他职业的奥术打击仍按原始“最高施法者等级”(MaxCasterLevel) 计算。
    [HarmonyPatch(typeof(ContextRankConfig), nameof(ContextRankConfig.GetValue))]
    internal static class ArcaneStrikeDragonheirOverride
    {
        private static readonly BlueprintGuid ArcaneStrikeBuffGuid = BlueprintGuid.Parse("98ac795afd1b2014eb9fdf2b9820808f");
        private static readonly BlueprintGuid FighterClassGuid = BlueprintGuid.Parse("48ac8db94d5de7645906c7d0ad3bcfbd");
        private static readonly BlueprintGuid DragonheirScionArchetypeGuid = BlueprintGuid.Parse("8dff97413c63c1147be8a5ca229abefc");

        static void Postfix(ContextRankConfig __instance, MechanicsContext context, ref int __result)
        {
            if (!Main.Enabled) return;
            try
            {
                var ownerBp = __instance?.OwnerBlueprint;
                if (ownerBp == null || ownerBp.AssetGuid != ArcaneStrikeBuffGuid) return;
                var baseTypeField = AccessTools.Field(typeof(ContextRankConfig), "m_BaseValueType") ?? AccessTools.Field(typeof(ContextRankConfig), "BaseValueType");
                if (baseTypeField != null)
                {
                    var baseEnumVal = baseTypeField.GetValue(__instance)?.ToString();
                    if (baseEnumVal != "MaxCasterLevel" && baseEnumVal != "CasterLevel" && baseEnumVal != "Default") return;
                }
                var caster = context?.MaybeCaster; if (caster == null) return;
                var prog = caster.Descriptor?.Progression; if (prog == null) return;
                var clsData = prog.Classes.FirstOrDefault(cd => cd.CharacterClass.AssetGuid == FighterClassGuid && cd.Archetypes.Any(a => a.AssetGuid == DragonheirScionArchetypeGuid));
                if (clsData == null) return;
                int dragonheirLevel = clsData.Level; if (dragonheirLevel <= 0) return;
                // 原始奥术打击公式：Rank = 1 + floor(CasterLevel / 5), capped at 5 ( +1 base then +1 per 5 levels, max +5 at 20 )
                int rank = 1 + (dragonheirLevel / 5);
                if (rank > 5) rank = 5;
                __result = rank; // 使用龙之贵胄等级映射后的 Rank
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[ArcaneStrikeOverride] Exception: " + ex.Message);
            }
        }
    }
}
