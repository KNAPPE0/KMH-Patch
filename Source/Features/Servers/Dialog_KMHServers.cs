using System;
using System.Collections.Generic;
using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Servers
{
    public class Dialog_KMHServers : Window_KMHBase
    {
        private Vector2 _scroll;

        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(560f, 480f);

        protected override void DrawContents(Rect inRect)
        {
            float top = KMHPatch.UI.DialogLayout.DrawTitle(inRect, UI.KmhText.Get("KMH_Servers_Title", "Colony Nexus Servers"));
            Widgets.Label(
                new Rect(0f, top, inRect.width, 38f),
                "<color=#A9AFB9>" + UI.KmhText.Get("KMH_Servers_Subtitle",
                    "Servers you have connected to. Versions are from the last time you joined.") + "</color>");

            List<SeenServersStore.Entry> entries = SeenServersStore.All();
            float listTop = top + 42f;
            Rect listRect = new Rect(0f, listTop, inRect.width, inRect.height - listTop - 40f);

            if (entries.Count == 0)
            {
                Widgets.Label(
                    listRect,
                    "<color=#A9AFB9>" + UI.KmhText.Get("KMH_Servers_Empty",
                        "No Colony Nexus servers seen yet. Connect to one and it will show up here.") + "</color>");
            }
            else
            {
                const float rowH = 56f;
                Rect view = new Rect(0f, 0f, listRect.width - 16f, entries.Count * rowH);

                Widgets.BeginScrollView(listRect, ref _scroll, view);

                float y = 0f;

                foreach (SeenServersStore.Entry entry in entries)
                {
                    Rect row = new Rect(0f, y, view.width, rowH - 4f);
                    Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));

                    string kmhColor = entry.KmhVersion == KmhProtocol.BuildVersion ? "#7CD37C" : "#E2C16B";

                    // The name once it is known, because an address is not how a player recognises a server.
                    string title = string.IsNullOrEmpty(entry.Name) ? Show(entry.Endpoint) : entry.Name;
                    UI.DialogLayout.LabelTrunc(
                        new Rect(row.x + 8f, row.y + 4f, row.width - 96f, 24f),
                        $"<b>{title}</b>");

                    string where = string.IsNullOrEmpty(entry.Name) ? "" : $"{Show(entry.Endpoint)}  |  ";
                    UI.DialogLayout.LabelTrunc(
                        new Rect(row.x + 8f, row.y + 28f, row.width - 16f, 22f),
                        $"<color=#A9AFB9>{where}</color><color={kmhColor}>Colony Nexus {Show(entry.KmhVersion)}</color>  |  RWT {Show(entry.RwtVersion)}  |  <color=#A9AFB9>{Ago(entry.LastSeenTicks)}</color>");

                    if (Widgets.ButtonText(new Rect(row.xMax - 84f, row.y + 4f, 76f, 24f),
                                           UI.KmhText.Get("KMH_Servers_Forget", "Forget")))
                    {
                        SeenServersStore.Forget(entry.IdentityKnown ? entry.ServerId : entry.Endpoint);
                    }

                    y += rowH;
                }

                Widgets.EndScrollView();
            }

            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 32f, 120f, 30f),
                                   UI.KmhText.Get("KMH_Common_Close", "Close")))
            {
                Close();
            }
        }

        // Inert here too: the endpoint is a key, so it cannot be changed where it is stored.
        internal static string Show(string value)
        {
            return string.IsNullOrEmpty(value) ? "?" : UI.KmhDisplayText.Inert(value);
        }

        private static string Ago(long utcTicks)
        {
            try
            {
                TimeSpan delta = DateTime.UtcNow - new DateTime(utcTicks, DateTimeKind.Utc);

                if (delta.TotalMinutes < 1) return "just now";
                if (delta.TotalHours < 1) return $"{(int)delta.TotalMinutes}m ago";
                if (delta.TotalDays < 1) return $"{(int)delta.TotalHours}h ago";

                return $"{(int)delta.TotalDays}d ago";
            }
            catch
            {
                return "";
            }
        }
    }
}