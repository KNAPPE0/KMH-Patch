using KMHPatch.Features.Servers;

namespace KMHPatch.Features.Global
{
    internal static class SeenServersStoreBridge
    {
        public static bool IsFavorite(string serverId) => SeenServersStore.IsFavorite(serverId);

        // "Recent" is local history only, never something Global decides.
        public static bool IsRecent(string serverId) => SeenServersStore.IsKnown(serverId);

        public static void ToggleFavorite(string serverId, string name)
            => SeenServersStore.SetFavorite(serverId, name, !SeenServersStore.IsFavorite(serverId));

        public static int AdoptNames(System.Collections.Generic.IList<ServerCardDto> cards)
        {
            if (cards == null || cards.Count == 0) return 0;
            var fresh = new System.Collections.Generic.List<SeenServersStore.Entry>(cards.Count);
            foreach (ServerCardDto c in cards)
            {
                if (c == null || string.IsNullOrEmpty(c.ServerId)) continue;
                fresh.Add(new SeenServersStore.Entry
                {
                    ServerId   = c.ServerId,
                    Name       = c.Name ?? "",
                    Endpoint   = c.JoinEndpoint ?? "",
                    KmhVersion = c.KmhVersion ?? "",
                    RwtVersion = c.RwtVersion ?? "",
                });
            }
            return SeenServersStore.RefreshFromDirectory(fresh);
        }
    }
}
