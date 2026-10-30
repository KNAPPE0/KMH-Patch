namespace KMHPatch.Features
{
    // The one definition of "the KMH tab has something waiting"; hand-kept sums elsewhere drift and miss channels.
    internal static class KmhUnread
    {
        public static int Total() => Comms() + FriendRequests();

        public static int Comms()
            => Chat.ChatCache.TotalUnread()
             + Mail.MailCache.Unread
             + Global.KmhGlobalDmClient.UnreadTotal;

        public static int FriendRequests() => Friends.KmhFriendsClient.Incoming.Count;
    }
}
