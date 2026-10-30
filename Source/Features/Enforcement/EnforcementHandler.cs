using System;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Enforcement.Dto;
using KMHPatch.SubProtocol;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    internal static class EnforcementHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementSnapshot,     OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementProfileBegin, OnProfileBegin);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementProfileChunk, OnProfileChunk);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementProfileEnd,   OnProfileEnd);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementRestore,      OnRestore);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementPublishAccepted, e => ConfigPublish.OnAccepted(e?.Data));
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementPublishAck,      e => ConfigPublish.OnAck(e?.Data));
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementPublishResult,   e => ConfigPublish.OnResult(e?.Data));
        }

        // Returns whether the request left the client, so hydration can retry instead of sitting on "Loading…".
        public static bool RequestSnapshot()
            => KmhDispatcher.Send(KmhProtocol.Kind.EnforcementSnapshotRequest, null);

        // Written off the main thread, hence Interlocked; the poll counts a pushed snapshot as fresh.
        private static long _lastSnapshotStamp;
        internal static long LastSnapshotStamp => System.Threading.Interlocked.Read(ref _lastSnapshotStamp);

        public static void SetEnabled(bool enabled)
            => KmhDispatcher.Send(KmhProtocol.Kind.EnforcementSetEnabled, new { enabled });

        public static void SetSafe(string mod, bool add)
        {
            if (string.IsNullOrWhiteSpace(mod)) return;
            KmhDispatcher.Send(KmhProtocol.Kind.EnforcementSetSafe, new { mod, add });
        }

        public static void SetFlag(string flag, bool value)
            => KmhDispatcher.Send(KmhProtocol.Kind.EnforcementSetFlag, new { flag, value });

        // Once per connection: snapshots arrive repeatedly; disconnect resets it for the next server.
        private static bool _joinNoticeShown;

        public static void ResetConnectionState()
        {
            _joinNoticeShown = false;
            System.Threading.Interlocked.Exchange(ref _lastSnapshotStamp, 0);
            EnforcementFlow.ResetForNewConnection();
        }

        private static void ShowJoinNotice(EnforcementSnapshotDto dto)
        {
            if (_joinNoticeShown || dto == null || !dto.Enabled) return;

            bool exempt = dto.AdminBypass && dto.IsAdmin;
            string msg;
            if (exempt)
                msg = $"Config enforcement is ON ({dto.ProfileFiles} mod config(s)). You're exempt as an admin.";
            else if (!dto.HasProfile)
                msg = "This server enforces mod configs - your Mod Options are locked to the server while you're connected.";
            else
                return; // the consent dialog is the notice

            _joinNoticeShown = true;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try { Messages.Message(msg, MessageTypeDefOf.NeutralEvent, historical: false); }
                catch (Exception ex) { KmhLog.Warn($"Enforcement: join notice failed: {ex.Message}"); }
            });
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            if (!EnforcementAdmission.Matches(env)) return;
            EnforcementSnapshotDto dto = env?.DataAs<EnforcementSnapshotDto>();
            if (dto == null) return;
            System.Threading.Interlocked.Exchange(ref _lastSnapshotStamp, System.Diagnostics.Stopwatch.GetTimestamp());
            EnforcementAdmission.ShowConsentSurface();
            EnforcementCache.Apply(dto.Enabled, dto.AdminBypass, dto.PreservePersonal, dto.IsAdmin, dto.HasProfile, dto.SafeMods, dto.ProfileHash, dto.ModOrder, dto.ModOrderHash,
                                   dto.ProfileRevision, dto.ProfileFiles);

            ShowJoinNotice(dto);

            if (!dto.Enabled)
            {
                if (EnforcementProfileApplier.IsApplied)
                {
                    KmhLog.Info("Enforcement: server isn't enforcing but a profile is applied - restoring originals.");
                    if (EnforcementProfileApplier.Restore("ConfirmedNonEnforcingServer")) NoticeRestored();
                }
                EnforcementFlow.Clear();
                if (EnforcementAdmission.Pending) EnforcementAdmission.Fail("Enforcement policy changed during admission. Reconnect to run stock RWT validation.");
            }
            else
            {
                EnforcementFlow.Evaluate();
                if ((dto.AdminBypass && dto.IsAdmin) || !dto.HasProfile) EnforcementAdmission.Complete();
            }

            KmhLog.Debug(
                $"Enforcement snapshot: {(EnforcementCache.IsLockActive() ? "LOCK active" : "unlocked")} " +
                $"(enabled={dto.Enabled}, admin={dto.IsAdmin}, safe={dto.SafeMods?.Count ?? 0})");
        }

        private static void OnProfileBegin(KmhEnvelope env)
        {
            if (env == null || !EnforcementAdmission.Matches(env)) return;
            EnforcementProfileApplier.OnProfileBegin(
                env.GetString("hash"), env.GetInt("chunk_count"), env.GetInt("total_bytes"));
        }

        private static void OnProfileChunk(KmhEnvelope env)
        {
            if (env == null || !EnforcementAdmission.Matches(env)) return;
            EnforcementProfileApplier.OnProfileChunk(
                env.GetString("hash"), env.GetInt("index"), env.GetString("data"));
        }

        private static void OnProfileEnd(KmhEnvelope env)
        {
            if (env == null || !EnforcementAdmission.Matches(env)) return;
            EnforcementProfileApplier.OnProfileEnd(env.GetString("hash"));
        }

        private static void OnRestore(KmhEnvelope env)
        {
            KmhLog.Info("Enforcement: server lifted enforcement - restoring personal configs.");
            if (EnforcementProfileApplier.Restore("ExplicitServerDisable")) NoticeRestored();
        }

        // Said once, while connected: the files are back but the game keeps the server's values until it restarts.
        private static void NoticeRestored()
        {
            if (!EnforcementProfileApplier.RestoreAwaitsRestart) return;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    Messages.Message(UI.KmhText.Get("KMH_Enf_RestoredOnJoin",
                        "Colony Nexus put your own mod settings back. They load the next time RimWorld starts; until then those mods' options stay locked."),
                        MessageTypeDefOf.NeutralEvent, historical: false);
                }
                catch (Exception ex) { KmhLog.Warn($"Enforcement: restore notice failed: {ex.Message}"); }
            });
        }
    }
}
