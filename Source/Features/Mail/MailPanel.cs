using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Mail.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Mail
{
    // Drawn into a caller-supplied rect, so the hub hosts it and the selection and scrolls live here.
    public class MailPanel
    {
        private Vector2 _listScroll;
        private Vector2 _bodyScroll;
        private long    _selectedId = 0;

        public void Draw(Rect rect)
        {
            float y = rect.y;
            float btnW = Mathf.Max(IconButton.WidthFor("Refresh", false), IconButton.WidthFor("Compose", true));
            if (IconButton.Draw(new Rect(rect.xMax - btnW, y, btnW - 4f, 28f), KMHTextures.Post, "Compose"))
                Dialog_KMHCompose.Open();
            if (Widgets.ButtonText(new Rect(rect.xMax - btnW * 2f, y, btnW - 4f, 28f), "Refresh"))
                MailHandler.RequestSnapshot();
            y += 34f;

            List<MailMessage> msgs = Messages();

            float bodyTop = y + (rect.yMax - y) * 0.5f;
            Rect listBox  = new Rect(rect.x, y, rect.width, bodyTop - y - 6f);
            Rect readBox  = new Rect(rect.x, bodyTop, rect.width, rect.yMax - bodyTop);

            Widgets.DrawMenuSection(listBox);
            DrawList(listBox, msgs);

            Widgets.DrawMenuSection(readBox);
            DrawReadingPane(readBox, msgs);
        }

        private void DrawList(Rect box, List<MailMessage> msgs)
        {
            long now = DateTime.UtcNow.Ticks;
            string empty = !MailCache.HasSnapshot
                ? $"<color=grey>{DialogLayout.AwaitingSnapshot("Loading mail…", "Mail")}</color>"
                : "<color=grey>No mail yet. Compose one!</color>";

            // Above the inbox, so goods in flight read as recoverable rather than silently parked.
            List<MailMessage> outgoing = Outgoing();
            DialogLayout.ScrollList(box, ref _listScroll, outgoing.Count + msgs.Count, DialogLayout.TextRowsH(2, 4f), (i, row) =>
            {
                if (i < outgoing.Count) DrawOutgoingRow(row, outgoing[i], now);
                else                    DrawRow(row, msgs[i - outgoing.Count], now);
            }, empty);
        }

        private void DrawOutgoingRow(Rect row, MailMessage m, long now)
        {
            if (m.Id == _selectedId) Widgets.DrawHighlightSelected(row);
            Rect inner = row.ContractedBy(6f);
            Color old = GUI.color;

            GUI.color = new Color(0.85f, 0.75f, 0.45f);
            Widgets.Label(new Rect(inner.x, inner.y + 1f, 14f, 18f), ">");
            GUI.color = old;

            DialogLayout.LabelTrunc(new Rect(inner.x + 14f, inner.y, Mathf.Max(0f, inner.width - 14f - 90f), DialogLayout.TextRowH),
                $"<color=grey>to</color> {LinkedAccountsCache.Format(m.To)}  <color=grey>· unopened</color>");
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.xMax - 90f, inner.y, 90f, 18f), Ago(m.SentUtcTicks, now), TextAnchor.UpperRight);
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            DialogLayout.LabelTrunc(new Rect(inner.x + 14f, inner.y + DialogLayout.TextRowH, Mathf.Max(0f, inner.width - 14f), DialogLayout.TextRowH),
                $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Accent)}>◆ </color>{AttachSummary(m)} <color=grey>waiting</color>");
            GUI.color = old;

            if (Widgets.ButtonInvisible(row)) { _selectedId = m.Id; _bodyScroll = Vector2.zero; }
        }

        private static List<MailMessage> Outgoing()
        {
            List<MailMessage> l = MailCache.Snapshot?.Outgoing;
            return l ?? new List<MailMessage>();
        }

        private void DrawRow(Rect row, MailMessage m, long now)
        {
            if (m.Id == _selectedId) Widgets.DrawHighlightSelected(row);
            Rect inner = row.ContractedBy(6f);

            Color old = GUI.color;
            if (m.IsUnread)
            {
                GUI.color = new Color(0.55f, 0.8f, 1f);
                Widgets.Label(new Rect(inner.x, inner.y + 1f, 12f, 18f), "●");
                GUI.color = old;
            }
            float lx = inner.x + 14f;

            string from = LinkedAccountsCache.Format(m.From);
            DialogLayout.LabelTrunc(new Rect(lx, inner.y, Mathf.Max(0f, inner.width - 14f - 90f), DialogLayout.TextRowH),
                m.IsUnread ? $"<b>{from}</b>" : from);
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.xMax - 90f, inner.y, 90f, 18f), Ago(m.SentUtcTicks, now), TextAnchor.UpperRight);
            GUI.color = old;

            string subject = string.IsNullOrEmpty(m.Subject) ? "<color=grey>(no subject)</color>" : m.Subject;
            string coin = m.HasOpenAttachment ? UI.KmhTheme.Accented("◆") + " " : "";
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            DialogLayout.LabelTrunc(new Rect(lx, inner.y + DialogLayout.TextRowH, Mathf.Max(0f, inner.width - 14f), DialogLayout.TextRowH), coin + subject);
            GUI.color = old;

            if (Widgets.ButtonInvisible(row)) Select(m);
        }

        private void DrawReadingPane(Rect box, List<MailMessage> msgs)
        {
            MailMessage sel = Find(msgs, _selectedId);
            Rect inner = box.ContractedBy(8f);
            if (sel == null)
            {
                MailMessage sent = Find(Outgoing(), _selectedId);
                if (sent != null) { DrawOutgoingPane(inner, sent); return; }
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(inner, "Select a message to read.");
                GUI.color = Color.white;
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            float ry = inner.y;
            DialogLayout.LabelTrunc(new Rect(inner.x, ry, inner.width - 90f, 22f),
                $"<b>{(string.IsNullOrEmpty(sel.Subject) ? "(no subject)" : sel.Subject)}</b>");

            const float actW = 84f;
            if (IconButton.Draw(new Rect(inner.xMax - actW, ry, actW, 24f), KMHTextures.Cancel, "Delete"))
            { MailHandler.Delete(sel.Id); _selectedId = 0; return; }
            ry += 24f;

            Color old = GUI.color; GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, ry, inner.width, 18f),
                $"from {LinkedAccountsCache.Format(sel.From)}  ·  {LocalTime(sel.SentUtcTicks)} ({Ago(sel.SentUtcTicks, now)})");
            GUI.color = old;
            ry += 22f;

            ry = DrawAttachment(inner, ry, sel);

            float replyH = 26f;
            float bodyTop = ry + 2f;
            Rect bodyRect = new Rect(inner.x, bodyTop, inner.width, inner.yMax - replyH - 4f - bodyTop);
            Widgets.LabelScrollable(bodyRect, string.IsNullOrEmpty(sel.Body) ? "<color=grey>(no message)</color>" : sel.Body, ref _bodyScroll);

            if (IconButton.Draw(new Rect(inner.x, inner.yMax - replyH, 110f, replyH), KMHTextures.Post, "Reply"))
                Dialog_KMHCompose.Open(sel.From);
        }

        // My own sent mail, so no accept or decline: it is not mine to take, only to recall out of escrow.
        private void DrawOutgoingPane(Rect inner, MailMessage sent)
        {
            long now = DateTime.UtcNow.Ticks;
            float ry = inner.y;
            DialogLayout.LabelTrunc(new Rect(inner.x, ry, inner.width, 22f),
                $"<b>{(string.IsNullOrEmpty(sent.Subject) ? "(no subject)" : sent.Subject)}</b>");
            ry += 24f;

            Color old = GUI.color; GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, ry, inner.width, 18f),
                $"to {LinkedAccountsCache.Format(sent.To)}  ·  sent {Ago(sent.SentUtcTicks, now)}  ·  not opened yet");
            GUI.color = old;
            ry += 22f;

            DialogLayout.LabelTrunc(new Rect(inner.x, ry + 4f, inner.width - 130f, 24f),
                $"<b><color={UI.KmhTheme.Hex(UI.KmhTheme.Accent)}>Waiting: {AttachSummary(sent)}</color></b>");
            if (IconButton.Draw(new Rect(inner.xMax - 120f, ry, 120f, 26f), KMHTextures.Withdraw, "Recall"))
            { MailHandler.TryRecall(sent.Id); _selectedId = 0; return; }
            ry += 30f;

            if (sent.HasItems)
            {
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(inner.x, ry, inner.width, 18f), ItemsLine(sent));
                GUI.color = old; ry += 20f;
            }
            if (sent.HasGear)
            {
                GUI.color = new Color(0.72f, 0.82f, 1f);
                DialogLayout.LabelTrunc(new Rect(inner.x, ry, inner.width, 18f), GearLine(sent));
                GUI.color = old; ry += 20f;
            }

            Rect bodyRect = new Rect(inner.x, ry + 2f, inner.width, Mathf.Max(20f, inner.yMax - ry - 4f));
            Widgets.LabelScrollable(bodyRect, string.IsNullOrEmpty(sent.Body) ? "<color=grey>(no message)</color>" : sent.Body, ref _bodyScroll);
        }

        // Returns the y below the banner, so the body flows under whatever height it took.
        private static float DrawAttachment(Rect inner, float ry, MailMessage sel)
        {
            Color old = GUI.color;
            if (sel.HasOpenAttachment)
            {
                DialogLayout.LabelTrunc(new Rect(inner.x, ry + 4f, inner.width - 210f, 24f),
                    $"<b><color={UI.KmhTheme.Hex(UI.KmhTheme.Accent)}>Attached: {AttachSummary(sel)}</color></b>");
                const float bw = 96f;
                if (IconButton.Draw(new Rect(inner.xMax - bw, ry, bw, 26f), KMHTextures.Deposit, "Accept"))  MailHandler.TryAccept(sel.Id);
                if (IconButton.Draw(new Rect(inner.xMax - bw * 2f - 4f, ry, bw, 26f), KMHTextures.Cancel, "Decline")) MailHandler.TryDecline(sel.Id);
                ry += 30f;
                if (sel.HasItems)
                {
                    GUI.color = DialogLayout.MutedColor;
                    DialogLayout.LabelTrunc(new Rect(inner.x, ry, inner.width, 18f), ItemsLine(sel));
                    GUI.color = old;
                    ry += 20f;
                }
                if (sel.HasGear)
                {
                    GUI.color = new Color(0.72f, 0.82f, 1f);
                    DialogLayout.LabelTrunc(new Rect(inner.x, ry, inner.width, 18f), GearLine(sel));
                    GUI.color = old;
                    ry += 20f;
                }
                return ry;
            }
            if (sel.AttachState == 2)   // claimed
            {
                GUI.color = new Color(0.55f, 0.8f, 0.55f);
                DialogLayout.LabelTrunc(new Rect(inner.x, ry + 4f, inner.width, 20f), $"Attachment accepted - {AttachSummary(sel)}.");
                GUI.color = old;
                return ry + 24f;
            }
            if (sel.AttachState == 3)   // refunded / withdrawn
            {
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(inner.x, ry + 4f, inner.width, 20f), "Attachment returned to sender.");
                GUI.color = old;
                return ry + 24f;
            }
            return ry;
        }

        // "500 silver", "3 item(s)", "2 gear", or a combination.
        private static string AttachSummary(MailMessage sel)
        {
            int units = 0; if (sel.AttachedItems != null) foreach (int q in sel.AttachedItems.Values) units += q;
            int gear  = sel.AttachedPayloads?.Count ?? 0;
            List<string> parts = new List<string>();
            if (sel.AttachedSilver > 0) parts.Add($"{SilverFmt.Format(sel.AttachedSilver)} silver");
            if (units > 0)              parts.Add($"{units} item(s)");
            if (gear > 0)               parts.Add($"{gear} gear");
            return parts.Count > 0 ? string.Join(" + ", parts) : "attachment";
        }

        private static string ItemsLine(MailMessage sel)
        {
            List<string> parts = new List<string>();
            if (sel.AttachedItems != null)
                foreach (KeyValuePair<string, int> kv in sel.AttachedItems) parts.Add($"{ItemLabels.ResolveLabel(kv.Key)} x{kv.Value}");
            return string.Join(",  ", parts);
        }

        private static string GearLine(MailMessage sel)
        {
            List<string> parts = new List<string>();
            if (sel.AttachedPayloads != null)
                foreach (KMHPatch.Items.KmhThingPayload p in sel.AttachedPayloads)
                    if (p != null) parts.Add(string.IsNullOrEmpty(p.DisplayLabel) ? ItemLabels.ResolveLabel(p.DefName) : p.DisplayLabel);
            return string.Join(",  ", parts);
        }

        private void Select(MailMessage m)
        {
            _selectedId = m.Id;
            _bodyScroll = Vector2.zero;
            if (m.IsUnread) MailHandler.MarkRead(m.Id);
        }

        private static List<MailMessage> Messages()
        {
            List<MailMessage> list = MailCache.Snapshot?.Messages;
            return list ?? new List<MailMessage>();
        }

        private static MailMessage Find(List<MailMessage> msgs, long id)
        {
            if (id <= 0) return null;
            foreach (MailMessage m in msgs) if (m != null && m.Id == id) return m;
            return null;
        }

        private static string LocalTime(long utcTicks)
        {
            if (utcTicks <= 0) return "";
            return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("MMM d, HH:mm");
        }

        private static string Ago(long utcTicks, long now)
        {
            if (utcTicks <= 0 || now <= utcTicks) return "just now";
            TimeSpan s = TimeSpan.FromTicks(now - utcTicks);
            if (s.TotalDays    >= 1) return $"{(int)s.TotalDays}d ago";
            if (s.TotalHours   >= 1) return $"{(int)s.TotalHours}h ago";
            if (s.TotalMinutes >= 1) return $"{(int)s.TotalMinutes}m ago";
            return "just now";
        }
    }
}
