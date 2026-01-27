using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.FactLogic;
using System.Linq;

namespace MDGA.Components
{
    // Postfix 打在 DC 的 CreateLimitlessBloodlineClaws 上，追加黑暗仪式切换能力到 True Dragon 特性
    [HarmonyPatch]
    internal static class TrueDragonDcPostfix
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var dcType = System.Type.GetType("DarkCodex.Mythic, DarkCodex", throwOnError: false);
            return dcType?.GetMethod("CreateLimitlessBloodlineClaws", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        }

        // DC method returns void; just run our augmentation after it completed
        static void Postfix()
        {
            if (!MDGA.Mythic.TrueDragon.IsDarkCodexLoaded()) return;
            // 触发创建并附加“龙族威仪”切换能力的完整流程
            MDGA.Mythic.TrueDragon.ApplyIfDcEnabled();
        }
    }
}
