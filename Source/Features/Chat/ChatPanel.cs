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
        private int     _heldId;
        private const int RefocusGiveUp = 60;
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

        public float Opacity = 1f;

        // Never inherited: rows measured in one font but drawn in whatever the last window left get cut off.
        internal const GameFont LogFont = GameFont.Small;

        private Action<Rect> _drawBody;

        public void Draw(Rect rect) => InLogFont(_drawBody ??= DrawBody, rect);

        internal static void InLogFont(Action<Rect> draw, Rect rect)
        {
            GameFont callerFont = Text.Font;
            Text.Font = LogFont;
            try { draw(rect); }
            finally { Text.Font = callerFont; }
        }

        private void DrawBody(Rect rect)
        {
            ChatCache.MarkRead(_channel);

            const float inputH = 30f, gap = 6f;
            Rect logRect   = new Rect(rect.x, rect.y, rect.width, rect.height - inputH - gap);
            float sendW    = Mathf.Clamp(rect.width * 0.22f, 44f, 80f);
            Rect inputRect = new Rect(rect.x, rect.yMax - inputH, Mathf.Max(40f, rect.width - sendW - 4f), inputH);
            Rect sendRect  = new Rect(inputRect.xMax + 4f, rect.yMax - inputH, sendW, inputH);

            // Laid out BEFORE the log: IMGUI ids follow draw order, so a log gaining a row moves the caret off this field.
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
            if (Widgets.ButtonText(sendRect, KmhText.Get("KMH_Dm_Send", "Send"))) { Send(); _refocus = true; }

            DrawMentionSuggestions(new Rect(inputRect.x, inputRect.y - 26f, inputRect.width, 24f), tab);

            string focusedNow = GUI.GetNameOfFocusedControl();
            if (DriftedOffField(_hadFocus, focusedNow == _controlName, focusedNow, GUIUtility.keyboardControl, _heldId))
                _refocus = true;
            _hadFocus = InputFocused = focusedNow == _controlName;
            if (InputFocused) _heldId = GUIUtility.keyboardControl;

            if (AutoFocus && !_focusedOnce) { _focusedOnce = true; _refocus = true; }

            if (_refocus)
            {
                if (ShouldTakeFocus(InputFocused, ++_refocusPasses)) GUI.FocusControl(_controlName);
                else { _refocus = false; _refocusPasses = 0; }
            }

            UI.KmhGlass.Section(logRect, Opacity);
            DrawLog(logRect.ContractedBy(4f));
        }

        // Drift is only a shifted id (same id, now unnamed); a click into another field is the player's choice to keep.
        internal static bool DriftedOffField(bool hadFocus, bool hasFocus, string focusedName, int keyboardControl, int heldId)
            => hadFocus && !hasFocus && string.IsNullOrEmpty(focusedName) && keyboardControl != 0 && keyboardControl == heldId;

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
        // Built in the measuring pass: rebuilt each frame they would be most of an open chat window's allocation.
        private string[] _lines  = new string[0];
        private string[] _watch  = new string[0];

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
                    _lines   = new string[_measured.Count];
                    _watch   = new string[_measured.Count];
                }
                for (int i = 0; i < _measured.Count; i++)
                {
                    _lines[i]   = Line(_measured[i]);
                    _watch[i]   = ChatVideoLink.FirstWatchUrl(_measured[i].Body);
                    _heights[i] = Mathf.Max(DialogLayout.TextRowH, Text.CalcHeight(_lines[i], viewW));
                }
            }

            List<ChatMessage> msgs = _measured;
            float[] heights = _heights, imageH = _imageH, tops = _tops;
            string[] lines = _lines, watch = _watch;

            // Re-run every frame so a picture finishing its load moves the rows it should, and nothing else.
            float total = 0f;
            for (int i = 0; i < msgs.Count; i++)
            {
                tops[i]   = total;
                imageH[i] = ImageBlockHeight(msgs[i], viewW, watch[i]);
                total += heights[i] + imageH[i] + 2f;
            }

            Rect view = new Rect(0f, 0f, viewW, Mathf.Max(total, box.height));
            float maxScroll = Mathf.Max(0f, total - box.height);

            int added = Mathf.Max(0, msgs.Count - _lastCount);
            _lastCount = msgs.Count;

            if (_pinnedBottom)
            {
                _scroll.y = maxScroll;
                _unreadBelow = 0;
            }
            else
            {
                if (_anchorId != 0)
                {
                    int a = IndexOfId(msgs, _anchorId);
                    if (a >= 0) _scroll.y = Mathf.Clamp(tops[a] - _anchorOffset, 0f, maxScroll);
                }
                if (added > 0) _unreadBelow += added;
            }

            Widgets.BeginScrollView(box, ref _scroll, view);
            if (msgs.Count == 0)
                DialogLayout.LabelTrunc(new Rect(2f, 2f, viewW, DialogLayout.TextRowH), "<color=#A9AFB9>No messages yet. Say hello!</color>");
            float y = 0f;
            for (int i = 0; i < msgs.Count; i++)
            {
                float rowH = heights[i] + imageH[i] + 2f;

                // An off-screen row still allocates controls and asks the image cache to fetch its picture.
                if (!OnScreen(y, rowH, _scroll.y, box.height)) { y += rowH; continue; }

                Rect r = new Rect(0f, y, viewW, heights[i]);
                Widgets.DrawHighlightIfMouseover(r);
                Widgets.Label(r, lines[i]);
                string tip = TipFor(msgs[i]);
                if (tip != null) TooltipHandler.TipRegion(r, tip);
                if (Widgets.ButtonInvisible(r)) ShowMsgMenu(msgs[i]);
                y += heights[i] + 2f;

                if (imageH[i] > 0f)
                {
                    DrawImageBlock(new Rect(0f, y, viewW, imageH[i]), msgs[i], watch[i]);
                    y += imageH[i];
                }
            }
            Widgets.EndScrollView();

            _pinnedBottom = _scroll.y >= maxScroll - 4f;
            if (_pinnedBottom) _unreadBelow = 0;

            // Re-derived from where the player ended up, so their scrolling moves the anchor and a height change does not.
            RememberAnchor(msgs, tops);

            if (!_pinnedBottom && _unreadBelow > 0) DrawNewMessagePill(box, maxScroll);
        }

        internal static bool OnScreen(float rowTop, float rowHeight, float scrollY, float viewHeight)
            => rowTop + rowHeight >= scrollY - RowMargin && rowTop <= scrollY + viewHeight + RowMargin;

        private const float RowMargin = 400f;

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

        // Resolved via LinkedAccountsCache so a copied Discord name cannot impersonate; colour is never the only signal.
        internal static string Line(ChatMessage m)
        {
            string t   = new DateTime(m.SentUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm");
            // A Global name is a Colony Nexus account, not one of this server's players: no local recasing or staff badge.
            bool global = ChatChannels.IsGlobal(m.Channel);
            string who = global ? m.FromUsername : LinkedAccountsCache.Canonical(m.FromUsername);

            string nameHex = UI.KmhTheme.Hex(global ? UI.KmhTheme.GlobalName(m.FromDiscord) : m.FromDiscord ? UI.KmhTheme.NameDiscord : UI.KmhTheme.NameNormal);
            string bodyHex = UI.KmhTheme.Hex(global ? UI.KmhTheme.GlobalText(m.FromDiscord) : m.FromDiscord ? UI.KmhTheme.TextDiscord : UI.KmhTheme.TextNormal);

            // [D] relayed from a linked account, [D?] from one with no KMH link; the tag can be hidden, the marker cannot.
            string src = "";
            if (m.FromDiscord)
            {
                bool tag = KMHPatchMod.Settings?.ShowDiscordTag ?? true;
                string mark = (global ? UI.KmhTheme.DefaultDiscordMarker : UI.KmhTheme.DiscordMarker) + (tag ? (m.UnverifiedSender ? " [D?]" : " [D]") : "");
                src = $"<color={UI.KmhTheme.Hex(global ? UI.KmhTheme.GlobalDiscordSrc : UI.KmhTheme.DiscordSrc)}>{mark}</color> ";
            }

            // A server relayed this for someone without an account, so the name is theirs to choose: it says so.
            if (global && !m.FromDiscord && !m.SenderVerified)
                src = "<color=#A9AFB9>[relayed]</color> ";

            // Unverified senders are excluded outright: a badge must never appear next to a name nobody proved.
            string badge = m.UnverifiedSender || global ? "" : Identity.KmhStaff.Tag(who);

            // A Discord line relayed by a server names that server, so the relay is never taken for Colony Nexus's own.
            string place = m.FromDiscord ? m.RelayedBy ?? "" : m.Origin ?? "";
            string from = global && place.Length > 0 && place != "global" && place != "discord"
                ? $" <color=#7F8794>· {(place.Length > 24 ? place.Substring(0, 23) + "…" : place)}</color>" : "";

            string shown = ChatMediaLabel.DisplayBody(m.Body, m.ImageUrl, out bool compacted);
            if (compacted) shown = $"<color=#A9AFB9>[</color>{shown}<color=#A9AFB9>]</color>";

            string atMe = ChatMentions.MentionsMe(m.Body, KmhSession.Me)
                ? $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Accent)}><b>@</b></color> " : "";

            return $"<color=#A9AFB9>{t}</color>  {atMe}{src}{badge}<color={nameHex}><b>{who}</b></color>{from}: "
                 + $"<color={bodyHex}>{shown}</color>";
        }

        // Measured from the CACHE, in the same pass as the row heights - this feeds the scroll viewRect.
        private static float ImageBlockHeight(ChatMessage m, float viewW, string watch)
        {
            if (m == null) return 0f;

            watch = watch ?? "";
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

        // One scale for both limits so the rect IS the picture and overlays land on it; never upscales.
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
        private void DrawImageBlock(Rect rect, ChatMessage m, string watchLink)
        {
            if (watchLink.Length > 0 && ChatYouTube.Available)
            {
                if (ChatVideoPlayer.IsActive(watchLink)) { DrawVideoBlock(rect, watchLink); return; }
                if (string.IsNullOrEmpty(m.ImageUrl)) { DrawWatchRow(rect, watchLink); return; }
            }

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
                PictureSize(iw, ih, rect.width, out float w, out float h);
                Rect img = new Rect(12f, rect.y + 2f, w, h);
                GUI.DrawTexture(img, tex, ScaleMode.ScaleToFit);
                if (watchLink.Length > 0) { DrawWatchBar(img, watchLink); return; }

                bool animated = ChatImageCache.IsAnimated(url);
                TooltipHandler.TipRegion(img,
                    $"{ChatMediaLabel.KindOf(url, animated)} from {ChatImageCache.HostOf(url)}");

                if (animated)
                {
                    Rect tag = new Rect(img.x + 2f, img.y + 2f, 34f, 16f);
                    Widgets.DrawBoxSolid(tag, new Color(0f, 0f, 0f, 0.55f));
                    Text.Font = GameFont.Tiny;
                    DialogLayout.LabelTrunc(tag, $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Info)}>{ChatMediaLabel.Badge(url)}</color>",
                                            TextAnchor.MiddleCenter);
                    Text.Font = LogFont;
                }
                return;
            }

            Rect row = new Rect(12f, rect.y, rect.width - 24f, PlaceholderHeight - 2f);
            if (ChatImageCache.IsLoading(url))
            {
                DialogLayout.LabelTrunc(row, $"<color=#A9AFB9>Loading image from {ChatImageCache.HostOf(url)}…</color>");
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
                        DialogLayout.LabelTrunc(row, $"<color=#A9AFB9>Media no longer available - {gone}</color>");
                        return;
                    }
                    ChatMediaRefreshClient.Request(m.MediaRef);   // idempotent: once per reference per session
                    DialogLayout.LabelTrunc(row, "<color=#A9AFB9>Getting a fresh link for this media…</color>");
                    return;
                }

                // Only history saved before media references existed gets here; no retry can fix it.
                if (ChatSignedLink.IsExpired(url))
                {
                    DialogLayout.LabelTrunc(row, "<color=#A9AFB9>Media no longer available - the host's link for it expired</color>");
                    return;
                }

                // Offered only where asking again could answer differently: a 404 says the same thing every time.
                bool canRetry = ChatImageCache.CanRetry(url);
                DialogLayout.LabelTrunc(row,
                    $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Warning)}>Image failed: {failed}</color>"
                    + (canRetry ? "  <color=#A9AFB9>(click to retry)</color>" : ""));
                if (canRetry && Widgets.ButtonInvisible(row)) ChatImageCache.Retry(url);
                return;
            }

            // The player's standing answer: 'automatically' removes the click, not the queue or the cap.
            if (KMHPatchMod.Settings?.AutoLoadChatImages == true)
            {
                ChatImageCache.Request(url);
                DialogLayout.LabelTrunc(row, $"<color=#A9AFB9>Loading image from {ChatImageCache.HostOf(url)}…</color>");
                return;
            }

            if (watchLink.Length > 0)
            {
                if (ChatYouTube.Available) { DrawWatchRow(rect, watchLink); return; }
                DialogLayout.LabelTrunc(row,
                    $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Info)}>[{ChatVideoLink.SiteOf(watchLink)} video]</color>  "
                    + $"<color=#A9AFB9>click to load the preview from {ChatImageCache.HostOf(url)}</color>");
                TooltipHandler.TipRegion(row,
                    $"{ChatVideoLink.SiteOf(watchLink)} serves no playable video file - its own player is the page, so there "
                    + "is nothing here for RimWorld to stream. The thumbnail is what can be shown.\n\nLoad it, then use "
                    + "Watch to open the video in your browser. A link to a video FILE does play in the game.");
                if (Widgets.ButtonInvisible(row)) ChatImageCache.Request(url);
                return;
            }

            DialogLayout.LabelTrunc(row,
                $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Info)}>[{ChatMediaLabel.Kind(m.ImageUrl)}]</color>  "
                + $"<color=#A9AFB9>click to load from {ChatImageCache.HostOf(url)}</color>");
            TooltipHandler.TipRegion(row,
                "Loading this image contacts " + ChatImageCache.HostOf(url) + " directly, which tells that host your "
                + "IP address.\n\nColony Nexus never loads one on its own. Turn on \"Load chat images automatically\" in Mod "
                + "Options if you would rather not click each time.");
            if (Widgets.ButtonInvisible(row)) ChatImageCache.Request(url);
        }

        private static void DrawWatchBar(Rect img, string watchUrl)
        {
            string site = ChatVideoLink.SiteOf(watchUrl);
            var bar = new Rect(img.x, img.yMax - 26f, img.width, 26f);
            Widgets.DrawBoxSolid(bar, new Color(0f, 0f, 0f, 0.65f));

            float x = bar.x + 4f;
            if (CanPlayHere(watchUrl) && bar.width > 190f) x = PlayHereButton(new Rect(x, bar.y + 2f, 74f, 22f), watchUrl, site);

            var btn = new Rect(x, bar.y + 2f, Mathf.Min(70f, Mathf.Max(40f, bar.xMax - 4f - x)), 22f);
            if (Widgets.ButtonText(btn, "Watch")) Application.OpenURL(watchUrl);

            string note = ChatYouTube.StatusFor(watchUrl) ?? ChatVideoPlayer.FailureFor(watchUrl) ?? WatchNote(site, watchUrl);
            if (bar.xMax - btn.xMax > 60f)
                DialogLayout.LabelTrunc(new Rect(btn.xMax + 6f, bar.y, bar.xMax - btn.xMax - 10f, 22f),
                                        $"<color=#A9AFB9>{note}</color>", TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(bar,
                $"{watchUrl}\n\n{site} serves no playable video file - its player is the page itself - so this "
                + "opens in your browser. A link to a video FILE plays here in the game instead.");
        }

        // Twitch and Vimeo are recognised as pages but only YouTube plays, so the rest must not offer a dead button.
        internal static bool CanPlayHere(string watchUrl)
            => ChatYouTube.Available && ChatYouTube.VideoIdOf(watchUrl).Length > 0;

        private static void DrawWatchRow(Rect rect, string watchUrl)
        {
            string site = ChatVideoLink.SiteOf(watchUrl);
            var row = new Rect(12f, rect.y, Mathf.Max(120f, rect.width - 24f), PlaceholderHeight - 2f);

            float x = row.x;
            if (CanPlayHere(watchUrl)) x = PlayHereButton(new Rect(x, row.y, 74f, row.height), watchUrl, site);

            var open = new Rect(x, row.y, 70f, row.height);
            if (Widgets.ButtonText(open, "Watch")) Application.OpenURL(watchUrl);
            TooltipHandler.TipRegion(open, watchUrl);
            x = open.xMax + 6f;

            string note = ChatYouTube.StatusFor(watchUrl) ?? ChatVideoPlayer.FailureFor(watchUrl) ?? WatchNote(site, watchUrl);
            if (row.xMax - x > 40f)
            {
                var noteRect = new Rect(x, row.y, row.xMax - x, row.height);
                DialogLayout.LabelTrunc(noteRect, $"<color=#A9AFB9>{note}</color>", TextAnchor.MiddleLeft);
                if (ChatYouTube.CanStop(watchUrl) && Widgets.ButtonInvisible(noteRect)) ChatYouTube.Stop(watchUrl);
            }
        }

        private static float PlayHereButton(Rect play, string watchUrl, string site)
        {
            if (Widgets.ButtonText(play, "Play here")) ChatYouTube.RequestPlay(watchUrl);
            TooltipHandler.TipRegion(play, PlayHereTip(site));
            return play.xMax + 4f;
        }

        private static string WatchNote(string site, string watchUrl)
        {
            string title = ChatYouTube.TitleFor(watchUrl);
            if (title.Length > 0) return title;
            return CanPlayHere(watchUrl) ? $"{site} - play here or open in your browser" : $"{site} - opens in your browser";
        }

        private static string PlayHereTip(string site)
            => $"Plays the video inside the game. The Colony Nexus server fetches it from {site} and sends it on, so nothing "
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

        // Pinned by a test: a refreshed link wins, then the resolver id while viable, then the url.
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

                string from = ChatYouTube.VideoIdOf(url).Length > 0
                    ? "Sent by this server and playing from a local copy."
                    : $"Streaming from {ChatImageCache.HostOf(url)}.";
                TooltipHandler.TipRegion(pic, $"{from}\n\nClick to watch fullscreen.");
                if (Widgets.ButtonInvisible(pic)) Dialog_KMHVideo.Open();

                float barW = Mathf.Clamp(Mathf.Max(vw, 440f), 120f, Mathf.Max(120f, rect.width - 24f));
                if (ChatVideoControls.Draw(new Rect(12f, rect.y + vh + 6f, barW, ChatVideoControls.BarHeight),
                                           inFullscreen: false))
                    Dialog_KMHVideo.Open();
                return;
            }

            Rect row = new Rect(12f, rect.y, rect.width - 24f, PlaceholderHeight - 2f);

            if (ChatVideoPlayer.IsPreparing(url))
            {
                DialogLayout.LabelTrunc(row, $"<color=#A9AFB9>Opening video from {ChatImageCache.HostOf(url)}…</color>");
                return;
            }

            string failed = ChatVideoPlayer.FailureFor(url);
            if (failed != null)
            {
                // Unity plays only what the platform can decode.
                DialogLayout.LabelTrunc(row,
                    $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Warning)}>Video can't play here: {failed}</color>  "
                    + "<color=#A9AFB9>(click to open in your browser)</color>");
                TooltipHandler.TipRegion(row,
                    "RimWorld can only play what your system can decode - mp4 works most reliably.\n\nOpening in a "
                    + "browser leaves the game and tells that host your IP address.");
                if (Widgets.ButtonInvisible(row)) OpenExternally(url);
                return;
            }

            DialogLayout.LabelTrunc(row,
                $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Info)}>[video]</color>  "
                + $"<color=#A9AFB9>click to play from {ChatImageCache.HostOf(url)}</color>");
            TooltipHandler.TipRegion(row,
                "Plays inside the game - nothing is downloaded and nothing is written to disk.\n\nStarting it does "
                + "contact " + ChatImageCache.HostOf(url) + " directly, which tells that host your IP address.\n\n"
                + "Plays with sound at your saved volume, scaled by RimWorld's master volume. Click the picture for "
                + "fullscreen. Only one video plays at a time.");
            if (Widgets.ButtonInvisible(row)) ChatVideoPlayer.Play(url);
        }

        internal static Action<string> OpenForTest;

        // The shell hands any other scheme to whatever program claims it.
        internal static void OpenExternally(string url)
        {
            if (!ChatImageCache.IsWebMedia(url)) { Diagnostics.KmhLog.Warn("Chat video: refused to open a link that is not https."); return; }
            if (OpenForTest != null) OpenForTest(url);
            else Launch(url);
        }

        private static void Launch(string url)
        {
            try { Application.OpenURL(url); }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"Chat video: could not open {url} - {ex.Message}"); }
        }

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
            if (m.UnverifiedSender)
                return "Relayed from Discord - unverified sender.\n\nThis person has not linked an account, so the name "
                     + "is only their chosen Discord display name. Do not treat it as proof of who they are.";
            // On a server channel the link is the server's own record; only Global's is checked by Colony Nexus.
            return ChatChannels.IsGlobal(m.Channel)
                ? "Relayed from Discord.\n\nThe sender has a verified Colony Nexus account link, so the name shown is their "
                + "real Colony Nexus identity."
                : "Relayed from Discord by this server.\n\nThe sender linked their Discord to a player on this server, so the "
                + "name shown is that player's name here. The link is this server's record, not one Colony Nexus checked.";
        }

        // Drawn OVER the log so row heights never move as it appears.
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
                // The name is the server's word, so it is shown inert; completion still inserts it as typed.
                string plain = KmhDisplayText.Inert(name, 40);
                float w = Mathf.Min(120f, Text.CalcSize(plain).x + 16f);
                if (x + w > strip.xMax) break;
                var btn = new Rect(x, strip.y + 2f, w, strip.height - 4f);
                string shown = ChatRosterCache.IsOnline(name) ? $"● {plain}" : $"○ {plain}";
                if (Widgets.ButtonText(btn, shown)) { _input = ChatMentions.Complete(_input, name); _refocus = true; }
                x += w + 3f;
            }
            Text.Font = LogFont;
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
            if (string.IsNullOrWhiteSpace(_input)) return;
            if (!ChatHandler.TrySend(_channel, _input)) return;   // rejected (rate limit, offline): keep the draft
            _input = "";
            _pinnedBottom = true;
            _refocus = true;   // applied after the field is laid out, so a run of messages needs no re-clicking
        }

        private void ShowMsgMenu(ChatMessage m)
        {
            if (m == null) return;
            // Global Chat acts on the sender's Global account; a same-named player here is someone else.
            KmhPlayerMenu.Target who = KmhPlayerMenu.TargetFor(m, _channel);
            List<FloatMenuOption> opts = KmhPlayerMenu.Options(who);
            // Visibility only (the server re-checks removals); Global Chat lines belong to Global, not this server's staff.
            if (!ChatChannels.IsGlobal(_channel) && (EnforcementCache.IsAdmin || Identity.KmhStaff.CanModerate(KmhSession.Me)))
                opts.Add(new FloatMenuOption("Remove message (staff)", () => ChatHandler.TryRemove(_channel, m.Id)));
            if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts, KmhPlayerMenu.Title(who)));
        }
    }
}
