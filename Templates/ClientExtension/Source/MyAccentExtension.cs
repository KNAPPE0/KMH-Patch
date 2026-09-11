using System;
using KMH.Sdk.Client;
using KMH.Sdk.Client.Apis;
using KMH.Sdk.Client.Events;

namespace MyAccent
{
    // Template: a toast on each new marketplace listing. The loader requires a parameterless constructor.
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

            // The first snapshot is a baseline, not a change.
            if (_lastMyListingCount < 0)
            {
                _lastMyListingCount = mine;
                return;
            }

            if (mine > _lastMyListingCount)
            {
                _host.Toast.Positive($"Nice - you now have {mine} active listing{(mine == 1 ? "" : "s")} on the market!");
                _host.Log.Info($"Marketplace updated: {_lastMyListingCount} -> {mine} of my listings.");
            }
            _lastMyListingCount = mine;
        }
    }
}
