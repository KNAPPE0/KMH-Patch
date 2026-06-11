using KMHPatch.Diagnostics;
using RimWorld;
using Verse;

namespace KMHPatch.Notifications
{
    // Cross-cutting helper for surfacing KMH events to the player.
    //
    // Two channels exist in RimWorld for this:
    //   - Messages.Message(...)         : short flash at the top of the screen, fades
    //   - Find.LetterStack.ReceiveLetter: persistent letter in the side bar, click for detail
    //
    // Use Flash for transient confirmations (deposit succeeded, sale completed). Use Letter for things the player
    // needs to actually act on or read carefully (quest accepted, marketplace listing expired, KMH server config
    // changed)
    //
    // Every notification also gets mirrored to KmhLog so server-driven events have a written record even if the
    // player dismissed the on-screen flash
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
