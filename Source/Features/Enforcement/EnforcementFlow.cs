using System;
using GameClient.Managers;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Shows one-time consent for enforced config changes; the profile only applies after the player clicks Apply now.
    internal static class EnforcementFlow
    {
        public static string PendingHash { get; private set; } = "";    // "" = nothing awaiting consent
        public static bool   IsApplyPending => !string.IsNullOrEmpty(PendingHash);

        private static bool     _awaitingApply;     // player consented; pull/apply in flight
        private static DateTime _applyRequestedUtc;
        private static string   _reassertedHash;    // already quiet-re-asserted this connection for this hash
        private static bool     _dialogOpen;

        public static void ResetForNewConnection()
        {
            PendingHash = ""; _awaitingApply = false; _reassertedHash = "";
        }

        // Called after the cache is updated with a fresh snapshot.
        public static void Evaluate()
        {
            bool enforcedForMe = EnforcementCache.Enabled && !(EnforcementCache.AdminBypass && EnforcementCache.IsAdmin);
            if (!enforcedForMe || !EnforcementCache.HasProfile) { PendingHash = ""; _awaitingApply = false; return; }

            string serverHash = EnforcementCache.ServerProfileHash ?? "";

            // Already on the server hash; re-assert once per connection as drift insurance, without restart.
            if (EnforcementProfileApplier.IsApplied && Eq(EnforcementProfileApplier.AppliedHash, serverHash))
            {
                PendingHash = ""; _awaitingApply = false;
                if (!Eq(_reassertedHash, serverHash))
                {
                    _reassertedHash = serverHash;
                    KmhDispatcher.Send(KmhProtocol.Kind.EnforcementProfileRequest, null);
                }
                return;
            }

            // A new / changed profile needs applying - gate it behind explicit consent.
            if (!Eq(PendingHash, serverHash)) { PendingHash = serverHash; _awaitingApply = false; }   // server changed it
            if (_awaitingApply && (DateTime.UtcNow - _applyRequestedUtc) > TimeSpan.FromSeconds(60)) _awaitingApply = false; // apply stalled
            if (!_awaitingApply) ShowConsentDialog();
        }

        public static void Clear() { PendingHash = ""; _awaitingApply = false; }

        // Player consented: pull the profile. The existing receive path applies it and then PROMPTS a restart.
        public static void ApplyNow()
        {
            if (!IsApplyPending) return;
            _awaitingApply = true;
            _applyRequestedUtc = DateTime.UtcNow;
            KmhLog.Info("Enforcement: player consented - pulling the server config profile.");
            KmhDispatcher.Send(KmhProtocol.Kind.EnforcementProfileRequest, null);
        }

        // Player declined: leave the server (configs untouched).
        public static void DeclineAndDisconnect()
        {
            KmhLog.Info("Enforcement: player declined config enforcement - disconnecting.");
            Clear();
            try { DisconnectionManager.DisconnectToMenu(); }
            catch (Exception ex) { KmhLog.Warn($"Enforcement: disconnect failed: {ex.Message}"); }
        }

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
