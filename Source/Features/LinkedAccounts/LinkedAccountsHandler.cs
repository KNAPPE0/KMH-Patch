using KMHPatch.Diagnostics;
using KMHPatch.Features.LinkedAccounts.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.LinkedAccounts
{
    internal static class LinkedAccountsHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.LinkedAccountsSnapshot, OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.LinkCode,               OnLinkCode);
        }

        public static bool RequestSnapshot()
        {
            return KmhDispatcher.Send(KmhProtocol.Kind.LinkedAccountsRequest, null);
        }

        // A code is only shown in answer to the player's own request, so a server cannot push text or a clipboard at will.
        private static System.DateTime _requestedUtc = System.DateTime.MinValue;
        internal static readonly System.TimeSpan AnswerWindow = System.TimeSpan.FromMinutes(2);

        public static bool RequestCode()
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.LinkRequest, null);
            if (!sent) KmhNotifications.NotConnected();
            else _requestedUtc = System.DateTime.UtcNow;
            return sent;
        }

        internal static bool AcceptCode(string code, System.DateTime nowUtc)
        {
            if (nowUtc - _requestedUtc > AnswerWindow || !WellFormed(code)) return false;
            _requestedUtc = System.DateTime.MinValue;
            return true;
        }

        internal static bool WellFormed(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 4 || code.Length > 16) return false;
            foreach (char c in code) if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
            return true;
        }

        internal static void ArmForTest(System.DateTime requestedUtc) => _requestedUtc = requestedUtc;

        private static void OnSnapshot(KmhEnvelope env)
        {
            LinkedAccountsSnapshot snapshot = env?.DataAs<LinkedAccountsSnapshot>();
            if (snapshot == null)
            {
                KmhLog.Warn("LinkedAccounts snapshot envelope had no parseable payload, ignoring");
                return;
            }
            LinkedAccountsCache.Apply(snapshot);
        }

        private static void OnLinkCode(KmhEnvelope env)
        {
            string code = env?.GetString("code");
            if (string.IsNullOrEmpty(code)) { KmhNotifications.Rejected("Server did not return a link code"); return; }
            if (!AcceptCode(code, System.DateTime.UtcNow))
            { KmhLog.Warn("LinkedAccounts: ignored a link code that was not asked for or is not a link code."); return; }
            int mins = env.GetInt("ttl_minutes", 0);
            string ttl = mins > 0 && mins <= 1440 ? $" (expires in {mins} min, one use)" : "";
            // The clipboard and the window stack are main-thread only, and a throw here would only be logged.
            KmhMainThread.Post(() =>
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    $"Your Discord link code:\n\n<b>{code}</b>{ttl}\n\nIn Discord, post in an allowed channel or DM the bot:\n!kmh-link {code}",
                    "Copy code", () => { try { GUIUtility.systemCopyBuffer = code; } catch { } }, "Close", null));
            });
        }
    }
}
