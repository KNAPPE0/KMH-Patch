using System;
using System.Collections.Generic;
using GameClient.Misc;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Reputation.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Reputation
{
    // Server-wide reputation board. Reads the reputation roster the server already pushes (same data that badges
    // names on the quest board) and ranks it by score. Trusted players float to the top, the unreliable sink - a
    // public accountability view for the quest economy
    public class Dialog_KMHReputationLeaderboard : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(560f, 620f);

        private Vector2  _scroll;
        private float    _refreshTimer   = DialogLayout.AutoRefreshSeconds;
        private DateTime _lastRefreshUtc = DateTime.UtcNow;

        public Dialog_KMHReputationLeaderboard()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            ReputationCache.RequestSnapshot();
            _lastRefreshUtc      = DateTime.UtcNow;
            ReputationCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            ReputationCache.Updated -= OnSnapshotUpdated;
        }

        private void OnSnapshotUpdated() => _lastRefreshUtc = DateTime.UtcNow;

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                ReputationCache.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Reputation Board");
            int secsSince = Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds);
            DialogLayout.DrawLiveBadge(rect, secsSince);
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (!ReputationCache.HasSnapshot)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 20f), "<color=grey>Loading reputation…</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f),
                "Earned from quest behaviour: +completed, -abandoned, -rejected proof.");
            GUI.color = oldCol;
            y += 22f;

            DrawHeader(new Rect(0f, y, rect.width, 22f));
            y += 24f;

            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawRows(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private static void DrawHeader(Rect r)
        {
            DialogLayout.LabelTrunc(new Rect(r.x + 6f, r.y, 40f, r.height), "<b>#</b>");
            DialogLayout.LabelTrunc(new Rect(r.x + 50f, r.y, r.width * 0.5f, r.height), "<b>Player</b>");
            DialogLayout.DrawCenteredLabel(new Rect(r.x + r.width * 0.6f, r.y, r.width * 0.18f, r.height), "<b>Score</b>");
            DialogLayout.DrawCenteredLabel(new Rect(r.x + r.width * 0.78f, r.y, r.width * 0.2f, r.height), "<b>Tier</b>");
        }

        private void DrawRows(Rect box)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 26f;

            List<ReputationEntryDto> rows = ReputationCache.Leaderboard();
            string me = SessionHandler.Username ?? "";

            float viewH    = Mathf.Max(inner.height, rows.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float ly = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                ReputationEntryDto e = rows[i];
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                bool isMe = !string.IsNullOrEmpty(me) && string.Equals(e.Username, me, StringComparison.OrdinalIgnoreCase);
                string nameCell = (isMe ? "★ " : "") + LinkedAccountsCache.Format(e.Username);

                DialogLayout.LabelTrunc(new Rect(6f, ly + 3f, 40f, rowH - 6f), $"{i + 1}");
                DialogLayout.LabelTrunc(new Rect(50f, ly + 3f, viewRect.width * 0.5f, rowH - 6f), nameCell);
                DialogLayout.DrawCenteredLabel(new Rect(viewRect.width * 0.6f, ly + 3f, viewRect.width * 0.18f, rowH - 6f),
                    e.Score.ToString());
                DialogLayout.DrawCenteredLabel(new Rect(viewRect.width * 0.78f, ly + 3f, viewRect.width * 0.2f, rowH - 6f),
                    TierLabel(e.Tier));

                ly += rowH;
            }
            if (rows.Count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f),
                    "<color=grey>No reputation recorded yet - complete some quests.</color>");
            }
            Widgets.EndScrollView();
        }

        private static string TierLabel(string tier)
        {
            switch (tier)
            {
                case "Trusted":    return "<color=#80ff80>Trusted</color>";
                case "Unreliable": return "<color=#ff8080>Unreliable</color>";
                default:           return "<color=grey>Neutral</color>";
            }
        }
    }
}
