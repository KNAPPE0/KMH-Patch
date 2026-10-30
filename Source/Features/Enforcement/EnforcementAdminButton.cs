using System;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    internal static class EnforcementAdminButton
    {
        public static void Draw(Rect inRect, Verse.Mod mod)
        {
            try
            {
                ModContentPack c = mod?.Content;
                if (c == null) return;
                if (!EnforcementCache.IsAdmin || !KmhDispatcher.IsKmhServer) return;

                string pkg = c.PackageId ?? "";
                if (pkg.StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)) return;
                if (EnforcementCache.IsKmhItself(c)) return;

                bool safe = EnforcementCache.IsSafeId(pkg) || EnforcementCache.IsSafeId(c.Name);

                const float w = 180f, h = 24f;
                Rect r = new Rect(inRect.xMax - w, inRect.y, w, h);

                Color prev = GUI.color;
                GUI.color = safe ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.95f, 0.85f, 0.45f);
                bool clicked = Widgets.ButtonText(r, safe ? "Colony Nexus: Unmark safe" : "Colony Nexus: Mark safe");
                GUI.color = prev;

                TooltipHandler.TipRegion(r, safe
                    ? "Colony Nexus: players may edit this mod's config. Click to enforce it again."
                    : "Colony Nexus: this mod's config is enforced. Click to let players edit it (safe).");

                if (clicked)
                {
                    if (safe) { EnforcementHandler.SetSafe(pkg, false); EnforcementHandler.SetSafe(c.Name, false); }
                    else      EnforcementHandler.SetSafe(pkg, true);
                    Messages.Message(
                        safe ? $"Colony Nexus: '{c.Name}' is now enforced (locked)."
                             : $"Colony Nexus: '{c.Name}' is now safe - players may edit it.",
                        MessageTypeDefOf.TaskCompletion, historical: false);
                }
            }
            catch (Exception ex) { KmhLog.Warn($"Enforcement admin button failed: {ex.Message}"); }
        }
    }
}
