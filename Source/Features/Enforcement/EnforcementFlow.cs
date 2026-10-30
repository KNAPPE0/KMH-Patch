using System;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // A profile never applies without the player's explicit consent, given twice: to the server, then to its files.
    internal static class EnforcementFlow
    {
        public static string PendingHash { get; private set; } = "";
        public static bool   IsApplyPending => !string.IsNullOrEmpty(PendingHash);

        private static bool     _awaitingApply;
        private static DateTime _applyRequestedUtc;
        private static string   _reassertedHash;
        private static bool     _dialogOpen;
        // The fingerprint of the mod list the consent dialog showed when the player chose Apply.
        private static string   _consentedOrder = "";
        // The hash and mod list whose file-by-file review the player accepted this connection.
        private static string   _reviewedHash = "";
        private static string   _reviewedOrder = "";
        private static bool     _reviewOpen;
        internal static Func<string, bool> ReviewForTest;

        public static void ResetForNewConnection()
        {
            PendingHash = ""; _awaitingApply = false; _reassertedHash = ""; _reviewedHash = ""; _reviewOpen = false;
            _consentedOrder = ""; _reviewedOrder = "";
            EnforcementProfileApplier.ResetReceive();
        }

        public static void Evaluate()
        {
            bool enforcedForMe = EnforcementCache.Enabled && !(EnforcementCache.AdminBypass && EnforcementCache.IsAdmin);
            if (!enforcedForMe || !EnforcementCache.HasProfile) { PendingHash = ""; _awaitingApply = false; return; }

            string serverHash = EnforcementCache.ServerProfileHash ?? "";

            // Already applied: re-assert once per connection as drift insurance, without a restart.
            if (AppliedAsConsented(serverHash))
            {
                PendingHash = ""; _awaitingApply = false;
                if (!Eq(_reassertedHash, serverHash))
                {
                    _reassertedHash = serverHash;
                    KmhDispatcher.Send(KmhProtocol.Kind.EnforcementProfileRequest, null);
                }
                return;
            }

            if (!Eq(PendingHash, serverHash)) { PendingHash = serverHash; _awaitingApply = false; }
            // A list other than the one the player agreed to is asked about again, never applied on that consent.
            if (_awaitingApply && !Eq(_consentedOrder, EnforcementCache.ModOrderFingerprint)) _awaitingApply = false;
            if (_awaitingApply && !_reviewOpen && (DateTime.UtcNow - _applyRequestedUtc) > TimeSpan.FromSeconds(60)) _awaitingApply = false;
            if (!_awaitingApply) ShowConsentDialog();
        }

        public static void Clear() { PendingHash = ""; _awaitingApply = false; }

        internal static bool MayApply(string hash)
            => ConsentedNow(hash) || AppliedAsConsented(hash);

        // Agreed to in this connection, as opposed to applied in an earlier one and only re-asserted; the list must be the one shown.
        internal static bool ConsentedNow(string hash)
            => _awaitingApply && Eq(PendingHash, hash) && Eq(_consentedOrder, EnforcementCache.ModOrderFingerprint);

        internal static bool Reviewed(string hash)
            => !string.IsNullOrEmpty(hash) && Eq(_reviewedHash, hash) && Eq(_reviewedOrder, EnforcementCache.ModOrderFingerprint);

        // What ApplyProfile may write: the list consented to now, or the one already applied for this server.
        internal static bool OrderAgreed(string orderFingerprint)
            => (_awaitingApply && Eq(_consentedOrder, orderFingerprint))
            || (EnforcementProfileApplier.IsApplied && EnforcementProfileApplier.AppliedServerId == KmhDispatcher.ServerId
                && Eq(EnforcementProfileApplier.AppliedOrderHash, orderFingerprint));

        // The second step of consent: the files themselves, read from the unpacked profile before anything is written.
        internal static void ShowReview(string hash, System.Collections.Generic.List<EnforcementProfileApplier.ReviewItem> items,
                                        Mods.KmhModPlan plan, string orderFingerprint, Action apply)
        {
            // A declined review in a test applies nothing; it never disconnects the game the test runs in.
            if (ReviewForTest != null)
            {
                if (ReviewForTest(hash)) { _reviewedHash = hash; _reviewedOrder = orderFingerprint ?? ""; apply(); }
                return;
            }
            _reviewOpen = true;
            _applyRequestedUtc = DateTime.UtcNow;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    Find.WindowStack.Add(new Dialog_KMHEnforcementReview(items, plan, ok =>
                    {
                        _reviewOpen = false;
                        if (!ok) { DeclineAndDisconnect(); return; }
                        _reviewedHash = hash;
                        _reviewedOrder = orderFingerprint ?? "";
                        _applyRequestedUtc = DateTime.UtcNow;
                        apply();
                    }));
                }
                catch (Exception ex) { _reviewOpen = false; KmhLog.Warn($"Enforcement: the settings review could not be shown: {ex.Message}"); }
            });
        }

        // Consent covered the settings and the mod order together; a server that changes either asks again.
        private static bool AppliedAsConsented(string hash)
            => EnforcementProfileApplier.IsApplied && Eq(EnforcementProfileApplier.AppliedHash, hash)
            && EnforcementProfileApplier.AppliedServerId == KmhDispatcher.ServerId
            && Eq(EnforcementProfileApplier.AppliedOrderHash, EnforcementCache.ModOrderFingerprint);

        // The receive path applies the profile and then prompts for a restart. shownOrder: the fingerprint of the list the player saw.
        public static void ApplyNow(string shownOrder)
        {
            if (!IsApplyPending) return;
            _awaitingApply = true;
            _consentedOrder = shownOrder ?? "";
            _applyRequestedUtc = DateTime.UtcNow;
            KmhLog.Info("Enforcement: player consented - pulling the server config profile.");
            KmhDispatcher.Send(KmhProtocol.Kind.EnforcementProfileRequest, null);
        }

        public static void DeclineAndDisconnect()
        {
            if (EnforcementAdmission.Pending) EnforcementAdmission.Fail("Player declined config enforcement.");
            KmhLog.Info("Enforcement: player declined config enforcement - disconnecting.");
            Clear();
            if (!RwtCompat.DisconnectToMainMenu())
                KmhLog.Warn("Enforcement: RWT offered no way back to the menu - the player is still connected.");
        }

        // Apply from elsewhere goes through the consent dialog, so the mod list is always seen before it is agreed to.
        internal static void ShowConsent() => ShowConsentDialog();

        private static void ShowConsentDialog()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    if (_dialogOpen || !IsApplyPending || Find.WindowStack == null) return;
                    if (Find.WindowStack.IsOpen(typeof(Dialog_KMHEnforcementConsent))) { _dialogOpen = true; return; }
                    _dialogOpen = true;
                    Find.WindowStack.Add(new Dialog_KMHEnforcementConsent(() => _dialogOpen = false));
                }
                catch (Exception ex) { KmhLog.Warn($"Enforcement: consent dialog failed: {ex.Message}"); _dialogOpen = false; }
            });
        }

        private static bool Eq(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
