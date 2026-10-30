using System;
using System.Net;
using System.Net.Sockets;

namespace KMHPatch.SubProtocol
{
    // A server's advertised API address is its word; it may name another public host, but not this player's own network.
    internal static class KmhDialPolicy
    {
        internal static Func<string, IPAddress[]> ResolveForTest;

        internal static bool MayDial(string rwtHost, string apiHost, out string why)
        {
            why = "";
            if (string.IsNullOrEmpty(apiHost) || string.Equals(apiHost, rwtHost, StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress[] api = Resolve(apiHost), rwt = Resolve(rwtHost);
            if (Array.Exists(rwt, IsLocal)) return true;
            if (Array.Exists(api, IsLocal)) { why = "it is a private or local address and the server is not"; return false; }
            return true;
        }

        private static IPAddress[] Resolve(string host)
        {
            if (ResolveForTest != null) return ResolveForTest(host) ?? new IPAddress[0];
            if (string.IsNullOrEmpty(host)) return new IPAddress[0];
            if (IPAddress.TryParse(host, out IPAddress literal)) return new[] { literal };
            try { return Dns.GetHostAddresses(host); }
            catch (Exception) { return new IPAddress[0]; }
        }

        internal static bool IsLocal(IPAddress a)
        {
            if (a == null) return false;
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any)) return true;
            if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] v6 = a.GetAddressBytes();
                return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || (v6[0] & 0xFE) == 0xFC;
            }
            byte[] b = a.GetAddressBytes();
            return b[0] == 10 || b[0] == 127 || b[0] == 0
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }
    }
}
