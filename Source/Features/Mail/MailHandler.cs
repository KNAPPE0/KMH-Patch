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
            }, KmhOpId.For(SendKey(to, subject, body, attachSilver, attachItems, attachPayloadSel)));
            if (sent) KmhNotifications.Neutral("Sending mail…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        // The goods are part of the action: without them two mails with different items shared one op id, and the
        // server dropped the second as a repeat.
        internal static string SendKey(string to, string subject, string body, long attachSilver,
                                       Dictionary<string, int> items, Dictionary<string, int> payloads)
            => $"mail.send|{to}|{subject}|{body}|{attachSilver}|{Sorted(items)}|{Sorted(payloads)}";

        private static string Sorted(Dictionary<string, int> d)
        {
            if (d == null || d.Count == 0) return "";
            var keys = new List<string>(d.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            foreach (string k in keys) sb.Append(k).Append('=').Append(d[k]).Append(',');
            return sb.ToString();
        }

        public static bool TryAccept(long id)  => SendForMessage(KmhProtocol.Kind.MailAccept, id);
        public static bool TryDecline(long id) => SendForMessage(KmhProtocol.Kind.MailDecline, id);
        public static bool TryRecall(long id)  => SendForMessage(KmhProtocol.Kind.MailRecall, id);

        private static bool SendForMessage(string kind, long id)
        {
            bool sent = id > 0 && KmhDispatcher.Send(kind, new { id });
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
