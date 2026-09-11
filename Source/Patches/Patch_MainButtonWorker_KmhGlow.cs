using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    [HarmonyPatch(typeof(MainButtonWorker), nameof(MainButtonWorker.DoButton))]
    internal static class Patch_MainButtonWorker_KmhGlow
    {
        [HarmonyPostfix]
        private static void Postfix(MainButtonWorker __instance, Rect rect)
        {
            try
            {
                if (__instance?.def == null || __instance.def.defName != "KMH") return;
                int unread = Features.Chat.ChatCache.TotalUnread() + Features.Mail.MailCache.Unread;
                if (unread <= 0) return;

                Color old = GUI.color;

                float pulse = 0.30f + 0.22f * Mathf.Sin(Time.realtimeSinceStartup * 4f);
                GUI.color = new Color(1f, 0.82f, 0.30f, pulse);
                Widgets.DrawHighlight(rect);

                string txt = unread > 99 ? "99+" : unread.ToString();
                float bw = 16f + txt.Length * 7f;
                Rect badge = new Rect(rect.xMax - bw - 4f, rect.y + 3f, bw, 16f);
                GUI.color = new Color(0.80f, 0.18f, 0.18f);
                Widgets.DrawBoxSolid(badge, new Color(0.80f, 0.18f, 0.18f));

                GUI.color = Color.white;
                GameFont pf = Text.Font; TextAnchor pa = Text.Anchor;
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(badge, txt);
                Text.Font = pf; Text.Anchor = pa;

                GUI.color = old;
            }
            catch { /* cosmetic only - never disturb the tab bar */ }
        }
    }
}
