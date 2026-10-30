using System;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Self-guarded: it draws inside RimWorld's own mod settings window.
    internal static class EnforcementLockUI
    {
        private static Vector2 _scroll;

        public static void DrawLockedNotice(Rect inRect, Verse.Mod mod)
        {
            try { DrawInner(inRect, mod); }
            catch (Exception ex)
            {
                KmhLog.Warn($"Enforcement locked-notice draw failed: {ex.Message}");
                try { Widgets.Label(inRect, "Mod options are locked by the server."); } catch { }
            }
        }

        private static void DrawInner(Rect inRect, Verse.Mod mod)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            if (EnforcementProfileApplier.ModAwaitsRestart(mod?.Content?.FolderName))
            {
                Text.Font = GameFont.Medium;
                listing.Label(UI.KmhText.Get("KMH_Enf_AwaitRestart_Title", "Restart to load your own settings"));
                Text.Font = GameFont.Small;
                listing.Gap(4f);
                listing.Label("<color=#A9AFB9>" + UI.KmhText.Get("KMH_Enf_AwaitRestart_Body",
                    "Your own settings for {0} are back on disk, but RimWorld still holds the server's in memory. They load when RimWorld restarts; until then they can't be changed here, because saving now would write the server's values over yours.",
                    UI.KmhDisplayText.Inert(mod?.Content?.Name ?? "this mod", 80)) + "</color>");
                listing.Gap(8f);
                if (listing.ButtonText(UI.KmhText.Get("KMH_Enf_AwaitRestart_Button", "Restart RimWorld now")))
                {
                    try { GenCommandLine.Restart(); }
                    catch (Exception ex) { KmhLog.Warn($"Enforcement: restart from the settings notice failed: {ex.Message}"); }
                }
                listing.End();
                return;
            }

            Text.Font = GameFont.Medium;
            listing.Label("Mod options locked");
            Text.Font = GameFont.Small;

            string modName = mod?.Content?.Name ?? "This mod";
            listing.Gap(4f);
            listing.Label(
                $"<color=#A9AFB9>The server is enforcing a config profile, so <b>{modName}</b>'s options " +
                "can't be changed right now - editing them would break sync or the intended balance.</color>");

            if (EnforcementCache.IsAdmin)
                listing.Label("<color=#f0c04f>You're an admin, but admin bypass is turned off on this server.</color>");

            listing.GapLine(10f);

            int n = EnforcementCache.SafeCount;
            Text.Font = GameFont.Medium;
            listing.Label($"Safe mods you can still change ({n})");
            Text.Font = GameFont.Small;

            if (n == 0)
                listing.Label("<color=#A9AFB9>None - the server hasn't marked any mods as safe to edit.</color>");

            float listTop = inRect.y + listing.CurHeight + 4f;
            listing.End();

            if (n <= 0) return;

            Rect box = new Rect(inRect.x, listTop, inRect.width, Mathf.Max(40f, inRect.yMax - listTop));
            Widgets.DrawMenuSection(box);
            Rect inner = box.ContractedBy(6f);

            const float rowH = 26f;
            float viewH = Mathf.Max(inner.height, n * rowH + 4f);
            Rect viewRect = new Rect(0f, 0f, inner.width - 18f, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float ly = 0f;
            int i = 0;
            foreach (string id in EnforcementCache.SafeMods)
            {
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i++ % 2 == 0) Widgets.DrawAltRect(row);
                UI.DialogLayout.LabelTrunc(new Rect(row.x + 4f, row.y, row.width - 8f, row.height), FriendlyMod(id));
                ly += rowH;
            }
            Widgets.EndScrollView();
        }

        // Server-supplied id, so it is drawn inert when it matches no installed mod.
        internal static string FriendlyMod(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;
            string shown = UI.KmhDisplayText.Inert(id);
            foreach (ModContentPack mcp in LoadedModManager.RunningMods)
            {
                if (string.Equals(mcp.PackageId, id, StringComparison.OrdinalIgnoreCase))
                    return $"{UI.KmhDisplayText.Inert(mcp.Name)}  <color=#A9AFB9>({shown})</color>";
            }
            return shown;
        }
    }
}
