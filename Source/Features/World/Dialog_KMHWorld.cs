using System;
using System.Collections.Generic;
using KMHPatch.Features.World.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.World
{
    // The full set of active global quests and world events; the Quest Board only banners the first few.
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

            float y = DialogLayout.DrawTitle(rect, "World");
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (!WorldCache.HasSnapshot)
            {
                DialogLayout.DrawMutedLabel(new Rect(0f, y, rect.width, 24f), "Waiting for the server…");
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
            Header(new Rect(0f, y, width, SectionHeaderHeight), "Global Quests", new Color(0.886f, 0.757f, 0.420f));
            y += SectionHeaderHeight;

            if (_quests.Count == 0)
            {
                DialogLayout.DrawMutedLabel(new Rect(0f, y, width, 24f), "No global quests are running right now.");
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
            Header(new Rect(0f, y, width, SectionHeaderHeight), "Active world events", new Color(0.486f, 0.827f, 0.486f));
            y += SectionHeaderHeight;

            if (_events.Count == 0)
            {
                DialogLayout.DrawMutedLabel(new Rect(0f, y, width, 24f), "No events are active right now.");
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

            // The countdown needs its own rect: right-anchoring inside the title's full-width rect only separates them while it stays short.
            float remW  = Mathf.Min(RemainingW, w);
            DialogLayout.LabelTrunc(new Rect(x + w - remW, y, remW, lineH), Remaining(e), TextAnchor.UpperRight);

            // Truncate, don't wrap: a long event title in a fixed one-line box wrapped and had its second line cut.
            DialogLayout.LabelTrunc(new Rect(x, y, Mathf.Max(0f, w - remW - 8f), lineH),
                $"<b><color=#7CD37C>{WorldEventText.Title(e)}</color></b>");
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
            return desc.Length > 0 ? desc : "<color=grey>No description supplied.</color>";
        }

        private static string Remaining(WorldEventDto e)
        {
            string t = WorldEventText.Remaining(e);
            return t.Length == 0 ? "" : $"<color=grey>{t} left</color>";
        }
    }
}
