using System.Collections.Generic;
using HarmonyLib;
using KMHPatch.Features.Enforcement;
using KMHPatch.SubProtocol;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    // Escape hatch to restore original configs after leaving an enforcing server; drawn by KmhMainMenuSurface.
    internal static class Patch_MainMenuDrawer_EnforcementRestore
    {
        private static bool _bootPromptsDone;

        internal static void DrawRecoveryPanel(float screenW, float screenH, Rect card, Rect pane)
        {
            try
            {
                if (KmhDispatcher.IsKmhServer) return;            // connected - use the in-game flow
                bool applied  = EnforcementProfileApplier.IsApplied;
                bool recovery = EnforcementProfileApplier.RecoveryRequired;
                BootPrompts(applied, recovery);
                // A failed apply can set RecoveryRequired without IsApplied, and that must still warn and offer the button.
                if (!applied && !recovery)
                {
                    if (EnforcementProfileApplier.ServerLeftovers().Count > 0) DrawLeftovers(screenW, screenH, card, pane);
                    else if (EnforcementProfileApplier.RestoreAwaitsRestart) DrawAwaitingRestart(screenW, screenH, card, pane);
                    return;
                }

                var health = EnforcementProfileApplier.BaselineHealth(out string why, out int backedUp);
                // The reason names a config file and Widgets.Label parses rich text, so a tag in a filename would eat the line.
                why = (why ?? "").Replace("<", "[").Replace(">", "]");
                bool canRestore = health == EnforcementProfileApplier.BaselineState.Verified;

                Rect panel = UI.KmhMainMenuSurface.Claim(new Vector2(420f, 112f), screenW, screenH, card, pane);
                Widgets.DrawWindowBackground(panel);
                Rect inner = panel.ContractedBy(8f);
                float y = inner.y;

                UI.DialogLayout.LabelTrunc(new Rect(inner.x, y, inner.width, 22f), recovery
                    ? "<color=#ff6b6b>Config enforcement needs recovery.</color>"
                    : "<color=#f0c04f>A server's config profile is still applied.</color>");
                y += 22f;

                int enforced = EnforcementProfileApplier.EnforcedFiles().Count;
                UI.DialogLayout.LabelTrunc(new Rect(inner.x, y, inner.width, 20f),
                    $"<color=#b8b8b8>{enforced} file(s) enforced.</color>");
                y += 20f;

                // Truncated to one line: the reason names a file and overflows this fixed panel.
                UI.DialogLayout.LabelTrunc(new Rect(inner.x, y, inner.width, 20f), health switch
                {
                    EnforcementProfileApplier.BaselineState.Verified =>
                        $"<color=#7ddc7d>Your originals are backed up and verified ({backedUp} file(s)).</color>",
                    EnforcementProfileApplier.BaselineState.None =>
                        $"<color=#b8b8b8>No personal backup: {why}.</color>",
                    _ => $"<color=#ff6b6b>The personal backup is damaged: {why}.</color>",
                });
                y += 24f;

                Rect button = new Rect(inner.x, y, inner.width, 24f);
                if (!canRestore)
                {
                    // Nothing trustworthy to put back: the only way out keeps today's files and sets the backup aside.
                    if (Widgets.ButtonText(button, UI.KmhText.Get("KMH_Enf_Release_Button", "Keep the current files and release the lock")))
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            UI.KmhText.Get("KMH_Enf_Release_Confirm", "Colony Nexus cannot put your own settings back because its copy of them is missing or damaged. Release the lock and keep the files as they are now? The damaged copy is set aside, not deleted, and any file still exactly the server's is listed on the main menu so you can keep it or reset it."),
                            () =>
                            {
                                if (!EnforcementProfileApplier.ReleaseKeepingFiles(out string rwhy))
                                    Find.WindowStack.Add(new Dialog_MessageBox(UI.KmhText.Get("KMH_Enf_Release_Failed", "The lock could not be released: {0}", UI.KmhDisplayText.Inert(rwhy, 200))));
                            }, destructive: true));
                    return;
                }

                if (Widgets.ButtonText(button, "Restore my original configs"))
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Restore your {backedUp} personal config file(s) and remove the server profile?\n\n" +
                        "Files the server profile added are removed; files it replaced go back to your own. " +
                        "RimWorld will restart. (It re-applies if you rejoin an enforcing server.)"
                        + (EnforcementProfileApplier.RestoresModList
                            ? "\n\n" + UI.KmhText.Get("KMH_Enf_RestoreAlsoModList", "Your own mod list from before a server changed it is put back too, unless you have changed it yourself since.")
                            : ""),
                        () =>
                        {
                            if (EnforcementProfileApplier.Restore()) { try { GenCommandLine.Restart(); } catch { } }
                            // A silent failure would read as "nothing happened".
                            else Find.WindowStack.Add(new Dialog_MessageBox(
                                "Your personal configs could not be restored. Nothing was changed.\n\n" +
                                "The Colony Nexus log has the reason. Your backup is in ConfigKMH_Originals next to Config."));
                        }));
                }
            }
            catch { }
        }

        // Once per boot at the menu: a damaged copy of the player's mod list, then a server's settings still applied.
        private static void BootPrompts(bool applied, bool recovery)
        {
            if (_bootPromptsDone || Find.WindowStack == null) return;
            _bootPromptsDone = true;
            EnforcementProfileApplier.MainMenuReached();
            if (EnforcementProfileApplier.StartupRestoreNote.Length > 0)
                Find.WindowStack.Add(new Dialog_MessageBox(EnforcementProfileApplier.StartupRestoreNote,
                    UI.KmhText.Get("KMH_Enf_AwaitRestart_Button", "Restart RimWorld now"), () => { try { GenCommandLine.Restart(); } catch { } },
                    UI.KmhText.Get("KMH_Enf_RestartLater", "Later"), null));
            if (EnforcementProfileApplier.DamagedModListFound)
                Find.WindowStack.Add(new Features.Mods.Dialog_KMHRecoverModList(
                    Features.Mods.KmhJoinPreparation.RestoreOutcomeNote(ModsConfigJournal.RestoreOutcome.SavedCopyWasAServerList)));
            // Not straight after the restart an apply asked for, and never while a join is being prepared.
            if (!applied || recovery || Features.Mods.KmhJoinPreparation.Active || EnforcementProfileApplier.AppliedThisSession) return;
            Find.WindowStack.Add(new Dialog_MessageBox(
                UI.KmhText.Get("KMH_Enf_StillApplied", "The mod settings of the last server you played on that enforces them are still in place. Put your own settings back now? RimWorld restarts to load them. Keep the server's if you are going back there - they can be restored later from the main menu."),
                UI.KmhText.Get("KMH_Enf_StillApplied_Restore", "Restore mine"),
                () =>
                {
                    if (EnforcementProfileApplier.Restore("MenuPromptAfterLeaving")) { try { GenCommandLine.Restart(); } catch { } }
                    else Find.WindowStack.Add(new Dialog_MessageBox(
                        "Your personal configs could not be restored. Nothing was changed.\n\n" +
                        "The Colony Nexus log has the reason. Your backup is in ConfigKMH_Originals next to Config."));
                },
                UI.KmhText.Get("KMH_Enf_StillApplied_Keep", "Keep the server's for now"), null));
        }

        private static void DrawAwaitingRestart(float screenW, float screenH, Rect card, Rect pane)
        {
            Rect panel = UI.KmhMainMenuSurface.Claim(new Vector2(420f, 76f), screenW, screenH, card, pane);
            Widgets.DrawWindowBackground(panel);
            Rect inner = panel.ContractedBy(8f);
            UI.DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width, 22f),
                "<color=#7ddc7d>" + UI.KmhText.Get("KMH_Enf_Restored_Title", "Your own mod settings are back on disk.") + "</color>");
            if (Widgets.ButtonText(new Rect(inner.x, inner.y + 30f, inner.width, 24f),
                                   UI.KmhText.Get("KMH_Enf_AwaitRestart_Button", "Restart RimWorld now")))
            { try { GenCommandLine.Restart(); } catch { } }
            TooltipHandler.TipRegion(panel, UI.KmhText.Get("KMH_Enf_Restored_Tip",
                "RimWorld still holds the server's settings in memory until it restarts, so those mods' options stay locked until then."));
        }

        private static void DrawLeftovers(float screenW, float screenH, Rect card, Rect pane)
        {
            var files = EnforcementProfileApplier.ServerLeftovers();
            Rect panel = UI.KmhMainMenuSurface.Claim(new Vector2(420f, 112f), screenW, screenH, card, pane);
            Widgets.DrawWindowBackground(panel);
            Rect inner = panel.ContractedBy(8f);
            UI.DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width, 22f),
                "<color=#f0c04f>" + UI.KmhText.Get("KMH_Enf_Leftovers_Title", "{0} mod setting file(s) still hold a server's settings.", files.Count) + "</color>");
            UI.DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 22f, inner.width, 20f),
                "<color=#b8b8b8>" + UI.KmhText.Get("KMH_Enf_Leftovers_Why", "An older Colony Nexus kept no copy of yours to put back.") + "</color>");
            float half = (inner.width - 8f) / 2f;
            Rect keep = new Rect(inner.x, inner.y + 52f, half, 24f), reset = new Rect(inner.x + half + 8f, inner.y + 52f, half, 24f);
            TooltipHandler.TipRegion(panel, UI.KmhDisplayText.Inert(string.Join(", ", new List<string>(files).ToArray()), 600));
            if (Widgets.ButtonText(keep, UI.KmhText.Get("KMH_Enf_Leftovers_Keep", "Keep them")))
                EnforcementProfileApplier.KeepServerLeftovers();
            if (!Widgets.ButtonText(reset, UI.KmhText.Get("KMH_Enf_Leftovers_Reset", "Use the mods' defaults"))) return;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                UI.KmhText.Get("KMH_Enf_Leftovers_Confirm", "Remove these {0} file(s) so those mods start from their own defaults? A file you have changed since is kept. RimWorld restarts afterwards.", files.Count),
                () =>
                {
                    if (EnforcementProfileApplier.ResetServerLeftovers(out int removed, out string why)) { try { GenCommandLine.Restart(); } catch { } }
                    else Messages.Message(UI.KmhText.Get("KMH_Enf_Leftovers_Failed", "Those settings could not be reset: {0}", UI.KmhDisplayText.Inert(why, 160)),
                                          MessageTypeDefOf.RejectInput, false);
                }, destructive: true));
        }
    }
}
