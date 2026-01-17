using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Encyclopedia;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Localization;
using Kingmaker.Blueprints.Encyclopedia.Blocks;
using UnityEngine;

namespace MDGA.Loc
{
    // 在蓝图缓存初始化时创建/更新两条百科页面，供 {d|Encyclopedia:...} 悬停使用
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class ZekariusGlossary
    {
        private static readonly BlueprintGuid LichPageGuid    = BlueprintGuid.Parse("7f6a3c7e-2e2c-4e5a-9a6c-6c4d6b6f3e21");
        private static readonly BlueprintGuid DragonsPageGuid = BlueprintGuid.Parse("b3d8e0a4-61a1-4f0f-9b7f-2c9e9f4b8a55");

        // 前缀阶段：在缓存构建过程中注册，避免 Postfix 时机过晚导致查找不到
        [HarmonyPrefix]
        static void Prefix()
        {
            try { BuildOrUpdatePages(true); } catch (Exception e) { Main.Log($"[ZekariusGlossary] Prefix Error: {e}"); }
        }

        [HarmonyPostfix]
        static void Postfix()
        {
            try { BuildOrUpdatePages(false); } catch (Exception e) { Main.Log($"[ZekariusGlossary] Postfix Error: {e}"); }
        }

        private static void BuildOrUpdatePages(bool early)
        {
            try
            {
                EnsurePage("MDGA_Zekarius_LichPathHint", LichPageGuid,
                    titleKey: "MDGA_Zekarius_LichPathHint_Title",
                    textKey:  "MDGA_Zekarius_LichPathHint_Text");
                EnsurePage("MDGA_Zekarius_TerendelevSevalrosHint", DragonsPageGuid,
                    titleKey: "MDGA_Zekarius_TerendelevSevalrosHint_Title",
                    textKey:  "MDGA_Zekarius_TerendelevSevalrosHint_Text");

                var lich = ResourcesLibrary.TryGetBlueprint<BlueprintEncyclopediaPage>(LichPageGuid);
                var drag = ResourcesLibrary.TryGetBlueprint<BlueprintEncyclopediaPage>(DragonsPageGuid);

                // 注册后立刻验证
                Main.Log($"[ZekariusGlossary] {(early?"Prefix":"Postfix")} verify: Lich={(lich!=null)} Dragons={(drag!=null)} Blocks: Lich={(lich?.Blocks?.Count ?? 0)} Dragons={(drag?.Blocks?.Count ?? 0)}");
            }
            catch (Exception ex) { Main.Log("[ZekariusGlossary] BuildOrUpdatePages failed: " + ex); }
        }

        private static void EnsurePage(string internalName, BlueprintGuid guid, string titleKey, string textKey)
        {
            var existing = ResourcesLibrary.TryGetBlueprint<BlueprintEncyclopediaPage>(guid);
            if (existing != null)
            {
                try { existing.name = internalName; } catch { }
                // 确保标题与正文绑定，并且存在文本块
                BindTitleAndDescription(existing, titleKey, textKey);
                EnsureTextBlock(existing, textKey);
                return;
            }

            // 页面实例使用反射/未初始化对象（避免 ScriptableObject 泛型限制）
            BlueprintEncyclopediaPage page = null;
            try { page = (BlueprintEncyclopediaPage)Activator.CreateInstance(typeof(BlueprintEncyclopediaPage), nonPublic: true); } catch { }
            if (page == null)
            {
                try { page = (BlueprintEncyclopediaPage)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintEncyclopediaPage)); } catch { }
            }
            if (page == null) { Main.Log("[ZekariusGlossary] Failed to create page instance for " + internalName); return; }

            try { page.name = internalName; } catch { }
            AssignGuidSafe(page, guid);

            InitializeLocalizedField(page, "m_Title");
            InitializeLocalizedField(page, "m_Description");

            BindTitleAndDescription(page, titleKey, textKey);
            EnsureTextBlock(page, textKey);

            try
            {
                // 1) GUID注册
                ResourcesLibrary.BlueprintsCache?.AddCachedBlueprint(guid, page);

                // 2) 名称索引同步（某些版本/加载器依赖 name 索引）
                var cache = ResourcesLibrary.BlueprintsCache;
                if (cache != null)
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                    var byName = cache.GetType().GetField("m_BlueprintsByName", flags)?.GetValue(cache) as System.Collections.IDictionary;
                    if (byName != null)
                    {
                        try { byName[internalName] = page; } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log("[ZekariusGlossary] AddCachedBlueprint error: " + ex.Message);
            }
        }

        private static void BindTitleAndDescription(BlueprintEncyclopediaPage page, string titleKey, string textKey)
        {
            try
            {
                LocalizationInjector.BindKeyAndText(page, "m_Title", titleKey, LocalizationInjector.GetFallback(titleKey));
                LocalizationInjector.BindKeyAndText(page, "m_Description", textKey, LocalizationInjector.GetFallback(textKey));
            }
            catch { }
        }

        private static void EnsureTextBlock(BlueprintEncyclopediaPage page, string textKey)
        {
            try
            {
                if (page.Blocks == null)
                {
                    page.Blocks = new System.Collections.Generic.List<BlueprintEncyclopediaBlock>();
                }
                // 检查是否已有文本块
                foreach (var b in page.Blocks)
                {
                    var bt = b as BlueprintEncyclopediaBlockText;
                    if (bt != null) return; // 已有文本块即可
                }
                // 创建一个文本块并绑定 LocalizedString 文本（必须用 ScriptableObject.CreateInstance）
                BlueprintEncyclopediaBlockText block = null;
                try { block = ScriptableObject.CreateInstance<BlueprintEncyclopediaBlockText>(); } catch { }
                if (block == null)
                {
                    try { block = (BlueprintEncyclopediaBlockText)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintEncyclopediaBlockText)); } catch { }
                }
                if (block == null) return;

                // 绑定 LocalizedString 到 Text 字段（通过反射设置私有 m_Key）
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var fiText = typeof(BlueprintEncyclopediaBlockText).GetField("m_Text", flags);
                if (fiText != null)
                {
                    var loc = new LocalizedString();
                    try
                    {
                        var fiKey = typeof(LocalizedString).GetField("m_Key", flags);
                        fiKey?.SetValue(loc, textKey);
                    }
                    catch { }
                    fiText.SetValue(block, loc);
                }
                page.Blocks.Add(block);
            }
            catch (Exception ex)
            {
                Main.Log("[ZekariusGlossary] EnsureTextBlock error: " + ex);
            }
        }

        private static void InitializeLocalizedField(object bp, string fieldName)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var fi = bp.GetType().GetField(fieldName, flags);
                if (fi == null) return;
                var current = fi.GetValue(bp);
                if (current != null) return;
                var locType = fi.FieldType;
                object loc = null;
                try { loc = Activator.CreateInstance(locType, nonPublic: true); } catch { }
                if (loc == null)
                {
                    try { loc = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(locType); } catch { }
                }
                if (loc == null) return;
                fi.SetValue(bp, loc);
            }
            catch { }
        }

        private static void AssignGuidSafe(SimpleBlueprint bp, BlueprintGuid guid)
        {
            try
            {
                var fPublic = bp.GetType().GetField("AssetGuid", BindingFlags.Instance | BindingFlags.Public);
                if (fPublic != null) { fPublic.SetValue(bp, guid); return; }
            }
            catch { }
            try
            {
                foreach (var f in bp.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (f.FieldType == typeof(BlueprintGuid))
                    {
                        try { f.SetValue(bp, guid); return; } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
