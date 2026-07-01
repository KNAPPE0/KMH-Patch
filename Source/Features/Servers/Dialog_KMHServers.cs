using System;
using System.Collections.Generic;
using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Servers
{
    // Local list of KMH servers this client has joined before.
    public class Dialog_KMHServers : Window_KMHBase
    {
        private Vector2 _scroll;

        public override Vector2 InitialSize => new Vector2(560f, 480f);

        protected override void DrawContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "KMH Servers");

            Text.Font = GameFont.Small;
            Widgets.Label(
                new Rect(0f, 34f, inRect.width, 38f),
                "<color=grey>Servers you have connected to. Versions are from the last time you joined.</color>");

            List<SeenServersStore.Entry> entries = SeenServersStore.All();
            Rect listRect = new Rect(0f, 78f, inRect.width, inRect.height - 78f - 40f);

            if (entries.Count == 0)
            {
                Widgets.Label(
                    listRect,
                    "<color=grey>No KMH servers seen yet. Connect to one and it will show up here.</color>");
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

                    Widgets.Label(
                        new Rect(row.x + 8f, row.y + 4f, row.width - 96f, 24f),
                        $"<b>{Show(entry.Endpoint)}</b>");

                    Widgets.Label(
                        new Rect(row.x + 8f, row.y + 28f, row.width - 16f, 22f),
                        $"<color={kmhColor}>KMH {Show(entry.KmhVersion)}</color>  |  RWT {Show(entry.RwtVersion)}  |  <color=grey>{Ago(entry.LastSeenTicks)}</color>");

                    if (Widgets.ButtonText(new Rect(row.xMax - 84f, row.y + 4f, 76f, 24f), "Forget"))
                    {
                        SeenServersStore.Forget(entry.Endpoint);
                    }

                    y += rowH;
                }

                Widgets.EndScrollView();
            }

            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 32f, 120f, 30f), "Close"))
            {
                Close();
            }
        }

        private static string Show(string value)
        {
            return string.IsNullOrEmpty(value) ? "?" : value;
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