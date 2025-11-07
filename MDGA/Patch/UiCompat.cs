using HarmonyLib;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main;
using System;

namespace MDGA.Patch
{
    // 吞掉 PrestigePlus 的 FixNoToybox2 前缀抛出的 InvalidOperationException（“Sequence contains no elements”）。
    // 该异常会中止 RefreshData，导致进度行（如术士血统轨）在龙门徒升级时消失。
    // 我们仅屏蔽这个特定且无害的情况，其他异常仍照常抛出。
    [HarmonyPatch(typeof(ClassProgressionVM), "DisposeImplementation")]
    internal static class UiCompat_SuppressFixNoToybox2
    {
        // 即便先前的前缀/原方法抛异常，Harmony 的 finalizer 也会运行。
        static Exception Finalizer(Exception __exception)
        {
            if (__exception is InvalidOperationException ioe && ioe.Message.Contains("Sequence contains no elements"))
            {
                if (Main.Settings.VerboseLogging)
                    Main.Log("[UICompat] Swallowed FixNoToybox2 empty sequence InvalidOperationException in ClassProgressionVM.DisposeImplementation.");
                return null; // suppress
            }
            return __exception; // keep others
        }
    }
}
