using System;
using System.Collections.Generic;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Standings
{
    // Frac is the left edge as a fraction of table width; cols[0] is always the left-aligned name column.
    internal sealed class Col<T>
    {
        public readonly string Header;
        public readonly float  Frac;
        public readonly Func<T, string> Cell;
        public Col(string header, float frac, Func<T, string> cell) { Header = header; Frac = frac; Cell = cell; }
    }

    internal static class StandingsTable
    {
        public const string Dash = "<color=#A9AFB9>—</color>";

        private const float TabH = 26f, TabGap = 4f;

        private static float TabW(string label, float max)
            => Mathf.Min(Mathf.Max(64f, Text.CalcSize(label ?? "").x + 20f), Mathf.Max(1f, max));

        // Place the header below this, never at a fixed offset - a narrow window wraps some boards onto a second line.
        public static float TabsH(IReadOnlyList<string> labels, float width)
        {
            if (labels == null || labels.Count == 0) return TabH;
            float[] widths = new float[labels.Count];
            for (int i = 0; i < labels.Count; i++) widths[i] = TabW(labels[i], width);
            return TabsHFrom(widths, width);
        }

        // Kept pure and split from Text.CalcSize, which needs a running game, so the offline suite can cover the wrap.
        public static float TabsHFrom(IReadOnlyList<float> widths, float containerWidth)
        {
            if (widths == null || widths.Count == 0) return TabH;
            int lines = 1;
            float x = 0f;
            for (int i = 0; i < widths.Count; i++)
            {
                float w = widths[i];
                if (x > 0f && x + w > containerWidth) { x = 0f; lines++; }
                x += w + TabGap;
            }
            return lines * TabH + (lines - 1) * TabGap;
        }

        public static bool Tabs(Rect row, IReadOnlyList<string> labels, ref int active)
        {
            bool changed = false;
            float x = 0f, y = row.y;
            for (int i = 0; i < labels.Count; i++)
            {
                float w = TabW(labels[i], row.width);
                if (x > 0f && x + w > row.width) { x = 0f; y += TabH + TabGap; }

                if (KmhBrand.Tab(new Rect(row.x + x, y, w, TabH), labels[i], i == active) && active != i) { active = i; changed = true; }
                x += w + TabGap;
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

        // Mirrors the server: ties share a rank (1, 2, 2, 4) and <= 0 never ranks; lower-is-better boards pass -value.
        public static int[] Ranks<T>(IReadOnlyList<T> rows, Func<T, long> value)
        {
            int[] ranks = new int[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                long v = value(rows[i]);
                if (v <= 0) continue;
                ranks[i] = i > 0 && ranks[i - 1] > 0 && value(rows[i - 1]) == v ? ranks[i - 1] : i + 1;
            }
            return ranks;
        }

        // ranks replaces the row number, so ties share a place and an empty row ranks nobody.
        public static void Draw<T>(Rect box, List<T> rows, Col<T>[] cols, ref Vector2 scroll,
                                   Func<T, bool> isMe, Action<T> onInfo, int[] ranks = null, Func<T, bool> isSelected = null, string empty = null)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = DialogLayout.RowHeightSingle;
            bool hasInfo = onInfo != null;

            float viewH    = Mathf.Max(inner.height, rows.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);
            float[] e      = Edges(cols, viewRect.width, hasInfo);
            float infoX    = viewRect.width - (hasInfo ? InfoW : 0f);
            string info    = KmhText.Get("KMH_St_Info", "Info");

            Widgets.BeginScrollView(inner, ref scroll, viewRect);
            DialogLayout.VisibleRange(scroll, inner.height, rowH, rows.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                T row = rows[i];
                float ly = i * rowH;
                Rect r = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(r);
                if (isSelected != null && isSelected(row))
                {
                    Widgets.DrawBoxSolid(r, KmhBrand.WithAlpha(KmhBrand.Accent, 0.12f));
                    Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), KmhBrand.Accent);
                }
                Widgets.DrawHighlightIfMouseover(r);

                int place = ranks == null ? i + 1 : i < ranks.Length ? ranks[i] : 0;
                string rank = place <= 0 ? Dash : place <= 3 ? $"<color=yellow>#{place}</color>" : $"<color=#A9AFB9>#{place}</color>";
                string star = (isMe != null && isMe(row)) ? "<color=#4cc38a>★</color> " : "";
                DialogLayout.LabelTrunc(new Rect(e[0], ly + 2f, e[1] - e[0] - 4f, rowH - 4f), $"{rank}  {star}<b>{cols[0].Cell(row)}</b>");
                for (int c = 1; c < cols.Length; c++)
                    DialogLayout.DrawCenteredLabel(new Rect(e[c], ly + 2f, e[c + 1] - e[c], rowH - 4f), cols[c].Cell(row));

                if (hasInfo && (KmhBrand.Button(new Rect(infoX + 4f, ly + 3f, InfoW - 8f, rowH - 6f), info, false)
                                || Widgets.ButtonInvisible(new Rect(0f, ly, infoX, rowH))))
                    onInfo(row);
            }
            if (rows.Count == 0)
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f), $"<color=#A9AFB9>{empty ?? KmhText.Get("KMH_St_NoEntries", "No entries yet.")}</color>");
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
            // Kept monotonic so a too-wide last data column never overruns the Info button.
            for (int i = cols.Length - 1; i >= 0; i--) if (e[i] > e[i + 1]) e[i] = e[i + 1];
            if (e[0] < 10f) e[0] = 10f;
            return e;
        }
    }
}
