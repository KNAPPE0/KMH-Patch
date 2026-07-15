using System;
using System.Collections.Generic;
using KMHPatch.Features.World.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.World
{
    // Read-only view of the server's active world events - never sends, never alters an event's lifetime.
    public class Dialog_KMHWorldEvents : Window
    {
        // An event can expire with no new snapshot arriving, so re-filter on a slow timer too, not just on arrival.
        private static readonly TimeSpan RefilterInterval = TimeSpan.FromSeconds(1);

        private Vector2 _scroll;
        private List<WorldEventDto> _events = new List<WorldEventDto>();
        private DateTime _lastRebuildUtc = DateTime.MinValue;
        private bool _subscribed;

        public override Vector2 InitialSize => new Vector2(620f, 480f);

        public Dialog_KMHWorldEvents()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            Rebuild();
            if (!_subscribed) { WorldCache.Updated += Rebuild; _subscribed = true; }   // guard: PreOpen can re-run
        }

        public override void PostClose()
        {
            if (_subscribed) { WorldCache.Updated -= Rebuild; _subscribed = false; }   // leaked handlers keep the dialog alive
            base.PostClose();
        }

        private void Rebuild()
        {
            _events = WorldCache.ActiveEvents();
            _lastRebuildUtc = DateTime.UtcNow;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (DateTime.UtcNow - _lastRebuildUtc >= RefilterInterval) Rebuild();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "Active world events");
            Text.Font = GameFont.Small;

            float y = 36f;
            if (!WorldCache.HasSnapshot)
            {
                Widgets.Label(new Rect(0f, y, inRect.width, 24f), "<color=grey>Waiting for the server…</color>");
                return;
            }
            if (_events.Count == 0)
            {
                Widgets.Label(new Rect(0f, y, inRect.width, 24f), "<color=grey>No events are active right now.</color>");
                return;
            }

            Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y);
            float viewW = outRect.width - 20f;   // leave room for the scrollbar so long text never clips
            float viewH = 0f;
            foreach (WorldEventDto e in _events) viewH += EntryHeight(e, viewW);

            Rect viewRect = new Rect(0f, 0f, viewW, viewH);
            Widgets.BeginScrollView(outRect, ref _scroll, viewRect);
            float ey = 0f;
            foreach (WorldEventDto e in _events)
            {
                float h = EntryHeight(e, viewW);
                DrawEntry(new Rect(0f, ey, viewW, h), e);
                ey += h;
            }
            Widgets.EndScrollView();
        }

        private static void DrawEntry(Rect r, WorldEventDto e)
        {
            Widgets.DrawMenuSection(new Rect(r.x, r.y, r.width, r.height - 6f));
            float x = r.x + 8f, w = r.width - 16f, y = r.y + 6f;

            string left = Remaining(e);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(x, y, w, 22f), left);
            Text.Anchor = TextAnchor.UpperLeft;

            Widgets.Label(new Rect(x, y, w - 90f, 22f), $"<b><color=#7CD37C>{WorldEventText.Title(e)}</color></b>");
            y += 22f;

            string body = Body(e);
            float bh = Text.CalcHeight(body, w);
            Widgets.Label(new Rect(x, y, w, bh), body);
        }

        private static float EntryHeight(WorldEventDto e, float width)
            => 22f + Text.CalcHeight(Body(e), width - 16f) + 14f;

        private static string Body(WorldEventDto e)
        {
            string desc = (e?.Description ?? "").Trim();
            return desc.Length > 0 ? desc : "<color=grey>No description supplied.</color>";
        }

        private static string Remaining(WorldEventDto e)
        {
            string t = WorldEventText.Remaining(e);
            return t.Length == 0 ? "" : $"<color=grey>{t} left</color>";
        }
    }
}
