using System;
using KMH.Sdk.Client;
using KMH.Sdk.Client.Events;

namespace EarningsTracker
{
    // Template: everything runs through the host, so an extension needs no Verse types. Copy the folder and rename.
    public sealed class EarningsTrackerExtension : IKmhClientExtension
    {
        public string Name    => "Earnings Tracker";
        public string Version => "1.0.0";

        private IKmhClientHost _host;
        private long _sessionSilver;
        private int  _sessionItems;

        public void Register(IKmhClientHost host)
        {
            _host = host;

            host.Events.KmhServerConnected   += OnConnected;
            host.Events.GrantReceived        += OnGrant;
            host.Events.WorldEventFired      += OnWorldEvent;
            host.Events.NotificationReceived += OnNotice;

            host.Log.Info("Ready - tracking KMH payouts this session.");
        }

        private void OnConnected(KmhServerConnectedEvent e)
        {
            _sessionSilver = 0;
            _sessionItems  = 0;
            _host.Log.Info($"Connected as {_host.LocalUsername}. Treasury snapshot loaded: {_host.Treasury.HasSnapshot}.");
        }

        private void OnGrant(KmhGrantReceivedEvent e)
        {
            if (e.Kind == "silver") _sessionSilver += e.Silver;
            else                    _sessionItems  += Math.Max(1, e.Quantity);

            string what = e.Kind == "silver" ? $"{e.Silver} silver" : $"{e.Quantity}x {e.ItemDefName}";
            _host.Log.Info($"Payout: {what}. Session total: {_sessionSilver} silver, {_sessionItems} item(s).");
        }

        private void OnWorldEvent(KmhWorldEventFiredEvent e)
        {
            // React to economic events - nudge the player to trade during a favourable window.
            if (e.Type == "tax_holiday" || e.Type == "market_boom")
                _host.Toast.Positive($"{e.Title} - good time to sell!");
        }

        private void OnNotice(KmhNotificationReceivedEvent e)
            => _host.Log.Info($"Offline notice [{e.Tone}] {e.Title}: {e.Body}");

        public void Shutdown()
            => _host?.Log.Info($"Session earnings: {_sessionSilver} silver, {_sessionItems} item(s).");
    }
}
