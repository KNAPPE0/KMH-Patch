using System;

namespace KMHPatch.Features.Discord
{
    public enum KmhActivity
    {
        None = 0,
        Marketplace,
        Auctions,
        WantBoard,
        Quests,
        Sites,
        Frontier,
        Treasury,
        Guild,
    }

    // Privacy: a fixed vocabulary - no amounts, item or pawn names, players, server or guild names, or coordinates.
    internal static class KmhPresenceActivity
    {
        // Survives briefly closing a screen, yet a status left behind by an alt-tab expires on its own.
        internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

        private static KmhActivity _current;
        private static DateTime _expiresUtc = DateTime.MinValue;

        internal static bool Enabled => KMHPatchMod.SettingsOrDefault.DiscordShowActivity;

        // Called every frame: the same activity only moves the deadline; a change invalidates so it pushes now.
        public static void Note(KmhActivity activity)
        {
            if (activity == KmhActivity.None || !Enabled) return;

            bool changed = activity != _current || Expired;
            _current = activity;
            _expiresUtc = DateTime.UtcNow.Add(Lifetime);
            if (changed) KmhRichPresence.Invalidate();
        }

        private static bool Expired => DateTime.UtcNow >= _expiresUtc;

        public static KmhActivity Current
        {
            get
            {
                if (!Enabled || Expired) return KmhActivity.None;
                return _current;
            }
        }

        public static void Clear()
        {
            bool had = _current != KmhActivity.None;
            _current = KmhActivity.None;
            _expiresUtc = DateTime.MinValue;
            if (had) KmhRichPresence.Invalidate();
        }

        // Generic by design: two players doing the same thing read identically, whatever they trade or where.
        public static string Line(KmhActivity activity)
        {
            switch (activity)
            {
                case KmhActivity.Marketplace: return UI.KmhText.Get("KMH_Presence_ActMarketplace", "At the marketplace");
                case KmhActivity.Auctions:    return UI.KmhText.Get("KMH_Presence_ActAuctions", "At the auction house");
                case KmhActivity.WantBoard:   return UI.KmhText.Get("KMH_Presence_ActWantBoard", "At the want board");
                case KmhActivity.Quests:      return UI.KmhText.Get("KMH_Presence_ActQuests", "Taking on a quest");
                case KmhActivity.Sites:       return UI.KmhText.Get("KMH_Presence_ActSites", "Building a site");
                case KmhActivity.Frontier:    return UI.KmhText.Get("KMH_Presence_ActFrontier", "Out on the frontier");
                case KmhActivity.Treasury:    return UI.KmhText.Get("KMH_Presence_ActTreasury", "At the treasury");
                case KmhActivity.Guild:       return UI.KmhText.Get("KMH_Presence_ActGuild", "At the guild hall");
                default:                      return "";
            }
        }

        internal static void ResetForTest()
        {
            _current = KmhActivity.None;
            _expiresUtc = DateTime.MinValue;
        }

        internal static void SetForTest(KmhActivity activity, DateTime expiresUtc)
        {
            _current = activity;
            _expiresUtc = expiresUtc;
        }
    }
}
