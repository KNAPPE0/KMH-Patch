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
    // A sidebar of channels plus Mail, each with an unread count, and a pane hosting whichever is selected.
    public class Dialog_KMHComms : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(900f, 640f);

        private const string MailView = "\0mail";   // sentinel; never a valid chat channel


        private string _selected = ChatChannels.Server;
        private string _viewing;   // what CommsFocus was last told, so a switch is one paired call
        private Vector2 _sideScroll;

        private readonly MailPanel _mail = new MailPanel();
        private readonly Dictionary<string, ChatPanel> _panels = new Dictionary<string, ChatPanel>(StringComparer.Ordinal);
        private readonly HashSet<string> _requested = new HashSet<string>(StringComparer.Ordinal);

        public Dialog_KMHComms()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            // Enter is the send key in chat, so it must not also be the window's accept-and-close key. Esc still closes.
            closeOnAccept           = false;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            EnableAutoRefresh(() => { ChatHandler.RequestSnapshot(); ChatHandler.RequestModeration(); MailHandler.RequestSnapshot(); });
            ChatCache.Updated           += MarkRefreshed;
            ChatModerationCache.Updated += MarkRefreshed;
            MailCache.Updated           += MarkRefreshed;
        }

        // openOnMail lands on the inbox, so "Mail" anywhere in the UI reaches this hub rather than a second window.
        public static void Open(bool openOnMail = false)
            => Find.WindowStack.Add(new Dialog_KMHComms { _selected = openOnMail ? MailView : ChatChannels.Server });

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
            CommsFocus.Switch(ref _viewing, _selected == MailView ? null : _selected);

            bool muted = KMHPatchMod.Settings != null && KMHPatchMod.Settings.ChatNotificationsMuted;
            float y = DialogLayout.DrawTitle(rect, muted ? "Communications  <color=grey>(notifications muted)</color>" : "Communications");

            // Plain text, no emoji: RimWorld's font atlas has no glyphs for them and they render as blank boxes.
            float muteW = Mathf.Clamp(rect.width - 200f, 0f, 146f);
            bool  showMute = muteW >= 90f;
            if (showMute && Widgets.ButtonText(new Rect(rect.width - muteW - 4f, 2f, muteW, 24f),
                                               muted ? "Muted - unmute" : "Notifications on"))
                ToggleMute();

            // Pops the channel into a window that does not pause the game; mail and moderation stay here.
            float popW = showMute && muteW >= 90f ? 78f : 0f;
            if (popW > 0f && _selected != MailView
                && Widgets.ButtonText(new Rect(rect.width - muteW - popW - 10f, 2f, popW, 24f), "Pop out"))
            {
                Dialog_KMHChatPopout.Open(_selected);
                Close();
            }

            // Offered only while something is unread, so it never competes with the buttons above for room.
            float markW = 0f;
            if (ChatCache.TotalUnread() > 0 && popW > 0f && rect.width - muteW - popW - 10f >= 300f)
            {
                markW = 92f;
                if (Widgets.ButtonText(new Rect(rect.width - muteW - popW - markW - 16f, 2f, markW, 24f), "Mark all read"))
                    ChatCache.MarkAllRead();
                markW += 6f;
            }

            // Drawn after the buttons and told what they took, so the badge stops beside them rather than under.
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, showMute ? muteW + popW + markW + 18f : 0f);
            DrawDiscordStatus(new Rect(0f, y, rect.width, 24f));
            y += 26f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            // Proportional with a readable floor, or a split-screen window leaves the chat log unusable.
            float sideW = Mathf.Clamp(rect.width * 0.24f, 140f, 210f);
            float paneH = DialogLayout.BodyHeight(rect, y);
            Rect sidebar = new Rect(0f, y, sideW, paneH);
            Rect content = new Rect(sideW + 8f, y, rect.width - sideW - 8f, paneH);

            DrawSidebar(sidebar);

            if (_selected == MailView) _mail.Draw(content);
            else                       PanelFor(_selected).Draw(content);

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
                        ? $"{glyph} <color=grey>Discord linked - relayed messages show this mark.</color>"
                        : $"{glyph} <color=grey>Discord linked as</color> <b>{name}</b>  <color=grey>· relayed messages show this mark.</color>",
                    TextAnchor.MiddleLeft);
                return;
            }

            const float btnW = 150f;
            DialogLayout.LabelTrunc(new Rect(r.x, r.y, Mathf.Max(0f, r.width - btnW - 12f), r.height),
                $"{glyph} <color=grey>Discord not linked - link it to chat from Discord under your own name.</color>",
                TextAnchor.MiddleLeft);
            if (Widgets.ButtonText(new Rect(r.xMax - btnW, r.y, btnW, r.height - 2f), "Link Discord"))
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
            Widgets.DrawMenuSection(rect);
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
            var list = new List<Entry>
            {
                Entry.Channel("Server Chat", ChatChannels.Server, this),
            };

            if (GuildCache.InGuild && GuildCache.Guild != null && !string.IsNullOrEmpty(GuildCache.Guild.Name))
            {
                string gc = ChatChannels.Guild(GuildCache.Guild.Name);
                EnsureRequested(gc);   // preload history so the unread count + first view are ready
                list.Add(Entry.Channel($"Guild · {GuildCache.Guild.Name}", gc, this));
            }

            list.Add(Entry.Header("Direct Messages"));
            foreach (string ch in ChatCache.Channels())
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

        private List<Entry> _players = new List<Entry>();
        private string _playersKey = "";

        // The dot is a shape, not a colour: online and offline stay apart for anyone who cannot tell the tints.
        private void AddPlayers(List<Entry> list, string me)
        {
            ChatRosterCache.RequestIfStale();
            if (!ChatRosterCache.Known) return;

            // A full server is hundreds of rows; rebuilding them per frame is hundreds of strings per frame.
            string key = $"{ChatRosterCache.Version}:{LinkedAccountsCache.LastUpdatedUtc.Ticks}:{ChatModerationCache.Version}:{me}";
            if (_playersKey != key)
            {
                _players = BuildPlayers(me);
                _playersKey = key;
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

            // Time played for whoever is here, how long ago for whoever is not.
            string note = online ? ChatRosterCache.ActiveOf(name) : ChatRosterCache.LastSeenOf(name);
            // Said in words, not by hiding the row: the player still needs to see that this person is here.
            if (ChatModerationCache.BlockingEnabled && ChatModerationCache.IsBlocked(name))
                note = note.Length > 0 ? note + " · blocked" : "blocked";
            string tail = note.Length > 0 ? $" <color=grey>· {note}</color>" : "";

            string label = self ? $"  {dot} {shown} <color=grey>(you)</color>{tail}"
                                : online ? $"  {dot} {shown}{tail}"
                                         : $"  <color=grey>{dot} {shown}</color>{tail}";
            return Entry.Button(label, () => Mention(name));
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
            => _selected != MailView && PanelFor(_selected).InputFocused;

        private ChatPanel PanelFor(string channel)
        {
            if (!_panels.TryGetValue(channel, out ChatPanel p)) { p = new ChatPanel(channel); _panels[channel] = p; }
            return p;
        }

        private void Select(string channel)
        {
            _selected = channel;
            EnsureRequested(channel);
            PanelFor(channel).RequestFocus();   // land in the message box, ready to type
        }

        private void EnsureRequested(string channel)
        {
            if (channel == MailView || _requested.Contains(channel)) return;
            _requested.Add(channel);
            ChatHandler.RequestSnapshot(channel);
        }

        private void NewDm()
        {
            Mail.Dialog_KMHPlayerPicker.Open("Message a player", u => Select(ChatChannels.Dm(KmhSession.Me, u)));
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
            if (Widgets.ButtonInvisible(rect)) e.OnClick?.Invoke();
        }

        private readonly struct Entry
        {
            public readonly string Label; public readonly int Unread; public readonly bool Selected; public readonly Action OnClick; public readonly bool IsHeader;
            public readonly string ChannelId;   // "" for headers/buttons; drives the sidebar's channel stripe colour
            public Entry(string label, int unread, bool selected, Action onClick, bool header, string channel = "")
            { Label = label; Unread = unread; Selected = selected; OnClick = onClick; IsHeader = header; ChannelId = channel ?? ""; }

            public static Entry Header(string label) => new Entry(label, 0, false, null, true);
            public static Entry Button(string label, Action onClick) => new Entry(label, 0, false, onClick, false);
            public static Entry Channel(string label, string channel, Dialog_KMHComms host)
                => new Entry(label, ChatCache.Unread(channel), host._selected == channel, () => host.Select(channel), false, channel);
        }
    }
}
