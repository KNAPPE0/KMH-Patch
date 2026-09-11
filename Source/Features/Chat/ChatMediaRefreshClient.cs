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
            public string Url = "";      // the replacement, once there is one
            public string Reason = "";   // why there will never be one
        }

        private static readonly Dictionary<string, Entry> _byRef = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public static void Register()
            => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ChatMediaRefreshed, OnRefreshed);

        // A replacement url for this reference, or "" if there isn't one yet.
        public static string FreshUrlFor(string reference)
            => !string.IsNullOrEmpty(reference) && _byRef.TryGetValue(reference, out Entry e) && e.State == State.Fresh
               ? e.Url : "";

        // Asked and answered: the original is gone for good, so there is nothing to retry.
        public static string GoneReasonFor(string reference)
            => !string.IsNullOrEmpty(reference) && _byRef.TryGetValue(reference, out Entry e) && e.State == State.Gone
               ? e.Reason : null;

        public static bool IsAsking(string reference)
            => !string.IsNullOrEmpty(reference) && _byRef.TryGetValue(reference, out Entry e) && e.State == State.Asking;

        // Once per reference per session. A dead link that cannot be refreshed must not turn into a request loop.
        public static void Request(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return;
            if (_byRef.ContainsKey(reference)) return;   // asking, answered, or already known gone

            _byRef[reference] = new Entry { State = State.Asking };
            KmhDispatcher.Send(KmhProtocol.Kind.ChatMediaRefresh, new { @ref = reference });
            KmhLog.Debug($"Chat media: asking the server to refresh {reference}");
        }

        private static void OnRefreshed(KmhEnvelope env)
        {
            string reference = env?.GetString("ref") ?? "";
            if (string.IsNullOrEmpty(reference)) return;
            if (!_byRef.TryGetValue(reference, out Entry e)) { e = new Entry(); _byRef[reference] = e; }

            string url = env.GetString("url") ?? "";
            if (env.GetBool("ok") && url.Length > 0)
            {
                e.State = State.Fresh;
                e.Url = url;
                KmhLog.Debug($"Chat media: {reference} refreshed");
                return;
            }

            e.State = State.Gone;
            e.Reason = env.GetString("reason") ?? "no longer available";
            KmhLog.Debug($"Chat media: {reference} cannot be refreshed - {e.Reason}");
        }

        public static void Clear() => _byRef.Clear();
    }
}
