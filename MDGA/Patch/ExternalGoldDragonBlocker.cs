using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints.JsonSystem; // BlueprintsCache

namespace MDGA.Patch
{
    /// <summary>
    /// 策略 A：在其 BlueprintsCache.Init 的 Postfix 运行之前，通过关闭其设置项，阻止外部 PATH_OF_BLING 添加自带的金龙法术书。
    /// 我们以 Prefix 运行，使用 Priority.First 且 HarmonyBefore("WOTR_PATH_OF_BLING").
    /// 安全（无硬依赖）。若其结构变更，我们只记录日志并继续。
    /// </summary>
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("WOTR_PATH_OF_BLING")] // ensure we run before that mod's patches
    internal static class ExternalGoldDragonBlocker
    {
        private static bool _attempted;

        static void Prefix()
        {
            if (_attempted) return; // run once
            _attempted = true;
            try
            {
                // Only act if our mod is enabled and merge feature desired.
                if (!Main.Enabled) return;
                if (!Main.Settings.EnableGoldenDragonMerge) return; // user not using merge -> don't interfere

                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "WOTR_PATH_OF_BLING", StringComparison.OrdinalIgnoreCase));
                if (asm == null)
                {
                    // nothing to block
                    return;
                }

                // Find Main type
                var mainType = asm.GetType("WOTR_PATH_OF_BLING.Main");
                if (mainType == null)
                {
                    Main.Log("[Compat] Detected PATH_OF_BLING assembly but failed to locate Main type.");
                    return;
                }

                // Get static field 'settings'
                var settingsField = mainType.GetField("settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (settingsField == null)
                {
                    Main.Log("[Compat] PATH_OF_BLING.Main.settings field not found.");
                    return;
                }
                var settingsObj = settingsField.GetValue(null);
                if (settingsObj == null)
                {
                    Main.Log("[Compat] PATH_OF_BLING settings is null (maybe not loaded yet).");
                    return;
                }

                // Field AddGoldDragonSpellbook inside their Settings nested class
                var addBookField = settingsObj.GetType().GetField("AddGoldDragonSpellbook", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (addBookField == null)
                {
                    Main.Log("[Compat] PATH_OF_BLING AddGoldDragonSpellbook field not found.");
                    return;
                }

                // If already false, nothing to do
                var current = addBookField.GetValue(settingsObj) as bool?;
                if (current == true)
                {
                    addBookField.SetValue(settingsObj, false);
                    Main.Log("[Compat] Disabled PATH_OF_BLING AddGoldDragonSpellbook before its patch executes.");
                }
                else
                {
                    Main.Log("[Compat] PATH_OF_BLING AddGoldDragonSpellbook already disabled (value=" + current + ").");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[Compat] Exception while disabling external Gold Dragon spellbook: " + ex.Message);
            }
        }
    }
}
