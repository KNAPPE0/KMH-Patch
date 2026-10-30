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

                if (KmhBrand.Tab(new Rect(x, y, w, TabHeight), label, i == selected)) onSelect?.Invoke(i);
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
            last = Mathf.Min(count, (int)((scroll.y + viewportHeight) / rowH) + 2);
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
                ? KmhText.Get("KMH_Dialog_NeedsNewerServer", "{0} need a newer server (Colony Nexus {1}+).",
                              featureName, SubProtocol.KmhProtocol.BuildVersion)
                : loadingLabel;

        // Shared "ends in …" countdown label for expiry rows (auctions, want-board, etc.). "—" when there's no expiry.
        public static string TimeLeft(long endsTicks, long now)
        {
            if (endsTicks <= 0) return "—";
            if (now >= endsTicks) return "<color=#f07167>" + KmhText.Get("KMH_Dialog_Closing", "closing…") + "</color>";
            System.TimeSpan s = System.TimeSpan.FromTicks(endsTicks - now);
            if (s.TotalDays    >= 1) return $"{(int)s.TotalDays}d {s.Hours}h";
            if (s.TotalHours   >= 1) return $"{(int)s.TotalHours}h {s.Minutes}m";
            if (s.TotalMinutes >= 1) return $"{(int)s.TotalMinutes}m";
            return $"{(int)s.TotalSeconds}s";
        }

        // Kept separate from TimeLeft: "never"/"expired" and "—"/"closing…" are both load-bearing wordings.
        public static string TimeRemainingShort(long expiresUtcTicks, long nowTicks)
        {
            if (expiresUtcTicks <= 0) return KmhText.Get("KMH_Dialog_Never", "never");
            if (nowTicks >= expiresUtcTicks) return "<color=#f07167>" + KmhText.Get("KMH_Dialog_Expired", "expired") + "</color>";
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

        // reserveRight keeps the title clear of whatever the window draws in the top-right corner (live badge, buttons).
        public static float DrawTitle(Rect rect, string title, float reserveRight = 0f)
        {
            Text.Font = GameFont.Medium;
            // Sized to the Medium font's real line height, or the title's descenders clip.
            float h = Mathf.Max(TitleHeight, Text.LineHeight);
            // Every Colony Nexus window carries the mark beside its title, one shared header instead of many.
            float m = Mathf.Round(h * 0.82f);
            KmhBrand.DrawMark(new Rect(0f, 2f + (h - m) / 2f, m, m));
            // One line, cut with the whole title on hover: a wrapped title lost its second line under the header.
            LabelTrunc(new Rect(m + 8f, 2f, Mathf.Max(0f, rect.width - m - 8f - Mathf.Max(0f, reserveRight)), h),
                       title, TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            return 2f + h + 6f;
        }

        // What DrawLiveBadge's longest wording takes, for a title on the same row to stop short of it.
        public static float LiveBadgeReserve
        {
            get
            {
                // Keyed on the wording, so switching language re-measures instead of keeping the old width.
                string stale = KmhText.Get("KMH_Dialog_LiveStale", "◐ stale · {0}s ago");
                if (_liveBadgeReserve > 0f && stale == _liveBadgeReserveFor) return _liveBadgeReserve;
                GameFont prev = Text.Font;
                Text.Font = GameFont.Tiny;
                _liveBadgeReserve = Mathf.Ceil(Text.CalcSize(KmhText.Get("KMH_Dialog_LiveStale", "◐ stale · {0}s ago", 9999)).x) + 16f;
                _liveBadgeReserveFor = stale;
                Text.Font = prev;
                return _liveBadgeReserve;
            }
        }

        private static float _liveBadgeReserve = -1f;
        private static string _liveBadgeReserveFor;

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
            string text = !received ? KmhText.Get("KMH_Dialog_LiveWaiting", "○ waiting…")
                        : stale     ? KmhText.Get("KMH_Dialog_LiveStale", "◐ stale · {0}s ago", secondsSinceRefresh)
                        : secondsSinceRefresh <= 1 ? KmhText.Get("KMH_Dialog_LiveJustNow", "● live · just now")
                                                   : KmhText.Get("KMH_Dialog_LiveAgo", "● live · {0}s ago", secondsSinceRefresh);
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
                KmhText.Get("KMH_Common_Close", "Close"));
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

        // The one inline "waiting for an answer" look: muted text over a short travelling brand bar.
        public static void DrawWaiting(Rect r, string text)
        {
            float rowH = Mathf.Min(TextRowH, r.height);
            DrawMutedLabel(new Rect(r.x, r.y, r.width, rowH), text);
            KmhLoadingPanel.DrawBar(new Rect(r.x, r.y + rowH + 4f, Mathf.Min(160f, r.width), 3f), -1f, KmhLoadingPanel.Motion.Now());
        }

        // Known progress in the brand bar, in place of vanilla's FillableBar.
        public static void DrawProgress(Rect r, float fraction)
            => KmhLoadingPanel.DrawBar(r, Mathf.Clamp01(fraction), KmhLoadingPanel.Motion.Now());

        // Ellipsis rather than a mid-glyph clip, full text on hover; with no anchor given it keeps the caller's Text.Anchor.
        public static void LabelTrunc(Rect r, string text, TextAnchor? anchor = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            // A subtracted column width can go non-positive, and drawing into it throws or spills across neighbours.
            if (r.width <= 1f) return;
            text = OneLine(text);
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            try
            {
                // Grows DOWNWARD only: growing centred starts the text above its rect and draws over the row before it.
                float lineH = Text.LineHeight;
                if (r.height < lineH) r = new Rect(r.x, r.y, r.width, lineH);
                Text.Anchor = ResolveAnchor(anchor, oldAnchor);
                // Never wrapped: 1.7 draws bold wider than it measures, and a wrapped name fell below its row unseen.
                Text.WordWrap = false;

                if (Text.CalcSize(text).x <= r.width)
                {
                    Widgets.Label(r, text);
                }
                else
                {
                    string plain = StripTags(text);
                    Widgets.Label(r, TruncateRich(text, r.width - TruncSlack));
                    if (Mouse.IsOver(r)) TooltipHandler.TipRegion(r, plain);
                }
            }
            catch
            {
                // One label must never blank a whole dialog - fall back to plain.
                try { Widgets.Label(r, text); } catch { /* skip just this label */ }
            }
            finally { Text.Anchor = oldAnchor; Text.WordWrap = oldWrap; }
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

        internal static TextAnchor ResolveAnchor(TextAnchor? asked, TextAnchor current) => ToMiddle(asked ?? current);

        // A one-line row given a line break draws its second line over the row below.
        public static string OneLine(string s)
            => string.IsNullOrEmpty(s) || s.IndexOfAny(LineBreaks) < 0
                ? s
                : s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

        private static readonly char[] LineBreaks = { '\n', '\r' };

        // Wraps where the rect holds the wrapped text; otherwise one truncated line with the whole text on hover.
        public static void LabelFit(Rect r, string text)
        {
            if (string.IsNullOrEmpty(text) || r.width <= 1f) return;
            if (Text.CalcHeight(text, r.width) <= r.height + 0.5f) Widgets.Label(r, text);
            else LabelTrunc(r, text);
        }

        // Wraps rather than truncates: half an explanation explains nothing. Returns the height used.
        public static float LabelWrapped(float x, float y, float width, string text)
        {
            if (string.IsNullOrEmpty(text) || width <= 1f) return 0f;
            float h = Text.CalcHeight(text, width);
            Widgets.Label(new Rect(x, y, width, h), text);
            return h;
        }

        // From the measured label, so a longer translation widens the button instead of spilling out of it.
        public static float ButtonW(string label, float min = 60f, float max = 10000f)
            => Mathf.Clamp(IconButton.WidthFor(label, false), min, Mathf.Max(min, max));

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

        // Cuts the visible text, not the markup: stripping the tags first made every long status line lose its colour.
        // Room for bold text, which measures narrower than it draws.
        internal const float TruncSlack = 4f;

        internal static string TruncateRich(string text, float width, System.Func<string, float> measure = null)
        {
            text = OneLine(text ?? "");
            measure ??= t => Text.CalcSize(t).x;
            int lo = 0, hi = StripTags(text).Length;
            string best = "...";
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                string cand = PrefixRich(text, mid);
                if (measure(cand) <= width) { best = cand; lo = mid + 1; } else hi = mid - 1;
            }
            return best;
        }

        // The first `visible` characters with every tag kept, then "..." and the tags still open, closed in order.
        internal static string PrefixRich(string text, int visible)
        {
            var sb = new System.Text.StringBuilder();
            var open = new System.Collections.Generic.Stack<string>();
            int seen = 0, i = 0;
            while (i < text.Length && seen < visible)
            {
                int end = text[i] == '<' ? text.IndexOf('>', i) : -1;
                if (end > i)
                {
                    string tag = text.Substring(i, end - i + 1);
                    if (tag.StartsWith("</")) { if (open.Count > 0) open.Pop(); }
                    else open.Push(tag.Substring(1).Split('=', '>', ' ')[0]);
                    sb.Append(tag);
                    i = end + 1;
                    continue;
                }
                sb.Append(text[i]); seen++; i++;
            }
            sb.Append("...");
            while (open.Count > 0) sb.Append("</").Append(open.Pop()).Append('>');
            return sb.ToString();
        }

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
