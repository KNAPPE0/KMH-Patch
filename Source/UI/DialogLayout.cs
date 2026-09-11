using UnityEngine;
using Verse;
// I will never remove these comments (:
namespace KMHPatch.UI
{
    internal static class DialogLayout
    {
        public const float TitleHeight = 32f;
        public const float AfterTitle  = 36f;

        public const float DividerHeight = 8f;

        // At least one font line, or a single-line cell has no room to draw.
        public const float RowHeightSingle = 28f;

        public const float CloseBtnWidth  = 96f;
        public const float CloseBtnHeight = 32f;
        public const float FooterReserve  = 44f;

        // A small enough resize drag makes the raw subtraction negative, which throws inside BeginScrollView.
        public static float BodyHeight(Rect rect, float y) => Mathf.Max(MinBodyHeight, rect.height - y - FooterReserve);

        public const float MinBodyHeight = 40f;

        // RimWorld centres a window at InitialSize without clamping, and resizing cannot recover off-screen content.
        public static Vector2 FitToScreen(float width, float height)
            => FitToScreen(width, height, Verse.UI.screenWidth, Verse.UI.screenHeight);

        // Pure overload so the clamp can be proven without a running game.
        public static Vector2 FitToScreen(float width, float height, float screenW, float screenH)
            => new Vector2(Mathf.Clamp(width,  MinDialogWidth,  Mathf.Max(MinDialogWidth,  screenW - ScreenMargin)),
                           Mathf.Clamp(height, MinDialogHeight, Mathf.Max(MinDialogHeight, screenH - ScreenMargin)));

        public const float ScreenMargin    = 40f;
        public const float MinDialogWidth  = 320f;
        public const float MinDialogHeight = 240f;

        // LabelTrunc grows any shorter rect to a full line, so a pitch under the font height overlaps every row.
        public static float TextRowH => Mathf.Ceil(Text.LineHeight);

        // Pitch for a row of `lines` stacked text lines plus optional padding, measured rather than guessed.
        public static float TextRowsH(int lines, float pad = 0f) => Mathf.Max(1, lines) * TextRowH + pad;

        public const float TabHeight = 26f;
        public const float TabGap    = 3f;

        // Wraps: laid out left-to-right with no width check, the last tabs draw outside the dialog and cannot be clicked.
        public static float TabRow(float y, float width, System.Collections.Generic.IList<string> labels,
                                   int selected, System.Action<int> onSelect)
        {
            if (labels == null || labels.Count == 0) return y;
            float x = 0f;
            for (int i = 0; i < labels.Count; i++)
            {
                string label = labels[i] ?? "";
                float w = Mathf.Min(Mathf.Max(66f, Text.CalcSize(label).x + 18f), Mathf.Max(1f, width));
                if (x > 0f && x + w > width) { x = 0f; y += TabHeight + TabGap; }

                Color old = GUI.color;
                if (i == selected) GUI.color = new Color(0.45f, 0.75f, 1f);
                if (Widgets.ButtonText(new Rect(x, y, w, TabHeight), label)) onSelect?.Invoke(i);
                GUI.color = old;

                x += w + TabGap;
            }
            return y + TabHeight + 6f;
        }

        public const float LiveBadgeWidth  = 220f;
        public const float LiveBadgeY      = 6f;
        public const float LiveBadgeHeight = 18f;
        public static readonly Color LiveBadgeColor = new Color(0.6f, 0.85f, 0.6f);

        // Only a fallback: the server pushes on every change, so this exists to catch a missed push.
        public const float AutoRefreshSeconds = 12f;

        public const float ListInnerPad         = 4f;
        public const float ScrollbarReserveWidth = 16f;

        // Prefer these over ad-hoc literals, or dialogs drift apart on spacing and button size.
        public const float EdgePad      = 6f;   // content inset from a panel edge
        public const float RowGap       = 6f;   // vertical gap between stacked controls
        public const float ButtonHeight = 30f;  // standard button row height
        public const float ButtonWidth  = 110f; // standard button width

        // One value, or subtext drifts between 0.7 and 0.8 per dialog.
        public static readonly Color MutedColor = new Color(0.72f, 0.72f, 0.72f);


        // Keep the viewRect height at count*rowH so the scrollbar stays correct; loop only [first, last).
        public static void VisibleRange(Vector2 scroll, float viewportHeight, float rowH, int count,
                                        out int first, out int last)
        {
            if (rowH <= 0f || count <= 0) { first = 0; last = 0; return; }
            first = Mathf.Max(0, (int)(scroll.y / rowH) - 1);
            if (first > count) first = count;
            last = Mathf.Min(count, first + (int)(viewportHeight / rowH) + 2);
        }

        // Draw any panel background around `box` before calling; this owns everything inside it.
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

        // An empty ServerBuild means the snapshot will never arrive, so say so instead of spinning on "Loading…".
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

        // Kept separate from TimeLeft: "never"/"expired" and "—"/"closing…" are both load-bearing wordings.
        public static string TimeRemainingShort(long expiresUtcTicks, long nowTicks)
        {
            if (expiresUtcTicks <= 0) return "never";
            if (nowTicks >= expiresUtcTicks) return "<color=#ff8080>expired</color>";
            try
            {
                System.TimeSpan span = System.TimeSpan.FromTicks(expiresUtcTicks - nowTicks);
                if (span.TotalDays    >= 1) return $"{(int)span.TotalDays}d";
                if (span.TotalHours   >= 1) return $"{(int)span.TotalHours}h";
                if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
                return $"{(int)span.TotalSeconds}s";
            }
            catch { return "?"; }
        }

        public static float DrawTitle(Rect rect, string title)
        {
            Text.Font = GameFont.Medium;
            // Sized to the Medium font's real line height, or the title's descenders clip.
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

        // Well past the auto-refresh interval, so an ordinary gap between snapshots never reads as a fault.
        private const int StaleAfterSeconds = 30;

        // received=false means nothing has been applied yet, so the badge must not claim to be live.
        public static void DrawLiveBadge(Rect rect, int secondsSinceRefresh, float reserveRight = 0f)
            => DrawLiveBadge(rect, secondsSinceRefresh, true, reserveRight);

        public static void DrawLiveBadge(Rect rect, int secondsSinceRefresh, bool received, float reserveRight)
        {
            Text.Font = GameFont.Tiny;
            TextAnchor oldAnchor = Text.Anchor;
            Color old = GUI.color;
            bool stale = received && secondsSinceRefresh >= StaleAfterSeconds;
            GUI.color = !received ? new Color(0.62f, 0.62f, 0.62f)
                      : stale     ? new Color(1f, 0.81f, 0.35f)
                                  : LiveBadgeColor;
            Text.Anchor = TextAnchor.UpperRight;
            // The word carries the state, not the colour.
            string text = !received ? "○ waiting…"
                        : stale     ? $"◐ stale · {secondsSinceRefresh}s ago"
                        : secondsSinceRefresh <= 1 ? "● live · just now"
                                                   : $"● live · {secondsSinceRefresh}s ago";
            float right = rect.width - Mathf.Max(0f, reserveRight);
            float w     = Mathf.Min(LiveBadgeWidth, Mathf.Max(0f, right));
            if (w > 0f) Widgets.Label(new Rect(right - w, LiveBadgeY, w, LiveBadgeHeight), text);
            Text.Anchor = oldAnchor;
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

        public static void DrawMutedLabel(Rect r, string text)
        {
            Color old = GUI.color;
            GUI.color = MutedColor;
            LabelTrunc(r, text);
            GUI.color = old;
        }

        // Truncated with an ellipsis rather than clipped mid-glyph, with the full text on hover.
        public static void LabelTrunc(Rect r, string text, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            if (string.IsNullOrEmpty(text)) return;
            // A subtracted column width can go non-positive, and drawing into it throws or spills across neighbours.
            if (r.width <= 1f) return;
            TextAnchor oldAnchor = Text.Anchor;
            try
            {
                // Grows DOWNWARD only: growing centred starts the text above its rect and draws over the row before it.
                float lineH = Text.LineHeight;
                if (r.height < lineH) r = new Rect(r.x, r.y, r.width, lineH);
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

        public static string SearchField(Rect r, string current, string placeholder = "Filter")
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
                // Horizontal inset only: losing height costs the centred placeholder its descender room.
                Widgets.Label(new Rect(r.x + 7f, r.y, r.width - 12f, r.height), placeholder);
                GUI.color   = oldC;
                Text.Anchor = oldA;
            }
            return result;
        }

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

        // Width measurement and truncation must see only the visible characters, not the markup.
        public static string StripTags(string s)
            => string.IsNullOrEmpty(s) ? s : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty);

        // Shared, or a checkbox row sits 2px higher in one dialog than the next.
        public const float ToolbarRowH = 32f;

        // Vanilla CheckboxLabeled wastes 80-100px on a flexible gap, which does not fit a toolbar row.
        public static float TightCheckboxWidth(string label) => 20f + 4f + Text.CalcSize(label ?? "").x + 14f;

        // Not Widgets.Checkbox: its unchecked texture is a cross, which on a filter row reads as "blocked", not "off".
        public static float DrawTightCheckbox(float x, float y, string label, ref bool value)
        {
            const float boxSize = 20f;
            const float padding = 4f;
            const float trail   = 14f;
            label ??= "";
            float labelW = Text.CalcSize(label).x;

            Rect box = new Rect(x, y - 2f, boxSize, boxSize);
            Rect hit = new Rect(x, y - 2f, boxSize + padding + labelW + 4f, boxSize + 2f);

            if (Mouse.IsOver(hit)) Widgets.DrawHighlight(hit);

            Color prevCol = GUI.color;
            Widgets.DrawBoxSolid(box, value ? CheckboxOnFill : CheckboxOffFill);
            GUI.color = CheckboxOutline;
            Widgets.DrawBox(box);
            GUI.color = prevCol;

            if (value)
            {
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(box, "✓");
                Text.Anchor = prevAnchor;
            }

            Widgets.Label(new Rect(x + boxSize + padding, y, labelW + 4f, 22f), label);

            if (Widgets.ButtonInvisible(hit))
            {
                value = !value;
                Verse.Sound.SoundStarter.PlayOneShotOnCamera(
                    value ? RimWorld.SoundDefOf.Checkbox_TurnedOn : RimWorld.SoundDefOf.Checkbox_TurnedOff);
            }
            return x + boxSize + padding + labelW + trail;
        }

        private static readonly Color CheckboxOnFill  = new Color(0.35f, 0.52f, 0.38f);
        private static readonly Color CheckboxOffFill = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color CheckboxOutline = new Color(0.55f, 0.55f, 0.55f);

        // A "Category: " prefix spends width on a word the player knows and truncates the part that varies.
        public static string DropdownLabel(string value) => (value ?? "") + " ▼";

        // Measured against every value the menu can show, or the first one fits and the rest ellipse.
        public static float DropdownWidth(System.Collections.Generic.IEnumerable<string> values,
                                          float min = 110f, float max = 220f)
        {
            float w = 0f;
            if (values != null)
                foreach (string v in values)
                {
                    try { w = Mathf.Max(w, Text.CalcSize(DropdownLabel(v)).x); } catch { }
                }
            return Mathf.Clamp(w + 26f, min, Mathf.Max(min, max));
        }

        // Label drawn separately so an over-long value truncates with a tooltip instead of being clipped mid-word.
        public static bool DrawDropdownButton(Rect r, string value)
        {
            bool clicked = Widgets.ButtonText(r, string.Empty);
            LabelTrunc(new Rect(r.x + 8f, r.y, Mathf.Max(1f, r.width - 16f), r.height),
                       DropdownLabel(value), TextAnchor.MiddleLeft);
            return clicked;
        }

        // Not a checkbox: three boxes with one ticked reads as three independent options, not one choice.
        public static bool DrawSegment(Rect r, string label, bool selected, bool enabled = true)
        {
            Color prev = GUI.color;
            Widgets.DrawBoxSolid(r, selected ? CheckboxOnFill : CheckboxOffFill);
            GUI.color = selected ? SegmentSelectedOutline : CheckboxOutline;
            Widgets.DrawBox(r);
            GUI.color = prev;

            if (enabled && Mouse.IsOver(r)) Widgets.DrawHighlight(r);

            Color textPrev = GUI.color;
            if (!enabled) GUI.color = MutedColor;
            LabelTrunc(new Rect(r.x + 6f, r.y, Mathf.Max(1f, r.width - 12f), r.height), label, TextAnchor.MiddleCenter);
            GUI.color = textPrev;

            return enabled && Widgets.ButtonInvisible(r);
        }

        private static readonly Color SegmentSelectedOutline = new Color(0.72f, 0.86f, 0.74f);

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

        // "EconomyScore" -> "Economy Score" for menu labels.
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
