using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Encyclopedia;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Localization;
using Kingmaker.UI.Common;

namespace MDGA.Loc
{
    // 拦截 UIUtility.GetGlossaryEntry：为 MDGA_Zekarius_* 返回我们自建的百科条目，指向已注册的页面蓝图。
    [HarmonyPatch(typeof(UIUtility), nameof(UIUtility.GetGlossaryEntry))]
    internal static class GlossaryEntryInjector
    {
        // 与 ZekariusGlossary 中保持一致的页面 GUID
        private static readonly BlueprintGuid LichPageGuid    = BlueprintGuid.Parse("7f6a3c7e-2e2c-4e5a-9a6c-6c4d6b6f3e21");
        private static readonly BlueprintGuid DragonsPageGuid = BlueprintGuid.Parse("b3d8e0a4-61a1-4f0f-9b7f-2c9e9f4b8a55");

        [HarmonyPrefix]
        private static bool Prefix(string key, ref GlossaryEntry __result)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return true; // 走原逻辑
                if (!key.StartsWith("MDGA_Zekarius_", StringComparison.OrdinalIgnoreCase)) return true; // 仅处理我们的键

                // 先尝试用原逻辑（若其他 mod 已提供 GlossaryEntry 则直接使用）
                var original = GlossaryHolder.GetEntry(key);
                if (original != null)
                {
                    __result = original;
                    return false;
                }

                // 找到我们事先注册的百科页蓝图（存在即视为有效）
                BlueprintEncyclopediaPage page = null;
                if (key.Equals("MDGA_Zekarius_LichPathHint", StringComparison.OrdinalIgnoreCase))
                    page = ResourcesLibrary.TryGetBlueprint<BlueprintEncyclopediaPage>(LichPageGuid);
                else if (key.Equals("MDGA_Zekarius_TerendelevSevalrosHint", StringComparison.OrdinalIgnoreCase))
                    page = ResourcesLibrary.TryGetBlueprint<BlueprintEncyclopediaPage>(DragonsPageGuid);

                if (page == null)
                {
                    return true; // 兜底：让原逻辑继续，避免破坏流程
                }

                // 直接构造 Root.Strings.GlossaryEntry（该类型 Name/Description 为 LocalizedString）
                var ge = new GlossaryEntry();
                ge.Key = key;

                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                // Name
                var nameLS = new LocalizedString();
                try { typeof(LocalizedString).GetField("m_Key", flags)?.SetValue(nameLS, key + "_Title"); } catch { }
                typeof(GlossaryEntry).GetField("Name", flags)?.SetValue(ge, nameLS);

                // Description
                var descLS = new LocalizedString();
                try { typeof(LocalizedString).GetField("m_Key", flags)?.SetValue(descLS, key + "_Text"); } catch { }
                typeof(GlossaryEntry).GetField("Description", flags)?.SetValue(ge, descLS);

                // Blueprint 字段可留空，UI 根据 Key 也能跳转百科；若需要可通过反射赋引用
                __result = ge;
                return false; // 我们已提供结果，跳过原逻辑
            }
            catch
            {
                return true; // 出错则回退原逻辑
            }
        }
    }
}
