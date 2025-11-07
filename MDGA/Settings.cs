using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Prerequisites;
using UnityEngine;
using UnityEngine.UI;
using UnityModManagerNet;

namespace MDGA
{
    public class Settings : UnityModManager.ModSettings
    {
        // Dragon Disciple tweaks
        public bool EnableDragonDiscipleFix = true;
        public bool DragonDiscipleFixLevel1 = false;
        public bool DragonDiscipleFullBAB = false;
        public bool VerboseLogging = false;
        public bool DragonDiscipleDiagnostic = false;

        // Golden Dragon merge options
        public bool EnableGoldenDragonMerge = true;      // master switch
        public bool GoldenDragonVerbose = false;         // detailed diagnostics
        public bool GoldenDragonOverrideAngelMergeText = true; // 当使用fallback复用天使合书时替换显示文本为金龙
        public bool GoldenDragonMergeHighSpells = true;  // 是否并入金龙 8/9/10 级独有神话法术

        // UI refresh (level-up) – default off to prevent spam; user can enable if some UI not updating after class selection
        public bool EnableUIRefresh = false;

        // Esoteric/secret bloodlines visibility in main selections (Sorcerer/Eldritch Scion/etc.)
        // Default false: do NOT inject EsotericDragons into main bloodline selections
        public bool AllowEsotericInMainBloodlineSelections = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}