using System;
using System.Collections.Generic;

namespace KMH.Sdk.Client.Apis
{
    /// <summary>How much an activity matters next to everything else the player is doing.</summary>
    public enum PresencePriority
    {
        /// <summary>Everyday play; rotates with the colony's other everyday activities.</summary>
        Normal = 0,
        /// <summary>Caravans, trade, visits, research milestones.</summary>
        Medium = 1,
        /// <summary>Disasters, major quests, important multiplayer events.</summary>
        High = 2,
        /// <summary>Raids and emergencies; shown at once, ahead of everything else.</summary>
        Critical = 3,
    }

    /// <summary>One activity an extension wants on the player's Colony Nexus Discord status.</summary>
    /// <remarks>Shown only while gameplay activity is on; markup is stripped and each line capped at 64 bytes.
    /// Never put player names, private messages or anything the player has not chosen to share in a claim.</remarks>
    public sealed class PresenceClaim
    {
        /// <summary>The headline, for example "Defending the outpost".</summary>
        public string Details { get; set; }
        /// <summary>Optional second line, used when there is no server name to show there.</summary>
        public string State { get; set; }
        /// <summary>Defaults to <see cref="PresencePriority.Medium"/>.</summary>
        public PresencePriority Priority { get; set; } = PresencePriority.Medium;
    }

    /// <summary>Adds an extension's activities to the Discord status. Providers run every few seconds on RimWorld's
    /// main thread, so keep them cheap; return null when there is nothing to report.</summary>
    public static class PresenceProviders
    {
        private const int MaxFailures = 3;

        private sealed class Entry
        {
            public Func<PresenceClaim> Provider;
            public int Failures;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Providers = new Dictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>Raised once when a provider is dropped after throwing repeatedly; the argument is its id.</summary>
        public static event Action<string> ProviderDisabled;

        /// <summary>Registers or replaces a provider. Namespace the id, for example "yourname.yourmod".</summary>
        public static bool Register(string id, Func<PresenceClaim> provider)
        {
            if (string.IsNullOrWhiteSpace(id) || provider == null) return false;
            lock (Gate) Providers[id] = new Entry { Provider = provider };
            return true;
        }

        /// <summary>Removes a provider; unknown ids are ignored.</summary>
        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (Gate) Providers.Remove(id);
        }

        /// <summary>Asks every provider once. Called by Colony Nexus; a provider that keeps throwing is dropped.</summary>
        public static List<PresenceClaim> Collect()
        {
            var claims = new List<PresenceClaim>();
            KeyValuePair<string, Entry>[] entries;
            lock (Gate)
            {
                if (Providers.Count == 0) return claims;
                entries = new KeyValuePair<string, Entry>[Providers.Count];
                ((ICollection<KeyValuePair<string, Entry>>)Providers).CopyTo(entries, 0);
            }
            foreach (KeyValuePair<string, Entry> e in entries)
            {
                try
                {
                    PresenceClaim c = e.Value.Provider();
                    e.Value.Failures = 0;
                    if (c != null && !string.IsNullOrWhiteSpace(c.Details)) claims.Add(c);
                }
                catch
                {
                    if (++e.Value.Failures < MaxFailures) continue;
                    lock (Gate) Providers.Remove(e.Key);
                    try { ProviderDisabled?.Invoke(e.Key); } catch { }
                }
            }
            return claims;
        }
    }
}
