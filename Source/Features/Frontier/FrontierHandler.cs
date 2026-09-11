using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Frontier.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Frontier
{
    // Runs on the network thread, so the world lookup is marshalled: Find.WorldGrid and TileFinder are Verse state.
    internal static class FrontierHandler
    {
        public static void Register()
            => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.FrontierPlacementRequest, OnPlacementRequest);

        public static bool Available => KmhCapabilities.Has(KmhCapabilities.Frontier);

        private static void OnPlacementRequest(KmhEnvelope env)
        {
            PlacementRequest req = env?.DataAs<PlacementRequest>();
            if (req == null || string.IsNullOrEmpty(req.TokenId)) return;

            KmhMainThread.Post(() =>
            {
                var exclude = new HashSet<int>(req.ExcludeTiles ?? new List<int>());
                if (!KmhPlacementFinder.TryFindCandidate(req.Nonce, exclude, out int tile, out string why))
                { KmhPlacementFinder.LogGaveUp(why); return; }

                string fingerprint = KmhPlacementFinder.WorldFingerprint();
                if (string.IsNullOrEmpty(fingerprint)) return;

                KmhDispatcher.Send(KmhProtocol.Kind.FrontierPlacementPropose, new
                {
                    token_id = req.TokenId,
                    tile,
                    layer = 0,
                    fingerprint,
                });
            });
        }
    }
}
