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
        public bool EnableDragonDiscipleFix = true;     // 现在包含 1/5/9 级施法者等级修复
        public bool DragonDiscipleFullBAB = false;      // 保持单独开关
        public bool VerboseLogging = false;             // 统一的详细日志总开关

        // Golden Dragon merge options
        public bool EnableGoldenDragonMerge = true;     // master switch

        // UI refresh (level-up)
        public bool EnableUIRefresh = false;

        // Esoteric/secret bloodlines visibility in main selections
        public bool AllowEsotericInMainBloodlineSelections = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}