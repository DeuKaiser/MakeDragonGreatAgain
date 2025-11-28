using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Prerequisites;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Localization;
using MDGA.Loc;
using UnityEngine;

namespace MDGA.GoldDragonMythic
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonBloodActivation
    {
        private static readonly BlueprintGuid GoldBreathSelectionGuid = BlueprintGuid.Parse("81fe63677a4f4f7fb64e9be7cf075948");
        private static readonly BlueprintGuid NewSelectionGuid = BlueprintGuid.Parse("f3d6a1a3b8b14e9c9a307163a7a5dd11");
        private static readonly BlueprintGuid GoldenDragonProgressionGuid = BlueprintGuid.Parse("a6fbca43902c6194c947546e89af64bd");

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

        private static bool _done;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            try
            {
                var srcSel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(GoldBreathSelectionGuid);
                var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(GoldenDragonProgressionGuid);
                if (srcSel == null || prog == null)
                {
                    Main.Log("[DragonBloodActivation] Source selection or progression not found.");
                    return;
                }

                var newSel = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelection>(NewSelectionGuid);
                if (newSel == null)
                {
                    newSel = CreateSelectionFrom(srcSel);
                    Register(newSel);
                    Localize(newSel);
                }

                InjectIntoProgression(prog, srcSel, newSel);
            }
            catch (Exception ex)
            {
                Main.Log("[DragonBloodActivation] Exception: " + ex);
            }
        }

        private static BlueprintFeatureSelection CreateSelectionFrom(BlueprintFeatureSelection src)
        {
            BlueprintFeatureSelection sel;
            try { sel = (BlueprintFeatureSelection)Activator.CreateInstance(typeof(BlueprintFeatureSelection)); }
            catch { sel = (BlueprintFeatureSelection)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BlueprintFeatureSelection)); }

            sel.name = "DragonBloodActivationSelection";
            SetGuid(sel, NewSelectionGuid);
            sel.IsClassFeature = true;
            sel.Ranks = 1;
            try
            {
                var iconField = typeof(BlueprintUnitFact).GetField("m_Icon", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                iconField?.SetValue(sel, iconField?.GetValue(src));
            }
            catch { }

            try
            {
                var f1 = typeof(BlueprintFeatureSelection).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var f2 = typeof(BlueprintFeatureSelection).GetField("m_AllFeatures", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                f1?.SetValue(sel, f1?.GetValue(src));
                f2?.SetValue(sel, f2?.GetValue(src));
            }
            catch { }

            try
            {
                var comp = new PrerequisiteFeaturesFromList
                {
                    Group = Prerequisite.GroupType.Any,
                    Amount = 1,
                    CheckInProgression = true,   // 未满足时不应可见/可选
                    HideInUI = true,              // 隐藏界面
                };
                var refs = DraconicRequisiteGuids
                    .Select(g => ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(g))
                    .Where(b => b != null)
                    .Select(b => b.ToReference<BlueprintFeatureReference>())
                    .ToArray();
                var field = typeof(PrerequisiteFeaturesFromList).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                field?.SetValue(comp, refs);
                AddComponent(sel, comp);
            }
            catch { }

            return sel;
        }

        private static void InjectIntoProgression(BlueprintProgression prog, BlueprintFeatureSelection oldSel, BlueprintFeatureSelection newSel)
        {
            try
            {
                var entries = prog.LevelEntries; if (entries == null) return;
                var fi = typeof(LevelEntry).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var level1 = entries.FirstOrDefault(le => le.Level == 1); if (level1 == null) return;
                var raw = fi?.GetValue(level1);
                var list = raw is BlueprintFeatureBaseReference[] arr ? arr.ToList() : raw as System.Collections.Generic.List<BlueprintFeatureBaseReference> ?? new System.Collections.Generic.List<BlueprintFeatureBaseReference>();

                // Keep the original breath selection, append an extra selection for draconic characters
                if (!list.Any(r => r != null && r.Get() == newSel))
                {
                    list.Add(newSel.ToReference<BlueprintFeatureBaseReference>());
                }
                if (fi.FieldType.IsArray) fi.SetValue(level1, list.ToArray()); else fi.SetValue(level1, list);
                Main.Log("[DragonBloodActivation] Inserted DragonBloodActivation at L1 (kept original breath selection).");
            }
            catch (Exception ex)
            {
                Main.Log("[DragonBloodActivation] InjectIntoProgression error: " + ex.Message);
            }
        }

        private static void Register(SimpleBlueprint bp)
        {
            try { ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp); }
            catch (Exception ex) { Main.Log("[DragonBloodActivation] Register exception: " + ex.Message); }
        }

        private static void Localize(BlueprintFeatureSelection sel)
        {
            try
            {
                const string nameZh = "龙血活化";
                const string descZh = "在龙神的注视下，体内的龙血得到活化，对吐息的掌控力进一步强化";
                const string nameEn = "Dragonblood Activation";
                const string descEn = "Under the Dragon God's gaze, the draconic blood within you is awakened, further enhancing your command over your breath weapon.";

                string nameKeyZh = "MDGA_GD_DragonBloodActivation_Name_zh";
                string descKeyZh = "MDGA_GD_DragonBloodActivation_Desc_zh";
                string nameKeyEn = "MDGA_GD_DragonBloodActivation_Name_en";
                string descKeyEn = "MDGA_GD_DragonBloodActivation_Desc_en";

                LocalizationInjector.RegisterDynamicKey(nameKeyZh, nameZh);
                LocalizationInjector.RegisterDynamicKey(descKeyZh, descZh);
                LocalizationInjector.RegisterDynamicKey(nameKeyEn, nameEn);
                LocalizationInjector.RegisterDynamicKey(descKeyEn, descEn);

                var fName = typeof(BlueprintUnitFact).GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var fDesc = typeof(BlueprintUnitFact).GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var nameLoc = Activator.CreateInstance(fName.FieldType);
                var descLoc = Activator.CreateInstance(fDesc.FieldType);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                bool zh = IsChinese();
                var chosenNameKey = zh ? nameKeyZh : nameKeyEn;
                var chosenDescKey = zh ? descKeyZh : descKeyEn;
                var chosenName = zh ? nameZh : nameEn;
                var chosenDesc = zh ? descZh : descEn;

                nameLoc.GetType().GetField("m_Key", flags)?.SetValue(nameLoc, chosenNameKey);
                nameLoc.GetType().GetField("m_Text", flags)?.SetValue(nameLoc, chosenName);
                descLoc.GetType().GetField("m_Key", flags)?.SetValue(descLoc, chosenDescKey);
                descLoc.GetType().GetField("m_Text", flags)?.SetValue(descLoc, chosenDesc);
                fName.SetValue(sel, nameLoc); fDesc.SetValue(sel, descLoc);
                LocalizationInjector.EnsureInjected();
            }
            catch (Exception ex)
            {
                Main.Log("[DragonBloodActivation] Localize error: " + ex.Message);
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

        private static void SetGuid(SimpleBlueprint bp, BlueprintGuid guid)
        {
            var f = bp.GetType().GetField("AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? bp.GetType().GetField("m_AssetGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? bp.GetType().GetField("m_Guid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            f?.SetValue(bp, guid);
        }

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
    }
}
