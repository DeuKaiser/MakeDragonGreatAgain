using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Encyclopedia;
using Kingmaker.Localization;
using UnityEngine;

namespace MDGA.Loc
{
    internal static partial class LocalizationInjectorExtension { }
    internal static class LocalizationInjector
    {
        private const string WisNameKey = "MDGA.DD.WisdomBonus.Name";
        private const string WisDescKey = "MDGA.DD.WisdomBonus.Desc"; // 旧版描述 Key（保留兼容，实际多级版本使用动态注册）
        private const string ChaNameKey = "MDGA.DD.CharismaBonus.Name";
        private const string ChaDescKey = "MDGA.DD.CharismaBonus.Desc"; // 旧版描述 Key（同上，与多级版本已分离）
        // 泽卡琉斯结局本地化 Key
        private const string ZekariusTitleKey = "MDGA_ZekariusEpilogue_Title";
        private const string ZekariusTextKey  = "MDGA_ZekariusEpilogue_Text";

        //泽卡琉斯结局分段拆分 Key
        private const string ZachariusTextKeyP1 = "MDGA_ZekariusEpilogue_Text_Part1";
        private const string ZachariusTextKeyP2 = "MDGA_ZekariusEpilogue_Text_Part2";
        private const string ZachariusTextKeyP3 = "MDGA_ZekariusEpilogue_Text_Part3";
        private const string ZachariusTextKeyP4 = "MDGA_ZekariusEpilogue_Text_Part4";
        private const string ZachariusTextKeyP5 = "MDGA_ZekariusEpilogue_Text_Part5";
        private const string ZachariusTextKeyP6 = "MDGA_ZekariusEpilogue_Text_Part6";

        private const string ZekariusLichHintKey = "MDGA_Zekarius_LichPathHint";
        private const string ZekariusDragonsHintKey = "MDGA_Zekarius_TerendelevSevalrosHint";
        // 新增：百科页的标题/正文本地化 key
        private const string ZekariusLichHintTitleKey    = "MDGA_Zekarius_LichPathHint_Title";
        private const string ZekariusLichHintTextKey     = "MDGA_Zekarius_LichPathHint_Text";
        private const string ZekariusDragonsHintTitleKey = "MDGA_Zekarius_TerendelevSevalrosHint_Title";
        private const string ZekariusDragonsHintTextKey  = "MDGA_Zekarius_TerendelevSevalrosHint_Text";
        // 金龙结局原始正文 key（需与游戏内实际 key 匹配）
        private const string GoldDragonEpilogueTextKey = "e3d5890a-4d66-44b8-a44f-0869e8c0313d";

        // 基础静态条目：早期简单+2 感知 / +2 魅力占位。后续真正显示用动态条目覆盖，这里仍注入以保证 fallback。
        private static readonly (string key,string text)[] Entries = new (string,string)[]
        {
            // 仅作兜底占位：正常情况下会被 RegisterFeatureLocalization 的动态条目覆盖
            (WisNameKey, "属性增强：感知+2"),
            (ChaNameKey, "属性增强：魅力+2"),

            // 巫妖导师泽卡琉斯结局：标题 & 正文 & 悬停提示说明（兜底中文，占位可被外部本地化覆盖）
            (ZekariusTitleKey, "泽卡琉斯的审判"),
            (ZekariusTextKey,
                // 将原文中的 d| 链接替换为 g| 链接以避免依赖 Traits 解析
                "龙神{g|Apsu}阿卜苏{/g}向来将善意视为不可交易之物，因此从来不会轻易接受信徒的条件，也就是用取悦祂的方式来获得报酬。但龙神会在看到真正的勇气与救赎时给予回应。在龙神的目光里，对首先完成真正的救赎的{name}自己尤为尊敬，在{g|lich}巫妖{/g}之力黑暗的{g|Encyclopedia:MDGA_Zekarius_LichPathHint}洗刷{/g}之下，指挥官的灵魂并没有越陷越深，而是在被浸透的黑暗中抓紧了心中的光芒，浪子回头洗尽铅华，重新回到了善良的阵营。而更加难能可贵的是，在指挥官的帮助之下，两个迷失的{g|Encyclopedia:MDGA_Zekarius_TerendelevSevalrosHint}族人{/g}被拯救，作为一个曾经的温血动物，这样的龙族精神赢得了龙神的尊重，于是一个机会，一个重新选择的机会被赐给了另一个孤独而勇敢的灵魂，大法师{g|Zacarius}泽卡琉斯{/g}，战场上的泽卡琉斯比任何圣教军都更视死如归，而面临死亡，他又比任何人都冷静理智，他驯服死亡为圣战服务的意图差点酿成了黑暗的灾祸，但他的忠诚与誓言为自己赢得了尊重。在等待坟墓女士的审判时的泽卡琉斯得到了一个机会，一个清偿罪孽的机会，来自龙神的力量洗尽了泽卡琉斯灵魂中浸染的黑暗。{n}审判的结果已经落定：泽卡琉斯被允许前往龙神的领域不朽回廊，在龙神双翼的庇护下度过不朽的岁月，做自己想做的一切之事，但被泽卡琉斯拒绝，他希望前往天堂，如果有机会，他希望继续执行圣战，消灭恶魔。{/n}"),
                /*未来可能的期望修改与分段状态：  
                    龙神{g|Apsu}阿卜苏{/g}向来将善意视为不可交易之物，因此从来不会轻易接受信徒的条件，也就是用取悦祂的方式来获得报酬。但龙神会在看到真正的勇气与救赎时给予回应。
                    在龙神的目光里，对首先完成真正救赎的{name}自己尤为尊敬，在{g|lich}巫妖{/g}之力黑暗的{g|Encyclopedia:MDGA_Zekarius_LichPathHint}洗刷{/g}之下，指挥官的灵魂并没有越陷越深，而是在被浸透的黑暗中抓紧了心中的光芒，浪子回头洗尽铅华，重新回到了善良的阵营。
                    而更加难能可贵的是，在指挥官的帮助之下，两个迷失的{g|Encyclopedia:MDGA_Zekarius_TerendelevSevalrosHint}族人{/g}被拯救，作为一个曾经的温血动物，这样的龙族精神赢得了龙神的尊重。
                    于是一个机会，一个重新选择的机会被赐给了另一个孤独而勇敢的灵魂，大法师{g|Zacarius}泽卡琉斯{/g}，战场上的泽卡琉斯比任何圣教军都更视死如归，而面临死亡，他又比任何人都冷静理智，他驯服死亡为圣战服务的意图虽然差点酿成了黑暗的灾祸，但他的忠诚与誓言为自己赢得了尊重。
                    在等待坟墓女士降下审判时的泽卡琉斯得到了一个机会，一个清偿罪孽的机会，龙神的降临的力量洗尽了泽卡琉斯灵魂中浸染的黑暗。这不是交易，龙神从不做交易，这只是一份礼物。
                    坟墓女士在判阁里权衡泽卡琉斯生前与生后所犯下的恶行，研究的禁忌，以及龙神贯穿星界的注释，审判的结果已经落定：泽卡琉斯被允许前往龙神的领域不朽回廊，在龙神双翼的庇护下度过不朽的岁月，无论是做自己的研究还是继续投身于与深渊的战斗。*/
            
            // 巫妖导师泽卡琉斯结局：新正文
            (ZachariusTextKeyP1,
                "龙神{g|Apsu}阿卜苏{/g}向来将善意视为不可交易之物，因此从来不会轻易接受信徒的条件，也就是用取悦祂的方式来获得报酬。但龙神会在看到真正的勇气与救赎时给予回应。"),
            (ZachariusTextKeyP2,
                "在龙神的目光里，对首先完成真正救赎的{name}自己尤为尊敬，在{g|lich}巫妖{/g}之力黑暗的{g|Encyclopedia:MDGA_Zekarius_LichPathHint}洗刷{/g}之下，指挥官的灵魂并没有越陷越深，而是在被浸透的黑暗中抓紧了心中的光芒，浪子回头洗尽铅华，重新回到了善良的阵营。"),
            (ZachariusTextKeyP3,
                "而更加难能可贵的是，在指挥官的帮助之下，两个迷失的{g|Encyclopedia:MDGA_Zekarius_TerendelevSevalrosHint}族人{/g}被拯救，作为一个曾经的温血动物，这样的龙族精神赢得了龙神的尊重。"),
            (ZachariusTextKeyP4,
                "于是一个机会，一个重新选择的机会被赐给了另一个孤独而勇敢的灵魂，大法师{g|Zacarius}泽卡琉斯{/g}，战场上的泽卡琉斯比任何圣教军都更视死如归，而面临死亡，他又比任何人都冷静理智，他驯服死亡为圣战服务的意图虽然差点酿成了黑暗的灾祸，但他的忠诚与誓言为自己赢得了尊重。"),
            (ZachariusTextKeyP5,
                "在等待坟墓女士降下审判时的泽卡琉斯得到了一个机会，一个清偿罪孽的机会，龙神的降临的力量洗尽了泽卡琉斯灵魂中浸染的黑暗。这不是交易，龙神从不做交易，这只是一份礼物。"),
            (ZachariusTextKeyP6,
                "坟墓女士在判阁里权衡泽卡琉斯生前与生后所犯下的恶行，研究的禁忌，以及龙神贯穿星界的注释，审判的结果已经落定：泽卡琉斯被允许前往龙神的领域不朽回廊，在龙神双翼的庇护下度过不朽的岁月，无论是做自己的研究还是继续投身于与深渊的战斗。"),
            //泽卡琉斯完整后记分段整页（同样替换 d| 为 g|）
            ("MDGA_ZekariusEpilogue_FullPage_Part1",
                "龙神{g|Apsu}阿卜苏{/g}向来将善意视为不可交易之物，因此从来不会轻易接受信徒的条件，也就是用取悦祂的方式来获得报酬。但龙神会在看到真正的勇气与救赎时给予回应。"),
            ("MDGA_ZekariusEpilogue_FullPage_Part2",
                "在龙神的目光里，对首先完成真正救赎的{name}自己尤为尊敬，在{g|lich}巫妖{/g}之力黑暗的{g|Encyclopedia:MDGA_Zekarius_LichPathHint}洗刷{/g}之下，指挥官的灵魂并没有越陷越深，而是在被浸透的黑暗中抓紧了心中的光芒，浪子回头洗尽铅华，重新回到了善良的阵营。"),
            ("MDGA_ZekariusEpilogue_FullPage_Part3",
                "而更加难能可贵的是，在指挥官的帮助之下，两个迷失的{g|Encyclopedia:MDGA_Zekarius_TerendelevSevalrosHint}族人{/g}被拯救，作为一个曾经的温血动物，这样的龙族精神赢得了龙神的尊重。"),
            ("MDGA_ZekariusEpilogue_FullPage_Part4",
                "于是一个机会，一个重新选择的机会被赐给了另一个孤独而勇敢的灵魂，大法师{g|Zacarius}泽卡琉斯{/g}，战场上的泽卡琉斯比任何圣教军都更视死如归，而面临死亡，他又比任何人都冷静理智，他驯服死亡为圣战服务的意图虽然差点酿成了黑暗的灾祸，但他的忠诚与誓言为自己赢得了尊重。"),
            ("MDGA_ZekariusEpilogue_FullPage_Part5",
                "在等待坟墓女士降下审判时的泽卡琉斯得到了一个机会，一个清偿罪孽的机会，龙神的降临的力量洗尽了泽卡琉斯灵魂中浸染的黑暗。这不是交易，龙神从不做交易，这只是一份礼物。"),
            ("MDGA_ZekariusEpilogue_FullPage_Part6",
                "坟墓女士在判阁里权衡泽卡琉斯生前与生后所犯下的恶行，研究的禁忌，以及龙神贯穿星界的注释，审判的结果已经落定：泽卡琉斯被允许前往龙神的领域不朽回廊，在龙神双翼的庇护下度过不朽的岁月，无论是做自己的研究还是继续投身于与深渊的战斗。"),


            (ZekariusLichHintKey,
                "你在前期选择了巫妖的神话之路。"),
            (ZekariusDragonsHintKey,
                "你唤醒了不死的特伦德勒夫的一部分前人格的同时，引导银龙塞瓦尔罗斯重拾与腐化抗争的决心。"),
            
            // 新增：百科页标题/正文本地化（用于 TooltipTemplateGlossary / EncyclopediaPageConfigurator）
            (ZekariusLichHintTitleKey, "巫妖之路"),
            (ZekariusLichHintTextKey,  "你在前期选择了巫妖的神话之路。"),
            (ZekariusDragonsHintTitleKey, "特伦德勒夫与塞瓦罗斯"),
            (ZekariusDragonsHintTextKey,  "你唤醒了不死的特伦德勒夫的一部分前人格的同时，引导银龙塞瓦尔罗斯重拾与腐化抗争的决心。"),

            // 泽卡琉斯完整后记整页（同样替换 d| 为 g|）
            ("MDGA_ZekariusEpilogue_FullPage",
                "龙神{g|Apsu}阿卜苏{/g}向来将善意视为不可交易之物，因此从来不会轻易接受信徒的条件，也就是用取悦祂的方式来获得报酬。但龙神会在看到真正的勇气与救赎时给予回应。在龙神的目光里，对首先完成真正的救赎的{name}自己尤为尊敬，在{g|lich}巫妖{/g}之力黑暗的{g|Encyclopedia:MDGA_Zekarius_LichPathHint}洗刷{/g}之下，指挥官的灵魂并没有越陷越深，而是在被浸透的黑暗中抓紧了心中的光芒，浪子回头洗尽铅华，重新回到了善良的阵营。而更加难能可贵的是，在指挥官的帮助之下，两个迷失的{g|Encyclopedia:MDGA_Zekarius_TerendelevSevalrosHint}族人{/g}被拯救，作为一个曾经的温血动物，这样的龙族精神赢得了龙神的尊重，于是一个机会，一个重新选择的机会被赐给了另一个孤独而勇敢的灵魂，大法师{g|Zacarius}泽卡琉斯{/g}，战场上的泽卡琉斯比任何圣教军都更视死如归，而面临死亡，他又比任何人都冷静理智，他驯服死亡为圣战服务的意图差点酿成了黑暗的灾祸，但他的忠诚与誓言为自己赢得了尊重。在等待坟墓女士的审判时的泽卡琉斯得到了一个机会，一个清偿罪孽的机会，来自龙神的力量洗尽了泽卡琉斯灵魂中浸染的黑暗。{n}审判的结果已经落定：泽卡琉斯被允许前往龙神的领域不朽回廊，在龙神双翼的庇护下度过不朽的岁月，做自己想做的一切之事，但被泽卡琉斯拒绝，他希望前往天堂，如果有机会，他希望继续执行圣战，消灭恶魔。{/n}")
};

        // 动态条目集合：为每个克隆/新建的特性生成唯一 key（例如 MDGA_DD_<guid>_m_DisplayName），文本在运行期注册。
        private static readonly Dictionary<string,string> DynamicEntries = new();

        private static bool _installedWatcher;
        private static HashSet<string> _applied = new HashSet<string>();
        private static object _lastPack;
        private static int _injectAttempts;
        private static bool _delayStarted;
        private static bool _completedInjection; // 注入完成标记：所有动态 key 均已落地且不再需要轮询
        private static bool _localeHookInstalled;

        private static readonly BlueprintGuid[] ArcanaGuids = new[] {
            BlueprintGuid.Parse("ac04aa27a6fd8b4409b024a6544c4928"),
            BlueprintGuid.Parse("a8baee8eb681d53438cc17bd1d125890"),
            BlueprintGuid.Parse("153e9b6b5b0f34d45ae8e815838aca80"),
            BlueprintGuid.Parse("5515ae09c952ae2449410ab3680462ed"),
            BlueprintGuid.Parse("caebe2fa3b5a94d4bbc19ccca86d1d6f"),
            BlueprintGuid.Parse("2a8ed839d57f31a4983041645f5832e2"),
            BlueprintGuid.Parse("1af96d3ab792e3048b5e0ca47f3a524b"),
            BlueprintGuid.Parse("456e305ebfec3204683b72a45467d87c"),
            BlueprintGuid.Parse("0f0cb88a2ccc0814aa64c41fd251e84e"),
            BlueprintGuid.Parse("677ae97f60d26474bbc24a50520f9424")
        }; 
        private static bool _arcanaScaled;

        // 为嵌套 helper 提供只读访问器（避免直接引用私有常量名解析问题）
        internal static string GoldDragonKeyConst => GoldDragonEpilogueTextKey;
        internal static string ZekariusTextKeyConst => ZekariusTextKey;

        internal static void RegisterDynamicKey(string key, string text)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text)) return;
            if (!DynamicEntries.ContainsKey(key))
            {
                DynamicEntries[key] = text;
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Registered dynamic key " + key);
                // 如果之前认为注入已完成，但现在出现新 key，则重新开启注入流程
                if (_completedInjection && !_applied.Contains(key))
                {
                    _completedInjection = false; // allow EnsureInjected to run again
                }
                EnsureInjected();
            }
        }

        internal static string GetFallback(string key)
        {
            foreach (var (k, t) in Entries)
                if (k == key) return t;
            if (DynamicEntries.TryGetValue(key, out var dyn)) return dyn;
            return null;
        }

        internal static void EnsureInjected()
        {
            if (_completedInjection && DynamicEntries.Keys.All(k => _applied.Contains(k))) return;
            _injectAttempts++;
            try
            {
                object pack = null;
                var packProp = typeof(LocalizationManager).GetProperty("CurrentPack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (packProp != null)
                {
                    try { pack = packProp.GetValue(null); } catch { pack = null; }
                }
                if (pack == null)
                {
                    foreach (var f in typeof(LocalizationManager).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        try
                        {
                            var ft = f.FieldType;
                            if (ft.Name.Contains("LocalizationPack"))
                            {
                                var val = f.GetValue(null);
                                if (val != null) { pack = val; break; }
                            }
                        }
                        catch { }
                    }
                }
                if (pack == null)
                {
                    if (Main.Settings.VerboseLogging && (_injectAttempts <= 3 || _injectAttempts % 100 == 0))
                        Main.Log("[DD ProgFix][Loc] EnsureInjected: localization pack still null (attempt " + _injectAttempts + ")");
                    try { DumpLocalizationManagerInternals(); } catch { }
                    return;
                }
                bool packSwapped = !ReferenceEquals(pack, _lastPack) && _lastPack != null;
                _lastPack = pack;

                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var dictField = pack.GetType().GetField("m_Strings", flags);
                if (dictField == null)
                {
                    if (Main.Settings.VerboseLogging && (_injectAttempts <= 3 || _injectAttempts % 200 == 0))
                        Main.Log("[DD ProgFix][Loc] m_Strings field not found on pack type " + pack.GetType().FullName);
                    return;
                }
                var dict = dictField.GetValue(pack) as System.Collections.IDictionary;
                if (dict == null)
                {
                    if (Main.Settings.VerboseLogging)
                        Main.Log("[DD ProgFix][LocDiag] m_Strings present but null; packType=" + pack.GetType().FullName);
                    try { DumpPackInternals(pack); } catch { }
                    return;
                }

                var seType = pack.GetType().GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(t => t.Name.Contains("StringEntry"));
                if (seType == null) return;
                try { DumpStringEntryType(seType); } catch { }
                var textField = seType.GetField("Text", flags) ?? seType.GetField("m_Text", flags);
                var traitsField = seType.GetField("Traits", flags);

                // 若没有 Traits 字段，直接走无 traits 路径
                bool hasTraits = traitsField != null;
                if (!hasTraits && Main.Settings.VerboseLogging)
                {
                    Main.Log("[LocDiag] StringEntry has no Traits field; using text-only entries for hints and glossary.");
                }

                int added = 0; int skipped = 0;
                // 注入静态占位条目
                foreach (var (key,text) in Entries)
                {
                    if (TryAdd(dict, seType, textField, traitsField, key, text)) added++; else skipped++;
                }
                // 强制覆写提示条目文本，避免出现悬停边框但文本为 null
                try { ForceOverrideHintEntries(dict, seType, textField); } catch { }

                // 仅在存在 Traits 字段时尝试复制/构造 Traits
                if (hasTraits)
                {
                    try { EnsureHintTraits(dict, seType, traitsField, textField); } catch { }
                    try { EnsureHintTraitsDefault(dict, traitsField); } catch { }
                }

                // 注入所有动态条目（特性名/描述等）
                foreach (var kv in DynamicEntries)
                {
                    if (TryAdd(dict, seType, textField, traitsField, kv.Key, kv.Value)) added++; else skipped++;
                }
                // Arcana 伤害骰描述扩展（逐级+2/+3/+4）——仅第一次成功才设定 _arcanaScaled
                if (!_arcanaScaled)
                {
                    try { ApplyArcanaScalingDescriptions(dict); } catch (Exception exA) { if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Arcana scale desc error: " + exA.Message); }
                }
                if (Main.Settings.VerboseLogging) Main.Log($"[DD ProgFix][Loc] Inject attempt #{_injectAttempts} packSwapped={packSwapped} added={added} skippedExisting={skipped}");
                // 在每次注入后执行挂钩式强制覆写（用于与 QuickLocalization 共存的 key 文本替换）
                DragonMightOverrideHelper.TryOverrideDragonMight(dict);
                HintOverrideHelper.TryOverride(dict, seType, textField);

                // 新增：对关键悬停键进行一次结构诊断，输出 Text 与 Traits 的实际状态
                try { DumpStringEntryForKeys(dict, seType); } catch { }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging && (_injectAttempts <= 3 || _injectAttempts % 200 == 0))
                    Main.Log("[DD ProgFix][Loc] EnsureInjected exception: " + ex.Message);
            }
        }

        private static bool TryAdd(System.Collections.IDictionary dict, Type seType, FieldInfo textField, FieldInfo traitsField, string key, string text)
        {
            try
            {
                if (dict.Contains(key))
                {
                    _applied.Add(key);
                    if (Main.Settings.VerboseLogging)
                        Main.Log($"[ZekariusHint] Key {key} already exists in dict, skip adding.");
                    return false;
                }
                var entry = Activator.CreateInstance(seType);
                textField?.SetValue(entry, text);
                // 不主动清空 Traits，避免影响悬停提示解析
                dict.Add(key, entry);
                _applied.Add(key);
                return true;
            }
            catch { return false; }
        }

        // 强制覆写提示条目文本，避免出现悬停边框但文本为 null
        private static void ForceOverrideHintEntries(System.Collections.IDictionary dict, Type seType, FieldInfo textField)
        {
            ForceOne(dict, seType, textField, ZekariusLichHintKey, GetFallback(ZekariusLichHintKey));
            ForceOne(dict, seType, textField, ZekariusDragonsHintKey, GetFallback(ZekariusDragonsHintKey));
        }

        private static void ForceOne(System.Collections.IDictionary dict, Type seType, FieldInfo textField, string key, string text)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text) || dict == null) return;

            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            if (dict.Contains(key))
            {
                var entry = dict[key];
                if (entry == null) return;
                var eType = entry.GetType();
                var eTextField = textField ?? (eType.GetField("Text", flags) ?? eType.GetField("m_Text", flags));
                if (eTextField == null) return;

                var cur = eTextField.GetValue(entry) as string;
                if (string.IsNullOrEmpty(cur))
                {
                    eTextField.SetValue(entry, text);
                    if (Main.Settings.VerboseLogging)
                        Main.Log($"[ZekariusHint] Overwrote empty text for key {key}");
                }
            }
            else
            {
                // 没有就直接创建一个
                var entry = Activator.CreateInstance(seType);
                textField?.SetValue(entry, text);
                dict.Add(key, entry);
                if (Main.Settings.VerboseLogging)
                    Main.Log($"[ZekariusHint] Created new entry for key {key}");
            }
        }

        // 为 d| 悬停提示条目补齐 Traits：尝试从现有包含 d| 的条目复制 Traits 作为模板
        private static void EnsureHintTraits(System.Collections.IDictionary dict, Type seType, FieldInfo traitsField, FieldInfo textField)
        {
            try
            {
                if (dict == null || traitsField == null) return;
                object templateTraits = null;
                // 找到任意一个现有条目：其 Text 包含 "{d|" 且 Traits 非空，作为模板
                foreach (var keyObj in dict.Keys)
                {
                    var key = keyObj as string; if (key == null) continue;
                    var entry = dict[key]; if (entry == null) continue;
                    var eType = entry.GetType();
                    var eTextField = textField ?? (eType.GetField("Text", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? eType.GetField("m_Text", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
                    var txt = eTextField?.GetValue(entry) as string;
                    var tr = traitsField.GetValue(entry);
                    if (!string.IsNullOrEmpty(txt) && txt.Contains("{d|") && tr != null)
                    {
                        templateTraits = tr;
                        break;
                    }
                }
                if (templateTraits == null) return;

                // 对我们两个 hint key：若 Traits 为空则复制模板 Traits
                foreach (var hk in new[] { ZekariusLichHintKey, ZekariusDragonsHintKey })
                {
                    if (!dict.Contains(hk)) continue;
                    var entry = dict[hk]; if (entry == null) continue;
                    var curTraits = traitsField.GetValue(entry);
                    if (curTraits == null)
                    {
                        try
                        {
                            traitsField.SetValue(entry, templateTraits);
                            if (Main.Settings.VerboseLogging) Main.Log("[ZekariusHint] Copied Traits from template to key " + hk);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[ZekariusHint] EnsureHintTraits error: " + ex.Message);
            }
        }

        // 构造最简默认 Traits：若复制失败或 Traits 仍为 null，则为两个提示键直接赋一个非空对象
        private static void EnsureHintTraitsDefault(System.Collections.IDictionary dict, FieldInfo traitsField)
        {
            try
            {
                if (dict == null || traitsField == null) return;
                // 创建一个 Traits 实例（不依赖具体结构，至少保证非空）
                object MakeTraits()
                {
                    try { return Activator.CreateInstance(traitsField.FieldType); } catch { }
                    try { return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(traitsField.FieldType); } catch { }
                    return null;
                }
                foreach (var hk in new[] { ZekariusLichHintKey, ZekariusDragonsHintKey })
                {
                    if (!dict.Contains(hk)) continue;
                    var entry = dict[hk]; if (entry == null) continue;
                    var curTraits = traitsField.GetValue(entry);
                    if (curTraits == null)
                    {
                        var traits = MakeTraits();
                        if (traits != null)
                        {
                            try
                            {
                                traitsField.SetValue(entry, traits);
                                if (Main.Settings.VerboseLogging) Main.Log("[ZekariusHint] Set default Traits object for key " + hk);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[ZekariusHint] EnsureHintTraitsDefault error: " + ex.Message);
            }
        }

        // 辅助：从一个工作正常的 dKey 复制 Traits 到目标键，使用 Main.Logger 输出
        private static void CopyTraitsFromWorkingDKey(
            IDictionary dict,
            Type stringEntryType,
            FieldInfo traitsField,
            string sourceKey,
            string[] targetKeys)
        {
            try
            {
                if (dict == null || stringEntryType == null || traitsField == null)
                {
                    Main.Log("[LocDiag] CopyTraits: invalid arguments.");
                    return;
                }

                // 允许源键不在 dict 中时，尝试加上前缀变体
                string resolvedSourceKey = sourceKey;
                if (!dict.Contains(resolvedSourceKey))
                {
                    // 常见工作键：`Encyclopedia:Damage` 或 纯 `Damage`
                    if (dict.Contains("Encyclopedia:" + sourceKey)) resolvedSourceKey = "Encyclopedia:" + sourceKey;
                    else if (dict.Contains("Damage")) resolvedSourceKey = "Damage";
                }

                if (!dict.Contains(resolvedSourceKey))
                {
                    Main.Log($"[LocDiag] CopyTraits: source key '{sourceKey}' not found in dict (resolved '{resolvedSourceKey}').");
                    return;
                }

                var sourceEntry = dict[resolvedSourceKey];
                if (sourceEntry == null || !stringEntryType.IsInstanceOfType(sourceEntry))
                {
                    Main.Log($"[LocDiag] CopyTraits: source entry for '{resolvedSourceKey}' invalid.");
                    return;
                }

                var sourceTraits = traitsField.GetValue(sourceEntry);
                if (sourceTraits == null)
                {
                    Main.Log($"[LocDiag] CopyTraits: source traits for '{resolvedSourceKey}' is null.");
                    return;
                }

                foreach (var key in targetKeys)
                {
                    string resolvedTargetKey = key;
                    if (!dict.Contains(resolvedTargetKey))
                    {
                        // 我们自己的键通常无前缀，但仍记录
                        Main.Log($"[LocDiag] CopyTraits: target key '{key}' not found, skip.");
                        continue;
                    }

                    var targetEntry = dict[resolvedTargetKey];
                    if (targetEntry == null || !stringEntryType.IsInstanceOfType(targetEntry))
                    {
                        Main.Log($"[LocDiag] CopyTraits: target entry for '{resolvedTargetKey}' invalid, skip.");
                        continue;
                    }

                    traitsField.SetValue(targetEntry, sourceTraits);
                    Main.Log($"[LocDiag] CopyTraits: copied traits from '{resolvedSourceKey}' to '{resolvedTargetKey}'.");
                }
            }
            catch (Exception e)
            {
                Main.Log("[LocDiag] CopyTraits failed. " + e);
            }
        }

        internal static void BindLocalizedStrings(BlueprintFeature wis, BlueprintFeature cha)
        {
            try
            {
                if (wis != null)
                {
                    SetLocKey(wis, "m_DisplayName", WisNameKey);
                    SetLocKey(wis, "m_Description", WisDescKey);
                }
                if (cha != null)
                {
                    SetLocKey(cha, "m_DisplayName", ChaNameKey);
                    SetLocKey(cha, "m_Description", ChaDescKey);
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] BindLocalizedStrings exception: " + ex.Message);
            }
        }

        private static void SetLocKey(object fact, string fieldName, string key)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            // 向上遍历继承链查找字段（特性可能在基类里声明）
            FieldInfo fi = null; Type t = fact.GetType();
            while (t != null && fi == null) { fi = t.GetField(fieldName, flags); t = t.BaseType; }
            if (fi == null) return;
            var loc = fi.GetValue(fact);
            if (loc == null) return;
            var keyField = loc.GetType().GetField("m_Key", flags);
            if (keyField == null) return;
            keyField.SetValue(loc, key);
        }

        internal static void InstallWatcher()
        {
            if (_installedWatcher) return; _installedWatcher = true;
            try
            {
                var go = new GameObject("MDGA_LocWatcher");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<LocWatcher>();
                InstallLocaleChangedHook();
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Failed to install watcher: " + ex.Message);
            }
        }

        internal static void StartDelayed()
        {
            if (_delayStarted) return; _delayStarted = true;
            try
            {
                var go = new GameObject("MDGA_LocDelay");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<DelayedInit>();
                InstallLocaleChangedHook();
            }
            catch (Exception ex) { if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] StartDelayed error: " + ex.Message); }
        }

        private class LocWatcher : MonoBehaviour
        {
            private float _timer;
            private int _burstFrames = 30; // 初始高频阶段用于快速覆盖 QuickLocalization 的早期写入
            private int _idleCounter;
            void Update()
            {
                if (_completedInjection) { Destroy(this); return; }
                if (_burstFrames > 0)
                {
                    _burstFrames--;
                    LocalizationInjector.EnsureInjected();
                    return;
                }
                _timer += Time.unscaledDeltaTime;
                if (_timer >= 15f) // 间隔轮询：防止语言包被其他 mod 重新分配后遗漏再注入
                {
                    _timer = 0f;
                    LocalizationInjector.EnsureInjected();
                }
                else if (_idleCounter++ % 600 == 0) // 低频探测 pack 是否被替换
                {
                    var packProp = typeof(LocalizationManager).GetProperty("CurrentPack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    var pack = packProp?.GetValue(null);
                    if (pack != null && !ReferenceEquals(pack, _lastPack))
                    {
                        if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Detected pack swap, reinjecting.");
                        _lastPack = pack;
                        _completedInjection = false;
                        LocalizationInjector.EnsureInjected();
                    }
                }
            }
        }

        private class DelayedInit : MonoBehaviour
        {
            private int _frames; // 延迟阶段：等待其他本地化覆盖完成后再最后补一次
            private bool _done;
            void Update()
            {
                if (_done || _completedInjection) { Destroy(this.gameObject); return; }
                _frames++;
                if (_frames < 150) // 前 150 帧内每 50 帧重试一次
                {
                    if (_frames % 50 == 0) LocalizationInjector.EnsureInjected();
                    return;
                }
                LocalizationInjector.EnsureInjected();
                _done = true;
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Delayed injection finished after " + _frames + " frames");
            }
        }

        internal static void DumpState(BlueprintFeature wis = null, BlueprintFeature cha = null)
        {
            if (Main.Settings != null && !Main.Settings.VerboseLogging) return;
            try
            {
                var packProp = typeof(LocalizationManager).GetProperty("CurrentPack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var pack = packProp?.GetValue(null);
                Main.Log("[DD ProgFix][LocDiag] Pack instance=" + (pack==null?"null":pack.ToString()));
                if (wis != null) LogLocFields(wis, "Wis");
                if (cha != null) LogLocFields(cha, "Cha");
            }
            catch (Exception ex)
            {
                Main.Log("[DD ProgFix][LocDiag] Exception: " + ex.Message);
            }
        }

        private static void LogLocFields(BlueprintFeature feat, string tag)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                FieldInfo fDisp = null, fDesc = null; Type t = feat.GetType();
                while (t != null && (fDisp == null || fDesc == null)) { fDisp ??= t.GetField("m_DisplayName", flags); fDesc ??= t.GetField("m_Description", flags); t = t.BaseType; }
                var disp = fDisp?.GetValue(feat);
                var desc = fDesc?.GetValue(feat);
                string dispKey = disp?.GetType().GetField("m_Key", flags)?.GetValue(disp) as string;
                string descKey = desc?.GetType().GetField("m_Key", flags)?.GetValue(desc) as string;
                Main.Log($"[DD ProgFix][LocDiag] {tag} displayKey={dispKey} descKey={descKey}");
            }
            catch (Exception ex) { Main.Log("[DD ProgFix][LocDiag] LogLocFields error: " + ex.Message); }
        }

        internal static void RegisterFeatureLocalization(BlueprintFeature feat, string display, string description)
        {
            try
            {
                if (feat == null) return;
                var displayKey = "MDGA_DD_" + feat.AssetGuid + "_m_DisplayName";
                var descKey = "MDGA_DD_" + feat.AssetGuid + "_m_Description";
                RegisterDynamicKey(displayKey, display);
                RegisterDynamicKey(descKey, description);

                // 直接将 blueprint 上的 m_DisplayName / m_Description 的 m_Key 指向我们动态 key；
                // 并在本地对象里写入 m_Text 作为兜底，防止 UI 读取时出现 null。
                BindKeyAndText(feat, "m_DisplayName", displayKey, display);
                BindKeyAndText(feat, "m_Description", descKey, description);
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] RegisterFeatureLocalization error: " + ex.Message);
            }
        }

        internal static void BindKeyAndText(object blueprint, string fieldName, string key, string text)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                FieldInfo fi = null; Type t = blueprint.GetType();
                while (t != null && fi == null) { fi = t.GetField(fieldName, flags); t = t.BaseType; }
                if (fi == null) return;
                var locObj = fi.GetValue(blueprint);
                if (locObj == null)
                {
                    // 尝试创建新的 LocalizedString 实例
                    var locType = fi.FieldType;
                    try { locObj = Activator.CreateInstance(locType); fi.SetValue(blueprint, locObj); } catch { return; }
                }
                var keyField = locObj.GetType().GetField("m_Key", flags);
                var textField = locObj.GetType().GetField("m_Text", flags);
                if (keyField != null) keyField.SetValue(locObj, key);
                if (textField != null && (textField.GetValue(locObj) as string) == null)
                {
                    // 仅在原文本为空时写入兜底文本，避免覆盖其他 mod 已经写入的内容
                    textField.SetValue(locObj, text);
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] BindKeyAndText error: " + ex.Message);
            }
        }

        private static void ApplyArcanaScalingDescriptions(System.Collections.IDictionary dict)
        {
            string suffixEn = " At 5th level this bonus increases to +2 per die, at 10th level to +3, and at 15th level to +4.";
            string suffixZh = " 在5级时该加值变为每骰+2，在10级为每骰+3，在15级为每骰+4。";
            int patched = 0; int skipped = 0;
            foreach (var guid in ArcanaGuids)
            {
                var feat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(guid);
                if (feat == null) { skipped++; continue; }
                try
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                    FieldInfo fDesc = null; Type t = feat.GetType();
                    while (t != null && fDesc == null) { fDesc = t.GetField("m_Description", flags); t = t.BaseType; }
                    if (fDesc == null) { skipped++; continue; }
                    var locObj = fDesc.GetValue(feat);
                    if (locObj == null) { skipped++; continue; }
                    var keyField = locObj.GetType().GetField("m_Key", flags);
                    var textField2 = locObj.GetType().GetField("m_Text", flags);
                    string origKey = keyField?.GetValue(locObj) as string;
                    if (!string.IsNullOrEmpty(origKey) && origKey.Contains("MDGAScale")) { skipped++; continue; }

                    string packText = null;
                    if (!string.IsNullOrEmpty(origKey) && dict.Contains(origKey))
                    {
                        var entry = dict[origKey];
                        var et = entry?.GetType();
                        var txtF = et?.GetField("Text", flags) ?? et?.GetField("m_Text", flags);
                        packText = txtF?.GetValue(entry) as string;
                    }
                    string baseText = packText ?? (textField2?.GetValue(locObj) as string ?? string.Empty);

                    bool isChinese = baseText.Any(c => c >= '\u4e00' && c <= '\u9fff');
                    bool alreadyHas = (baseText.Contains("15级") || baseText.Contains("15th level") || baseText.Contains("15th") || baseText.Contains("15级为每骰+4"));
                    string newText = alreadyHas ? baseText : baseText + (isChinese ? suffixZh : suffixEn);

                    string newKeyBase = string.IsNullOrEmpty(origKey) ? ("MDGA_ARCANA_DESC_" + guid) : origKey;
                    string newKey = newKeyBase + "_MDGAScale";
                    keyField?.SetValue(locObj, newKey);
                    textField2?.SetValue(locObj, newText);
                    RegisterDynamicKey(newKey, newText);
                    patched++;
                }
                catch { skipped++; }
            }
            if (patched > 0) _arcanaScaled = true;
            if (Main.Settings.VerboseLogging) Main.Log($"[DD ProgFix][Loc] Arcana scaling descriptions patched: {patched} (skipped {skipped})");
        }

        private static void InstallLocaleChangedHook()
        {
            if (_localeHookInstalled) return;
            try
            {
                var lmType = typeof(LocalizationManager);
                // 首先尝试事件（优先，无侵入）
                var evt = lmType.GetEvent("OnLocaleChanged", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (evt != null)
                {
                    var handlerType = evt.EventHandlerType;
                    var invoke = handlerType.GetMethod("Invoke");
                    var pars = invoke.GetParameters();
                    Delegate del;
                    if (pars.Length == 0)
                    {
                        Action a = () => LocaleChangedCallback();
                        del = Delegate.CreateDelegate(handlerType, a.Target, a.Method);
                    }
                    else if (pars.Length == 1)
                    {
                        try {
                            var p0Type = pars[0].ParameterType;
                            var dm = new DynamicMethod("MDGA_LocaleChangedProxy", typeof(void), new Type[] { p0Type }, typeof(LocalizationInjector), true);
                            var il = dm.GetILGenerator();
                            il.Emit(OpCodes.Call, typeof(LocalizationInjector).GetMethod(nameof(LocaleChangedCallback), BindingFlags.Static | BindingFlags.NonPublic));
                            il.Emit(OpCodes.Ret);
                            del = dm.CreateDelegate(handlerType);
                        } catch {
                            del = Delegate.CreateDelegate(handlerType, typeof(LocalizationInjector).GetMethod(nameof(LocaleChangedCallback), BindingFlags.Static | BindingFlags.NonPublic), true);
                        }
                    }
                    else
                    {
                        Action a = () => LocaleChangedCallback();
                        del = Delegate.CreateDelegate(handlerType, a.Target, a.Method, false);
                    }
                    evt.AddEventHandler(null, del);
                    _localeHookInstalled = true;
                    if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Installed OnLocaleChanged event hook.");
                }
                else
                {
                    // 若没有事件，则回退：Patch ChangeLanguage / SetLanguage / SetLocale 之类的方法
                    var m = lmType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(mi => mi.Name.Contains("ChangeLanguage") || mi.Name.Contains("SetLanguage") || mi.Name.Contains("SetLocale"));
                    if (m != null)
                    {
                        var harmony = new Harmony("MDGA.LocalizationHook");
                        harmony.Patch(m, postfix: new HarmonyMethod(typeof(LocalizationInjector).GetMethod(nameof(LocaleChangedPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        _localeHookInstalled = true;
                        if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Patched method " + m.Name + " for locale change hook.");
                    }
                    else
                    {
                        if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] No locale change event or method found; relying on watcher.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] InstallLocaleChangedHook error: " + ex.Message);
            }
        }

        private static void LocaleChangedCallback()
        {
            try
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Locale changed -> reinjection reset");
                _completedInjection = false;
                _arcanaScaled = false; // force arcana patch re-run
                _injectAttempts = 0;
                EnsureInjected();
                StartDelayed(); // schedule aggressive retries
                // Recreate watcher if it was destroyed
                if (!_installedWatcher) InstallWatcher();
                // 语言切换后再次覆写龙族之力描述
                DragonMightOverrideHelper.MarkDirty();
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] LocaleChangedCallback error: " + ex.Message);
            }
        }

        private static void LocaleChangedPostfix()
        {
            LocaleChangedCallback();
        }

        // 诊断：枚举 LocalizationManager 的所有静态字段/属性，打印类型与当前值是否为 null
        private static void DumpLocalizationManagerInternals()
        {
            if (!Main.Settings.VerboseLogging) return;
            try
            {
                var lmType = typeof(LocalizationManager);
                Main.Log("[LocDiag] ==== LocalizationManager internals ====");
                foreach (var f in lmType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object val = null; string err = null;
                    try { val = f.GetValue(null); } catch (Exception ex) { err = ex.GetType().Name + ":" + ex.Message; }
                    Main.Log($"[LocDiag] Field {f.Name} type={f.FieldType.FullName} val={(val==null?"null":val.ToString())} err={(err??"none")}");
                }
                foreach (var p in lmType.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object val = null; string err = null;
                    try { val = p.GetValue(null, null); } catch (Exception ex) { err = ex.GetType().Name + ":" + ex.Message; }
                    Main.Log($"[LocDiag] Prop  {p.Name} type={p.PropertyType.FullName} val={(val==null?"null":val.ToString())} err={(err??"none")}");
                }
                Main.Log("[LocDiag] =====================================");
            }
            catch (Exception ex)
            {
                Main.Log("[LocDiag] DumpLocalizationManagerInternals exception: " + ex.Message);
            }
        }

        // 诊断：枚举 pack 的潜在字符串容器（如 m_Strings 以外的集合），帮助识别版本差异
        private static void DumpPackInternals(object pack)
        {
            if (!Main.Settings.VerboseLogging || pack == null) return;
            try
            {
                var t = pack.GetType();
                Main.Log("[LocDiag] ==== Pack internals type=" + t.FullName + " ====");
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object val = null; string err = null; string detail = "";
                    try { val = f.GetValue(pack); } catch (Exception ex) { err = ex.GetType().Name + ":" + ex.Message; }
                    if (val is IDictionary d) detail = " dict.count=" + d.Count;
                    else if (val is ICollection c) detail = " collection.count=" + c.Count;
                    Main.Log($"[LocDiag] Field {f.Name} type={f.FieldType.FullName} val={(val==null?"null":val.ToString())}{detail} err={(err??"none")}");
                }
                foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object val = null; string err = null; string detail = "";
                    try { val = p.GetValue(pack, null); } catch (Exception ex) { err = ex.GetType().Name + ":" + ex.Message; }
                    if (val is IDictionary d) detail = " dict.count=" + d.Count;
                    else if (val is ICollection c) detail = " collection.count=" + c.Count;
                    Main.Log($"[LocDiag] Prop  {p.Name} type={p.PropertyType.FullName} val={(val==null?"null":val.ToString())}{detail} err={(err??"none")}");
                }
                Main.Log("[LocDiag] =====================================");
            }
            catch (Exception ex)
            {
                Main.Log("[LocDiag] DumpPackInternals exception: " + ex.Message);
            }
        }

        // 诊断：输出我们关心的两个提示键的 StringEntry 的 Text 与 Traits 状态
        private static void DumpStringEntryForKeys(IDictionary dict, Type seType)
        {
            if (!Main.Settings.VerboseLogging || dict == null) return;
            try
            {
                foreach (var hk in new[] {
                    "MDGA_Zekarius_LichPathHint",
                    "MDGA_Zekarius_TerendelevSevalrosHint"
                })
                {
                    if (!dict.Contains(hk))
                    {
                        Main.Log("[LocDiag] Hint key missing in dict: " + hk);
                        continue;
                    }
                    var entry = dict[hk];
                    if (entry == null) { Main.Log("[LocDiag] Hint entry is null: " + hk); continue; }
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var eType = entry.GetType();
                    var textF = eType.GetField("Text", flags) ?? eType.GetField("m_Text", flags);
                    var traitsF = eType.GetField("Traits", flags);
                    var txt = textF?.GetValue(entry) as string;
                    var traits = traitsF?.GetValue(entry);
                    Main.Log($"[LocDiag] Key={hk} TextLen={(txt==null?"null":txt.Length.ToString())} Traits={(traits==null?"null":traits.ToString())} EntryType={eType.FullName}");
                    // 额外打印 Traits 的所有字段（若可访问）
                    if (traits != null)
                    {
                        foreach (var tf in traits.GetType().GetFields(flags))
                        {
                            object v = null; string err = null;
                            try { v = tf.GetValue(traits); } catch (Exception ex) { err = ex.GetType().Name + ":" + ex.Message; }
                            Main.Log($"[LocDiag]  Traits.{tf.Name} type={tf.FieldType.FullName} val={(v==null?"null":v.ToString())} err={(err??"none")}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log("[LocDiag] DumpStringEntryForKeys exception: " + ex.Message);
            }
        }

        // 诊断：输出 StringEntry 类型的结构，以确认 Text/Traits 字段的真实名称与可访问性
        private static bool _dumpedStringEntryType;
        private static void DumpStringEntryType(Type seType)
        {
            if (_dumpedStringEntryType || !Main.Settings.VerboseLogging || seType == null) return;
            _dumpedStringEntryType = true;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                Main.Log("[LocDiag] ==== StringEntry type structure ====");
                Main.Log("[LocDiag] Type=" + seType.FullName);
                foreach (var f in seType.GetFields(flags))
                {
                    Main.Log($"[LocDiag] Field {f.Name} type={f.FieldType.FullName} isPublic={f.IsPublic} isStatic={f.IsStatic}");
                }
                foreach (var p in seType.GetProperties(flags))
                {
                    Main.Log($"[LocDiag] Prop  {p.Name} type={p.PropertyType.FullName} canRead={p.CanRead} canWrite={p.CanWrite}");
                }
                foreach (var m in seType.GetMethods(flags))
                {
                    if (m.DeclaringType != seType) continue;
                    Main.Log($"[LocDiag] Method {m.Name} ret={m.ReturnType.FullName} params={string.Join(",", m.GetParameters().Select(x => x.ParameterType.Name))}");
                }
                Main.Log("[LocDiag] =====================================");
            }
            catch (Exception ex)
            {
                Main.Log("[LocDiag] DumpStringEntryType exception: " + ex.Message);
            }
        }

        // 诊断：对指定 key 的字典条目做原始 dump，包含 EntryType / Text / Traits 以及 Traits 的所有字段值
        private static void DumpRawEntryForKey(IDictionary dict, Type seType, FieldInfo textField, FieldInfo traitsField, string key)
        {
            if (!Main.Settings.VerboseLogging || dict == null || string.IsNullOrEmpty(key)) return;
            try
            {
                if (!dict.Contains(key))
                {
                    Main.Log("[LocDiag] RawDump: key not found in dict -> " + key);
                    return;
                }
                var entry = dict[key];
                if (entry == null)
                {
                    Main.Log("[LocDiag] RawDump: entry null -> " + key);
                    return;
                }
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var eType = entry.GetType();
                var eTextField = textField ?? (eType.GetField("Text", flags) ?? eType.GetField("m_Text", flags));
                var eTraitsField = traitsField ?? eType.GetField("Traits", flags);
                var txt = eTextField?.GetValue(entry) as string;
                var traits = eTraitsField?.GetValue(entry);
                Main.Log("[LocDiag] ==== RawDump for key '" + key + "' ====");
                Main.Log("[LocDiag] EntryType=" + eType.FullName + " TextLen=" + (txt==null?"null":txt.Length.ToString()));
                // 列出该 entry 的所有字段与属性当前值
                foreach (var f in eType.GetFields(flags))
                {
                    object v = null; string err = null;
                    try { v = f.GetValue(entry); } catch (Exception ex) { err = ex.GetType().Name + ":" + ex.Message; }
                    Main.Log($"[LocDiag]  EntryField {f.Name} type={f.FieldType.FullName} val={(v==null?"null":v.ToString())} err={(err??"none")}");
                }
                foreach (var p in eType.GetProperties(flags))
                {
                    object v = null; string err = null;
                    try { v = p.GetValue(entry, null); } catch (Exception ex) { err = ex.GetType().Name + ":" + ex.Message; }
                    Main.Log($"[LocDiag]  EntryProp  {p.Name} type={p.PropertyType.FullName} val={(v==null?"null":v.ToString())} err={(err??"none")}");
                }
                // Traits 深入展开
                if (traits == null)
                {
                    Main.Log("[LocDiag] Traits=null");
                }
                else
                {
                    var tt = traits.GetType();
                    Main.Log("[LocDiag] TraitsType=" + tt.FullName);
                    foreach (var tf in tt.GetFields(flags))
                    {
                        object v = null; string err = null;
                        try { v = tf.GetValue(traits); } catch (Exception ex) { err = ex.GetType().Name + ":" + ex.Message; }
                        Main.Log($"[LocDiag]  TraitsField {tf.Name} type={tf.FieldType.FullName} val={(v==null?"null":v.ToString())} err={(err??"none")}");
                    }
                    foreach (var tp in tt.GetProperties(flags))
                    {
                        object v = null; string err = null;
                        try { v = tp.GetValue(traits, null); } catch (Exception ex) { err = ex.GetType().Name + ":" + ex.Message; }
                        Main.Log($"[LocDiag]  TraitsProp  {tp.Name} type={tp.PropertyType.FullName} val={(v==null?"null":v.ToString())} err={(err??"none")}");
                    }
                }
                Main.Log("[LocDiag] ===============================");
            }
            catch (Exception ex)
            {
                Main.Log("[LocDiag] DumpRawEntryForKey exception: " + ex.Message);
            }
        }

    }

    // 针对 DragonMight 描述的原始 key 强制覆写辅助：
    // QC 会在自身加载阶段批量替换 pack 中文本。我们的策略：在每次 EnsureInjected 后调用 TryOverride 再写入一次，确保描述被自定义版本覆盖。
    // 若找不到原始 key（极少数情况），则回退到动态 key 绑定方案（DragonMightExpansion 已注册）。
    internal static class DragonMightOverrideHelper
    {
        private static bool _doneOnce; // 首次成功后标记；再次语言切换或 pack swap 时置脏（_dirty）以重新覆盖。
        private static string _cachedOrigKey; // DragonMight 原始描述 key
        private static readonly BlueprintGuid DragonMightGuid = BlueprintGuid.Parse("bfc6aa5be6bc41f68ca78aef37913e9f");
        private static bool _dirty = true; // 是否需要重新尝试覆盖（语言切换 / pack 被替换）

        internal static void MarkDirty() { _dirty = true; }

        internal static void TryOverrideDragonMight(System.Collections.IDictionary dict)
        {
            try
            {
                if (!_dirty && _doneOnce) return; // 已完成且未脏
                var ability = ResourcesLibrary.TryGetBlueprint<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility>(DragonMightGuid);
                if (ability == null) return;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                // 定位能力描述字段 m_Description（LocalizedString）
                FieldInfo fDesc = null; Type t = ability.GetType();
                while (t != null && fDesc == null) { fDesc = t.GetField("m_Description", flags); t = t.BaseType; }
                var loc = fDesc?.GetValue(ability);
                if (loc == null) return;
                var keyField = loc.GetType().GetField("m_Key", flags);
                var textField = loc.GetType().GetField("m_Text", flags);
                string origKey = keyField?.GetValue(loc) as string;
                if (string.IsNullOrEmpty(origKey)) return;
                _cachedOrigKey ??= origKey; // 记录原始 key

                // 新文本（与 DragonMightExpansion 中保持一致）
                string zh = "你可以花费{g|Encyclopedia:Swift_Action}迅捷动作{/g}，令自己和周围30英尺内的盟友每施法者1{g|Encyclopedia:Combat_Round}轮{/g}内造成的{g|Encyclopedia:Damage}伤害{/g}提高50%。";
                string en = "As a {g|Encyclopedia:Swift_Action}swift action{/g}, you and allies within 30 feet deal 50% more {g|Encyclopedia:Damage}damage{/g} for 1 {g|Encyclopedia:Combat_Round}round{/g} per {g|Encyclopedia:Caster_Level}caster level{/g}.";
                bool isZh = LocalizationInjectorExtension_IsChinese();
                string finalText = isZh ? zh : en;

                // 覆写策略：若 pack 包含原始 key => 直接改 entry.Text；否则回退到动态 key。
                if (dict.Contains(origKey))
                {
                    var entry = dict[origKey];
                    var eType = entry?.GetType();
                    var eTextField = eType?.GetField("Text", flags) ?? eType?.GetField("m_Text", flags);
                    if (eTextField != null)
                    {
                        eTextField.SetValue(entry, finalText);
                        _doneOnce = true;
                        _dirty = false;
                        if (Main.Settings.VerboseLogging) Main.Log("[DragonMightOverride] Overrode existing pack key=" + origKey + " textLen=" + finalText.Length + " locale=" + (isZh?"zh":"en"));
                    }
                }
                else
                {
                    // 回退：使用我们动态 key（保持与 DragonMightExpansion 兼容），并设置 m_Key 指向该动态 key
                    string dynKey = isZh ? "MDGA_DragonMight_Desc_zh" : "MDGA_DragonMight_Desc_en";
                    LocalizationInjector.RegisterDynamicKey(dynKey, finalText);
                    keyField?.SetValue(loc, dynKey);
                    textField?.SetValue(loc, finalText);
                    if (Main.Settings.VerboseLogging) Main.Log("[DragonMightOverride] Pack missing origKey; rebound to dynamic key=" + dynKey);
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DragonMightOverride] Exception: " + ex.Message);
            }
        }

        // 语言判定：尝试从 LocalizationManager.CurrentLocale 与系统语言推断是否中文
        private static bool LocalizationInjectorExtension_IsChinese()
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
                if (Application.systemLanguage == SystemLanguage.ChineseSimplified || Application.systemLanguage == SystemLanguage.Chinese || Application.systemLanguage == SystemLanguage.ChineseTraditional)
                    return true;
            }
            catch { }
            return false;
        }
    }

    // 悬停提示键后缀覆写辅助：每次注入末尾都强制把两个提示键的 Text 写为兜底文本，避免QL之后为空。
    internal static class HintOverrideHelper
    {
        internal static void TryOverride(System.Collections.IDictionary dict, Type seType, FieldInfo textField)
        {
            try
            {
                if (dict == null) return;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                foreach (var hk in new[] { "MDGA_Zekarius_LichPathHint", "MDGA_Zekarius_TerendelevSevalrosHint" })
                {
                    if (!dict.Contains(hk)) continue;
                    var entry = dict[hk]; if (entry == null) continue;
                    var eType = entry.GetType();
                    var eTextField = textField ?? (eType.GetField("Text", flags) ?? eType.GetField("m_Text", flags));
                    if (eTextField == null) continue;

                    // 强制设置为兜底文本
                    eTextField.SetValue(entry, LocalizationInjector.GetFallback(hk));

                    if (Main.Settings.VerboseLogging)
                        Main.Log($"[HintOverride] Force set text for {hk}");
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[HintOverride] Exception: " + ex.Message);
            }
        }
    }
}
