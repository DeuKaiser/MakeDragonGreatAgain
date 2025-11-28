using HarmonyLib;
using Kingmaker.UnitLogic.Class.LevelUp; // LevelUpController
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MDGA.Components
{
    internal static class LevelUpUiRefresher
    {
        // 节流与去抖：避免日志刷屏/重复重算循环
        private const int CooldownFrames = 45; // 在 60fps 下约 0.75s
        private static readonly Dictionary<int, int> LastScheduleFrame = new(); // key = 控制器哈希 ^ 标签哈希

        private class DelayedRefresh : MonoBehaviour
        {
            public LevelUpController Controller;
            public string Tag;
            void Update()
            {
                if (Controller == null) { Destroy(gameObject); return; }
                // 若用户关闭了设置中的开关，则直接停止
                if (!Main.Settings.EnableUIRefresh) { Destroy(gameObject); return; }
                try
                {
                    Controller.ApplyClassMechanics();
                    if (Main.Settings.VerboseLogging) Main.Log("[UIRefresh] ApplyClassMechanics re-run (" + Tag + ").");
                }
                catch (Exception ex) { Main.Log("[UIRefresh] Refresh exception: " + ex.Message); }
                Destroy(gameObject);
            }
        }

        private static void Schedule(LevelUpController c, string tag)
        {
            if (c == null) return;
            if (!Main.Settings.EnableUIRefresh) return; // 总开关
            // 去抖判断
            try
            {
                int key = unchecked(c.GetHashCode() ^ tag.GetHashCode());
                int now = Time.frameCount;
                if (LastScheduleFrame.TryGetValue(key, out var last) && now - last < CooldownFrames)
                {
                    return; // 冷却中
                }
                LastScheduleFrame[key] = now;
                var go = new GameObject("MDGA_UIRefresh_" + tag);
                UnityEngine.Object.DontDestroyOnLoad(go);
                var helper = go.AddComponent<DelayedRefresh>();
                helper.Controller = c;
                helper.Tag = tag;
                if (Main.Settings.VerboseLogging) Main.Log("[UIRefresh] Scheduled delayed ApplyClassMechanics tag=" + tag);
            }
            catch (Exception ex) { Main.Log("[UIRefresh] Schedule error: " + ex.Message); }
        }

        [HarmonyPatch(typeof(LevelUpController), nameof(LevelUpController.SelectClass))]
        private static class SelectClassPatch
        {
            static void Postfix(LevelUpController __instance, bool __result)
            {
                if (!__result || !Main.Enabled) return;
                Schedule(__instance, "SelectClass");
            }
        }

        // 预留的 ApplyClassMechanics Postfix（当前不启用）
        [HarmonyPatch(typeof(LevelUpController), nameof(LevelUpController.ApplyClassMechanics))]
        private static class ApplyClassMechanicsPatch
        {
            static bool Prepare() => false;
            static void Postfix(LevelUpController __instance) { }
        }
    }
}
