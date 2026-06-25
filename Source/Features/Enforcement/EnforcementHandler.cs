using System;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Enforcement.Dto;
using KMHPatch.SubProtocol;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Receives the server's enforcement messages: the snapshot, the chunked profile (begin/chunk/end), and the
    // restore signal
    internal static class EnforcementHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementSnapshot,     OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementProfileBegin, OnProfileBegin);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementProfileChunk, OnProfileChunk);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementProfileEnd,   OnProfileEnd);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.EnforcementRestore,      OnRestore);
        }

        // -- admin mutations from the in-game enforcement dialog --

        // Re-pull the snapshot so the dialog sees current is_admin (e.g. after being op'd mid-session) without a
        // reconnect
        public static void RequestSnapshot()
            => KmhDispatcher.Send(KmhProtocol.Kind.EnforcementSnapshotRequest, null);

        public static void SetEnabled(bool enabled)
            => KmhDispatcher.Send(KmhProtocol.Kind.EnforcementSetEnabled, new { enabled });

        public static void SetSafe(string mod, bool add)
        {
            if (string.IsNullOrWhiteSpace(mod)) return;
            KmhDispatcher.Send(KmhProtocol.Kind.EnforcementSetSafe, new { mod, add });
        }

        public static void SetFlag(string flag, bool value)
            => KmhDispatcher.Send(KmhProtocol.Kind.EnforcementSetFlag, new { flag, value });

        // 32KB raw per chunk: base64 (~43KB) + JSON envelope overhead stays under the 64KB envelope cap (our
        // transport is JSON, not raw binary packets)
        private const int UploadChunkRawBytes = 32 * 1024;

        // Zip this admin's Config folder and upload it as the server profile. ConfigProfileUtility drops
        // personal/cache/mod-list files; safe mods too
        public static bool PublishMyConfigs(out string summary)
        {
            summary = "";
            try
            {
                string dir = GenFilePaths.ConfigFolderPath;
                byte[] zip = ConfigProfileUtility.CreateConfigZipBytes(dir, EnforcementMods.IsSafeModConfigFile);
                if (zip == null || zip.Length == 0) { summary = "Nothing to publish - config zip was empty."; return false; }

                string hash = ConfigProfileUtility.Sha256Hex(zip);
                var chunks = ConfigProfileUtility.SplitIntoChunks(zip, UploadChunkRawBytes);

                if (!KmhDispatcher.Send(KmhProtocol.Kind.EnforcementUploadBegin,
                        new { hash, chunk_count = chunks.Count, total_bytes = zip.Length }))
                { summary = "Not connected to a KMH server."; return false; }

                for (int i = 0; i < chunks.Count; i++)
                    KmhDispatcher.Send(KmhProtocol.Kind.EnforcementUploadChunk,
                        new { hash, index = i, data = Convert.ToBase64String(chunks[i]) });

                KmhDispatcher.Send(KmhProtocol.Kind.EnforcementUploadEnd, new { hash });

                summary = $"Uploaded config profile: {zip.Length / 1024} KB in {chunks.Count} chunk(s), hash {Short(hash)}.";
                return true;
            }
            catch (Exception ex) { summary = $"Publish failed: {ex.Message}"; return false; }
        }

        private static string Short(string hash)
            => string.IsNullOrEmpty(hash) ? "?" : hash.Substring(0, Math.Min(8, hash.Length));

        // Once per connection (the snapshot can arrive several times); reset on disconnect so the next server
        // re-notifies
        private static bool _joinNoticeShown;

        public static void ResetConnectionState() { _joinNoticeShown = false; EnforcementFlow.ResetForNewConnection(); }

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
                return; // enforced WITH a profile + not exempt: the Apply-&-restart / Disconnect consent dialog is the notice

            _joinNoticeShown = true;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try { Messages.Message(msg, MessageTypeDefOf.NeutralEvent, historical: false); }
                catch (Exception ex) { KmhLog.Warn($"Enforcement: join notice failed: {ex.Message}"); }
            });
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            EnforcementSnapshotDto dto = env?.DataAs<EnforcementSnapshotDto>();
            if (dto == null) return;
            EnforcementCache.Apply(dto.Enabled, dto.AdminBypass, dto.PreservePersonal, dto.IsAdmin, dto.HasProfile, dto.SafeMods, dto.ProfileHash);

            ShowJoinNotice(dto);

            if (!dto.Enabled)
            {
                // Non-enforcing server: lift any still-applied profile.
                if (EnforcementProfileApplier.IsApplied)
                {
                    KmhLog.Info("Enforcement: server isn't enforcing but a profile is applied - restoring originals.");
                    EnforcementProfileApplier.Restore();
                }
                EnforcementFlow.Clear();
            }
            else
            {
                // EnforcementFlow decides: exempt admins do nothing; an already-applied profile re-asserts quietly; a
                // new/changed profile shows the Apply-now/Disconnect consent dialog (no silent apply or restart).
                EnforcementFlow.Evaluate();
            }

            KmhLog.Debug(
                $"Enforcement snapshot: {(EnforcementCache.IsLockActive() ? "LOCK active" : "unlocked")} " +
                $"(enabled={dto.Enabled}, admin={dto.IsAdmin}, safe={dto.SafeMods?.Count ?? 0})");
        }

        private static void OnProfileBegin(KmhEnvelope env)
        {
            if (env == null) return;
            EnforcementProfileApplier.OnProfileBegin(
                env.GetString("hash"), env.GetInt("chunk_count"), env.GetInt("total_bytes"));
        }

        private static void OnProfileChunk(KmhEnvelope env)
        {
            if (env == null) return;
            EnforcementProfileApplier.OnProfileChunk(
                env.GetString("hash"), env.GetInt("index"), env.GetString("data"));
        }

        private static void OnProfileEnd(KmhEnvelope env)
        {
            if (env == null) return;
            EnforcementProfileApplier.OnProfileEnd(env.GetString("hash"));
        }

        private static void OnRestore(KmhEnvelope env)
        {
            KmhLog.Info("Enforcement: server lifted enforcement - restoring personal configs.");
            EnforcementProfileApplier.Restore();
        }
    }
}
