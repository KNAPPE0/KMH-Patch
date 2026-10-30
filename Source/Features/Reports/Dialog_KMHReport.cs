using System;
using System.Collections.Generic;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Reports
{
    public class Dialog_KMHReport : Window_KMHBase
    {
        public override Vector2 InitialSize => DialogLayout.FitToScreen(680f, 720f);

        // Reachable from the main menu, where there is no session to end.
        protected override bool ClosesOnSessionEnd => false;

        private const string FieldPrefix = "kmh-report-";
        private const float LabelW = 110f;
        private const float RowH = 34f;
        private const float ControlH = 28f;
        private const float CheckRowH = 30f;

        private string _kind;
        private string _area = ReportAreas.General;
        private string _severity = "";
        private string _frequency = "";
        private string _title;
        private string _body;
        private bool _toServer;
        private bool _toDevelopers = true;
        private bool _serverTouched;
        private readonly byte[] _screenshot;
        private readonly string _screenshotProblem;
        private readonly KmhReportDiagnostics.Captured _diagnostics;
        private Texture2D _thumb;
        private bool _attachShot;
        private bool _attachDiagnostics;
        private bool _typing;
        private Vector2 _bodyScroll;
        private string _problem = "";

        internal Dialog_KMHReport(string kind, string title, string body, byte[] screenshot, string screenshotProblem,
                                  KmhReportDiagnostics.Captured diagnostics)
        {
            _kind = ReportKinds.Normalize(kind);
            _title = title ?? "";
            _body = body ?? "";
            if (_kind == ReportKinds.Bug && _body.Trim().Length == 0) _body = BugTemplate();
            _screenshot = screenshot;
            _screenshotProblem = screenshotProblem ?? "";
            _diagnostics = diagnostics;
            _attachShot = KMHPatchMod.Settings?.ReportAttachScreenshot ?? true;
            _attachDiagnostics = KMHPatchMod.Settings?.ReportAttachDiagnostics ?? true;
            _toServer = KmhReportClient.ServerAvailable;

            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;
            closeOnClickedOutside   = false;
            minSize                 = new Vector2(540f, 620f);
            KmhReportClient.Forget();
        }

        protected override bool TypingInThisWindow => _typing;

        public override void PreOpen()
        {
            base.PreOpen();
            if (_screenshot == null) return;
            try
            {
                _thumb = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!ImageConversion.LoadImage(_thumb, _screenshot)) { UnityEngine.Object.Destroy(_thumb); _thumb = null; }
            }
            catch { _thumb = null; }
        }

        public override void PostClose()
        {
            base.PostClose();
            if (_thumb != null) { UnityEngine.Object.Destroy(_thumb); _thumb = null; }
            KmhReportClient.Forget();
        }

        private static bool Locked => KmhReportClient.Phase != ReportPhase.Idle;

        internal static string BugTemplate()
            => KmhText.Get("KMH_Report_TplDid", "What I did:") + "\n\n\n"
             + KmhText.Get("KMH_Report_TplExpected", "What I expected:") + "\n\n\n"
             + KmhText.Get("KMH_Report_TplHappened", "What happened:") + "\n";

        private static bool IsUntouchedTemplate(string body) => (body ?? "").Trim() == BugTemplate().Trim();

        private void SetKind(string kind)
        {
            if (kind == _kind) return;
            if (_kind == ReportKinds.Bug && IsUntouchedTemplate(_body)) _body = "";
            _kind = kind;
            if (_kind == ReportKinds.Bug && _body.Trim().Length == 0) _body = BugTemplate();
        }

        protected override void DrawContents(Rect rect)
        {
            string focused = GUI.GetNameOfFocusedControl();
            _typing = !string.IsNullOrEmpty(focused) && focused.StartsWith(FieldPrefix, StringComparison.Ordinal);
            bool locked = Locked;
            bool available = KmhReportClient.ServerAvailable;
            // Followed until the player decides, so joining a server with the window open still offers its staff.
            if (!_serverTouched && !locked) _toServer = available;

            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_Report_Title", "Report a bug, an idea or a question"));
            DialogLayout.DrawSectionDivider(rect, ref y);
            Text.Font = GameFont.Small;

            RowLabel(y, KmhText.Get("KMH_Report_Kind", "What is it?"));
            float segW = Mathf.Min(150f, (rect.width - LabelW - 18f) / 3f);
            float x = LabelW + 6f;
            if (DialogLayout.DrawSegment(new Rect(x, y, segW, ControlH), KmhText.Get("KMH_Report_Bug", "Something is wrong"),
                                         _kind == ReportKinds.Bug, !locked)) SetKind(ReportKinds.Bug);
            if (DialogLayout.DrawSegment(new Rect(x + segW + 4f, y, segW, ControlH), KmhText.Get("KMH_Report_Suggestion", "An idea"),
                                         _kind == ReportKinds.Suggestion, !locked)) SetKind(ReportKinds.Suggestion);
            if (DialogLayout.DrawSegment(new Rect(x + (segW + 4f) * 2f, y, segW, ControlH), KmhText.Get("KMH_Report_Question", "A question"),
                                         _kind == ReportKinds.Question, !locked)) SetKind(ReportKinds.Question);
            y += RowH;

            RowLabel(y, KmhText.Get("KMH_Report_Area", "Area"));
            Rect areaRect = new Rect(LabelW + 6f, y, Mathf.Min(AreaWidth(), rect.width - LabelW - 6f), ControlH);
            if (locked) DialogLayout.LabelTrunc(new Rect(areaRect.x + 4f, areaRect.y + 3f, areaRect.width, 24f), AreaLabel(_area));
            else if (DialogLayout.DrawDropdownButton(areaRect, AreaLabel(_area))) OpenAreaMenu();
            y += RowH;

            if (_kind == ReportKinds.Bug)
            {
                y = ChoiceRow(rect, y, KmhText.Get("KMH_Report_Severity", "How bad?"), locked, ref _severity,
                    (ReportSeverities.Blocks,   KmhText.Get("KMH_Report_Sev_Blocks", "Blocks me")),
                    (ReportSeverities.Annoying, KmhText.Get("KMH_Report_Sev_Annoying", "Annoying")),
                    (ReportSeverities.Minor,    KmhText.Get("KMH_Report_Sev_Minor", "Minor")));
                y = ChoiceRow(rect, y, KmhText.Get("KMH_Report_Frequency", "How often?"), locked, ref _frequency,
                    (ReportFrequencies.Always,    KmhText.Get("KMH_Report_Freq_Always", "Every time")),
                    (ReportFrequencies.Sometimes, KmhText.Get("KMH_Report_Freq_Sometimes", "Sometimes")),
                    (ReportFrequencies.Once,      KmhText.Get("KMH_Report_Freq_Once", "Once")));
            }

            RowLabel(y, KmhText.Get("KMH_Report_Summary", "Summary"));
            const float counterW = 64f;
            Rect titleRect = new Rect(LabelW + 6f, y, Mathf.Max(60f, rect.width - LabelW - 12f - counterW), ControlH);
            if (locked) DialogLayout.LabelTrunc(new Rect(titleRect.x + 4f, titleRect.y + 3f, titleRect.width, 24f), KmhDisplayText.Inert(_title));
            else
            {
                GUI.SetNextControlName(FieldPrefix + "title");
                string t = Widgets.TextField(titleRect, _title ?? "");
                _title = t.Length > ReportLimits.MaxTitleChars ? t.Substring(0, ReportLimits.MaxTitleChars) : t;
                if (_title.Length == 0) Placeholder(titleRect, SummaryHint());
            }
            Counter(new Rect(rect.width - counterW, y + 4f, counterW, 22f), _title.Length, ReportLimits.MaxTitleChars);
            y += RowH;

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), KmhText.Get("KMH_Report_Details", "Details"));
            y += 22f;

            float below = BelowBody(rect, 0f, false, available);
            float bodyH = Mathf.Max(70f, rect.height - y - below - DialogLayout.FooterReserve - 22f);
            DrawBody(new Rect(0f, y, rect.width, bodyH), DetailsHint());
            y += bodyH + 2f;
            Counter(new Rect(rect.width - 120f, y, 120f, 20f), _body.Length, ReportLimits.MaxBodyChars);
            y += 20f;

            BelowBody(rect, y, true, available);
            DrawFooter(rect, available);
        }

        private static void RowLabel(float y, string text) => DialogLayout.LabelTrunc(new Rect(0f, y + 3f, LabelW, 24f), text);

        private static float ChoiceRow(Rect rect, float y, string label, bool locked, ref string value,
                                       params (string Id, string Label)[] options)
        {
            RowLabel(y, label);
            float w = Mathf.Min(140f, (rect.width - LabelW - 6f - 4f * (options.Length - 1)) / options.Length);
            for (int i = 0; i < options.Length; i++)
            {
                var r = new Rect(LabelW + 6f + i * (w + 4f), y, w, ControlH);
                if (DialogLayout.DrawSegment(r, options[i].Label, value == options[i].Id, !locked))
                    value = value == options[i].Id ? "" : options[i].Id;
            }
            return y + RowH;
        }

        private string SummaryHint()
        {
            switch (_kind)
            {
                case ReportKinds.Suggestion: return KmhText.Get("KMH_Report_SummaryHintIdea", "One line, e.g. \"Let me sort the quest board by reward\"");
                case ReportKinds.Question:   return KmhText.Get("KMH_Report_SummaryHintQuestion", "One line, e.g. \"How do guild taxes work?\"");
                default:                     return KmhText.Get("KMH_Report_SummaryHintBug", "One line, e.g. \"Marketplace window stays empty\"");
            }
        }

        private string DetailsHint()
        {
            switch (_kind)
            {
                case ReportKinds.Suggestion: return KmhText.Get("KMH_Report_DetailsHintIdea", "What you would like, and what it would help you do.");
                case ReportKinds.Question:   return KmhText.Get("KMH_Report_DetailsHintQuestion", "What you would like to know, and where you got stuck.");
                default:                     return KmhText.Get("KMH_Report_DetailsHintBug", "What happened, what you expected instead, and how to make it happen again.");
            }
        }

        internal static string AreaLabel(string area)
        {
            switch (area)
            {
                case "joining":      return KmhText.Get("KMH_Report_Area_Joining", "Joining & server browser");
                case "marketplace":  return KmhText.Get("KMH_Report_Area_Marketplace", "Marketplace");
                case "auctions":     return KmhText.Get("KMH_Report_Area_Auctions", "Auctions");
                case "want_board":   return KmhText.Get("KMH_Report_Area_WantBoard", "Want Board");
                case "treasury":     return KmhText.Get("KMH_Report_Area_Treasury", "Treasury");
                case "mail":         return KmhText.Get("KMH_Report_Area_Mail", "Mail");
                case "chat":         return KmhText.Get("KMH_Report_Area_Chat", "Chat");
                case "guilds":       return KmhText.Get("KMH_Report_Area_Guilds", "Guilds");
                case "quests":       return KmhText.Get("KMH_Report_Area_Quests", "Quests");
                case "world_events": return KmhText.Get("KMH_Report_Area_WorldEvents", "World events");
                case "sites":        return KmhText.Get("KMH_Report_Area_Sites", "Sites");
                case "roadworks":    return KmhText.Get("KMH_Report_Area_Roadworks", "Roadworks");
                case "frontier":     return KmhText.Get("KMH_Report_Area_Frontier", "Frontier");
                case "standings":    return KmhText.Get("KMH_Report_Area_Standings", "Standings & leaderboards");
                case "performance":  return KmhText.Get("KMH_Report_Area_Performance", "Performance");
                case "other":        return KmhText.Get("KMH_Report_Area_Other", "Other");
                default:             return KmhText.Get("KMH_Report_Area_General", "General");
            }
        }

        private float _areaWidth;

        private float AreaWidth()
        {
            if (_areaWidth > 0f) return _areaWidth;
            var labels = new List<string>();
            foreach (string a in ReportAreas.All) labels.Add(AreaLabel(a));
            return _areaWidth = DialogLayout.DropdownWidth(labels, 180f, 280f);
        }

        private void OpenAreaMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (string a in ReportAreas.All)
            {
                string id = a;
                options.Add(new FloatMenuOption(AreaLabel(id), () => _area = id));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawBody(Rect box, string hint)
        {
            if (Locked)
            {
                Widgets.DrawBoxSolid(box, new Color(0.10f, 0.10f, 0.11f));
                Rect inner = box.ContractedBy(6f);
                string shown = KmhDisplayText.Inert(_body);
                float h = Mathf.Max(inner.height, Text.CalcHeight(shown, inner.width - 16f) + 6f);
                Widgets.BeginScrollView(inner, ref _bodyScroll, new Rect(0f, 0f, inner.width - 16f, h));
                Widgets.Label(new Rect(0f, 0f, inner.width - 16f, h), shown);
                Widgets.EndScrollView();
                return;
            }
            // Verse has no scrollable text area; the view leads the text's growth.
            float viewW = box.width - 16f;
            float viewH = Mathf.Max(box.height, Text.CalcHeight(_body + "\n\n", viewW) + 10f);
            Widgets.BeginScrollView(box, ref _bodyScroll, new Rect(0f, 0f, viewW, viewH));
            GUI.SetNextControlName(FieldPrefix + "body");
            string b = Widgets.TextArea(new Rect(0f, 0f, viewW, viewH), _body ?? "");
            Widgets.EndScrollView();
            _body = b.Length > ReportLimits.MaxBodyChars ? b.Substring(0, ReportLimits.MaxBodyChars) : b;
            if (_body.Length == 0) Placeholder(new Rect(box.x, box.y + 2f, box.width, 24f), hint);
        }

        // One path measures and draws, so the body above is sized by exactly what is drawn here.
        private float BelowBody(Rect rect, float y, bool draw, bool available)
        {
            float top = y;
            y += 6f;
            Color old = GUI.color;
            if (KmhReportClient.Phase == ReportPhase.Idle)
            {
                if (draw)
                {
                    RowLabel(y - 3f, KmhText.Get("KMH_Report_SendTo", "Send to"));
                    float x = LabelW + 6f;
                    float half = (rect.width - x) / 2f;
                    string staffTip = available ? null
                        : KmhText.Get("KMH_Report_StaffUnavailable", "Join a Colony Nexus server that takes reports to send one to its staff.");
                    bool staff = _toServer && available;
                    if (CheckboxCell(x, y, half - 8f, KmhText.Get("KMH_Report_ToStaff", "This server's staff"), ref staff, available, staffTip)
                        && available)
                    {
                        _toServer = staff;
                        _serverTouched = true;
                    }
                    CheckboxCell(x + half, y, half, KmhText.Get("KMH_Report_ToDevelopers", "Colony Nexus developers"), ref _toDevelopers, true,
                                 KmhText.Get("KMH_Report_DevelopersTip", "The people who make Colony Nexus. They read every report, whichever server it came from."));
                }
                y += CheckRowH - 4f;

                y += Paragraph(rect, y, HowLine(available), DialogLayout.MutedColor, old, draw) + 6f;

                if (draw) DrawAttachRows(rect, y);
                y += CheckRowH * 2f + 4f;
            }
            else
            {
                if (draw) DrawReference(rect, y);
                y += ControlH + 4f;
                foreach ((string text, Color color) in StatusLines())
                    y += Paragraph(rect, y, text, color, old, draw) + 2f;
                y += 4f;
            }

            if (_problem.Length > 0)
                y += Paragraph(rect, y, _problem, KmhTheme.Warning, old, draw) + 4f;

            y += Paragraph(rect, y, PrivacyLine(), DialogLayout.MutedColor, old, draw) + 4f;
            return y - top;
        }

        private static float Paragraph(Rect rect, float y, string text, Color color, Color restore, bool draw)
        {
            float h = Text.CalcHeight(text, rect.width);
            if (draw)
            {
                GUI.color = color;
                Widgets.Label(new Rect(0f, y, rect.width, h), text);
                GUI.color = restore;
            }
            return h;
        }

        private string HowLine(bool available)
        {
            bool staff = _toServer && available;
            if (!staff && !_toDevelopers)
                return KmhText.Get("KMH_Report_NeedDestination", "Choose who should get it - this server's staff, the Colony Nexus developers, or both.");
            return KmhReportClient.HowText(KmhReportClient.CurrentView(), staff, _toDevelopers);
        }

        private void DrawAttachRows(Rect rect, float y)
        {
            RowLabel(y - 3f, KmhText.Get("KMH_Report_Attach", "Attach"));
            float x = LabelW + 6f;
            bool canShot = _screenshot != null;
            string shotLabel = canShot ? KmhText.Get("KMH_Report_AttachShot", "A screenshot of the game as it was when you opened this")
                                       : KmhText.Get("KMH_Report_NoShot", "No screenshot - {0}", _screenshotProblem.Length > 0 ? _screenshotProblem : "none was taken");
            bool shot = _attachShot && canShot;
            float thumbW = _thumb != null ? 96f : 0f;
            if (CheckboxCell(x, y, rect.width - x - thumbW - 10f, shotLabel, ref shot, canShot, null) && canShot)
            {
                _attachShot = shot;
                Remember();
            }
            if (_thumb != null)
            {
                Rect thumb = new Rect(rect.width - thumbW, y - 4f, thumbW, CheckRowH + 2f);
                GUI.DrawTexture(thumb, _thumb, ScaleMode.ScaleToFit);
                if (Mouse.IsOver(thumb)) TooltipHandler.TipRegion(thumb, KmhText.Get("KMH_Report_ShotTip", "This is what will be attached."));
            }
            y += CheckRowH;

            const float previewW = 120f;
            bool diag = _attachDiagnostics;
            if (CheckboxCell(x, y, rect.width - x - previewW - 10f, KmhText.Get("KMH_Report_AttachDiagnostics",
                    "Diagnostics - Colony Nexus log, game errors, mod list, performance, system and settings"), ref diag, true, null))
            {
                _attachDiagnostics = diag;
                Remember();
            }
            if (Widgets.ButtonText(new Rect(rect.width - previewW, y - 3f, previewW, 26f), KmhText.Get("KMH_Report_Preview", "See what's sent")))
                Find.WindowStack.Add(new Dialog_KMHReportPreview(Fields(), BodyToSend(), _attachShot && _screenshot != null ? _screenshot.Length : 0,
                                                                 _diagnostics, _attachDiagnostics, HowLine(KmhReportClient.ServerAvailable)));
        }

        private static bool CheckboxCell(float x, float y, float width, string label, ref bool value, bool enabled, string tip)
        {
            string shown = Fit(label, Mathf.Max(20f, width - 40f));
            bool before = value;
            if (enabled) DialogLayout.DrawTightCheckbox(x, y, shown, ref value);
            else
            {
                Color old = GUI.color;
                GUI.color = DialogLayout.MutedColor;
                Rect box = new Rect(x, y - 2f, 20f, 20f);
                Widgets.DrawBoxSolid(box, new Color(0.12f, 0.12f, 0.12f));
                Widgets.DrawBox(box);
                Widgets.Label(new Rect(x + 24f, y, Mathf.Max(1f, width - 24f), 22f), shown);
                GUI.color = old;
            }
            string tipText = shown != label ? (string.IsNullOrEmpty(tip) ? label : label + "\n\n" + tip) : tip;
            Rect hit = new Rect(x, y - 2f, Mathf.Min(width, DialogLayout.TightCheckboxWidth(shown)), 24f);
            if (!string.IsNullOrEmpty(tipText) && Mouse.IsOver(hit)) TooltipHandler.TipRegion(hit, tipText);
            return enabled && value != before;
        }

        private static void DrawReference(Rect rect, float y)
        {
            ReportOutboxEntry e = KmhReportClient.Shown;
            string reference = e?.Reference ?? KmhReportClient.Short(KmhReportClient.ShownId);
            string label = KmhText.Get("KMH_Report_Reference", "Reference {0}", reference);
            Rect r = new Rect(0f, y, Mathf.Max(60f, rect.width - 100f), ControlH);
            Text.Font = GameFont.Medium;
            DialogLayout.LabelTrunc(r, label, TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            if (Mouse.IsOver(r))
                TooltipHandler.TipRegion(r, KmhText.Get("KMH_Report_ReferenceTip", "Quote this when you talk to the server's staff or the Colony Nexus developers about it."));
            if (reference.Length > 0 && Widgets.ButtonText(new Rect(rect.width - 90f, y, 90f, 26f), KmhText.Get("KMH_Report_CopyRef", "Copy")))
                GUIUtility.systemCopyBuffer = reference;
        }

        private static readonly Color SentColor = new Color(0.55f, 0.85f, 0.55f);

        private static List<(string, Color)> StatusLines()
        {
            var lines = new List<(string, Color)>();
            ReportOutboxEntry e = KmhReportClient.Shown;
            if (KmhReportClient.Phase == ReportPhase.Preparing || e == null)
            {
                lines.Add(("● " + KmhText.Get("KMH_Report_Preparing", "Preparing your report…"), KmhTheme.Info));
                return lines;
            }
            if (e.Server.Chosen)
                lines.Add(Line(KmhText.Get("KMH_Report_ToStaff", "This server's staff"), e.Server, true, ""));
            if (e.Developers.Chosen)
                lines.Add(Line(KmhText.Get("KMH_Report_ToDevelopers", "Colony Nexus developers"), e.Developers, false,
                               KmhReportClient.ServerLabel(KmhDispatcher.ServerName)));
            if (string.IsNullOrEmpty(e.Stem))
                lines.Add((KmhText.Get("KMH_Report_NotSavedLocal", "It could not be saved on this computer, so it is lost if the game closes before it is delivered."), KmhTheme.Warning));
            return lines;
        }

        private static (string, Color) Line(string who, ReportDelivery d, bool staff, string server)
        {
            switch (d.State)
            {
                case ReportDeliveryStates.Sending:
                    return ("● " + KmhText.Get("KMH_Report_StateSending", "{0}: sending…", who), KmhTheme.Info);
                case ReportDeliveryStates.Sent:
                    if (d.Via == ReportVia.Global)
                        return ("✓ " + KmhText.Get("KMH_Report_StateDirect", "{0}: delivered with your linked account.", who), SentColor);
                    return ("✓ " + (staff ? KmhText.Get("KMH_Report_StateDelivered", "{0}: delivered.", who)
                                          : KmhText.Get("KMH_Report_StateRelayed", "{0}: on its way through {1}.", who, server)), SentColor);
                case ReportDeliveryStates.Failed:
                    return ("✗ " + KmhText.Get("KMH_Report_StateFailed", "{0}: not delivered - {1}.", who, KmhReportClient.ReasonText(d)), KmhTheme.Warning);
                default:
                    return ("○ " + KmhText.Get("KMH_Report_StatePending", "{0}: saved, and sent automatically as soon as possible ({1}).",
                                               who, KmhReportClient.ReasonText(d)), KmhTheme.Info);
            }
        }

        private static string PrivacyLine()
            => KmhText.Get("KMH_Report_Privacy",
                "Never included: passwords, account tokens, chat or save files. Your Windows user folder is removed from every path.");

        private void DrawFooter(Rect rect, bool available)
        {
            float y = rect.height - DialogLayout.FooterReserve + 6f;
            const float w = 150f, h = 32f;

            if (KmhReportClient.Phase == ReportPhase.Idle)
            {
                if (Widgets.ButtonText(new Rect(0f, y, 110f, h), KmhText.Get("KMH_Common_Cancel", "Cancel"))) Close();
                if (Widgets.ButtonText(new Rect(rect.width - w, y, w, h), KmhText.Get("KMH_Report_Send", "Send report"))) TrySend(available);
                return;
            }

            if (Widgets.ButtonText(new Rect(0f, y, 110f, h), KmhText.Get("KMH_Common_Close", "Close"))) Close();
            float x = rect.width - w;
            if (KmhReportClient.CanResend)
            {
                if (Widgets.ButtonText(new Rect(x, y, w, h), KmhText.Get("KMH_Report_SendAgain", "Send again"))) KmhReportClient.Resend();
                x -= w + 8f;
            }
            if (!string.IsNullOrEmpty(KmhReportClient.SavedCopyPath)
                && Widgets.ButtonText(new Rect(x, y, w, h), KmhText.Get("KMH_Report_OpenCopies", "Open saved copy")))
            {
                try { Application.OpenURL(KmhReportClient.CopiesFolder); } catch { }
            }
        }

        private ReportFields Fields()
            => new ReportFields
            {
                Kind = _kind, Area = _area, Severity = _severity, Frequency = _frequency,
                ToServer = _toServer && KmhReportClient.ServerAvailable, ToDevelopers = _toDevelopers, Title = _title,
            };

        // The template alone says nothing, so it is not sent.
        private string BodyToSend() => IsUntouchedTemplate(_body) ? "" : _body;

        private void TrySend(bool available)
        {
            if (_title.Trim().Length == 0)
            {
                _problem = KmhText.Get("KMH_Report_NeedSummary", "Give it a one-line summary first - it is what the owner sees in the list.");
                GUI.FocusControl(FieldPrefix + "title");
                return;
            }
            if (!(_toServer && available) && !_toDevelopers)
            {
                _problem = KmhText.Get("KMH_Report_NeedDestination", "Choose who should get it - this server's staff, the Colony Nexus developers, or both.");
                return;
            }
            _problem = "";
            GUI.FocusControl(null);
            KmhReportClient.Send(Fields(), BodyToSend(), _attachShot ? _screenshot : null, Clone(_diagnostics), _attachDiagnostics);
        }

        // A copy: Complete trims and scrubs in place.
        internal static KmhReportDiagnostics.Captured Clone(KmhReportDiagnostics.Captured c)
            => c == null ? null : new KmhReportDiagnostics.Captured
               {
                   Root = (JObject)c.Root?.DeepClone(), Redact = c.Redact, LogPath = c.LogPath, TopError = c.TopError,
                   Versions = (JObject)(c.Versions ?? new JObject()).DeepClone(),
               };

        private void Remember()
        {
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s == null) return;
            s.ReportAttachScreenshot = _attachShot;
            s.ReportAttachDiagnostics = _attachDiagnostics;
            KMHPatchMod.SaveSettings();
        }

        private static void Placeholder(Rect r, string text)
        {
            Color old = GUI.color;
            TextAnchor anchor = Text.Anchor;
            GUI.color = new Color(1f, 1f, 1f, 0.38f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(r.x + 7f, r.y, Mathf.Max(1f, r.width - 12f), Mathf.Max(24f, r.height)), text);
            Text.Anchor = anchor;
            GUI.color = old;
        }

        private static void Counter(Rect r, int used, int max)
        {
            Color old = GUI.color;
            GUI.color = used >= max ? KmhTheme.Warning : DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(r, $"{used}/{max}", TextAnchor.MiddleRight);
            GUI.color = old;
        }

        private static string Fit(string label, float width)
        {
            if (width <= 20f || Text.CalcSize(label).x <= width) return label;
            return label.Truncate(width);
        }
    }

    // Drawn only where visible, so a long log costs no frame.
    public class Dialog_KMHReportPreview : Window_KMHBase
    {
        public override Vector2 InitialSize => DialogLayout.FitToScreen(820f, 680f);
        protected override bool ClosesOnSessionEnd => false;

        private readonly List<string> _lines = new List<string>();
        private string _json = "";
        private int _diagnosticsChars;
        private Vector2 _scroll;

        internal Dialog_KMHReportPreview(ReportFields fields, string body, int screenshotBytes, KmhReportDiagnostics.Captured diagnostics,
                                         bool attachDiagnostics, string delivery)
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
            try
            {
                string diag = KmhReportDiagnostics.Complete(Dialog_KMHReport.Clone(diagnostics), fields, attachDiagnostics, delivery,
                                                            ReportLimits.MaxDiagnosticsChars);
                _diagnosticsChars = diag.Length;
                _json = PreviewObject(fields, body, screenshotBytes, diag).ToString(Formatting.Indented);
                // Mod names and log lines are others' text: their markup stays inert.
                foreach (string line in _json.Replace("\r\n", "\n").Split('\n')) _lines.Add(KmhDisplayText.Inert(line));
            }
            catch (Exception ex) { _lines.Add("Could not build the preview: " + ex.Message); }
        }

        internal static JObject PreviewObject(ReportFields fields, string body, int screenshotBytes, string diagnostics)
        {
            ReportFields f = (fields ?? new ReportFields()).Normalized();
            JToken diag;
            try { diag = string.IsNullOrEmpty(diagnostics) ? (JToken)"" : JToken.Parse(diagnostics); }
            catch { diag = diagnostics ?? ""; }
            return new JObject
            {
                ["report_id"]          = KmhText.Get("KMH_Report_PreviewIdLater", "assigned when you press Send"),
                ["kind"]               = f.Kind,
                ["title"]              = (f.Title ?? "").Trim(),
                ["body"]               = (body ?? "").Trim(),
                ["area"]               = f.Area,
                ["severity"]           = f.Severity,
                ["frequency"]          = f.Frequency,
                ["destinations"]       = new JArray(ReportDestinations.Of(f.ToServer, f.ToDevelopers).ToArray()),
                ["client_version"]     = KmhProtocol.DisplayVersion + "+" + KmhProtocol.UiBuildTag,
                ["screenshot"]         = screenshotBytes > 0
                    ? KmhText.Get("KMH_Report_PreviewShot", "a JPEG picture, {0} KB", ((screenshotBytes + 1023) / 1024).ToString()) : "",
                ["client_diagnostics"] = diag,
            };
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_Report_PreviewTitle", "Everything that is sent"));
            Color old = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            string note = KmhText.Get("KMH_Report_PreviewNote", "{0} characters of diagnostics. Exactly this goes with the report when you press Send.",
                                      _diagnosticsChars.ToString("N0"));
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), note);
            GUI.color = old;
            y += 26f;

            Text.Font = GameFont.Tiny;
            float rowH = Mathf.Ceil(Text.LineHeight) + 1f;
            Rect box = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawBoxSolid(box, new Color(0.09f, 0.09f, 0.10f));
            DialogLayout.ScrollList(box, ref _scroll, _lines.Count, rowH, (i, row) =>
                DialogLayout.LabelTrunc(new Rect(row.x + 4f, row.y, row.width - 4f, row.height), _lines[i]));
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(new Rect(0f, rect.height - DialogLayout.FooterReserve + 6f, 160f, 32f),
                                   KmhText.Get("KMH_Report_CopyAll", "Copy all")))
                GUIUtility.systemCopyBuffer = _json;
            if (DialogLayout.DrawCloseButton(rect)) Close();
        }
    }
}
