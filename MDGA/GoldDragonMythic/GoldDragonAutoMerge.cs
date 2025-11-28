using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.JsonSystem;
using System.Text.RegularExpressions; // 已添加
using Kingmaker.UnitLogic.Class.LevelUp; // 用于 FeatureSelectionState、LevelUpState
using Kingmaker.UnitLogic; // UnitDescriptor
using Kingmaker.Blueprints.Root; // BlueprintRoot，用于动态收集法术书
using Kingmaker.Localization; // 已添加
using UnityEngine; // 用于 ScriptableObject
using Kingmaker.Blueprints.Facts; // 用于 BlueprintUnitFact
using System.Collections.Generic;
using Kingmaker; // 如需游戏日志
using Kingmaker.UnitLogic.Abilities.Blueprints; // 为 BlueprintAbilityReference 添加
using Kingmaker.Blueprints.Classes.Prerequisites;
using MDGA.Loc; // PrerequisiteFeaturesFromList

namespace MDGA.GoldDragonMythic
{
    internal static class GoldDragonAutoMerge
    {
        private const string AngelMergeFeatureGuid = "e1fbb0e0e610a3a4d91e5e5284587939";
        private const string AngelMythicSpellListGuid = "deaffb4218ccf2f419ffd6e41603131a"; // kept for compatibility
        private const string AngelSpellsKnownTableGuid = "2d574ccdea8543bda1dffe63b0f16760"; // fallback table if GD specific not found

        // 自定义合书特性 GUID（仍然需要）—始终指向（可能已增强的）天使法术列表
        private static readonly BlueprintGuid GD_MergeFeatureGuid = BlueprintGuid.Parse("5bf6f5d4d2e04a1a9f4b4f4b6a9a1111");
        private const string GoldenDragonProgressionGuid = "a6fbca43902c6194c947546e89af64bd";
        private const string GoldenDragonClassGuid = "daf1235b6217787499c14e4e32142523"; // 金龙神话职业
        private const string ExternalGoldDragonSpellbookGuid = "9a9ced35-fa75-4287-bc87-ba97e29812c5";
        private const string GoldDragonSpellbookGuid = "614b5ef6df084725aa872d43e0d0cd1e"; // 原版金龙法术书
    // 混血术士专用法术书（用于允许 Crossblooded 参与合书）
    private const string CrossbloodedSorcererSpellbookGuid = "cb0be5988031ebe4c947086a1170eacc";
        // 旧的复合神话列表 GUID，保留用于向后兼容（不再创建）
        private static readonly BlueprintGuid GD_CompositeMythicListGuid = BlueprintGuid.Parse("9d2c8f6c4d4f47d4846f42d6a2b70055");

        private static bool _externalGoldDragonSpellbookPresent;
        private static bool _ran;

        private static BlueprintFeatureSelectMythicSpellbook _customMerge; // 缓存引用
        // 不再使用复合或天使增强列表
        private static BlueprintSpellList _compositeList = null;

        private static string _gdOverrideNameZh = "神话法术书";
        private static string _gdOverrideDescZh = "当你踏上金龙之路并达到神话阶层8时，你可以选择将金龙神话施法进度与之前的一个非神话施法职业合并，或使用一部独立的金龙神话法术书。\n合并时：你的金龙神话阶层与所选职业的施法者等级累加（总施法者等级上限30），使用原职业的法术位施放“金龙法术”。提升施法者等级可加强这些法术。复合列表中的金龙专属法术只会在你能够施放相应环位时出现。自发施法职业同时按原职业与神话阶层分别获得新的已知法术。\n独立时：获得一部仅包含金龙神话法术（及其专属法术）的独立法术书，拥有自己独立的进度与法术位；其法术位数量不能通过（其他职业的）施法者等级提升，只随神话阶层推进。\n若你是多职业或不希望改变原职业法术位分配，推荐使用独立法术书；若你是纯或近纯单一施法职业，推荐合并以获得更高的总施法者等级。\n前提：需要拥有龙族血统，且不能与金龙自身或另一部神话法术书再次合书。";
        private static string _gdOverrideNameEn = "Mythic Spellbook";
        private static string _gdOverrideDescEn = "When you reach Mythic Rank 8 on the Golden Dragon path, you may either merge the Golden Dragon mythic spell progression with one previous non‑mythic casting class, or use an independent Golden Dragon mythic spellbook.\nMerged: Your Golden Dragon mythic ranks stack with the chosen class for caster level (total caster level cap 30). You use that class's spell slots to cast a \"Golden Dragon list\" . Increasing caster level empowers these spells. Golden Dragon unique spells only appear once you can cast their spell level. Spontaneous casters gain new known spells from both the base class progression and mythic ranks.\nIndependent: You gain a separate Golden Dragon mythic spellbook that contains only Golden Dragon mythic spells (including its unique spells). It has its own progression and slots; its slots scale only with mythic rank and are not increased by other caster levels.\nRecommendation: Choose the independent book if you are multi‑classed or do not want to alter your base class slot progression. Choose merging if you are a pure or near‑pure single casting class to reach a higher effective caster level.\nPrerequisite: You must possess a draconic bloodline. You cannot merge with the Golden Dragon spellbook itself again or with another mythic spellbook.";

        // 判定是否拥有“龙族血脉”的基准特性（与 DragonBloodActivation 中保持一致）
        private static readonly BlueprintGuid[] DraconicRequisiteGuids = new[]
        {
            BlueprintGuid.Parse("d89fb8ce9152ffa4dacd69390f3d7721"),
            BlueprintGuid.Parse("64e1f27147b642448842ab8dcbaca03f"),
            BlueprintGuid.Parse("12bb1056a5f3f9f4b9facdb78b8d8914"),
            BlueprintGuid.Parse("1d34d95ad4961e343b02db14690eb6d8"),
            BlueprintGuid.Parse("eef664d1e4318f64cb2304d1628d29ae"),
            BlueprintGuid.Parse("bef8d08ee3c20b246b404ce3ef948291"),
            BlueprintGuid.Parse("49115e2147cd32841baa34c305171daa"),
            BlueprintGuid.Parse("9c5ed34089fedf54ba8d0f43565bcc91"),
            BlueprintGuid.Parse("01e7aab638d6a0b43bc4e9d5b49e68d9"),
            BlueprintGuid.Parse("3867419bf47841b428333808dfdf4ae0"),
            // Crossblooded secondary draconic bloodline progressions (10 colors) — treat as requisite to allow merge
            BlueprintGuid.Parse("be355f1518587224799e6c125aad2ac0"), // Gold
            BlueprintGuid.Parse("a335def5677a4dc46bab211b30a9f33c"), // Green
            BlueprintGuid.Parse("076d1648e1341f841a222de5b89ba215"), // Silver
            BlueprintGuid.Parse("0970d63bc8e90274a90f6c001318df77"), // White
            BlueprintGuid.Parse("12484c4d15c3e134f9fd23931c38e996"), // Red
            BlueprintGuid.Parse("cbc80f3fdc72aac4bb6963efe3062473"), // Blue
            BlueprintGuid.Parse("601387ac1873bba448bddcfbf00af8d7"), // Black
            BlueprintGuid.Parse("b7555ce6cfc7fc042bb7b701d2618307"), // Brass
            BlueprintGuid.Parse("b71dc10096302384c901787a40b81fbf"), // Bronze
            BlueprintGuid.Parse("c0684cd2e00de724990bb389e672db90"), // Copper
        };

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
        private static string GD_Name => IsChinese() ? _gdOverrideNameZh : _gdOverrideNameEn;
        private static string GD_Desc => IsChinese() ? _gdOverrideDescZh : _gdOverrideDescEn;

        internal static void TryRunAfterUMMLoad()
        {
            if (!Main.Enabled) return;
            if (!Main.Settings.EnableGoldenDragonMerge) return;
            if (_ran) { EnsureProgressionHasMergeFeature(); return; }
            var angelMerge = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelectMythicSpellbook>(AngelMergeFeatureGuid);
            if (angelMerge != null)
            {
                try { Implement(); _ran = true; } catch (Exception ex) { Main.Log("[GD Merge] Late init exception: " + ex); }
            }
        }

        [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
        private static class Patch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (_ran) return; _ran = true; if (!Main.Enabled) return; if (!Main.Settings.EnableGoldenDragonMerge) return;
                try { Implement(); } catch (Exception ex) { Main.Log("[GD Merge] Exception: " + ex); }
            }
        }

        private static void Implement()
        {
            _externalGoldDragonSpellbookPresent = ResourcesLibrary.TryGetBlueprint<BlueprintSpellbook>(ExternalGoldDragonSpellbookGuid) != null;

            if (_customMerge != null)
            {
                EnsureProgressionHasMergeFeature();
                return;
            }

            var angelMerge = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelectMythicSpellbook>(AngelMergeFeatureGuid);
            // 获取金龙神话法术列表
            var goldBook = ResourcesLibrary.TryGetBlueprint<BlueprintSpellbook>(GoldDragonSpellbookGuid);
            BlueprintSpellList goldMythicList = null;
            BlueprintSpellsTable goldSpellsKnown = null;
            if (goldBook != null)
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var fMyth = goldBook.GetType().GetField("m_MythicSpellList", flags);
                var mythRef = fMyth?.GetValue(goldBook) as BlueprintSpellListReference;
                goldMythicList = mythRef?.Get();

                // 尝试从金龙法术书读取自发已知表（若无，则稍后回退到天使表）
                var fKnown = goldBook.GetType().GetField("m_SpellKnownForSpontaneous", flags);
                var knownRef = fKnown?.GetValue(goldBook) as BlueprintSpellsTableReference;
                goldSpellsKnown = knownRef?.Get();
            }

            var angelKnown = ResourcesLibrary.TryGetBlueprint<BlueprintSpellsTable>(AngelSpellsKnownTableGuid);
            if (angelMerge == null || goldMythicList == null)
            {
                Main.Log("[GD Merge] Required sources missing – abort custom merge creation (angelMerge or goldMythicList).");
                return;
            }

            try
            {
                _customMerge = (BlueprintFeatureSelectMythicSpellbook)Activator.CreateInstance(typeof(BlueprintFeatureSelectMythicSpellbook));
            }
            catch
            {
                _customMerge = (BlueprintFeatureSelectMythicSpellbook)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintFeatureSelectMythicSpellbook));
            }
            _customMerge.name = "GoldenDragonIncorporateSpellbook";
            SetGuid(_customMerge, GD_MergeFeatureGuid);
            _customMerge.IsClassFeature = true;
            _customMerge.Ranks = 1;
            try
            {
                var iconField = typeof(BlueprintUnitFact).GetField("m_Icon", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                iconField?.SetValue(_customMerge, iconField?.GetValue(angelMerge));
            }
            catch { }
            var allowed = BuildDynamicAllowedSpellbooks();
            typeof(BlueprintFeatureSelectMythicSpellbook).GetField("m_AllowedSpellbooks", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(_customMerge, allowed);
            try
            {
                var names = allowed?.Select(r => r?.Get()?.name + ":" + (r?.Get()?.AssetGuid.ToString() ?? ""))?.ToArray() ?? Array.Empty<string>();
                Main.Log($"[GD Merge][AllowedSB] Set on custom merge: count={(allowed==null?0:allowed.Length)}");
                if (Main.Settings.VerboseLogging)
                    Main.Log($"[GD Merge][AllowedSB] -> [\n  {string.Join(",\n  ", names)}\n]");
            }
            catch (Exception ex) { Main.Log("[GD Merge][AllowedSB] Post-set summary error: " + ex.Message); }
            // 仅使用金龙神话法术列表进行合书
            typeof(BlueprintFeatureSelectMythicSpellbook).GetField("m_MythicSpellList", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(_customMerge, goldMythicList.ToReference<BlueprintSpellListReference>());
            // 自发已知表优先使用金龙，找不到则回退天使
            var knownTableToUse = goldSpellsKnown ?? angelKnown;
            if (knownTableToUse != null)
                typeof(BlueprintFeatureSelectMythicSpellbook).GetField("m_SpellKnownForSpontaneous", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(_customMerge, knownTableToUse.ToReference<BlueprintSpellsTableReference>());

            // 添加“需要拥有任意一个龙族血统（隐藏未满足时）”的前置条件（包含混血术士主/副血系的动态收集）
            try
            {
                var baseRefs = DraconicRequisiteGuids
                    .Select(g => ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(g))
                    .Where(b => b != null)
                    .Select(b => b.ToReference<BlueprintFeatureReference>())
                    .ToList();
                var dynamic = FindDraconicRequisiteFeatures();
                foreach (var f in dynamic)
                {
                    var r = f.ToReference<BlueprintFeatureReference>();
                    if (!baseRefs.Any(x => x.Guid == r.Guid)) baseRefs.Add(r);
                }

                var comp = new PrerequisiteFeaturesFromList
                {
                    Group = Prerequisite.GroupType.Any,
                    Amount = 1,
                    CheckInProgression = true,
                    HideInUI = true,
                };
                var field = typeof(PrerequisiteFeaturesFromList).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                field?.SetValue(comp, baseRefs.ToArray());
                AddComponent(_customMerge, comp);

                if (Main.Settings.VerboseLogging)
                {
                    Main.Log($"[GD Merge] Draconic requisite features count={baseRefs.Count} (static={DraconicRequisiteGuids.Length}, dynamic+dedup={baseRefs.Count - DraconicRequisiteGuids.Length})");
                }
            }
            catch { }
            ApplyLocalizedText(_customMerge);

            try
            {
                ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(_customMerge.AssetGuid, _customMerge);
                if (ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelectMythicSpellbook>(_customMerge.AssetGuid) == null)
                    Main.Log("[GD Merge] WARNING: registration check failed for custom merge feature");
                else Main.Log("[GD Merge] Custom merge feature registered OK.");
            }
            catch (Exception ex)
            {
                Main.Log("[GD Merge] Registration exception: " + ex.Message);
            }

            EnsureProgressionHasMergeFeature();
        }

        // 取消天使部分：不再增强天使法术列表
        private static void AugmentAngelListInPlace(BlueprintSpellList angelList) { /* no-op: Angel part removed per new design */ }

        // （原 BuildCompositeMythicList 已移除—保留方法名但留空，以兼容可能的外部引用）
        private static void BuildCompositeMythicList(BlueprintSpellList angelList) { /* obsolete under strategy B */ }

        private static void DumpSpellListStructure(BlueprintSpellList list)
        {
            if (list == null) return;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                Main.Log("[GD Merge][Struct] Dumping fields of Angel Mythic SpellList type=" + list.GetType().FullName);
                foreach (var f in list.GetType().GetFields(flags))
                {
                    var val = f.GetValue(list);
                    Main.Log("[GD Merge][Struct] Field " + f.Name + " : " + f.FieldType.Name + " value=" + (val==null?"null":val.GetType().Name));
                }
                foreach (var p in list.GetType().GetProperties(flags))
                {
                    object val = null; try { if (p.GetIndexParameters().Length==0) val = p.GetValue(list,null); } catch { }
                    Main.Log("[GD Merge][Struct] Prop " + p.Name + " : " + p.PropertyType.Name + " valueType=" + (val==null?"null":val.GetType().Name));
                }
            }
            catch (Exception ex)
            {
                Main.Log("[GD Merge][Struct] Dump error: " + ex.Message);
            }
        }

        private static void AugmentAngelListWithGoldDragon(BlueprintSpellList angel, BlueprintSpellList goldMythic, BlueprintSpellList goldBase)
        {
            try
            {
                if (angel == null) return;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var lvlField = typeof(BlueprintSpellList).GetField("m_SpellsByLevel", flags) ?? typeof(BlueprintSpellList).GetField("SpellsByLevel", flags);
                object angelLevelsRaw = lvlField?.GetValue(angel) ?? typeof(BlueprintSpellList).GetProperty("SpellsByLevel", flags)?.GetValue(angel);
                var angelEnum = angelLevelsRaw as System.Collections.IEnumerable; if (angelEnum == null) { Main.Log("[GD Merge] Angel list augment: cannot enumerate levels"); return; }
                var levelListType = angelEnum.Cast<object>().FirstOrDefault()?.GetType(); if (levelListType == null) return;
                var fLevel = levelListType.GetField("m_SpellLevel", flags) ?? levelListType.GetField("Level", flags) ?? levelListType.GetField("SpellLevel", flags);
                var fSpells = levelListType.GetField("m_Spells", flags) ?? levelListType.GetField("Spells", flags);
                var angelLevelObjects = angelEnum.Cast<object>().ToList();
                var angelMap = new Dictionary<int, System.Collections.IList>();
                foreach (var o in angelLevelObjects)
                {
                    int lv = (int)(fLevel?.GetValue(o) ?? 0);
                    var list = fSpells?.GetValue(o) as System.Collections.IList;
                    if (!angelMap.ContainsKey(lv) && list != null) angelMap[lv] = list;
                }
                int added = 0;
                void MergeSrc(BlueprintSpellList src)
                {
                    if (src == null) return;
                    object raw = lvlField?.GetValue(src) ?? typeof(BlueprintSpellList).GetProperty("SpellsByLevel", flags)?.GetValue(src);
                    var srcEnum = raw as System.Collections.IEnumerable; if (srcEnum == null) return;
                    foreach (var sl in srcEnum)
                    {
                        int level = (int)(fLevel?.GetValue(sl) ?? 0);
                        // 仅合并高环独有法术（8-10 环）
                        if (level < 8 || level > 10) continue;
                        if (!angelMap.TryGetValue(level, out var target) || target == null) continue;
                        var srcList = fSpells?.GetValue(sl) as System.Collections.IList; if (srcList == null) continue;
                        foreach (var spellRef in srcList)
                        {
                            var guid = GetAbilityRefGuid(spellRef); if (guid == BlueprintGuid.Empty) continue;
                            bool exists = false; foreach (var e in target) { if (GetAbilityRefGuid(e) == guid) { exists = true; break; } }
                            if (!exists) { target.Add(spellRef); added++; }
                        }
                    }
                }
                MergeSrc(goldMythic); MergeSrc(goldBase);
                Main.Log(added > 0 ? "[GD Merge] Augmented Angel mythic list with Gold Dragon high spells (L8-10) count=" + added : "[GD Merge] No unique Gold Dragon high spells to add (already present or disabled by settings)." );
            }
            catch (Exception ex) { Main.Log("[GD Merge] AugmentAngelList exception: " + ex.Message); }
        }

        private static void DumpMythicListDiagnostics(BlueprintSpellList list, string label)
        {
            if (list == null) { Main.Log("[GD Merge][ListDiag] " + label + " list=null"); return; }
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var lvlField = typeof(BlueprintSpellList).GetField("m_SpellsByLevel", flags) ?? typeof(BlueprintSpellList).GetField("SpellsByLevel", flags);
                object raw = lvlField?.GetValue(list) ?? typeof(BlueprintSpellList).GetProperty("SpellsByLevel", flags)?.GetValue(list);
                var enumerable = raw as System.Collections.IEnumerable;
                if (enumerable == null) { Main.Log("[GD Merge][ListDiag] " + label + " cannot enumerate levels"); return; }
                int total = 0;
                Main.Log("[GD Merge][ListDiag] ==== " + label + " BEGIN ====");
                foreach (var lvlObj in enumerable)
                {
                    if (lvlObj == null) continue;
                    var t = lvlObj.GetType();
                    var fLevel = t.GetField("m_SpellLevel", flags) ?? t.GetField("Level", flags) ?? t.GetField("SpellLevel", flags);
                    var fSpells = t.GetField("m_Spells", flags) ?? t.GetField("Spells", flags);
                    int level = (int)(fLevel?.GetValue(lvlObj) ?? 0);
                    var spellsList = fSpells?.GetValue(lvlObj) as System.Collections.IEnumerable;
                    int count = 0; List<string> ids = null;
                    if (spellsList != null)
                    {
                        ids = new List<string>();
                        foreach (var s in spellsList)
                        {
                            count++;
                            if (Main.Settings.VerboseLogging)
                            {
                                var guid = GetAbilityRefGuid(s);
                                if (guid != BlueprintGuid.Empty) ids.Add(guid.ToString().Substring(0,8));
                            }
                        }
                    }
                    total += count;
                    if (Main.Settings.VerboseLogging) Main.Log($"[GD Merge][ListDiag] L{level}: count={count} guids=[{string.Join(",", ids)}]"); else Main.Log($"[GD Merge][ListDiag] L{level}: count={count}");
                }
                Main.Log("[GD Merge][ListDiag] TOTAL spells=" + total + " label=" + label);
                Main.Log("[GD Merge][ListDiag] ==== " + label + " END ====");
            }
            catch (Exception ex) { Main.Log("[GD Merge][ListDiag] Exception while dumping list: " + ex.Message); }
        }

        private static void EnsureProgressionHasMergeFeature()
        {
            try
            {
                var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(GoldenDragonProgressionGuid); if (prog == null || _customMerge == null) return;
                // 优先在 MR8 放置（与描述一致），若不存在则回退到 1 级
                var entry = prog.LevelEntries?.FirstOrDefault(le => le.Level == 8) ?? prog.LevelEntries?.FirstOrDefault(le => le.Level == 1);
                if (entry == null) return;
                var fi = typeof(LevelEntry).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var raw = fi?.GetValue(entry);
                var list = raw is BlueprintFeatureBaseReference[] arr ? arr.ToList() : raw as List<BlueprintFeatureBaseReference> ?? new List<BlueprintFeatureBaseReference>();
                var targetRef = _customMerge.ToReference<BlueprintFeatureBaseReference>();
                int desiredIndex = Math.Min(1, Math.Max(0, list.Count)); // 尽量放在第二项
                int beforeCount = list.Count;
                int currentIndex = list.FindIndex(r => r?.Get()?.AssetGuid == GD_MergeFeatureGuid);
                if (currentIndex < 0)
                {
                    list.Insert(desiredIndex, targetRef);
                    if (Main.Settings.VerboseLogging) Main.Log("[GD Merge] Inserted custom merge feature at index=" + desiredIndex + ".");
                }
                else if (currentIndex != desiredIndex)
                {
                    var item = list[currentIndex];
                    list.RemoveAt(currentIndex);
                    // 如果移除使 desiredIndex 越界或项数减少，重新校正
                    desiredIndex = Math.Min(1, Math.Max(0, list.Count));
                    list.Insert(desiredIndex, item);
                    if (Main.Settings.VerboseLogging) Main.Log($"[GD Merge] Moved custom merge feature from index={currentIndex} to index={desiredIndex}.");
                }
                if (fi.FieldType.IsArray) fi.SetValue(entry, list.ToArray()); else fi.SetValue(entry, list);
                try
                {
                    Main.Log($"[GD Merge] Progression placement: prog={prog.name} guid={prog.AssetGuid} level={entry.Level} beforeCount={beforeCount} afterCount={list.Count} targetIndex={Math.Min(desiredIndex, list.Count-1)}");
                    if (Main.Settings.VerboseLogging)
                    {
                        // 列出前后若干相邻项，帮助确认插入位置
                        int idx = Math.Min(desiredIndex, list.Count-1);
                        int from = Math.Max(0, idx - 2);
                        int to = Math.Min(list.Count - 1, idx + 2);
                        for (int i = from; i <= to; i++)
                        {
                            var f = list[i]?.Get();
                            Main.Log($"[GD Merge]   [{i}] {f?.name ?? "null"} guid={(f==null?"":f.AssetGuid.ToString())}");
                        }
                    }
                }
                catch (Exception ex) { Main.Log("[GD Merge] Progression placement log error: " + ex.Message); }
            }
            catch (Exception ex) { Main.Log("[GD Merge] EnsureProgressionHasMergeFeature error: " + ex.Message); }
        }

        private static bool _locRegistered;
        private static void ApplyLocalizedText(BlueprintFeatureSelectMythicSpellbook bp)
        {
            try
            {
                if (!_locRegistered)
                {
                    LocalizationInjector.RegisterDynamicKey("MDGA_GD_Merge_Name_zh", _gdOverrideNameZh);
                    LocalizationInjector.RegisterDynamicKey("MDGA_GD_Merge_Desc_zh", _gdOverrideDescZh);
                    LocalizationInjector.RegisterDynamicKey("MDGA_GD_Merge_Name_en", _gdOverrideNameEn);
                    LocalizationInjector.RegisterDynamicKey("MDGA_GD_Merge_Desc_en", _gdOverrideDescEn);
                    _locRegistered = true;
                }
                var fName = typeof(BlueprintUnitFact).GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var fDesc = typeof(BlueprintUnitFact).GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var nameLoc = fName?.GetValue(bp) ?? new LocalizedString();
                var descLoc = fDesc?.GetValue(bp) ?? new LocalizedString();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                string nameKey = IsChinese() ? "MDGA_GD_Merge_Name_zh" : "MDGA_GD_Merge_Name_en";
                string descKey = IsChinese() ? "MDGA_GD_Merge_Desc_zh" : "MDGA_GD_Merge_Desc_en";
                nameLoc.GetType().GetField("m_Key", flags)?.SetValue(nameLoc, nameKey);
                nameLoc.GetType().GetField("m_Text", flags)?.SetValue(nameLoc, GD_Name);
                descLoc.GetType().GetField("m_Key", flags)?.SetValue(descLoc, descKey);
                descLoc.GetType().GetField("m_Text", flags)?.SetValue(descLoc, GD_Desc);
                fName?.SetValue(bp, nameLoc); fDesc?.SetValue(bp, descLoc);
                LocalizationInjector.EnsureInjected();
            }
            catch (Exception ex) { Main.Log("[GD Merge] ApplyLocalizedText error: " + ex.Message); }
            try { bp.OnEnable(); } catch { }
        }

        private static BlueprintSpellbookReference[] BuildDynamicAllowedSpellbooks()
        {
            try
            {
                var classes = BlueprintRoot.Instance?.Progression?.CharacterClasses;
                var list = new List<BlueprintSpellbookReference>();
                if (classes != null)
                {
                    foreach (var cls in classes)
                    {
                        if (cls == null) continue; if (cls.IsMythic) continue; if (cls.Spellbook == null) continue; if (cls.Spellbook.SpellList == null) continue;
                        bool excluded = false; string reason = "";
                        if (_externalGoldDragonSpellbookPresent && cls.Spellbook.AssetGuid == BlueprintGuid.Parse(ExternalGoldDragonSpellbookGuid)) { excluded = true; reason = "external-GD-spellbook"; }
                        else if (cls.AssetGuid == BlueprintGuid.Parse(GoldenDragonClassGuid)) { excluded = true; reason = "self-GD-class"; }

                        if (Main.Settings.VerboseLogging)
                        {
                            Main.Log($"[GD Merge][AllowedSB] Scan class: {cls.name} classGuid={cls.AssetGuid} mythic={cls.IsMythic} spellbook={(cls.Spellbook==null?"null":cls.Spellbook.name)} sbGuid={(cls.Spellbook==null?"":cls.Spellbook.AssetGuid.ToString())} hasList={(cls.Spellbook?.SpellList!=null)}");
                        }

                        if (excluded)
                        {
                            if (Main.Settings.VerboseLogging) Main.Log($"[GD Merge][AllowedSB]  -> exclude: {cls.name} reason={reason}");
                            continue;
                        }
                        list.Add(cls.Spellbook.ToReference<BlueprintSpellbookReference>());
                        if (Main.Settings.VerboseLogging) Main.Log($"[GD Merge][AllowedSB]  -> include spellbook: {cls.Spellbook.name} guid={cls.Spellbook.AssetGuid}");

                        // 额外处理：术士（Sorcerer）存在“混血术士(Crossblooded)”替换法术书，需将该专用法术书一并加入允许列表
                        try
                        {
                            var clsName = cls.name ?? string.Empty;
                            if (clsName.IndexOf("Sorcerer", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var cross = ResourcesLibrary.TryGetBlueprint<BlueprintSpellbook>(CrossbloodedSorcererSpellbookGuid);
                                if (cross != null && cross.SpellList != null)
                                {
                                    var crossRef = cross.ToReference<BlueprintSpellbookReference>();
                                    if (!list.Any(r => r != null && r.Guid == crossRef.Guid))
                                    {
                                        list.Add(crossRef);
                                        if (Main.Settings.VerboseLogging) Main.Log($"[GD Merge][AllowedSB]  -> include Crossblooded spellbook: {cross.name} guid={cross.AssetGuid}");
                                    }
                                }
                                else if (Main.Settings.VerboseLogging)
                                {
                                    Main.Log("[GD Merge][AllowedSB]  -> Crossblooded spellbook not found or has no list, guid=" + CrossbloodedSorcererSpellbookGuid);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (Main.Settings.VerboseLogging) Main.Log("[GD Merge][AllowedSB] Crossblooded inject error: " + ex.Message);
                        }
                    }
                }
                var finalArr = list.Distinct().ToArray();
                try
                {
                    var finalNames = finalArr.Select(r => r?.Get()?.name + ":" + (r?.Get()?.AssetGuid.ToString() ?? ""));
                    Main.Log($"[GD Merge][AllowedSB] Final allowed count={finalArr.Length}");
                    if (Main.Settings.VerboseLogging) Main.Log($"[GD Merge][AllowedSB] Final list -> [\n  {string.Join(",\n  ", finalNames)}\n]");
                }
                catch (Exception ex) { Main.Log("[GD Merge][AllowedSB] Final summary error: " + ex.Message); }
                return finalArr;
            }
            catch (Exception ex) { Main.Log("[GD Merge] BuildDynamicAllowedSpellbooks error: " + ex.Message); }
            return Array.Empty<BlueprintSpellbookReference>();
        }

        // 选择限制补丁（扩展龙血识别，覆盖混血术士主/副血）
        [HarmonyPatch(typeof(BlueprintFeatureSelectMythicSpellbook), nameof(BlueprintFeatureSelectMythicSpellbook.CanSelect))]
        private static class CanSelectPatch
        {
            private static readonly Regex RxRequisite = new Regex("^Draconic.*BloodlineRequisiteFeature$", RegexOptions.Compiled);
            private static bool NameLooksDraconic(string n)
            {
                if (string.IsNullOrEmpty(n)) return false;
                // 覆盖：普通术士、混血副血、探索者等命名
                if (n.IndexOf("Draconic", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Bloodline", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("CrossbloodedSecondaryBloodlineDraconic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("SeekerBloodlineDraconic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (RxRequisite.IsMatch(n)) return true;
                return false;
            }
            private static bool HasDraconic(UnitDescriptor unit)
            {
                if (unit == null) return false;
                try
                {
                    var guidSet = new HashSet<BlueprintGuid>(DraconicRequisiteGuids);
                    foreach (var fact in unit.Facts.List)
                    {
                        var bp = fact?.Blueprint; if (bp == null) continue; var n = bp.name ?? string.Empty;
                        // 1) 直接按 GUID 判断（包含普通龙血前置特性 + 混血次级龙血进阶）
                        if (guidSet.Contains(bp.AssetGuid))
                        {
                            if (Main.Settings.VerboseLogging) Main.Log($"[GD Merge] Draconic detected by GUID: {n} guid={bp.AssetGuid}");
                            return true;
                        }
                        // 2) 名称兜底（兼容不同命名模式）
                        if (NameLooksDraconic(n))
                        {
                            if (Main.Settings.VerboseLogging) Main.Log($"[GD Merge] Draconic detected by name: {n} guid={bp.AssetGuid}");
                            return true;
                        }
                    }
                }
                catch { }
                return false;
            }
            [HarmonyPostfix]
            static void Postfix(UnitDescriptor unit, object item, BlueprintFeatureSelectMythicSpellbook __instance, ref bool __result)
            {
                if (!__result) return; if (__instance == null || __instance.AssetGuid != GD_MergeFeatureGuid) return; if (!HasDraconic(unit)) { __result = false; return; }
                BlueprintSpellbook chosen = null; try { var p = item?.GetType().GetProperty("Param", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item); chosen = p?.GetType().GetProperty("Blueprint")?.GetValue(p) as BlueprintSpellbook; } catch { }
                if (chosen == null) return; if (chosen.SpellList == null) { __result = false; return; }
                var gdClass = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(GoldenDragonClassGuid);
                if (gdClass?.Spellbook != null && gdClass.Spellbook.AssetGuid == chosen.AssetGuid) { __result = false; return; }
                if (_externalGoldDragonSpellbookPresent && chosen.AssetGuid == BlueprintGuid.Parse(ExternalGoldDragonSpellbookGuid)) { __result = false; return; }
            }
        }

        // 文本覆盖补丁（保持不变）
        [HarmonyPatch]
        private static class TextGetterPatch
        {
            [HarmonyTargetMethods]
            static System.Collections.Generic.IEnumerable<MethodBase> Targets()
            {
                var t = typeof(BlueprintUnitFact);
                var m1 = t.GetMethod("get_DisplayName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var m2 = t.GetMethod("get_Description", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m1 != null && m1.ReturnType == typeof(string)) yield return m1;
                if (m2 != null && m2.ReturnType == typeof(string)) yield return m2;
            }
            static void Postfix(BlueprintUnitFact __instance, ref string __result, MethodBase __originalMethod)
            {
                try
                {
                    if (__instance == null) return;
                    if (__instance.AssetGuid != GD_MergeFeatureGuid) return;
                    if (__originalMethod.Name == "get_DisplayName") __result = GD_Name; else if (__originalMethod.Name == "get_Description") __result = GD_Desc;
                }
                catch { }
            }
        }

        private static void SetGuid(SimpleBlueprint bp, BlueprintGuid guid)
        {
            try
            {
                var f = bp.GetType().GetField("AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? bp.GetType().GetField("m_AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? bp.GetType().GetField("m_Guid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) f.SetValue(bp, guid);
            }
            catch (Exception ex) { Main.Log("[GD Merge] SetGuid error: " + ex.Message); }
        }

        private static BlueprintGuid ExtractAbilityReferenceGuid(object abilityRef)
        {
            if (abilityRef == null) return BlueprintGuid.Empty;
            try
            {
                if (abilityRef is BlueprintAbility ability)
                    return ability.AssetGuid;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var t = abilityRef.GetType();
                string[] fieldNames = { "m_Guid", "m_AssetGuid", "guid", "m_BlueprintGuid", "m_Id" };
                foreach (var fn in fieldNames)
                {
                    var f = t.GetField(fn, flags);
                    if (f != null)
                    {
                        var val = f.GetValue(abilityRef);
                        if (val is BlueprintGuid bg && bg != BlueprintGuid.Empty) return bg;
                    }
                }
                string[] propNames = { "Guid", "AssetGuid", "BlueprintGuid" };
                foreach (var pn in propNames)
                {
                    var p = t.GetProperty(pn, flags);
                    if (p != null && p.GetIndexParameters().Length == 0)
                    {
                        var val = p.GetValue(abilityRef, null);
                        if (val is BlueprintGuid bg && bg != BlueprintGuid.Empty) return bg;
                    }
                }
                var mGetGuid = t.GetMethod("get_Guid", flags);
                if (mGetGuid != null && mGetGuid.ReturnType == typeof(BlueprintGuid))
                {
                    var val = mGetGuid.Invoke(abilityRef, null);
                    if (val is BlueprintGuid bg && bg != BlueprintGuid.Empty) return bg;
                }
                var mGet = t.GetMethod("Get", flags, null, Type.EmptyTypes, null);
                if (mGet != null)
                {
                    var got = mGet.Invoke(abilityRef, null);
                    if (got is BlueprintAbility ba2 && ba2.AssetGuid != BlueprintGuid.Empty)
                        return ba2.AssetGuid;
                }
            }
            catch { }
            return BlueprintGuid.Empty;
        }
        private static BlueprintGuid GetAbilityRefGuid(object abilityRef) => ExtractAbilityReferenceGuid(abilityRef);

        private static void AddComponent(BlueprintScriptableObject bp, BlueprintComponent comp)
        {
            try
            {
                var t = typeof(BlueprintScriptableObject);
                var f = t.GetField("m_Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                      ?? t.GetField("Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var arr = (f?.GetValue(bp) as BlueprintComponent[]) ?? Array.Empty<BlueprintComponent>();
                var newArr = arr.Concat(new[] { comp }).ToArray();
                f?.SetValue(bp, newArr);
            }
            catch { }
        }

        // 动态枚举蓝图库，收集所有“龙系血统前置/必备”特性（包含混血术士的副血）
        private static IEnumerable<BlueprintFeature> FindDraconicRequisiteFeatures()
        {
            var result = new List<BlueprintFeature>();
            try
            {
                var cache = ResourcesLibrary.BlueprintsCache; if (cache == null) return result;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                foreach (var f in cache.GetType().GetFields(flags))
                {
                    var ft = f.FieldType;
                    if (!ft.IsGenericType || ft.GetGenericTypeDefinition() != typeof(Dictionary<,>)) continue;
                    var args = ft.GetGenericArguments();
                    if (args.Length != 2 || args[0] != typeof(BlueprintGuid) || !typeof(SimpleBlueprint).IsAssignableFrom(args[1])) continue;
                    var dict = f.GetValue(cache) as System.Collections.IDictionary; if (dict == null) continue;
                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        if (entry.Value is BlueprintFeature feat)
                        {
                            var n = feat.name ?? string.Empty;
                            // 扩展匹配：去除对 "Requisite" 关键词的强制要求，以捕获 Crossblooded 次级龙血等进阶特性。
                            // 逻辑：凡名称同时包含 Draconic 与 Bloodline 即视为龙系血统相关；另外保留特例（Crossblooded / Seeker）。
                            bool looksDraconic =
                                (n.IndexOf("Draconic", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Bloodline", StringComparison.OrdinalIgnoreCase) >= 0)
                                || (n.IndexOf("CrossbloodedSecondaryBloodline", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Draconic", StringComparison.OrdinalIgnoreCase) >= 0)
                                || (n.IndexOf("SeekerBloodlineDraconic", StringComparison.OrdinalIgnoreCase) >= 0);
                            if (looksDraconic)
                            {
                                // 排除无效/重复
                                if (!result.Any(r => r.AssetGuid == feat.AssetGuid)) result.Add(feat);
                                if (Main.Settings.VerboseLogging)
                                    Main.Log("[GD Merge] (+Dyn) Draconic match added: name=" + n + " guid=" + feat.AssetGuid);
                            }
                        }
                    }
                }
                if (Main.Settings.VerboseLogging) Main.Log("[GD Merge] Dynamic draconic feature total=" + result.Count);
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[GD Merge] FindDraconicRequisiteFeatures error: " + ex.Message);
            }
            return result;
        }
    }
}
