namespace KMHPatch.UI
{
    // Applied where remote text ENTERS a cache, so every renderer is covered without each one remembering to.
    internal static class KmhDisplayText
    {
        // Look-alike brackets rather than stripping, which would eat a player's "<3".
        public static string Inert(string s)
            => string.IsNullOrEmpty(s) ? s : s.Replace('<', '‹').Replace('>', '›');

        public static string Inert(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (maxLen > 0 && s.Length > maxLen) s = s.Substring(0, maxLen) + "…";
            return Inert(s);
        }

        // For names: a line break would let a name start a line of its own that reads as the client's.
        // Bodies (mail, notices, reports) keep theirs, which is why Inert itself does not do this.
        public static string InertLine(string s, int maxLen) => Inert(OneLine(s), maxLen);

        // Every run of line breaks, Unicode's separators included, becomes one space.
        public static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            bool inBreak = false;
            foreach (char c in s)
            {
                bool brk = (c >= (char)10 && c <= (char)13) || c == (char)0x85 || c == (char)0x2028 || c == (char)0x2029;
                if (brk && inBreak) continue;
                sb.Append(brk ? ' ' : c);
                inBreak = brk;
            }
            return sb.ToString();
        }

        // Guild names are keys the server matches on, so caches keep them raw and only the drawn copy is made inert.
        public const int MaxGuildName = 80;

        public static string GuildName(string name) => Inert(name ?? "", MaxGuildName);

        // Written by a guild officer and drawn for every member; the editor keeps the raw text.
        public const int MaxMotd = 256;

        public static string Motd(string motd)
            => string.IsNullOrWhiteSpace(motd) ? "<color=#A9AFB9>(no message of the day)</color>" : Inert(motd, MaxMotd);
    }
}
