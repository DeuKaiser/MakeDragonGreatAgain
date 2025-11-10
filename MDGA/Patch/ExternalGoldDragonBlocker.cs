using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints.JsonSystem; // BlueprintsCache

namespace MDGA.Patch
{
    /// <summary>
    /// 兼容：在 BlueprintsCache.Init 的前置阶段（Prefix，最高优先级）关闭外部 PATH_OF_BLING 模组的“添加金龙神话法术书”开关，
    /// 防止其在我们自定义合书逻辑之前插入额外的金龙法术书蓝图。只针对设置字段，不做侵入式修改；失败时仅记录日志。
    /// 条件：本模组已启用且用户开启金龙合书功能；否则不干预。
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
                // 仅在本模组启用且合并特性被用户所需时才进行处理
                if (!Main.Enabled) return;
                if (!Main.Settings.EnableGoldenDragonMerge) return; // 用户未使用合并特性 -> 不进行干预

                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "WOTR_PATH_OF_BLING", StringComparison.OrdinalIgnoreCase));
                if (asm == null)
                {
                    // 无需阻止任何操作
                    return;
                }

                // 查找 Main 类型
                var mainType = asm.GetType("WOTR_PATH_OF_BLING.Main");
                if (mainType == null)
                {
                    Main.Log("[Compat] Detected PATH_OF_BLING assembly but failed to locate Main type.");
                    return;
                }

                // 获取静态字段 'settings'
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

                // 查找设置中嵌套类的 AddGoldDragonSpellbook 字段
                var addBookField = settingsObj.GetType().GetField("AddGoldDragonSpellbook", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (addBookField == null)
                {
                    Main.Log("[Compat] PATH_OF_BLING AddGoldDragonSpellbook field not found.");
                    return;
                }

                // 如果已经是 false，则无需操作
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
