using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.UI.MVVM._VM.Tooltip.Utils;
using Owlcat.Runtime.UI.Tooltips;
using UnityEngine;

namespace MDGA.Loc
{
    // Tooltip 诊断补丁（降级版）：仅在 TooltipHelper 和 TooltipEncyclopediaTemplate 构造处记录关键信息，避免影响存档与流程。
    internal static class TooltipDiagnostics
    {
        private static bool Verbose => Main.Enabled && Main.Settings != null && Main.Settings.VerboseLogging;
        private static void SafeLog(string msg) { try { if (Verbose) Main.Log(msg); } catch { } }

        private static void DumpKeyResolution(string from, IEnumerable<string> keys)
        {
            try
            {
                if (!Verbose) return;
                var all = new List<string>();
                foreach (var k in keys.Where(k => !string.IsNullOrEmpty(k)))
                {
                    all.Add(k);
                    if (!k.StartsWith("Encyclopedia:", StringComparison.OrdinalIgnoreCase)) all.Add("Encyclopedia:" + k);
                    if (k.StartsWith("Encyclopedia:", StringComparison.OrdinalIgnoreCase)) all.Add(k.Substring("Encyclopedia:".Length));
                }
                all = all.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                IDictionary dict = null; Type seType = null; FieldInfo textField = null;
                try
                {
                    var pack = typeof(Kingmaker.Localization.LocalizationManager)
                        .GetProperty("CurrentPack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
                    if (pack != null)
                    {
                        var df = pack.GetType().GetField("m_Strings", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        dict = df?.GetValue(pack) as IDictionary;
                        var se = pack.GetType().GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                                     .FirstOrDefault(t => t.Name.Contains("StringEntry"));
                        seType = se;
                        if (seType != null)
                            textField = seType.GetField("Text", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                     ?? seType.GetField("m_Text", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    }
                }
                catch { }

                SafeLog($"[TooltipDiag] ==== Resolve from {from} ====");
                foreach (var key in all)
                {
                    string packStat = "pack:NA";
                    if (dict != null)
                    {
                        if (dict.Contains(key))
                        {
                            var entry = dict[key];
                            string len = "?";
                            try { var txt = textField?.GetValue(entry) as string; len = (txt == null ? "null" : txt.Length.ToString()); } catch { }
                            packStat = $"pack:hit(len={len})";
                        }
                        else packStat = "pack:miss";
                    }
                    SafeLog($"[TooltipDiag] key='{key}' => {packStat}");
                }
                SafeLog("[TooltipDiag] ============================");
            }
            catch { }
        }

        // 仅记录 TooltipHelper.ShowLinkTooltip 重载调用
        [HarmonyPatch]
        private static class Patch_All_ShowLinkTooltip
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var t = typeof(TooltipHelper);
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == nameof(TooltipHelper.ShowLinkTooltip)) yield return m;
                }
            }

            [HarmonyPostfix]
            private static void Postfix(object[] __args, MethodBase __originalMethod)
            {
                try
                {
                    if (!Verbose) return;
                    string one = null; string[] many = null;
                    for (int i = 0; i < (__args?.Length ?? 0); i++)
                    {
                        if (__args[i] is string s) { one = s; break; }
                        if (__args[i] is string[] arr) { many = arr; break; }
                    }
                    if (many != null)
                    {
                        SafeLog($"[TooltipDiag] {__originalMethod.Name}(keys=[{string.Join(",", many)}])");
                        DumpKeyResolution($"{__originalMethod.Name}[arr]", many);
                    }
                    else
                    {
                        SafeLog($"[TooltipDiag] {__originalMethod.Name}(key='{one}')");
                        if (!string.IsNullOrEmpty(one)) DumpKeyResolution($"{__originalMethod.Name}[str]", new[] { one });
                    }
                }
                catch { }
            }
        }

        // 仅记录 TooltipEncyclopediaTemplate 的构造入参
        [HarmonyPatch]
        private static class Patch_TooltipEncyclopediaTemplate_Ctors
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var t = typeof(Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipEncyclopediaTemplate);
                foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) yield return ctor;
            }

            [HarmonyPostfix]
            private static void Postfix(object[] __args)
            {
                try
                {
                    if (!Verbose) return;
                    var keys = new List<string>();
                    foreach (var a in __args ?? Array.Empty<object>())
                    {
                        if (a is string s && !string.IsNullOrEmpty(s)) keys.Add(s);
                        else if (a is string[] arr && arr.Length > 0) keys.AddRange(arr.Where(x => !string.IsNullOrEmpty(x)));
                    }
                    SafeLog($"[TooltipDiag] TooltipEncyclopediaTemplate::.ctor keys=[{string.Join(",", keys)}]");
                    if (keys.Count > 0) DumpKeyResolution("Template::.ctor", keys);
                }
                catch { }
            }
        }
    }
}
