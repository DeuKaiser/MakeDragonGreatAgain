using HarmonyLib;
using System.Reflection;
using UnityModManagerNet;
using MDGA.AutoMerge; // 引入以调用延迟执行
using System.IO;
using UnityEngine;
using MDGA.Patch; // localization injector

namespace MDGA
{
    static class Main
    {
        public static UnityModManager.ModEntry ModEntry;
        public static bool Enabled;
        private static Harmony _harmony;
        public static Settings Settings;

        internal static void Log(string msg)
        {
            ModEntry?.Logger.Log("[MDGA] " + msg);
        }

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry) ?? new Settings();
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.Logger.Log("[MDGA] Load() begin. Mod path=" + modEntry.Path);

            var safeModeFile = Path.Combine(modEntry.Path, "SAFE_MODE");
            var safeModeFileLower = Path.Combine(modEntry.Path, "safe_mode");
            bool safe = File.Exists(safeModeFile) || File.Exists(safeModeFileLower) || Directory.Exists(safeModeFile) || Directory.Exists(safeModeFileLower);
            if (safe)
            {
                modEntry.Logger.Log("[MDGA] SAFE_MODE detected (file or folder). Skip Harmony.PatchAll.");
                modEntry.OnToggle = OnToggle;
                Enabled = true;
                // still try localization injection even in safe mode (non-invasive)
                TryEarlyLocalizationInjection();
                return true;
            }

            _harmony = new Harmony(modEntry.Info.Id);
            try
            {
                Log("Patching assembly...");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log("PatchAll finished.");
            }
            catch (System.Exception ex)
            {
                Log("PatchAll exception: " + ex);
            }
            modEntry.OnToggle = OnToggle;
            Log("Loaded and patched.");
            GoldDragonAutoMerge.TryRunAfterUMMLoad();
            TryEarlyLocalizationInjection();
            ArcanaLateDescriptionFix.Ensure();
            return true;
        }

        private static void TryEarlyLocalizationInjection()
        {
            try
            {
                Log("[LocInit] Early EnsureInjected start");
                LocalizationInjector.EnsureInjected();
                LocalizationInjector.InstallWatcher();
                LocalizationInjector.StartDelayed();
            }
            catch (System.Exception ex)
            {
                Log("[LocInit] Early localization injection error: " + ex.Message);
            }
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            Log("Toggled: " + value);
            if (value)
            {
                GoldDragonAutoMerge.TryRunAfterUMMLoad();
                TryEarlyLocalizationInjection();
                LocalizationInjector.StartDelayed();
                ArcanaLateDescriptionFix.Ensure();
            }
            return true;
        }

        static void OnGUI(UnityModManager.ModEntry entry)
        {
            GUILayout.Label("Make Dragon Great Again - Settings", UnityEngine.GUI.skin.label);
            Settings.EnableDragonDiscipleFix = GUILayout.Toggle(Settings.EnableDragonDiscipleFix, "启用 龙脉术士 5/9 级施法进阶修复 (需要重启)" );
            Settings.DragonDiscipleFixLevel1 = GUILayout.Toggle(Settings.DragonDiscipleFixLevel1, "额外启用 1 级施法进阶 (10/10, 需要重启)" );
            Settings.DragonDiscipleFullBAB = GUILayout.Toggle(Settings.DragonDiscipleFullBAB, "龙脉术士使用全BAB进度 (需要重启)" );
            Settings.DragonDiscipleDiagnostic = GUILayout.Toggle(Settings.DragonDiscipleDiagnostic, "下次加载输出龙脉术士进阶诊断 (一次性, 需重启)" );
            Settings.VerboseLogging = GUILayout.Toggle(Settings.VerboseLogging, "详细日志 (调试用)" );
            GUILayout.Space(10);
            GUILayout.Label("Golden Dragon 合书", GUI.skin.box);
            Settings.EnableGoldenDragonMerge = GUILayout.Toggle(Settings.EnableGoldenDragonMerge, "启用 金龙合书 (需要重启)" );
            Settings.GoldenDragonMergeHighSpells = GUILayout.Toggle(Settings.GoldenDragonMergeHighSpells, "并入金龙 8/9/10 环专属法术 (关闭则仅用天使法表)" );
            Settings.GoldenDragonVerbose = GUILayout.Toggle(Settings.GoldenDragonVerbose, "金龙合书详细日志 (调试)" );
            Settings.GoldenDragonOverrideAngelMergeText = GUILayout.Toggle(Settings.GoldenDragonOverrideAngelMergeText, "（fallback时）覆盖天使合书文本" );
            GUILayout.Space(4);
            GUILayout.Label("LevelUp UI 自动刷新 (慎用)", GUI.skin.box);
            Settings.EnableUIRefresh = GUILayout.Toggle(Settings.EnableUIRefresh, "启用 选择职业后自动再执行一次机制重算 (避免显示卡死)" );
            GUILayout.Label("默认关闭。只有当升级面板显示未刷新时才开启，避免日志刷屏与性能浪费。", UnityEngine.GUI.skin.label);
            GUILayout.Space(8);
            GUILayout.Label("合书条件: 必须拥有龙族血统 + 目标职业具备 SpellList (固定, 不可关闭)", UnityEngine.GUI.skin.label);
            GUILayout.Label("(原先的两个可选开关已移除)", UnityEngine.GUI.skin.label);
            GUILayout.Label("更改部分设置后请重新启动游戏以确保蓝图缓存重新构建。", UnityEngine.GUI.skin.box);
        }

        static void OnSaveGUI(UnityModManager.ModEntry entry)
        {
            Settings.Save(entry);
        }
    }
}