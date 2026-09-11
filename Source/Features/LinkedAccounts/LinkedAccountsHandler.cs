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

        // Ask the server to mint a fresh Discord link code - replaces typing /kmh link.
        public static bool RequestCode()
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.LinkRequest, null);
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

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
            int mins = env.GetInt("ttl_minutes", 0);
            string ttl = mins > 0 ? $" (expires in {mins} min, one use)" : "";
            // The clipboard and the window stack are main-thread only, and a throw here would only be logged.
            KmhMainThread.Post(() =>
            {
                try { GUIUtility.systemCopyBuffer = code; } catch { }
                Find.WindowStack.Add(new Dialog_MessageBox(
                    $"Your Discord link code:\n\n<b>{code}</b>{ttl}\n\nCopied to your clipboard. In Discord, post in an allowed channel or DM the bot:\n!kmh-link {code}"));
            });
        }
    }
}
