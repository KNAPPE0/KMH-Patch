using System;
using System.Collections.Generic;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Standings
{
    // One column of a ranked standings table. Frac is the column's left edge as a fraction of table width.
    // cols[0] is always the name column (left-aligned, carries the rank + ★ self-marker); the rest are centered.
    internal sealed class Col<T>
    {
        public readonly string Header;
        public readonly float  Frac;
        public readonly Func<T, string> Cell;
        public Col(string header, float frac, Func<T, string> cell) { Header = header; Frac = frac; Cell = cell; }
    }

    // Shared renderers so every board (player/guild/records/…) looks and virtualizes the same way.
    internal static class StandingsTable
    {
        public const string Dash = "<color=grey>—</color>";

        // A row of selectable category tabs. Returns true (and updates active) when the selection changes.
        public static bool Tabs(Rect row, IReadOnlyList<string> labels, ref int active)
        {
            bool changed = false;
            float x = row.x;
            for (int i = 0; i < labels.Count; i++)
            {
                float w = Mathf.Max(64f, Text.CalcSize(labels[i]).x + 20f);
                Color old = GUI.color;
                if (i == active) GUI.color = new Color(0.45f, 0.75f, 1f);
                if (Widgets.ButtonText(new Rect(x, row.y, w, 26f), labels[i])) { if (active != i) { active = i; changed = true; } }
                GUI.color = old;
                x += w + 4f;
            }
            return changed;
        }

        public static void Header<T>(Rect r, Col<T>[] cols, int accentCol, bool hasInfo)
        {
            // Keep headers on the same inner rect as rows, or columns drift into the nav.
            float pad     = DialogLayout.ListInnerPad;
            float originX = r.x + pad;
            float viewW   = r.width - pad * 2f - DialogLayout.ScrollbarReserveWidth;
            float[] e = Edges(cols, viewW, hasInfo);
            DialogLayout.LabelTrunc(new Rect(originX + e[0], r.y, e[1] - e[0], r.height), $"<b>{cols[0].Header}</b>");
            for (int i = 1; i < cols.Length; i++)
            {
                string h = i == accentCol ? $"<color=#74bdff><b>{cols[i].Header}</b></color>" : $"<b>{cols[i].Header}</b>";
                DialogLayout.DrawCenteredLabel(new Rect(originX + e[i], r.y, e[i + 1] - e[i], r.height), h);
            }
        }

        // Ranked, virtualized table. isMe highlights the caller's row; onInfo (when set) adds a right-edge Info button.
        public static void Draw<T>(Rect box, List<T> rows, Col<T>[] cols, ref Vector2 scroll,
                                   Func<T, bool> isMe, Action<T> onInfo)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = DialogLayout.RowHeightSingle;
            bool hasInfo = onInfo != null;

            float viewH    = Mathf.Max(inner.height, rows.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);
            float[] e      = Edges(cols, viewRect.width, hasInfo);
            float infoX    = viewRect.width - (hasInfo ? InfoW : 0f);

            Widgets.BeginScrollView(inner, ref scroll, viewRect);
            DialogLayout.VisibleRange(scroll, inner.height, rowH, rows.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                T row = rows[i];
                float ly = i * rowH;
                Rect r = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(r);
                Widgets.DrawHighlightIfMouseover(r);

                string rank = i < 3 ? $"<color=yellow>#{i + 1}</color>" : $"<color=grey>#{i + 1}</color>";
                string star = (isMe != null && isMe(row)) ? "<color=#80ff80>★</color> " : "";
                DialogLayout.LabelTrunc(new Rect(e[0], ly + 2f, e[1] - e[0] - 4f, rowH - 4f), $"{rank}  {star}<b>{cols[0].Cell(row)}</b>");
                for (int c = 1; c < cols.Length; c++)
                    DialogLayout.DrawCenteredLabel(new Rect(e[c], ly + 2f, e[c + 1] - e[c], rowH - 4f), cols[c].Cell(row));

                if (hasInfo && Widgets.ButtonText(new Rect(infoX + 4f, ly + 3f, InfoW - 8f, rowH - 6f), "Info"))
                    onInfo(row);
            }
            if (rows.Count == 0)
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f), "<color=grey>No entries yet.</color>");
            Widgets.EndScrollView();
        }

        private const float InfoW = 64f;

        private static float[] Edges<T>(Col<T>[] cols, float width, bool hasInfo)
        {
            // Scale column fractions by the table area, excluding the Info button, so last columns don't clip.
            float right = width - (hasInfo ? InfoW : 0f);
            float[] e = new float[cols.Length + 1];
            for (int i = 0; i < cols.Length; i++) e[i] = cols[i].Frac * right;
            e[cols.Length] = right;
            // guard monotonicity so a too-wide last data column never overruns the Info button
            for (int i = cols.Length - 1; i >= 0; i--) if (e[i] > e[i + 1]) e[i] = e[i + 1];
            if (e[0] < 10f) e[0] = 10f;
            return e;
        }
    }
}
