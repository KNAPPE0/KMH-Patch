using HarmonyLib;
using KMHPatch.Features.Enforcement;
using KMHPatch.SubProtocol;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    // Main-menu escape hatch to restore original configs after leaving an enforcing server.
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_EnforcementRestore
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                if (KmhDispatcher.IsKmhServer) return;            // connected - use the in-game flow
                if (!EnforcementProfileApplier.IsApplied) return;

                const float w = 380f, h = 62f;
                Rect panel = new Rect((Verse.UI.screenWidth - w) / 2f, 16f, w, h);
                Widgets.DrawWindowBackground(panel);
                Rect inner = panel.ContractedBy(8f);

                Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f),
                    "<color=#ffce4d>A server's config profile is still applied.</color>");
                if (Widgets.ButtonText(new Rect(inner.x, inner.y + 24f, inner.width, 24f), "Restore my original configs"))
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Restore your personal configs and remove the server profile?\n\n" +
                        "RimWorld will restart. (It re-applies if you rejoin an enforcing server.)",
                        () => { EnforcementProfileApplier.Restore(); try { GenCommandLine.Restart(); } catch { } }));
                }
            }
            catch { }
        }
    }
}
