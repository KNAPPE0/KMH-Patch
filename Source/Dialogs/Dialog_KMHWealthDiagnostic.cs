using System.Collections.Generic;
using KMHPatch.Features.Wealth;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Reports the same numbers the storyteller reads, so a deposit can be checked against the raid maths directly.
    public class Dialog_KMHWealthDiagnostic : Window_KMHBase
    {
        protected override bool ClosesOnSessionEnd => false;

        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(560f, 520f);

        public Dialog_KMHWealthDiagnostic()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;
        }

        public static void Open() => Find.WindowStack.Add(new Dialog_KMHWealthDiagnostic());

        private Vector2 _scroll;

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "KMH storyteller wealth");
            DialogLayout.DrawSectionDivider(rect, ref y);

            Rect box   = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            float viewW = Mathf.Max(1f, box.width - DialogLayout.ScrollbarReserveWidth);

            KmhStack measure = new KmhStack(viewW, true);
            Body(measure);

            Rect view = new Rect(0f, 0f, viewW, Mathf.Max(measure.Y + 8f, box.height));
            Widgets.BeginScrollView(box, ref _scroll, view);
            Body(new KmhStack(viewW, false));
            Widgets.EndScrollView();

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void Body(KmhStack st)
        {
            if (!KmhWealthLedger.Enabled)
            {
                st.Header("Off-map wealth");
                st.Label("<color=#C8A24A>The server has turned the wealth feature off, so KMH adds nothing to threat scaling.</color>");
                return;
            }

            st.Header("KMH off-map holdings");

            List<KeyValuePair<string, float>> parts = KmhWealthLedger.Breakdown();
            float kmh = 0f;
            foreach (KeyValuePair<string, float> p in parts)
            {
                kmh += p.Value;
                Money(st, p.Key, p.Value, p.Value <= 0f);
            }

            st.Gap(6f);
            Money(st, "KMH contribution", kmh, false);

            st.Header("Storyteller");
            Map map = Find.CurrentMap;
            if (map?.wealthWatcher == null)
            {
                st.Label("<color=#9A9A9A>No active map.</color>");
                return;
            }

            float vanilla = map.wealthWatcher.WealthTotal;
            Money(st, "Map wealth (vanilla)", vanilla, false);
            Money(st, "Read by the storyteller", map.PlayerWealthForStoryteller, false);

            st.Gap(6f);
            st.Label("<color=#9A9A9A>Map wealth is recounted by RimWorld on its own schedule, so it lags a deposit. "
                   + "The KMH figure is added on every read and follows the treasury within a second.</color>");
        }

        private static void Money(KmhStack st, string label, float value, bool dim)
        {
            string v = value.ToString("N0");
            if (dim) { label = $"<color=#7A7A7A>{label}</color>"; v = $"<color=#7A7A7A>{v}</color>"; }
            st.ValueRow(label, v);
        }
    }
}
