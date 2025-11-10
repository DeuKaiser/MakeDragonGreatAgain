using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes; // BlueprintFeature
using Kingmaker.Blueprints.Facts; // BlueprintUnitFact
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.Classes.Spells; // SpellDescriptor extensions
using Kingmaker.Designers.Mechanics.Facts; // DraconicBloodlineArcana
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem; // for EntityFact
using Kingmaker.Enums.Damage; // DamageEnergyType

namespace MDGA.Patch
{
    [HarmonyPatch(typeof(DraconicBloodlineArcana), nameof(DraconicBloodlineArcana.OnEventAboutToTrigger))]
    [HarmonyPriority(Priority.First)] // 先于原有逻辑执行，以便完全替换加值算法
    internal static class DraconicBloodlineArcanaScalingPatch
    {
        private static readonly BlueprintGuid SorcererClassGuid = BlueprintGuid.Parse("b3a505fb61437dc4097f43c3f8f9a4cf");
        private static readonly BlueprintGuid DragonDiscipleClassGuid = BlueprintGuid.Parse("72051275b1dbb2d42ba9118237794f7c");

        internal static readonly BlueprintGuid[] ArcanaGuids = new[]
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

        private static bool _locAugmented;

        // 追加描述（本地化层面后补说明）
        [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
        private static class DraconicArcanaLocalizationAugment
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (_locAugmented) return; _locAugmented = true;
                try
                {
                    if (!Main.Enabled) return;
                    foreach (var guid in ArcanaGuids)
                    {
                        var feat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(guid);
                        if (feat == null) continue;
                        try
                        {
                            var descField = typeof(BlueprintUnitFact).GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            var locObj = descField?.GetValue(feat);
                            if (locObj == null) continue;
                            var textField = locObj.GetType().GetField("m_Text", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            var current = textField?.GetValue(locObj) as string ?? string.Empty;
                            if (current.Contains("5th") || current.Contains("5级")) continue; // 已包含
                            string suffixEn = " (Damage per die increases to +2 at 5th level, +3 at 10th, and +4 at 15th.)";
                            string suffixZh = " 在5级时该加值变为每骰+2，在10级为每骰+3，在15级为每骰+4。";
                            bool hasChinese = current.Any(c => c >= '\u4e00' && c <= '\u9fff');
                            string newText = hasChinese ? (current + suffixZh) : (current + suffixEn + suffixZh);
                            textField?.SetValue(locObj, newText);
                        }
                        catch (Exception exSub)
                        {
                            Main.Log("[ArcanaScale][Loc] Sub-error: " + exSub.Message);
                        }
                    }
                    Main.Log("[ArcanaScale] Localization descriptions augmented.");
                }
                catch (Exception ex)
                {
                    Main.Log("[ArcanaScale] Localization augment error: " + ex.Message);
                }
            }
        }

        private static EntityFact GetOwningFactFromPropertyOrField(object component)
        {
            if (component == null) return null;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type t = component.GetType();
            while (t != null)
            {
                try
                {
                    var prop = t.GetProperty("Fact", flags) ?? t.GetProperty("Owner", flags);
                    if (prop != null)
                    {
                        var val = prop.GetValue(component, null) as EntityFact;
                        if (val != null) return val;
                    }
                }
                catch { }
                try
                {
                    var field = t.GetField("m_Fact", flags) ?? t.GetField("Fact", flags) ?? t.GetField("OwnerFact", flags);
                    if (field != null)
                    {
                        var val = field.GetValue(component) as EntityFact;
                        if (val != null) return val;
                    }
                }
                catch { }
                t = t.BaseType;
            }
            return null;
        }

        private static EntityFact FindFactContainingComponent(object component, UnitDescriptor unit)
        {
            if (component == null || unit == null) return null;
            var list = unit.Facts?.List; if (list == null) return null;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            foreach (var f in list)
            {
                if (f == null) continue;
                try
                {
                    // 在 EntityFact 上尝试通过 m_Components/Components 字段查找
                    var ft = f.GetType();
                    var compField = ft.GetField("m_Components", flags) ?? ft.GetField("Components", flags);
                    var compObj = compField?.GetValue(f);
                    var enumerable = compObj as System.Collections.IEnumerable;
                    if (enumerable != null)
                    {
                        foreach (var c in enumerable)
                        {
                            if (object.ReferenceEquals(c, component)) return f;
                        }
                    }
                    // 某些版本改为属性暴露
                    var compProp = ft.GetProperty("Components", flags) ?? ft.GetProperty("AllComponents", flags);
                    var compVal = compProp?.GetValue(f, null) as System.Collections.IEnumerable;
                    if (compVal != null)
                    {
                        foreach (var c in compVal)
                        {
                            if (object.ReferenceEquals(c, component)) return f;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private static bool DescriptorMatchesEnergy(SpellDescriptor descriptor, BaseDamage damage)
        {
            try
            {
                var ed = damage as EnergyDamage; if (ed == null) return false;
                if (descriptor.HasAnyFlag(SpellDescriptor.Acid) && ed.EnergyType == DamageEnergyType.Acid) return true;
                if (descriptor.HasAnyFlag(SpellDescriptor.Fire) && ed.EnergyType == DamageEnergyType.Fire) return true;
                if (descriptor.HasAnyFlag(SpellDescriptor.Cold) && ed.EnergyType == DamageEnergyType.Cold) return true;
                if (descriptor.HasAnyFlag(SpellDescriptor.Electricity) && ed.EnergyType == DamageEnergyType.Electricity) return true;
            }
            catch { }
            return false;
        }

        [HarmonyPrefix]
        private static bool Prefix(DraconicBloodlineArcana __instance, RuleCalculateDamage evt)
        {
            try
            {
                if (!Main.Enabled || __instance == null || evt == null) return true; // keep vanilla
                var context = evt.Reason.Context;
                var ability = context?.SourceAbility;
                bool isSpell = ability != null && ability.IsSpell;
                // 通过描述符识别：呼吸武器（BreathWeapon）可视作法术来源处理
                bool isBreath = (context?.SpellDescriptor.HasAnyFlag(SpellDescriptor.BreathWeapon) ?? false);

                // 按元素 gating：要么描述符包含对应元素，要么（吐息）伤害包类型匹配
                bool elementMatch = (context != null && context.SpellDescriptor.HasAnyFlag(__instance.SpellDescriptor))
                                    || (isBreath && evt.DamageBundle.Any(d => DescriptorMatchesEnergy(__instance.SpellDescriptor, d)));
                if (!elementMatch) return true;

                // SpellsOnly：允许吐息例外
                if (__instance.SpellsOnly && !isSpell && !isBreath) return true;

                // 物理能力直接跳过（吐息除外）
                if (ability != null && ability.Type == AbilityType.Physical && !isBreath) return true;

                var prog = evt.Initiator?.Descriptor?.Progression;
                if (prog == null) return true;

                var sorc = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(SorcererClassGuid);
                var dd = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(DragonDiscipleClassGuid);
                int eff = 0;
                if (sorc != null) eff += prog.GetClassLevel(sorc);
                if (dd != null) eff += prog.GetClassLevel(dd);

                int tier = eff >= 15 ? 4 : eff >= 10 ? 3 : eff >= 5 ? 2 : 1;
                if (tier <= 1) return true; // 交给原版 +1 处理

                // 尝试定位自身 fact（作为修正来源）
                // 1) 属性或字段 Fact/Owner
                // 2) 在单位 facts 中反查包含该组件的 fact
                // 3) 兜底：按 GUID/名称猜测（下面的逻辑）
                EntityFact arcanaFact = GetOwningFactFromPropertyOrField(__instance);
                if (arcanaFact == null)
                {
                    arcanaFact = FindFactContainingComponent(__instance, evt.Initiator?.Descriptor);
                }

                if (arcanaFact == null)
                {
                    try
                    {
                        var facts = evt.Initiator?.Descriptor?.Facts?.List;
                        if (facts != null)
                        {
                            arcanaFact = facts.FirstOrDefault(f => f?.Blueprint != null && ArcanaGuids.Contains(f.Blueprint.AssetGuid));
                            if (arcanaFact == null)
                            {
                                foreach (var f in facts)
                                {
                                    var name = f?.Blueprint?.name;
                                    if (string.IsNullOrEmpty(name)) continue;
                                    var lower = name.ToLowerInvariant();
                                    if (lower.Contains("draconic") && lower.Contains("arcana")) { arcanaFact = f; break; }
                                }
                            }
                        }
                    }
                    catch { }
                }

                bool any = false;
                foreach (var dmg in evt.DamageBundle)
                {
                    if (dmg.Precision) continue;
                    var dice = dmg.Dice.ModifiedValue;
                    if (dice.Rolls <= 0) continue;
                    // 吐息需逐项判定元素匹配；否则遵循事件整体描述符
                    if (isBreath && !DescriptorMatchesEnergy(__instance.SpellDescriptor, dmg)) continue;

                    int basePerDie = 1;
                    if (__instance.UseContextBonus)
                    {
                        int ctx = 0; try { ctx = __instance.Value.Calculate(context); } catch { }
                        basePerDie = Math.Max(ctx, 1);
                    }
                    int perDie = Math.Max(basePerDie, tier);
                    int total = perDie * dice.Rolls;
                    if (total <= 0) continue;
                    dmg.AddModifier(total, arcanaFact);
                    any = true;
                }
                if (any)
                {
                    Main.Log($"[ArcanaScale] Applied scaled modifier via Prefix eff={eff} tier={tier} ability={ability?.NameSafe()} fact={(arcanaFact?.Blueprint?.name ?? "<null>")}");
                }
                return false; // 跳过原版（原版只 +1/骰）
            }
            catch (Exception ex)
            {
                Main.Log("[ArcanaScale] Prefix exception (falling back to vanilla): " + ex.Message);
                return true;
            }
        }
    }
}
