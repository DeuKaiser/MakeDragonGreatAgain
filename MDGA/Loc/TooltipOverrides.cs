using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Controllers.Dialog;
using Kingmaker.UI.MVVM._VM.Tooltip.Utils;
using Owlcat.Runtime.UI.Tooltips;
using UnityEngine;

namespace MDGA.Loc
{
    // 仅增加详细诊断日志，用于确定实际命中的重载与参数位置，不改变行为路径。
    internal static class TooltipOverrides
    {
        private static void SafeLog(string msg)
        {
            try { if (Main.Enabled) Main.Log(msg); } catch { }
        }

        private static string FormatArg(object o)
        {
            try
            {
                if (o == null) return "<null>";
                var t = o.GetType();
                if (o is string s) return $"string:'{s}'";
                if (o is string[] arr) return $"string[]:[{string.Join(",", arr.Select(x => x ?? "<null>"))}]";
                if (t.IsValueType) return $"{t.Name}:{o}";
                return $"{t.FullName}";
            }
            catch { return "<err>"; }
        }

        [HarmonyPatch(
            typeof(TooltipHelper),
            nameof(TooltipHelper.ShowLinkTooltip),
            new Type[]
            {
                typeof(MonoBehaviour),
                typeof(string),
                typeof(List<SkillCheckDC>),
                typeof(List<SkillCheckResult>),
                typeof(TooltipConfig)
            })]
        private static class Patch_ShowLinkTooltip_String
        {
            [HarmonyPrefix]
            private static bool Prefix(MonoBehaviour component, string key, TooltipConfig config)
            {
                return HandleLinkTooltip(component, new[] { key }, config);
            }
        }

        [HarmonyPatch(
            typeof(TooltipHelper),
            nameof(TooltipHelper.ShowLinkTooltip),
            new Type[]
            {
                typeof(MonoBehaviour),
                typeof(string[]),
                typeof(List<SkillCheckDC>),
                typeof(List<SkillCheckResult>),
                typeof(TooltipConfig)
            })]
        private static class Patch_ShowLinkTooltip_Keys
        {
            [HarmonyPrefix]
            private static bool Prefix(MonoBehaviour component, string[] keys, TooltipConfig config)
            {
                return HandleLinkTooltip(component, keys, config);
            }
        }

        private static bool HandleLinkTooltip(MonoBehaviour component, IEnumerable<string> keys, TooltipConfig config)
        {
            try
            {
                var key = (keys ?? Enumerable.Empty<string>())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .Select(k => k.StartsWith("Encyclopedia:", StringComparison.OrdinalIgnoreCase)
                        ? k.Substring("Encyclopedia:".Length)
                        : k)
                    .FirstOrDefault(k => k.StartsWith("MDGA_Zekarius_", StringComparison.OrdinalIgnoreCase));

                if (key == null) return true;

                var text = LocalizationInjector.GetFallback(key);
                var attempted = TryShowSimpleTooltip(component, text, config);
                SafeLog($"[TooltipDiag] key='{key}' fallbackTextLen={(text == null ? -1 : text.Length)} attempted={attempted}");
                return !attempted;
            }
            catch (Exception ex)
            {
                SafeLog($"[TooltipDiag] Prefix error: {ex.Message}");
                return true;
            }
        }

        // 保持原有的简单模板逻辑实现
        private static bool TryShowSimpleTooltip(MonoBehaviour component, string text, TooltipConfig config)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || component == null) return false;
                var tSimple = AccessTools.TypeByName("Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateSimple")
                             ?? AccessTools.TypeByName("Owlcat.Runtime.UI.Tooltips.TooltipTemplateSimple");
                if (tSimple == null) { SafeLog("[TooltipDiag] TooltipTemplateSimple type missing"); return false; }
                object template = null;
                var ctor = tSimple.GetConstructor(new[] { typeof(string) });
                if (ctor != null) template = ctor.Invoke(new object[] { text });
                else
                {
                    template = Activator.CreateInstance(tSimple);
                    var tf = tSimple.GetField("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (tf != null) tf.SetValue(template, text);
                }
                if (template == null) { SafeLog("[TooltipDiag] Template create failed"); return false; }
                var mi = typeof(TooltipHelper).GetMethod("ShowTooltip", BindingFlags.Public | BindingFlags.Static);
                if (mi == null) { SafeLog("[TooltipDiag] TooltipHelper.ShowTooltip missing"); return false; }
                mi.Invoke(null, new object[] { component, template, config });
                return true;
            }
            catch (Exception ex)
            {
                SafeLog("[TooltipDiag] TryShowSimpleTooltip error: " + ex.Message);
                return false;
            }
        }
    }
}
