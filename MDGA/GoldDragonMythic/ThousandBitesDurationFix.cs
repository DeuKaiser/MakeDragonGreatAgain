using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Localization;
using MDGA.Loc;
using Kingmaker.Blueprints.Facts; // Added for BlueprintUnitFact

namespace MDGA.GoldDragonMythic
{
    // 千重烈咬：将持续时间从固定 1 分钟改为 按施法者等级的 1 分钟/级，并更新描述与面板显示的持续时间文案
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class ThousandBitesDurationFix
    {
        private static bool _done;
        private static readonly BlueprintGuid ThousandBitesGuid = BlueprintGuid.Parse("d35b16edbd5c436286e34cf7bcbdb645");

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!MDGA.Main.Enabled) return;
            try
            {
                var ability = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(ThousandBitesGuid);
                if (ability == null) { MDGA.Main.Log("[ThousandBites] Ability not found."); return; }

                EnsureCasterLevelRank(ability);
                RetargetApplyBuffsToMinutesPerLevel(ability);
                UpdateLocalizedDescription(ability);
                UpdateLocalizedDuration(ability); // 新增：更新面板“持续时间”一行

                MDGA.Main.Log("[ThousandBites] Duration changed to 1 minute per caster level & description/duration text updated.");
            }
            catch (Exception ex)
            {
                MDGA.Main.Log("[ThousandBites] Exception: " + ex.Message);
            }
        }

        private static void EnsureCasterLevelRank(BlueprintAbility ability)
        {
            // 确保存在 ContextRankConfig，基于 CasterLevel，Progression=AsIs
            var comps = ability.ComponentsArray ?? Array.Empty<BlueprintComponent>();
            var crcType = AccessTools.TypeByName("Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig");
            if (crcType == null) { MDGA.Main.Log("[ThousandBites] ContextRankConfig type missing."); return; }

            var existing = comps.FirstOrDefault(c => c != null && c.GetType().Name == "ContextRankConfig");
            BlueprintComponent crcComp = existing ?? Activator.CreateInstance(crcType) as BlueprintComponent;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var baseValField = crcType.GetField("m_BaseValueType", flags) ?? crcType.GetField("BaseValueType", flags);
            var progField = crcType.GetField("m_Progression", flags) ?? crcType.GetField("Progression", flags);
            var baseEnum = AccessTools.TypeByName("Kingmaker.UnitLogic.Mechanics.Components.ContextRankBaseValueType");
            var progEnum = AccessTools.TypeByName("Kingmaker.UnitLogic.Mechanics.Components.ContextRankProgression");
            try
            {
                if (baseValField != null && baseEnum != null)
                {
                    var casterLevelVal = Enum.Parse(baseEnum, "CasterLevel");
                    baseValField.SetValue(crcComp, casterLevelVal);
                }
                if (progField != null && progEnum != null)
                {
                    var asIs = Enum.Parse(progEnum, "AsIs");
                    progField.SetValue(crcComp, asIs);
                }
            }
            catch { }

            if (existing == null)
            {
                ability.ComponentsArray = comps.Concat(new[] { crcComp }).ToArray();
            }
        }

        private static void RetargetApplyBuffsToMinutesPerLevel(BlueprintAbility ability)
        {
            // 遍历 AbilityEffectRunAction 的 Actions，将所有 ContextActionApplyBuff 的 DurationValue 设为 Minutes，BonusValue=Rank
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var run in ability.ComponentsArray.OfType<AbilityEffectRunAction>())
            {
                object actionList = run.GetType().GetField("Actions", flags)?.GetValue(run)
                                    ?? run.GetType().GetProperty("Actions", flags)?.GetValue(run, null);
                if (actionList == null) continue;
                var actionsArrField = actionList.GetType().GetField("Actions", flags)
                                     ?? actionList.GetType().GetField("m_Actions", flags);
                var arr = actionsArrField?.GetValue(actionList) as object[];
                if (arr == null || arr.Length == 0) continue;

                for (int i = 0; i < arr.Length; i++)
                {
                    var act = arr[i]; if (act == null) continue;
                    if (act is ContextActionApplyBuff cab)
                    {
                        SetMinutesPerLevel(cab);
                    }
                    else
                    {
                        RetargetNestedApplyBuffs(act);
                    }
                }
            }
        }

        private static void RetargetNestedApplyBuffs(object action)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var f in action.GetType().GetFields(flags))
            {
                object val = null; try { val = f.GetValue(action); } catch { }
                if (val == null) continue;
                if (val is ContextActionApplyBuff cab)
                {
                    SetMinutesPerLevel(cab);
                }
                else if (val is object[] arr)
                {
                    foreach (var elem in arr) RetargetNestedApplyBuffs(elem);
                }
                else
                {
                    var t = val.GetType();
                    if (t.IsPrimitive || t.IsEnum || t == typeof(string)) continue;
                    RetargetNestedApplyBuffs(val);
                }
            }
        }

        private static void SetMinutesPerLevel(ContextActionApplyBuff cab)
        {
            try
            {
                var dv = cab.DurationValue; // struct
                dv.Rate = DurationRate.Minutes;
                dv.DiceType = Kingmaker.RuleSystem.DiceType.Zero;
                dv.DiceCountValue = new ContextValue { ValueType = ContextValueType.Simple, Value = 0 };
                dv.BonusValue = new ContextValue { ValueType = ContextValueType.Rank, Value = 0 }; // Rank = CasterLevel
                cab.DurationValue = dv;
            }
            catch { }
        }

        private static void UpdateLocalizedDescription(BlueprintAbility ability)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var descField = typeof(BlueprintUnitFact).GetField("m_Description", flags);
                var locObj = descField?.GetValue(ability);
                if (locObj == null) return;

                string zh = "在金龙形态下，你能够花费{g|Encyclopedia:Swift_Action}迅捷动作{/g}，在每{g|Encyclopedia:Caster_Level}施法者等级{/g}1分钟内获得3次额外{g|Encyclopedia:Attack}攻击{/g}。这些额外攻击可与{g|SpellsHaste}加速术{/g}及其类似效果提供的额外攻击次数叠加。";
                string en = "While in the form of a Gold Dragon, as a {g|Encyclopedia:Swift_Action}swift action{/g}, you gain 3 additional {g|Encyclopedia:Attack}attacks{/g} for 1 minute per {g|Encyclopedia:Caster_Level}caster level{/g}. These additional attacks stack with the bonus attacks from {g|SpellsHaste}haste{/g} and other similar effects.";

                string keyZh = "MDGA_ThousandBites_Desc_zh";
                string keyEn = "MDGA_ThousandBites_Desc_en";
                try { LocalizationInjector.RegisterDynamicKey(keyZh, zh); } catch { }
                try { LocalizationInjector.RegisterDynamicKey(keyEn, en); } catch { }
                try { LocalizationInjector.EnsureInjected(); } catch { }

                bool isZh = IsChinese();
                var sharedField = locObj.GetType().GetField("Shared", flags);
                if (sharedField != null) try { sharedField.SetValue(locObj, null); } catch { }
                var keyField = locObj.GetType().GetField("m_Key", flags);
                if (keyField != null) try { keyField.SetValue(locObj, isZh ? keyZh : keyEn); } catch { }
                var textField = locObj.GetType().GetField("m_Text", flags);
                if (textField != null) try { textField.SetValue(locObj, isZh ? zh : en); } catch { }
            }
            catch { }
        }

        private static void UpdateLocalizedDuration(BlueprintAbility ability)
        {
            // 更新面板上的“持续时间”字段（LocalizedDuration），改为原版样式 “1分钟/级” / "1 minute/level"
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var durField = typeof(BlueprintAbility).GetField("LocalizedDuration", flags) ?? typeof(BlueprintAbility).GetField("m_LocalizedDuration", flags);
                var locObj = durField?.GetValue(ability);
                if (locObj == null) return;
                string zh = "1分钟/级"; // 原版样式
                string en = "1 minute/level"; // Original style
                string keyZh = "MDGA_ThousandBites_Duration_zh";
                string keyEn = "MDGA_ThousandBites_Duration_en";
                try { LocalizationInjector.RegisterDynamicKey(keyZh, zh); } catch { }
                try { LocalizationInjector.RegisterDynamicKey(keyEn, en); } catch { }
                try { LocalizationInjector.EnsureInjected(); } catch { }
                bool isZh = IsChinese();
                var sharedField = locObj.GetType().GetField("Shared", flags); if (sharedField != null) try { sharedField.SetValue(locObj, null); } catch { }
                var keyField = locObj.GetType().GetField("m_Key", flags); if (keyField != null) try { keyField.SetValue(locObj, isZh ? keyZh : keyEn); } catch { }
                var textField = locObj.GetType().GetField("m_Text", flags); if (textField != null) try { textField.SetValue(locObj, isZh ? zh : en); } catch { }
            }
            catch { }
        }

        private static bool IsChinese()
        {
            try
            {
                var loc = LocalizationManager.CurrentLocale;
                if (loc != null)
                {
                    string locStr = loc.ToString();
                    if (!string.IsNullOrEmpty(locStr) && locStr.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var langProp = loc.GetType().GetProperty("Language", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var langObj = langProp?.GetValue(loc, null);
                    if (langObj != null && langObj.ToString().ToLower().StartsWith("zh")) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
