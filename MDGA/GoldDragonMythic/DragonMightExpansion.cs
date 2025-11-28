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
using Kingmaker.Utility; // Feet
using Kingmaker.Localization; // locale detection
using UnityEngine; // SystemLanguage
using Kingmaker.Blueprints.Facts;
using MDGA.Loc;

namespace MDGA.GoldDragonMythic
{
    // 在蓝图加载后：
    // 1) 将“龙族之力（DragonMight）”设为以施法者为中心，影响30尺内盟友
    // 2) 将所有 ContextActionApplyBuff 的持续时间改为按施法者等级（每等级2轮）
    // 3) 更新描述文本（中文）
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonMightExpansion
    {
        private static bool _done;
        private static readonly BlueprintGuid DragonMightGuid = BlueprintGuid.Parse("bfc6aa5be6bc41f68ca78aef37913e9f");
        // 如果无法通过 Progression=Double / MultiplyBy2 等枚举实现翻倍，则退回骰子算法 (DiceType.One & DiceCountValue=Rank & BonusValue=Rank)
        private static bool _needDiceDoubleFallback = false;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            try
            {
                var ability = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(DragonMightGuid);
                if (ability == null)
                {
                    Main.Log("[DragonMightExpansion] Ability blueprint not found.");
                    return;
                }

                // 1) 添加/确保范围组件：以施法者为中心影响30尺盟友
                EnsureTargetsAroundAllies30ft(ability);

                // 2) 将持续时间改为“每施法者等级2轮”：
                //    - 为能力添加 ContextRankConfig(CasterLevel)
                //    - 将所有 AbilityEffectRunAction 内的 ContextActionApplyBuff.DurationValue.BonusValue 改为 Rank
                EnsureCasterLevelRank(ability);
                TryRetargetBuffDurationsToRank(ability);

                // 3) 更新描述文本（注册动态本地化键并绑定）
                UpdateLocalizedDescription(ability);

                Main.Log("[DragonMightExpansion] Applied AoE allies=30ft, duration=2x CL rounds, description updated.");
            }
            catch (Exception ex)
            {
                Main.Log("[DragonMightExpansion] Exception: " + ex.Message);
            }
        }

        private static void EnsureTargetsAroundAllies30ft(BlueprintAbility ability)
        {
            try
            {
                var comps = GetComponentsArray(ability) ?? Array.Empty<BlueprintComponent>();
                bool hasAround = comps.OfType<AbilityTargetsAround>().Any();
                if (!hasAround)
                {
                    var around = new AbilityTargetsAround();
                    // 通过反射同时兼容 m_ 字段或公开属性命名
                    if (!SetFieldOrProp(around, "m_Radius", new Feet(30f))) SetFieldOrProp(around, "Radius", new Feet(30f));
                    if (!SetFieldOrProp(around, "m_TargetType", TargetType.Ally)) SetFieldOrProp(around, "TargetType", TargetType.Ally);
                    if (!SetFieldOrProp(around, "m_IncludeDead", false)) SetFieldOrProp(around, "IncludeDead", false);
                    var cond = new Kingmaker.ElementsSystem.ConditionsChecker();
                    if (!SetFieldOrProp(around, "m_Condition", cond)) SetFieldOrProp(around, "Condition", cond);
                    comps = comps.Concat(new BlueprintComponent[] { around }).ToArray();
                    SetComponentsArray(ability, comps);
                    if (Main.Settings?.VerboseLogging ?? false) Main.Log("[DragonMightExpansion] Added AbilityTargetsAround Allies 30ft.");
                }
                else
                {
                    // 如果已经存在，则尝试将半径与目标类型校正为 30ft / Ally
                    foreach (var a in comps.OfType<AbilityTargetsAround>())
                    {
                        try { if (!(SetFieldOrProp(a, "m_Radius", new Feet(30f)) || SetFieldOrProp(a, "Radius", new Feet(30f)))) { } } catch { }
                        try { if (!(SetFieldOrProp(a, "m_TargetType", TargetType.Ally) || SetFieldOrProp(a, "TargetType", TargetType.Ally))) { } } catch { }
                        try { if (!(SetFieldOrProp(a, "m_IncludeDead", false) || SetFieldOrProp(a, "IncludeDead", false))) { } } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonMightExpansion] EnsureTargetsAround error: " + ex.Message);
            }
        }

        private static void EnsureCasterLevelRank(BlueprintAbility ability)
        {
            try
            {
                var comps = GetComponentsArray(ability) ?? Array.Empty<BlueprintComponent>();
                var crcType = AccessTools.TypeByName("Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig");
                if (crcType == null) { Main.Log("[DragonMightExpansion] ContextRankConfig type missing."); return; }

                // 找到第一个 ContextRankConfig；若没有则新建
                var existing = comps.FirstOrDefault(c => c != null && c.GetType().Name == "ContextRankConfig");
                BlueprintComponent crcComp = existing;
                bool created = false;
                if (crcComp == null)
                {
                    crcComp = Activator.CreateInstance(crcType) as BlueprintComponent;
                    created = true;
                }

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var baseValField = crcType.GetField("m_BaseValueType", flags) ?? crcType.GetField("BaseValueType", flags);
                var progField = crcType.GetField("m_Progression", flags) ?? crcType.GetField("Progression", flags);
                var baseEnum = AccessTools.TypeByName("Kingmaker.UnitLogic.Mechanics.Components.ContextRankBaseValueType");
                var progEnum = AccessTools.TypeByName("Kingmaker.UnitLogic.Mechanics.Components.ContextRankProgression");

                string progressionApplied = "<none>";
                if (baseValField != null && baseEnum != null)
                {
                    try
                    {
                        var casterLevelVal = Enum.Parse(baseEnum, "CasterLevel");
                        baseValField.SetValue(crcComp, casterLevelVal);
                    }
                    catch { }
                }
                if (progField != null && progEnum != null)
                {
                    object targetVal = null;
                    string[] names = new[] { "Double", "MultiplyBy2", "MultiplyByTwo", "Times2", "TwoTimes" };
                    foreach (var n in names)
                    {
                        try { targetVal = Enum.Parse(progEnum, n); progressionApplied = n; break; } catch { }
                    }
                    if (targetVal == null)
                    {
                        // 无法找到倍增枚举，退回 AsIs + 标记需要骰子兜底
                        try { targetVal = Enum.Parse(progEnum, "AsIs"); progressionApplied = "AsIs"; _needDiceDoubleFallback = true; } catch { }
                    }
                    try { if (targetVal != null) progField.SetValue(crcComp, targetVal); } catch { }
                }
                else
                {
                    // 无 progression 字段，直接走骰子兜底
                    _needDiceDoubleFallback = true;
                }

                if (created)
                {
                    comps = comps.Concat(new BlueprintComponent[] { crcComp }).ToArray();
                    SetComponentsArray(ability, comps);
                }

                if (Main.Settings?.VerboseLogging ?? false)
                {
                    Main.Log($"[DragonMightExpansion] ContextRankConfig {(created ? "created" : "updated")} progression={progressionApplied} diceFallback={_needDiceDoubleFallback}");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonMightExpansion] EnsureCasterLevelRank error: " + ex.Message);
            }
        }

        private static void TryRetargetBuffDurationsToRank(BlueprintAbility ability)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var comps = GetComponentsArray(ability) ?? Array.Empty<BlueprintComponent>();
                foreach (var run in comps.OfType<AbilityEffectRunAction>())
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
                        // 直接动作：ContextActionApplyBuff
                        if (act is ContextActionApplyBuff cab)
                        {
                            RetargetDurationToRank(cab);
                        }
                        else
                        {
                            // 条件/嵌套动作中也可能包含 ApplyBuff
                            RetargetNestedApplyBuffs(act);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonMightExpansion] Retarget durations error: " + ex.Message);
            }
        }

        private static void RetargetNestedApplyBuffs(object action)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                // 常见的容器：Conditional.IfTrue/IfFalse, ActionList.Actions 等
                foreach (var f in action.GetType().GetFields(flags))
                {
                    object val = null; try { val = f.GetValue(action); } catch { }
                    if (val == null) continue;
                    if (val is ContextActionApplyBuff cab)
                    {
                        RetargetDurationToRank(cab);
                    }
                    else if (val is object[] arr)
                    {
                        foreach (var elem in arr) RetargetNestedApplyBuffs(elem);
                    }
                    else
                    {
                        // 进入子对象递归
                        var t = val.GetType();
                        // 跳过简单类型
                        if (t.IsPrimitive || t.IsEnum || t == typeof(string)) continue;
                        RetargetNestedApplyBuffs(val);
                    }
                }
            }
            catch { }
        }

        private static void RetargetDurationToRank(ContextActionApplyBuff cab)
        {
            try
            {
                var dv = cab.DurationValue; // struct copy
                dv.Rate = DurationRate.Rounds;
                if (_needDiceDoubleFallback)
                {
                    // 骰子兜底：DiceType.One，骰子数量=Rank，额外加值=Rank => 总计 2×Rank
                    dv.DiceType = Kingmaker.RuleSystem.DiceType.One;
                    dv.DiceCountValue = new ContextValue { ValueType = ContextValueType.Rank, Value = 0 };
                    dv.BonusValue = new ContextValue { ValueType = ContextValueType.Rank, Value = 0 };
                }
                else
                {
                    // 期望 progression 已倍增，直接 Bonus=Rank
                    dv.DiceType = Kingmaker.RuleSystem.DiceType.Zero;
                    dv.DiceCountValue = new ContextValue { ValueType = ContextValueType.Simple, Value = 0 };
                    dv.BonusValue = new ContextValue { ValueType = ContextValueType.Rank, Value = 0 };
                }
                cab.DurationValue = dv;
                cab.ToCaster = false; // 目标为范围内的单位
                if (Main.Settings?.VerboseLogging ?? false)
                {
                    Main.Log($"[DragonMightExpansion] ApplyBuff duration retargeted (diceFallback={_needDiceDoubleFallback})");
                }
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

        private static void UpdateLocalizedDescription(BlueprintAbility ability)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var fDesc = typeof(BlueprintUnitFact).GetField("m_Description", flags);
                var loc = fDesc?.GetValue(ability);
                if (loc != null)
                {
                    var tf = loc.GetType().GetField("m_Text", flags);
                    string zh = "你可以花费{g|Encyclopedia:Swift_Action}迅捷动作{/g}，令自己和周围30英尺内的盟友每施法者2{g|Encyclopedia:Combat_Round}轮{/g}内造成的{g|Encyclopedia:Damage}伤害{/g}提高50%。";
                    string en = "As a {g|Encyclopedia:Swift_Action}swift action{/g}, you and allies within 30 feet deal 50% more {g|Encyclopedia:Damage}damage{/g} for 2 {g|Encyclopedia:Combat_Round}rounds{/g} per {g|Encyclopedia:Caster_Level}caster level{/g}.";

                    // 使用我们已有的 LocalizationInjector 向当前语言包注册两个动态 key（更新后的 2 轮/CL 文案）
                    string keyZh = "MDGA_DragonMight_Desc_zh";
                    string keyEn = "MDGA_DragonMight_Desc_en";
                    try { LocalizationInjector.RegisterDynamicKey(keyZh, zh); } catch { }
                    try { LocalizationInjector.RegisterDynamicKey(keyEn, en); } catch { }
                    try { LocalizationInjector.EnsureInjected(); } catch { }

                    // 解除 Shared 本地化绑定（否则自定义 key 会被原共享表覆盖）
                    var sharedField = loc.GetType().GetField("Shared", flags);
                    if (sharedField != null) try { sharedField.SetValue(loc, null); } catch { }
                    // 绑定到根据当前语言选择的 key
                    var keyField = loc.GetType().GetField("m_Key", flags);
                    string bindKey = IsChinese() ? keyZh : keyEn;
                    if (keyField != null) try { keyField.SetValue(loc, bindKey); } catch { }
                    // 作为兜底，也写入 m_Text（QuickLocalization 未命中 key 时仍可显示）
                    tf?.SetValue(loc, IsChinese() ? zh : en);
                    if (Main.Settings?.VerboseLogging ?? false) Main.Log($"[DragonMightExpansion] Description key bound -> {bindKey}");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonMightExpansion] Update description error: " + ex.Message);
            }
        }

        // 移除强制 UI 动作类型覆盖：保留为原始（自由动作）。

        private static BlueprintComponent[] GetComponentsArray(BlueprintScriptableObject bp)
        {
            var t = typeof(BlueprintScriptableObject);
            var compField = t.GetField("Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                           ?? t.GetField("m_Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (compField != null)
            {
                var value = compField.GetValue(bp) as BlueprintComponent[];
                if (value != null) return value;
            }
            var pi = t.GetProperty("ComponentsArray", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return (pi?.GetValue(bp) as BlueprintComponent[]);
        }

        private static void SetComponentsArray(BlueprintScriptableObject bp, BlueprintComponent[] comps)
        {
            var t = typeof(BlueprintScriptableObject);
            var compField = t.GetField("Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                           ?? t.GetField("m_Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (compField != null) { compField.SetValue(bp, comps); return; }
            var pi = t.GetProperty("ComponentsArray", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (pi != null && pi.CanWrite) pi.SetValue(bp, comps);
        }

        // 工具：设置字段或属性（存在即设置，返回 true）
        private static bool SetFieldOrProp(object obj, string name, object value)
        {
            if (obj == null) return false;
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var f = t.GetField(name, flags);
            if (f != null)
            {
                try { f.SetValue(obj, value); return true; } catch { }
            }
            var p = t.GetProperty(name, flags);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(obj, value, null); return true; } catch { }
            }
            return false;
        }
    }
}
