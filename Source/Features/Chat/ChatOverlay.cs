using System;
using System.Collections.Generic;
using KMHPatch.Features.Chat.Dto;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Chat
{
    // Labels only, drawn before the window stack so clicks reach the map and windows cover it; DMs never show here.
    internal static class ChatOverlay
    {
        internal const int MaxLines = 5;
        internal static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(18), FadeFor = TimeSpan.FromSeconds(4);
        internal const float BottomBarH = 35f, Margin = 8f, Left = 10f, MinTop = 120f;
        // RimWorld's mouse-over readout (terrain, plants, light) grows up from just above the bottom bar.
        internal const float ReadoutRoom = 140f;

        private sealed class Entry
        {
            public ChatMessage Msg;
            public DateTime AtUtc;
            public string Text;
            public float Width = -1f, Height;
        }

        private static readonly List<Entry> _recent = new List<Entry>();
        private static readonly List<Entry> _shown = new List<Entry>();
        private static readonly List<Rect> _windows = new List<Rect>();

        public static void Register() => ChatCache.MessageArrived += OnArrived;

        internal static bool Shows(string channel)
            => ChatChannels.IsServerLocal(channel) || ChatChannels.IsGuild(channel) || ChatChannels.IsGlobal(channel);

        // History loads never reach here; Global Chat can arrive off the main thread, hence the lock.
        private static void OnArrived(ChatMessage m)
        {
            if (m == null || !Shows(m.Channel)) return;
            lock (_recent)
            {
                _recent.Add(new Entry { Msg = m, AtUtc = DateTime.UtcNow });
                if (_recent.Count > MaxLines) _recent.RemoveAt(0);
            }
        }

        internal static float Alpha(TimeSpan age)
            => age < ShowFor ? 1f : 1f - (float)((age - ShowFor).TotalSeconds / FadeFor.TotalSeconds);

        // Lifted above overlapping windows: the Architect menu's description pane floats there and would cut the overlay.
        internal static float BottomFor(float screenH, float left, float width, float height, List<Rect> windows)
        {
            float bottom = screenH - BottomBarH - ReadoutRoom;
            for (int pass = 0; pass < 8; pass++)
            {
                bool moved = false;
                foreach (Rect w in windows)
                    if (w.xMin < left + width && w.xMax > left && w.yMin < bottom && w.yMax > bottom - height)
                    {
                        bottom = w.yMin - Margin;
                        moved = true;
                    }
                if (!moved) break;
            }
            return bottom;
        }

        internal const int MaxChars = 180;

        // One line per message: a block pasted from Discord would fill the whole column, line break by line break.
        internal static string OneLine(string body, int max)
        {
            if (string.IsNullOrEmpty(body)) return body ?? "";
            var sb = new System.Text.StringBuilder(Math.Min(body.Length, max) + 4);
            bool gap = false, brk = false;
            foreach (char ch in body)
            {
                if (ch == '\r' || ch == '\n') { brk = true; continue; }
                if (ch == ' ' || ch == '\t') { gap = true; continue; }
                if (sb.Length > 0 && (brk || gap)) sb.Append(brk ? " / " : " ");
                brk = gap = false;
                if (sb.Length >= max) { sb.Append('…'); break; }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static ChatMessage Flattened(ChatMessage m) => new ChatMessage
        {
            Id = m.Id, Channel = m.Channel, FromUsername = m.FromUsername, Body = OneLine(m.Body, MaxChars),
            SentUtcTicks = m.SentUtcTicks, Origin = m.Origin, SenderVerified = m.SenderVerified,
            ImageUrl = m.ImageUrl, IsVideo = m.IsVideo, MediaId = m.MediaId, MediaRef = m.MediaRef,
        };

        // The channel is named in a fixed colour: no name or body can make a server line read as Global.
        internal static string TextFor(ChatMessage m)
            => $"<color=#A9AFB9>{KmhCommsNotifications.Label(m.Channel)}</color>" + ChatPanel.Line(Flattened(m)).Replace("<b>", "").Replace("</b>", "");

        // Stamped by every frame the overlay could draw, so a toast is not also raised for a line it is showing.
        private static long _drawableTicks;

        internal static bool WillShow(string channel)
            => KMHPatchMod.Settings?.ChatOverlay == true && Shows(channel)
            && DateTime.UtcNow.Ticks - System.Threading.Interlocked.Read(ref _drawableTicks) < TimeSpan.TicksPerSecond;

        internal static void Draw()
        {
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s == null || !s.ChatOverlay || Event.current == null || Event.current.type != EventType.Repaint) return;
            if (Current.ProgramState != ProgramState.Playing || Find.WindowStack == null) return;
            if (Find.UIRoot?.screenshotMode?.FiltersCurrentEvent == true) return;
            if (Find.WindowStack.IsOpen<Comms.Dialog_KMHChatPopout>() || Find.WindowStack.IsOpen<Comms.Dialog_KMHComms>()) return;

            DateTime now = DateTime.UtcNow;
            System.Threading.Interlocked.Exchange(ref _drawableTicks, now.Ticks);
            _shown.Clear();
            lock (_recent)
                foreach (Entry e in _recent)
                    if (Alpha(now - e.AtUtc) > 0f && !ChatModerationCache.IsBlocked(e.Msg.FromUsername)) _shown.Add(e);
            if (_shown.Count == 0) return;

            float width = Mathf.Min(460f, Verse.UI.screenWidth * 0.32f), textW = width - 12f;

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            try
            {
                Text.Font = ChatPanel.LogFont;
                Text.Anchor = TextAnchor.UpperLeft;
                float total = 0f;
                foreach (Entry e in _shown)
                {
                    // Bold draws wider than it measures on 1.7 and leaves wrapped rows blank, so the overlay goes without.
                    if (e.Text == null) e.Text = TextFor(e.Msg);
                    if (e.Width != textW) { e.Width = textW; e.Height = Text.CalcHeight(e.Text, textW); }
                    total += e.Height + 6f;
                }
                _windows.Clear();
                foreach (Window w in Find.WindowStack.Windows) _windows.Add(w.windowRect);
                float y = BottomFor(Verse.UI.screenHeight, Left, width, total, _windows);

                for (int i = _shown.Count - 1; i >= 0; i--)
                {
                    Entry e = _shown[i];
                    float h = e.Height + 4f;
                    if (y - h < MinTop) break;
                    y -= h;
                    float a = Mathf.Clamp01(Alpha(now - e.AtUtc));
                    Rect row = new Rect(Left, y, width, h);
                    Widgets.DrawBoxSolid(row, new Color(0f, 0f, 0f, 0.42f * a));
                    GUI.color = new Color(1f, 1f, 1f, a);
                    Widgets.Label(new Rect(row.x + 6f, row.y + 2f, textW, e.Height), e.Text);
                    y -= 2f;
                }
            }
            finally
            {
                Text.Font = font;
                Text.Anchor = anchor;
                GUI.color = color;
            }
        }

        internal static void ClearForTest() { lock (_recent) _recent.Clear(); }

        internal static void AddForTest(string channel, string from, string body)
            => OnArrived(new ChatMessage { Id = DateTime.UtcNow.Ticks, Channel = channel, FromUsername = from, Body = body, SentUtcTicks = DateTime.UtcNow.Ticks });
    }
}
