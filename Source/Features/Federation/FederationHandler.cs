using System;
using KMHPatch.Features.Federation.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;
using KMHPatch.UI;

namespace KMHPatch.Features.Federation
{
    // Only the contract's fields are sent; the guild, rank, officer and every permission are the server's to decide.
    internal static class FederationHandler
    {
        public static void Register()
            => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.FederationSnapshot, OnSnapshot);

        internal static void OnSnapshot(KmhEnvelope env)
        {
            FederationSnapshot snapshot = env?.DataAs<FederationSnapshot>();
            if (snapshot != null) FederationCache.Apply(snapshot);
        }

        // Stands in for the transport during a self-test, so each request's kind, payload and op id can be read back.
        internal static Func<string, object, string, bool> SenderForTest;

        // The one way out, so no request reaches a server that does not speak kmh.federation.* or has it switched off.
        private static bool Send(string kind, object data, string opId)
        {
            if (!FederationGate.FederationOffered) return false;
            return SenderForTest != null ? SenderForTest(kind, data, opId) : KmhDispatcher.Send(kind, data, opId);
        }

        // refresh makes the server read Global again, which it allows once per 30 s per link; a timer sends false.
        public static bool RequestSnapshot(bool refresh) => Send(KmhProtocol.Kind.FederationRequest, new { refresh }, null);

        // Each action key is written out in full so the contract check can match it to the scope the server claims.
        public static bool TryCreate(string name, string tag, string description, string color)
        {
            if (!Profile(name, tag, description, color, out string n, out string t, out string d, out string c)) return false;
            return Mutate(KmhProtocol.Kind.FederationCreate, new { name = n, tag = t, description = d, color = c },
                          KmhOpId.For($"federation.create|{t}"));
        }

        public static bool TryJoin(string invite)
        {
            string code = FederationRules.NormalizeInvite(invite);
            if (code.Length == 0) return Refuse(KmhText.Get("KMH_Fed_NeedInvite", "Paste the invite code first."));
            return Mutate(KmhProtocol.Kind.FederationJoin, new { invite = (invite ?? "").Trim() },
                          KmhOpId.For($"federation.join|{code}"));
        }

        public static bool TryInvite(string targetServer)
        {
            string target = (targetServer ?? "").Trim();
            if (target.Length == 0) return Refuse(KmhText.Get("KMH_Fed_NeedTarget", "Paste the other server's Server ID first."));
            return Mutate(KmhProtocol.Kind.FederationInvite, new { target_server = target },
                          KmhOpId.For($"federation.invite|{target}"));
        }

        public static bool TryRevokeInvite(string inviteId)
        {
            string id = (inviteId ?? "").Trim();
            if (id.Length == 0) return false;
            return Mutate(KmhProtocol.Kind.FederationInviteRevoke, new { invite_id = id },
                          KmhOpId.For($"federation.invite_revoke|{id}"));
        }

        public static bool TryLeave()
        {
            string self = OwnLinkId();
            if (self.Length == 0) return Refuse(KmhText.Get("KMH_Fed_NotLinked", "Your guild is not in a federation."));
            return Mutate(KmhProtocol.Kind.FederationLeave, null, KmhOpId.For($"federation.leave|{self}"));
        }

        public static bool TryRemove(string linkId)
        {
            string target = (linkId ?? "").Trim();
            if (target.Length == 0) return false;
            return Mutate(KmhProtocol.Kind.FederationRemove, new { link_id = target },
                          KmhOpId.For($"federation.remove|{target}"));
        }

        public static bool TryTransfer(string linkId)
        {
            string target = (linkId ?? "").Trim();
            if (target.Length == 0) return false;
            return Mutate(KmhProtocol.Kind.FederationTransfer, new { link_id = target },
                          KmhOpId.For($"federation.transfer|{target}"));
        }

        public static bool TryUpdate(string name, string tag, string description, string color)
        {
            if (!Profile(name, tag, description, color, out string n, out string t, out string d, out string c)) return false;
            return Mutate(KmhProtocol.Kind.FederationUpdate, new { name = n, tag = t, description = d, color = c },
                          KmhOpId.For($"federation.update|{t}"));
        }

        // Refused here as well as in the window, so no path can disband without the tag typed exactly.
        public static bool TryDisband(string confirm, string reason)
        {
            FederationSnapshot s = FederationCache.Snapshot;
            string tag = s?.Federation?.Tag ?? "";
            if (!FederationRules.DisbandConfirmed(confirm, tag))
                return Refuse(KmhText.Get("KMH_Fed_DisbandTypeTag", "Type the federation's tag exactly as shown to disband it."));
            string self = OwnLinkId();
            if (self.Length == 0) return Refuse(KmhText.Get("KMH_Fed_NotLinked", "Your guild is not in a federation."));
            string why = (reason ?? "").Trim();
            if (why.Length > FederationRules.ReasonMax) why = why.Substring(0, FederationRules.ReasonMax);
            return Mutate(KmhProtocol.Kind.FederationDisband, new { confirm = confirm.Trim(), reason = why },
                          KmhOpId.For($"federation.disband|{self}"));
        }

        public static bool TryReprove()
        {
            string self = OwnLinkId();
            if (self.Length == 0) return Refuse(KmhText.Get("KMH_Fed_NotLinked", "Your guild is not in a federation."));
            return Mutate(KmhProtocol.Kind.FederationReprove, null, KmhOpId.For($"federation.reprove|{self}"));
        }

        private static string OwnLinkId() => (FederationCache.Snapshot?.Link?.LinkId ?? "").Trim();

        // The window shows the same reasons beside the form; this is the guard for every other caller.
        private static bool Profile(string name, string tag, string description, string color,
                                    out string n, out string t, out string d, out string c)
        {
            n = FederationRules.NormalizeName(name);
            t = FederationRules.NormalizeTag(tag);
            d = FederationRules.NormalizeDescription(description);
            c = (color ?? "").Trim().ToUpperInvariant();
            if (!FederationRules.ValidName(n))
                return Refuse(KmhText.Get("KMH_Fed_BadName", "A federation name is 3 to 32 characters, without < or >."));
            if (!FederationRules.ValidTag(t))
                return Refuse(KmhText.Get("KMH_Fed_BadTag", "A tag is 2 to 5 letters or digits."));
            if (!FederationRules.ValidDescription(d))
                return Refuse(KmhText.Get("KMH_Fed_BadDescription", "A description is at most 280 characters, without < or >."));
            if (!FederationRules.ValidColor(c))
                return Refuse(KmhText.Get("KMH_Fed_BadColor", "Pick one of the offered colours, or none."));
            return true;
        }

        // Stands in for RimWorld's toasts during a self-test, where MessageTypeDefOf cannot resolve.
        internal static Action<string> NoticeForTest;

        private static void Notify(string text, bool refused)
        {
            if (NoticeForTest != null) NoticeForTest(text);
            else if (refused) KmhNotifications.Rejected(text);
            else KmhNotifications.Neutral(text);
        }

        private static bool Refuse(string why)
        {
            Notify(why, refused: true);
            return false;
        }

        private static bool Mutate(string kind, object data, string opId)
        {
            bool sent = Send(kind, data, opId);
            if (sent) Notify(KmhText.Get("KMH_Fed_Sending", "Asking the server…"), refused: false);
            else Notify(KmhText.Get("KMH_Fed_NotSent", "Not connected to a Colony Nexus server, so nothing was sent."), refused: true);
            return sent;
        }
    }
}
