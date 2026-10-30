using System.Collections.Generic;
using KMHPatch.Features.Global;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Comms
{
    // Not a ChatPanel: these messages come from Global and must keep working when this server is gone.
    internal sealed class GlobalDmPanel
    {
        private Vector2 _scroll;
        private string _draft = "";
        private string _openFor = "";
        private readonly List<float> _heights = new List<float>();

        public void Draw(Rect rect, string peerId)
        {
            // Latched only once the fetch is dispatched, so a click that loses the race to a background poll retries.
            if (_openFor != peerId && KmhGlobalDmClient.OpenConversation(peerId))
            {
                _openFor = peerId;
                _draft = "";
            }
            // Told every frame so the poll refreshes what is on screen, not only on selection.
            KmhGlobalDmClient.Showing(peerId);

            if (!KmhGlobalDmClient.Linked)
            {
                Widgets.Label(rect, KmhText.Get("KMH_Dm_NotLinked",
                    "Link your Steam account to Colony Nexus Global to send messages across servers."));
                return;
            }

            float headH = DialogLayout.TextRowH + 6f;
            DialogLayout.LabelTrunc(new Rect(rect.x, rect.y, rect.width, headH),
                $"<b>{KmhText.Get("KMH_Dm_With", "Messages with {0}", PeerName(peerId))}</b>  <color=#A9AFB9>{KmhText.Get("KMH_Dm_AcrossServers", "across every Colony Nexus server")}</color>",
                TextAnchor.MiddleLeft);
            rect = new Rect(rect.x, rect.y + headH, rect.width, Mathf.Max(0f, rect.height - headH));

            float InputH = 32f + StatusH(peerId);
            Rect listRect = new Rect(rect.x, rect.y, rect.width, rect.height - InputH - 6f);
            List<GlobalDmMessage> messages = KmhGlobalDmClient.History(peerId);

            if (KmhGlobalDmClient.State == GlobalDmState.Unavailable)
            {
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, 44f), KmhText.Get("KMH_Dm_Offline",
                    "Colony Nexus Global cannot be reached. Messages already sent are safe and will appear when it is back. "
                  + "Server and guild chat are unaffected."));
                listRect = new Rect(rect.x, rect.y + 48f, rect.width, rect.height - 48f - InputH - 6f);
            }
            else if (messages.Count == 0 && KmhGlobalDmClient.State == GlobalDmState.Loading)
            {
                Widgets.Label(listRect, KmhText.Get("KMH_Dm_Loading", "Asking Colony Nexus Global..."));
                DrawInput(rect, peerId, InputH);
                return;
            }

            DrawMessages(listRect, peerId, messages);
            DrawInput(rect, peerId, InputH);
        }

        private void DrawMessages(Rect rect, string peerId, List<GlobalDmMessage> messages)
        {
            float more = KmhGlobalDmClient.HasMore(peerId) ? 28f : 0f;
            float height = more + 4f;
            // Measured at the width each message is drawn at, or wrapped lines outgrow the view and the last is cut.
            float viewW = rect.width - 18f;
            _heights.Clear();
            foreach (GlobalDmMessage m in messages)
            {
                float mh = Text.CalcHeight(m.Shown, viewW - 24f);
                _heights.Add(mh);
                height += mh + 6f;
            }

            Rect view = new Rect(0f, 0f, viewW, Mathf.Max(height, rect.height));
            Widgets.BeginScrollView(rect, ref _scroll, view);

            float y = 0f;
            if (more > 0f)
            {
                if (Widgets.ButtonText(new Rect(0f, 0f, view.width, 24f),
                                       KmhText.Get("KMH_Dm_Older", "Load older messages")) && !KmhGlobalDmClient.Busy)
                    KmhGlobalDmClient.LoadOlder(peerId);
                y = more;
            }

            if (messages.Count == 0)
                Widgets.Label(new Rect(0f, y, view.width, 24f),
                    KmhText.Get("KMH_Dm_Empty", "No messages yet. Say something."));

            for (int i = 0; i < messages.Count; i++)
            {
                GlobalDmMessage m = messages[i];
                float h = _heights[i];
                GUI.color = m.Mine ? new Color(0.78f, 0.86f, 0.95f) : Color.white;
                Widgets.Label(new Rect(12f, y, view.width - 24f, h), m.Shown);
                GUI.color = Color.white;
                y += h + 6f;
            }
            Widgets.EndScrollView();
        }

        internal const string FieldName = "kmhGlobalDm";
        private string _sentDraft;

        public bool InputFocused { get; private set; }

        private static float StatusH(string peerId)
            => KmhGlobalDmClient.LastSendPeer == peerId && KmhGlobalDmClient.LastSend != KmhGlobalDmClient.SendStage.Idle
               && KmhGlobalDmClient.LastSend != KmhGlobalDmClient.SendStage.Sent ? DialogLayout.TextRowH + 2f : 0f;

        internal string PeerShown(string peerId) => PeerName(peerId);

        private static string PeerName(string peerId)
        {
            foreach (GlobalDmConversation c in KmhGlobalDmClient.Conversations) if (c.PlayerGlobalId == peerId) return c.Shown;
            foreach (List<Friends.FriendDto> rows in new[] { Friends.KmhFriendsClient.Friends, Friends.KmhFriendsClient.Incoming, Friends.KmhFriendsClient.Outgoing })
                foreach (Friends.FriendDto f in rows) if (f.PlayerGlobalId == peerId) return f.Shown;
            return KmhText.Get("KMH_Dm_SomePlayer", "a Colony Nexus player");
        }

        private void DrawInput(Rect rect, string peerId, float inputH)
        {
            Rect row = new Rect(rect.x, rect.yMax - inputH, rect.width, inputH);
            bool may = KmhGlobalDmClient.MayReply(peerId);
            InputFocused = false;

            if (!may)
            {
                // Greyed rather than hidden: a box that silently refuses is worse than one that says why.
                Widgets.Label(row, KmhText.Get("KMH_Dm_CannotReply",
                    "This player is not accepting messages from you."));
                return;
            }

            // What happened to the last message, above the box: kept there until Global took it.
            float statusH = StatusH(peerId);
            if (statusH > 0f)
            {
                bool bad = KmhGlobalDmClient.LastSend != KmhGlobalDmClient.SendStage.Sending;
                GUI.color = bad ? KmhBrand.Bad : DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(row.x, row.y, row.width, statusH),
                    bad ? KmhGlobalDmClient.LastSendNote : KmhText.Get("KMH_Dm_Sending", "Sending…"), TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                row = new Rect(row.x, row.y + statusH, row.width, row.height - statusH);
            }

            if (KmhGlobalDmClient.LastSend == KmhGlobalDmClient.SendStage.Sent && KmhGlobalDmClient.LastSendPeer == peerId
                && _sentDraft != null && _draft == _sentDraft) { _draft = ""; _sentDraft = null; }

            Event e = Event.current;
            bool enter = e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                      && GUI.GetNameOfFocusedControl() == FieldName;

            Rect box = new Rect(row.x, row.y, row.width - 86f, row.height);
            GUI.SetNextControlName(FieldName);
            string typed = Widgets.TextField(box, _draft);
            if (typed != _draft) { _draft = typed; KmhGlobalDmClient.ClearSend(); }
            InputFocused = GUI.GetNameOfFocusedControl() == FieldName;

            bool sending = KmhGlobalDmClient.LastSend == KmhGlobalDmClient.SendStage.Sending;
            bool can = !KmhGlobalDmClient.Busy && !sending && _draft.Trim().Length > 0;
            bool clicked = Widgets.ButtonText(new Rect(row.xMax - 82f, row.y, 82f, row.height),
                                              KmhText.Get("KMH_Dm_Send", "Send"), active: can);
            if (enter) e.Use();
            if ((clicked || enter) && can)
            {
                if (KmhGlobalDmClient.Send(peerId, _draft)) _sentDraft = _draft;
                else Notifications.KmhNotifications.Rejected(KmhText.Get("KMH_Dm_Busy", "Still busy with the last message. Try again in a moment."));
            }
        }
    }
}
