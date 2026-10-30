using System;
using System.Collections.Generic;
using System.Linq;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    public class Dialog_KMHEnforcement : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(640f, 720f);

        private Vector2 _scroll;
        private string  _filter = "";
        // Cached: the running mod list never changes in-game, so it is not re-filtered every OnGUI pass.
        private readonly KmhFilteredView<ModContentPack> _modsView = new KmhFilteredView<ModContentPack>();

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
            // Fresh snapshot so is_admin reflects the server right now, e.g. an admin ran `op <you>` mid-session.
            try { EnforcementHandler.RequestSnapshot(); } catch { }
            ConfigHeuristics.ClearCache();
        }

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

            // Locked during a publish, and spaced so a misclick cannot hit admin_bypass instead of the enforcement switch.
            bool busy = ConfigPublish.Active;
            const float RowH = 26f, RowGap = 10f;

            bool enabled = EnforcementCache.Enabled, newEnabled = enabled;
            Widgets.CheckboxLabeled(new Rect(0f, y, rect.width, RowH),
                "Enforce configs (lock players' Mod Options to the server)", ref newEnabled, disabled: busy);
            if (!busy && newEnabled != enabled) EnforcementHandler.SetEnabled(newEnabled);
            y += RowH + RowGap;

            bool bypass = EnforcementCache.AdminBypass, newBypass = bypass;
            Widgets.CheckboxLabeled(new Rect(0f, y, rect.width, RowH),
                "Admins are exempt (you can edit freely)", ref newBypass, disabled: busy);
            if (!busy && newBypass != bypass) EnforcementHandler.SetFlag("admin_bypass", newBypass);
            y += RowH + RowGap;

            bool preserve = EnforcementCache.PreservePersonal, newPreserve = preserve;
            const string preserveLabel = "Keep players' personal values on apply (window/colour/audio merged in; Mod Options stay locked)";
            // Measured: the label wraps to two lines at 1280 wide.
            float preserveH = Mathf.Max(RowH, Text.CalcHeight(preserveLabel, rect.width - 30f) + 2f);
            Widgets.CheckboxLabeled(new Rect(0f, y, rect.width, preserveH), preserveLabel, ref newPreserve, disabled: busy);
            if (!busy && newPreserve != preserve) EnforcementHandler.SetFlag("preserve_personal", newPreserve);
            y += preserveH + RowGap;

            Color old = GUI.color; GUI.color = DialogLayout.MutedColor;
            string profile = EnforcementCache.HasProfile
                ? $"rev {EnforcementCache.ProfileRevision} · {EnforcementCache.ProfileFiles} file(s) · {ConfigPublish.ShortHash(EnforcementCache.ServerProfileHash)}"
                : "none";
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                $"State: {(enabled ? "ON" : "OFF")}   ·   mode: {(preserve ? "gameplay-only" : "locked")}   ·   " +
                $"safe: {EnforcementCache.SafeCount}   ·   profile: {profile}");
            GUI.color = old;
            y += 26f;

            y = DrawPublishRow(rect, y, busy);

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                "<color=#A9AFB9>Checked mods stay editable by players. Tags suggest which look personal.</color>");
            y += 24f;

            _filter = DialogLayout.SearchField(new Rect(0f, y, rect.width, 28f), _filter, "Filter mods");
            y += 34f;

            Rect listBox = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(listBox);
            DrawModList(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private float DrawPublishRow(Rect rect, float y, bool busy)
        {
            if (busy)
            {
                Rect bar = new Rect(0f, y, rect.width - 110f, 24f);
                Widgets.DrawBoxSolid(bar, BarEmpty);
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * ConfigPublish.Progress, bar.height), BarFill);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(bar, $"{ConfigPublish.Message}  {ConfigPublish.BytesSent / 1024}/{ConfigPublish.BytesTotal / 1024} KB");
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonText(new Rect(rect.width - 100f, y, 100f, 24f), "Cancel")) ConfigPublish.Cancel();
                return y + 32f;
            }

            ConfigManifest preview = Preview();
            // Measured and stacked when too wide: translated labels outgrow 250 and 230px.
            string pubLabel = KmhText.Get("KMH_Enf_PublishMyConfigs", "Publish my configs");
            string markLabel = KmhText.Get("KMH_Enf_MarkLikelyPersonalSafe", "Mark likely-personal safe");
            float pubW = DialogLayout.ButtonW(pubLabel, 250f, rect.width), markW = DialogLayout.ButtonW(markLabel, 230f, rect.width);
            bool stacked = pubW + 10f + markW > rect.width;
            Rect pub = new Rect(0f, y, pubW, 30f);
            if (Widgets.ButtonText(pub, pubLabel))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Publish {preview.Count} config file(s) ({preview.TotalBytes / 1024} KB) as the server profile?\n\n" +
                    "This replaces the current profile; every enforcing client pulls it and restarts.",
                    ConfigPublish.Start));
            TooltipHandler.TipRegion(pub, $"Uploads {preview.Count} managed config file(s) as the profile every enforcing client receives.");

            if (stacked) y += 34f;
            Rect mark = stacked ? new Rect(0f, y, markW, 30f) : new Rect(pubW + 10f, y, markW, 30f);
            if (Widgets.ButtonText(mark, markLabel)) MarkLikelyPersonalSafe();
            TooltipHandler.TipRegion(mark, "Marks every mod whose settings ALL look player-specific (heuristic) as safe. Review after.");
            y += 34f;

            Color c = GUI.color; GUI.color = DialogLayout.MutedColor;
            string line = $"Will publish {preview.Count} file(s), {preview.TotalBytes / 1024} KB";
            ConfigManifestEntry big = Largest(preview);
            if (big != null && big.Size > 1024 * 1024) line += $"  ·  largest: {big.Path} ({big.Size / 1024} KB)";
            if (ConfigPublish.Phase is PublishPhase.Done or PublishPhase.Failed or PublishPhase.Cancelled)
                line = ConfigPublish.Message;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f), line);
            GUI.color = c;
            return y + 24f;
        }

        private static readonly Color BarFill  = new Color(0.35f, 0.52f, 0.38f);
        private static readonly Color BarEmpty = new Color(0.16f, 0.16f, 0.16f);

        private ConfigManifest _preview;
        private int _previewFrame = -1;

        // Rebuilt at most once a second: hashing every config file each OnGUI pass is far too slow.
        private ConfigManifest Preview()
        {
            int second = (int)(Time.realtimeSinceStartup);
            if (_preview != null && _previewFrame == second) return _preview;
            _previewFrame = second;
            try { _preview = ConfigManifest.Build(GenFilePaths.ConfigFolderPath, EnforcementMods.IsSafeModConfigFile); }
            catch { _preview = new ConfigManifest(); }
            return _preview;
        }

        private static ConfigManifestEntry Largest(ConfigManifest m)
        {
            ConfigManifestEntry best = null;
            foreach (ConfigManifestEntry e in m.Entries) if (best == null || e.Size > best.Size) best = e;
            return best;
        }

        private void DrawClientView(Rect rect, float y)
        {
            bool on = EnforcementCache.Enabled;
            bool preserve = EnforcementCache.PreservePersonalActive();

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f),
                on ? "<color=#f0c04f>This server enforces mod configs while you're connected.</color>"
                   : "<color=#7fd07f>This server is not enforcing mod configs.</color>");
            y += 30f;

            if (on)
            {
                int locked = LoadedModManager.RunningModsListForReading.Count(
                    m => m != null && EnforcementProfileApplier.IsModConfigEnforced(m.FolderName));
                Color mu = GUI.color; GUI.color = DialogLayout.MutedColor;
                // Measured: the safe-mod list has no length limit.
                y += DialogLayout.LabelWrapped(0f, y, rect.width,
                    (preserve
                        ? "Mode: your personal values (window position, colours, audio) are kept when the profile applies; gameplay settings are enforced and Mod Options stay read-only.\n"
                        : "Mode: locked - the server's config is applied and your Mod Options for enforced mods are read-only.\n") +
                    $"{(preserve ? "Mods with gameplay settings enforced" : "Mods locked to the server")}: {locked}\n" +
                    $"Mods you can fully edit: {(EnforcementCache.SafeCount == 0 ? "none" : EnforcementCache.SafeModsShown())}") + 4f;
                GUI.color = mu;
            }

            if (EnforcementFlow.IsApplyPending)
            {
                Color pc = GUI.color; GUI.color = new Color(1f, 0.81f, 0.30f);
                y += DialogLayout.LabelWrapped(0f, y, rect.width,
                    "This server's config profile hasn't been applied yet. Apply it to play here (RimWorld restarts), or disconnect.") + 4f;
                GUI.color = pc;
                const string applyLabel = "Apply the server's config profile (restarts RimWorld)";
                if (Widgets.ButtonText(new Rect(0f, y, DialogLayout.ButtonW(applyLabel, 360f, rect.width), 30f), applyLabel))
                    EnforcementFlow.ShowConsent();
                y += 38f;
            }

            if (EnforcementProfileApplier.IsApplied || EnforcementProfileApplier.RecoveryRequired)
            {
                var health = EnforcementProfileApplier.BaselineHealth(out string bwhy, out int backedUp);
                // Widgets.Label parses rich text and the reason names a config file.
                bwhy = (bwhy ?? "").Replace("<", "[").Replace(">", "]");
                bool ready = health == EnforcementProfileApplier.BaselineState.Verified;

                Color bc = GUI.color;
                GUI.color = ready ? new Color(0.49f, 0.86f, 0.49f) : new Color(1f, 0.42f, 0.42f);
                y += DialogLayout.LabelWrapped(0f, y, rect.width, ready
                    ? $"Your originals are backed up and verified ({backedUp} file(s))."
                    : $"Your originals cannot be restored right now: {bwhy}.") + 2f;
                GUI.color = bc;

                if (ready && Widgets.ButtonText(new Rect(0f, y, 280f, 30f), "Restore my original configs"))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Restore your personal configs and lift enforcement locally? RimWorld will restart. (It re-applies if you rejoin an enforcing server.)"
                        + (EnforcementProfileApplier.RestoresModList
                            ? "\n\n" + KmhText.Get("KMH_Enf_RestoreAlsoModList", "Your own mod list from before a server changed it is put back too, unless you have changed it yourself since.")
                            : ""),
                        () =>
                        {
                            if (EnforcementProfileApplier.Restore()) { try { GenCommandLine.Restart(); } catch { } }
                            else Find.WindowStack.Add(new Dialog_MessageBox(
                                "Your personal configs could not be restored. Nothing was changed; the Colony Nexus log has the reason."));
                        }));
                y += 38f;
            }

            Color g = GUI.color; GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelWrapped(0f, y, rect.width,
                "Enforcement is set by server admins. To become an admin, an existing admin runs  op <your name>  in the server console.");
            GUI.color = g;

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

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

            List<ModContentPack> running = LoadedModManager.RunningModsListForReading;
            List<ModContentPack> mods = _modsView.Get(running, filter, () => running
                .Where(m => m != null
                    && !(m.PackageId ?? "").StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
                    && (filter.Length == 0
                        || (m.Name ?? "").ToLowerInvariant().Contains(filter)
                        || (m.PackageId ?? "").ToLowerInvariant().Contains(filter)))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());

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

                ConfigHeuristics.Hint hint = ConfigHeuristics.Classify(m);
                float tagW = 0f;
                string tag = TagFor(hint);
                if (tag != null)
                {
                    tagW = 96f;
                    Rect tagRect = new Rect(row.xMax - tagW - 6f, ly + 6f, tagW, rowH - 12f);
                    DialogLayout.LabelTrunc(tagRect, tag, TextAnchor.MiddleRight);
                    TooltipHandler.TipRegion(tagRect, TooltipFor(hint));
                }

                DialogLayout.LabelTrunc(new Rect(row.x + 34f, ly + 6f, row.width - 40f - tagW, rowH - 12f),
                    $"{m.Name}  <color=#A9AFB9>({m.PackageId})</color>");
                ly += rowH;
            }
            if (mods.Count == 0)
                DialogLayout.LabelTrunc(new Rect(8f, 8f, viewRect.width, 20f), "<color=#A9AFB9>No mods match.</color>");
            Widgets.EndScrollView();
        }
    }
}
