using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.FactLogic; // DescriptionModifier

namespace MDGA.Patch
{
    /// <summary>
    /// 为龙族秘法特性在运行时追加“随等级提升的每骰加值说明”（5/10/15级依次为+2/+3/+4）。
    /// 不改动原有本地化键，仅在当前文本后拼接说明。
    /// </summary>
    [TypeId("6b6a2a74-e2a1-407c-9bcf-4f2b1a1d0aca")] // 有效 GUID（此前无效字符串会导致 PatchAll 失败）
    internal sealed class ArcanaScalingDescriptionModifier : DescriptionModifier
    {
        private const string ZhSuffix = " 在5级时该加值变为每骰+2，在10级为每骰+3，在15级为每骰+4。";
        private const string EnSuffix = " At 5th level this bonus increases to +2 per die, at 10th level to +3, and at 15th level to +4.";
        public override string Modify(string originalString)
        {
            try
            {
                if (string.IsNullOrEmpty(originalString)) return originalString;
                // 若已包含 15 级终阶提示（中/英），则不再重复追加
                if (originalString.Contains("15级") || originalString.Contains("15级为每骰+4") || originalString.Contains("15th level") || originalString.Contains("15th"))
                    return originalString;
                bool hasChinese = originalString.Any(c => c >= '\u4e00' && c <= '\u9fff');
                return originalString.TrimEnd() + (hasChinese ? ZhSuffix : EnSuffix);
            }
            catch { return originalString; }
        }
    }

    /// <summary>
    /// 在蓝图加载后把 ArcanaScalingDescriptionModifier 注入到 10 个龙族秘法特性上。
    /// </summary>
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class ArcanaScalingDescriptionModifierInjector
    {
        private static bool _done;
        private static readonly BlueprintGuid[] ArcanaGuids = new[]
        {
            BlueprintGuid.Parse("ac04aa27a6fd8b4409b024a6544c4928"), // Gold
            BlueprintGuid.Parse("a8baee8eb681d53438cc17bd1d125890"), // Red
            BlueprintGuid.Parse("153e9b6b5b0f34d45ae8e815838aca80"), // Brass
            BlueprintGuid.Parse("5515ae09c952ae2449410ab3680462ed"), // Black
            BlueprintGuid.Parse("caebe2fa3b5a94d4bbc19ccca86d1d6f"), // Green
            BlueprintGuid.Parse("2a8ed839d57f31a4983041645f5832e2"), // Copper
            BlueprintGuid.Parse("1af96d3ab792e3048b5e0ca47f3a524b"), // Silver
            BlueprintGuid.Parse("456e305ebfec3204683b72a45467d87c"), // White
            BlueprintGuid.Parse("0f0cb88a2ccc0814aa64c41fd251e84e"), // Blue
            BlueprintGuid.Parse("677ae97f60d26474bbc24a50520f9424")  // Bronze
        };

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            try
            {
                int added = 0; int already = 0; int missing = 0;
                foreach (var guid in ArcanaGuids)
                {
                    var feat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(guid);
                    if (feat == null) { missing++; continue; }
                    try
                    {
                        // 通过反射读取/写回组件数组
                        var bpType = typeof(BlueprintScriptableObject);
                        var compField = bpType.GetField("m_Components", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        var comps = compField?.GetValue(feat) as BlueprintComponent[] ?? Array.Empty<BlueprintComponent>();
                        if (comps.OfType<ArcanaScalingDescriptionModifier>().Any()) { already++; continue; }
                        var newComps = comps.Concat(new BlueprintComponent[] { new ArcanaScalingDescriptionModifier() }).ToArray();
                        compField?.SetValue(feat, newComps);
                        // 清空描述修饰符缓存，确保新组件立即生效
                        var cacheField = feat.GetType().GetField("m_DescriptionModifiersCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (cacheField != null) try { cacheField.SetValue(feat, null); } catch { }
                        added++;
                    }
                    catch (Exception exFeat)
                    {
                        Main.Log("[ArcanaDescMod] Error patching feature " + guid + " : " + exFeat.Message);
                    }
                }
                Main.Log($"[ArcanaDescMod] Injected modifier added={added} already={already} missing={missing}");
            }
            catch (Exception ex)
            {
                Main.Log("[ArcanaDescMod] Injector exception: " + ex.Message);
            }
        }
    }
}
