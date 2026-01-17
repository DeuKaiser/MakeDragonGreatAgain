using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Controllers.Dialog;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.ElementsSystem;
using System;

namespace MDGA.Components
{
    public class ShowCustomBookPage : GameAction
    {
        public BlueprintBookPage m_Page;

        public override string GetCaption()
        {
            return "[MDGA] Show custom book page (via PlayBookPage)";
        }

        public override void RunAction()
        {
            if (m_Page == null)
            {
                Main.Log("[ShowCustomBookPage] m_Page is null");
                return;
            }

            var controller = Game.Instance?.DialogController;
            if (controller == null)
            {
                Main.Log("[ShowCustomBookPage] Game.Instance.DialogController is null");
                return;
            }

            Main.Log($"[ShowCustomBookPage] Invoke PlayBookPage for {m_Page.AssetGuid}");

            // 通过反射调用私有方法 PlayBookPage(BlueprintBookPage)
            var mi = typeof(DialogController).GetMethod(
                "PlayBookPage",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (mi == null)
            {
                Main.Log("[ShowCustomBookPage] PlayBookPage method not found via reflection");
                return;
            }

            try
            {
                mi.Invoke(controller, new object[] { m_Page });
            }
            catch (System.Exception ex)
            {
                Main.Log("[ShowCustomBookPage] PlayBookPage invoke error: " + ex);
            }
        }
    }
}