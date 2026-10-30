using System;
using System.Collections.Generic;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Reports
{
    public class Dialog_KMHMyReports : Window_KMHBase
    {
        private Vector2 _scroll;

        public override Vector2 InitialSize => DialogLayout.FitToScreen(720f, 560f);

        public static void Open()
        {
            Find.WindowStack.Add(new Dialog_KMHMyReports());
            if (KmhMyReports.Linked) KmhMyReports.Refresh();
        }

        private sealed class Row
        {
            public string Id = "", Reference = "", Title = "", Kind = "", Server = "";
            public long Created;
            public ReportOutboxEntry Local;
            public KmhMyReports.Remote Remote;
        }

        protected override void DrawContents(Rect inRect)
        {
            float y = DialogLayout.DrawTitle(inRect, KmhText.Get("KMH_MyReports_Title", "My reports"));

            string state = !KmhMyReports.Linked ? KmhText.Get("KMH_MyReports_NotLinked", "Link your Colony Nexus account to see what the developers did with your reports.")
                : KmhMyReports.Fetching ? KmhText.Get("KMH_MyReports_Checking", "Asking Colony Nexus for news on your reports…")
                : KmhMyReports.LastProblem.Length > 0 ? KmhMyReports.LastProblem
                : KmhMyReports.LastFetchUtc == DateTime.MinValue ? ""
                : string.Format(KmhText.Get("KMH_MyReports_Checked", "Developer status checked {0} ago."), Ago(DateTime.UtcNow - KmhMyReports.LastFetchUtc));
            float refreshW = KmhMyReports.Linked ? Mathf.Max(110f, Text.CalcSize(KmhText.Get("KMH_MyReports_Refresh", "Check now")).x + 24f) : 0f;
            DialogLayout.LabelTrunc(new Rect(0f, y, inRect.width - refreshW - 8f, 28f), $"<color=#A9AFB9>{state}</color>", TextAnchor.MiddleLeft);
            if (refreshW > 0f && !KmhMyReports.Fetching
                && Widgets.ButtonText(new Rect(inRect.width - refreshW, y, refreshW, 28f), KmhText.Get("KMH_MyReports_Refresh", "Check now")))
                KmhMyReports.Refresh();
            y += 34f;

            List<Row> rows = Rows();
            float footerH = 40f;
            Rect list = new Rect(0f, y, inRect.width, inRect.height - y - footerH - 6f);
            Widgets.DrawMenuSection(list);
            Rect inner = list.ContractedBy(DialogLayout.ListInnerPad);
            float line = DialogLayout.TextRowH, rowH = line * 3f + 12f;
            Rect view = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, Mathf.Max(inner.height, rows.Count * rowH));
            Widgets.BeginScrollView(inner, ref _scroll, view);
            if (rows.Count == 0)
                DialogLayout.LabelTrunc(new Rect(6f, 6f, view.width - 12f, line),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_MyReports_None", "No reports sent from this game yet.") + "</color>");
            DialogLayout.VisibleRange(_scroll, inner.height, rowH, rows.Count, out int first, out int last);
            for (int i = first; i < last; i++) DrawRow(new Rect(0f, i * rowH, view.width, rowH - 4f), rows[i], i, line);
            Widgets.EndScrollView();

            Rect foot = new Rect(0f, inRect.height - footerH, inRect.width, footerH);
            string newLabel = KmhText.Get("KMH_Report_Button", "Report a bug or suggestion");
            float newW = Mathf.Min(foot.width * 0.5f, Text.CalcSize(newLabel).x + 30f);
            if (Widgets.ButtonText(new Rect(foot.x, foot.y + 4f, newW, 32f), newLabel)) KmhReportClient.Open();
            string folderLabel = KmhText.Get("KMH_MyReports_Folder", "Open saved copies");
            float folderW = Text.CalcSize(folderLabel).x + 30f;
            if (Widgets.ButtonText(new Rect(foot.x + newW + 8f, foot.y + 4f, folderW, 32f), folderLabel))
                Application.OpenURL("file:///" + KmhReportClient.CopiesFolder.Replace('\\', '/'));
            if (DialogLayout.DrawCloseButton(inRect)) Close();
        }

        private static void DrawRow(Rect r, Row row, int index, float line)
        {
            if (index % 2 == 0) Widgets.DrawAltRect(r);
            float x = r.x + 6f, w = r.width - 12f;
            string when = row.Created > 0 ? DateTimeOffset.FromUnixTimeSeconds(row.Created).LocalDateTime.ToString("d MMM HH:mm") : "";
            string head = $"<b>{row.Reference}</b>  <color=#A9AFB9>{when} · {KindLabel(row.Kind)}</color>  {KmhDisplayText.Inert(row.Title, 120)}";
            DialogLayout.LabelTrunc(new Rect(x, r.y + 3f, w, line), head);

            var parts = new List<string>();
            if (row.Local != null)
            {
                string server = Delivery(row.Local.Server);
                if (server.Length > 0) parts.Add(KmhText.Get("KMH_MyReports_ToServer", "Server staff") + ": " + server);
                string devs = Delivery(row.Local.Developers);
                if (devs.Length > 0) parts.Add(KmhText.Get("KMH_MyReports_ToDevs", "Developers") + ": " + devs);
            }
            if (row.Remote != null)
            {
                string status = KmhMyReports.StatusLabel(row.Remote.Status, row.Remote.FixedIn);
                if (status.Length > 0) parts.Add("<color=#f0a24a>" + status + "</color>");
            }
            DialogLayout.LabelTrunc(new Rect(x, r.y + 3f + line, w, line), string.Join("   ·   ", parts));

            List<KmhMyReports.Reply> replies = row.Remote?.Messages;
            if (replies != null && replies.Count > 0)
            {
                KmhMyReports.Reply latest = replies[replies.Count - 1];
                Rect replyRect = new Rect(x, r.y + 3f + line * 2f, w, line);
                DialogLayout.LabelTrunc(replyRect, "<color=#4cc38a>" + KmhText.Get("KMH_MyReports_Reply", "Developers replied") + ":</color> "
                                                   + KmhDisplayText.Inert(latest.Body, 400));
                var all = new System.Text.StringBuilder();
                foreach (KmhMyReports.Reply reply in replies) all.AppendLine(KmhDisplayText.Inert(reply.Body, 2000)).AppendLine();
                TooltipHandler.TipRegion(replyRect, all.ToString().TrimEnd());
            }
            else if (!string.IsNullOrEmpty(row.Server))
                DialogLayout.LabelTrunc(new Rect(x, r.y + 3f + line * 2f, w, line),
                    "<color=#A9AFB9>" + string.Format(KmhText.Get("KMH_MyReports_SentFrom", "Sent while on {0}"), KmhDisplayText.Inert(row.Server, 60)) + "</color>");
        }

        private static List<Row> Rows()
        {
            var rows = new List<Row>();
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReportOutboxEntry e in KmhReportOutbox.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.ReportId)) continue;
                have.Add(e.ReportId);
                rows.Add(new Row { Id = e.ReportId, Reference = e.Reference, Title = e.Title, Kind = e.Kind, Server = e.ServerName,
                                   Created = e.CreatedUnix, Local = e, Remote = KmhMyReports.For(e.ReportId) });
            }
            foreach (KmhMyReports.Remote r in KmhMyReports.All())
                if (!have.Contains(r.ReportId))
                    rows.Add(new Row { Id = r.ReportId, Reference = string.IsNullOrEmpty(r.Reference) ? KmhReportClient.Short(r.ReportId) : r.Reference,
                                       Title = r.Title, Kind = r.Kind, Created = r.CreatedUnix, Remote = r });
            rows.Sort((a, b) => b.Created.CompareTo(a.Created));
            return rows;
        }

        internal static string Delivery(ReportDelivery d)
        {
            if (d == null || !d.Chosen) return "";
            switch (d.State)
            {
                case ReportDeliveryStates.Sent:    return KmhText.Get("KMH_MyReports_Sent", "sent");
                case ReportDeliveryStates.Sending: return KmhText.Get("KMH_MyReports_Sending", "sending…");
                case ReportDeliveryStates.Failed:  return KmhText.Get("KMH_MyReports_NotSent", "not sent");
                default:                           return KmhText.Get("KMH_MyReports_Waiting", "waiting to send");
            }
        }

        private static string KindLabel(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "suggestion": return KmhText.Get("KMH_MyReports_KindIdea", "Idea");
                case "question":   return KmhText.Get("KMH_MyReports_KindQuestion", "Question");
                default:           return KmhText.Get("KMH_MyReports_KindBug", "Bug");
            }
        }

        private static string Ago(TimeSpan t)
            => t.TotalMinutes < 1 ? KmhText.Get("KMH_MyReports_Moments", "moments")
             : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min" : $"{(int)t.TotalHours} h";
    }
}
