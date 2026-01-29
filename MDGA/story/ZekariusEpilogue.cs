using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.AreaLogic.Etudes;
using Kingmaker.ElementsSystem;
using Kingmaker.Localization;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Designers.EventConditionActionSystem.Conditions;
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UnitLogic;
using Kingmaker.Blueprints.Quests; // added for Quest/Objective blueprints
using Kingmaker.AreaLogic.QuestSystem; // added for QuestState/QuestObjectiveState
using UnityEngine; // for MonoBehaviour delayed runner

namespace MDGA.Story
{
    // 巫妖导师泽卡琉斯结局改写雏形
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class ZekariusEpilogue
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (!Main.Enabled) return;

            try
            {
                // 1) 先从磁盘读取我们自带的 jbp JSON 并反序列化为蓝图对象，然后注册到蓝图库
                TryRegisterCustomEpilogues();

                // 在蓝图层添加条件（当前暂时清空以排查）
                TryAddBlueprintConditions();

                // 额外诊断：
                TryDumpCompletionConditions();
                TryDumpStoryPrereqs();

                // 暂停插入行为，保留蓝图与诊断，等待后续研究
                Main.Log("[ZekariusEpilogue] Insertion disabled by request; no changes to target page cues.");
                // TryInsertCustomCueIntoPage();
            }
            catch (System.Exception ex)
            {
                Main.Log("[ZekariusEpilogue] Error: " + ex);
            }
        }

        // 从 jbp 文件加载并反序列化为蓝图对象，然后注册到蓝图库
        private static void TryRegisterCustomEpilogues()
        {
            try
            {
                var modPath = Main.ModEntry?.Path;
                if (string.IsNullOrEmpty(modPath)) return;

                var baseDir = Path.Combine(modPath, "Blueprints", "CustomEpilogues");
                // 分段 Cue 路径
                var cueP1 = Path.Combine(baseDir, "MDGA_ZekariusEpilogueCueP1.jbp");
                var cueP2 = Path.Combine(baseDir, "MDGA_ZekariusEpilogueCueP2.jbp");
                var cueP3 = Path.Combine(baseDir, "MDGA_ZekariusEpilogueCueP3.jbp");
                var cueP4 = Path.Combine(baseDir, "MDGA_ZekariusEpilogueCueP4.jbp");
                var cueP5 = Path.Combine(baseDir, "MDGA_ZekariusEpilogueCueP5.jbp");
                var cueP6 = Path.Combine(baseDir, "MDGA_ZekariusEpilogueCueP6.jbp");
                // 自定义页与继续答案
                var pagePath = Path.Combine(baseDir, "MDGA_ZekariusEpiloguePage.jbp");
                var answerPath = Path.Combine(baseDir, "MDGA_ZekariusEpilogueAnswer.jbp");

                // NOTE: Runtime registration of .jbp via Json deserialization is not save-safe in WotR.
                // It can instantiate engine ScriptableObjects (e.g., SharedStringAsset) using 'new',
                // which leads to serialization/save issues.
                // Keep the files on disk for future tooling, but do not register them at runtime.
                Main.Log("[ZekariusEpilogue] Custom epilogue blueprint registration is disabled (save-stability safeguard).");
            }
            catch (Exception ex)
            {
                Main.Log("[ZekariusEpilogue] TryRegisterCustomEpilogues exception: " + ex.Message);
            }
        }

        private static void RegisterSingleBlueprintFromJson<T>(string fullPath) where T : SimpleBlueprint
        {
            try
            {
                if (!File.Exists(fullPath)) { Main.Log("[ZekariusEpilogue] File not found: " + fullPath); return; }
                var json = File.ReadAllText(fullPath);
                var jObj = JObject.Parse(json);
                var assetIdStr = jObj["AssetId"]?.ToString();
                var dataToken = jObj["Data"];
                if (string.IsNullOrEmpty(assetIdStr) || dataToken == null)
                {
                    Main.Log("[ZekariusEpilogue] Invalid jbp content: missing AssetId/Data in " + fullPath);
                    return;
                }

                var reader = dataToken.CreateReader();
                var bp = (T)Json.Serializer.Deserialize(reader, typeof(T));
                if (bp == null)
                {
                    Main.Log("[ZekariusEpilogue] Deserialize failed for " + fullPath);
                    return;
                }

                BlueprintGuid guid;
                if (!Guid.TryParse(assetIdStr, out var gnet))
                {
                    var trimmed = assetIdStr.Replace("!bp_", string.Empty);
                    if (!Guid.TryParse(trimmed, out gnet))
                    {
                        Main.Log("[ZekariusEpilogue] Invalid GUID in AssetId: " + assetIdStr);
                        return;
                    }
                }
                guid = new BlueprintGuid(gnet);
                try
                {
                    var f = bp.GetType().GetField("AssetGuid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                          ?? bp.GetType().GetField("m_AssetGuid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    f?.SetValue(bp, guid);
                }
                catch { }

                ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(guid, bp);
                Main.Log($"[ZekariusEpilogue] Registered {typeof(T).Name} name={bp.name} guid={guid}");
            }
            catch (Exception ex)
            {
                Main.Log("[ZekariusEpilogue] RegisterSingleBlueprintFromJson error: " + ex.Message);
            }
        }

        private static readonly BlueprintGuid ApsuDeityGuid           = BlueprintGuid.Parse("772e2673945e4583a804ae01f67efea0");
        // 特伦笛利弗救赎完成 Etude（TerendelevAlive_Dragon）
        private static readonly BlueprintGuid TrendelevSavedEtudeGuid = BlueprintGuid.Parse("6c31fca14a4c4fb499f5eccee5eda148");
        // 塞瓦罗斯变成银龙并飞走（SevalrosFlyAsSilver）
        private static readonly BlueprintGuid SevalrosSavedEtudeGuid  = BlueprintGuid.Parse("04b61fb2199493a40880b43a40ad68a6");
        private static readonly BlueprintGuid TargetBookPageGuid      = BlueprintGuid.Parse("85576114d956fb443aa76cccb9ee2031"); // 金龙哈拉塞利亚斯结局书页
        private static readonly BlueprintGuid DefaultCueGuid          = BlueprintGuid.Parse("cdadfdf03caf2be4ea24753d8163208d"); // 金龙哈拉塞利亚斯结局内容

        // 新增：我们注册的新整页与“继续”答案 GUID
        private static readonly BlueprintGuid ZekariusPageGuid        = BlueprintGuid.Parse("9f1c9c22-5c83-4c04-9f2f-2b4a9b4b6c7e");
        private static readonly BlueprintGuid ContinueAnswerGuid      = BlueprintGuid.Parse("5c60cb734b268f640aec357ad4754ef4");

        // 新增：通灵塔叛乱任务（Errand）与其唯一目标（m_FinishParent=true）
        private static readonly BlueprintGuid CleanZigguratQuestGuid     = BlueprintGuid.Parse("93edc5e0a867b474fbc65b38ac1655dc");
        private static readonly BlueprintGuid CleanZigguratObjectiveGuid = BlueprintGuid.Parse("265ec37167da4a14a9b8bd54a625e7f0");

        private static bool _insertDone; // 仅插入一次

        private static bool HasValidGuids()
        {
            return TargetBookPageGuid != BlueprintGuid.Empty &&
                   TrendelevSavedEtudeGuid != BlueprintGuid.Empty &&
                   SevalrosSavedEtudeGuid != BlueprintGuid.Empty &&
                   ApsuDeityGuid != BlueprintGuid.Empty;
        }

        // 诊断输出：列出目标书页与相关答案/提示的条件结构，便于确认完成条件是否写错
        private static void TryDumpCompletionConditions()
        {
            try
            {
                var page = ResourcesLibrary.TryGetBlueprint<BlueprintBookPage>(TargetBookPageGuid);
                var cue = ResourcesLibrary.TryGetBlueprint<BlueprintCue>(DefaultCueGuid);
                if (page == null)
                {
                    Main.Log("[ZekariusEpilogue][Diag] Target page not found; skip condition dump.");
                    return;
                }
                Main.Log($"[ZekariusEpilogue][Diag] Dump conditions for page={page.name} ({page.AssetGuid})");

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Dump page.OnShow conditions if present
                try
                {
                    var onShow = page.OnShow;
                    if (onShow != null)
                    {
                        var fiCond = typeof(ActionList).GetField("ConditionsChecker", flags) ?? typeof(ActionList).GetField("m_ConditionsChecker", flags);
                        var checker = fiCond?.GetValue(onShow);
                        DumpConditionsChecker("page.OnShow", checker, flags);
                    }
                }
                catch (Exception ex) { Main.Log("[ZekariusEpilogue][Diag] page.OnShow cond dump error: " + ex.Message); }

                // Dump each answer's conditions
                try
                {
                    var answersRefs = page.Answers ?? new List<BlueprintAnswerBaseReference>();
                    foreach (var ar in answersRefs)
                    {
                        var a = ar?.Get() as BlueprintAnswer; if (a == null) continue;
                        Main.Log($"[ZekariusEpilogue][Diag] Answer {a.name} ({a.AssetGuid})");
                        var fiCond = typeof(BlueprintAnswer).GetField("Conditions", flags) ?? typeof(BlueprintAnswer).GetField("m_Conditions", flags);
                        var checker = fiCond?.GetValue(a);
                        DumpConditionsChecker("answer.Conditions", checker, flags);
                    }
                }
                catch (Exception ex) { Main.Log("[ZekariusEpilogue][Diag] answers cond dump error: " + ex.Message); }

                // Dump cue conditions
                try
                {
                    if (cue != null)
                    {
                        Main.Log($"[ZekariusEpilogue][Diag] Cue {cue.name} ({cue.AssetGuid})");
                        var fiCond = typeof(BlueprintCueBase).GetField("Conditions", flags) ?? typeof(BlueprintCueBase).GetField("m_Conditions", flags);
                        var checker = fiCond?.GetValue(cue);
                        DumpConditionsChecker("cue.Conditions", checker, flags);
                    }
                }
                catch (Exception ex) { Main.Log("[ZekariusEpilogue][Diag] cue cond dump error: " + ex.Message); }
            }
            catch (Exception ex)
            {
                Main.Log("[ZekariusEpilogue][Diag] TryDumpCompletionConditions error: " + ex.Message);
            }
        }

        private static void DumpConditionsChecker(string owner, object checker, BindingFlags flags)
        {
            try
            {
                if (checker == null)
                {
                    Main.Log($"[ZekariusEpilogue][Diag] {owner}: no ConditionsChecker.");
                    return;
                }
                var fiConds = checker.GetType().GetField("Conditions", flags) ?? checker.GetType().GetField("m_Conditions", flags);
                var arr = fiConds?.GetValue(checker) as Condition[];
                var fiPolicy = checker.GetType().GetField("Operation", flags) ?? checker.GetType().GetField("m_Operation", flags);
                var op = fiPolicy?.GetValue(checker);
                Main.Log($"[ZekariusEpilogue][Diag] {owner}: op={(op?.ToString() ?? "<null>")} count={(arr?.Length ?? 0)}");
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var c = arr[i]; if (c == null) continue;
                        Main.Log($"[ZekariusEpilogue][Diag]   [{i}] {c.GetType().FullName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log($"[ZekariusEpilogue][Diag] DumpConditionsChecker error on {owner}: " + ex.Message);
            }
        }

        // 额外诊断：输出剧情前置是否满足（是否信仰阿普苏、两条龙的救赎）
        private static void TryDumpStoryPrereqs()
        {
            try
            {
                var apsu = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(ApsuDeityGuid);
                var trenEtude = ResourcesLibrary.TryGetBlueprint<BlueprintEtude>(TrendelevSavedEtudeGuid);
                var sevalEtude = ResourcesLibrary.TryGetBlueprint<BlueprintEtude>(SevalrosSavedEtudeGuid);
                Main.Log($"[ZekariusEpilogue][Diag] Prereq Blueprints ready: apsu={(apsu!=null)} trenEtude={(trenEtude!=null)} sevalEtude={(sevalEtude!=null)}");

                var game = Game.Instance;
                if (game == null || game.Player == null || game.Player.MainCharacter == null)
                {
                    Main.Log("[ZekariusEpilogue][Diag] Game.Player not ready; skip runtime prereq check.");
                    return;
                }

                var unit = game.Player.MainCharacter.Value;
                bool hasApsu = false;
                try { hasApsu = unit?.Descriptor?.HasFact(apsu) == true; } catch { }
                Main.Log($"[ZekariusEpilogue][Diag] IsApsuFollower={hasApsu} (ApsuGuid={ApsuDeityGuid})");
                // 枚举单位事实，打印包含 Apsu 的名称与 GUID，定位 GUID 不匹配问题
                try
                {
                    var facts = unit?.Descriptor?.Facts?.List;
                    if (facts != null)
                    {
                        foreach (var f in facts)
                        {
                            var bp = f?.Blueprint; if (bp == null) continue;
                            var name = bp.name ?? string.Empty;
                            if (name.IndexOf("Apsu", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Main.Log($"[ZekariusEpilogue][Diag] UnitFact hit: {name} guid={bp.AssetGuid}");
                            }
                        }
                    }
                }
                catch (Exception exFacts) { Main.Log("[ZekariusEpilogue][Diag] Enumerate facts error: " + exFacts.Message); }

                // 检查 Etude 是否已播放/完成（用反射避免编译期类型依赖）
                bool trenDone = false, sevalDone = false;
                try
                {
                    // 通过类型名获取 EtudeSystem（避免直接引用导致编译错误）
                    var etudeSystemType = Type.GetType("Kingmaker.AreaLogic.Etudes.EtudeSystem, Assembly-CSharp")
                                             ?? AppDomain.CurrentDomain.GetAssemblies()
                                                 .Select(a => a.GetType("Kingmaker.AreaLogic.Etudes.EtudeSystem", false))
                                                 .FirstOrDefault(t => t != null);
                    if (etudeSystemType != null)
                    {
                        var instProp = etudeSystemType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        var etudeSys = instProp?.GetValue(null);
                        if (etudeSys != null)
                        {
                            var miIsPlayed = etudeSystemType.GetMethod("IsPlayed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(BlueprintEtude) }, null);
                            if (miIsPlayed != null)
                            {
                                if (trenEtude != null) trenDone = (bool)miIsPlayed.Invoke(etudeSys, new object[] { trenEtude });
                                if (sevalEtude != null) sevalDone = (bool)miIsPlayed.Invoke(etudeSys, new object[] { sevalEtude });
                            }
                            else
                            {
                                // 备用：尝试从 EtudesPlayer/PlayedEtudes 中检查
                                var fiPlayer = etudeSystemType.GetField("m_Player", BindingFlags.Instance | BindingFlags.NonPublic) ?? etudeSystemType.GetField("Player", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                var playerObj = fiPlayer?.GetValue(etudeSys);
                                if (playerObj != null)
                                {
                                    var fiPlayed = playerObj.GetType().GetField("m_PlayedEtudes", BindingFlags.Instance | BindingFlags.NonPublic) ?? playerObj.GetType().GetField("PlayedEtudes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    var played = fiPlayed?.GetValue(playerObj) as System.Collections.IEnumerable;
                                    if (played != null)
                                    {
                                        foreach (var item in played)
                                        {
                                            var bpProp = item?.GetType().GetProperty("Blueprint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                            var bp = bpProp?.GetValue(item) as BlueprintEtude;
                                            if (bp == null) continue;
                                            if (trenEtude != null && bp.AssetGuid == trenEtude.AssetGuid) trenDone = true;
                                            if (sevalEtude != null && bp.AssetGuid == sevalEtude.AssetGuid) sevalDone = true;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            Main.Log("[ZekariusEpilogue][Diag] EtudeSystem.Instance is null");
                        }
                    }
                    else
                    {
                        Main.Log("[ZekariusEpilogue][Diag] EtudeSystem type not found via reflection");
                    }
                }
                catch (Exception ex) { Main.Log("[ZekariusEpilogue][Diag] Etude check error: " + ex.Message); }

                Main.Log($"[ZekariusEpilogue][Diag] TrendelevSavedEtudeDone={trenDone} SevalrosSavedEtudeDone={sevalDone}");

                // 附加：输出通灵塔任务与目标的状态
                try
                {
                    var qb = game.Player.QuestBook;
                    var questBp = ResourcesLibrary.TryGetBlueprint<BlueprintQuest>(CleanZigguratQuestGuid);
                    var objBp   = ResourcesLibrary.TryGetBlueprint<BlueprintQuestObjective>(CleanZigguratObjectiveGuid);
                    QuestState qstate = QuestState.None;
                    QuestObjectiveState ostate = QuestObjectiveState.None;
                    if (qb != null)
                    {
                        try { if (questBp != null) qstate = qb.GetQuestState(questBp); } catch { }
                        try { if (objBp != null) ostate = qb.GetObjectiveState(objBp); } catch { }
                    }
                    Main.Log($"[ZekariusEpilogue][Diag] CleanZiggurat questState={qstate} objectiveState={ostate}");
                }
                catch (Exception exQ) { Main.Log("[ZekariusEpilogue][Diag] Quest dump error: " + exQ.Message); }
            }
            catch (Exception ex)
            {
                Main.Log("[ZekariusEpilogue][Diag] TryDumpStoryPrereqs error: " + ex.Message);
            }
        }

        // 参考 WRM 的做法：把自定义结局 Cue 插入到目标书页的 Cues 序列中
        private static void TryInsertCustomCueIntoPage()
        {
            try
            {
                var page = ResourcesLibrary.TryGetBlueprint<BlueprintBookPage>(TargetBookPageGuid);
                var customPage = ResourcesLibrary.TryGetBlueprint<BlueprintBookPage>(ZekariusPageGuid);
                var defaultCue = ResourcesLibrary.TryGetBlueprint<BlueprintCue>(DefaultCueGuid);
                if (page == null)
                {
                    Main.Log("[ZekariusEpilogue] Insert skip: target page not found.");
                    return;
                }

                // 不再做代码层前置检查，交由蓝图上的 Conditions 评估

                // 我们的自定义 Cue 放在独立 Page 中（customPage），实际插入的是该 page 的全部 Cue 引用（按顺序）
                var customRefs = new List<BlueprintCueBaseReference>();
                if (customPage != null && customPage.Cues != null && customPage.Cues.Count > 0)
                {
                    customRefs.AddRange(customPage.Cues);
                }
                else
                {
                    Main.Log("[ZekariusEpilogue] Custom page or its cues missing; nothing to insert.");
                }

                var list = page.Cues ?? new List<BlueprintCueBaseReference>();

                // 查找默认结局 Cue 在当前页中的位置，尽量插入到它前面
                int insertIndex = Math.Min(2, list.Count);
                if (defaultCue != null)
                {
                    var idx = list.FindIndex(r => r?.Get()?.AssetGuid == defaultCue.AssetGuid);
                    if (idx >= 0) insertIndex = Math.Max(0, idx); // 在默认结局前插入
                }

                if (customRefs.Count > 0)
                {
                    int inserted = 0;
                    // 依次插入 6 段，保持顺序，避免重复
                    foreach (var customCueRef in customRefs)
                    {
                        var customGuid = customCueRef?.Get()?.AssetGuid ?? BlueprintGuid.Empty;
                        if (customGuid == BlueprintGuid.Empty) continue;
                        var exists = list.Any(r => r?.Get()?.AssetGuid == customGuid);
                        if (!exists)
                        {
                            list.Insert(insertIndex, customCueRef);
                            insertIndex++; // 后续段落紧跟其后
                            inserted++;
                        }
                    }

                    if (inserted > 0)
                    {
                        page.Cues = list; // 赋回以确保修改生效
                        _insertDone = true;
                        Main.Log($"[ZekariusEpilogue] Inserted {inserted} custom cues starting at index {Math.Max(0, insertIndex - inserted)} on page {page.name}.");
                    }
                    else
                    {
                        _insertDone = true; // 已存在视为完成
                        Main.Log("[ZekariusEpilogue] All custom cues already present; skip.");
                    }
                }
                else
                {
                    Main.Log("[ZekariusEpilogue] No custom cue references to insert; skip.");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[ZekariusEpilogue] TryInsertCustomCueIntoPage error: " + ex.Message);
            }
        }

        // 在蓝图层添加条件：HasFact(Apsu) + EtudeStatus(Trendelev/Sevalros Completed) + ObjectiveStatus(CleanZiggurat objective Completed)
        private static void TryAddBlueprintConditions()
        {
            try
            {
                var page = ResourcesLibrary.TryGetBlueprint<BlueprintBookPage>(ZekariusPageGuid);
                if (page != null)
                {
                    ClearConditionsOnOwner(page);
                }
                var cueGuids = new[]
                {
                    BlueprintGuid.Parse("507E81E3-A0A2-41C3-9C73-5854EA74D3FA"),
                    BlueprintGuid.Parse("121DF189-FBCC-4ED6-9078-C2E5C68237A5"),
                    BlueprintGuid.Parse("EC96E5FD-CA7A-47F6-83E4-7E146C24BEEE"),
                    BlueprintGuid.Parse("D098E711-59C3-4F8E-A237-6129B2F7F4E8"),
                    BlueprintGuid.Parse("037054D2-3FB7-4BFD-B874-EE736B407F60"),
                    BlueprintGuid.Parse("8055BB0A-B16C-48E7-B1F0-0071BC99832B")
                };
                foreach (var gid in cueGuids)
                {
                    var cue = ResourcesLibrary.TryGetBlueprint<BlueprintCue>(gid);
                    if (cue != null) ClearConditionsOnOwner(cue);
                }
                Main.Log("[ZekariusEpilogue] Temporarily cleared all blueprint conditions for gating.");
            }
            catch (Exception ex)
            {
                Main.Log("[ZekariusEpilogue] TryAddBlueprintConditions error: " + ex.Message);
            }
        }

        private static void ClearConditionsOnOwner(object owner)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                object checker = null;
                if (owner is BlueprintBookPage bpPage)
                {
                    var fi = typeof(BlueprintBookPage).GetField("Conditions", flags) ?? typeof(BlueprintBookPage).GetField("m_Conditions", flags);
                    checker = fi?.GetValue(bpPage);
                    if (checker == null)
                    {
                        var tChecker = typeof(ConditionsChecker);
                        checker = Activator.CreateInstance(tChecker);
                        fi?.SetValue(bpPage, checker);
                    }
                }
                else if (owner is BlueprintCue bpCue)
                {
                    var fi = typeof(BlueprintCueBase).GetField("Conditions", flags) ?? typeof(BlueprintCueBase).GetField("m_Conditions", flags);
                    checker = fi?.GetValue(bpCue);
                    if (checker == null)
                    {
                        var tChecker = typeof(ConditionsChecker);
                        checker = Activator.CreateInstance(tChecker);
                        fi?.SetValue(bpCue, checker);
                    }
                }
                if (checker == null) return;

                var opField = checker.GetType().GetField("Operation", flags) ?? checker.GetType().GetField("m_Operation", flags);
                opField?.SetValue(checker, Enum.Parse(opField.FieldType, "And"));
                var condsField = checker.GetType().GetField("Conditions", flags) ?? checker.GetType().GetField("m_Conditions", flags);
                condsField?.SetValue(checker, Array.Empty<Condition>());
            }
            catch { }
        }
    }
}
