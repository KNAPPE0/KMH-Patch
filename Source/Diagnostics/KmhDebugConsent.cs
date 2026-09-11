using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Diagnostics
{
    // Nothing has been transmitted at the point this prompt appears.
    internal static class KmhDebugConsent
    {
        private static bool _open;

        // A window opened mid-join is closed again by the map transition before the player can answer it.
        internal static bool CanPrompt(ProgramState state, bool playUi, bool haveMap, bool windowStack)
            => windowStack && playUi && haveMap && state == ProgramState.Playing;

        // Driven from the main-thread pump: the hello arrives on the network thread, and the window stack is main-only.
        public static void PumpPrompt()
        {
            if (_open || !KmhDebugUplink.AwaitingConsent) return;
            if (!CanPrompt(Current.ProgramState, Find.UIRoot is RimWorld.UIRoot_Play, Find.CurrentMap != null,
                           Find.WindowStack != null)) return;

            _open = true;
            string server = string.IsNullOrEmpty(KmhDispatcher.ServerName) ? "This server" : $"'{KmhDispatcher.ServerName}'";

            Find.WindowStack.Add(new Dialog_MessageBox(
                $"{server} has asked to receive your KMH log to help diagnose a problem.\n\n" +
                "<b>Nothing has been sent.</b> Your log stays on your machine unless you agree.\n\n" +
                "Sharing sends KMH log lines only - never chat, saves, or hardware/system information. " +
                "You can stop at any time from the KMH tab under Advanced & tools.",
                "Share for this session", () => { KmhDebugUplink.GrantForSession(); _open = false; },
                "Not now",                () => { KmhDebugUplink.DeclineForSession(); _open = false; },
                "KMH log sharing requested")
            { closeOnClickedOutside = false });
        }

        // A disconnect while the prompt is up leaves it answering for a server that is gone; let the next one re-ask.
        public static void Reset() => _open = false;
    }
}
