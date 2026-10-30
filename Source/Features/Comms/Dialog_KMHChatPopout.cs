using System;
using System.Collections.Generic;
using KMHPatch.Features.Chat;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Comms
{
    // Deliberately not a second hub: no sidebar, mail or pause, so it stays readable while the colony runs.
    public class Dialog_KMHChatPopout : Window_KMHBase
    {
        private const float MinW = KMHPatchSettings.ChatPopoutMinW, MinH = KMHPatchSettings.ChatPopoutMinH;

        private string _channel;
        private readonly Dictionary<string, ChatPanel> _panels = new Dictionary<string, ChatPanel>(StringComparer.Ordinal);
        private readonly HashSet<string> _requested = new HashSet<string>(StringComparer.Ordinal);
        private bool _placed;

        public override Vector2 InitialSize => SizeWithin(SavedW, SavedH, MinW, MinH);

        private static float SavedW => KMHPatchMod.Settings?.ChatPopoutW ?? KMHPatchSettings.ChatPopoutDefW;
        private static float SavedH => KMHPatchMod.Settings?.ChatPopoutH ?? KMHPatchSettings.ChatPopoutDefH;

        internal override float BackgroundOpacity => KMHPatchSettings.ChatOpacity(KMHPatchMod.Settings);

        public Dialog_KMHChatPopout(string channel)
        {
            _channel = string.IsNullOrEmpty(channel) ? ChatChannels.Server : channel;

            doCloseX                = true;
            draggable               = true;
            resizeable              = true;
            minSize                 = new Vector2(MinW, MinH);
            forcePause              = false;
            absorbInputAroundWindow = false;
            // Enter sends a message here, and Esc belongs to the game while a floating window is open.
            closeOnAccept           = false;
            closeOnCancel           = false;
            drawShadow              = false;

            EnableAutoRefresh(() => { ChatHandler.RequestSnapshot(); ChatHandler.RequestModeration(); });
            ChatCache.Updated += MarkRefreshed;
        }

        public static void Open(string channel = null)
        {
            Dialog_KMHChatPopout live = Find.WindowStack?.WindowOfType<Dialog_KMHChatPopout>();
            if (live != null)
            {
                if (!string.IsNullOrEmpty(channel)) live.Select(channel);
                Find.WindowStack.Notify_ManuallySetFocus(live);
                return;
            }
            Find.WindowStack?.Add(new Dialog_KMHChatPopout(channel ?? Remembered()));
        }

        // Re-checked against what the player can still see, or a left guild reopens on a channel nobody answers.
        private static string Remembered()
        {
            string want = KMHPatchMod.Settings?.ChatPopoutChannel;
            if (string.IsNullOrEmpty(want) || want == ChatChannels.Server) return ChatChannels.Server;
            return Channels().Contains(want) ? want : ChatChannels.Server;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            CommsFocus.Enter(_channel);
            EnsureRequested(_channel);
        }

        public override void PostOpen()
        {
            base.PostOpen();
            if (_placed) return;
            _placed = true;
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s != null) PlaceAt(s.ChatPopoutX, s.ChatPopoutY);
        }

        public override void PostClose()
        {
            base.PostClose();
            ChatCache.Updated -= MarkRefreshed;
            CommsFocus.Leave(_channel);
            RememberPlacement();

            ChatVideoPlayer.StopIfNobodyWatching();
        }

        private void ResetToDefault()
        {
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s != null)
            {
                s.ChatPopoutX = s.ChatPopoutY = -1f;
                s.ChatPopoutW = KMHPatchSettings.ChatPopoutDefW;
                s.ChatPopoutH = KMHPatchSettings.ChatPopoutDefH;
                KMHPatchMod.SaveSettings();
            }
            CentreAt(KMHPatchSettings.ChatPopoutDefW, KMHPatchSettings.ChatPopoutDefH);
        }

        private void RememberPlacement()
        {
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s == null) return;
            s.ChatPopoutX = windowRect.x;
            s.ChatPopoutY = windowRect.y;
            s.ChatPopoutW = Mathf.Max(MinW, windowRect.width);
            s.ChatPopoutH = Mathf.Max(MinH, windowRect.height);
            s.ChatPopoutChannel = _channel ?? "";
            KMHPatchMod.SaveSettings();
        }

        protected override void DrawContents(Rect rect)
        {
            float y = 0f;

            string defaultLabel = KmhText.Get("KMH_Chat_Default", "Default");
            float defaultW = DialogLayout.ButtonW(defaultLabel, 60f, 130f);
            const float glassW = 52f;
            float pickW = Mathf.Min(rect.width - defaultW - 96f - glassW - 4f, 260f);
            int elsewhere = UnreadElsewhere();
            string picker = LabelFor(_channel) + (elsewhere > 0 ? $"  ({elsewhere})" : "");
            // IconButton truncates with the full name on hover; a centred label would cut a long name at both edges.
            if (IconButton.Draw(new Rect(0f, y, Mathf.Max(80f, pickW), 24f), null, picker))
                ShowChannelMenu();

            // One click per step, so the map can show through without a trip to the mod settings.
            Rect glassR = new Rect(rect.width - 88f - defaultW - glassW - 4f, y, glassW, 24f);
            KMHPatchSettings st = KMHPatchMod.Settings;
            if (Widgets.ButtonText(glassR, Mathf.RoundToInt(KMHPatchSettings.ChatOpacity(st) * 100f) + "%") && st != null)
            {
                st.ChatWindowOpacity = KMHPatchSettings.NextChatOpacity(st.ChatWindowOpacity);
                KMHPatchMod.SaveSettings();
            }
            TooltipHandler.TipRegion(glassR, KmhText.Get("KMH_Chat_OpacityTip",
                "How solid the chat windows are. Click to let more of the map show through; the text stays readable."));

            Rect defaultR = new Rect(rect.width - 88f - defaultW, y, defaultW, 24f);
            if (Widgets.ButtonText(defaultR, defaultLabel)) ResetToDefault();
            TooltipHandler.TipRegion(defaultR, "Put this window back to its default size and position.");

            Rect hubR = new Rect(rect.width - 84f, y, 60f, 24f);
            if (Widgets.ButtonText(hubR, "Hub"))
            {
                Dialog_KMHComms.Open();
                Close();
            }
            TooltipHandler.TipRegion(hubR, "Open the full Communications hub (mail, blocked players, Discord link).");

            y += 28f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            Rect body = new Rect(0f, y, rect.width, Mathf.Max(60f, rect.height - y));
            ChatPanel panel = Panel();
            panel.Opacity = BackgroundOpacity;
            panel.Draw(body);
        }

        // In ExtraOnGUI because it runs in screen space, so it sees the outside click the window itself never receives.
        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (Event.current == null || Event.current.type != EventType.MouseDown) return;
            if (windowRect.Contains(Event.current.mousePosition)) return;
            Verse.UI.UnfocusCurrentControl();
        }

        protected override bool TypingInThisWindow => Panel().InputFocused;

        // One per channel for the window's lifetime, so switching away and back keeps the draft and the scroll position.
        private ChatPanel Panel()
        {
            if (!_panels.TryGetValue(_channel, out ChatPanel p)) { p = NewPanel(_channel); _panels[_channel] = p; }
            return p;
        }

        // This window does not absorb input, so a self-focusing TextField would eat RimWorld's movement keys.
        private static ChatPanel NewPanel(string channel) => new ChatPanel(channel) { AutoFocus = false };

        private void Select(string channel)
        {
            if (string.IsNullOrEmpty(channel) || channel == _channel) return;
            CommsFocus.Switch(ref _channel, channel);
            EnsureRequested(_channel);
        }

        private int UnreadElsewhere()
        {
            int n = 0;
            foreach (string ch in Channels())
                if (ch != _channel) n += ChatCache.Unread(ch);
            return n;
        }

        private void EnsureRequested(string channel)
        {
            if (string.IsNullOrEmpty(channel) || _requested.Contains(channel)) return;
            _requested.Add(channel);
            ChatHandler.RequestSnapshot(channel);
        }

        private void ShowChannelMenu()
        {
            var opts = new List<FloatMenuOption>();
            foreach (string ch in Channels())
            {
                string target = ch;
                int unread = ChatCache.Unread(target);
                string label = LabelFor(target) + (unread > 0 ? $"  ({unread})" : "");
                opts.Add(new FloatMenuOption(label, () => Select(target)));
            }
            if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static List<string> Channels()
        {
            var list = new List<string> { ChatChannels.Server };
            if (GuildCache.InGuild && GuildCache.Guild != null && !string.IsNullOrEmpty(GuildCache.Guild.Name))
                list.Add(ChatChannels.Guild(GuildCache.Guild.Name));
            foreach (string ch in ChatCache.Channels())
                if (ChatChannels.IsDm(ch) && !list.Contains(ch)) list.Add(ch);
            return list;
        }

        private static string LabelFor(string channel)
        {
            if (ChatChannels.IsDm(channel)) return "DM · " + LinkedAccountsCache.Format(ChatChannels.OtherParty(channel, KmhSession.Me));
            if (ChatChannels.IsGuild(channel)) return "Guild · " + KmhDisplayText.GuildName(channel.Substring("guild:".Length));
            return "Server Chat";
        }
    }
}
