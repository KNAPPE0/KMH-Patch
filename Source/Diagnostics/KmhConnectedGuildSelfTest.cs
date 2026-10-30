using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KMHPatch.Features.ConnectedGuild;
using KMHPatch.Features.ConnectedGuild.Dto;

namespace KMHPatch.Diagnostics
{
    // The client may never assert a rank, a link or completion before the server does. The guild balance is
    // carried (a guild has no player token to read Global with), so it arrives stamped and authorises nothing.
    internal static class KmhConnectedGuildSelfTest
    {
        public static List<(string, bool, string)> Run()
        {
            var r = new List<(string, bool, string)>();
            try
            {
                Authority(r);
                States(r);
                Snapshots(r);
            }
            catch (Exception ex) { r.Add(("ConnectedGuildHarness", false, ex.ToString())); }
            finally { ConnectedGuildCache.Clear(); }
            return r;
        }

        private static ConnectedGuildSnapshot Snap(string state = "Active", bool available = true,
                                                   bool linked = true, bool mayMove = true, bool mayAdmin = true,
                                                   long revision = 1, params ConnectedGuildOperation[] ops)
            => new ConnectedGuildSnapshot
            {
                Available = available, Linked = linked, InGuild = true, Guild = "Ironclad", Rank = "owner",
                MayMove = mayMove, MayAdmin = mayAdmin, State = state,
                LinkId = "link-1", GuildId = "9f2c", Revision = revision, MaxAmount = 1_000_000,
                SettledSilver = 900, AvailableSilver = 900, BalanceReadUtcTicks = DateTime.UtcNow.Ticks,
                PendingIn = ops.Where(o => o.Open && o.IsDeposit).Sum(o => o.Amount),
                PendingOut = ops.Where(o => o.Open && !o.IsDeposit).Sum(o => o.Amount),
                Operations = ops.ToList(),
            };

        private static ConnectedGuildOperation Op(string kind, long amount, string phase, bool open,
                                                  string detail = "")
            => new ConnectedGuildOperation { Id = "srv:t1", Kind = kind, Amount = amount, Phase = phase,
                                             Open = open, Detail = detail, Who = "someone" };

        private static void Authority(List<(string, bool, string)> r)
        {
            string[] names = typeof(ConnectedGuildSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                           .Select(p => p.Name.ToLowerInvariant()).ToArray();
            // Nothing usable as a credential either way; the balance is here deliberately, a token/account/server id is not.
            string[] forbidden = { "token", "userid", "globaluserid", "serverid", "signature", "key", "generation" };
            var leaked = forbidden.Where(f => names.Contains(f)).ToList();
            r.Add(("connected guild: the snapshot carries no credential, account id or server identity",
                   leaked.Count == 0,
                   leaked.Count == 0 ? string.Join(",", names) : "LEAKED: " + string.Join(",", leaked)));

            // A balance without a read time could be shown as current for ever.
            r.Add(("connected guild: the federated balance arrives with the time it was read",
                   names.Contains("balancereadutcticks"), string.Join(",", names)));

            string[] opNames = typeof(ConnectedGuildOperation).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                              .Select(p => p.Name.ToLowerInvariant()).ToArray();
            r.Add(("connected guild: an operation record names no account and no server",
                   !opNames.Contains("userid") && !opNames.Contains("serverid") && !opNames.Contains("linkid"),
                   string.Join(",", opNames)));

            // An amount only: any extra parameter would let the client assert something the server should decide.
            MethodInfo dep = typeof(ConnectedGuildHandler).GetMethod("TryDeposit", BindingFlags.Public | BindingFlags.Static);
            MethodInfo wit = typeof(ConnectedGuildHandler).GetMethod("TryWithdraw", BindingFlags.Public | BindingFlags.Static);
            r.Add(("connected guild: the client can only ever send an amount",
                   Takes(dep, typeof(long)) && Takes(wit, typeof(long)), Describe(dep) + " " + Describe(wit)));

            // Membership lives in kmh.federation.*; a link helper here would send a kind no server handles.
            string[] gone = { "TryLink", "TryReprove", "TryRevoke", "TryInvite", "TryRevokeInvite" };
            var left = gone.Where(n => typeof(ConnectedGuildHandler).GetMethod(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) != null).ToList();
            r.Add(("connected guild: the treasury handler no longer manages the federation link",
                   left.Count == 0, left.Count == 0 ? "only deposit and withdraw remain" : "STILL PRESENT: " + string.Join(",", left)));

            // The cache has one writer and it is the snapshot handler, so nothing in the UI can nudge a balance.
            bool oneWriter = typeof(ConnectedGuildCache).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static) != null
                          && typeof(ConnectedGuildCache).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                 .All(m => m.Name != "Apply" && !m.Name.StartsWith("Set", StringComparison.Ordinal));
            r.Add(("connected guild: the cache is written only by the server snapshot",
                   oneWriter, "Apply is internal, no public setter"));
        }

        private static bool Takes(MethodInfo m, Type t)
        {
            ParameterInfo[] p = m?.GetParameters();
            return p != null && p.Length == 1 && p[0].ParameterType == t;
        }

        private static string Describe(MethodInfo m)
            => m == null ? "missing" : m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")";

        private static void States(List<(string, bool, string)> r)
        {
            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap());
            r.Add(("connected guild: an active link may move value",
                   ConnectedGuildCache.CanMoveValue && !ConnectedGuildCache.NeedsReproof,
                   "state=Active"));

            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(state: "ReproofRequired"));
            ConnectedGuildSnapshot pending = ConnectedGuildCache.Snapshot;
            r.Add(("connected guild: a link awaiting re-proof cannot move value but can still be read",
                   !ConnectedGuildCache.CanMoveValue && ConnectedGuildCache.NeedsReproof
                   && pending.AvailableSilver == 900,
                   $"canMove={ConnectedGuildCache.CanMoveValue} balance={pending.AvailableSilver}"));

            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(state: "Revoked"));
            r.Add(("connected guild: a revoked link moves nothing",
                   !ConnectedGuildCache.CanMoveValue && ConnectedGuildCache.Snapshot.Revoked,
                   "state=Revoked"));

            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(linked: false, state: ""));
            r.Add(("connected guild: an unlinked guild moves nothing",
                   !ConnectedGuildCache.CanMoveValue && !ConnectedGuildCache.Linked, "linked=false"));

            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(available: false));
            r.Add(("connected guild: an unreachable Global is reported as such rather than as an empty guild",
                   !ConnectedGuildCache.ServerAvailable && ConnectedGuildCache.Linked
                   && ConnectedGuildCache.Snapshot.LocalSilver == 0,
                   "available=false, still linked"));

            // The server refuses such a rank, so the UI must agree rather than offer a button that always fails.
            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(mayMove: false, mayAdmin: false));
            ConnectedGuildSnapshot denied = ConnectedGuildCache.Snapshot;
            r.Add(("connected guild: a rank without permission is reported as such, with the server's reason",
                   !denied.MayMove && !denied.MayAdmin,
                   $"mayMove={denied.MayMove} mayAdmin={denied.MayAdmin}"));
        }

        private static void Snapshots(List<(string, bool, string)> r)
        {
            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(revision: 5));
            ConnectedGuildCache.Apply(Snap(revision: 3, mayMove: false));
            r.Add(("connected guild: a snapshot that arrives late does not move the display backwards",
                   ConnectedGuildCache.Snapshot.Revision == 5 && ConnectedGuildCache.Snapshot.MayMove,
                   $"revision={ConnectedGuildCache.Snapshot.Revision}"));

            // A reconnect clears first, so a different server's lower revision still applies.
            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(revision: 1));
            r.Add(("connected guild: a new server's first snapshot applies after a disconnect",
                   ConnectedGuildCache.Snapshot != null && ConnectedGuildCache.Snapshot.Revision == 1,
                   "cleared then applied"));

            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(ops: Op("Deposit", 120, "Escrowed", true)));
            r.Add(("connected guild: an open operation blocks another being offered, and counts as pending",
                   ConnectedGuildCache.HasOpenOperation && ConnectedGuildCache.PendingIn == 120
                   && ConnectedGuildCache.PendingOut == 0,
                   $"pendingIn={ConnectedGuildCache.PendingIn}"));

            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(ops: Op("Withdraw", 75, "Settled", false)));
            r.Add(("connected guild: a settled operation stops being pending and stops blocking",
                   !ConnectedGuildCache.HasOpenOperation && ConnectedGuildCache.PendingOut == 0,
                   "settled"));

            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(ops: Op("Deposit", 50, "Escrowed", true, "global unreachable")));
            ConnectedGuildOperation retrying = ConnectedGuildCache.Operations[0];
            r.Add(("connected guild: a stuck operation says it is retrying and why",
                   retrying.Retrying && retrying.Status == "Sending to Colony Nexus Global",
                   $"retrying={retrying.Retrying} status={retrying.Status}"));

            ConnectedGuildCache.Clear();
            ConnectedGuildCache.Apply(Snap(ops: Op("Deposit", 10, "SomethingNewer", true)));
            r.Add(("connected guild: an unknown phase displays as itself rather than as nothing",
                   ConnectedGuildCache.Operations[0].Status == "SomethingNewer",
                   ConnectedGuildCache.Operations[0].Status));

            ConnectedGuildCache.Clear();
            r.Add(("connected guild: clearing leaves no stale guild state behind",
                   !ConnectedGuildCache.HasSnapshot && !ConnectedGuildCache.Linked
                   && !ConnectedGuildCache.CanMoveValue && ConnectedGuildCache.Operations.Count == 0,
                   "cleared"));
        }
    }
}
