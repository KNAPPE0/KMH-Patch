using System.Collections.Generic;
using System.Threading;
using KMHPatch.Diagnostics;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Reports
{
    // A Colony Nexus error offers a ready report once: the player who just saw it is the one who knows what they did.
    internal static class KmhErrorOffer
    {
        internal const int MaxOffersPerSession = 3;
        private static long _seen;
        private static float _next;
        private static int _offers;
        private static readonly HashSet<string> _offered = new HashSet<string>();

        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 2f;
            long count = Interlocked.Read(ref KmhLog.ErrorCount);
            if (count == _seen) return;
            _seen = count;
            string error = KmhLog.LastError ?? "";
            string signature = KmhGameLogWatch.Signature(error);
            bool busy = LongEventHandler.AnyEventNowOrWaiting || KmhReportClient.Phase != ReportPhase.Idle
                        || Find.WindowStack == null || Find.WindowStack.IsOpen<Dialog_KMHErrorOffer>();
            if (!ShouldOffer(signature, KMHPatchMod.Settings?.OfferErrorReports ?? true, busy, _offers, _offered)) return;
            _offered.Add(signature);
            _offers++;
            Find.WindowStack.Add(new Dialog_KMHErrorOffer(signature, error));
        }

        internal static bool ShouldOffer(string signature, bool enabled, bool busy, int offersSoFar, HashSet<string> offered)
            => enabled && !busy && !string.IsNullOrEmpty(signature) && offersSoFar < MaxOffersPerSession && !offered.Contains(signature);
    }

    internal sealed class Dialog_KMHErrorOffer : Window
    {
        private readonly string _signature, _error;
        private readonly float _openedAt;
        private const float StaysSeconds = 45f;

        public override Vector2 InitialSize => new Vector2(440f, 178f);

        public Dialog_KMHErrorOffer(string signature, string error)
        {
            _signature = signature ?? "";
            _error = error ?? "";
            _openedAt = Time.realtimeSinceStartup;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            soundAppear = null;
            layer = WindowLayer.GameUI;
        }

        protected override void SetInitialSizeAndPosition()
            => windowRect = new Rect(Verse.UI.screenWidth - InitialSize.x - 18f, Verse.UI.screenHeight - InitialSize.y - 90f, InitialSize.x, InitialSize.y);

        public override void DoWindowContents(Rect r)
        {
            if (Time.realtimeSinceStartup - _openedAt > StaysSeconds) { Close(false); return; }
            Text.Font = GameFont.Small;
            float line = DialogLayout.TextRowH;
            DialogLayout.LabelTrunc(new Rect(0f, 0f, r.width - 24f, line),
                "<b>" + KmhText.Get("KMH_ErrorOffer_Title", "Colony Nexus hit a problem") + "</b>");
            Rect sig = new Rect(0f, line + 2f, r.width, line);
            DialogLayout.LabelTrunc(sig, "<color=#A9AFB9>" + KmhDisplayText.Inert(_signature, 160) + "</color>");
            TooltipHandler.TipRegion(sig, KmhDisplayText.Inert(_error, 1200));
            string why = KmhText.Get("KMH_ErrorOffer_Body", "Reporting it now, while you remember what you were doing, helps the most.");
            float whyH = Text.CalcHeight(why, r.width);
            Widgets.Label(new Rect(0f, line * 2f + 6f, r.width, whyH), why);

            float by = r.height - 32f, gap = 6f;
            string report = KmhText.Get("KMH_ErrorOffer_Report", "Report it"), later = KmhText.Get("KMH_ErrorOffer_Later", "Not now"),
                   never = KmhText.Get("KMH_ErrorOffer_Never", "Don't offer again");
            float w1 = Text.CalcSize(report).x + 24f, w2 = Text.CalcSize(later).x + 24f, w3 = Text.CalcSize(never).x + 24f;
            if (Widgets.ButtonText(new Rect(0f, by, w1, 30f), report))
            {
                Close(false);
                string title = string.Format(KmhText.Get("KMH_ErrorOffer_ReportTitle", "Error: {0}"), _signature);
                KmhReportClient.Open(ReportKinds.Bug, title.Length > 120 ? title.Substring(0, 119) + "…" : title,
                                     KmhText.Get("KMH_ErrorOffer_ReportBody", "What I was doing when it happened:") + "\n\n\n"
                                     + KmhText.Get("KMH_ErrorOffer_ReportError", "The error Colony Nexus logged:") + "\n" + _error);
            }
            if (Widgets.ButtonText(new Rect(w1 + gap, by, w2, 30f), later)) Close(false);
            if (Widgets.ButtonText(new Rect(w1 + w2 + gap * 2f, by, Mathf.Min(w3, r.width - w1 - w2 - gap * 2f), 30f), never))
            {
                if (KMHPatchMod.Settings != null) { KMHPatchMod.Settings.OfferErrorReports = false; KMHPatchMod.SaveSettings(); }
                Close(false);
            }
        }
    }
}
