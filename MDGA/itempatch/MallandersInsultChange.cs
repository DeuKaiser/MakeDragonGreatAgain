using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.Facts; // BlueprintUnitFact
using Kingmaker.Localization; // LocalizedString
using MDGA.Loc; // LocalizationInjector
using Kingmaker.UnitLogic.Buffs.Blueprints; // BlueprintBuff
using UnityEngine; // Application.systemLanguage

namespace MDGA.itempatch
{
    // 优化“马兰德的耻辱（夕阳骑士徽记腰带）”
    // 1) 让火焰增伤对“法术类/超自然（吐息）”也生效（原只检查 Spell）
    // 2) 覆盖物品/能力/增益说明文本
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class MallandersInsultChange
    {
        private static bool _done;
        private static readonly BlueprintGuid BeltItemGuid = BlueprintGuid.Parse("f21d7afb2cd9c2a408677be2059eab19");
        private static readonly BlueprintGuid BeltBuffGuid = BlueprintGuid.Parse("55f44ad3de06b6b4083046a65469dde4");
        private static readonly BlueprintGuid BeltAbilityGuid = BlueprintGuid.Parse("19a21e1bbb441ee42b91e7bc71a4e2c4");

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            try
            {
                ExpandOutgoingTriggers();
                UpdateDescriptions();
            }
            catch (Exception ex)
            {
                Main.Log("[MallandersInsult] Exception: " + ex);
            }
        }

        private static void ExpandOutgoingTriggers()
        {
            var buff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(BeltBuffGuid);
            if (buff == null) { Main.Log("[MallandersInsult] Buff not found."); return; }

            var comps = GetComponentsArray(buff) ?? Array.Empty<BlueprintComponent>();
            var triggerType = AccessTools.TypeByName("Kingmaker.UnitLogic.Mechanics.Components.AddOutgoingDamageTrigger");
            if (triggerType == null)
            {
                Main.Log("[MallandersInsult] AddOutgoingDamageTrigger type missing.");
                return;
            }

            // 修正：原先枚举命名空间写错，导致始终为 null
            var abilityTypeEnum = AccessTools.TypeByName("Kingmaker.UnitLogic.Abilities.Blueprints.AbilityType")
                               ?? AccessTools.TypeByName("Kingmaker.UnitLogic.Abilities.AbilityType");

            var spellTriggers = comps.Where(c => c != null && c.GetType() == triggerType).ToList();
            if (spellTriggers.Count == 0) { Main.Log("[MallandersInsult] No AddOutgoingDamageTrigger on buff."); return; }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fiCheckAbilityType = triggerType.GetField("CheckAbilityType", flags) ?? triggerType.GetField("m_CheckAbilityType", flags);
            var fiAbilityType = triggerType.GetField("m_AbilityType", flags) ?? triggerType.GetField("AbilityType", flags);
            var fiCheckEnergyType = triggerType.GetField("CheckEnergyDamageType", flags) ?? triggerType.GetField("m_CheckEnergyDamageType", flags);
            var fiEnergyType = triggerType.GetField("EnergyType", flags) ?? triggerType.GetField("m_EnergyType", flags);
            var fiApplyToAoE = triggerType.GetField("ApplyToAreaEffectDamage", flags) ?? triggerType.GetField("m_ApplyToAreaEffectDamage", flags);

            string GetAbilityTypeName(object trigger)
            {
                try { var v = fiAbilityType?.GetValue(trigger); return v?.ToString() ?? string.Empty; } catch { return string.Empty; }
            }
            bool IsFireFiltered(object trigger)
            {
                try
                {
                    var check = (bool)(fiCheckEnergyType?.GetValue(trigger) ?? false);
                    var e = fiEnergyType?.GetValue(trigger)?.ToString() ?? string.Empty;
                    return check && string.Equals(e, "Fire", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }

            // 方案B：直接取消能力类型检查，并且打开 AoE 检查。这样所有来源的“火焰伤害”都会触发污邪2d6。
            int toggled = 0;
            foreach (var t in spellTriggers)
            {
                try
                {
                    if (IsFireFiltered(t))
                    {
                        // 1) 忽略 AbilityType
                        if (fiCheckAbilityType != null)
                        {
                            fiCheckAbilityType.SetValue(t, false);
                        }
                        // 2) AoE 也触发
                        try { fiApplyToAoE?.SetValue(t, true); } catch { }
                        toggled++;
                    }
                }
                catch { }
            }

            // 仍然保底：如果没有任何一条启用了 AoE/去除能力类型，则基于现有模板克隆一条“通用”触发器
            if (toggled == 0)
            {
                var template = spellTriggers.FirstOrDefault(IsFireFiltered) ?? spellTriggers.FirstOrDefault();
                if (template != null)
                {
                    var clone = CloneComponent(template);
                    try
                    {
                        // 忽略能力类型筛选
                        if (fiCheckAbilityType != null) fiCheckAbilityType.SetValue(clone, false);
                        // AoE 生效
                        try { fiApplyToAoE?.SetValue(clone, true); } catch { }
                        // 能量过滤保持 Fire
                        if (fiCheckEnergyType != null) fiCheckEnergyType.SetValue(clone, true);
                        if (fiEnergyType != null) fiEnergyType.SetValue(clone, Enum.Parse(fiEnergyType.FieldType, "Fire"));
                        comps = comps.Concat(new[] { clone }).ToArray();
                        SetComponentsArray(buff, comps);
                        toggled = 1;
                    }
                    catch (Exception setEx)
                    {
                        Main.Log("[MallandersInsult] Fallback create trigger failed: " + setEx.Message);
                    }
                }
            }

            Main.Log(toggled > 0
                ? $"[MallandersInsult] Enabled AoE and removed AbilityType check on {toggled} fire triggers (belt buff {buff.name})."
                : "[MallandersInsult] Could not modify or create fire triggers.");
        }

        private static void UpdateDescriptions()
        {
            // 文案（中/英）
            const string zh = "你可以让自己被一层不可见的腐化之火包围。在其影响下，你的所有火焰{g|Encyclopedia:Spell}法术{/g}或{g|Encyclopedia:Special_Abilities}类法术能力{/g}（包括{g|Encyclopedia:BreathWeapon}吐息{/g}）造成额外{g|Encyclopedia:Dice}2d6{/g}点{g|Encyclopedia:Energy_Damage}污邪伤害{/g}。你将{g|Encyclopedia:Energy_Immunity}免疫{/g}火焰伤害，但获得对寒冷和神圣伤害的弱点。";
            const string en = "You can wreathe yourself in invisible corrupted fire. While active, all your fire spells or spell-like/supernatural abilities (including breath weapons) deal an additional 2d6 unholy damage. You become immune to fire damage, but gain vulnerability to cold and holy.";

            TryOverrideDescription(BeltAbilityGuid, zh, en);
            TryOverrideDescription(BeltBuffGuid, zh, en);
            TryOverrideDescription(BeltItemGuid, zh, en);
        }

        private static void TryOverrideDescription(BlueprintGuid guid, string zh, string en)
        {
            var bp = ResourcesLibrary.TryGetBlueprint(guid);
            if (bp == null) return;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var fDesc = typeof(BlueprintUnitFact).IsAssignableFrom(bp.GetType())
                    ? bp.GetType().GetField("m_Description", flags)
                    : bp.GetType().GetField("m_DescriptionText", flags) ?? bp.GetType().GetField("m_Description", flags);

                if (fDesc == null)
                {
                    // 某些物品使用统一根类型字段名不同，尝试逐层父类
                    Type t = bp.GetType();
                    while (t != null && fDesc == null)
                    {
                        fDesc = t.GetField("m_Description", flags) ?? t.GetField("m_DescriptionText", flags);
                        t = t.BaseType;
                    }
                }

                if (fDesc == null) return;
                var loc = fDesc.GetValue(bp);
                if (loc == null)
                {
                    // 构建一个 LocalizedString 实例
                    loc = Activator.CreateInstance(typeof(LocalizedString));
                }

                // 注册并绑定动态 key
                string keyZh = $"MDGA_MallandersInsult_{guid}_zh";
                string keyEn = $"MDGA_MallandersInsult_{guid}_en";
                try { LocalizationInjector.RegisterDynamicKey(keyZh, zh); } catch { }
                try { LocalizationInjector.RegisterDynamicKey(keyEn, en); } catch { }
                try { LocalizationInjector.EnsureInjected(); } catch { }

                bool zhEnv = IsChinese();
                var locType = loc.GetType();
                var keyField = locType.GetField("m_Key", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var textField = locType.GetField("m_Text", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var sharedField = locType.GetField("Shared", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                try { sharedField?.SetValue(loc, null); } catch { }
                keyField?.SetValue(loc, zhEnv ? keyZh : keyEn);
                textField?.SetValue(loc, zhEnv ? zh : en); // 兜底文本
                fDesc.SetValue(bp, loc);

                // 某些蓝图有缓存，需要刷新（如果存在）
                try
                {
                    var cacheField = bp.GetType().GetField("m_DescriptionModifiersCache", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (cacheField != null) cacheField.SetValue(bp, null);
                }
                catch { }

                Main.Log($"[MallandersInsult] Description overridden for {bp.name} ({guid}).");
            }
            catch (Exception ex)
            {
                Main.Log($"[MallandersInsult] Override description error {guid}: {ex.Message}");
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
                    if (!string.IsNullOrEmpty(locStr) && locStr.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var langProp = loc.GetType().GetProperty("Language", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var langObj = langProp?.GetValue(loc, null);
                    if (langObj != null && langObj.ToString().ToLower().StartsWith("zh")) return true;
                }
            }
            catch { }
            try
            {
                var ci = System.Globalization.CultureInfo.CurrentUICulture;
                if (ci != null && ci.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return true;
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

        // -------- 通用反射工具 --------
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

        private static BlueprintComponent CloneComponent(BlueprintComponent src)
        {
            if (src == null) return null;
            BlueprintComponent inst;
            try { inst = (BlueprintComponent)Activator.CreateInstance(src.GetType()); }
            catch { inst = (BlueprintComponent)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(src.GetType()); }
            CopyAllFields(src, inst);
            return inst;
        }

        private static void CopyAllFields(object src, object dst)
        {
            if (src == null || dst == null) return;
            var t = src.GetType();
            for (; t != null; t = t.BaseType)
            {
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var f in fields)
                {
                    try
                    {
                        var val = f.GetValue(src);
                        if (val is Array arr)
                        {
                            var len = arr.Length;
                            var newArr = Array.CreateInstance(arr.GetType().GetElementType(), len);
                            Array.Copy(arr, newArr, len);
                            f.SetValue(dst, newArr);
                        }
                        else
                        {
                            f.SetValue(dst, val);
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
