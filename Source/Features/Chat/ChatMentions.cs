using System;
using System.Collections.Generic;

namespace KMHPatch.Features.Chat
{
    // Pure so the rules can be checked headless: getting this wrong is a missed message or a pointless notification.
    internal static class ChatMentions
    {
        // A Discord line can carry @everyone, which must never become a way to notify a whole server from outside it.
        private static readonly string[] Reserved = { "everyone", "here", "all", "channel" };

        internal static bool IsReserved(string name)
        {
            foreach (string r in Reserved)
                if (string.Equals(name, r, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The shape a KMH account can have, so a trailing comma ends the mention rather than joining the name.
        internal static bool IsNameChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '-';

        // Every @name in the body, reserved words excluded. Duplicates are kept out; order follows the text.
        internal static List<string> Find(string body)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(body)) return found;

            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] != '@') continue;
                // Mid-word @ is an address or a handle someone typed, not a mention.
                if (i > 0 && IsNameChar(body[i - 1])) continue;

                int start = i + 1, end = start;
                while (end < body.Length && IsNameChar(body[end])) end++;
                if (end == start) continue;

                string name = body.Substring(start, end - start);
                i = end - 1;
                if (IsReserved(name)) continue;

                bool have = false;
                foreach (string f in found)
                    if (string.Equals(f, name, StringComparison.OrdinalIgnoreCase)) { have = true; break; }
                if (!have) found.Add(name);
            }
            return found;
        }

        public static bool MentionsMe(string body, string me)
        {
            if (string.IsNullOrEmpty(me)) return false;
            foreach (string name in Find(body))
                if (string.Equals(name, me, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The name being typed at the end of the box, or null. "@" alone counts: the roster is the point of the list.
        internal static string PartialAt(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;

            int at = input.LastIndexOf('@');
            if (at < 0) return null;
            if (at > 0 && IsNameChar(input[at - 1])) return null;

            for (int i = at + 1; i < input.Length; i++)
                if (!IsNameChar(input[i])) return null;   // the mention already ended; nothing is being typed
            return input.Substring(at + 1);
        }

        // Replaces the partial being typed with the chosen name, leaving everything before it alone.
        internal static string Complete(string input, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return input ?? "";
            string text = input ?? "";
            int at = text.LastIndexOf('@');
            if (PartialAt(text) == null || at < 0) return Insert(text, name);
            return text.Substring(0, at) + "@" + name + " ";
        }

        // What the input box should hold after clicking a name in the roster.
        internal static string Insert(string input, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return input ?? "";
            string text = input ?? "";
            if (text.Length > 0 && !char.IsWhiteSpace(text[text.Length - 1])) text += " ";
            return text + "@" + name + " ";
        }
    }
}
