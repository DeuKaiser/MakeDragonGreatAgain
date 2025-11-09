using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Facts;

namespace MDGA.Patch
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonDiscipleDiagnostics
    {
        private const string DDClass = "72051275b1dbb2d42ba9118237794f7c";
        private const string DDProgression = "69fc2bad2eb331346a6c777423e0d0f7";
        private static bool _ran;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_ran) return; _ran = true;
            if (!Main.Enabled) return;
            if (Main.Settings == null || !Main.Settings.VerboseLogging) return; // 仅在统一详细日志开启时输出
            try
            {
                var prog = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(DDProgression);
                var cls = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(DDClass);
                if (prog == null || cls == null)
                {
                    Main.Log("[DDDiag] Missing class or progression blueprint.");
                    return;
                }
                Main.Log("[DDDiag] ==== Dragon Disciple Progression Dump BEGIN ====");
                var entries = prog.LevelEntries;
                if (entries == null)
                {
                    Main.Log("[DDDiag] No LevelEntries");
                    return;
                }
                int spellAdvTotal = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    var le = entries[i];
                    Main.Log($"[DDDiag] -- Level {le.Level} --");
                    var features = ExtractFirstLayerFeatures(le);
                    if (features.Length == 0) Main.Log("[DDDiag]   (no direct features)");
                    foreach (var f in features)
                    {
                        if (f == null) { Main.Log("[DDDiag]   <null feature>"); continue; }
                        bool hasAdd = SafeHasAddSpellbook(f);
                        if (hasAdd) spellAdvTotal++;
                        Main.Log($"[DDDiag]   Feature {f.name} guid={f.AssetGuidThreadSafe} AddSpellbook={hasAdd}");
                        DumpSelectionChildren(f, 2);
                    }
                }
                Main.Log($"[DDDiag] Total direct AddSpellbookLevel features counted (first + selection layers): {spellAdvTotal}");
                Main.Log("[DDDiag] ==== Dragon Disciple Progression Dump END ====");
            }
            catch (Exception ex)
            {
                Main.Log("[DDDiag] Exception: " + ex);
            }
        }

        private static BlueprintFeatureBase[] ExtractFirstLayerFeatures(LevelEntry le)
        {
            try
            {
                var fi = typeof(LevelEntry).GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi != null)
                {
                    var raw = fi.GetValue(le);
                    if (raw is BlueprintFeatureBase[] arr) return arr;
                }
                var fi2 = typeof(LevelEntry).GetField("Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi2 != null)
                {
                    var raw = fi2.GetValue(le);
                    if (raw is BlueprintFeatureBase[] arr2) return arr2;
                }
                var pi = typeof(LevelEntry).GetProperty("Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (pi != null)
                {
                    var raw = pi.GetValue(le);
                    if (raw is BlueprintFeatureBase[] arr3) return arr3;
                }
            }
            catch { }
            return Array.Empty<BlueprintFeatureBase>();
        }

        private static void DumpSelectionChildren(BlueprintFeatureBase feat, int indent)
        {
            var t = feat.GetType();
            if (!t.Name.Contains("Selection")) return;
            try
            {
                var fiAll = t.GetField("m_AllFeatures", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var fiFeats = t.GetField("m_Features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var refs = new System.Collections.Generic.List<BlueprintFeatureReference>();
                if (fiAll?.GetValue(feat) is BlueprintFeatureReference[] all) refs.AddRange(all);
                if (fiFeats?.GetValue(feat) is BlueprintFeatureReference[] feats) refs.AddRange(feats);
                if (refs.Count == 0) return;
                string pad = new string(' ', indent * 2);
                foreach (var r in refs.Distinct())
                {
                    BlueprintFeatureBase child = null;
                    try { child = r.Get(); } catch { }
                    if (child == null) { Main.Log($"[DDDiag]{pad}- child <null>"); continue; }
                    bool hasAdd = SafeHasAddSpellbook(child);
                    Main.Log($"[DDDiag]{pad}- child Feature {child.name} guid={child.AssetGuidThreadSafe} AddSpellbook={hasAdd}");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[DDDiag] Selection dump exception: " + ex.Message);
            }
        }

        private static bool SafeHasAddSpellbook(BlueprintFeatureBase f)
        {
            try { return f.GetComponents<AddSpellbookLevel>().Any(); } catch { return false; }
        }
    }
}
