using System.Collections.Generic;
using System.Text;
using KMHPatch.Features.OfflineMail.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;
using RimWorld;
using Verse;

namespace KMHPatch.Features.OfflineMail
{
    // Shows queued offline notices as persistent letters, collating many into one "while you were away" letter.
    internal static class NotificationHandler
    {
        public static void Register() => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.NotifyQueued, OnQueued);

        private static void OnQueued(KmhEnvelope env)
        {
            NotificationBatch batch = env?.DataAs<NotificationBatch>();
            if (batch?.Notifications == null || batch.Notifications.Count == 0) return;
            List<NotificationDto> notices = batch.Notifications;
            // Letters touch UI/game state - run on the main thread once load has finished.
            LongEventHandler.ExecuteWhenFinished(() => Show(notices));
        }

        private static void Show(List<NotificationDto> notices)
        {
            if (notices.Count == 1)
            {
                NotificationDto n = notices[0];
                KmhNotifications.Letter(Title(n), n.Body, ToneToDef(n.Tone));
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (NotificationDto n in notices)
            {
                sb.Append("• ");
                string t = Title(n);
                if (!string.IsNullOrEmpty(t)) sb.Append(t).Append(": ");
                sb.AppendLine(n.Body);
            }
            KmhNotifications.Letter($"While you were away ({notices.Count})", sb.ToString().TrimEnd(), LetterDefOf.NeutralEvent);
        }

        private static string Title(NotificationDto n) => string.IsNullOrEmpty(n.Title) ? "Notice" : n.Title;

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
