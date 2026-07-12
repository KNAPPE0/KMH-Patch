using UnityEngine;
using Verse;
// I will never remove these comments (:
namespace KMHPatch.UI
{
    // Shared layout constants + helpers for the KMH dialog family, so every dialog keeps a pixel-consistent look.
    internal static class DialogLayout
    {
        // -- Title row --
        public const float TitleHeight = 32f;
        public const float AfterTitle  = 36f;

        // -- Section divider --
        public const float DividerHeight = 8f;

        // -- List rows -- (>= one font line so a single-line cell has room)
        public const float RowHeightSingle = 28f;

        // -- Close / footer --
        public const float CloseBtnWidth  = 96f;
        public const float CloseBtnHeight = 32f;
        public const float FooterReserve  = 44f;

        // -- Live indicator (top-right "● live · Ns ago") --
        public const float LiveBadgeWidth  = 220f;
        public const float LiveBadgeY      = 6f;
        public const float LiveBadgeHeight = 18f;
        public static readonly Color LiveBadgeColor = new Color(0.6f, 0.85f, 0.6f);

        // Fallback poll for open dialogs; the server pushes fresh snapshots on every change, so this only catches
        // missed pushes - 12s halves idle packet noise vs the old 8s with no visible staleness.
        public const float AutoRefreshSeconds = 12f;

        // -- Standard padding --
        public const float ListInnerPad         = 4f;
        public const float ScrollbarReserveWidth = 16f;

        // -- Design tokens. Prefer these over ad-hoc literals so every KMH
        //    dialog shares one look (spacing, button size, subtext colour). --
        public const float EdgePad      = 6f;   // content inset from a panel edge
        public const float RowGap       = 6f;   // vertical gap between stacked controls
        public const float ButtonHeight = 30f;  // standard button row height
        public const float ButtonWidth  = 110f; // standard button width

        // Muted grey for secondary text. One value so subtext reads the same everywhere instead of drifting between
        // 0.7 and 0.8 per dialog
        public static readonly Color MutedColor = new Color(0.72f, 0.72f, 0.72f);

        // -- Helpers --

        // Row virtualization: which row indices are actually inside the scroll viewport, so a list draws only the
        // ~visible handful instead of every row (keeps big modded item/quest/auction lists at a flat per-frame cost).
        // Keep the scroll viewRect height at count*rowH so the scrollbar stays correct; loop only [first, last).
        public static void VisibleRange(Vector2 scroll, float viewportHeight, float rowH, int count,
                                        out int first, out int last)
        {
            if (rowH <= 0f || count <= 0) { first = 0; last = 0; return; }
            first = Mathf.Max(0, (int)(scroll.y / rowH) - 1);
            if (first > count) first = count;
            last = Mathf.Min(count, first + (int)(viewportHeight / rowH) + 2);
        }

        // One-call scrolling, virtualized row list for the KMH list dialogs (auctions, wants, marketplace, quests,
        // sites, standings…). Handles the identical boilerplate every one of them used to repeat: inner inset, view
        // height + scrollbar reserve, BeginScrollView, the visible-range window, per-row alternating tint and
        // mouseover highlight, the empty-state label, and EndScrollView. The caller only draws each row's content via
        // drawRow(index, rowRect) and supplies an optional message for the empty case. `scroll` is the caller's
        // persisted scroll position. Draw any panel background (DrawMenuSection) around `box` before calling.
        public static void ScrollList(Rect box, ref Vector2 scroll, int count, float rowH,
                                      System.Action<int, Rect> drawRow, string emptyLabel = null)
        {
            Rect inner = box.ContractedBy(ListInnerPad);
            float viewH = Mathf.Max(inner.height, count * rowH + 6f);
            Rect viewRect = new Rect(0f, 0f, inner.width - ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref scroll, viewRect);
            VisibleRange(scroll, inner.height, rowH, count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                Rect row = new Rect(0f, i * rowH, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);
                try { drawRow?.Invoke(i, row); }
                catch (System.Exception ex) { KMHPatch.Diagnostics.KmhLog.Warn($"ScrollList row {i} draw threw: {ex.Message}"); }
            }
            if (count == 0 && !string.IsNullOrEmpty(emptyLabel))
                LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f), emptyLabel);
            Widgets.EndScrollView();
        }

        // Placeholder for a feature list still waiting on its first server snapshot. A pre-1.1.0 server never sends
        // the v1.1.0 snapshots, so its empty ServerBuild means the snapshot will never arrive - say that plainly
        // instead of spinning on "Loading…" forever. Caller wraps the result in its own colour tags.
        public static string AwaitingSnapshot(string loadingLabel, string featureName)
            => string.IsNullOrEmpty(SubProtocol.KmhDispatcher.ServerBuild)
                ? $"{featureName} need a newer server (KMH {SubProtocol.KmhProtocol.BuildVersion}+)."
                : loadingLabel;

        // Shared "ends in …" countdown label for expiry rows (auctions, want-board, etc.). "—" when there's no expiry.
        public static string TimeLeft(long endsTicks, long now)
        {
            if (endsTicks <= 0) return "—";
            if (now >= endsTicks) return "<color=#ff8080>closing…</color>";
            System.TimeSpan s = System.TimeSpan.FromTicks(endsTicks - now);
            if (s.TotalDays    >= 1) return $"{(int)s.TotalDays}d {s.Hours}h";
            if (s.TotalHours   >= 1) return $"{(int)s.TotalHours}h {s.Minutes}m";
            if (s.TotalMinutes >= 1) return $"{(int)s.TotalMinutes}m";
            return $"{(int)s.TotalSeconds}s";
        }

        public static float DrawTitle(Rect rect, string title)
        {
            Text.Font = GameFont.Medium;
            // Size the title to the Medium font's real line height so its descenders don't clip; content starts
            // just below it
            float h = Mathf.Max(TitleHeight, Text.LineHeight);
            Widgets.Label(new Rect(0f, 2f, rect.width, h), title);
            Text.Font = GameFont.Small;
            return 2f + h + 6f;
        }

        public static void DrawSectionDivider(Rect rect, ref float y)
        {
            Widgets.DrawLineHorizontal(0f, y, rect.width);
            y += DividerHeight;
        }

        public static void DrawLiveBadge(Rect rect, int secondsSinceRefresh)
        {
            Text.Font = GameFont.Tiny;
            Color old = GUI.color;
            GUI.color = LiveBadgeColor;
            string text = secondsSinceRefresh <= 1
                ? "● live · just now"
                : $"● live · {secondsSinceRefresh}s ago";
            Widgets.Label(new Rect(rect.width - LiveBadgeWidth, LiveBadgeY, LiveBadgeWidth, LiveBadgeHeight), text);
            GUI.color = old;
            Text.Font = GameFont.Small;
        }

        public static bool DrawCloseButton(Rect rect)
        {
            return Widgets.ButtonText(
                new Rect(rect.width - CloseBtnWidth - 4f,
                         rect.height - FooterReserve + 4f,
                         CloseBtnWidth,
                         CloseBtnHeight),
                "Close");
        }

        public static void DrawCenteredLabel(Rect r, string text)
            => LabelTrunc(r, text, TextAnchor.MiddleCenter);

        // Secondary / hint text in the shared muted grey. Saves the save-color / set / label / restore dance every
        // caller repeats
        public static void DrawMutedLabel(Rect r, string text)
        {
            Color old = GUI.color;
            GUI.color = MutedColor;
            LabelTrunc(r, text);
            GUI.color = old;
        }

        // Single-line label that fits the rect: text wider than the rect is truncated with an ellipsis (never
        // clipped mid-glyph), full text on hover. Rich-text formatting is kept while it fits.
        public static void LabelTrunc(Rect r, string text, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextAnchor oldAnchor = Text.Anchor;
            try
            {
                // Guarantee a full line of vertical room so descenders (g/y/p/q) and the bottoms of letters never
                // clip, even when a caller passes a row shorter than the font. Grow the rect (centered) and
                // vertically-center the text - the fix for "bottoms cut off"
                float lineH = Text.LineHeight;
                if (r.height < lineH)
                    r = new Rect(r.x, r.y - (lineH - r.height) / 2f, r.width, lineH);
                Text.Anchor = ToMiddle(anchor);

                if (Text.CalcSize(text).x <= r.width)
                {
                    Widgets.Label(r, text);
                }
                else
                {
                    string plain = StripTags(text);
                    Widgets.Label(r, plain.Truncate(r.width));
                    if (Mouse.IsOver(r)) TooltipHandler.TipRegion(r, plain);
                }
            }
            catch
            {
                // One label must never blank a whole dialog - fall back to plain.
                try { Widgets.Label(r, text); } catch { /* skip just this label */ }
            }
            finally { Text.Anchor = oldAnchor; }
        }

        // Universal filter/search box. Field is grown to a full line and the placeholder vertically centered so
        // neither the text nor the greyed placeholder clips (the old ContractedBy(6,4) clipped glyph bottoms).
        public static string SearchField(Rect r, string current, string placeholder = "Filter…")
        {
            float minH = Text.LineHeight + 6f;
            if (r.height < minH) r = new Rect(r.x, r.y, r.width, minH);

            string result = Widgets.TextField(r, current ?? string.Empty);

            if (string.IsNullOrEmpty(current))
            {
                TextAnchor oldA = Text.Anchor;
                Color      oldC = GUI.color;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color   = new Color(1f, 1f, 1f, 0.35f);
                // Inset only horizontally (clear of the caret); keep full height so the centered placeholder keeps
                // its descender room
                Widgets.Label(new Rect(r.x + 7f, r.y, r.width - 12f, r.height), placeholder);
                GUI.color   = oldC;
                Text.Anchor = oldA;
            }
            return result;
        }

        // Keep the horizontal alignment but force vertical centering, so a single line sits in the middle of its
        // row with descender room
        private static TextAnchor ToMiddle(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft:   case TextAnchor.LowerLeft:   return TextAnchor.MiddleLeft;
                case TextAnchor.UpperCenter: case TextAnchor.LowerCenter: return TextAnchor.MiddleCenter;
                case TextAnchor.UpperRight:  case TextAnchor.LowerRight:  return TextAnchor.MiddleRight;
                default: return a;
            }
        }

        // Drop rich-text tags (<b>, <color=..>, <i>) so width measurement and truncation operate on the visible
        // characters only
        public static string StripTags(string s)
            => string.IsNullOrEmpty(s) ? s : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty);

        // Compact checkbox + label with the box right next to the text (vanilla CheckboxLabeled wastes 80-100 px
        // on a flexible gap, awkward in toolbar rows). Returns the next x so calls can chain along a row.
        public static float DrawTightCheckbox(float x, float y, string label, ref bool value)
        {
            const float boxSize = 20f;
            const float padding = 4f;
            const float trail   = 14f;
            float labelW = Text.CalcSize(label).x;

            Widgets.Checkbox(x, y - 2f, ref value, boxSize);
            Widgets.Label(new Rect(x + boxSize + padding, y, labelW + 4f, 22f), label);
            return x + boxSize + padding + labelW + trail;
        }

        // Pop a float menu listing every value of enum T (labeled by `label`), invoking `onPick` with the chosen one.
        // Replaces the identical "new List<FloatMenuOption> → foreach Enum.GetValues → capture → add → new FloatMenu"
        // boilerplate every enum dropdown (sort pickers, etc.) used to repeat.
        public static void EnumFloatMenu<T>(System.Func<T, string> label, System.Action<T> onPick) where T : System.Enum
        {
            System.Collections.Generic.List<FloatMenuOption> opts = new System.Collections.Generic.List<FloatMenuOption>();
            foreach (T v in (T[])System.Enum.GetValues(typeof(T)))
            {
                T captured = v;
                opts.Add(new FloatMenuOption(label(captured), () => onPick(captured)));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Friendly version of an enum name - drops the CamelCase and adds spaces so "EconomyScore" -> "Economy
        // Score" for FloatMenu labels
        public static string FriendlyEnumName(System.Enum value)
        {
            if (value == null) return "";
            string name = value.ToString();
            System.Text.StringBuilder sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
