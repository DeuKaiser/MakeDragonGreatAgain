using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Spells; // 用于 ApplySpellbook 与相关类型
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem; // 恢复 BlueprintsCache 引用
using Kingmaker.Designers.Mechanics.Facts; // 用于 SkipLevelsForSpellProgression
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp;
using Kingmaker.UnitLogic.Class.LevelUp.Actions; // ApplySpellbook
using Kingmaker.UnitLogic.FactLogic; // AddStatBonus
using MDGA.Loc;
using UnityEngine;

namespace MDGA.GeneralClasses.DragonDisciple
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class NormalizeDragonDiscipleClass
    {
        private const string DragonDiscipleClassGuid = "72051275b1dbb2d42ba9118237794f7c";
        private const string DragonDiscipleProgressionGuid = "69fc2bad2eb331346a6c777423e0d0f7";
        private const string HighBABProgressionGuid = "b3057560ffff3514299e8b93e7648a9d"; // 来自武僧/战士（全 BAB）
        private static bool _done;

        // 现有的属性加成特性 GUID
        internal static readonly BlueprintGuid DD_IntelligenceFeatureGuid = BlueprintGuid.Parse("6ad7e3699a6a9ad47a25b3464a305627");
        internal static readonly BlueprintGuid DD_StrengthFeatureGuid = BlueprintGuid.Parse("270259f59c6ae6040babd5797feef2e2"); // 用作稳健模板（UI 验证）
        // 新的感知 / 魅力特性 GUID
        internal static readonly BlueprintGuid DD_WisdomFeatureGuid = BlueprintGuid.Parse("b0d0f8a5c2a8495d9a1ebc6d4eaa0001");
        internal static readonly BlueprintGuid DD_CharismaFeatureGuid = BlueprintGuid.Parse("c1d0f8a5c2a8495d9a1ebc6d4eaa0002");
        // 新的分级属性加成特性 GUID（区分以便逐级描述本地化）
        internal static readonly BlueprintGuid DD_WisdomFeatureGuid_L3 = BlueprintGuid.Parse("b0d0f8a5c2a8495d9a1ebc6d4eaa0001"); // 3 级保持原映射
        internal static readonly BlueprintGuid DD_WisdomFeatureGuid_L6 = BlueprintGuid.Parse("b0d0f8a5c2a8495d9a1ebc6d4eaa1006");
        internal static readonly BlueprintGuid DD_WisdomFeatureGuid_L8 = BlueprintGuid.Parse("b0d0f8a5c2a8495d9a1ebc6d4eaa1008");
        internal static readonly BlueprintGuid DD_CharismaFeatureGuid_L3 = BlueprintGuid.Parse("c1d0f8a5c2a8495d9a1ebc6d4eaa0002"); // 保持原映射
        internal static readonly BlueprintGuid DD_CharismaFeatureGuid_L6 = BlueprintGuid.Parse("c1d0f8a5c2a8495d9a1ebc6d4eaa2006");
        internal static readonly BlueprintGuid DD_CharismaFeatureGuid_L8 = BlueprintGuid.Parse("c1d0f8a5c2a8495d9a1ebc6d4eaa2008");
        internal static readonly BlueprintGuid DD_IntelligenceFeatureGuid_L3 = BlueprintGuid.Parse("6ad7e3699a6a9ad47a25b3464a3056a3");
        internal static readonly BlueprintGuid DD_IntelligenceFeatureGuid_L6 = BlueprintGuid.Parse("6ad7e3699a6a9ad47a25b3464a3056a6");

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            if (Main.Settings == null || !Main.Settings.EnableDragonDiscipleFix)
            {
                Main.Log("[DD ProgFix] Disabled via settings.");
                return;
            }
            try
            {
                // 扩展诊断阶段（仅在 VerboseLogging 下输出结构）
                if (Main.Settings.VerboseLogging)
                {
                    try { DD_FeatureHelpers.DumpBlueprintFieldLayouts(); } catch { }
                    try { DD_FeatureHelpers.DumpCacheExtended(); } catch { }
                }

                var ddClass = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(DragonDiscipleClassGuid);
                var progression = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(DragonDiscipleProgressionGuid);
                if (ddClass == null || progression == null)
                {
                    Main.Log("[DD ProgFix] Class or progression not found");
                    return;
                }
                // 调整 SkipLevels（移除 1/5/9 级阻断）
                var targetLevels = new[] { 1, 5, 9 };
                var skip = ddClass.GetComponents<SkipLevelsForSpellProgression>().FirstOrDefault();
                if (skip != null && skip.Levels != null && skip.Levels.Length > 0)
                {
                    var old = string.Join(",", skip.Levels);
                    skip.Levels = skip.Levels.Where(l => !targetLevels.Contains(l)).ToArray();
                    Main.Log($"[DD ProgFix] Adjusted SkipLevels (was [{old}] now [{string.Join(",", skip.Levels)}]).");
                }
                // 将 BAB 提升为全进度
                try
                {
                    if (!Main.Settings.DragonDiscipleFullBAB)
                    {
                        if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix] Full BAB toggle not enabled – skip.");
                    }
                    else
                    {
                        var highBAB = ResourcesLibrary.TryGetBlueprint<BlueprintStatProgression>(HighBABProgressionGuid);
                        if (highBAB != null)
                        {
                            var tClass = typeof(BlueprintCharacterClass);
                            FieldInfo fi = tClass.GetField("m_BaseAttackBonus", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            PropertyInfo pi = tClass.GetProperty("BaseAttackBonus", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            BlueprintStatProgressionReference currentRef = null;
                            if (fi != null) currentRef = fi.GetValue(ddClass) as BlueprintStatProgressionReference;
                            else if (pi != null) currentRef = pi.GetValue(ddClass) as BlueprintStatProgressionReference;
                            bool needSet = true;
                            if (currentRef != null)
                            {
                                try
                                {
                                    var currentBp = currentRef.Get();
                                    if (currentBp != null && currentBp.AssetGuid == highBAB.AssetGuid) needSet = false;
                                }
                                catch { }
                            }
                            if (needSet)
                            {
                                var newRef = highBAB.ToReference<BlueprintStatProgressionReference>();
                                if (fi != null) fi.SetValue(ddClass, newRef);
                                else if (pi != null && pi.CanWrite) pi.SetValue(ddClass, newRef);
                                Main.Log("[DD ProgFix] Set Dragon Disciple BAB to Full progression via reflection.");
                            }
                        }
                        else Main.Log("[DD ProgFix] High BAB progression blueprint not found.");
                    }
                }
                catch (Exception exBab) { Main.Log("[DD ProgFix] Exception while setting BAB: " + exBab.Message); }

                Main.Log("[DD ProgFix] Initialization base adjustments complete.");

                // 扩展属性加成
                try { DD_FeatureHelpers.EnsureDragonDiscipleAbilityBonuses(progression); } catch (Exception exFeat) { Main.Log("[DD ProgFix] Ability bonus extension error: " + exFeat.Message); }

                // 诊断：注入后输出 3/6/8 级（仅详细日志）
                if (Main.Settings.VerboseLogging)
                {
                    try { DD_FeatureHelpers.DumpLevels(progression, new int[] { 3, 6, 8 }); } catch { }
                }
                try { LocalizationInjector.EnsureInjected(); LocalizationInjector.InstallWatcher(); } catch { }
            }
            catch (System.Exception ex)
            {
                Main.Log("[DD ProgFix] Exception: " + ex);
            }
        }
    }

    // ApplySpellbook.Apply 的 Postfix——为 DD1 实现手动法术选择生成（该职业缺少自有法术书）
    [HarmonyPatch(typeof(ApplySpellbook), nameof(ApplySpellbook.Apply))]
    internal static class DragonDiscipleLevel1ManualSpellbookAdvance
    {
        private const string DragonDiscipleClassGuid = "72051275b1dbb2d42ba9118237794f7c";
        private static readonly HashSet<string> Processed = new HashSet<string>();
        private static readonly Dictionary<string, int> ExpectedAfter = new Dictionary<string, int>(); // key: unit|bookGuid
        private static MethodInfo _miTryApplyCustomSpells;

        // 允许会话级重置，以便洗点/新升级流程可重新应用
        internal static void ResetCache()
        {
            try { Processed.Clear(); ExpectedAfter.Clear(); } catch { }
        }

        internal static void EnsurePersisted(UnitDescriptor unit)
        {
            try
            {
                if (unit?.Unit == null) return;
                string unique = unit.Unit?.UniqueId ?? unit.GetHashCode().ToString();
                var keys = ExpectedAfter.Keys.Where(k => k.StartsWith(unique + "|"))?.ToList();
                if (keys == null || keys.Count == 0) return;
                foreach (var key in keys)
                {
                    if (!ExpectedAfter.TryGetValue(key, out int expected)) continue;
                    var parts = key.Split('|');
                    if (parts.Length < 3) continue;
                    var bookGuidStr = parts[2];
                    var book = unit.Spellbooks.FirstOrDefault(b => b?.Blueprint != null && b.Blueprint.AssetGuid.ToString() == bookGuidStr);
                    if (book == null) continue;
                    // If something reset our base below expected (e.g., respec), re-add until it matches.
                    while (book.BaseLevel < expected)
                    {
                        int before = book.BaseLevel;
                        book.AddBaseLevel();
                        int after = book.BaseLevel;
                        if (after == before) break; // safety
                    }
                }
            }
            catch { }
        }

        static void Postfix(ApplySpellbook __instance, LevelUpState state, UnitDescriptor unit)
        {
            try
            {
                if (!Main.Enabled) return;
                if (Main.Settings == null || !Main.Settings.EnableDragonDiscipleFix) return; // 合并后不再单独检测 L1 开关
                if (state?.SelectedClass == null || unit == null) return;
                if (state.Mode == LevelUpState.CharBuildMode.CharGen) return; // 避免角色创建 UI 的预处理阶段
                if (unit.Unit == null || !unit.Unit.IsMainCharacter || !unit.Unit.IsPlayerFaction) return;

                var ddClass = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(DragonDiscipleClassGuid);
                if (ddClass == null) return;
                if (state.SelectedClass != ddClass) return;

                int ddLevel = unit.Progression.GetClassLevel(ddClass);
                if (ddLevel != 1) return; // only level 1 currently

                // 确保位于预期的实际升级步骤（避免预览阶段重复）
                if (state.NextClassLevel != ddLevel) return;

                string unique = unit.Unit?.UniqueId ?? unit.GetHashCode().ToString();
                string key = unique + "|DD|" + ddLevel;
                if (Processed.Contains(key)) return;

                var book = unit.Spellbooks
                    .Where(b => b?.Blueprint != null && !b.Blueprint.IsMythic && b.Blueprint.Spontaneous && b.Blueprint.IsArcane)
                    .OrderByDescending(b => b.BaseLevel)
                    .FirstOrDefault();
                if (book == null)
                {
                    if (Main.Settings.VerboseLogging) Main.Log("[DD L1 Manual] No qualifying spontaneous arcane spellbook found – skip.");
                    Processed.Add(key);
                    return;
                }

                // 如选择已存在且 0..10 级均已初始化，则视为已处理
                var existing = state.GetSpellSelection(book.Blueprint, book.Blueprint.SpellList);
                if (existing != null && existing.LevelCount.All(s => s != null))
                {
                    if (Main.Settings.VerboseLogging) Main.Log("[DD L1 Manual] Selection already fully initialized – skip.");
                    Processed.Add(key);
                    return;
                }

                int before = book.BaseLevel;
                book.AddBaseLevel();
                int after = book.BaseLevel;
                if (after != before + 1)
                {
                    Main.Log($"[DD L1 Manual] Unexpected base change {before}->{after}, abort.");
                    Processed.Add(key);
                    return;
                }

                try { ExpectedAfter[unique + "|" + book.Blueprint.AssetGuid.ToString()] = after; } catch { }

                var selection = state.DemandSpellSelection(book.Blueprint, book.Blueprint.SpellList);
                if (selection == null)
                {
                    Main.Log("[DD L1 Manual] DemandSpellSelection returned null – abort.");
                    Processed.Add(key);
                    return;
                }

                var table = book.Blueprint.SpellsKnown;
                int oldIdx = before;
                int newIdx = after;
                for (int i = 0; i <= 10; i++) // 始终初始化 0..10 级以满足 CharGenVM 的假设
                {
                    int diff = 0;
                    if (table != null)
                    {
                        int oldCount = Math.Max(0, table.GetCount(oldIdx, i));
                        int newCount = Math.Max(0, table.GetCount(newIdx, i));
                        diff = Math.Max(0, newCount - oldCount);
                    }
                    selection.SetLevelSpells(i, diff);
                }

                if (book.Blueprint.SpellsPerLevel > 0)
                {
                    bool first = before == 0;
                    if (first)
                    {
                        selection.SetExtraSpells(0, book.MaxSpellLevel);
                        selection.ExtraByStat = true;
                        selection.UpdateMaxLevelSpells(unit);
                    }
                    else
                    {
                        selection.SetExtraSpells(book.Blueprint.SpellsPerLevel, book.MaxSpellLevel);
                    }
                }

                try
                {
                    _miTryApplyCustomSpells ??= AccessTools.Method(typeof(ApplySpellbook), "TryApplyCustomSpells");
                    var comps = (book.Blueprint as BlueprintScriptableObject)?.ComponentsArray;
                    if (_miTryApplyCustomSpells != null && comps != null)
                    {
                        foreach (var c in comps)
                        {
                            if (c != null && c.GetType().Name == "AddCustomSpells")
                            {
                                _miTryApplyCustomSpells.Invoke(null, new object[] { book, c, state, unit });
                            }
                        }
                    }
                }
                catch (Exception exCustom)
                {
                    Main.Log("[DD L1 Manual] CustomSpells reflection error: " + exCustom.Message);
                }

                Processed.Add(key);
                Main.Log($"[DD L1 Manual] Manual advance OK (single). Spellbook={book.Blueprint.name} base {before}->{after}.");
            }
            catch (System.Exception ex)
            {
                Main.Log("[DD L1 Manual] Exception: " + ex);
            }
        }
    }

    // ---- 属性加成特性注入逻辑（补丁内部使用）----
    internal static class DragonDiscipleAbilityBonusBuilder
    {
        private const string DragonDiscipleProgressionGuid = "69fc2bad2eb331346a6c777423e0d0f7";
        private const string DragonDiscipleClassGuid = "72051275b1dbb2d42ba9118237794f7c";
    }

    internal static class DD_FeatureHelpers
    {
        private static bool _built;
        private static bool _retroApplied;
        private static bool _dumpedLayout;
        private static BlueprintFeature _wisUnified, _chaUnified; // 使用原智力蓝图作参考

        internal static void DumpBlueprintFieldLayouts()
        {
            if (_dumpedLayout) return; _dumpedLayout = true;
            try
            {
                Main.Log("[DD ProgFix] ==== 蓝图字段布局转储 开始 ====");
                DumpType(typeof(SimpleBlueprint));
                DumpType(typeof(BlueprintScriptableObject));
                DumpType(typeof(BlueprintFeature));
                Main.Log("[DD ProgFix] ==== 蓝图字段布局转储 结束 ====");
            }
            catch (Exception ex) { Main.Log("[DD ProgFix] Field layout dump exception: " + ex.Message); }
        }
        private static void DumpType(Type t)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
                foreach (var f in t.GetFields(flags))
                {
                    bool isGuid = f.FieldType == typeof(BlueprintGuid);
                    Main.Log($"[DD ProgFix]   Field {f.Name} : {f.FieldType.FullName}{(isGuid ? " (BlueprintGuid)" : string.Empty)}");
                }
            }
            catch { }
        }

        internal static void EnsureDragonDiscipleAbilityBonuses(BlueprintProgression progression)
        {
            if (progression == null) return;
            BuildOrFetchFeatures();
            foreach (var le in progression.LevelEntries)
            {
                if (le == null) continue;
                var fi = typeof(LevelEntry).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi == null) continue;
                var raw = fi.GetValue(le);
                List<BlueprintFeatureBaseReference> list = raw switch
                {
                    BlueprintFeatureBaseReference[] arr => arr.ToList(),
                    List<BlueprintFeatureBaseReference> l => l,
                    _ => new List<BlueprintFeatureBaseReference>()
                };
                bool changed = false;

                // Ensure wis/cha unified features present at 3/6/10
                if (le.Level == 3)
                {
                    changed |= AddFeatureIfMissing(list, _wisUnified);
                    changed |= AddFeatureIfMissing(list, _chaUnified);
                    // keep original intelligence feature at 3 (already present in vanilla)
                    changed |= AddFeatureIfMissing(list, ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_IntelligenceFeatureGuid));
                }
                if (le.Level == 6)
                {
                    changed |= AddFeatureIfMissing(list, _wisUnified);
                    changed |= AddFeatureIfMissing(list, _chaUnified);
                    changed |= AddFeatureIfMissing(list, ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_IntelligenceFeatureGuid));
                }
                if (le.Level == 10)
                {
                    changed |= AddFeatureIfMissing(list, _wisUnified);
                    changed |= AddFeatureIfMissing(list, _chaUnified);
                    // move INT bonus from level 8 -> 10
                    changed |= AddFeatureIfMissing(list, ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_IntelligenceFeatureGuid));
                }

                // Remove unexpected INT bonus at level 8
                if (le.Level == 8)
                {
                    var intFeat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_IntelligenceFeatureGuid);
                    if (intFeat != null)
                    {
                        int removed = list.RemoveAll(r => r?.Get() == intFeat);
                        if (removed > 0) changed = true;
                    }
                }

                if (changed)
                {
                    if (fi.FieldType.IsArray) fi.SetValue(le, list.ToArray()); else fi.SetValue(le, list);
                }
            }
        }

        private static void BuildOrFetchFeatures()
        {
            if (_built) return; _built = true;
            // 获取或创建统一特性（Ranks=3）。附加 AddStatBonusPerFeatureRank 组件。
            _wisUnified = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_WisdomFeatureGuid) ??
                          CreateUnifiedFeature(NormalizeDragonDiscipleClass.DD_WisdomFeatureGuid, "DragonDiscipleWisdom", StatType.Wisdom,
                              "属性增强：{g|Encyclopedia:Wisdom}感知{/g}+2", "达到3级后，龙脉术士使角色的{g|Encyclopedia:Wisdom}感知{/g}值+2；达到6级后，再+2；达到10级后，再+2（共+6）。");
            _chaUnified = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_CharismaFeatureGuid) ??
                          CreateUnifiedFeature(NormalizeDragonDiscipleClass.DD_CharismaFeatureGuid, "DragonDiscipleCharisma", StatType.Charisma,
                              "属性增强：{g|Encyclopedia:Charisma}魅力{/g}+2", "达到3级后，龙脉术士使角色的{g|Encyclopedia:Charisma}魅力{/g}值+2；达到6级后，再+2；达到10级后，再+2（共+6）。");

            // Ensure original Intelligence feature allows 3 ranks so it can be granted at 3/6/10
            var intFeat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_IntelligenceFeatureGuid);
            if (intFeat != null)
            {
                TrySetRanks(intFeat, 3);
                // 更新智力特性的显示名称与描述保持与新增的感知/魅力一致的格式（加入百科标记）
                var intDisplay = "属性增强：{g|Encyclopedia:Intelligence}智力{/g}+2";
                var intDesc = "达到3级后，龙脉术士使角色的{g|Encyclopedia:Intelligence}智力{/g}值+2；达到6级后，再+2；达到10级后，再+2（共+6）。";
                ApplyLoc(intFeat, intDisplay, intDesc);
                LocalizationInjector.RegisterFeatureLocalization(intFeat, intDisplay, intDesc);
                Main.Log("[DD ProgFix] Updated Intelligence feature localization for multi-rank progression (with glossary tags).");
            }

            // 从力量特性复制图标以保持视觉一致
            var strength = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_StrengthFeatureGuid);
            if (strength != null)
            {
                CopyIcon(strength, _wisUnified); CopyIcon(strength, _chaUnified);
                var intFeatIcon = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_IntelligenceFeatureGuid);
                CopyIcon(strength, intFeatIcon); // 也确保智力图标统一
            }
        }

        private static void CopyIcon(BlueprintFeature src, BlueprintFeature dst)
        {
            if (src == null || dst == null) return;
            try
            {
                var f = typeof(BlueprintUnitFact).GetField("m_Icon", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var icon = f?.GetValue(src);
                if (icon != null) f?.SetValue(dst, icon);
            }
            catch { }
        }

        private static BlueprintFeature CreateUnifiedFeature(BlueprintGuid guid, string internalName, StatType stat, string display, string desc)
        {
            var strength = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NormalizeDragonDiscipleClass.DD_StrengthFeatureGuid);
            BlueprintFeature feat = null;
            if (strength != null)
            {
                feat = CloneFromTemplate(strength, guid, internalName, stat);
            }
            else
            {
                feat = NewFeature(guid, internalName);
                TrySetRanks(feat, 3);
                TrySetComponentsArray(feat, Array.Empty<BlueprintComponent>());
                EnsureAddStatBonus(feat, stat);
            }
            ApplyLoc(feat, display, desc);
            LocalizationInjector.RegisterFeatureLocalization(feat, display, desc);
            Main.Log($"[DD ProgFix] Created unified feature {internalName} {guid} stat={stat}");
            return feat;
        }

        private static BlueprintFeature CloneFromTemplate(BlueprintFeature template, BlueprintGuid guid, string internalName, StatType stat)
        {
            // 创建新实例并浅拷贝字段
            BlueprintFeature feat;
            try { feat = (BlueprintFeature)Activator.CreateInstance(typeof(BlueprintFeature)); }
            catch { feat = (BlueprintFeature)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintFeature)); }

            // 将模板的所有实例字段复制到新实例
            CopyAllFields(template, feat);

            // Set identity
            feat.name = internalName;
            AssignGuid(feat, guid);

            // Ensure ranks allow multiple grants
            TrySetRanks(feat, 3);

            // 深拷贝组件并重定向 AddStatBonus
            var comps = GetComponentsArray(template) ?? Array.Empty<BlueprintComponent>();
            var cloned = new List<BlueprintComponent>(comps.Length);
            foreach (var c in comps)
            {
                if (c == null) continue;
                var nc = (BlueprintComponent)Activator.CreateInstance(c.GetType());
                CopyAllFields(c, nc);
                if (nc is AddStatBonus add)
                {
                    add.Stat = stat;
                    add.Value = 2;
                    if (add.Descriptor == 0) add.Descriptor = ModifierDescriptor.Racial;
                }
                cloned.Add(nc);
            }
            TrySetComponentsArray(feat, cloned.ToArray());

            // 注册
            RegisterInternal(feat);
            return feat;
        }

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

        private static void CopyAllFields(object src, object dst)
        {
            if (src == null || dst == null) return;
            var t = src.GetType();
            for (; t != null; t = t.BaseType)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var f in t.GetFields(flags))
                {
                    // Skip AssetGuid to assign our own later
                    if (f.FieldType == typeof(BlueprintGuid) && f.Name == "AssetGuid") continue;
                    try
                    {
                        var val = f.GetValue(src);
                        // Arrays: copy array instances to avoid sharing mutable arrays
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

        private static BlueprintFeature NewFeature(BlueprintGuid guid, string internalName)
        {
            // Create managed instance (BlueprintFeature in Wrath is not a Unity ScriptableObject)
            BlueprintFeature feat;
            try { feat = (BlueprintFeature)Activator.CreateInstance(typeof(BlueprintFeature)); }
            catch { feat = (BlueprintFeature)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintFeature)); }
            feat.name = internalName;
            AssignGuid(feat, guid);
            feat.IsClassFeature = true;
            feat.ReapplyOnLevelUp = false;
            feat.HideInUI = false;
            RegisterInternal(feat);
            return feat;
        }

        private static T CreateComponentSafe<T>() where T : BlueprintComponent
        {
            try { return (T)Activator.CreateInstance(typeof(T)); }
            catch { return (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T)); }
        }

        private static void EnsureAddStatBonus(BlueprintFeature feat, StatType stat)
        {
            try
            {
                var t = typeof(BlueprintScriptableObject);
                var compField = t.GetField("Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                               ?? t.GetField("m_Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                BlueprintComponent[] comps = null;
                if (compField != null)
                {
                    comps = compField.GetValue(feat) as BlueprintComponent[];
                }
                if (comps == null)
                {
                    var pi = t.GetProperty("ComponentsArray", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    comps = (pi?.GetValue(feat) as BlueprintComponent[]) ?? Array.Empty<BlueprintComponent>();
                }

                // Use vanilla AddStatBonus to avoid custom component serialization
                var add = comps.OfType<AddStatBonus>().FirstOrDefault();
                if (add == null)
                {
                    add = CreateComponentSafe<AddStatBonus>();
                    add.Stat = stat;
                    add.Descriptor = ModifierDescriptor.Racial;
                    add.Value = 2;
                    comps = comps.Concat(new BlueprintComponent[] { add }).ToArray();
                }
                else
                {
                    add.Stat = stat; add.Value = 2; if (add.Descriptor == 0) add.Descriptor = ModifierDescriptor.Racial;
                }

                bool set = false;
                if (compField != null)
                {
                    compField.SetValue(feat, comps); set = true;
                }
                if (!set)
                {
                    var pi = t.GetProperty("ComponentsArray", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (pi != null && pi.CanWrite) { pi.SetValue(feat, comps); set = true; }
                }
            }
            catch { }
        }

        private static void ApplyLoc(BlueprintFeature feat, string display, string desc)
        {
            ApplyLocField(feat, "m_DisplayName", display); ApplyLocField(feat, "m_Description", desc);
        }
        private static void ApplyLocField(BlueprintFeature feat, string fieldName, string text)
        {
            // 始终创建新的 LocalizedString 实例，避免多个克隆特性共享同一对象
            // （否则会导致文本被相互覆盖，如都变成“魅力”）。
            var fi = typeof(BlueprintUnitFact).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi == null) return;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object newLoc = null;
            try { newLoc = Activator.CreateInstance(fi.FieldType); }
            catch { /* fallthrough; will try uninitialized object */ }
            if (newLoc == null)
            {
                try { newLoc = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(fi.FieldType); }
                catch { }
            }
            if (newLoc == null) return;
            newLoc.GetType().GetField("m_Text", flags)?.SetValue(newLoc, text);
            newLoc.GetType().GetField("m_Key", flags)?.SetValue(newLoc, "MDGA_DD_" + feat.AssetGuid + "_" + fieldName);
            fi.SetValue(feat, newLoc);
        }

        internal static void EnsureFactsForUnit(UnitEntityData unit, int ddLevel)
        {
            try
            {
                BuildOrFetchFeatures();
                if (ddLevel >= 3) EnsureFact(unit, _wisUnified);
                if (ddLevel >= 3) EnsureFact(unit, _chaUnified);
                // Set ranks to match thresholds 3/6/10
                int rank = ddLevel >= 10 ? 3 : ddLevel >= 6 ? 2 : 1;
                TrySetFactRank(unit, _wisUnified, rank);
                TrySetFactRank(unit, _chaUnified, rank);
            }
            catch { }
        }

        private static void TrySetFactRank(UnitEntityData unit, BlueprintFeature feat, int rank)
        {
            try
            {
                var fact = unit.Facts.List.FirstOrDefault(f => f?.Blueprint == feat);
                if (fact == null) return;
                var mi = fact.GetType().GetMethod("SetRank", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(fact, new object[] { rank });
            }
            catch { }
        }

        private static void EnsureFact(object unit, BlueprintFeature feat)
        {
            if (feat == null || unit == null) return;
            try
            {
                var descProp = unit.GetType().GetProperty("Descriptor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var descriptor = descProp?.GetValue(unit); if (descriptor == null) return;
                var factsProp = descriptor.GetType().GetProperty("Facts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var facts = factsProp?.GetValue(descriptor); if (facts == null) return;
                var listField = facts.GetType().GetField("m_Facts", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? facts.GetType().GetField("Facts", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var factList = listField?.GetValue(facts) as System.Collections.IEnumerable;
                if (factList != null)
                {
                    foreach (var f in factList)
                    {
                        var bpProp = f?.GetType().GetProperty("Blueprint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (bpProp?.GetValue(f) == feat) return;
                    }
                }
                MethodInfo miAddFact = null;
                foreach (var m in descriptor.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name == "AddFact") { var ps = m.GetParameters(); if (ps.Length >= 1 && typeof(BlueprintUnitFact).IsAssignableFrom(ps[0].ParameterType)) { miAddFact = m; break; } }
                }
                if (miAddFact != null)
                {
                    var ps = miAddFact.GetParameters(); object[] invoke = ps.Length switch { 1 => new object[] { feat }, 2 => new object[] { feat, null }, 3 => new object[] { feat, null, null }, _ => new object[] { feat } };
                    miAddFact.Invoke(descriptor, invoke);
                }
            }
            catch { }
        }

        // Utility helpers reused
        private static void AssignGuid(SimpleBlueprint bp, BlueprintGuid guid)
        { try { var fPublic = bp.GetType().GetField("AssetGuid", BindingFlags.Instance | BindingFlags.Public); if (fPublic != null) { fPublic.SetValue(bp, guid); return; } } catch { } foreach (var f in bp.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)) if (f.FieldType == typeof(BlueprintGuid)) { try { f.SetValue(bp, guid); return; } catch { } } }
        private static void RegisterInternal(SimpleBlueprint bp)
        { try { ResourcesLibrary.BlueprintsCache?.AddCachedBlueprint(bp.AssetGuid, bp); } catch { }
        }

        internal static void DumpLevels(BlueprintProgression prog, int[] levels)
        {
            if (prog?.LevelEntries == null) return;
            foreach (var lvl in levels)
            {
                var le = prog.LevelEntries.FirstOrDefault(e => e.Level == lvl);
                if (le == null) { Main.Log($"[DD ProgFix][DUMP] Level {lvl} entry missing"); continue; }
                var fi = typeof(LevelEntry).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var raw = fi?.GetValue(le);
                IEnumerable<BlueprintFeatureBaseReference> refs = raw switch
                {
                    BlueprintFeatureBaseReference[] arr => arr,
                    List<BlueprintFeatureBaseReference> list => list,
                    _ => Enumerable.Empty<BlueprintFeatureBaseReference>()
                };
                Main.Log($"[DD ProgFix][DUMP] Level {lvl} features: " + string.Join(", ", refs.Select(r => r?.Get()?.name + ":" + r?.Get()?.AssetGuid.ToString().Substring(0, 8))));
            }
        }
        internal static void DumpCacheExtended() { }

        private static bool AddFeatureIfMissing(List<BlueprintFeatureBaseReference> list, BlueprintFeature feat)
        {
            if (feat == null) return false;
            if (list.Any(r => r?.Get() == feat)) return false;
            list.Add(feat.ToReference<BlueprintFeatureBaseReference>());
            return true;
        }

        // Missing helpers restored
        private static void TrySetRanks(BlueprintFeature feat, int ranks)
        {
            try
            {
                var fi = typeof(BlueprintFeature).GetField("Ranks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi != null) { fi.SetValue(feat, ranks); return; }
                var pi = typeof(BlueprintFeature).GetProperty("Ranks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (pi != null && pi.CanWrite) { pi.SetValue(feat, ranks); }
            }
            catch { }
        }

        private static void TrySetComponentsArray(BlueprintScriptableObject bp, BlueprintComponent[] comps)
        {
            try
            {
                var t = typeof(BlueprintScriptableObject);
                var compField = t.GetField("Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                               ?? t.GetField("m_Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (compField != null) { compField.SetValue(bp, comps); return; }
                var pi = t.GetProperty("ComponentsArray", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (pi != null && pi.CanWrite) { pi.SetValue(bp, comps); }
            }
            catch { }
        }

        private static void DumpTypePublic(Type t)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
                foreach (var f in t.GetFields(flags))
                {
                    try
                    {
                        bool isGuid = f.FieldType == typeof(BlueprintGuid);
                        Main.Log($"[DD ProgFix]   Field {f.Name} : {f.FieldType.FullName}{(isGuid ? " (BlueprintGuid)" : string.Empty)}");
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(LevelUpController), nameof(LevelUpController.ApplyLevelUpActions))]
    internal static class DragonDiscipleStatFeatureEnsurePatch
    {
        private const string DDGuid = "72051275b1dbb2d42ba9118237794f7c";

        [HarmonyPostfix]
        private static void Postfix(LevelUpController __instance, UnitEntityData unit)
        {
            try
            {
                if (!Main.Enabled) return;
                if (Main.Settings == null || !Main.Settings.EnableDragonDiscipleFix) return;
                var target = unit ?? __instance?.Unit;
                if (target == null) return;
                var ddClass = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(DDGuid); if (ddClass == null) return;
                int ddLevel = 0; try { ddLevel = target.Progression.GetClassLevel(ddClass); } catch { }
                if (ddLevel <= 0) return;
                DD_FeatureHelpers.EnsureFactsForUnit(target, ddLevel);
            }
            catch (Exception ex)
            {
                Main.Log("[DD ProgFix][EnsurePatch] Exception: " + ex.Message);
            }
        }
    }

    // Ensure again at final commit so respec cases get corrected after levels are actually applied
    [HarmonyPatch(typeof(LevelUpController), "Commit")]
    internal static class DragonDiscipleStatFeatureEnsureCommitPatch
    {
        private const string DDGuid = "72051275b1dbb2d42ba9118237794f7c";
        [HarmonyPostfix]
        private static void Postfix(LevelUpController __instance)
        {
            try
            {
                if (!Main.Enabled) return;
                if (Main.Settings == null || !Main.Settings.EnableDragonDiscipleFix) return;
                var unit = __instance?.Unit; if (unit == null) return;
                var ddClass = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(DDGuid); if (ddClass == null) return;
                int ddLevel = 0; try { ddLevel = unit.Progression.GetClassLevel(ddClass); } catch { }
                if (ddLevel <= 0) return;
                DD_FeatureHelpers.EnsureFactsForUnit(unit, ddLevel);
                // Ensure manual DD1 spellbook advance persists past respec commit
                try { DragonDiscipleLevel1ManualSpellbookAdvance.EnsurePersisted(unit.Descriptor); } catch { }
            }
            catch (Exception ex)
            {
                Main.Log("[DD ProgFix][EnsureCommit] Exception: " + ex.Message);
            }
        }
    }
}
