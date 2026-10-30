using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Chat
{
    // Asks the server once: it holds the bot token, and nothing about Discord's API reaches the client.
    internal static class ChatMediaRefreshClient
    {
        private enum State { None, Asking, Fresh, Gone }

        private sealed class Entry
        {
            public State  State;
            public string Url = "";
            public string Reason = "";
        }

        private static readonly Dictionary<string, Entry> _byRef = new Dictionary<string, Entry>(StringComparer.Ordinal);

        // A session's worth of expired media; past it a dead link simply says so instead of asking again.
        internal const int MaxRefs = 1024;

        public static void Register()
            => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ChatMediaRefreshed, OnRefreshed);

        public static string FreshUrlFor(string reference)
            => !string.IsNullOrEmpty(reference) && _byRef.TryGetValue(reference, out Entry e) && e.State == State.Fresh
               ? e.Url : "";

        public static string GoneReasonFor(string reference)
            => !string.IsNullOrEmpty(reference) && _byRef.TryGetValue(reference, out Entry e) && e.State == State.Gone
               ? e.Reason : null;

        public static bool IsAsking(string reference)
            => !string.IsNullOrEmpty(reference) && _byRef.TryGetValue(reference, out Entry e) && e.State == State.Asking;

        // Once per reference per session. A dead link that cannot be refreshed must not turn into a request loop.
        public static void Request(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return;
            if (_byRef.ContainsKey(reference)) return;
            if (_byRef.Count >= MaxRefs) return;

            _byRef[reference] = new Entry { State = State.Asking };
            KmhDispatcher.Send(KmhProtocol.Kind.ChatMediaRefresh, new { @ref = reference });
            KmhLog.Debug($"Chat media: asking the server to refresh {reference}");
        }

        private static void OnRefreshed(KmhEnvelope env)
        {
            string reference = env?.GetString("ref") ?? "";
            if (string.IsNullOrEmpty(reference)) return;
            // Only an answer to this client's own question; anything else would grow the table at the server's will.
            if (!_byRef.TryGetValue(reference, out Entry e) || e.State != State.Asking) return;

            string url = env.GetString("url") ?? "";
            if (env.GetBool("ok") && url.Length > 0)
            {
                e.State = State.Fresh;
                e.Url = url;
                KmhLog.Debug($"Chat media: {reference} refreshed");
                return;
            }

            e.State = State.Gone;
            e.Reason = UI.KmhDisplayText.Inert(env.GetString("reason") ?? "no longer available", 160);
            KmhLog.Debug($"Chat media: {reference} cannot be refreshed - {e.Reason}");
        }

        public static void Clear() => _byRef.Clear();
    }
}
