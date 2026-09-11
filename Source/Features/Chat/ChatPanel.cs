using System;
using System.Collections.Generic;
using KMHPatch.Features.Chat.Dto;
using KMHPatch.Features.Enforcement;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Chat
{
    // A live chat log and input for one channel.
    public class ChatPanel
    {
        private readonly string _channel;
        private Vector2 _scroll;
        private string  _input = "";
        private int _lastCount;
        // Media finishing its download moves every offset below it, so the view pins to a MESSAGE, not a pixel row.
        private long  _anchorId;
        private float _anchorOffset;
        private int   _unreadBelow;
        private bool    _pinnedBottom = true;
        private string  _controlName;
        private bool    _refocus;
        private bool    _hadFocus;
        private int     _refocusPasses;
        private const int RefocusGiveUp = 60;
        // Focus the input the first time this channel is shown, so switching channel lands ready to type without a click.
        private bool    _focusedOnce;

        // False in a floating window: a focused TextField swallows RimWorld's keys and the camera stops responding.
        public bool AutoFocus = true;

        public ChatPanel(string channel) { _channel = channel ?? ChatCache.ServerChannel; _controlName = "kmhChat_" + _channel; }

        public string Channel => _channel;

        // Read a frame later by the window, which decides whether Enter belongs here before anything is drawn.
        public bool InputFocused { get; private set; }

        // Not done on every draw, or focus is yanked back the moment the player clicks anything else.
        public void RequestFocus() => _refocus = true;

        public void InsertMention(string name) => _input = ChatMentions.Insert(_input, name);

        public void Draw(Rect rect)
        {
            ChatCache.MarkRead(_channel);   // visible = read

            const float inputH = 30f, gap = 6f;
            Rect logRect   = new Rect(rect.x, rect.y, rect.width, rect.height - inputH - gap);
            // Send yields width with the input, so the field never inverts on a narrow pane.
            float sendW    = Mathf.Clamp(rect.width * 0.22f, 44f, 80f);
            Rect inputRect = new Rect(rect.x, rect.yMax - inputH, Mathf.Max(40f, rect.width - sendW - 4f), inputH);
            Rect sendRect  = new Rect(inputRect.xMax + 4f, rect.yMax - inputH, sendW, inputH);

            // Laid out BEFORE the log: IMGUI ids come from draw order, so a log that gains a row moves the caret off this field.
            bool enter = EnterPressed();
            bool tab   = TabPressed();

            if (Event.current.type == EventType.MouseDown && inputRect.Contains(Event.current.mousePosition))
                RaiseHost();

            GUI.SetNextControlName(_controlName);
            _input = Widgets.TextField(inputRect, _input ?? "");

            if (enter)
            {
                // Consumed only now, so _input is current and the keypress cannot reach the window's accept handler.
                Event.current.Use();
                Send();
            }
            if (Widgets.ButtonText(sendRect, "Send")) { Send(); _refocus = true; }

            DrawMentionSuggestions(new Rect(inputRect.x, inputRect.y - 26f, inputRect.width, 24f), tab);

            string focusedNow = GUI.GetNameOfFocusedControl();
            if (DriftedOffField(_hadFocus, focusedNow == _controlName, focusedNow, GUIUtility.keyboardControl))
                _refocus = true;
            _hadFocus = InputFocused = focusedNow == _controlName;

            if (AutoFocus && !_focusedOnce) { _focusedOnce = true; _refocus = true; }

            if (_refocus)
            {
                if (ShouldTakeFocus(InputFocused, ++_refocusPasses)) GUI.FocusControl(_controlName);
                else { _refocus = false; _refocusPasses = 0; }
            }

            Widgets.DrawMenuSection(logRect);
            DrawLog(logRect.ContractedBy(4f));
        }

        // A shifted control id leaves the keyboard on an unnamed control; an unfocus by the player is never taken back.
        internal static bool DriftedOffField(bool hadFocus, bool hasFocus, string focusedName, int keyboardControl)
            => hadFocus && !hasFocus && string.IsNullOrEmpty(focusedName) && keyboardControl != 0;

        // Never ask while the field already holds the caret: re-applying focus resets the editor and eats keystrokes.
        internal static bool ShouldTakeFocus(bool alreadyFocused, int passes)
            => !alreadyFocused && passes <= RefocusGiveUp;

        // Text layout is the expensive half and changes only with the log or the width; picture heights are lookups.
        private List<ChatMessage> _measured = new List<ChatMessage>();
        private string _measuredChannel = "";
        private long   _measuredGeneration = -1;
        private long   _measuredUiGeneration = -1;
        private bool   _measuredDiscordTag;
        private float  _measuredWidth = -1f;

        // Line() renders linked names, staff badges and a setting too, and none of those bumps the chat generation.
        private static long UiInputsGeneration => KmhCacheEvents.Generation;
        private float[] _heights = new float[0];
        private float[] _imageH  = new float[0];
        private float[] _tops    = new float[0];

        private void DrawLog(Rect box)
        {
            float viewW = Mathf.Max(1f, box.width - DialogLayout.ScrollbarReserveWidth);
            long generation = ChatCache.Generation;
            long uiGeneration = UiInputsGeneration;
            bool discordTag = KMHPatchMod.Settings?.ShowDiscordTag ?? true;
            bool stale = generation != _measuredGeneration
                      || uiGeneration != _measuredUiGeneration
                      || discordTag != _measuredDiscordTag
                      || !string.Equals(_measuredChannel, _channel, StringComparison.Ordinal)
                      || !Mathf.Approximately(_measuredWidth, viewW);

            if (stale)
            {
                _measured = ChatCache.Recent(_channel);
                _measuredChannel = _channel; _measuredGeneration = generation; _measuredWidth = viewW;
                _measuredUiGeneration = uiGeneration; _measuredDiscordTag = discordTag;
                if (_heights.Length < _measured.Count)
                {
                    _heights = new float[_measured.Count];
                    _imageH  = new float[_measured.Count];
                    _tops    = new float[_measured.Count];
                }
                for (int i = 0; i < _measured.Count; i++)
                    _heights[i] = Mathf.Max(DialogLayout.TextRowH, Text.CalcHeight(Line(_measured[i]), viewW));
            }

            List<ChatMessage> msgs = _measured;
            float[] heights = _heights, imageH = _imageH, tops = _tops;

            // Re-run every frame so a picture finishing its load moves the rows it should, and nothing else.
            float total = 0f;
            for (int i = 0; i < msgs.Count; i++)
            {
                tops[i]   = total;
                imageH[i] = ImageBlockHeight(msgs[i], viewW);
                total += heights[i] + imageH[i] + 2f;
            }

            Rect view = new Rect(0f, 0f, viewW, Mathf.Max(total, box.height));
            float maxScroll = Mathf.Max(0f, total - box.height);

            int added = Mathf.Max(0, msgs.Count - _lastCount);
            _lastCount = msgs.Count;

            if (_pinnedBottom)
            {
                // Stays at the bottom through new messages and through a picture loading further up.
                _scroll.y = maxScroll;
                _unreadBelow = 0;
            }
            else
            {
                // Scrolled up: hold the message being read in place, since an image above it moves every offset below.
                if (_anchorId != 0)
                {
                    int a = IndexOfId(msgs, _anchorId);
                    if (a >= 0) _scroll.y = Mathf.Clamp(tops[a] - _anchorOffset, 0f, maxScroll);
                }
                if (added > 0) _unreadBelow += added;
            }

            Widgets.BeginScrollView(box, ref _scroll, view);
            if (msgs.Count == 0)
                DialogLayout.LabelTrunc(new Rect(2f, 2f, viewW, DialogLayout.TextRowH), "<color=grey>No messages yet. Say hello!</color>");
            float y = 0f;
            for (int i = 0; i < msgs.Count; i++)
            {
                float rowH = heights[i] + imageH[i] + 2f;

                // An off-screen row still allocates controls and asks the image cache to fetch its picture.
                if (!OnScreen(y, rowH, _scroll.y, box.height)) { y += rowH; continue; }

                Rect r = new Rect(0f, y, viewW, heights[i]);
                Widgets.DrawHighlightIfMouseover(r);
                Widgets.Label(r, Line(msgs[i]));
                string tip = TipFor(msgs[i]);
                if (tip != null) TooltipHandler.TipRegion(r, tip);
                if (Widgets.ButtonInvisible(r)) ShowMsgMenu(msgs[i]);
                y += heights[i] + 2f;

                if (imageH[i] > 0f)
                {
                    DrawImageBlock(new Rect(0f, y, viewW, imageH[i]), msgs[i]);
                    y += imageH[i];
                }
            }
            Widgets.EndScrollView();

            _pinnedBottom = _scroll.y >= maxScroll - 4f;
            if (_pinnedBottom) _unreadBelow = 0;

            // Re-derived from where the player ended up, so their scrolling moves the anchor and a height change does not.
            RememberAnchor(msgs, tops);

            // Drawn over the log, outside the scroll view, so it costs the viewRect nothing.
            if (!_pinnedBottom && _unreadBelow > 0) DrawNewMessagePill(box, maxScroll);
        }

        // A margin either side keeps the row above and below ready, so scrolling does not reveal blanks.
        internal static bool OnScreen(float rowTop, float rowHeight, float scrollY, float viewHeight)
            => rowTop + rowHeight >= scrollY - RowMargin && rowTop <= scrollY + viewHeight + RowMargin;

        private const float RowMargin = 400f;

        // The topmost message still on screen, and how far below the viewport's top edge it sits.
        private void RememberAnchor(List<ChatMessage> msgs, float[] tops)
        {
            for (int i = 0; i < msgs.Count; i++)
            {
                if (tops[i] < _scroll.y - 0.5f) continue;
                _anchorId = msgs[i].Id;
                _anchorOffset = tops[i] - _scroll.y;
                return;
            }
            _anchorId = 0;
            _anchorOffset = 0f;
        }

        private static int IndexOfId(List<ChatMessage> msgs, long id)
        {
            for (int i = 0; i < msgs.Count; i++) if (msgs[i].Id == id) return i;
            return -1;
        }

        // "3 new messages" - only while scrolled away from the bottom, and it takes the player there.
        private void DrawNewMessagePill(Rect box, float maxScroll)
        {
            string label = _unreadBelow == 1 ? "1 new message  v" : $"{_unreadBelow} new messages  v";
            float w = Mathf.Min(box.width - 24f, 190f);
            Rect pill = new Rect(box.x + (box.width - w) * 0.5f, box.yMax - 30f, w, 24f);

            Widgets.DrawBoxSolid(pill, new Color(0f, 0f, 0f, 0.72f));
            Widgets.DrawBox(pill);
            TextAnchor a = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(pill, $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Accent)}>{label}</color>");
            Text.Anchor = a;

            if (Widgets.ButtonInvisible(pill))
            {
                _scroll.y = maxScroll;
                _pinnedBottom = true;
                _unreadBelow = 0;
                _anchorId = 0;
            }
        }

        // Three independent signals so colour is never the only one; the name resolves through LinkedAccountsCache, so a copied Discord name cannot impersonate.
        private static string Line(ChatMessage m)
        {
            string t   = new DateTime(m.SentUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm");
            string who = LinkedAccountsCache.Canonical(m.FromUsername);

            string nameHex = UI.KmhTheme.Hex(m.FromDiscord ? UI.KmhTheme.NameDiscord : UI.KmhTheme.NameNormal);
            string bodyHex = UI.KmhTheme.Hex(m.FromDiscord ? UI.KmhTheme.TextDiscord : UI.KmhTheme.TextNormal);

            // [D] relayed from a linked account, [D?] from someone with no KMH link. The tag can be hidden, the marker cannot.
            string src = "";
            if (m.FromDiscord)
            {
                bool tag = KMHPatchMod.Settings?.ShowDiscordTag ?? true;
                string mark = UI.KmhTheme.DiscordMarker + (tag ? (m.UnverifiedSender ? " [D?]" : " [D]") : "");
                src = $"<color={UI.KmhTheme.Hex(UI.KmhTheme.DiscordSrc)}>{mark}</color> ";
            }

            // Unverified senders are excluded outright: a badge must never appear next to a name nobody proved.
            string badge = m.UnverifiedSender ? "" : Identity.KmhStaff.Tag(who);

            // A body that is only the address of the picture below it becomes a short label; anything written is kept.
            string shown = ChatMediaLabel.DisplayBody(m.Body, m.ImageUrl, out bool compacted);
            if (compacted) shown = $"<color=grey>[</color>{shown}<color=grey>]</color>";

            // A line naming you is marked with a glyph as well as a tint, so it still stands out without colour.
            string atMe = ChatMentions.MentionsMe(m.Body, KmhSession.Me)
                ? $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Accent)}><b>@</b></color> " : "";

            return $"<color=grey>{t}</color>  {atMe}{src}{badge}<color={nameHex}><b>{who}</b></color>: "
                 + $"<color={bodyHex}>{shown}</color>";
        }

        // Measured from the CACHE, in the same pass as the row heights - this feeds the scroll viewRect.
        private static float ImageBlockHeight(ChatMessage m, float viewW)
        {
            if (m == null) return 0f;

            // A watch link earns a row of its own even with no preview picture, because the row IS the play button.
            string watch = ChatVideoLink.FirstWatchUrl(m.Body);
            if (watch.Length > 0 && ChatYouTube.Available)
            {
                // Only a PLAYING stream has real dimensions to reserve; one still opening is a single line like any other.
                if (ChatVideoPlayer.IsPlaying(watch))
                {
                    VideoPictureSize(viewW, out _, out float wh);
                    return wh + ChatVideoControls.BarHeight + 8f;
                }
                if (ChatVideoPlayer.IsActive(watch) || string.IsNullOrEmpty(m.ImageUrl)) return PlaceholderHeight;
            }

            if (string.IsNullOrEmpty(m.ImageUrl)) return 0f;

            // Sized from the video's own aspect so a portrait clip is not letterboxed, plus a row for the transport bar.
            if (m.IsVideo)
            {
                if (!ChatVideoPlayer.IsPlaying(m.ImageUrl)) return PlaceholderHeight;
                VideoPictureSize(viewW, out _, out float vh);
                return vh + ChatVideoControls.BarHeight + 8f;
            }
            // Measured from the animation's STILL size - frames that differ would resize the row and shove the log around.
            if (ChatImageCache.TrySize(MediaKey(m), out int iw, out int ih))
            {
                PictureSize(iw, ih, viewW, out _, out float h);
                return h + 4f;
            }
            return PlaceholderHeight;
        }

        private const float MaxImageHeight    = 220f;
        private const float PlaceholderHeight = 26f;

        private static float ImageWidth(float viewW) => Mathf.Max(32f, viewW - 24f);

        // Both limits are applied to the same scale, so the rect IS the picture and overlays land on it. Never upscales.
        internal static void PictureSize(int iw, int ih, float viewW, out float w, out float h)
        {
            float scale = Mathf.Min(1f, ImageWidth(viewW) / Mathf.Max(1, iw), MaxImageHeight / Mathf.Max(1, ih));
            w = Mathf.Max(1f, iw * scale);
            h = Mathf.Max(1f, ih * scale);
        }

        // Measured in one place: the reserved height and the drawn picture feed the same viewRect across both passes.
        private static void VideoPictureSize(float viewW, out float w, out float h)
            => PictureSize(ChatVideoPlayer.Width, ChatVideoPlayer.Height, viewW, out w, out h);

        // A placeholder is the normal state: fetching tells the host the player's IP, so the click is the request.
        private void DrawImageBlock(Rect rect, ChatMessage m)
        {
            // A playing watch link IS the row; until then it needs the play row, picture or no picture.
            string watchLink = ChatVideoLink.FirstWatchUrl(m.Body);
            if (watchLink.Length > 0 && ChatYouTube.Available)
            {
                if (ChatVideoPlayer.IsActive(watchLink)) { DrawVideoBlock(rect, watchLink); return; }
                if (string.IsNullOrEmpty(m.ImageUrl)) { DrawWatchRow(rect, watchLink); return; }
            }

            // Either the server-resolved id or the vetted url, decided by MediaKey.
            string url = MediaKey(m);

            // Same consent rule as an image: a click starts it and nothing starts on its own.
            if (m.IsVideo)
            {
                DrawVideoBlock(rect, url);
                return;
            }

            if (ChatImageCache.IsReady(url, out Texture2D tex) && tex != null
                && ChatImageCache.TrySize(url, out int iw, out int ih))
            {
                // Sized from the still dimensions so an animation keeps one footprint whatever frame is showing.
                PictureSize(iw, ih, rect.width, out float w, out float h);
                Rect img = new Rect(12f, rect.y + 2f, w, h);
                GUI.DrawTexture(img, tex, ScaleMode.ScaleToFit);
                string watch = ChatVideoLink.FirstWatchUrl(m.Body);
                if (!string.IsNullOrEmpty(watch)) { DrawWatchBar(img, watch); return; }

                bool animated = ChatImageCache.IsAnimated(url);
                TooltipHandler.TipRegion(img,
                    $"{ChatMediaLabel.KindOf(url, animated)} from {ChatImageCache.HostOf(url)}");

                // Otherwise 'is this animating or did it just load a still?' is only answerable by staring at it.
                if (animated)
                {
                    Rect tag = new Rect(img.x + 2f, img.y + 2f, 34f, 16f);
                    Widgets.DrawBoxSolid(tag, new Color(0f, 0f, 0f, 0.55f));
                    Text.Font = GameFont.Tiny;
                    DialogLayout.LabelTrunc(tag, $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Info)}>{ChatMediaLabel.Badge(url)}</color>",
                                            TextAnchor.MiddleCenter);
                    Text.Font = GameFont.Small;
                }
                return;
            }

            Rect row = new Rect(12f, rect.y, rect.width - 24f, PlaceholderHeight - 2f);
            if (ChatImageCache.IsLoading(url))
            {
                DialogLayout.LabelTrunc(row, $"<color=grey>Loading image from {ChatImageCache.HostOf(url)}…</color>");
                return;
            }

            string failed = ChatImageCache.FailureFor(url);
            if (failed != null)
            {
                // An expired Discord signature answers 404 for ever: ask for a fresh url ONCE, then say so plainly.
                if (!string.IsNullOrEmpty(m.MediaRef))
                {
                    string gone = ChatMediaRefreshClient.GoneReasonFor(m.MediaRef);
                    if (gone != null)
                    {
                        DialogLayout.LabelTrunc(row, $"<color=grey>Media no longer available - {gone}</color>");
                        return;
                    }
                    ChatMediaRefreshClient.Request(m.MediaRef);   // idempotent: once per reference per session
                    DialogLayout.LabelTrunc(row, "<color=grey>Getting a fresh link for this media…</color>");
                    return;
                }

                // Nothing a retry can reach. Only history saved before media references existed gets here.
                if (ChatSignedLink.IsExpired(url))
                {
                    DialogLayout.LabelTrunc(row, "<color=grey>Media no longer available - the host's link for it expired</color>");
                    return;
                }

                // Offered only where asking again could answer differently: a 404 says the same thing every time.
                bool canRetry = ChatImageCache.CanRetry(url);
                DialogLayout.LabelTrunc(row,
                    $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Warning)}>Image failed: {failed}</color>"
                    + (canRetry ? "  <color=grey>(click to retry)</color>" : ""));
                if (canRetry && Widgets.ButtonInvisible(row)) ChatImageCache.Retry(url);
                return;
            }

            // The player's standing answer: 'automatically' removes the click, not the queue or the cap.
            if (KMHPatchMod.Settings?.AutoLoadChatImages == true)
            {
                ChatImageCache.Request(url);
                DialogLayout.LabelTrunc(row, $"<color=grey>Loading image from {ChatImageCache.HostOf(url)}…</color>");
                return;
            }

            if (watchLink.Length > 0)
            {
                if (ChatYouTube.Available) { DrawWatchRow(rect, watchLink); return; }
                DialogLayout.LabelTrunc(row,
                    $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Info)}>[{ChatVideoLink.SiteOf(watchLink)} video]</color>  "
                    + $"<color=grey>click to load the preview from {ChatImageCache.HostOf(url)}</color>");
                TooltipHandler.TipRegion(row,
                    $"{ChatVideoLink.SiteOf(watchLink)} serves no playable video file - its own player is the page, so there "
                    + "is nothing here for RimWorld to stream. The thumbnail is what can be shown.\n\nLoad it, then use "
                    + "Watch to open the video in your browser. A link to a video FILE does play in the game.");
                if (Widgets.ButtonInvisible(row)) ChatImageCache.Request(url);
                return;
            }

            DialogLayout.LabelTrunc(row,
                $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Info)}>[{ChatMediaLabel.Kind(m.ImageUrl)}]</color>  "
                + $"<color=grey>click to load from {ChatImageCache.HostOf(url)}</color>");
            TooltipHandler.TipRegion(row,
                "Loading this image contacts " + ChatImageCache.HostOf(url) + " directly, which tells that host your "
                + "IP address.\n\nKMH never loads one on its own. Turn on \"Load chat images automatically\" in Mod "
                + "Options if you would rather not click each time.");
            if (Widgets.ButtonInvisible(row)) ChatImageCache.Request(url);
        }

        // Drawn over the image rather than beside it, so the controls cost the row no extra height.
        private static void DrawWatchBar(Rect img, string watchUrl)
        {
            string site = ChatVideoLink.SiteOf(watchUrl);
            var bar = new Rect(img.x, img.yMax - 26f, img.width, 26f);
            Widgets.DrawBoxSolid(bar, new Color(0f, 0f, 0f, 0.65f));

            float x = bar.x + 4f;
            if (CanPlayHere(watchUrl) && bar.width > 190f)
            {
                var play = new Rect(x, bar.y + 2f, 74f, 22f);
                if (Widgets.ButtonText(play, "Play here")) ChatYouTube.RequestPlay(watchUrl);
                TooltipHandler.TipRegion(play, PlayHereTip(site));
                x = play.xMax + 4f;
            }

            var btn = new Rect(x, bar.y + 2f, Mathf.Min(70f, Mathf.Max(40f, bar.xMax - 4f - x)), 22f);
            if (Widgets.ButtonText(btn, "Watch")) Application.OpenURL(watchUrl);

            // The resolve's answer first, then what the player made of it - a stream that would not decode has to say so.
            string note = ChatYouTube.StatusFor(watchUrl) ?? ChatVideoPlayer.FailureFor(watchUrl) ?? WatchNote(site, watchUrl);
            if (bar.xMax - btn.xMax > 60f)
                DialogLayout.LabelTrunc(new Rect(btn.xMax + 6f, bar.y, bar.xMax - btn.xMax - 10f, 22f),
                                        $"<color=grey>{note}</color>", TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(bar,
                $"{watchUrl}\n\n{site} serves no playable video file - its player is the page itself - so this "
                + "opens in your browser. A link to a video FILE plays here in the game instead.");
        }

        // Twitch and Vimeo are recognised as pages but only YouTube plays, so the rest must not offer a dead button.
        internal static bool CanPlayHere(string watchUrl)
            => ChatYouTube.Available && ChatYouTube.VideoIdOf(watchUrl).Length > 0;

        // The play row for a link with no preview picture - a typed link, or a server that does not vet thumbnails.
        private static void DrawWatchRow(Rect rect, string watchUrl)
        {
            string site = ChatVideoLink.SiteOf(watchUrl);
            var row = new Rect(12f, rect.y, Mathf.Max(120f, rect.width - 24f), PlaceholderHeight - 2f);

            float x = row.x;
            if (CanPlayHere(watchUrl))
            {
                var play = new Rect(x, row.y, 74f, row.height);
                if (Widgets.ButtonText(play, "Play here")) ChatYouTube.RequestPlay(watchUrl);
                TooltipHandler.TipRegion(play, PlayHereTip(site));
                x = play.xMax + 4f;
            }

            var open = new Rect(x, row.y, 70f, row.height);
            if (Widgets.ButtonText(open, "Watch")) Application.OpenURL(watchUrl);
            TooltipHandler.TipRegion(open, watchUrl);
            x = open.xMax + 6f;

            // The resolve's answer first, then what the player made of it - a stream that would not decode has to say so.
            string note = ChatYouTube.StatusFor(watchUrl) ?? ChatVideoPlayer.FailureFor(watchUrl) ?? WatchNote(site, watchUrl);
            if (row.xMax - x > 40f)
            {
                var noteRect = new Rect(x, row.y, row.xMax - x, row.height);
                DialogLayout.LabelTrunc(noteRect, $"<color=grey>{note}</color>", TextAnchor.MiddleLeft);
                if (ChatYouTube.CanStop(watchUrl) && Widgets.ButtonInvisible(noteRect)) ChatYouTube.Stop(watchUrl);
            }
        }

        // The title once the server has sent one, and what this row can actually do until then.
        private static string WatchNote(string site, string watchUrl)
        {
            string title = ChatYouTube.TitleFor(watchUrl);
            if (title.Length > 0) return title;
            return CanPlayHere(watchUrl) ? $"{site} - play here or open in your browser" : $"{site} - opens in your browser";
        }

        private static string PlayHereTip(string site)
            => $"Plays the video inside the game. The KMH server fetches it from {site} and sends it on, so nothing "
             + "here ever contacts the site and it never learns your IP address.\n\n"
             + "The first play downloads the video to a temporary folder on this machine; watching it again, or "
             + "looping it, then costs the server nothing. Mod options sets how much is kept and can clear it.\n\n"
             + "Quality is capped by the server. A long video can take a while to arrive - the row shows how far it "
             + "has got, and clicking it stops the download.";

        internal static string MediaKey(ChatMessage m)
            => m == null ? "" : MediaKeyFor(m.IsVideo, m.ImageUrl, m.MediaId,
                                            ChatMediaRefreshClient.FreshUrlFor(m.MediaRef),
                                            ChatMediaClient.Available,
                                            ChatMediaClient.FailureFor(m.MediaId) != null);

        // Pure and pinned by a test: a refreshed link wins, then the resolver id while viable, then the url.
        internal static string MediaKeyFor(bool isVideo, string imageUrl, string mediaId, string freshUrl,
                                           bool resolverAvailable, bool resolvedFailed)
        {
            if (!isVideo && !string.IsNullOrEmpty(freshUrl)) return freshUrl;
            if (isVideo) return imageUrl ?? "";
            if (string.IsNullOrEmpty(mediaId) || !resolverAvailable) return imageUrl ?? "";
            if (resolvedFailed && !string.IsNullOrEmpty(imageUrl)) return imageUrl;
            return mediaId;
        }

        private void DrawVideoBlock(Rect rect, string url)
        {
            if (ChatVideoPlayer.IsPlaying(url))
            {
                VideoPictureSize(rect.width, out float vw, out float vh);
                Rect pic = new Rect(12f, rect.y + 2f, vw, vh);

                Texture frame = ChatVideoPlayer.Frame;
                if (frame != null) GUI.DrawTexture(pic, frame, ScaleMode.ScaleToFit);

                // Buffering and paused both look like a still picture, so the one that is the network's fault says so.
                string status = ChatVideoControls.StatusWord();
                if (status != null)
                {
                    TextAnchor a = Text.Anchor; Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(pic.x, pic.center.y - 12f, pic.width, 24f), $"<color=white>{status}</color>");
                    Text.Anchor = a;
                }

                // A watch link is played from this machine's own copy; anything else is still streamed from its host.
                string from = ChatYouTube.VideoIdOf(url).Length > 0
                    ? "Sent by this server and playing from a local copy."
                    : $"Streaming from {ChatImageCache.HostOf(url)}.";
                TooltipHandler.TipRegion(pic, $"{from}\n\nClick to watch fullscreen.");
                if (Widgets.ButtonInvisible(pic)) Dialog_KMHVideo.Open();

                // Aligned to the picture where it fits, but never below the width the transport needs.
                float barW = Mathf.Clamp(Mathf.Max(vw, 440f), 120f, Mathf.Max(120f, rect.width - 24f));
                if (ChatVideoControls.Draw(new Rect(12f, rect.y + vh + 6f, barW, ChatVideoControls.BarHeight),
                                           inFullscreen: false))
                    Dialog_KMHVideo.Open();
                return;
            }

            Rect row = new Rect(12f, rect.y, rect.width - 24f, PlaceholderHeight - 2f);

            if (ChatVideoPlayer.IsPreparing(url))
            {
                DialogLayout.LabelTrunc(row, $"<color=grey>Opening video from {ChatImageCache.HostOf(url)}…</color>");
                return;
            }

            string failed = ChatVideoPlayer.FailureFor(url);
            if (failed != null)
            {
                // Unity plays what the platform can decode; when it cannot, say so rather than leaving a dead row.
                DialogLayout.LabelTrunc(row,
                    $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Warning)}>Video can't play here: {failed}</color>  "
                    + "<color=grey>(click to open in your browser)</color>");
                TooltipHandler.TipRegion(row,
                    "RimWorld can only play what your system can decode - mp4 works most reliably.\n\nOpening in a "
                    + "browser leaves the game and tells that host your IP address.");
                if (Widgets.ButtonInvisible(row)) OpenExternally(url);
                return;
            }

            DialogLayout.LabelTrunc(row,
                $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Info)}>[video]</color>  "
                + $"<color=grey>click to play from {ChatImageCache.HostOf(url)}</color>");
            TooltipHandler.TipRegion(row,
                "Plays inside the game - nothing is downloaded and nothing is written to disk.\n\nStarting it does "
                + "contact " + ChatImageCache.HostOf(url) + " directly, which tells that host your IP address.\n\n"
                + "Plays with sound at your saved volume, scaled by RimWorld's master volume. Click the picture for "
                + "fullscreen. Only one video plays at a time.");
            if (Widgets.ButtonInvisible(row)) ChatVideoPlayer.Play(url);
        }

        private static void OpenExternally(string url)
        {
            try { Application.OpenURL(url); }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"Chat video: could not open {url} - {ex.Message}"); }
        }

        // Said in words on hover, because a mark only helps someone who already knows what it means.
        private static string TipFor(ChatMessage m)
        {
            // A compacted body hides the url, so the hover has to carry it or nobody can see where the media came from.
            if (m != null && !string.IsNullOrEmpty(m.ImageUrl))
            {
                ChatMediaLabel.DisplayBody(m.Body, m.ImageUrl, out bool wasCompacted);
                if (wasCompacted)
                {
                    string where = $"Media from {ChatImageCache.HostOf(m.ImageUrl)}\n\n{m.Body}";
                    return m.FromDiscord ? where + "\n\n" + DiscordTip(m) : where;
                }
            }

            if (m == null || !m.FromDiscord) return null;
            return DiscordTip(m);
        }

        private static string DiscordTip(ChatMessage m)
        {
            return m.UnverifiedSender
                ? "Relayed from Discord - unverified sender.\n\nThis person has not linked a KMH account, so the name "
                + "is only their chosen Discord display name. Do not treat it as proof of who they are."
                : "Relayed from Discord.\n\nThe sender has a verified KMH account link, so the name shown is their "
                + "real KMH identity.";
        }

        // Typing @ offers the names it could be. Drawn OVER the log, so the row heights below never move as it appears.
        private void DrawMentionSuggestions(Rect strip, bool completeFirst)
        {
            string partial = ChatMentions.PartialAt(_input);
            if (partial == null || !InputFocused) return;

            List<string> names = ChatRosterCache.Match(partial, 5);
            if (names.Count == 0) return;

            if (completeFirst)
            {
                Event.current.Use();
                _input = ChatMentions.Complete(_input, names[0]);
                _refocus = true;
                return;
            }

            Widgets.DrawBoxSolid(strip, new Color(0f, 0f, 0f, 0.85f));
            float x = strip.x + 2f;
            Text.Font = GameFont.Tiny;
            foreach (string name in names)
            {
                float w = Mathf.Min(120f, Text.CalcSize(name).x + 16f);
                if (x + w > strip.xMax) break;
                var btn = new Rect(x, strip.y + 2f, w, strip.height - 4f);
                string shown = ChatRosterCache.IsOnline(name) ? $"● {name}" : $"○ {name}";
                if (Widgets.ButtonText(btn, shown)) { _input = ChatMentions.Complete(_input, name); _refocus = true; }
                x += w + 3f;
            }
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(strip, "Tab completes the first name.");
        }

        // Tab would otherwise move IMGUI's focus off the field, which is the one thing that must not happen mid-word.
        private bool TabPressed()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown || e.keyCode != KeyCode.Tab) return false;
            return GUI.GetNameOfFocusedControl() == _controlName;
        }

        private bool EnterPressed()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return false;
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return false;
            // Shift+Enter is not ours - leave it for anything that wants a newline, and never send on it.
            if (e.shift) return false;
            return GUI.GetNameOfFocusedControl() == _controlName;
        }

        // Fronted so RimWorld offers it the keystroke first; harmless when it is already on top.
        private static void RaiseHost()
        {
            try
            {
                Window host = Find.WindowStack?.currentlyDrawnWindow;
                if (host == null) return;
                Find.WindowStack.Notify_ClickedInsideWindow(host);
                Find.WindowStack.Notify_ManuallySetFocus(host);
            }
            catch { }
        }

        private void Send()
        {
            if (string.IsNullOrWhiteSpace(_input)) return;   // empty/whitespace Enter does nothing
            if (!ChatHandler.TrySend(_channel, _input)) return;   // rejected (rate limit, offline): keep the draft
            _input = "";
            _pinnedBottom = true;
            _refocus = true;   // applied after the field is laid out, so a run of messages needs no re-clicking
        }

        // Click a message for moderation actions: block/unblock its sender, or (staff) remove it.
        private void ShowMsgMenu(ChatMessage m)
        {
            if (m == null) return;
            var opts = new List<FloatMenuOption>();
            if (!KmhSession.Same(m.FromUsername, KmhSession.Me) && ChatModerationCache.BlockingEnabled)
            {
                if (ChatModerationCache.IsBlocked(m.FromUsername))
                    opts.Add(new FloatMenuOption($"Unblock {m.FromUsername}", () => ChatHandler.TryBlock(m.FromUsername, false)));
                else
                    opts.Add(new FloatMenuOption($"Block {m.FromUsername}", () => ChatHandler.TryBlock(m.FromUsername, true)));
            }
            // Menu visibility only - the server re-checks every removal, so this can never grant anything.
            if (EnforcementCache.IsAdmin || Identity.KmhStaff.CanModerate(KmhSession.Me))
                opts.Add(new FloatMenuOption("Remove message (staff)", () => ChatHandler.TryRemove(_channel, m.Id)));
            if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
        }
    }
}
