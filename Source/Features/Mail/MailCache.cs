using System;
using KMHPatch.Features.Mail.Dto;

namespace KMHPatch.Features.Mail
{
    public static class MailCache
    {
        public static MailSnapshot Snapshot       { get; private set; }
        public static DateTime     LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool         HasSnapshot    => Snapshot != null;
        public static int          Unread         => Snapshot?.Unread ?? 0;

        public static event Action Updated;

        internal static void Apply(MailSnapshot snapshot)
        {
            // Transports reorder; the disconnect clear is what lets a new server's lower revision still apply.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
            Snapshot       = MakeInert(snapshot);
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Mail");
        }

        // Widgets.Label renders rich text, so a subject or body carrying tags is neutralised once here, not at each label.
        private static MailSnapshot MakeInert(MailSnapshot s)
        {
            if (s == null) return null;
            Inert(s.Messages);
            Inert(s.Outgoing);
            return s;
        }

        private static void Inert(System.Collections.Generic.List<MailMessage> list)
        {
            if (list == null) return;
            foreach (MailMessage m in list)
            {
                if (m == null) continue;
                m.From    = UI.KmhDisplayText.Inert(m.From);
                m.To      = UI.KmhDisplayText.Inert(m.To);
                m.Subject = UI.KmhDisplayText.Inert(m.Subject);
                m.Body    = UI.KmhDisplayText.Inert(m.Body);
            }
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
