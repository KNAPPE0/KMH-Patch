using KMHPatch.Diagnostics;
using RimWorld;
using Verse;

namespace KMHPatch.Notifications
{
    // Surfaces KMH events: Flash for transient confirmations, Letter for things the player must read/act on.
    // Everything is mirrored to KmhLog so server-driven events have a record even if the flash was dismissed.
    public static class KmhNotifications
    {
        // Top-of-screen flash. Defaults to NeutralEvent - pass a different MessageTypeDef when the event has a
        // clear positive/negative tone (PositiveEvent, NegativeEvent, RejectInput, ThreatSmall, etc.)
        public static void Flash(string message, MessageTypeDef type = null)
        {
            if (string.IsNullOrEmpty(message)) return;

            string prefixed = $"[KMH] {message}";
            Messages.Message(prefixed, type ?? MessageTypeDefOf.NeutralEvent, historical: true);
            KmhLog.Info($"Flash: {message}");
        }

        // Flash for repeatable events (deposits, donations): the first shows normally; repeats of the same key inside
        // the window are summarized into ONE cumulative line ("4 deposits pending - save to finalize") instead of
        // stacking a message per click.
        private static readonly System.Collections.Generic.Dictionary<string, (System.DateTime last, int count)> _coalesce
            = new System.Collections.Generic.Dictionary<string, (System.DateTime, int)>();
        public static void FlashCoalesced(string key, string firstMessage, System.Func<int, string> summary,
                                          MessageTypeDef type = null, double windowSeconds = 8.0)
        {
            System.DateTime now = System.DateTime.UtcNow;
            if (!_coalesce.TryGetValue(key, out var s) || (now - s.last).TotalSeconds > windowSeconds)
            {
                _coalesce[key] = (now, 1);
                Flash(firstMessage, type);
                return;
            }
            _coalesce[key] = (now, s.count + 1);
            string text = summary?.Invoke(s.count + 1) ?? firstMessage;
            Messages.Message($"[KMH] {text}", type ?? MessageTypeDefOf.NeutralEvent, historical: false);
            KmhLog.Info($"Flash (coalesced x{s.count + 1}): {text}");
        }

        // Persistent letter that lands in the side bar. Player can click to read in full and dismiss when ready
        public static void Letter(string label, string body, LetterDef def = null)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(body)) return;
            if (Find.LetterStack == null)
            {
                // No active game - fall back to a flash so the message isn't lost.
                Flash($"{label}: {body}");
                return;
            }

            string prefixedLabel = $"[KMH] {label}";
            Find.LetterStack.ReceiveLetter(prefixedLabel, body, def ?? LetterDefOf.NeutralEvent);
            KmhLog.Info($"Letter: {label} - {body}");
        }

        // Convenience shortcuts so feature code doesn't have to import MessageTypeDefOf / LetterDefOf at every
        // callsite
        public static void Positive(string message) => Flash(message, MessageTypeDefOf.PositiveEvent);
        public static void Negative(string message) => Flash(message, MessageTypeDefOf.NegativeEvent);
        public static void Rejected(string message) => Flash(message, MessageTypeDefOf.RejectInput);
        public static void Neutral (string message) => Flash(message, MessageTypeDefOf.NeutralEvent);
    }
}
