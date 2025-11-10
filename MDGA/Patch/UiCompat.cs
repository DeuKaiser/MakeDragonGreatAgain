using HarmonyLib;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main;
using System;

namespace MDGA.Patch
{
    // 某些环境下（PrestigePlus / FixNoToybox2 等组合）会导致关闭职业进度面板时抛出
    // InvalidOperationException("Sequence contains no elements")，进而阻断刷新。
    // 通过 finalizer 吞掉该特定异常，避免 UI 刷新链路被切断。
    [HarmonyPatch(typeof(ClassProgressionVM), "DisposeImplementation")]
    internal static class UiCompat_SuppressFixNoToybox2
    {
        static Exception Finalizer(Exception __exception)
        {
            if (__exception is InvalidOperationException ioe && ioe.Message.Contains("Sequence contains no elements"))
            {
                if (Main.Settings.VerboseLogging)
                    Main.Log("[UICompat] Swallowed empty-sequence InvalidOperationException in ClassProgressionVM.DisposeImplementation.");
                return null;
            }
            return __exception;
        }
    }
}
