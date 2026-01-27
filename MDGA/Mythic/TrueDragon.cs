using System;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.ResourceLinks;
using BlueprintCore.Utils;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;
using MDGA.Loc;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using UnityEngine;
using BlueprintCore.Blueprints.Configurators.UnitLogic.ActivatableAbilities;
using BlueprintCore.Blueprints.Configurators.UnitLogic.Abilities;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Buffs;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Abilities;
using BlueprintCore.Blueprints.CustomConfigurators.Classes;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Mechanics.Actions;
using System.Reflection;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Buffs.Components;

namespace MDGA.Mythic
{
    internal static class TrueDragon
    {
        private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Buff component: +2 caster level for the owner when calculating ability params
        private class DraconicDignityCasterLevelBonus : UnitBuffComponentDelegate,
            IInitiatorRulebookHandler<RuleCalculateAbilityParams>
        {
            public void OnEventAboutToTrigger(RuleCalculateAbilityParams evt)
            {
                if (Owner != null && evt != null)
                {
                    evt.AddBonusCasterLevel(2);
                }
            }
            public void OnEventDidTrigger(RuleCalculateAbilityParams evt) { }
        }

        private static void ReplaceBuffInActionList(object actionListObj, BlueprintBuff newBuff)
        {
            if (actionListObj == null || newBuff == null) return;
            try
            {
                var type = actionListObj.GetType();
                var actionsField = type.GetField("Actions", BF);
                GameAction[] inner = null;
                if (actionsField != null)
                {
                    inner = actionsField.GetValue(actionListObj) as GameAction[];
                }
                else
                {
                    var actionsProp = type.GetProperty("Actions", BF);
                    if (actionsProp != null)
                        inner = actionsProp.GetValue(actionListObj, null) as GameAction[];
                }
                if (inner != null)
                    ReplaceBuffInActions(inner, newBuff);
            }
            catch { }
        }

        private static void ReplaceBuffInConditional(object conditionalObj, BlueprintBuff newBuff)
        {
            if (conditionalObj == null || newBuff == null) return;
            try
            {
                var type = conditionalObj.GetType();
                // Conditional has fields IfTrue/IfFalse of type ActionList
                var ifTrue = type.GetField("IfTrue", BF)?.GetValue(conditionalObj);
                var ifFalse = type.GetField("IfFalse", BF)?.GetValue(conditionalObj);
                ReplaceBuffInActionList(ifTrue, newBuff);
                ReplaceBuffInActionList(ifFalse, newBuff);
            }
            catch { }
        }
        private static void ReplaceBuffInActions(GameAction[] actions, BlueprintBuff newBuff)
        {
            if (actions == null || newBuff == null) return;
            for (int i = 0; i < actions.Length; i++)
            {
                var act = actions[i];
                if (act is ContextActionApplyBuff apply)
                {
                    apply.m_Buff = newBuff.ToReference<BlueprintBuffReference>();
                }
                else if (act is ContextActionRemoveBuff remove)
                {
                    remove.m_Buff = newBuff.ToReference<BlueprintBuffReference>();
                }
                else if (act != null && act.GetType().FullName == "Kingmaker.ElementsSystem.Conditional")
                {
                    ReplaceBuffInConditional(act, newBuff);
                }
            }
        }
        //“龙族威仪”
        //“真龙和30英尺半径内的同伴{g|Encyclopedia:Spell}施法{/g}时，{g|Encyclopedia:Caster_Level}施法者等级{/g}视为高2级。”
        //“A true dragon and all its companions in a 30-foot radius cast {g|Encyclopedia:Spell}spells{/g} as if their {g|Encyclopedia:Caster_Level}caster level{/g} was 2 levels higher.”
        // 预处理：检测 Dark Codex 是否已加载
        internal static bool IsDarkCodexLoaded()
        {
            try
            {
                // 方式一：通过类型解析（优先，最快）
                if (Type.GetType("DarkCodex.Mythic, DarkCodex", throwOnError: false) != null)
                    return true;

                // 方式二：遍历已加载程序集名称
                return AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Any(a => string.Equals(a.GetName().Name, "DarkCodex", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        // 仅当 DC 启用时应用额外效果（在 Main 初始化流程中调用此方法）
        internal static void ApplyIfDcEnabled(BlueprintFeature knownFeature = null)
        {
            if (!IsDarkCodexLoaded())
                return;

            // 在这里添加对 DC 真龙（True Dragon）的效果增补逻辑
            // 目标效果（参考巫妖黑暗仪式）：
            // 30英尺半径内的同伴施法时，施法者等级视为高2级。
            // 复用现有的 Dark Rites 切换能力（Guid: c511a005670280c44b6975a2a18a8a7b）并添加到 True Dragon 特性上。

            try
            {
                Debug.Log("[MDGA.TrueDragon] ApplyIfDcEnabled start.");
                var feature = knownFeature ?? FindFeatureByDisplayName("True Dragon");
                if (feature == null)
                {
                    Debug.LogWarning("[MDGA.TrueDragon] True Dragon feature not found.");
                    return;
                }
                // 组装完整的切换→光环→范围→效果buff链路
                // 不修改 True Dragon 特性本身的名称、描述或图标，只新增克隆的切换能力

                // 将 GUID 提前定义，便于后续诊断日志使用
                var originalBuffGuid = "793b138567d79624b97e78969d239307"; // Dark Rites area buff
                var newBuffGuid = "c3e6d1aa7bc040dca2c0a7b9e3e5d102"; // Draconic Dignity area buff
                var originalAreaEffectGuid = "2dce35c38b3c01041aff62e9d395af76"; // Dark Rites area effect
                var originalEffectBuffGuid = "c8a852e1ca98b364198d28de555b6788"; // Dark Rites effect buff applied in area
                var newEffectBuffGuid = "9f2b5b2d9a9a4b2dbb6d5c1b6a1c0d01"; // Draconic Dignity effect buff
                var newAreaEffectGuid = "a0b1c2d3e4f50617a8b9c0d1e2f3a4b5"; // Draconic Dignity area effect
                var originalFeatureGuid = "9703d79082dc75e4aaaa4387b9c95229"; // Dark Rites feature
                var newFeatureGuid = "d2a31af5bd02f9a4a87d6015fdba56bb"; // Draconic Dignity feature

                // 准备独立的切换能力：使用新的唯一 GUID（避免与已有 Feature GUID 冲突）
                var newToggleGuid = "b9d1a4f3a1c64f7d9a0b1c2d3e4f5678"; // New unique GUID for cloned activatable ability
                var newToggle = ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(newToggleGuid);
                if (newToggle == null)
                {
                    var original = ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>("c511a005670280c44b6975a2a18a8a7b");
                    if (original == null)
                    {
                        Debug.LogWarning("[MDGA.TrueDragon] Original Dark Rites toggle not found.");
                        return;
                    }

                    var opIconSource = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>("41cf93453b027b94886901dbfc680cb9");
                    var opBuffIconSource = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>("86a663442ca18284986e73153d51e2a6");
                    var icon = opBuffIconSource?.Icon ?? opIconSource?.Icon ?? original.Icon;

                    // 克隆 Buff（叠加），保留原 AreaEffect 引用以避免缺失配置器类型导致编译错误

                    BlueprintBuff newBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(newBuffGuid);
                    if (newBuff == null)
                    {
                        Debug.Log("[MDGA.TrueDragon] Creating new area aura buff clone.");
                        // 先克隆范围内实际应用的效果 Buff（用于显示到单位状态栏并支持叠加）
                        var newEffectBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(newEffectBuffGuid);
                        if (newEffectBuff == null)
                        {
                            Debug.Log("[MDGA.TrueDragon] Creating new effect buff clone.");
                            newEffectBuff = BuffConfigurator.New("MDGA_DragonDignityEffectBuff", newEffectBuffGuid)
                                .CopyFrom(originalEffectBuffGuid)
                                .SetStacking(Kingmaker.UnitLogic.Buffs.Blueprints.StackingType.Stack)
                                .SetIcon(icon)
                                .Configure();
                            Debug.Log("[MDGA.TrueDragon] Effect buff configured: " + (newEffectBuff != null));
                            // 使正式效果 buff 生效：挂载+2施法者等级组件
                            try
                            {
                                var effBp = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(newEffectBuffGuid);
                                if (effBp != null)
                                {
                                    var list = (effBp.ComponentsArray ?? Array.Empty<BlueprintComponent>()).ToList();
                                    bool hasBonus = list.OfType<DraconicDignityCasterLevelBonus>().Any();
                                    if (!hasBonus)
                                    {
                                        list.Add(new DraconicDignityCasterLevelBonus());
                                        effBp.ComponentsArray = list.ToArray();
                                        Debug.Log("[MDGA.TrueDragon] Attached DraconicDignityCasterLevelBonus to effect buff.");
                                    }
                                }
                            }
                            catch { }
                            // 绑定名称与描述与切换一致
                            try
                            {
                                var isZhBuff = IsChineseLocale();
                                var dispBuffZh = "龙族威仪";
                                var descBuffZh = "真龙和30英尺半径内的同伴施法时，施法者等级视为高2级。";
                                var dispBuffEn = "Draconic Dignity";
                                var descBuffEn = "In 30-foot radius, allies cast as if caster level was 2 levels higher.";
                                var nameBuff = isZhBuff ? dispBuffZh : dispBuffEn;
                                var descBuff = isZhBuff ? descBuffZh : descBuffEn;
                                MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.EffectBuff.Name", nameBuff);
                                MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.EffectBuff.Desc", descBuff);
                                LocalizationInjector.BindKeyAndText(newEffectBuff, "m_DisplayName", "MDGA.TrueDragon.EffectBuff.Name", nameBuff);
                                LocalizationInjector.BindKeyAndText(newEffectBuff, "m_Description", "MDGA.TrueDragon.EffectBuff.Desc", descBuff);
                            }
                            catch { }
                        }

                        // 克隆 AreaEffect，并改为应用新的效果 Buff
                        var newAreaEffect = ResourcesLibrary.TryGetBlueprint<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbilityAreaEffect>(newAreaEffectGuid);
                        if (newAreaEffect == null)
                        {
                            Debug.Log("[MDGA.TrueDragon] Creating new area effect clone.");
                            newAreaEffect = AbilityAreaEffectConfigurator.New("MDGA_DragonDignityAreaEffect", newAreaEffectGuid)
                                .CopyFrom(originalAreaEffectGuid)
                                .Configure();
                            Debug.Log("[MDGA.TrueDragon] AreaEffect configured: " + (newAreaEffect != null));
                            try
                            {
                                // 直接替换运行时组件里的效果 Buff 引用
                                var cfgArea = ResourcesLibrary.TryGetBlueprint<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbilityAreaEffect>(newAreaEffectGuid);
                                if (cfgArea != null)
                                {
                                    Debug.Log("[MDGA.TrueDragon] Loaded configured AreaEffect to modify components.");
                                    // 最小化：直接用 AbilityAreaEffectBuff 组件管理效果 buff 的进入/离开应用
                                    try
                                    {
                                        var effRef = newEffectBuff.ToReference<BlueprintBuffReference>();
                                        var areaComps = (cfgArea.ComponentsArray ?? Array.Empty<BlueprintComponent>()).ToList();
                                        // 移除可能存在的旧 AbilityAreaEffectBuff 指向旧 buff
                                        areaComps = areaComps.Where(c => !(c is Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectBuff aab && aab.Buff != null && aab.Buff.AssetGuid != newEffectBuff.AssetGuid)).ToList();
                                        var areaBuffComp = new Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectBuff
                                        {
                                            Condition = new Kingmaker.ElementsSystem.ConditionsChecker { Conditions = Array.Empty<Kingmaker.ElementsSystem.Condition>() },
                                            CheckConditionEveryRound = false,
                                            // set private m_Buff via reference field
                                            // m_Buff is a serialized field; assign through property if available
                                        };
                                        // 通过反射设置 m_Buff 引用
                                        var mbuffField = typeof(Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectBuff).GetField("m_Buff", BF);
                                        if (mbuffField != null)
                                            mbuffField.SetValue(areaBuffComp, effRef);
                                        areaComps.Add(areaBuffComp);
                                        cfgArea.ComponentsArray = areaComps.ToArray();
                                        // 目标过滤与半径（仅在字段存在时设置）
                                        try
                                        {
                                            var affectEnemiesField = typeof(Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbilityAreaEffect).GetField("AffectEnemies", BF);
                                            var canBeUsedInTacticalCombatField = typeof(Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbilityAreaEffect).GetField("CanBeUsedInTacticalCombat", BF);
                                            var sizeField = typeof(Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbilityAreaEffect).GetField("Size", BF);
                                            if (affectEnemiesField != null) affectEnemiesField.SetValue(cfgArea, false);
                                            if (canBeUsedInTacticalCombatField != null) canBeUsedInTacticalCombatField.SetValue(cfgArea, false);
                                            if (sizeField != null)
                                            {
                                                // 30 feet radius
                                                sizeField.SetValue(cfgArea, new Kingmaker.Utility.Feet(30f));
                                            }
                                        }
                                        catch { }
                                        Debug.Log("[MDGA.TrueDragon] Added AbilityAreaEffectBuff to apply new effect buff inside area.");
                                    }
                                    catch { }
                                    var run = cfgArea.ComponentsArray?.OfType<Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectRunAction>().FirstOrDefault();
                                    if (run != null)
                                    {
                                        Debug.Log("[MDGA.TrueDragon] Found AbilityAreaEffectRunAction. Updating actions buff refs.");
                                        ReplaceBuffInActions(run.UnitEnter?.Actions, newEffectBuff);
                                        ReplaceBuffInActions(run.UnitExit?.Actions, newEffectBuff);
                                        Debug.Log("[MDGA.TrueDragon] Replaced buff refs in UnitEnter/Exit actions (with recursion).");
                                    }
                                    else
                                    {
                                        // 组件缺失时，添加一个新的 RunAction，将进入/离开时应用/移除效果 Buff
                                        var effRef = newEffectBuff.ToReference<BlueprintBuffReference>();
                                        var apply = new ContextActionApplyBuff
                                        {
                                            m_Buff = effRef,
                                            AsChild = true
                                        };
                                        var remove = new ContextActionRemoveBuff
                                        {
                                            m_Buff = effRef
                                        };
                                        var newRun = new Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectRunAction
                                        {
                                            UnitEnter = new Kingmaker.ElementsSystem.ActionList { Actions = new GameAction[] { apply } },
                                            UnitExit = new Kingmaker.ElementsSystem.ActionList { Actions = new GameAction[] { remove } }
                                        };
                                        var areaComps = cfgArea.ComponentsArray.ToList();
                                        areaComps.Add(newRun);
                                        cfgArea.ComponentsArray = areaComps.ToArray();
                                        Debug.Log("[MDGA.TrueDragon] AbilityAreaEffectRunAction was missing; added with apply/remove effect buff actions.");
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning("[MDGA.TrueDragon] Failed to load configured AreaEffect for modification.");
                                }
                            }
                            catch { }
                        }

                        // 克隆范围 Aura Buff，并确保其引用新的 AreaEffect
                        newBuff = BuffConfigurator.New("MDGA_DragonDignityAreaBuff", newBuffGuid)
                            .CopyFrom(originalBuffGuid)
                            .SetStacking(Kingmaker.UnitLogic.Buffs.Blueprints.StackingType.Stack)
                            .SetIcon(icon)
                            .Configure();
                        Debug.Log("[MDGA.TrueDragon] Aura buff configured: " + (newBuff != null));
                        try
                        {
                            var cfgBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(newBuffGuid);
                            if (cfgBuff != null)
                            {
                                Debug.Log("[MDGA.TrueDragon] Loaded configured aura buff. Updating AddAreaEffect.");
                                bool foundAddArea = false;
                                foreach (var comp in cfgBuff.ComponentsArray)
                                {
                                    var addArea = comp as Kingmaker.UnitLogic.Buffs.Components.AddAreaEffect;
                                    if (addArea != null)
                                    {
                                        foundAddArea = true;
                                        addArea.m_AreaEffect = ResourcesLibrary.TryGetBlueprint<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbilityAreaEffect>(newAreaEffectGuid)
                                            .ToReference<BlueprintAbilityAreaEffectReference>();
                                        Debug.Log("[MDGA.TrueDragon] AddAreaEffect.m_AreaEffect updated.");
                                    }
                                }
                                if (!foundAddArea)
                                {
                                    var areaRef = ResourcesLibrary.TryGetBlueprint<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbilityAreaEffect>(newAreaEffectGuid)
                                        ?.ToReference<BlueprintAbilityAreaEffectReference>();
                                    if (areaRef != null)
                                    {
                                        var newAddArea = new Kingmaker.UnitLogic.Buffs.Components.AddAreaEffect
                                        {
                                            m_AreaEffect = areaRef
                                        };
                                        var list = cfgBuff.ComponentsArray.ToList();
                                        list.Add(newAddArea);
                                        cfgBuff.ComponentsArray = list.ToArray();
                                        Debug.Log("[MDGA.TrueDragon] AddAreaEffect component was missing on aura buff; added and bound to new area effect.");
                                    }
                                    else
                                    {
                                        Debug.LogWarning("[MDGA.TrueDragon] Failed to resolve new area effect reference to add AddAreaEffect component.");
                                    }
                                }
                            }
                            else
                            {
                                Debug.LogWarning("[MDGA.TrueDragon] Failed to load aura buff after Configure.");
                            }
                        }
                        catch { }
                    }

                    // 克隆 Dark Rites Feature 为“龙族威仪”特性（不挂载到任何位置，仅作为完整组件集的一部分）
                    try
                    {
                        var ddFeature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(newFeatureGuid);
                        if (ddFeature == null)
                        {
                            ddFeature = FeatureConfigurator.New("MDGA_DragonDignityFeature", newFeatureGuid)
                                .CopyFrom(originalFeatureGuid)
                                .Configure();
                            Debug.Log("[MDGA.TrueDragon] Cloned Dark Rites Feature into Draconic Dignity Feature: " + (ddFeature != null));
                            // 绑定显示名与描述（与切换一致）
                            try
                            {
                                bool isZhFeat = IsChineseLocale();
                                string dispZhFeat = "龙族威仪";
                                string descZhFeat = "真龙和30英尺半径内的同伴{g|Encyclopedia:Spell}施法{/g}时，{g|Encyclopedia:Caster_Level}施法者等级{/g}视为高2级。";
                                string dispEnFeat = "Draconic Dignity";
                                string descEnFeat = "A true dragon and all its companions in a 30-foot radius cast {g|Encyclopedia:Spell}spells{/g} as if their {g|Encyclopedia:Caster_Level}caster level{/g} was 2 levels higher.";
                                var nameFeat = isZhFeat ? dispZhFeat : dispEnFeat;
                                var descFeat = isZhFeat ? descZhFeat : descEnFeat;
                                MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.Feature.Name", nameFeat);
                                MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.Feature.Desc", descFeat);
                                LocalizationInjector.BindKeyAndText(ddFeature, "m_DisplayName", "MDGA.TrueDragon.Feature.Name", nameFeat);
                                LocalizationInjector.BindKeyAndText(ddFeature, "m_Description", "MDGA.TrueDragon.Feature.Desc", descFeat);
                            }
                            catch { }

                            // 替换组件中的 AddFacts，使其只引用我们新的 MDGA_DragonDignityToggle
                            try
                            {
                                var compsFeat = (ddFeature.ComponentsArray ?? Array.Empty<BlueprintComponent>()).ToList();
                                for (int i = 0; i < compsFeat.Count; i++)
                                {
                                    if (compsFeat[i] is AddFacts af)
                                    {
                                        var facts = af.m_Facts ?? Array.Empty<BlueprintUnitFactReference>();
                                        // 过滤掉旧黑暗仪式 toggle 的引用，替换为新 toggle
                                        var oldToggleGuid = BlueprintGuid.Parse("c511a005670280c44b6975a2a18a8a7b");
                                        var list = facts.Where(f => f != null && f.Guid != oldToggleGuid).ToList();
                                        var newToggleBp = ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(newToggleGuid);
                                        if (newToggleBp != null)
                                        {
                                            list.Add(newToggleBp.ToReference<BlueprintUnitFactReference>());
                                        }
                                        af.m_Facts = list.ToArray();
                                    }
                                }
                                ddFeature.ComponentsArray = compsFeat.ToArray();
                                Debug.Log("[MDGA.TrueDragon] Updated Draconic Dignity Feature AddFacts to point to new toggle.");
                            }
                            catch { }
                        }
                    }
                    catch { }

                    // 使用 BlueprintCore 复制原始能力的大部分设置（通过 GUID 字符串传入原始蓝图），但绑定到新 Buff
                    newToggle = ActivatableAbilityConfigurator.New("MDGA_DragonDignityToggle", newToggleGuid)
                        .CopyFrom("c511a005670280c44b6975a2a18a8a7b")
                        .SetBuff(newBuff.ToReference<BlueprintBuffReference>())
                        .SetIcon(icon)
                        .SetIsOnByDefault(true)
                        .SetDeactivateIfCombatEnded(false)
                        .SetDeactivateIfOwnerDisabled(false)
                        .SetOnlyInCombat(false)
                        .SetHiddenInUI(false)
                        .Configure();
                    Debug.Log("[MDGA.TrueDragon] Toggle configured: " + (newToggle != null));

                    // 绑定名称与描述
                    try
                    {
                        bool isZhNew = IsChineseLocale();
                        string dispZhNew = "龙族威仪";
                        string descZhNew = "真龙和30英尺半径内的同伴{g|Encyclopedia:Spell}施法{/g}时，{g|Encyclopedia:Caster_Level}施法者等级{/g}视为高2级。";
                        string dispEnNew = "Draconic Dignity";
                        string descEnNew = "A true dragon and all its companions in a 30-foot radius cast {g|Encyclopedia:Spell}spells{/g} as if their {g|Encyclopedia:Caster_Level}caster level{/g} was 2 levels higher.";
                        var nameTextNew = isZhNew ? dispZhNew : dispEnNew;
                        var descTextNew = isZhNew ? descZhNew : descEnNew;
                        // 先注册动态键以避免 Unknown Key
                        MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.Toggle.Name", nameTextNew);
                        MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.Toggle.Desc", descTextNew);
                        LocalizationInjector.BindKeyAndText(newToggle, "m_DisplayName", "MDGA.TrueDragon.Toggle.Name", nameTextNew);
                        LocalizationInjector.BindKeyAndText(newToggle, "m_Description", "MDGA.TrueDragon.Toggle.Desc", descTextNew);
                    }
                    catch { }
                }

                // 本地化与图标覆盖
                try
                {
                    bool isZh2 = IsChineseLocale();
                    string dispZh2 = "龙族威仪";
                    string descZh2 = "真龙和30英尺半径内的同伴{g|Encyclopedia:Spell}施法{/g}时，{g|Encyclopedia:Caster_Level}施法者等级{/g}视为高2级。";
                    string dispEn2 = "Draconic Dignity";
                    string descEn2 = "A true dragon and all its companions in a 30-foot radius cast {g|Encyclopedia:Spell}spells{/g} as if their {g|Encyclopedia:Caster_Level}caster level{/g} was 2 levels higher.";
                    var nameText2 = isZh2 ? dispZh2 : dispEn2;
                    var descText2 = isZh2 ? descZh2 : descEn2;
                    // 再次确保动态键已注册（幂等）
                    MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.Toggle.Name", nameText2);
                    MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.Toggle.Desc", descText2);
                    LocalizationInjector.BindKeyAndText(newToggle, "m_DisplayName", "MDGA.TrueDragon.Toggle.Name", nameText2);
                    LocalizationInjector.BindKeyAndText(newToggle, "m_Description", "MDGA.TrueDragon.Toggle.Desc", descText2);
                    // 图标使用“神威如岳”（Overwhelming Presence）
                    var op2 = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>("41cf93453b027b94886901dbfc680cb9");
                    if (op2 != null && op2.Icon != null)
                        newToggle.m_Icon = op2.Icon;
                    // 为新 Buff 绑定名称与描述（显示在状态栏）
                    try
                    {
                        var isZhBuff = IsChineseLocale();
                        var dispBuffZh = "龙族威仪";
                        var descBuffZh = "真龙和30英尺半径内的同伴施法时，施法者等级视为高2级。";
                        var dispBuffEn = "Draconic Dignity";
                        var descBuffEn = "In 30-foot radius, allies cast as if caster level was 2 levels higher.";
                        var nameBuff = isZhBuff ? dispBuffZh : dispBuffEn;
                        var descBuff = isZhBuff ? descBuffZh : descBuffEn;
                        MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.AuraBuff.Name", nameBuff);
                        MDGA.Loc.LocalizationInjector.RegisterDynamicKey("MDGA.TrueDragon.AuraBuff.Desc", descBuff);
                        var dignityBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>("c3e6d1aa7bc040dca2c0a7b9e3e5d102");
                        if (dignityBuff != null)
                        {
                            LocalizationInjector.BindKeyAndText(dignityBuff, "m_DisplayName", "MDGA.TrueDragon.AuraBuff.Name", nameBuff);
                            LocalizationInjector.BindKeyAndText(dignityBuff, "m_Description", "MDGA.TrueDragon.AuraBuff.Desc", descBuff);
                        }
                    }
                    catch { }
                }
                catch { }

                // 若已有旧的黑暗仪式引用，替换为新的切换能力；否则添加新的
                var oldGuid = BlueprintGuid.Parse("c511a005670280c44b6975a2a18a8a7b");
                var comps = (feature.ComponentsArray ?? Array.Empty<BlueprintComponent>()).ToList();
                bool alreadyHasNew = comps.OfType<AddFacts>().Any(af => af.m_Facts != null && af.m_Facts.Any(f => f != null && f.Guid == newToggle.AssetGuid));
                if (!alreadyHasNew)
                {
                    // 先添加新的切换能力
                    var add = new AddFacts()
                    {
                        m_Facts = new BlueprintUnitFactReference[] { newToggle.ToReference<BlueprintUnitFactReference>() }
                    };
                    comps.Add(add);
                    Debug.Log("[MDGA.TrueDragon] Added new toggle to feature AddFacts.");

                    // 再安全移除旧的黑暗仪式引用
                    comps = comps.Where(comp =>
                    {
                        var af = comp as AddFacts;
                        if (af == null) return true;
                        if (af.m_Facts != null && af.m_Facts.Any(f => f != null && f.Guid == oldGuid)) return false;
                        return true;
                    }).ToList();
                    Debug.Log("[MDGA.TrueDragon] Removed old Dark Rites AddFacts if present.");
                }
                else
                {
                    Debug.Log("[MDGA.TrueDragon] Feature already has new toggle; skipping AddFacts add.");
                }

                feature.ComponentsArray = comps.ToArray();
                try
                {
                    // 诊断：打印 aura buff 的 AddAreaEffect 指向、AreaEffect 的 RunAction Buff 引用
                    try
                    {
                        var dbgBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(newBuffGuid);
                        var dbgArea = ResourcesLibrary.TryGetBlueprint<BlueprintAbilityAreaEffect>(newAreaEffectGuid);
                        var dbgEffBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(newEffectBuffGuid);
                        var origArea = ResourcesLibrary.TryGetBlueprint<BlueprintAbilityAreaEffect>("2dce35c38b3c01041aff62e9d395af76");
                        var origEffBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>("c8a852e1ca98b364198d28de555b6788");
                        if (dbgBuff != null)
                        {
                            var addArea = dbgBuff.ComponentsArray?.OfType<Kingmaker.UnitLogic.Buffs.Components.AddAreaEffect>().FirstOrDefault();
                            Debug.Log($"[MDGA.TrueDragon][Diag] AuraBuff has AddAreaEffect: {(addArea != null)} AreaRef={(addArea?.m_AreaEffect.Guid.ToString() ?? "null")}");
                        }
                        if (dbgArea != null)
                        {
                            var run = dbgArea.ComponentsArray?.OfType<Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectRunAction>().FirstOrDefault();
                            BlueprintGuid enterBuff = default, exitBuff = default;
                            var enter = run?.UnitEnter?.Actions ?? Array.Empty<GameAction>();
                            foreach (var a in enter)
                            {
                                if (a is ContextActionApplyBuff ap && ap.m_Buff != null) { enterBuff = ap.m_Buff.Guid; break; }
                            }
                            var exit = run?.UnitExit?.Actions ?? Array.Empty<GameAction>();
                            foreach (var a in exit)
                            {
                                if (a is ContextActionRemoveBuff rm && rm.m_Buff != null) { exitBuff = rm.m_Buff.Guid; break; }
                            }
                            bool hasEnterBuff = !enterBuff.Equals(default(BlueprintGuid));
                            bool hasExitBuff = !exitBuff.Equals(default(BlueprintGuid));
                            Debug.Log($"[MDGA.TrueDragon][Diag] AreaEffect RunAction present={(run!=null)} UnitEnter.ApplyBuff={(hasEnterBuff? enterBuff.ToString() : "null")} UnitExit.RemoveBuff={(hasExitBuff? exitBuff.ToString() : "null")} expectedEffectBuff={(dbgEffBuff?.AssetGuid.ToString() ?? "null")}");
                            // dump component types
                            var areaTypes = string.Join(", ", (dbgArea.ComponentsArray ?? Array.Empty<BlueprintComponent>()).Select(c => c?.GetType().FullName));
                            Debug.Log($"[MDGA.TrueDragon][Diag] New AreaEffect components: {areaTypes}");
                        }
                        if (origArea != null)
                        {
                            var origAreaTypes = string.Join(", ", (origArea.ComponentsArray ?? Array.Empty<BlueprintComponent>()).Select(c => c?.GetType().FullName));
                            Debug.Log($"[MDGA.TrueDragon][Diag] Original DarkRites AreaEffect components: {origAreaTypes}");
                        }
                        if (dbgEffBuff != null)
                        {
                            var effTypes = string.Join(", ", (dbgEffBuff.ComponentsArray ?? Array.Empty<BlueprintComponent>()).Select(c => c?.GetType().FullName));
                            Debug.Log($"[MDGA.TrueDragon][Diag] New EffectBuff components: {effTypes}");
                        }
                        if (origEffBuff != null)
                        {
                            var origEffTypes = string.Join(", ", (origEffBuff.ComponentsArray ?? Array.Empty<BlueprintComponent>()).Select(c => c?.GetType().FullName));
                            Debug.Log($"[MDGA.TrueDragon][Diag] Original DarkRites EffectBuff components: {origEffTypes}");
                        }
                    }
                    catch { }

                    // 去重：确保特性上只保留一个“龙族威仪”切换
                    try
                    {
                        var lst = feature.ComponentsArray.ToList();
                        for (int i = 0; i < lst.Count; i++)
                        {
                            if (lst[i] is AddFacts af && af.m_Facts != null)
                            {
                                var facts = af.m_Facts.ToList();
                                // 移除重复的新切换引用，只保留一个
                                var seen = new HashSet<BlueprintGuid>();
                                var dedup = new List<BlueprintUnitFactReference>();
                                foreach (var f in facts)
                                {
                                    if (f == null) continue;
                                    var g = f.Guid;
                                    if (g == newToggle.AssetGuid)
                                    {
                                        if (!seen.Contains(g)) { dedup.Add(f); seen.Add(g); }
                                        // 跳过重复
                                        continue;
                                    }
                                    if (!seen.Contains(g)) { dedup.Add(f); seen.Add(g); }
                                }
                                af.m_Facts = dedup.ToArray();
                            }
                        }
                        feature.ComponentsArray = lst.ToArray();
                    }
                    catch { }

                    Debug.Log($"[MDGA.TrueDragon] Chain GUIDs: toggle={newToggleGuid} aura={newBuffGuid} area={newAreaEffectGuid} effect={newEffectBuffGuid}");
                    var afFacts = feature.ComponentsArray.OfType<AddFacts>().SelectMany(af => af.m_Facts ?? Array.Empty<BlueprintUnitFactReference>()).Select(r => r?.Guid.ToString()).ToArray();
                    Debug.Log("[MDGA.TrueDragon] Feature AddFacts contains GUIDs: " + string.Join(", ", afFacts));
                    Debug.Log($"[MDGA.TrueDragon] Toggle props: BuffGuid={newToggle?.m_Buff?.Guid} HiddenInUI={newToggle?.HiddenInUI} OnlyInCombat={newToggle?.OnlyInCombat} IsOnByDefault={newToggle?.IsOnByDefault} DeactivateIfCombatEnded={newToggle?.DeactivateIfCombatEnded}");
                }
                catch { }
                Debug.Log("[MDGA.TrueDragon] ApplyIfDcEnabled completed successfully.");
            }
            catch
            {
                // 忽略运行时错误，避免影响其他流程
                Debug.LogWarning("[MDGA.TrueDragon] ApplyIfDcEnabled encountered an exception, suppressed.");
            }
        }

        private static bool IsChineseLocale()
        {
            try
            {
                var loc = LocalizationManager.CurrentLocale;
                if (loc != null)
                {
                    string locStr = loc.ToString();
                    if (!string.IsNullOrEmpty(locStr) && locStr.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var langProp = loc.GetType().GetProperty("Language", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    var langObj = langProp?.GetValue(loc, null);
                    if (langObj != null && langObj.ToString().ToLower().StartsWith("zh")) return true;
                }
            }
            catch { }
            try
            {
                return UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.ChineseSimplified
                    || UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.Chinese
                    || UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.ChineseTraditional;
            }
            catch { }
            return false;
        }

        internal static BlueprintFeature FindFeatureByDisplayName(string displayName)
        {
            try
            {
                // 直接使用 DC 已知 GUID（LimitlessBloodlineClaws → True Dragon）
                // GUID 源自 DarkCodex Settings/blueprints.txt
                var dcTrueDragon = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>("2342307641fd42dcabd1441cc2fae0d9");
                if (dcTrueDragon != null)
                {
                    Debug.Log("[MDGA.TrueDragon] Found True Dragon by known GUID.");
                    return dcTrueDragon;
                }
            }
            catch { }

            // 兜底：从神话能力选择集中按内部名/显示名查找
            try
            {
                var mythicAbilitySel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>("ba0e5a900b775be4a99702f1ed08914d"); // MythicAbilitySelection
                if (mythicAbilitySel != null && mythicAbilitySel.m_AllFeatures != null)
                {
                    foreach (var factRef in mythicAbilitySel.m_AllFeatures)
                    {
                        var f = factRef?.Get() as BlueprintFeature;
                        if (f == null) continue;
                        var internalName = f.name ?? string.Empty;
                        if (string.Equals(internalName, "LimitlessBloodlineClaws", StringComparison.Ordinal))
                        {
                            Debug.Log($"[MDGA.TrueDragon] Found by internal name. GUID={f.AssetGuid}");
                            return f;
                        }
                        // 备用：显示名包含 True Dragon（在英文环境下有效）
                        try
                        {
                            var disp = f.m_DisplayName?.ToString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(disp) && disp.IndexOf(displayName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Debug.Log($"[MDGA.TrueDragon] Found by display text match. name={internalName} GUID={f.AssetGuid}");
                                return f;
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    Debug.LogWarning("[MDGA.TrueDragon] MythicAbilitySelection not found or empty when searching for True Dragon.");
                }
            }
            catch { }

            // 兜底2：使用 CodexLib 提供的 BpCache 反射遍历全部 BlueprintFeature
            try
            {
                var codexType = System.Type.GetType("CodexLib.BpCache, CodexLib", throwOnError: false);
                if (codexType != null)
                {
                    var getMethod = codexType.GetMethods(BF).FirstOrDefault(m => m.IsGenericMethodDefinition && m.Name == "Get" && m.GetParameters().Length == 0);
                    if (getMethod != null)
                    {
                        var gm = getMethod.MakeGenericMethod(typeof(BlueprintFeature));
                        var enumerable = gm.Invoke(null, null) as System.Collections.IEnumerable;
                        if (enumerable != null)
                        {
                            foreach (var obj in enumerable)
                            {
                                var f = obj as BlueprintFeature; if (f == null) continue;
                                var internalName = f.name ?? string.Empty;
                                if (string.Equals(internalName, "LimitlessBloodlineClaws", StringComparison.Ordinal))
                                {
                                    Debug.Log($"[MDGA.TrueDragon] Found via CodexLib.BpCache internal name. GUID={f.AssetGuid}");
                                    return f;
                                }
                                try
                                {
                                    var disp = f.m_DisplayName?.ToString() ?? string.Empty;
                                    if (!string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(disp) && disp.IndexOf(displayName, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        Debug.Log($"[MDGA.TrueDragon] Found via CodexLib.BpCache display: {internalName} GUID={f.AssetGuid}");
                                        return f;
                                    }
                                    // 额外匹配：FeatureGroup 包含 MythicAbility
                                    var groups = f.Groups ?? Array.Empty<FeatureGroup>();
                                    if (groups.Contains(FeatureGroup.MythicAbility) && internalName.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        Debug.Log($"[MDGA.TrueDragon] Found via CodexLib.BpCache groups/internalName heuristic. GUID={f.AssetGuid}");
                                        return f;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            // 最后兜底：按候选内部名 TryGetBlueprint
            try
            {
                // ResourcesLibrary 不提供直接枚举，这里通过 BlueprintsCache.ForEachLoaded 需要反射访问，避免复杂性，用常见候选名再尝试一次
                string[] candidateNames = new[] { "TrueDragon", "True_Dragon", "LimitlessBloodlineClaws" };
                foreach (var cand in candidateNames)
                {
                    try
                    {
                        var byName = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(cand);
                        if (byName != null)
                        {
                            Debug.Log($"[MDGA.TrueDragon] Found by candidate name {cand}. GUID={byName.AssetGuid}");
                            return byName;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            Debug.LogWarning("[MDGA.TrueDragon] True Dragon feature still not found after GUID and selection scan.");
            return null;
        }

        private static BlueprintActivatableAbility FindActivatableAbilityByGuid(string guid)
        {
            try
            {
                var ab = ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(guid);
                return ab;
            }
            catch { return null; }
        }
    }
}
