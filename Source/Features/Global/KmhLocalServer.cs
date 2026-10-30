using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace KMHPatch.Features.Global
{
    // Home routers often cannot loop their public address back, so a listed server on this PC is dialled locally;
    // the first hello must then name the server the player picked.
    internal static class KmhLocalServer
    {
        private static string _expectedId = "", _listed = "", _dialled = "";
        private static int _port;
        private static DateTime _atUtc = DateTime.MinValue;

        // A join is answered within minutes; an older expectation must not refuse a later, unrelated connection.
        internal static readonly TimeSpan HelloWindow = TimeSpan.FromMinutes(10);

        internal static Func<int, IPAddress> ListenerForTest;

        internal static string DialHost(string host, int port, string serverId, DateTime? nowUtc = null)
        {
            _expectedId = ""; _listed = ""; _dialled = ""; _port = 0;
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrEmpty(serverId) || IsLoopback(host)) return host;
            IPAddress bound = ListenerForTest != null ? ListenerForTest(port) : ListenerHere(port);
            if (bound == null) return host;

            string local = bound.Equals(IPAddress.Any) || bound.Equals(IPAddress.IPv6Any) || IPAddress.IsLoopback(bound)
                ? "127.0.0.1" : bound.ToString();
            _expectedId = serverId; _listed = $"{host.Trim()}:{port}"; _dialled = local; _port = port;
            _atUtc = nowUtc ?? DateTime.UtcNow;
            Diagnostics.KmhLog.Info($"Join: this PC is listening on port {port}, so {_listed} is joined through {local} "
                                  + "(a home router often cannot loop its own public address back).");
            return local;
        }

        internal static bool IsLoopback(string host)
        {
            string h = (host ?? "").Trim();
            if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(h, out IPAddress a) && IPAddress.IsLoopback(a);
        }

        // The OS listener table, never a probe: an unauthenticated socket on RWT's port can fail someone else's login.
        private static IPAddress ListenerHere(int port)
        {
            try
            {
                IPEndPoint hit = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().FirstOrDefault(e => e.Port == port);
                return hit?.Address;
            }
            catch (Exception ex)
            {
                Diagnostics.KmhLog.Debug($"Join: could not read this PC's listeners ({ex.GetType().Name}); dialling the listed address.");
                return null;
            }
        }

        // One-shot: the first hello after a rerouted join must come from the server the player picked.
        internal static bool HelloMatches(string helloServerId, DateTime? nowUtc = null)
        {
            if (_expectedId.Length == 0) return true;
            string expected = _expectedId;
            _expectedId = "";
            if ((nowUtc ?? DateTime.UtcNow) - _atUtc > HelloWindow) return true;
            return string.Equals(helloServerId ?? "", expected, StringComparison.OrdinalIgnoreCase);
        }

        // The listed address, not the local reroute, is "the address this game dialled" for join tickets and presence.
        internal static string ListedFor(string currentIp, int currentPort)
            => _listed.Length > 0 && currentPort == _port && string.Equals(currentIp ?? "", _dialled, StringComparison.OrdinalIgnoreCase)
               ? _listed : null;

        internal static bool IsRerouted(string currentIp, int currentPort) => ListedFor(currentIp, currentPort) != null;

        internal static void ResetForTest() { _expectedId = ""; _listed = ""; _dialled = ""; _port = 0; ListenerForTest = null; }
    }
}
