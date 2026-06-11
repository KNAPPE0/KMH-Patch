using UnityEngine;
using Verse;

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

        // 8s auto-refresh cadence - the server is the source of truth, but a polling failover catches missed
        // broadcasts.
        public const float AutoRefreshSeconds = 8f;

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

        // Single-line label that fits the rect: if the text is wider than the rect it's truncated with an ellipsis
        // instead of clipping mid-glyph. Use this for any fixed-height row cell so changing column widths / padding
        // can never cut text off. The full text shows as a tooltip on
        // hover so nothing is lost. Rich-text formatting is kept while it fits;
        // a truncated cell falls back to plain text.
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

        // Universal filter/search box. One place to guarantee text and the greyed placeholder never clip: the field
        // is grown to a full line of height and the placeholder is vertically centered at full height (the old code
        // shrank it with ContractedBy(6,4), leaving 20 px under a 22 px font, which clipped the bottoms). Returns
        // the edited text
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

        // Compact checkbox + label, ☐ right next to the text (vanilla CheckboxLabeled wastes 80-100 px on a
        // flexible gap between label and box, which looks awkward in horizontal toolbar rows)
        //
        // Returns the next x to continue the toolbar at - chain like:
        //   float cbx = 0f;
        //   cbx = DialogLayout.DrawTightCheckbox(cbx, y, "A", ref a);
        //   cbx = DialogLayout.DrawTightCheckbox(cbx, y, "B", ref b);
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
