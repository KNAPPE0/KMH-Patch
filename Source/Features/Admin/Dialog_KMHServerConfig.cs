using System;
using System.Collections.Generic;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Admin
{
    // Never applied locally: the server writes the file, re-clamps on reload and echoes what it holds, which is shown.
    public class Dialog_KMHServerConfig : Window_KMHBase
    {
        public override Vector2 InitialSize => DialogLayout.FitToScreen(980f, 660f);

        private string _area;
        private Vector2 _sideScroll;
        private Vector2 _fieldScroll;
        // Only changed fields are sent, so two owners cannot overwrite each other's untouched settings with a stale copy.
        private readonly Dictionary<string, string> _edits = new Dictionary<string, string>(StringComparer.Ordinal);
        // The serial is kept apart from the flag: a serial of zero is a real value, not "nothing in flight".
        private bool _inFlight;
        private int  _sentAtSerial;

        public Dialog_KMHServerConfig()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            forcePause = false;
            draggable = true;
            resizeable = true;
            ServerConfigCache.Updated += MarkRefreshed;
        }

        // The server refuses a non-owner anyway; this only decides whether a button worth pressing is shown at all.
        public static bool AvailableToMe
            => SubProtocol.KmhCapabilities.Has(SubProtocol.KmhCapabilities.ConfigEditor)
            && string.Equals(Identity.KmhStaff.RoleOf(KmhSession.Me), "owner", StringComparison.OrdinalIgnoreCase);

        public static void Open()
        {
            ServerConfigCache.Clear();
            ServerConfigCache.RequestAreas();
            Find.WindowStack.Add(new Dialog_KMHServerConfig());
        }

        public override void PostClose()
        {
            base.PostClose();
            ServerConfigCache.Updated -= MarkRefreshed;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Server settings");

            List<string> areas = ServerConfigCache.Areas();
            if (areas.Count == 0)
            {
                // The server's own refusal is specific and actionable; the generic line is only for no answer at all.
                if (!ServerConfigCache.LastResultOk && ServerConfigCache.LastResult.Length > 0)
                    Widgets.Label(new Rect(rect.x, y + 8f, rect.width, 96f), ServerConfigCache.LastResult);
                else DialogLayout.DrawWaiting(new Rect(rect.x, y + 8f, rect.width, 32f), "Asking the server for its settings…");
                return;
            }

            const float sideW = 190f;
            float bodyH = rect.yMax - y - 40f;
            DrawAreas(new Rect(rect.x, y, sideW, bodyH), areas);
            DrawFields(new Rect(rect.x + sideW + 8f, y, rect.width - sideW - 8f, bodyH));
            DrawFooter(new Rect(rect.x, rect.yMax - 34f, rect.width, 30f));
        }

        private void DrawAreas(Rect rect, List<string> areas)
        {
            Widgets.DrawMenuSection(rect);
            const float rowH = 28f;
            Rect inner = rect.ContractedBy(4f);
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, inner.width - DialogLayout.ScrollbarReserveWidth),
                                 Mathf.Max(inner.height, areas.Count * rowH + 4f));
            Widgets.BeginScrollView(inner, ref _sideScroll, view);
            DialogLayout.VisibleRange(_sideScroll, inner.height, rowH, areas.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                Rect row = new Rect(0f, i * rowH, view.width, rowH - 2f);
                bool selected = string.Equals(areas[i], _area, StringComparison.OrdinalIgnoreCase);
                if (selected) Widgets.DrawHighlightSelected(row);
                int pending = PendingIn(areas[i]);
                string label = pending > 0 ? $"{areas[i]}  ({pending}*)" : areas[i];
                if (Widgets.ButtonInvisible(row)) Select(areas[i]);
                Widgets.Label(new Rect(row.x + 6f, row.y, row.width - 8f, row.height), label);
            }
            Widgets.EndScrollView();
        }

        private int PendingIn(string area)
        {
            if (!string.Equals(area, _area, StringComparison.OrdinalIgnoreCase)) return 0;
            return _edits.Count;
        }

        private void Select(string area)
        {
            if (string.Equals(area, _area, StringComparison.OrdinalIgnoreCase)) return;
            // Unsent edits and any in-flight wait are dropped, or they leak into another file or stick on "Applying…".
            _edits.Clear();
            _inFlight = false;
            _area = area;
            _fieldScroll = Vector2.zero;
            if (ServerConfigCache.Fields(area) == null) ServerConfigCache.RequestArea(area);
        }

        private const float RowH = 30f;

        private void DrawFields(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);

            if (string.IsNullOrEmpty(_area))
            {
                Widgets.Label(inner, "Pick a settings file on the left.");
                return;
            }

            List<ConfigField> fields = ServerConfigCache.Fields(_area);
            if (fields == null) { DialogLayout.DrawWaiting(new Rect(inner.x, inner.y, inner.width, 32f), "Loading…"); return; }
            if (fields.Count == 0) { Widgets.Label(inner, "This file has nothing editable."); return; }

            Rect view = new Rect(0f, 0f, Mathf.Max(1f, inner.width - DialogLayout.ScrollbarReserveWidth),
                                 fields.Count * RowH + 4f);
            Widgets.BeginScrollView(inner, ref _fieldScroll, view);
            DialogLayout.VisibleRange(_fieldScroll, inner.height, RowH, fields.Count, out int first, out int last);
            for (int i = first; i < last; i++)
                DrawField(new Rect(0f, i * RowH, view.width, RowH - 4f), fields[i]);
            Widgets.EndScrollView();
        }

        private void DrawField(Rect row, ConfigField f)
        {
            float labelW = Mathf.Min(320f, row.width * 0.45f);
            Rect labelRect = new Rect(row.x + 4f, row.y, labelW, row.height);
            Rect valueRect = new Rect(labelRect.xMax + 6f, row.y, row.width - labelW - 14f, row.height);

            GUI.color = f.Editable ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            DialogLayout.LabelTrunc(labelRect, f.Path, TextAnchor.MiddleLeft);
            GUI.color = Color.white;

            if (f.Secret)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Widgets.Label(valueRect, "(credential - set this in the file on the server)");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            string current = _edits.TryGetValue(f.Path, out string pending) ? pending : f.Value;

            if (f.ReadOnly)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                DialogLayout.LabelTrunc(valueRect, string.IsNullOrEmpty(current) ? "(set by the server)" : current, TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                return;
            }

            if (f.Type == "bool")
            {
                bool on = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase);
                bool was = on;
                Widgets.Checkbox(new Vector2(valueRect.x, valueRect.y + 2f), ref on);
                if (on != was) Set(f, on ? "true" : "false");
                return;
            }

            string typed = Widgets.TextField(valueRect, current ?? "");
            if (!string.Equals(typed, current, StringComparison.Ordinal)) Set(f, typed);
        }

        // An edit equal to what the server already holds is dropped, so "Apply" stays accurate about what changed.
        private void Set(ConfigField f, string value)
        {
            if (string.Equals(value, f.Value, StringComparison.Ordinal)) _edits.Remove(f.Path);
            else _edits[f.Path] = value;
        }

        private void DrawFooter(Rect rect)
        {
            if (!string.IsNullOrEmpty(ServerConfigCache.LastResult))
            {
                GUI.color = ServerConfigCache.LastResultOk ? new Color(0.6f, 0.9f, 0.6f) : new Color(0.95f, 0.6f, 0.6f);
                DialogLayout.LabelTrunc(new Rect(rect.x, rect.y, rect.width - 400f, rect.height), ServerConfigCache.LastResult, TextAnchor.MiddleLeft);
                GUI.color = Color.white;
            }

            Rect restart = new Rect(rect.xMax - 390f, rect.y, 134f, rect.height - 2f);
            Rect revert  = new Rect(rect.xMax - 250f, rect.y, 110f, rect.height - 2f);
            Rect apply   = new Rect(rect.xMax - 130f, rect.y, 126f, rect.height - 2f);

            // Offered in this window, not only at the console: a restart applies the settings edited here.
            if (Widgets.ButtonText(restart, "Restart server"))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Restart the server now?\n\nEveryone is disconnected and reconnects once it is back. "
                    + "Settings that only take effect on a restart are applied then.",
                    () =>
                    {
                        if (ServerConfigCache.RequestRestart()) return;
                        ServerConfigCache.LastResult = "Not connected to the server - nothing was sent.";
                        ServerConfigCache.LastResultOk = false;
                    },
                    destructive: true));

            if (Widgets.ButtonText(revert, "Revert") && _edits.Count > 0) _edits.Clear();

            // Held until the server confirms, never dropped on send, or a refusal throws away everything the owner typed.
            if (_inFlight && ServerConfigCache.ResultSerial != _sentAtSerial)
            {
                if (ServerConfigCache.LastResultOk) _edits.Clear();
                _inFlight = false;
            }

            string label = _inFlight ? "Applying…" : (_edits.Count == 0 ? "Apply" : $"Apply ({_edits.Count})");
            if (Widgets.ButtonText(apply, label) && !_inFlight && _edits.Count > 0 && !string.IsNullOrEmpty(_area))
            {
                _sentAtSerial = ServerConfigCache.ResultSerial;
                _inFlight = true;
                if (!ServerConfigCache.Apply(_area, new Dictionary<string, string>(_edits)))
                {
                    _inFlight = false;
                    ServerConfigCache.LastResult = "Not connected to the server - nothing was sent.";
                    ServerConfigCache.LastResultOk = false;
                }
            }
        }
    }
}
