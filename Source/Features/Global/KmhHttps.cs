using System;
using System.Net.Http;
using System.Net.Security;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Global
{
    // Each client checks certificates itself, so another mod's process-wide "accept any certificate" cannot reach it.
    internal static class KmhHttps
    {
        internal static HttpClientHandler Handler(bool redirects = true)
        {
            HttpClientHandler h = new HttpClientHandler { AllowAutoRedirect = redirects };
            try { h.ServerCertificateCustomValidationCallback = Strict; }
            catch (Exception ex) when (ex is NotImplementedException || ex is PlatformNotSupportedException)
            { KmhLog.Debug($"HTTPS: this runtime has no per-client certificate check ({ex.GetType().Name})."); }
            return h;
        }

        internal static HttpClient Client(TimeSpan timeout, bool redirects = true)
            => new HttpClient(Handler(redirects)) { Timeout = timeout };

        internal static bool Strict(HttpRequestMessage req, System.Security.Cryptography.X509Certificates.X509Certificate2 cert,
                                    System.Security.Cryptography.X509Certificates.X509Chain chain, SslPolicyErrors errors)
            => errors == SslPolicyErrors.None;

        internal static bool StrictPeer(object sender, System.Security.Cryptography.X509Certificates.X509Certificate cert,
                                        System.Security.Cryptography.X509Certificates.X509Chain chain, SslPolicyErrors errors)
            => errors == SslPolicyErrors.None;

        // net472 has no per-socket check; the runtime the game runs on may, so it is looked up there.
        private static readonly System.Reflection.PropertyInfo SocketCheck =
            typeof(System.Net.WebSockets.ClientWebSocketOptions).GetProperty("RemoteCertificateValidationCallback");

        // False where the runtime cannot check this socket's certificate itself: the caller must not open it then.
        internal static bool CheckSocket(System.Net.WebSockets.ClientWebSocketOptions options)
        {
            if (options == null || SocketCheck == null || !SocketCheck.CanWrite
                || SocketCheck.PropertyType != typeof(RemoteCertificateValidationCallback)) return false;
            try
            {
                SocketCheck.SetValue(options, new RemoteCertificateValidationCallback(StrictPeer), null);
                return SocketCheck.GetValue(options, null) != null;
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"HTTPS: this runtime has no per-socket certificate check ({ex.GetType().Name}).");
                return false;
            }
        }
    }
}
