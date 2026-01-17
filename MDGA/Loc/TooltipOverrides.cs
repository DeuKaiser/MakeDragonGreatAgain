using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
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

        // Patch 所有 ShowLinkTooltip 重载：仅日志，不改逻辑除非是我们的键且成功改为简单模板（行为保持）。
        [HarmonyPatch]
        private static class Patch_ShowLinkTooltip_Override
        {
            static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
            {
                var t = typeof(TooltipHelper);
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == nameof(TooltipHelper.ShowLinkTooltip))
                    {
                        try
                        {
                            var ps = m.GetParameters();
                            var sig = string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            SafeLog($"[TooltipDiag] Hook ShowLinkTooltip overload: {m.Name}({sig})");
                        }
                        catch { }
                        yield return m;
                    }
                }
            }

            [HarmonyPrefix]
            private static bool Prefix(MethodBase __originalMethod, object[] __args)
            {
                try
                {
                    // 记录命中的重载和全部参数内容
                    var ps = __originalMethod?.GetParameters() ?? Array.Empty<ParameterInfo>();
                    var sig = string.Join(", ", ps.Select(p => p.ParameterType.Name));
                    SafeLog($"[TooltipDiag] Invoked ShowLinkTooltip overload ({sig}); argCount={__args?.Length ?? 0}");
                    if (__args != null)
                    {
                        for (int i = 0; i < __args.Length; i++)
                        {
                            SafeLog($"[TooltipDiag]  arg[{i}] => {FormatArg(__args[i])}");
                        }
                    }

                    // 原有处理：仅当键匹配时尝试简单模板，否则完全不改变行为
                    if (__args == null || __args.Length < 2) return true;
                    var second = __args[1];
                    var looksMdga = false;
                    if (second is string s) looksMdga = s.IndexOf("MDGA_Zekarius_", StringComparison.OrdinalIgnoreCase) >= 0;
                    else if (second is string[] arr) looksMdga = arr.Any(x => x != null && x.IndexOf("MDGA_Zekarius_", StringComparison.OrdinalIgnoreCase) >= 0);
                    SafeLog($"[TooltipDiag] looksMdga={looksMdga}");

                    if (!looksMdga) return true;

                    // 记录解析到的键值
                    string key = null;
                    if (second is string s2)
                    {
                        key = s2.StartsWith("Encyclopedia:", StringComparison.OrdinalIgnoreCase) ? s2.Substring("Encyclopedia:".Length) : s2;
                    }
                    else if (second is string[] a2)
                    {
                        key = a2.Select(k => k ?? "").Select(k => k.StartsWith("Encyclopedia:", StringComparison.OrdinalIgnoreCase) ? k.Substring("Encyclopedia:".Length) : k)
                                .FirstOrDefault(k => k.StartsWith("MDGA_Zekarius_", StringComparison.OrdinalIgnoreCase));
                    }
                    SafeLog($"[TooltipDiag] resolvedKey='{key}'");

                    var text = LocalizationInjector.GetFallback(key);
                    SafeLog($"[TooltipDiag] fallbackTextLen={(text==null? -1 : text.Length)}");

                    // 提取组件与配置并尝试简单模板显示
                    var comp = __args[0] as MonoBehaviour;
                    var cfgObj = __args.LastOrDefault();
                    var cfg = cfgObj is TooltipConfig c ? c : default;
                    var attempted = TryShowSimpleTooltip(comp, text, cfg);
                    SafeLog($"[TooltipDiag] TryShowSimpleTooltip attempted={attempted}");
                    if (attempted) return false; // 我们接管成功，跳过原逻辑
                }
                catch (Exception ex)
                {
                    SafeLog($"[TooltipDiag] Prefix error: {ex.Message}");
                }
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
