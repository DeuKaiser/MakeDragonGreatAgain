using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.Facts; // added for BlueprintUnitFact
using Kingmaker.Localization; // locale detection
using UnityEngine;
using MDGA.Loc;

namespace MDGA.GeneralClasses.DragonheirScion
{
    // 覆写 DragonicDefences* 特性的描述文本，匹配数值翻倍后的结果。
    // 使用 LocalizationInjector 绑定动态 key，以抵抗 QuickLocalization 覆盖。
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonheirDefencesDescriptionOverride
    {
        private static bool _done;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            try
            {
                // 目标中文与英文文本
                string zh = "3级起，龙之贵胄获得+2天生{g|Encyclopedia:Armor_Class}防御{/g}{g|Encyclopedia:Bonus}加值{/g}，以及10点对应她的能量类型的{g|Encyclopedia:Energy_Resistance}抗性{/g}。7级起，天生防御加值提高到+4，能量抗性提高到20点。13级起，天生防御加值提高到+6，能量抗性提高到40点。";
                string en = "At 3rd level, a dragonheir scion gains a +2 natural armor {g|Encyclopedia:Bonus}bonus{/g} to {g|Encyclopedia:Armor_Class}AC{/g} and energy {g|Encyclopedia:Energy_Resistance}resistance{/g} 10 against her energy type. At 7th level, this increases to a +4 natural armor bonus and energy resistance 20; at 13th level, it increases to a +6 natural armor bonus and energy resistance 40.";

                bool isZh = IsChinese();
                string descText = isZh ? zh : en;

                var progGuids = new[]
                {
                    BlueprintGuid.Parse("5172cdce55b2455f878ff8c74c964a1e"), // Gold
                    BlueprintGuid.Parse("db69413e69184b099f7825092c5dbc4f"), // Green
                    BlueprintGuid.Parse("267dbd4789fa4b75a44294c7d1625bba"), // Red
                    BlueprintGuid.Parse("b6a90286e6894c7b8fbd52a29dda9f48"), // Silver
                    BlueprintGuid.Parse("1bdad91f7210419b9e5b4801d084e14b"), // White
                    BlueprintGuid.Parse("6127949882384a5cb75074e4d77ceae3"), // Black
                    BlueprintGuid.Parse("3da0972ad5574bfd8a7de9bc5460e7e9"), // Blue
                    BlueprintGuid.Parse("47f3f1a3349349fe9a7f68f8d2b6da5d"), // Brass
                    BlueprintGuid.Parse("df2807f0649c4237ab7c4d62a2acaaee"), // Bronze
                    BlueprintGuid.Parse("416ee6e6b4834bb8bd5afe8b08a69865"), // Copper
                };

                int patched = 0;
                foreach (var gid in progGuids)
                {
                    var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(gid);
                    if (prog == null) continue;
                    foreach (var le in prog.LevelEntries)
                    {
                        if (le == null) continue;
                        if (le.Level != 3) continue; // DragonicDefences 在 3 级
                        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        var fiFeatures = typeof(LevelEntry).GetField("m_Features", flags);
                        var featuresObj = fiFeatures?.GetValue(le);
                        if (featuresObj == null) continue;
                        foreach (var fref in (System.Collections.IEnumerable)featuresObj)
                        {
                            BlueprintFeature feat = null;
                            var miGet = fref?.GetType().GetMethod("Get", flags);
                            if (miGet != null) feat = miGet.Invoke(fref, null) as BlueprintFeature;
                            if (feat == null) continue;
                            if (!feat.name.StartsWith("DragonicDefences", StringComparison.OrdinalIgnoreCase)) continue;

                            try
                            {
                                // 绑定动态本地化：显示名沿用原值，描述改为新文本（按语言二选一）。
                                string displayName = null;
                                try {
                                    var f = typeof(BlueprintUnitFact).GetField("m_DisplayName", flags);
                                    var loc = f?.GetValue(feat);
                                    displayName = loc?.ToString() ?? feat.name;
                                } catch { displayName = feat.name; }
                                LocalizationInjector.RegisterFeatureLocalization(feat, displayName, descText);
                                patched++;
                            }
                            catch { }
                        }
                    }
                }
                if (Main.Settings.VerboseLogging) Main.Log("[DragonheirDefencesDesc] Patched descriptions: " + patched + " (locale=" + (isZh?"zh":"en") + ")");
            }
            catch (Exception ex)
            {
                Main.Log("[DragonheirDefencesDesc] Error: " + ex.Message);
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
                if (Application.systemLanguage == SystemLanguage.ChineseSimplified || Application.systemLanguage == SystemLanguage.Chinese || Application.systemLanguage == SystemLanguage.ChineseTraditional)
                    return true;
            }
            catch { }
            return false;
        }
    }
}
