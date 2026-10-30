using System.Collections.Generic;
using System.Text;
using KMHPatch.Features.OfflineNotices.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;
using RimWorld;
using Verse;

namespace KMHPatch.Features.OfflineNotices
{
    internal static class NotificationHandler
    {
        public static void Register() => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.NotifyQueued, OnQueued);

        private static void OnQueued(KmhEnvelope env)
        {
            NotificationBatch batch = env?.DataAs<NotificationBatch>();
            if (batch?.Notifications == null || batch.Notifications.Count == 0) return;

            // A server too old to send ids cannot be acked either, so those keep the original show-and-forget path.
            var identified = new List<NotificationDto>();
            var legacy     = new List<NotificationDto>();
            foreach (NotificationDto n in batch.Notifications)
            {
                if (n == null) continue;
                (string.IsNullOrEmpty(n.Id) ? legacy : identified).Add(n);
            }

            if (identified.Count > 0)
            {
                // Stored before the ack, because the server drops it once acked. No write, no ack, so it re-sends.
                if (PendingNoticeStore.Accept(identified, out List<NotificationDto> stored))
                {
                    var ids = new List<string>(identified.Count);
                    foreach (NotificationDto n in identified) ids.Add(n.Id);
                    KmhDispatcher.Send(KmhProtocol.Kind.NotifyAck, new { ids });
                    legacy.AddRange(stored);
                }
                else Diagnostics.KmhLog.Warn("Pending notices: not acknowledging - they could not be stored, so the server keeps them.");
            }

            if (legacy.Count == 0) return;
            Display(legacy, null);
        }

        // Anything stored but never displayed, replayed on the next load - the crash window between ack and letter.
        internal static void ShowStoredPending()
        {
            List<NotificationDto> pending = PendingNoticeStore.TakePending();
            // Replayed after a restart, perhaps on another server, so no server name is put on them.
            if (pending.Count > 0) Display(pending, UI.KmhText.Get("KMH_Notice_StoredSource", "Server notice"));
        }

        private static void Display(List<NotificationDto> notices, string server)
        {
            // Letters + extension callbacks touch UI/game state - run on the main thread once load has finished.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                foreach (NotificationDto n in notices)
                    if (n != null)
                        Extensibility.KmhClientEventBus.Instance.RaiseNotificationReceived(
                            new KMH.Sdk.Client.Events.KmhNotificationReceivedEvent { Title = n.Title ?? "", Body = n.Body ?? "", Tone = n.Tone ?? "", UtcTicks = n.UtcTicks });
                Show(notices, server);
                PendingNoticeStore.MarkShown(notices);
            });
        }

        private static void Show(List<NotificationDto> notices, string server)
        {
            if (notices.Count == 1)
            {
                NotificationDto n = notices[0];
                KmhNotifications.ServerLetter(Title(n), Body(n), ToneToDef(n.Tone), server);
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (NotificationDto n in notices)
            {
                sb.Append("• ");
                string t = Title(n);
                if (!string.IsNullOrEmpty(t)) sb.Append(t).Append(": ");
                sb.AppendLine(Body(n));
            }
            KmhNotifications.ServerLetter($"While you were away ({notices.Count})", sb.ToString().TrimEnd(), LetterDefOf.NeutralEvent, server);
        }

        // Notices carry other players' words, a mail subject among them, so they are drawn inert and capped.
        internal static string Title(NotificationDto n) => string.IsNullOrEmpty(n.Title) ? "Notice" : UI.KmhDisplayText.Inert(n.Title, 80);
        internal static string Body(NotificationDto n) => UI.KmhDisplayText.Inert(n.Body ?? "", 1000);

        private static LetterDef ToneToDef(string tone)
        {
            switch (tone)
            {
                case "positive": return LetterDefOf.PositiveEvent;
                case "negative": return LetterDefOf.NegativeEvent;
                default:         return LetterDefOf.NeutralEvent;
            }
        }
    }
}
