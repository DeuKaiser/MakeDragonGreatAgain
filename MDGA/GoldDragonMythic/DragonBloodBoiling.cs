using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.Root;
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Localization;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp;
using Kingmaker.UnitLogic.Class;
using MDGA.Loc;

namespace MDGA.GoldDragonMythic
{
    /// <summary>
    /// “龙血沸腾”实现（方案A）：
    /// - 在金龙神话2级发放一个隐藏被动特性，用于识别并放宽等级上限到30级（需拥有龙脉血承）。
    /// - Patch LevelUpController.GetEffectiveLevel：拥有该特性时，使用扩展经验阈值并允许逐级提升至30级。
    /// - Patch LevelUpController.CanLevelUp：拥有该特性时，依据单位ExperienceTable判断下一阈值，严格控制单级升级（<=30）。
    /// </summary>
    [HarmonyPatch]
    internal static class DragonBloodBoiling
    {
        private static readonly BlueprintGuid FeatureGuid = BlueprintGuid.Parse("fe3a7f0af0f541d1a9b2b8f7cd5f88ab");
        private const string GoldenDragonProgressionGuid = "a6fbca43902c6194c947546e89af64bd"; // 金龙进阶（仅3级，对应全局8/9/10）

        private static bool _installed;
        private const int DragonCap = 30; // 目标突破等级上限
        private static BlueprintStatProgression _xpDragonCap; // 缓存的扩展经验表（至30级）
        // 仅在拥有“龙脉血承”时生效：与 DragonBloodActivation 中使用的判定保持一致
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
        };

        [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
        private static class CacheInit
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (_installed) return; _installed = true;
                if (!Main.Enabled) return;
                try
                {
                    var feature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(FeatureGuid);
                    if (feature == null)
                    {
                        feature = CreateFeature();
                        Register(feature);
                    }
                    InjectToGoldenDragonLevel2(feature);
                    // 关键：扩展经验阈值到30级，避免阈值不增导致连续升级
                    EnsureExtendedXPTable();
                }
                catch (Exception ex)
                {
                    Main.Log("[DragonBloodBoiling] Init exception: " + ex);
                }
            }
        }

        private static BlueprintFeature CreateFeature()
        {
            BlueprintFeature bp;
            try { bp = (BlueprintFeature)Activator.CreateInstance(typeof(BlueprintFeature)); }
            catch { bp = (BlueprintFeature)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintFeature)); }
            bp.name = "MDGA_DragonBloodBoiling";
            SetGuid(bp, FeatureGuid);
            bp.IsClassFeature = true;
            bp.Ranks = 1;
            // 本地化（中英）
            try
            {
                const string nameZh = "龙血沸腾";
                const string descZh = "进入金龙神话道途2以后，在龙神的注视下，凡躯中的稀薄龙血被点燃，将躯体改造为能够承受高贵龙族精神的神性躯体，将突破职业等级的极限到30级，仅在拥有龙脉血承时生效。";
                const string nameEn = "Dragon Blood Boiling";
                const string descEn = "Upon reaching Golden Dragon mythic rank 2, the faint draconic blood within your mortal body ignites under the Dragon God's gaze, reshaping you into a vessel worthy of noble draconic spirit. Your character level cap increases to 30. This effect only applies if you possess a draconic bloodline inheritance.";

                LocalizationInjector.RegisterDynamicKey("MDGA_DragonBloodBoiling_Name_zh", nameZh);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonBloodBoiling_Desc_zh", descZh);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonBloodBoiling_Name_en", nameEn);
                LocalizationInjector.RegisterDynamicKey("MDGA_DragonBloodBoiling_Desc_en", descEn);

                var fName = typeof(BlueprintUnitFact).GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var fDesc = typeof(BlueprintUnitFact).GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var nameLoc = Activator.CreateInstance(fName.FieldType);
                var descLoc = Activator.CreateInstance(fDesc.FieldType);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                bool zh = IsChinese();
                nameLoc.GetType().GetField("m_Key", flags)?.SetValue(nameLoc, zh ? "MDGA_DragonBloodBoiling_Name_zh" : "MDGA_DragonBloodBoiling_Name_en");
                nameLoc.GetType().GetField("m_Text", flags)?.SetValue(nameLoc, zh ? nameZh : nameEn);
                descLoc.GetType().GetField("m_Key", flags)?.SetValue(descLoc, zh ? "MDGA_DragonBloodBoiling_Desc_zh" : "MDGA_DragonBloodBoiling_Desc_en");
                descLoc.GetType().GetField("m_Text", flags)?.SetValue(descLoc, zh ? descZh : descEn);
                fName.SetValue(bp, nameLoc);
                fDesc.SetValue(bp, descLoc);
                LocalizationInjector.EnsureInjected();
            }
            catch { }

            return bp;
        }

        private static void Register(SimpleBlueprint bp)
        {
            try { ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp); }
            catch (Exception ex) { Main.Log("[DragonBloodBoiling] Register error: " + ex.Message); }
        }

        private static void InjectToGoldenDragonLevel2(BlueprintFeature feature)
        {
            try
            {
                var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(GoldenDragonProgressionGuid);
                if (prog == null) { Main.Log("[DragonBloodBoiling] Golden Dragon progression not found."); return; }
                var target = prog.LevelEntries?.FirstOrDefault(le => le.Level == 2); // 金龙第2级（全局MR=9）
                if (target == null) { Main.Log("[DragonBloodBoiling] Level 2 entry missing in Golden Dragon progression."); return; }

                var fi = typeof(LevelEntry).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var raw = fi?.GetValue(target);
                var list = raw is BlueprintFeatureBaseReference[] arr ? arr.ToList() : raw as System.Collections.Generic.List<BlueprintFeatureBaseReference> ?? new System.Collections.Generic.List<BlueprintFeatureBaseReference>();
                if (!list.Any(r => r?.Get()?.AssetGuid == feature.AssetGuid))
                {
                    list.Add(feature.ToReference<BlueprintFeatureBaseReference>());
                    if (fi.FieldType.IsArray) fi.SetValue(target, list.ToArray()); else fi.SetValue(target, list);
                    Main.Log("[DragonBloodBoiling] Feature injected at Golden Dragon L2.");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DragonBloodBoiling] InjectToProgression error: " + ex.Message);
            }
        }

        // 扩展经验表：保证21~30级阈值严格递增，防止一次性连跳
        private static void EnsureExtendedXPTable()
        {
            try
            {
                var root = BlueprintRoot.Instance; if (root == null) { Main.Log("[DragonBloodBoiling][XP] BlueprintRoot null"); return; }
                var prog = root.Progression; if (prog == null) { Main.Log("[DragonBloodBoiling][XP] Progression null"); return; }
                var xpTable = prog.LegendXPTable ?? prog.XPTable; if (xpTable == null) { Main.Log("[DragonBloodBoiling][XP] No XP table available"); return; }

                // 读取当前阈值（通过 GetBonus 连续读取，直到不递增）
                var values = new System.Collections.Generic.List<int>();
                int last = 0;
                for (int lvl = 0; lvl <= 60; lvl++) // 安全上限
                {
                    int req;
                    try { req = xpTable.GetBonus(lvl); } catch { break; }
                    if (lvl > 0 && (req <= 0 || req <= last)) { break; }
                    values.Add(req);
                    last = req;
                }
                if (values.Count == 0) { Main.Log("[DragonBloodBoiling][XP] Could not read XP table values"); return; }

                int beforeMax = values.Count - 1;
                // 需要至少到20级，且在21级处存在缺口或不递增，才进行扩展
                if (beforeMax >= DragonCap && values[Math.Min(DragonCap, values.Count - 1)] > values[Math.Min(DragonCap - 1, values.Count - 2)])
                {
                    Main.Log("[DragonBloodBoiling][XP] Table already >= L" + DragonCap + ". No extend.");
                    return;
                }

                // 若不足 21，先保证有到20的值
                if (beforeMax < 20)
                {
                    Main.Log($"[DragonBloodBoiling][XP] Table too short (max={beforeMax}). Will extend from last available.");
                }

                int startIdx = System.Math.Min(System.Math.Max(1, values.Count - 1), 20); // 从已知最后一个或20开始
                int basePrev = values[startIdx - 1];
                int baseCurr = values[startIdx];
                int delta = System.Math.Max(100000, baseCurr - basePrev); // 合理下限，避免0或负增量

                // 增长倍率，随级数轻微增加
                double growth = 1.25; // 可调：1.20~1.35
                int cur = values[startIdx];
                for (int lvl = startIdx + 1; lvl <= DragonCap; lvl++)
                {
                    // 逐级放大增量
                    delta = (int)System.Math.Ceiling(delta * growth);
                    cur += delta;
                    // 确保严格递增且不溢出
                    if (values.Count > lvl) values[lvl] = cur; else values.Add(cur);
                }

                // 写回底层数组/列表字段
                if (TryApplyXpArray(xpTable, values.ToArray()))
                {
                    Main.Log($"[DragonBloodBoiling][XP] Extended XP table to L{DragonCap}. L20={values[20]}, L21={values[21]}, L{DragonCap}={values[DragonCap]}");
                }
                else
                {
                    Main.Log("[DragonBloodBoiling][XP] Failed to set XP table via reflection.");
                }
            }
            catch (System.Exception ex)
            {
                Main.Log("[DragonBloodBoiling][XP] Exception: " + ex.Message);
            }
        }

        private static bool TryApplyXpArray(object xpTable, int[] newVals)
        {
            try
            {
                var t = xpTable.GetType();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // 常见字段名尝试
                var candidates = new[] { "m_Bonuses", "Bonuses", "m_Experience", "Experience", "m_ExperienceTable", "ExperienceTable", "m_Levels", "Levels" };
                foreach (var name in candidates)
                {
                    var f = t.GetField(name, flags);
                    if (f == null) continue;
                    var ft = f.FieldType;
                    if (ft == typeof(int[])) { f.SetValue(xpTable, newVals); return true; }
                    if (typeof(System.Collections.IList).IsAssignableFrom(ft))
                    {
                        var list = f.GetValue(xpTable) as System.Collections.IList;
                        if (list == null) { list = (System.Collections.IList)System.Activator.CreateInstance(ft); }
                        list.Clear();
                        foreach (var v in newVals) list.Add(v);
                        f.SetValue(xpTable, list);
                        return true;
                    }
                }

                // 属性尝试
                var pcandidates = new[] { "Bonuses", "Experience", "ExperienceTable", "Levels" };
                foreach (var name in pcandidates)
                {
                    var p = t.GetProperty(name, flags);
                    if (p == null || !p.CanWrite) continue;
                    if (p.PropertyType == typeof(int[])) { p.SetValue(xpTable, newVals); return true; }
                    if (typeof(System.Collections.IList).IsAssignableFrom(p.PropertyType))
                    {
                        var list = p.GetValue(xpTable) as System.Collections.IList;
                        if (list == null) { list = (System.Collections.IList)System.Activator.CreateInstance(p.PropertyType); }
                        list.Clear();
                        foreach (var v in newVals) list.Add(v);
                        p.SetValue(xpTable, list);
                        return true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.Log("[DragonBloodBoiling][XP] TryApplyXpArray error: " + ex.Message);
            }
            return false;
        }

        private static bool HasBoiling(UnitDescriptor unit)
        {
            try
            {
                if (unit == null) return false;
                var feats = unit.Progression?.Features; if (feats == null) return false;
                var factBp = ResourcesLibrary.TryGetBlueprint<BlueprintUnitFact>(FeatureGuid);
                if (factBp == null) return false;
                if (!feats.HasFact(factBp)) return false; // 先要求拥有龙血沸腾特性
                // 再检查是否拥有任意龙脉血承（Draconic血承 GUID 列表）
                foreach (var g in DraconicRequisiteGuids)
                {
                    var bp = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(g);
                    if (bp != null && feats.HasFact(bp)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        // 为拥有“龙血沸腾”的单位，直接覆盖其 ExperienceTable 与 MaxCharacterLevel（仿 ToyBox 思路）
        [HarmonyPatch(typeof(UnitProgressionData), nameof(UnitProgressionData.ExperienceTable), MethodType.Getter)]
        private static class UnitProgressionData_ExperienceTable_Patch
        {
            private static bool Prefix(UnitProgressionData __instance, ref BlueprintStatProgression __result)
            {
                try
                {
                    var owner = __instance?.Owner; if (owner == null) return true;
                    // Owner 已经是 UnitDescriptor，不存在 Descriptor 属性，直接传入
                    if (!HasBoiling(owner)) return true; // 非本特性：走原逻辑
                    if (_xpDragonCap == null) _xpDragonCap = BuildDragonXpCap();
                    __result = _xpDragonCap;
                    return false; // 拦截原实现
                }
                catch { return true; }
            }
        }

        [HarmonyPatch(typeof(UnitProgressionData), nameof(UnitProgressionData.MaxCharacterLevel), MethodType.Getter)]
        private static class UnitProgressionData_MaxCharacterLevel_Patch
        {
            private static bool Prefix(UnitProgressionData __instance, ref int __result)
            {
                try
                {
                    var owner = __instance?.Owner; if (owner == null) return true;
                    if (!HasBoiling(owner)) return true;
                    __result = DragonCap;
                    return false;
                }
                catch { return true; }
            }
        }

        private static BlueprintStatProgression BuildDragonXpCap()
        {
            try
            {
                // 连续增长：沿用 ToyBox 模式，截断到30级（索引0..30）。
                int[] full = new int[] {
                    0,0,2000,5000,9000,15000,23000,35000,51000,75000,105000,155000,220000,315000,445000,635000,890000,1300000,1800000,2550000,
                    3600000,4650000,5700000,6750000,7800000,8850000,9900000,10950000,12000000,13050000,14100000
                };
                int[] bonuses = full.Take(DragonCap + 1).ToArray(); // 0..30 共31项
                var xp = new BlueprintStatProgression();
                // 直接设置 Bonuses 属性（该类型在游戏中只用 Bonuses 读取）
                var prop = typeof(BlueprintStatProgression).GetProperty("Bonuses", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite) prop.SetValue(xp, bonuses);
                else
                {
                    var f = typeof(BlueprintStatProgression).GetField("Bonuses", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             ?? typeof(BlueprintStatProgression).GetField("m_Bonuses", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    f?.SetValue(xp, bonuses);
                }
                return xp;
            }
            catch
            {
                // 兜底：若反射失败，返回根表，虽不理想但避免崩溃
                return Game.Instance?.BlueprintRoot?.Progression?.XPTable ?? Game.Instance?.BlueprintRoot?.Progression?.LegendXPTable;
            }
        }

        // 放宽有效等级到30
        [HarmonyPatch(typeof(LevelUpController), nameof(LevelUpController.GetEffectiveLevel))]
        private static class Patch_GetEffectiveLevel
        {
            [HarmonyPostfix]
            private static void Postfix(UnitEntityData unit, ref int __result)
            {
                // 改为不回溯按总经验直接提升多级，避免“一口气升到30级”。
                // 保留原生逐级升级节奏，仅在拥有特性时对结果做上限保护（不超过30）。
                try
                {
                    if (unit == null)
                    {
                        UnitEntityData main = null;
                        try { main = Game.Instance.Player.MainCharacter.Value; } catch { }
                        unit = main;
                    }
                    if (unit == null) return;
                    if (!HasBoiling(unit.Descriptor)) return;
                    if (__result > DragonCap) __result = DragonCap; // feature 生效时把有效等级上限钳到30
                    // 不再根据 XP 回推并连跳多级；让 CanLevelUp 控制“是否还能升一级”。
                }
                catch { }
            }
        }

        // 放宽可升级判断（使用Legend表，允许升至30）
        [HarmonyPatch(typeof(LevelUpController), nameof(LevelUpController.CanLevelUp))]
        private static class Patch_CanLevelUp
        {
            [HarmonyPostfix]
            private static void Postfix(UnitDescriptor unit, bool isNext, ref bool __result)
            {
                // 精简逻辑：仅在拥有特性且尚未到30级、XP 达到下一阈值时允许升级；不做多级回溯。
                try
                {
                    if (unit == null) return;
                    if (!HasBoiling(unit)) return;          // 没有特性：不干预原结果
                    if (unit.Progression.CharacterLevel >= DragonCap) { __result = false; return; }
                    if (Game.Instance?.Player?.IsInCombat == true) return;
                    if (unit.State?.IsDead == true) return;

                    // 关键：用单位自己的 ExperienceTable（会触发我们的 getter 覆盖），保证与UI阈值一致
                    var xpTable = unit.Progression?.ExperienceTable
                                   ?? BlueprintRoot.Instance?.Progression?.LegendXPTable
                                   ?? BlueprintRoot.Instance?.Progression?.XPTable;
                    if (xpTable == null) return;
                    int lvl = unit.Progression.CharacterLevel;
                    int nextReq = xpTable.GetBonus(lvl + 1);
                    int currReq = xpTable.GetBonus(lvl);
                    if (nextReq <= 0 || nextReq <= currReq) { __result = false; return; } // 不递增：强制不给升，防止表未扩展或异常
                    // 若经验未达到下一阈值，则无论原结果如何都关掉升级按钮
                    if (unit.Progression.Experience < nextReq) { __result = false; return; }
                    // 经验满足则放行（只放行一阶）
                    __result = true;
                }
                catch { }
            }
        }

        private static void SetGuid(SimpleBlueprint bp, BlueprintGuid guid)
        {
            try
            {
                var f = bp.GetType().GetField("AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                      ?? bp.GetType().GetField("m_AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                      ?? bp.GetType().GetField("m_Guid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                f?.SetValue(bp, guid);
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
            try
            {
                var ci = System.Globalization.CultureInfo.CurrentUICulture;
                if (ci != null && ci.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            try
            {
                if (UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.ChineseSimplified ||
                    UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.Chinese ||
                    UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.ChineseTraditional)
                    return true;
            }
            catch { }
            return false;
        }
    }
}
