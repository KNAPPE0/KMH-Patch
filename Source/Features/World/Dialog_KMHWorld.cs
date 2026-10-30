using System;
using System.Collections.Generic;
using KMHPatch.Features.World.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.World
{
    public class Dialog_KMHWorld : Window_KMHBase
    {
        // An event or quest can expire with no new snapshot arriving, so re-filter on a slow timer too.
        private static readonly TimeSpan RefilterInterval = TimeSpan.FromSeconds(1);

        private Vector2 _scroll;
        private List<WorldEventDto>  _events = new List<WorldEventDto>();
        private List<ServerQuestDto> _quests = new List<ServerQuestDto>();
        private DateTime _lastRebuildUtc = DateTime.MinValue;
        private bool _subscribed;

        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(720f, 600f);

        public Dialog_KMHWorld()
        {
            doCloseX                = true;
            forcePause              = false;
            absorbInputAroundWindow = false;
            draggable               = true;
            resizeable              = true;

            EnableAutoRefresh(() => WorldHandler.RequestSnapshot());
        }

        public override void PreOpen()
        {
            base.PreOpen();
            Rebuild();
            if (!_subscribed) { WorldCache.Updated += OnUpdated; _subscribed = true; }   // guard: PreOpen can re-run
        }

        public override void PostClose()
        {
            if (_subscribed) { WorldCache.Updated -= OnUpdated; _subscribed = false; }   // leaked handlers keep the dialog alive
            base.PostClose();
        }

        private void OnUpdated() { Rebuild(); MarkRefreshed(); }

        private void Rebuild()
        {
            _events = WorldCache.ActiveEvents();
            _quests = WorldCache.ActiveServerQuests();
            _lastRebuildUtc = DateTime.UtcNow;
        }

        protected override void DrawContents(Rect rect)
        {
            if (DateTime.UtcNow - _lastRebuildUtc >= RefilterInterval) Rebuild();

            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_Tab_World", "World"), DialogLayout.LiveBadgeReserve);
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (!WorldCache.HasSnapshot)
            {
                DialogLayout.DrawWaiting(new Rect(0f, y, rect.width, 32f), KmhText.Get("KMH_World_Waiting", "Waiting for the server…"));
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            Rect outRect = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            float viewW  = outRect.width - DialogLayout.ScrollbarReserveWidth;

            float viewH = SectionHeaderHeight + QuestsHeight()
                        + SectionHeaderHeight + EventsHeight(viewW);
            Rect viewRect = new Rect(0f, 0f, viewW, Mathf.Max(viewH, outRect.height));

            Widgets.BeginScrollView(outRect, ref _scroll, viewRect);
            float vy = 0f;
            vy = DrawQuests(viewW, vy);
            DrawEvents(viewW, vy);
            Widgets.EndScrollView();

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private const float SectionHeaderHeight = 26f;

        private float QuestsHeight()
            => _quests.Count == 0 ? 24f : _quests.Count * (GlobalQuestRow.Height + 4f);

        private float EventsHeight(float width)
        {
            if (_events.Count == 0) return 24f;
            float h = 0f;
            foreach (WorldEventDto e in _events) h += EntryHeight(e, width);
            return h;
        }

        private float DrawQuests(float width, float y)
        {
            Header(new Rect(0f, y, width, SectionHeaderHeight), KmhText.Get("KMH_World_GlobalQuests", "Global Quests"),
                   new Color(0.886f, 0.757f, 0.420f));
            y += SectionHeaderHeight;

            if (_quests.Count == 0)
            {
                DialogLayout.DrawMutedLabel(new Rect(0f, y, width, 24f),
                    KmhText.Get("KMH_World_NoQuests", "No global quests are running right now."));
                return y + 24f;
            }

            string me  = KmhSession.Me;
            long   now = DateTime.UtcNow.Ticks;
            foreach (ServerQuestDto q in _quests)
            {
                GlobalQuestRow.Draw(new Rect(0f, y, width, GlobalQuestRow.Height), q, me, now);
                y += GlobalQuestRow.Height + 4f;
            }
            return y;
        }

        private void DrawEvents(float width, float y)
        {
            Header(new Rect(0f, y, width, SectionHeaderHeight), KmhText.Get("KMH_World_ActiveEvents", "Active world events"),
                   new Color(0.486f, 0.827f, 0.486f));
            y += SectionHeaderHeight;

            if (_events.Count == 0)
            {
                DialogLayout.DrawMutedLabel(new Rect(0f, y, width, 24f),
                    KmhText.Get("KMH_World_NoEvents", "No events are active right now."));
                return;
            }

            foreach (WorldEventDto e in _events)
            {
                float h = EntryHeight(e, width);
                DrawEntry(new Rect(0f, y, width, h), e);
                y += h;
            }
        }

        private static void Header(Rect r, string label, Color tint)
        {
            Color old = GUI.color;
            GUI.color = tint;
            DialogLayout.LabelTrunc(new Rect(r.x, r.y + 4f, r.width, DialogLayout.TextRowH), $"<b>{label}</b>");
            GUI.color = old;
        }

        private const float RemainingW = 90f;

        private static void DrawEntry(Rect r, WorldEventDto e)
        {
            Widgets.DrawMenuSection(new Rect(r.x, r.y, r.width, r.height - 6f));
            float x = r.x + 8f, w = r.width - 16f, y = r.y + 6f;
            float lineH = DialogLayout.TextRowH;

            // Own rect for the countdown: right-anchoring inside the title's rect only works while the title stays short.
            float remW  = Mathf.Min(RemainingW, w);
            DialogLayout.LabelTrunc(new Rect(x + w - remW, y, remW, lineH), Remaining(e), TextAnchor.UpperRight);

            // Truncate, don't wrap: a long title would wrap in this one-line box and lose its second line.
            DialogLayout.LabelTrunc(new Rect(x, y, Mathf.Max(0f, w - remW - 8f), lineH),
                $"<b><color=#4cc38a>{WorldEventText.Title(e)}</color></b>");
            y += lineH;

            string body = Body(e);
            Widgets.Label(new Rect(x, y, w, Text.CalcHeight(body, w)), body);
        }

        // Must match DrawEntry's title line height and body width, or the description is clipped.
        private static float EntryHeight(WorldEventDto e, float width)
            => DialogLayout.TextRowH + Text.CalcHeight(Body(e), width - 16f) + 14f;

        private static string Body(WorldEventDto e)
        {
            string desc = (e?.Description ?? "").Trim();
            return desc.Length > 0 ? desc : "<color=#A9AFB9>" + KmhText.Get("KMH_World_NoDescription", "No description supplied.") + "</color>";
        }

        private static string Remaining(WorldEventDto e)
        {
            string t = WorldEventText.Remaining(e);
            return t.Length == 0 ? "" : "<color=#A9AFB9>" + KmhText.Get("KMH_World_TimeLeft", "{0} left", t) + "</color>";
        }
    }
}
