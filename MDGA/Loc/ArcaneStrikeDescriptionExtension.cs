using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Localization;
using UnityEngine;

namespace MDGA.Loc
{
    // 为奥术打击描述追加龙之贵胄特殊说明（修复显示 GUID 的问题，直接写入原始基础文本再追加说明）
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class ArcaneStrikeDescriptionExtension
    {
        private static bool _done;
        private static readonly BlueprintGuid ArcaneStrikeFeatureGuid = BlueprintGuid.Parse("0ab2f21a922feee4dab116238e3150b4");
        private static readonly BlueprintGuid ArcaneStrikeAbilityGuid = BlueprintGuid.Parse("006c6015761e75e498026cd3cd88de7e");
        private static readonly BlueprintGuid ArcaneStrikeBuffGuid    = BlueprintGuid.Parse("98ac795afd1b2014eb9fdf2b9820808f");

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            try
            {
                bool isZh = IsChinese();
                string appendZh = "（若为龙之贵胄，则计算龙之贵胄职业等级代替施法者等级）";
                string appendEn = " (If you are a Dragonheir Scion, your Dragonheir Scion class levels are used instead of caster level.)";

                // 原始基础描述（未被本地化表成功提取时的兜底）
                string baseZh = "每轮消耗一个{g|Encyclopedia:Swift_Action}迅捷动作{/g}，将自己一部分的力量注入武器中。在1轮内，你的武器造成的{g|Encyclopedia:Damage}伤害{/g}＋1，并视为魔法以克服{g|Encyclopedia:Damage_Reduction}伤害减免{/g}。每有5个施法者等级此加值再＋1，在20级达到＋5。";
                string baseEn = "Spending a {g|Encyclopedia:Swift_Action}swift action{/g} each round, you can imbue your weapons with power. For 1 round, your weapons deal +1 {g|Encyclopedia:Damage}damage{/g} and count as magic for overcoming {g|Encyclopedia:Damage_Reduction}damage reduction{/g}. For every 5 caster levels, this bonus increases by +1, to a maximum of +5 at level 20.";

                string finalZh = baseZh + appendZh;
                string finalEn = baseEn + appendEn;

                PatchDescription(ArcaneStrikeFeatureGuid, isZh ? finalZh : finalEn, isZh);
                PatchDescription(ArcaneStrikeAbilityGuid, isZh ? finalZh : finalEn, isZh);
                PatchDescription(ArcaneStrikeBuffGuid, isZh ? finalZh : finalEn, isZh);
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[ArcaneStrikeDescExt] Error: " + ex.Message);
            }
        }

        private static void PatchDescription(BlueprintGuid guid, string newText, bool zh)
        {
            try
            {
                var bp = ResourcesLibrary.TryGetBlueprint<BlueprintScriptableObject>(guid);
                if (bp == null) return;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                FieldInfo fDesc = null; Type t = bp.GetType();
                while (t != null && fDesc == null) { fDesc = t.GetField("m_Description", flags); t = t.BaseType; }
                if (fDesc == null) return;
                var loc = fDesc.GetValue(bp); if (loc == null) return;
                var keyF = loc.GetType().GetField("m_Key", flags);
                var textF = loc.GetType().GetField("m_Text", flags);
                var sharedF = loc.GetType().GetField("Shared", flags);

                // 解除共享并强制绑定动态 key 与文本（避免被外部覆盖），不做 Contains 判定以规避空值异常
                sharedF?.SetValue(loc, null);
                string dynKey = "MDGA_ArcaneStrikeDesc_" + guid + (zh?"_zh":"_en");
                LocalizationInjector.RegisterDynamicKey(dynKey, newText);
                keyF?.SetValue(loc, dynKey);
                textF?.SetValue(loc, newText);
                // 确保注入器实际把文本放进包里
                try { LocalizationInjector.EnsureInjected(); } catch { }

                if (Main.Settings.VerboseLogging) Main.Log("[ArcaneStrikeDescExt] Patched " + guid);
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[ArcaneStrikeDescExt] PatchDescription error: " + ex.Message);
            }
        }

        private static bool IsChinese()
        {
            try
            {
                var loc = LocalizationManager.CurrentLocale;
                if (loc != null)
                {
                    string locStr = loc.ToString();
                    if (!string.IsNullOrEmpty(locStr) && locStr.IndexOf("zh", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var langProp = loc.GetType().GetProperty("Language", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var langObj = langProp?.GetValue(loc, null);
                    if (langObj != null && langObj.ToString().ToLower().StartsWith("zh")) return true;
                }
            }
            catch { }
            try
            {
                if (Application.systemLanguage == SystemLanguage.ChineseSimplified || Application.systemLanguage == SystemLanguage.Chinese || Application.systemLanguage == SystemLanguage.ChineseTraditional)
                    return true;
            }
            catch { }
            return false;
        }
    }
}
