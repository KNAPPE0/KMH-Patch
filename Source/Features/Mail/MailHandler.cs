using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Mail.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Mail
{
    // The server sets the sender from the authenticated session, so only the recipient and text are sent.
    internal static class MailHandler
    {
        public static void Register() => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.MailSnapshot, OnSnapshot);

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.MailRequest, null);

        public static bool TrySend(string to, string subject, string body, long attachSilver = 0,
                                   Dictionary<string, int> attachItems = null, Dictionary<string, int> attachPayloadSel = null)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.MailSend, new
            {
                to                 = to ?? "",
                subject            = subject ?? "",
                body               = body ?? "",
                attach_silver      = attachSilver < 0 ? 0 : attachSilver,
                attach_items       = attachItems ?? new Dictionary<string, int>(),
                attach_payload_sel = attachPayloadSel ?? new Dictionary<string, int>(),
            }, KmhOpId.For($"mail.send|{to}|{subject}|{body}|{attachSilver}"));
            if (sent) KmhNotifications.Neutral("Sending mail…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryAccept(long id)
        {
            bool sent = id > 0 && KmhDispatcher.Send(KmhProtocol.Kind.MailAccept, new { id });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryDecline(long id)
        {
            bool sent = id > 0 && KmhDispatcher.Send(KmhProtocol.Kind.MailDecline, new { id });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        // Sender pulls their own still-unread attachment back out of escrow.
        public static bool TryRecall(long id)
        {
            bool sent = id > 0 && KmhDispatcher.Send(KmhProtocol.Kind.MailRecall, new { id });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        public static bool MarkRead(long id) => id > 0 && KmhDispatcher.Send(KmhProtocol.Kind.MailMarkRead, new { id });

        public static bool Delete(long id)
        {
            bool sent = id > 0 && KmhDispatcher.Send(KmhProtocol.Kind.MailDelete, new { id });
            if (sent) KmhNotifications.Positive("Deleting…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            MailSnapshot snap = env?.DataAs<MailSnapshot>();
            if (snap == null) { KmhLog.Warn("Mail snapshot had no parseable payload, ignoring"); return; }
            MailCache.Apply(snap);
        }
    }
}
