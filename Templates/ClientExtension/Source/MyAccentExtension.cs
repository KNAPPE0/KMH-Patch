using System;
using KMH.Sdk.Client;
using KMH.Sdk.Client.Apis;
using KMH.Sdk.Client.Events;

namespace MyAccent
{
    // Example KMH client-side extension. Shows a green toast whenever
    // the player posts a new marketplace listing, with their current
    // listing count.
    //
    // What this demonstrates:
    //   1. Class implementing IKmhClientExtension with a parameterless ctor.
    //   2. Capturing the host so handlers reach it later.
    //   3. Subscribing to a cache-updated event.
    //   4. Reading the local username via host.LocalUsername.
    //   5. Iterating SDK record types (no internal DTOs).
    //   6. Using INotifications.Positive for a toast.
    //   7. Logging through IClientLog (auto-prefixed [ext:My KMH Accent]).
    public sealed class MyAccentExtension : IKmhClientExtension
    {
        public string Name    => "My KMH Accent";
        public string Version => "1.0.0";

        private IKmhClientHost _host;
        private int _lastMyListingCount = -1;

        public void Register(IKmhClientHost host)
        {
            _host = host;
            host.Events.MarketplaceCacheUpdated += OnMarketplaceCacheUpdated;
            host.Log.Info("Registered. Will cheer on new listings.");
        }

        public void Shutdown()
        {
            if (_host != null)
            {
                _host.Events.MarketplaceCacheUpdated -= OnMarketplaceCacheUpdated;
                _host = null;
            }
        }

        private void OnMarketplaceCacheUpdated(MarketplaceCacheUpdatedEvent e)
        {
            string me = _host.LocalUsername;
            if (string.IsNullOrEmpty(me)) return;

            int mine = 0;
            foreach (var l in _host.Marketplace.Listings)
            {
                if (string.Equals(l.SellerUsername, me, StringComparison.OrdinalIgnoreCase))
                    mine++;
            }

            // First snapshot we see - just record + skip.
            if (_lastMyListingCount < 0)
            {
                _lastMyListingCount = mine;
                return;
            }

            // Increase = we just posted (or someone else cancelled, etc.).
            if (mine > _lastMyListingCount)
            {
                _host.Toast.Positive($"Nice - you now have {mine} active listing{(mine == 1 ? "" : "s")} on the market!");
                _host.Log.Info($"Marketplace updated: {_lastMyListingCount} -> {mine} of my listings.");
            }
            _lastMyListingCount = mine;
        }
    }
}
