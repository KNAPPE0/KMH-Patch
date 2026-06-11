using System;
using System.Collections.Generic;
using System.Linq;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // In-game config enforcement: admins get the full controls, everyone else a read-only view + restore. The
    // server is authoritative (mutations admin-gated)
    public class Dialog_KMHEnforcement : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(640f, 720f);

        private Vector2 _scroll;
        private string  _filter = "";

        public Dialog_KMHEnforcement()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            // Pull a fresh snapshot so is_admin reflects the server right now - e.g. if an admin just ran `op
            // <you>` while you were already in-game
            try { EnforcementHandler.RequestSnapshot(); } catch { }
            ConfigHeuristics.ClearCache(); // re-scan configs fresh each open
        }

        // Suggestion tag shown on a mod row (hint only).
        private static string TagFor(ConfigHeuristics.Hint h)
        {
            switch (h.Verdict)
            {
                case ConfigHeuristics.Verdict.Personal: return "<color=#7fd07f>likely personal</color>";
                case ConfigHeuristics.Verdict.Mixed:    return "<color=#d8c060>mixed</color>";
                default: return null;
            }
        }

        private static string TooltipFor(ConfigHeuristics.Hint h)
        {
            if (h.Verdict == ConfigHeuristics.Verdict.Personal)
                return $"Every setting ({h.Total}) looks player-specific (window/colour/audio/telemetry). Probably safe to mark editable.";
            return $"{h.Personal} of {h.Total} settings look player-specific; the rest may affect the shared game. Review before marking safe.";
        }

        protected override void DrawContents(Rect rect)
        {
            bool admin = EnforcementCache.IsAdmin;
            float y = DialogLayout.DrawTitle(rect, admin ? "Config Enforcement (admin)" : "Config Enforcement");
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (!admin) { DrawClientView(rect, y); return; }

            // Master toggle.
            bool enabled = EnforcementCache.Enabled, newEnabled = enabled;
            Widgets.CheckboxLabeled(new Rect(0f, y, rect.width, 28f),
                "Enforce configs (lock players' Mod Options to the server)", ref newEnabled);
            if (newEnabled != enabled) EnforcementHandler.SetEnabled(newEnabled);
            y += 30f;

            // Power rails.
            bool bypass = EnforcementCache.AdminBypass, newBypass = bypass;
            Widgets.CheckboxLabeled(new Rect(0f, y, rect.width, 24f), "Admins are exempt (you can edit freely)", ref newBypass);
            if (newBypass != bypass) EnforcementHandler.SetFlag("admin_bypass", newBypass);
            y += 26f;

            bool preserve = EnforcementCache.PreservePersonal, newPreserve = preserve;
            Widgets.CheckboxLabeled(new Rect(0f, y, rect.width, 24f),
                "Keep players' personal values on apply (window/colour/audio merged in; Mod Options stay locked)", ref newPreserve);
            if (newPreserve != preserve) EnforcementHandler.SetFlag("preserve_personal", newPreserve);
            y += 28f;

            Color old = GUI.color; GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                $"State: {(enabled ? "ON" : "OFF")}   ·   mode: {(preserve ? "gameplay-only" : "locked")}   ·   " +
                $"safe: {EnforcementCache.SafeCount}   ·   profile: {(EnforcementCache.HasProfile ? "yes" : "none")}");
            GUI.color = old;
            y += 26f;

            Rect pub = new Rect(0f, y, 250f, 30f);
            if (Widgets.ButtonText(pub, "Publish my configs"))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Publish your current mod configs as the server profile?\n\nThis replaces the current profile; every enforcing client pulls it and restarts.",
                    () => { bool ok = EnforcementHandler.PublishMyConfigs(out string s); if (ok) Notifications.KmhNotifications.Positive(s); else Notifications.KmhNotifications.Rejected(s); }));
            TooltipHandler.TipRegion(pub, "Zips your Config and uploads it as the profile every enforcing client receives.");

            Rect mark = new Rect(260f, y, 230f, 30f);
            if (Widgets.ButtonText(mark, "Mark likely-personal safe")) MarkLikelyPersonalSafe();
            TooltipHandler.TipRegion(mark, "Marks every mod whose settings ALL look player-specific (heuristic) as safe. Review after.");
            y += 36f;

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                "<color=grey>Checked mods stay editable by players. Tags suggest which look personal.</color>");
            y += 24f;

            _filter = DialogLayout.SearchField(new Rect(0f, y, rect.width, 28f), _filter, "Filter mods…");
            y += 34f;

            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawModList(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        // Read-only view for non-admins: what's enforced, what they can still edit, and the restore escape hatch
        private void DrawClientView(Rect rect, float y)
        {
            bool on = EnforcementCache.Enabled;
            bool preserve = EnforcementCache.PreservePersonalActive();

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f),
                on ? "<color=#ffce4d>This server enforces mod configs while you're connected.</color>"
                   : "<color=#7fd07f>This server is not enforcing mod configs.</color>");
            y += 30f;

            if (on)
            {
                int locked = LoadedModManager.RunningModsListForReading.Count(
                    m => m != null && EnforcementProfileApplier.IsModConfigEnforced(m.FolderName));
                Color mu = GUI.color; GUI.color = DialogLayout.MutedColor;
                Widgets.Label(new Rect(0f, y, rect.width, 96f),
                    (preserve
                        ? "Mode: your personal values (window position, colours, audio) are kept when the profile applies; gameplay settings are enforced and Mod Options stay read-only.\n"
                        : "Mode: locked - the server's config is applied and your Mod Options for enforced mods are read-only.\n") +
                    $"{(preserve ? "Mods with gameplay settings enforced" : "Mods locked to the server")}: {locked}\n" +
                    $"Mods you can fully edit: {(EnforcementCache.SafeCount == 0 ? "none" : string.Join(", ", EnforcementCache.SafeMods))}");
                GUI.color = mu;
                y += 100f;
            }

            if (EnforcementProfileApplier.IsApplied)
            {
                if (Widgets.ButtonText(new Rect(0f, y, 280f, 30f), "Restore my original configs"))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Restore your personal configs and lift enforcement locally? RimWorld will restart. (It re-applies if you rejoin an enforcing server.)",
                        () => { EnforcementProfileApplier.Restore(); try { GenCommandLine.Restart(); } catch { } }));
                y += 38f;
            }

            Color g = GUI.color; GUI.color = DialogLayout.MutedColor;
            Widgets.Label(new Rect(0f, y, rect.width, 44f),
                "Enforcement is set by server admins. To become an admin, an existing admin runs  op <your name>  in the server console.");
            GUI.color = g;

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        // Admin convenience: mark every "all settings look personal" mod safe.
        private static void MarkLikelyPersonalSafe()
        {
            int n = 0;
            foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
            {
                if (m == null || (m.PackageId ?? "").StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)) continue;
                if (EnforcementCache.IsSafeId(m.PackageId) || EnforcementCache.IsSafeId(m.Name)) continue;
                if (ConfigHeuristics.Classify(m).Verdict == ConfigHeuristics.Verdict.Personal)
                { EnforcementHandler.SetSafe(m.PackageId, true); n++; }
            }
            Notifications.KmhNotifications.Positive(n > 0 ? $"Marked {n} likely-personal mod(s) safe." : "No unmarked likely-personal mods found.");
        }

        private void DrawModList(Rect box)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 30f;
            string filter = (_filter ?? "").Trim().ToLowerInvariant();

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading
                .Where(m => m != null
                    && !(m.PackageId ?? "").StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase) // skip Core + DLC
                    && (filter.Length == 0
                        || (m.Name ?? "").ToLowerInvariant().Contains(filter)
                        || (m.PackageId ?? "").ToLowerInvariant().Contains(filter)))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            float viewH = Mathf.Max(inner.height, mods.Count * rowH + 6f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float ly = 0f;
            for (int i = 0; i < mods.Count; i++)
            {
                ModContentPack m = mods[i];
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                bool isSafe  = EnforcementCache.IsSafeId(m.PackageId) || EnforcementCache.IsSafeId(m.Name);
                bool toggled = isSafe;
                Widgets.Checkbox(row.x + 6f, ly + 4f, ref toggled, 22f);
                if (toggled != isSafe)
                {
                    if (toggled) EnforcementHandler.SetSafe(m.PackageId, true);
                    // Remove by both id + name so a name-added entry clears too.
                    else { EnforcementHandler.SetSafe(m.PackageId, false); EnforcementHandler.SetSafe(m.Name, false); }
                }

                // Heuristic "likely personal" tag (a hint only - never auto-applied).
                ConfigHeuristics.Hint hint = ConfigHeuristics.Classify(m);
                float tagW = 0f;
                string tag = TagFor(hint);
                if (tag != null)
                {
                    tagW = 96f;
                    Rect tagRect = new Rect(row.xMax - tagW - 6f, ly + 6f, tagW, rowH - 12f);
                    var prevAnchor = Text.Anchor; Text.Anchor = TextAnchor.MiddleRight;
                    DialogLayout.LabelTrunc(tagRect, tag);
                    Text.Anchor = prevAnchor;
                    TooltipHandler.TipRegion(tagRect, TooltipFor(hint));
                }

                DialogLayout.LabelTrunc(new Rect(row.x + 34f, ly + 6f, row.width - 40f - tagW, rowH - 12f),
                    $"{m.Name}  <color=grey>({m.PackageId})</color>");
                ly += rowH;
            }
            if (mods.Count == 0)
                DialogLayout.LabelTrunc(new Rect(8f, 8f, viewRect.width, 20f), "<color=grey>No mods match.</color>");
            Widgets.EndScrollView();
        }
    }
}
