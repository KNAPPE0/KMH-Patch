using System;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Notice drawn in place of a locked mod's settings; self-guarded because it draws inside RimWorld's own window.
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

            Text.Font = GameFont.Medium;
            listing.Label("Mod options locked");
            Text.Font = GameFont.Small;

            string modName = mod?.Content?.Name ?? "This mod";
            listing.Gap(4f);
            listing.Label(
                $"<color=grey>The server is enforcing a config profile, so <b>{modName}</b>'s options " +
                "can't be changed right now - editing them would break sync or the intended balance.</color>");

            if (EnforcementCache.IsAdmin)
                listing.Label("<color=#ffce4d>You're an admin, but admin bypass is turned off on this server.</color>");

            listing.GapLine(10f);

            int n = EnforcementCache.SafeCount;
            Text.Font = GameFont.Medium;
            listing.Label($"Safe mods you can still change ({n})");
            Text.Font = GameFont.Small;

            if (n == 0)
                listing.Label("<color=grey>None - the server hasn't marked any mods as safe to edit.</color>");

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

        private static string FriendlyMod(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;
            foreach (ModContentPack mcp in LoadedModManager.RunningMods)
            {
                if (string.Equals(mcp.PackageId, id, StringComparison.OrdinalIgnoreCase))
                    return $"{mcp.Name}  <color=grey>({id})</color>";
            }
            return id;
        }
    }
}
