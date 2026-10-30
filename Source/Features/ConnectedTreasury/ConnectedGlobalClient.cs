using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Features.Account;
using KMHPatch.Features.Global;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.ConnectedTreasury
{
    public sealed class ConnectedBalance
    {
        public long Settled;
        public long Reserved;
        public long Available;
        public long Debt;
        public bool Frozen;
        public List<ConnectedHistoryLine> History = new List<ConnectedHistoryLine>();
    }

    public sealed class ConnectedHistoryLine
    {
        public string Kind = "";
        public long Amount;
        public long SettledAfter;
        public string ServerId = "";
        public string Detail = "";
        public long AtUnix;
    }

    // Read from Global as the player, never relayed by a game server, which could fake a payment by lying about a balance.
    internal static class ConnectedGlobalClient
    {
        // Replaced by the self-tests, which have no network. Nothing else assigns it.
        internal static Func<string, string, CancellationToken, Task<GlobalHttp.Result>> SenderForTest;

        // Self-tests run in-game, so they must never write the player's real account file for a token.
        internal static Func<string> TokenForTest;

        private static string Token()
            => TokenForTest != null ? TokenForTest() : KmhAccountStore.TokenFor(GlobalHttp.BaseUrl);

        private static Task<GlobalHttp.Result> GetAsync(string path, string bearer, CancellationToken ct)
            => SenderForTest != null ? SenderForTest(path, bearer, ct) : GlobalHttp.GetAsync(path, bearer, ct);

        public static bool HasToken => !string.IsNullOrEmpty(Token());

        // Replaced by the self-tests. Nothing else assigns it.
        internal static Func<string, string, string, CancellationToken, Task<GlobalHttp.Result>> PostForTest;

        // Asked with the player's own credential, never via the game server the grant exists to hold back.
        public static async Task<(string Grant, string Error)> RequestWithdrawGrantAsync(string serverId, long amount,
                                                                                        string dialled, CancellationToken ct)
        {
            string bearer = Token();
            if (string.IsNullOrEmpty(bearer)) return ("", "no Colony Nexus Global account is linked");
            string json = WithdrawGrantBody(serverId, amount, dialled);
            ulong steam = Account.KmhSteamPersona.SteamId64();
            string path = "/api/v1/player/treasury/withdraw-grant" + (steam > 0 ? "?steam_id=" + steam : "");
            GlobalHttp.Result r = PostForTest != null
                ? await PostForTest(path, json, bearer, ct).ConfigureAwait(false)
                : await GlobalHttp.PostAsync(path, json, bearer, ct).ConfigureAwait(false);
            if (!r.Ok || r.Body == null) return ("", string.IsNullOrEmpty(r.ErrorCode) ? r.Status.ToString() : r.ErrorCode);
            string grant = (string)r.Body["grant"] ?? "";
            return grant.Length == 0 ? ("", "no grant in the answer") : (grant, "");
        }

        internal static string WithdrawGrantBody(string serverId, long amount, string dialled)
            => new Newtonsoft.Json.Linq.JObject { ["server_id"] = serverId ?? "", ["amount"] = amount, ["dialled"] = dialled ?? "" }
                   .ToString(Newtonsoft.Json.Formatting.None);

        public static async Task RefreshAsync(int historyLimit, CancellationToken ct)
        {
            string token = Token();
            if (string.IsNullOrEmpty(token))
            {
                // Not an error state: an unlinked player simply has no federated account to read.
                ConnectedTreasuryCache.ApplyBalance(null, "");
                return;
            }

            GlobalHttp.Result r;
            try
            {
                r = await GetAsync("/api/v1/treasury/me?limit=" + historyLimit, token, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ConnectedTreasuryCache.ApplyBalance(null, ex.GetType().Name);
                return;
            }

            if (r == null || !r.Ok || r.Body == null)
            {
                // Global is unreachable or refused. The previous balance is dropped rather than shown as current.
                ConnectedTreasuryCache.ApplyBalance(null, r?.ErrorCode ?? "unreachable");
                return;
            }

            ConnectedTreasuryCache.ApplyBalance(Parse(r.Body), "");
        }

        private static ConnectedBalance Parse(JObject body)
        {
            ConnectedBalance b = new ConnectedBalance();
            JToken bal = body["balance"];
            if (bal != null)
            {
                b.Settled   = Num(bal["settled"]);
                b.Reserved  = Num(bal["reserved"]);
                b.Available = Num(bal["available"]);
                b.Debt      = Num(bal["debt"]);
                b.Frozen    = bal["frozen"] != null && bal["frozen"].Type == JTokenType.Boolean
                              && bal["frozen"].Value<bool>();
            }

            if (body["entries"] is JArray entries)
                foreach (JToken e in entries)
                {
                    if (e == null) continue;
                    b.History.Add(new ConnectedHistoryLine
                    {
                        Kind         = Str(e["kind"]),
                        Amount       = Num(e["amount"]),
                        SettledAfter = Num(e["settled"]),
                        // Owner-written text never reaches a label raw.
                        ServerId     = UI.KmhDisplayText.Inert(Str(e["server_id"])),
                        Detail       = UI.KmhDisplayText.Inert(Str(e["detail"])),
                        AtUnix       = Num(e["at"]),
                    });
                }
            return b;
        }

        private static long Num(JToken t)
            => t == null || t.Type == JTokenType.Null ? 0 : t.Value<long>();

        private static string Str(JToken t)
            => t == null || t.Type == JTokenType.Null ? "" : t.ToString();
    }
}
