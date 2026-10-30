using System;
using System.Collections.Generic;
using KMHPatch.Features.Chat;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Mail;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Comms
{
    public class Dialog_KMHComms : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(900f, 640f);

        private const string MailView = "\0mail";   // sentinel; never a valid chat channel

        private string _selected = ChatChannels.Server;
        private string _viewing;   // what CommsFocus was last told, so a switch is one paired call
        private Vector2 _sideScroll;

        private readonly MailPanel _mail = new MailPanel();
        private readonly Dictionary<string, ChatPanel> _panels = new Dictionary<string, ChatPanel>(StringComparer.Ordinal);
        private readonly GlobalDmPanel _globalDm = new GlobalDmPanel();
        private readonly HashSet<string> _requested = new HashSet<string>(StringComparer.Ordinal);

        internal override float BackgroundOpacity => KMHPatchSettings.ChatOpacity(KMHPatchMod.Settings);

        public Dialog_KMHComms()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            // Enter is the send key in chat, so it must not also be the window's accept-and-close key. Esc still closes.
            closeOnAccept           = false;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            EnableAutoRefresh(() =>
            {
                if (!OnServer) return;
                ChatHandler.RequestSnapshot(); ChatHandler.RequestModeration(); MailHandler.RequestSnapshot();
            });
            ChatCache.Updated           += MarkRefreshed;
            ChatModerationCache.Updated += MarkRefreshed;
            MailCache.Updated           += MarkRefreshed;
        }

        // Off a server (the main menu, or after leaving one) only what Global holds can work: Global Chat and Global DMs.
        private static bool OnServer => SubProtocol.KmhDispatcher.IsKmhServer;

        // openOnMail lands on the inbox, so "Mail" anywhere in the UI reaches this hub rather than a second window.
        public static void Open(bool openOnMail = false)
            => Find.WindowStack.Add(new Dialog_KMHComms { _selected = openOnMail ? MailView : OnServer ? ChatChannels.Server : ChatChannels.Global });

        public static void OpenGlobal() => OpenOn(ChatChannels.Global);

        private static bool WorksOffServer(string channel)
            => ChatChannels.IsGlobal(channel) || ChatChannels.IsGlobalDm(channel);

        public static void OpenOn(string channel)
        {
            if (string.IsNullOrEmpty(channel)) { Open(); return; }
            Dialog_KMHComms open = Find.WindowStack.WindowOfType<Dialog_KMHComms>();
            if (open == null)
            {
                open = new Dialog_KMHComms();
                Find.WindowStack.Add(open);
            }
            open.Select(channel);
        }

        public override void PostClose()
        {
            base.PostClose();
            ChatCache.Updated           -= MarkRefreshed;
            ChatModerationCache.Updated -= MarkRefreshed;
            MailCache.Updated           -= MarkRefreshed;
            CommsFocus.Leave(_viewing);
            _viewing = null;

            ChatVideoPlayer.StopIfNobodyWatching();
        }

        protected override void DrawContents(Rect rect)
        {
            bool onServer = OnServer;
            // Leaving a server with Server Chat or Mail open lands on what still works, not on a dead channel.
            if (!onServer && !WorksOffServer(_selected)) _selected = ChatChannels.Global;
            CommsFocus.Switch(ref _viewing, _selected == MailView ? null : _selected);

            bool muted = KMHPatchMod.Settings != null && KMHPatchMod.Settings.ChatNotificationsMuted;

            // Plain text, no emoji (the font atlas has none); widths from the labels, so a translation fits its button.
            string muteLabel = muted ? KmhText.Get("KMH_Comms_Unmute", "Muted - unmute") : KmhText.Get("KMH_Comms_NotifyOn", "Notifications on");
            string popLabel  = "Pop out";
            string markLabel = KmhText.Get("KMH_Comms_MarkRead", "Mark all read");
            float muteW = Mathf.Min(DialogLayout.ButtonW(muteLabel, 146f), Mathf.Max(0f, rect.width - 200f));
            bool  showMute = muteW >= 90f;

            float popW = showMute && onServer ? DialogLayout.ButtonW(popLabel, 78f) : 0f;
            // Offered only while something is unread, so it never competes with the buttons above for room.
            float markW = ChatCache.TotalUnread() > 0 && popW > 0f && rect.width - muteW - popW - 10f >= 300f
                ? DialogLayout.ButtonW(markLabel, 92f) : 0f;
            float buttonsW = showMute ? muteW + popW + (markW > 0f ? markW + 6f : 0f) + 18f : 0f;

            float y = DialogLayout.DrawTitle(rect, muted ? KmhText.Get("KMH_Comms_TitleMuted", "Communications  <color=#A9AFB9>(notifications muted)</color>") : KmhText.Get("KMH_Comms_Title", "Communications"),
                                             buttonsW + DialogLayout.LiveBadgeReserve);

            if (showMute && Widgets.ButtonText(new Rect(rect.width - muteW - 4f, 2f, muteW, 24f), muteLabel))
                ToggleMute();

            if (popW > 0f && _selected != MailView
                && Widgets.ButtonText(new Rect(rect.width - muteW - popW - 10f, 2f, popW, 24f), popLabel))
            {
                Dialog_KMHChatPopout.Open(_selected);
                Close();
            }

            if (markW > 0f && Widgets.ButtonText(new Rect(rect.width - muteW - popW - markW - 16f, 2f, markW, 24f), markLabel))
                ChatCache.MarkAllRead();

            // Drawn after the buttons and told what they took, so the badge stops beside them rather than under.
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, buttonsW);
            if (onServer) DrawDiscordStatus(new Rect(0f, y, rect.width, 24f));
            else DialogLayout.LabelTrunc(new Rect(0f, y, rect.width - 8f, 24f),
                "<color=#A9AFB9>" + KmhText.Get("KMH_Comms_OffServer", "Not on a server: Global Chat and messages across servers work from here. Join a server for its chat, mail and players.") + "</color>",
                TextAnchor.MiddleLeft);
            y += 26f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            // Proportional with a floor for split-screen; capped at 240 so a player row with its time still fits.
            float sideW = Mathf.Clamp(rect.width * 0.27f, 140f, 240f);
            float paneH = DialogLayout.BodyHeight(rect, y);
            Rect sidebar = new Rect(0f, y, sideW, paneH);
            Rect content = new Rect(sideW + 8f, y, rect.width - sideW - 8f, paneH);

            DrawSidebar(sidebar);

            // Cleared when no cross-server conversation is on screen, or the poll keeps re-marking one nobody sees.
            if (!ChatChannels.IsGlobalDm(_selected)) Features.Global.KmhGlobalDmClient.Showing("");

            if (_selected == MailView) _mail.Draw(content);
            // Held by Global, so it draws from there rather than from this server's chat cache.
            else if (ChatChannels.IsGlobalDm(_selected)) _globalDm.Draw(content, ChatChannels.GlobalDmPeer(_selected));
            else
            {
                ChatPanel panel = PanelFor(_selected);
                panel.Opacity = BackgroundOpacity;
                panel.Draw(content);
            }

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        // The glyph matches the one marking relayed messages, so the two read as the same system.
        private void DrawDiscordStatus(Rect r)
        {
            string me     = KmhSession.Me;
            bool   linked = LinkedAccountsCache.IsLinked(me);
            string glyph  = $"<color={UI.KmhTheme.Hex(UI.KmhTheme.DiscordSrc)}>◈</color>";

            if (linked)
            {
                // The display name only - never the Discord id; there is no player-facing reason to show it.
                string name = LinkedAccountsCache.DiscordNameFor(me);
                DialogLayout.LabelTrunc(new Rect(r.x, r.y, r.width - 8f, r.height),
                    string.IsNullOrEmpty(name)
                        ? $"{glyph} <color=#A9AFB9>Discord linked - relayed messages show this mark.</color>"
                        : $"{glyph} <color=#A9AFB9>Discord linked as</color> <b>{name}</b>  <color=#A9AFB9>· relayed messages show this mark.</color>",
                    TextAnchor.MiddleLeft);
                return;
            }

            const float btnW = 150f;
            DialogLayout.LabelTrunc(new Rect(r.x, r.y, Mathf.Max(0f, r.width - btnW - 12f), r.height),
                $"{glyph} <color=#A9AFB9>Discord not linked - link it to chat from Discord under your own name.</color>",
                TextAnchor.MiddleLeft);
            if (Widgets.ButtonText(new Rect(r.xMax - btnW, r.y, btnW, r.height - 2f), KmhText.Get("KMH_Comms_LinkDiscord", "Link Discord")))
                Features.LinkedAccounts.LinkedAccountsHandler.RequestCode();
        }

        private static void ToggleMute()
        {
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s == null) return;
            s.ChatNotificationsMuted = !s.ChatNotificationsMuted;
            KMHPatchMod.SaveSettings();
        }

        private void DrawSidebar(Rect rect)
        {
            UI.KmhGlass.Section(rect, BackgroundOpacity);
            List<Entry> entries = BuildEntries();

            const float rowH = 30f;
            Rect inner = rect.ContractedBy(4f);
            Rect view  = new Rect(0f, 0f, Mathf.Max(1f, inner.width - DialogLayout.ScrollbarReserveWidth),
                                  Mathf.Max(inner.height, entries.Count * rowH + 4f));
            Widgets.BeginScrollView(inner, ref _sideScroll, view);
            DialogLayout.VisibleRange(_sideScroll, inner.height, rowH, entries.Count, out int first, out int last);
            for (int i = first; i < last; i++)
                DrawEntry(new Rect(0f, i * rowH, view.width, rowH - 2f), entries[i]);
            Widgets.EndScrollView();
        }

        private List<Entry> BuildEntries()
        {
            string me = KmhSession.Me;
            var list = new List<Entry>();

            // Driven from here as well as the main menu: a player who went straight into a game never ran that surface.
            Features.Global.GlobalChatClient.EnsureRunning();

            // Labelled cross-server, and shown even while probing: a hidden tab reads as a missing feature.
            if (Features.Global.GlobalChatClient.Offered)
                list.Add(Entry.Channel(KmhText.Get("KMH_Comms_GlobalChat", "Global Chat") + Features.Global.GlobalChatClient.StatusSuffix,
                                       ChatChannels.Global, this)
                         .WithTip(KmhText.Get("KMH_Comms_GlobalChatTip", "Chat with players on every Colony Nexus server. Needs a linked Colony Nexus account.")));

            bool onServer = OnServer;
            if (onServer) list.Add(Entry.Channel(KmhText.Get("KMH_Comms_ServerChat", "Server Chat"), ChatChannels.Server, this));

            if (onServer && GuildCache.InGuild && GuildCache.Guild != null && !string.IsNullOrEmpty(GuildCache.Guild.Name))
            {
                string gc = ChatChannels.Guild(GuildCache.Guild.Name);
                EnsureRequested(gc);   // preload history so the unread count + first view are ready
                list.Add(Entry.Channel($"Guild · {KmhDisplayText.GuildName(GuildCache.Guild.Name)}", gc, this));
            }

            // Held by Global per GlobalUserId, so they survive changing server; labelled apart from server-local DMs.
            if (Features.Global.KmhGlobalDmClient.Linked)
            {
                Features.Global.KmhGlobalDmClient.PollIfDue();
                list.Add(Entry.Header(KmhText.Get("KMH_Comms_DirectAcross", "Direct · across servers")));
                bool listed = false;
                foreach (Features.Global.GlobalDmConversation c in Features.Global.KmhGlobalDmClient.Conversations)
                {
                    string ch = ChatChannels.GlobalDm(c.PlayerGlobalId);
                    listed |= ch == _selected;
                    list.Add(Entry.Channel("  " + c.Shown, ch, this, c.Unread));
                }
                // A conversation just opened from Friends has no messages yet, but it is the one on screen.
                if (!listed && ChatChannels.IsGlobalDm(_selected))
                    list.Add(Entry.Channel("  " + KmhText.Get("KMH_Comms_NewConversation", "New: {0}", _globalDm.PeerShown(ChatChannels.GlobalDmPeer(_selected))), _selected, this));
                else if (Features.Global.KmhGlobalDmClient.Conversations.Count == 0)
                    list.Add(Entry.Header(Features.Global.KmhGlobalDmClient.State == Features.Global.GlobalDmState.Unavailable
                        ? "  Colony Nexus Global unreachable" : "  no conversations yet"));
                list.Add(Entry.Button("  + Message a Colony Nexus player…", NewGlobalDm));
            }

            if (!onServer)
            {
                // Linking is what switches these on; everything below needs a server.
                if (!Features.Global.KmhGlobalDmClient.Linked)
                    list.Add(Entry.Button(KmhText.Get("KMH_Comms_LinkAccount", "Link your account…"), () => Dialogs.Dialog_KMHAccount.Open()));
                if (!Features.Global.GlobalChatClient.Offered)
                    list.Add(Entry.Header(KmhText.Get("KMH_Comms_NoGlobalChat", "This directory has no Global Chat")));
                return list;
            }

            list.Add(Entry.Header(KmhText.Get("KMH_Comms_Direct", "Direct Messages")));
            ChatCache.ChannelsInto(_channelBuf);
            foreach (string ch in _channelBuf)
            {
                if (!ChatChannels.IsDm(ch)) continue;
                string other = ChatChannels.OtherParty(ch, me);
                list.Add(Entry.Channel("  " + LinkedAccountsCache.Format(other), ch, this));
            }
            list.Add(Entry.Button("  + New DM…", NewDm));

            if (ChatModerationCache.Count > 0)
                list.Add(Entry.Button($"Blocked players ({ChatModerationCache.Count})", ShowBlocked));

            list.Add(Entry.Header(""));
            list.Add(new Entry("Mail", MailCache.Unread, _selected == MailView, () => _selected = MailView, false));

            AddPlayers(list, me);
            return list;
        }

        private readonly List<string> _channelBuf = new List<string>();
        private List<Entry> _players = new List<Entry>();
        private int _rosterVer = -1, _modVer = -1;
        private long _linkedTicks = -1;
        private string _playersFor = null;

        // The dot is a shape, not a colour: online and offline stay apart for anyone who cannot tell the tints.
        private void AddPlayers(List<Entry> list, string me)
        {
            ChatRosterCache.RequestIfStale();
            if (!ChatRosterCache.Known) return;

            // A full server is hundreds of rows: rebuilt only on change, compared per field so the check never allocates.
            long linked = LinkedAccountsCache.LastUpdatedUtc.Ticks;
            if (_rosterVer != ChatRosterCache.Version || _modVer != ChatModerationCache.Version
                || _linkedTicks != linked || !string.Equals(_playersFor, me, StringComparison.Ordinal))
            {
                _players = BuildPlayers(me);
                _rosterVer = ChatRosterCache.Version;
                _modVer = ChatModerationCache.Version;
                _linkedTicks = linked;
                _playersFor = me;
            }
            list.AddRange(_players);
        }

        private List<Entry> BuildPlayers(string me)
        {
            var rows = new List<Entry> { Entry.Header($"Players ({ChatRosterCache.Online.Count} online)") };
            foreach (string name in ChatRosterCache.Online)
                rows.Add(PlayerEntry(name, me, online: true));

            if (ChatRosterCache.Offline.Count == 0) return rows;
            rows.Add(Entry.Header("Offline"));
            foreach (string name in ChatRosterCache.Offline)
                rows.Add(PlayerEntry(name, me, online: false));
            return rows;
        }

        private Entry PlayerEntry(string name, string me, bool online)
        {
            string dot   = online ? "●" : "○";
            string shown = LinkedAccountsCache.Format(name);
            bool   self  = string.Equals(name, me, StringComparison.OrdinalIgnoreCase);

            string note = online ? ChatRosterCache.ActiveOf(name) : ChatRosterCache.LastSeenOf(name);
            // Said in words, not by hiding the row: the player still needs to see that this person is here.
            if (ChatModerationCache.BlockingEnabled && ChatModerationCache.IsBlocked(name))
                note = note.Length > 0 ? note + " · blocked" : "blocked";
            string tail = note.Length > 0 ? $" <color=#A9AFB9>· {note}</color>" : "";

            string label = self ? $"  {dot} {shown} <color=#A9AFB9>(you)</color>{tail}"
                                : online ? $"  {dot} {shown}{tail}"
                                         : $"  <color=#A9AFB9>{dot} {shown}</color>{tail}";
            return Entry.Button(label, () => PlayerMenu(name));
        }

        private void PlayerMenu(string name)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(KmhText.Get("KMH_PlayerMenu_Mention", "Mention in chat"), () => Mention(name)),
            };
            options.AddRange(KmhPlayerMenu.Options(name));
            Find.WindowStack.Add(new FloatMenu(options, KmhPlayerMenu.Title(new KmhPlayerMenu.Target { LocalName = name })));
        }

        // Writes the name into the message box rather than sending: the sentence is the player's to finish.
        private void Mention(string name)
        {
            if (_selected == MailView) _selected = ChatChannels.Server;
            ChatPanel panel = PanelFor(_selected);
            panel.InsertMention(name);
            panel.RequestFocus();
        }

        // Only the visible channel's panel, because a panel that is not drawn keeps whatever it last reported.
        protected override bool TypingInThisWindow
            => _selected != MailView && (ChatChannels.IsGlobalDm(_selected) ? _globalDm.InputFocused : PanelFor(_selected).InputFocused);

        private ChatPanel PanelFor(string channel)
        {
            if (!_panels.TryGetValue(channel, out ChatPanel p)) { p = new ChatPanel(channel); _panels[channel] = p; }
            return p;
        }

        private void Select(string channel)
        {
            _selected = channel;
            if (ChatChannels.IsGlobalDm(channel)) return;   // its own pane fetches on first draw
            EnsureRequested(channel);
            PanelFor(channel).RequestFocus();
        }

        private void EnsureRequested(string channel)
        {
            if (channel == MailView || !OnServer || _requested.Contains(channel)) return;
            _requested.Add(channel);
            ChatHandler.RequestSnapshot(channel);
        }

        private void NewDm()
        {
            Mail.Dialog_KMHPlayerPicker.Open(KmhText.Get("KMH_Comms_MessagePlayer", "Message a player"), u => Select(ChatChannels.Dm(KmhSession.Me, u)));
        }

        // Friends only: there is no directory of people to search, and acceptance is the recipient's privacy call.
        private void NewGlobalDm()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Features.Friends.FriendDto f in Features.Friends.KmhFriendsClient.Friends)
            {
                string id = f.PlayerGlobalId;
                options.Add(new FloatMenuOption(f.Shown, () => Select(ChatChannels.GlobalDm(id))));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption(KmhText.Get("KMH_Comms_OpenFriends", "Open Friends to add someone"), Dialogs.Dialog_KMHFriends.Open));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowBlocked()
        {
            var opts = new List<FloatMenuOption>();
            foreach (string u in ChatModerationCache.Blocked())
            {
                string who = u;
                opts.Add(new FloatMenuOption($"Unblock {LinkedAccountsCache.Format(who)}", () => ChatHandler.TryBlock(who, false)));
            }
            if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void DrawEntry(Rect rect, Entry e)
        {
            if (e.IsHeader)
            {
                Color c = GUI.color; GUI.color = DialogLayout.MutedColor;
                if (!string.IsNullOrEmpty(e.Label)) DialogLayout.LabelTrunc(rect.ContractedBy(4f), e.Label, TextAnchor.MiddleLeft);
                GUI.color = c;
                return;
            }
            if (e.Selected) Widgets.DrawHighlightSelected(rect);
            Widgets.DrawHighlightIfMouseover(rect);

            // The label still names the channel, so the stripe's colour is never the only signal.
            if (!string.IsNullOrEmpty(e.ChannelId))
                Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 3f, 3f, rect.height - 6f),
                                     UI.KmhTheme.ForChannel(e.ChannelId));

            // Unread keeps its count in parentheses as well as the accent, for the same reason.
            string txt = e.Unread > 0
                ? $"{e.Label}  <color={UI.KmhTheme.Hex(UI.KmhTheme.Accent)}>({e.Unread})</color>"
                : e.Label;
            DialogLayout.LabelTrunc(new Rect(rect.x + 8f, rect.y, rect.width - 12f, rect.height), txt, TextAnchor.MiddleLeft);
            if (!string.IsNullOrEmpty(e.Tip)) TooltipHandler.TipRegion(rect, e.Tip);
            if (Widgets.ButtonInvisible(rect)) e.OnClick?.Invoke();
        }

        private readonly struct Entry
        {
            public readonly string Label; public readonly int Unread; public readonly bool Selected; public readonly Action OnClick; public readonly bool IsHeader;
            public readonly string ChannelId;
            public readonly string Tip;
            public Entry(string label, int unread, bool selected, Action onClick, bool header, string channel = "", string tip = null)
            { Label = label; Unread = unread; Selected = selected; OnClick = onClick; IsHeader = header; ChannelId = channel ?? ""; Tip = tip; }

            public Entry WithTip(string tip) => new Entry(Label, Unread, Selected, OnClick, IsHeader, ChannelId, tip);

            public static Entry Header(string label) => new Entry(label, 0, false, null, true);
            public static Entry Button(string label, Action onClick) => new Entry(label, 0, false, onClick, false);
            public static Entry Channel(string label, string channel, Dialog_KMHComms host)
                => new Entry(label, ChatCache.Unread(channel), host._selected == channel, () => host.Select(channel), false, channel);

            // Global conversations keep their unread count on Global, not in the local chat cache.
            public static Entry Channel(string label, string channel, Dialog_KMHComms host, int unread)
                => new Entry(label, unread, host._selected == channel, () => host.Select(channel), false, channel);
        }
    }
}
