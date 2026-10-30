using KMHPatch.Features.Global;
using Verse;

namespace KMHPatch.Features.Account
{
    // Every gate stops nagging: asks once, only when actionable, never while the directory is unreachable.
    internal static class KmhAccountPrompt
    {
        private static bool _suppressedThisSession;
        private static bool _shown;

        public static void SuppressForSession() => _suppressedThisSession = true;

        public static void SuppressForever()
        {
            _suppressedThisSession = true;
            if (KMHPatchMod.Settings == null) return;
            KMHPatchMod.Settings.AskToLinkSteam = false;
            KMHPatchMod.Settings.Write();
        }

        public static bool ShouldOffer()
        {
            if (_shown || _suppressedThisSession) return false;
            if (KMHPatchMod.Settings?.AskToLinkSteam == false) return false;
            if (KmhAccountStore.HasStoredToken(GlobalHttp.BaseUrl)) return false;

            KmhGlobalCapabilities.EnsureProbed();
            return KmhGlobalCapabilities.Online && KmhGlobalCapabilities.PlayerLinking;
        }

        public static void OfferOnce()
        {
            if (!ShouldOffer()) return;
            _shown = true;
            if (Find.WindowStack == null) return;
            Find.WindowStack.Add(new Dialogs.Dialog_KMHLinkSteam());
        }

        // The account panel is always reachable by hand, so reopening it clears only the once-per-launch latch.
        public static void AllowAgainThisSession()
        {
            _shown = false;
            _suppressedThisSession = false;
        }

        internal static void ResetForTest()
        {
            _shown = false;
            _suppressedThisSession = false;
        }

    }
}
