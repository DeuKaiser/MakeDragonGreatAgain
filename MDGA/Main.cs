using HarmonyLib;
using System.Reflection;
using UnityModManagerNet;
using MDGA.GoldDragonMythic; // 引入以调用延迟执行 (修复命名空间)
using System.IO;
using UnityEngine;
using MDGA.Loc; // localization injector
using MDGA.Mythic; // call TrueDragon.ApplyIfDcEnabled

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

                // 自检：看看 BlueprintsCache.Init 上有哪些 Postfix（用于确认 DragonheirLegacy 是否成功挂上去）
                try
                {
                    if (Settings != null && Settings.VerboseLogging)
                    {
                        var target = AccessTools.Method(typeof(Kingmaker.Blueprints.JsonSystem.BlueprintsCache), "Init");
                        if (target == null)
                        {
                            Log("[DragonheirLegacy][Diag] BlueprintsCache.Init method not found by AccessTools.");
                        }
                        else
                        {
                            var info = Harmony.GetPatchInfo(target);
                            if (info == null)
                            {
                                Log("[DragonheirLegacy][Diag] Harmony.GetPatchInfo returned null for BlueprintsCache.Init.");
                            }
                            else
                            {
                                var postfixes = info.Postfixes
                                    .Select(p => p.PatchMethod.DeclaringType != null ? p.PatchMethod.DeclaringType.FullName : p.PatchMethod.Name)
                                    .ToArray();
                                Log("[DragonheirLegacy][Diag] Postfixes on BlueprintsCache.Init: " + string.Join(", ", postfixes));
                            }
                        }
                    }
                }
                catch (System.Exception exDiag)
                {
                    if (Settings != null && Settings.VerboseLogging)
                        Log("[DragonheirLegacy][Diag] Error while checking patches: " + exDiag.Message);
                }
            }
            catch (System.Exception ex)
            {
                Log("PatchAll exception: " + ex);
            }
            modEntry.OnToggle = OnToggle;
            Log("Loaded and patched.");
            // 在加载完成后尝试应用 DC 真龙增强（若 DC 已启用）
            try { TrueDragon.ApplyIfDcEnabled(); } catch { }
            GoldDragonAutoMerge.TryRunAfterUMMLoad();
            TryEarlyLocalizationInjection();
            ArcanaLateDescriptionFix.Ensure();
            return true;
        }

        private static void TryEarlyLocalizationInjection()
        {
            try
            {
                if (Settings != null && Settings.VerboseLogging)
                    Log("[LocInit] Early EnsureInjected start");
                LocalizationInjector.EnsureInjected();
                LocalizationInjector.InstallWatcher();
                LocalizationInjector.StartDelayed();
            }
            catch (System.Exception ex)
            {
                if (Settings != null && Settings.VerboseLogging)
                    Log("[LocInit] Early localization injection error: " + ex.Message);
            }
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            Log("Toggled: " + value);
            if (value)
            {
                // 重新启用时也尝试应用真龙增强
                try { TrueDragon.ApplyIfDcEnabled(); } catch { }
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
            Settings.EnableDragonDiscipleFix = GUILayout.Toggle(Settings.EnableDragonDiscipleFix, "启用 龙脉术士 施法者等级修复 (涵盖 1/5/9, 需要重启)" );
            Settings.DragonDiscipleFullBAB = GUILayout.Toggle(Settings.DragonDiscipleFullBAB, "龙脉术士使用全BAB进度 (需要重启)" );
            Settings.VerboseLogging = GUILayout.Toggle(Settings.VerboseLogging, "详细日志 (调试用, 包含全部模块)" );
            GUILayout.Space(10);
            GUILayout.Label("Golden Dragon 合书", GUI.skin.box);
            Settings.EnableGoldenDragonMerge = GUILayout.Toggle(Settings.EnableGoldenDragonMerge, "启用 金龙合书 (需要重启)" );
            GUILayout.Space(4);
            GUILayout.Label("LevelUp UI 自动刷新 (慎用)", GUI.skin.box);
            Settings.EnableUIRefresh = GUILayout.Toggle(Settings.EnableUIRefresh, "启用 选择职业后自动再执行一次机制重算 (避免显示卡死)" );
            GUILayout.Label("默认关闭。只有当升级面板显示未刷新时才开启，避免日志刷屏与性能浪费。", UnityEngine.GUI.skin.label);
            GUILayout.Space(8);
            GUILayout.Label("合书条件: 必须拥有龙族血统 + 目标职业具备 SpellList (固定, 不可关闭)", UnityEngine.GUI.skin.label);
            GUILayout.Label("更改部分设置后请重新启动游戏以确保蓝图缓存重新构建。", UnityEngine.GUI.skin.box);
        }

        static void OnSaveGUI(UnityModManager.ModEntry entry)
        {
            Settings.Save(entry);
        }
    }
}