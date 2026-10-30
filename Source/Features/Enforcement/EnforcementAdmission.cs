using System;
using System.Linq;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using Newtonsoft.Json.Linq;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    internal static class EnforcementAdmission
    {
        internal const string Capability = "preworld_enforcement_gate";
        internal const string VerifyKind = "kmh.enforcement.verified";
        internal const string FailKind = "kmh.enforcement.failed";
        internal static bool Pending { get; private set; }
        internal static bool Verified { get; private set; }
        internal static string Id { get; private set; }
        private static string _serverId, _hash;
        private static long _generation;
        private static object _connection;
        internal static Action<string> FailureForTest;

        internal static void StartConnection(object connection)
        {
            if (ReferenceEquals(connection, _connection)) return;
            _connection = connection;
            KmhDispatcher.ResetSession();
            KmhClientCaches.ClearAll();
            EnforcementHandler.ResetConnectionState();
        }

        internal static void Reset()
        {
            Pending = Verified = false;
            Id = _serverId = _hash = null;
            _generation = 0;
        }

        internal static bool Begin(KmhEnvelope env)
        {
            if (!env.GetBool("admission_required")) return true;
            if (!(env.GetString("capabilities") ?? "").Split(',').Contains(Capability)
                || !env.GetBool("enforcement_enabled")
                || string.IsNullOrEmpty(env.GetString("admission_id")) || string.IsNullOrEmpty(env.GetString("server_id")))
            { Fail("The server's admission handshake is incomplete."); return false; }
            string id = env.GetString("admission_id");
            if (Pending && Id != id) { Fail("The admission changed. Reconnect to retry."); return false; }
            Pending = true;
            Id = id;
            _serverId = env.GetString("server_id");
            _hash = env.GetString("admission_hash") ?? "";
            _generation = ReadGeneration(env);
            return true;
        }

        internal static bool IsControl(string kind) => kind == VerifyKind || kind == FailKind
            || kind == KmhProtocol.Kind.Hello || kind == KmhProtocol.Kind.HelloAck
            // Admitted while held: it is the message that ends a hold this client cannot satisfy.
            || kind == KmhProtocol.Kind.Incompatible
            || kind == KmhProtocol.Kind.Ping || kind == KmhProtocol.Kind.Pong
            || kind == KmhProtocol.Kind.EnforcementSnapshot || kind == KmhProtocol.Kind.EnforcementSnapshotRequest
            || kind == KmhProtocol.Kind.EnforcementProfileRequest || kind == KmhProtocol.Kind.EnforcementProfileBegin
            || kind == KmhProtocol.Kind.EnforcementProfileChunk || kind == KmhProtocol.Kind.EnforcementProfileEnd;

        internal static object Bind(object data)
        {
            var payload = data == null ? new JObject() : JObject.FromObject(data);
            payload["admission_id"] = Id;
            payload["server_id"] = _serverId;
            payload["admission_hash"] = _hash;
            payload["admission_generation"] = _generation;
            return payload;
        }

        internal static bool Matches(KmhEnvelope env) => !Pending || (env != null
            && env.GetString("admission_id") == Id && env.GetString("server_id") == _serverId
            && env.GetString("admission_hash") == _hash && ReadGeneration(env) == _generation);

        private static long ReadGeneration(KmhEnvelope env)
            => long.TryParse(env.GetString("admission_generation"), out long value) ? value : -1;

        internal static void Complete()
        {
            if (!Pending) return;
            if (EnforcementProfileApplier.RecoveryRequired) { Fail("Config recovery is required."); return; }
            Verified = true;
            string resolution = EnforcementCache.AdminBypass && EnforcementCache.IsAdmin ? "AdminBypass"
                : !EnforcementCache.HasProfile ? "NoProfile" : "ProfileVerified";
            if (!KmhDispatcher.Send(VerifyKind, new { active = true, resolution }))
            { Verified = false; Fail("Could not send admission verification. Reconnect to retry."); }
        }

        internal static bool ReceiveGlobalData()
        {
            if (Pending && !Verified) { Fail("Server data arrived before config verification."); return false; }
            Pending = false;
            return true;
        }

        internal static void ShowConsentSurface()
        {
            if (!Pending || Find.WindowStack == null) return;
            foreach (var window in Find.WindowStack.Windows.ToArray())
                if (window.GetType().Name == "DLG_Wait") window.Close();
        }

        internal static void Fail(string reason)
        {
            KmhLog.Error("Enforcement admission failed: " + reason);
            if (Pending) KmhDispatcher.Send(FailKind, new { reason });
            Verified = false;
            if (FailureForTest != null) { FailureForTest(reason); return; }
            RwtCompat.DisconnectToMainMenu();
        }
    }
}
