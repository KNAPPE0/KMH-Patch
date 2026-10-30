using System;
using System.Reflection;

namespace KMHPatch.Features.Account
{
    // Steamworks exists only when launched through Steam, so every call is reflected; one shared copy, since a
    // drifted duplicate would fail silently.
    internal static class KmhSteamInterop
    {
        public static Type Find(string fullName)
        {
            Type direct = Type.GetType(fullName, throwOnError: false);
            if (direct != null) return direct;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { Type found = a.GetType(fullName, throwOnError: false); if (found != null) return found; }
                catch (Exception) { }
            }
            return null;
        }
    }
}
