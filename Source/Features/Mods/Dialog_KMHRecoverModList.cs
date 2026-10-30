using System;
using System.Collections.Generic;
using KMHPatch.Features.Enforcement;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Mods
{
    // The lists that could be the player's own, each shown by what it would change; nothing is used without a choice.
    public class Dialog_KMHRecoverModList : Window
    {
        private readonly string _reason;
        private readonly List<ModsConfigJournal.Candidate> _lists;
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Vector2 _scroll;

        public override Vector2 InitialSize => DialogLayout.FitToScreen(640f, 560f);

        public Dialog_KMHRecoverModList(string reason)
        {
            _reason = reason ?? "";
            _lists = ModsConfigJournal.RecoverableLists(EnforcementProfileApplier.ModsJournalRoot);
            foreach (InstalledMod m in Global.KmhLocalMods.Installed())
                if (!string.IsNullOrEmpty(m.PackageId)) _names[KmhModPlan.Bare(m.PackageId)] = m.Name;
            foreach (string id in Global.KmhLocalMods.ActivePackageIds()) _active.Add(KmhModPlan.Bare(id));
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), KmhText.Get("KMH_Recover_Title", "Recover your own mod list"));
            Text.Font = GameFont.Small;

            string intro = (_reason.Length > 0 ? _reason + "\n\n" : "")
                + (_lists.Count == 0
                    ? KmhText.Get("KMH_Recover_None", "There is no other mod list on this computer that Colony Nexus can offer. Your current list stays as it is; you can change it on RimWorld's Mods page.")
                    : KmhText.Get("KMH_Recover_Intro", "These lists were saved on this computer before a server changed yours. Choose one to use it, or close this window to keep your current list. Your current list is kept too, so this can be undone the same way."));
            float introH = Text.CalcHeight(intro, inRect.width);
            Widgets.Label(new Rect(0f, 38f, inRect.width, introH), intro);

            Rect body = new Rect(0f, 46f + introH, inRect.width, inRect.height - 46f - introH - 40f);
            float rowH = 92f;
            Rect view = new Rect(0f, 0f, body.width - 18f, Mathf.Max(body.height, _lists.Count * (rowH + 6f)));
            Widgets.BeginScrollView(body, ref _scroll, view);
            float y = 0f;
            foreach (ModsConfigJournal.Candidate c in _lists)
            {
                DrawList(new Rect(0f, y, view.width, rowH), c);
                y += rowH + 6f;
            }
            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(inRect.width - 220f, inRect.height - 34f, 220f, 32f),
                                   KmhText.Get("KMH_Recover_Keep", "Keep my current list")))
                Close();
        }

        private void DrawList(Rect r, ModsConfigJournal.Candidate c)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(6f);
            string when = c.WhenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width - 150f, 22f),
                "<b>" + SourceLabel(c.Source) + "</b>  <color=#A9AFB9>" + when + ", "
                + KmhText.Get("KMH_Recover_Count", "{0} mods", c.Mods.Count) + "</color>");

            var on = new List<string>();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in c.Mods)
            {
                string bare = KmhModPlan.Bare(id);
                listed.Add(bare);
                if (!_active.Contains(bare)) on.Add(Name(bare));
            }
            var off = new List<string>();
            foreach (string id in _active) if (!listed.Contains(id)) off.Add(Name(id));

            string change = KmhText.Get("KMH_Recover_On", "Switches on: {0}", Few(on)) + "\n"
                          + KmhText.Get("KMH_Recover_Off", "Switches off: {0}", Few(off));
            Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width - 150f, inner.height - 24f), change);

            Rect use = new Rect(inner.xMax - 140f, inner.y + 4f, 140f, 30f);
            if (!Widgets.ButtonText(use, KmhText.Get("KMH_Recover_Use", "Use this list"))) return;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                KmhText.Get("KMH_Recover_Confirm", "Use the {0} list from {1}? Your current list is saved first, and RimWorld has to restart to load the change.", SourceLabel(c.Source), when),
                () =>
                {
                    if (!ModsConfigJournal.RecoverList(EnforcementProfileApplier.ModsJournalRoot, c, out string why))
                    {
                        Messages.Message(KmhText.Get("KMH_Recover_Failed", "That mod list could not be used: {0}", KmhDisplayText.Inert(why, 160)),
                                         MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                    Close();
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KmhText.Get("KMH_Prep_Restored", "Your own mod list and RimWorld Together copy are back on disk. RimWorld loads them when it restarts - and until then, opening the Mods page would undo this.") + "\n\n"
                      + KmhText.Get("KMH_Browser_ModPlan_RestartNow", "Restart now?"),
                        () => { try { GenCommandLine.Restart(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"Restart after recovering a mod list failed: {ex.Message}"); } },
                        destructive: false));
                },
                destructive: false));
        }

        private string Name(string bare)
            => KmhDisplayText.Inert(_names.TryGetValue(bare, out string n) && !string.IsNullOrEmpty(n) ? n : bare, 60);

        private static string Few(List<string> items)
        {
            if (items.Count == 0) return KmhText.Get("KMH_Recover_Nothing", "nothing");
            const int Max = 6;
            string shown = string.Join(", ", items.GetRange(0, Math.Min(Max, items.Count)).ToArray());
            return items.Count <= Max ? shown : KmhText.Get("KMH_Recover_More", "{0} and {1} more", shown, items.Count - Max);
        }

        private static string SourceLabel(string source)
        {
            switch (source)
            {
                case "saved":    return KmhText.Get("KMH_Recover_Saved", "Your saved list");
                case "archive":  return KmhText.Get("KMH_Recover_Archive", "A list Colony Nexus kept");
                case "modsync":  return KmhText.Get("KMH_Recover_ModSync", "Your list before the last server change");
                case "rimworld": return KmhText.Get("KMH_Recover_RimWorld", "RimWorld's own backup");
                default:         return source;
            }
        }
    }
}
