using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KMHPatch.Features.ConnectedTreasury;
using KMHPatch.Features.ConnectedTreasury.Dto;
using KMHPatch.Features.Global;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Diagnostics
{
    // The client never states a balance, an account or completion; a Global outage degrades this window only.
    internal static class KmhConnectedTreasurySelfTest
    {
        public static List<(string, bool, string)> Run()
        {
            var r = new List<(string, bool, string)>();
            try
            {
                Authority(r);
                States(r);
                Snapshots(r);
                Balances(r);
                JoinTickets(r);
                WithdrawGrants(r);
            }
            catch (Exception ex) { r.Add(("ConnectedTreasuryHarness", false, ex.ToString())); }
            finally { ConnectedGlobalClient.SenderForTest = null; ConnectedGlobalClient.PostForTest = null; ConnectedGlobalClient.TokenForTest = null; ConnectedTreasuryCache.Clear(); }
            return r;
        }

        private static ConnectedSnapshot Snap(bool available = true, bool linked = true, long revision = 1,
                                              params ConnectedOperation[] ops)
            => new ConnectedSnapshot
            {
                Available = available,
                Linked = linked,
                Revision = revision,
                MaxAmount = 1_000_000,
                PendingIn = ops.Where(o => o.Open && o.IsDeposit).Sum(o => o.Amount),
                PendingOut = ops.Where(o => o.Open && !o.IsDeposit).Sum(o => o.Amount),
                Operations = ops.ToList(),
            };

        private static ConnectedOperation Op(string kind, long amount, string phase, bool open, string detail = "")
            => new ConnectedOperation { Id = "srv:t1", Kind = kind, Amount = amount, Phase = phase,
                                        Open = open, Detail = detail };

        private static void Authority(List<(string, bool, string)> r)
        {
            // No snapshot field may state a balance or an account: the balance comes from Global on the player's token.
            string[] names = typeof(ConnectedSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                      .Select(p => p.Name.ToLowerInvariant()).ToArray();
            string[] forbidden = { "settled", "available balance", "balance", "userid", "globaluserid", "serverid",
                                   "token", "credited", "delivered" };
            var leaked = forbidden.Where(f => names.Contains(f.Replace(" ", ""))).ToList();
            r.Add(("connected: the server snapshot cannot carry a balance, an account id or a completion claim",
                   leaked.Count == 0, leaked.Count == 0 ? string.Join(",", names) : "LEAKED: " + string.Join(",", leaked)));

            string[] opNames = typeof(ConnectedOperation).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                         .Select(p => p.Name.ToLowerInvariant()).ToArray();
            r.Add(("connected: an operation record names no account and no server",
                   !opNames.Contains("userid") && !opNames.Contains("serverid") && !opNames.Contains("globaluserid"),
                   string.Join(",", opNames)));

            bool onlyGlobalWrites = typeof(ConnectedTreasuryCache)
                .GetMethod("ApplyBalance", BindingFlags.NonPublic | BindingFlags.Static) != null;
            r.Add(("connected: the balance has exactly one writer, and it is the Global reader",
                   onlyGlobalWrites, "ApplyBalance is internal"));
        }

        private static void States(List<(string, bool, string)> r)
        {
            ConnectedTreasuryCache.Clear();
            r.Add(("connected: with no snapshot the window waits rather than claiming anything",
                   !ConnectedTreasuryCache.HasSnapshot && !ConnectedTreasuryCache.Linked
                   && !ConnectedTreasuryCache.ServerAvailable, "cleared"));

            ConnectedTreasuryCache.Apply(Snap(linked: false, revision: 1));
            r.Add(("connected: an unlinked player is told to link, not shown a balance",
                   ConnectedTreasuryCache.HasSnapshot && !ConnectedTreasuryCache.Linked
                   && ConnectedTreasuryCache.Balance == null, "linked=false"));

            ConnectedTreasuryCache.Apply(Snap(available: false, revision: 2));
            r.Add(("connected: Global unreachable is its own state, not an error and not a zero balance",
                   !ConnectedTreasuryCache.ServerAvailable && ConnectedTreasuryCache.Linked,
                   "available=false"));

            // A Global outage must leave the local treasury alone.
            object localBefore = Features.Treasury.TreasuryCache.Snapshot;
            ConnectedTreasuryCache.Clear();
            r.Add(("connected: clearing the connected cache does not touch the local treasury",
                   ReferenceEquals(localBefore, Features.Treasury.TreasuryCache.Snapshot), "local untouched"));
        }

        private static void Snapshots(List<(string, bool, string)> r)
        {
            ConnectedTreasuryCache.Clear();
            ConnectedTreasuryCache.Apply(Snap(revision: 5,
                ops: Op("Deposit", 300, "Escrowed", true)));
            r.Add(("connected: a pending deposit is shown as pending, never as credited",
                   ConnectedTreasuryCache.PendingIn == 300 && ConnectedTreasuryCache.HasOpenOperation
                   && ConnectedTreasuryCache.Operations[0].Status == "Sending to Colony Nexus Global",
                   $"in={ConnectedTreasuryCache.PendingIn} status={ConnectedTreasuryCache.Operations[0].Status}"));

            ConnectedTreasuryCache.Apply(Snap(revision: 6,
                ops: Op("Withdraw", 250, "Delivering", true)));
            r.Add(("connected: a pending withdrawal is shown as pending, never as delivered",
                   ConnectedTreasuryCache.PendingOut == 250 && ConnectedTreasuryCache.HasOpenOperation,
                   $"out={ConnectedTreasuryCache.PendingOut}"));

            // An open operation is what disables the submit buttons, on the same rule the server enforces.
            r.Add(("connected: an open operation blocks another being offered",
                   ConnectedTreasuryCache.HasOpenOperation, "busy"));

            ConnectedTreasuryCache.Apply(Snap(revision: 7,
                ops: Op("Withdraw", 250, "Settled", false)));
            r.Add(("connected: a finalized operation stops counting as pending and reads as finalized",
                   ConnectedTreasuryCache.PendingOut == 0 && !ConnectedTreasuryCache.HasOpenOperation
                   && ConnectedTreasuryCache.Operations[0].Status == "Finalized",
                   $"out={ConnectedTreasuryCache.PendingOut}"));

            ConnectedTreasuryCache.Apply(Snap(revision: 8,
                ops: Op("Deposit", 100, "Escrowed", true, "global unreachable")));
            r.Add(("connected: a stalled operation says it is retrying and why",
                   ConnectedTreasuryCache.Operations[0].Retrying
                   && ConnectedTreasuryCache.Operations[0].Detail == "global unreachable", "retrying"));

            // Transports reorder, so an older snapshot can arrive late.
            long before = ConnectedTreasuryCache.Snapshot.Revision;
            ConnectedTreasuryCache.Apply(Snap(revision: 3, ops: Op("Deposit", 999, "Opening", true)));
            r.Add(("connected: a snapshot older than the one on screen is ignored",
                   ConnectedTreasuryCache.Snapshot.Revision == before
                   && ConnectedTreasuryCache.PendingIn == 100, $"revision={ConnectedTreasuryCache.Snapshot.Revision}"));

            ConnectedTreasuryCache.Clear();
            r.Add(("connected: a server switch clears the operations that belonged to the old server",
                   !ConnectedTreasuryCache.HasSnapshot && ConnectedTreasuryCache.Operations.Count == 0, "cleared"));

            // A lower revision applies again after the clear, or a new server could never populate the window.
            ConnectedTreasuryCache.Apply(Snap(revision: 1));
            r.Add(("connected: after the clear a new server's first snapshot applies whatever its revision",
                   ConnectedTreasuryCache.HasSnapshot && ConnectedTreasuryCache.Snapshot.Revision == 1, "applied"));
        }

        private static void Balances(List<(string, bool, string)> r)
        {
            ConnectedTreasuryCache.Clear();

            ConnectedGlobalClient.TokenForTest = () => "";
            bool asked = false;
            ConnectedGlobalClient.SenderForTest = (path, bearer, ct) =>
            { asked = true; return System.Threading.Tasks.Task.FromResult(new GlobalHttp.Result { Ok = true }); };
            ConnectedGlobalClient.RefreshAsync(25, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            r.Add(("connected: an unlinked player asks Global for nothing and is not shown an error",
                   !asked && ConnectedTreasuryCache.Balance == null && ConnectedTreasuryCache.BalanceError == "",
                   $"asked={asked} error={ConnectedTreasuryCache.BalanceError}"));

            ConnectedGlobalClient.TokenForTest = () => "selftest-token";

            JObject body = new JObject
            {
                ["balance"] = new JObject
                {
                    ["settled"] = 1000, ["reserved"] = 250, ["available"] = 750, ["debt"] = 0, ["frozen"] = false,
                },
                ["entries"] = new JArray
                {
                    new JObject { ["kind"] = "Deposit", ["amount"] = 1000, ["settled"] = 1000,
                                  ["server_id"] = "srv", ["detail"] = "", ["at"] = 1700000000L },
                    new JObject { ["kind"] = "Reserve", ["amount"] = 250, ["settled"] = 1000,
                                  ["server_id"] = "srv", ["detail"] = "", ["at"] = 1700000100L },
                },
            };

            ConnectedGlobalClient.SenderForTest =
                (path, bearer, ct) => System.Threading.Tasks.Task.FromResult(
                    new GlobalHttp.Result { Ok = true, Body = body });

            ConnectedGlobalClient.RefreshAsync(25, System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();

            ConnectedBalance b = ConnectedTreasuryCache.Balance;
            r.Add(("connected: the balance and its history are read from Global, not from the server snapshot",
                   b != null && b.Available == 750 && b.Reserved == 250 && b.History.Count == 2,
                   b == null ? "null" : $"available={b.Available} reserved={b.Reserved} history={b.History.Count}"));

            r.Add(("connected: finalized history keeps the balance each movement produced",
                   b != null && b.History[0].Kind == "Deposit" && b.History[0].SettledAfter == 1000,
                   b == null ? "null" : b.History[0].Kind));

            ConnectedGlobalClient.SenderForTest =
                (path, bearer, ct) => System.Threading.Tasks.Task.FromResult(
                    new GlobalHttp.Result { Ok = false, ErrorCode = "unreachable" });
            ConnectedGlobalClient.RefreshAsync(25, System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();

            r.Add(("connected: an unreachable Global shows no balance rather than a stale one",
                   ConnectedTreasuryCache.Balance == null
                   && ConnectedTreasuryCache.BalanceError == "unreachable",
                   $"error={ConnectedTreasuryCache.BalanceError}"));

            // A thrown transport is the same outcome, not an exception escaping into a draw call.
            ConnectedGlobalClient.SenderForTest =
                (path, bearer, ct) => throw new InvalidOperationException("boom");
            ConnectedGlobalClient.RefreshAsync(25, System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();
            r.Add(("connected: a transport that throws degrades to the same no-balance state",
                   ConnectedTreasuryCache.Balance == null && !string.IsNullOrEmpty(ConnectedTreasuryCache.BalanceError),
                   $"error={ConnectedTreasuryCache.BalanceError}"));

            ConnectedGlobalClient.SenderForTest = null;
            ConnectedGlobalClient.TokenForTest = null;
        }

        // Addressed to the far end's claimed id, a hostile server could replay the player's ticket at a trusted one.
        private static void JoinTickets(List<(string, bool, string)> r)
        {
            string body = Features.Global.KmhJoinTicketClient.RequestBody("d89d16b84a25498198b9ad22f53b20ae", "rwt.knappe0.games:25560");
            Newtonsoft.Json.Linq.JObject o = Newtonsoft.Json.Linq.JObject.Parse(body);
            r.Add(("join ticket: the request names the endpoint this game actually connected to",
                   (string)o["dialled"] == "rwt.knappe0.games:25560" && (string)o["server_id"] == "d89d16b84a25498198b9ad22f53b20ae", body));

            bool same = Features.Global.KmhJoinTicketClient.StillSameSession(4, "a", 4, "a");
            bool moved = Features.Global.KmhJoinTicketClient.StillSameSession(4, "a", 5, "a");
            bool other = Features.Global.KmhJoinTicketClient.StillSameSession(4, "a", 4, "b");
            r.Add(("join ticket: a ticket that comes back after the player moved on is not handed to the next server",
                   same && !moved && !other, $"same={same} reconnected={moved} otherServer={other}"));
        }

        // The player's own game asks Global for the grant, for the dialled server; the game server could never get one.
        private static void WithdrawGrants(List<(string, bool, string)> r)
        {
            string sentPath = "", sentJson = "", sentBearer = "";
            ConnectedGlobalClient.TokenForTest = () => "bearer-selftest";
            ConnectedGlobalClient.PostForTest = (path, json, bearer, ct) =>
            {
                sentPath = path; sentJson = json; sentBearer = bearer;
                return System.Threading.Tasks.Task.FromResult(new GlobalHttp.Result
                    { Ok = true, Status = 200, Body = new Newtonsoft.Json.Linq.JObject { ["grant"] = "g-123" } });
            };
            var ok = ConnectedGlobalClient.RequestWithdrawGrantAsync("d89d16b84a25498198b9ad22f53b20ae", 250, "rwt.knappe0.games:25560",
                                                                     System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Newtonsoft.Json.Linq.JObject body = Newtonsoft.Json.Linq.JObject.Parse(sentJson);
            r.Add(("withdraw grant: asked of Global as the player, for this server, amount and dialled endpoint",
                   ok.Grant == "g-123" && sentBearer == "bearer-selftest" && sentPath.StartsWith("/api/v1/player/treasury/withdraw-grant")
                   && (string)body["server_id"] == "d89d16b84a25498198b9ad22f53b20ae" && (long)body["amount"] == 250
                   && (string)body["dialled"] == "rwt.knappe0.games:25560", $"{sentPath} {sentJson}"));

            ConnectedGlobalClient.PostForTest = (path, json, bearer, ct) => System.Threading.Tasks.Task.FromResult(
                new GlobalHttp.Result { Ok = false, Status = 409, ErrorCode = "KMH-SRV-321" });
            var refused = ConnectedGlobalClient.RequestWithdrawGrantAsync("d89d16b84a25498198b9ad22f53b20ae", 250, "evil.example:25560",
                                                                          System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            ConnectedGlobalClient.TokenForTest = () => "";
            var unlinked = ConnectedGlobalClient.RequestWithdrawGrantAsync("x", 1, "y", System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            r.Add(("withdraw grant: a refusal or a missing account yields no grant, and says why",
                   refused.Grant == "" && refused.Error == "KMH-SRV-321" && unlinked.Grant == "" && unlinked.Error.Length > 0,
                   $"refused={refused.Error} unlinked={unlinked.Error}"));
            ConnectedGlobalClient.PostForTest = null;
            ConnectedGlobalClient.TokenForTest = null;
        }
    }
}
