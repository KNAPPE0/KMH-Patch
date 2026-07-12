using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Treasury.Dto;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Features.Treasury
{
    // Item-loss guard: server holds a deposit PENDING until we confirm its goods-removal is durably saved (_durable via
    // ExposeData, rolls back with the goods). Layered hooks + self-heal so no save path (RWT/autosave/modded) leaves it stuck.
    public class GameComponent_KMHDepositLedger : GameComponent
    {
        private const int    MaxLedger        = 256;
        private const double HeartbeatSeconds = 5.0;

        private List<string> _durable = new List<string>();
        private readonly HashSet<string> _sessionAdded = new HashSet<string>();

        private bool     _saveArmed;              // ExposeData(Saving) ran; confirm on the next update (post-write)
        private bool     _reconciledThisSession;
        private DateTime _lastHeartbeatUtc = DateTime.MinValue;
        private int      _lastSeenPending  = -1;

        // Monotonic save generation: bumped once per save and scribed, so it rides with the goods-removal it commits.
        // Reported on confirm/reconcile so the server can spot a rolled-back (save-scummed) client and reverse deposits
        // finalized past the generation the client now holds - closes the treasury-deposit + save-recovery dupe.
        // (Client half only; server enforcement lands in a later release - old servers just ignore the field.)
        private long     _epoch;

        public GameComponent_KMHDepositLedger(Game game) { }

        public static GameComponent_KMHDepositLedger Instance => Current.Game?.GetComponent<GameComponent_KMHDepositLedger>();

        public override void ExposeData()
        {
            base.ExposeData();
            // Fold this session's deposits into the durable set as the save is written, so the txn ids and the matching
            // goods removal persist (and roll back) together. Runs for EVERY save path (all serialize through Scribe).
            if (Scribe.mode == LoadSaveMode.Saving && _sessionAdded.Count > 0)
            {
                int added = 0;
                foreach (string t in _sessionAdded)
                    if (!_durable.Contains(t)) { _durable.Add(t); added++; }
                while (_durable.Count > MaxLedger) _durable.RemoveAt(0);
                KmhLog.Debug($"[KMH Treasury] Save detected (scribe write): folded {added} pending deposit txn(s) into durable ({_durable.Count} total).");
            }
            // Bump the generation on every save, BEFORE scribing it, so the value written equals this save's
            // generation and any older save loads a strictly lower one. Atomic with the durable fold above, so the
            // epoch never disagrees with the goods-removal it rode in with.
            if (Scribe.mode == LoadSaveMode.Saving) _epoch++;
            Scribe_Collections.Look(ref _durable, "kmhDepositLedger", LookMode.Value);
            Scribe_Values.Look(ref _epoch, "kmhDepositEpoch", 0L);
            if (_durable == null) _durable = new List<string>();
            // Confirm AFTER the write succeeds (next update / post-save hook), never mid-serialization - a save that
            // never finishes must never confirm.
            if (Scribe.mode == LoadSaveMode.Saving) { _sessionAdded.Clear(); _saveArmed = true; }
        }

        public void RecordDeposit(string txnId)
        {
            if (string.IsNullOrEmpty(txnId)) return;
            _sessionAdded.Add(txnId);
            KmhLog.Debug($"[KMH Treasury] Deposit pending until save txn={txnId}");
        }

        // Called by every save hook we can catch (SaveGame postfix, Autosaver postfix, the post-write _saveArmed flush).
        public void NotifySaved(string source)
        {
            KmhLog.Debug($"[KMH Treasury] Save detected (source={source}); {_durable.Count} durable deposit txn(s) to confirm.");
            if (KmhDispatcher.IsKmhServer) SendConfirm(_durable, source);
            else KmhLog.Debug("[KMH Treasury] Confirm deferred - not connected to a KMH server (self-heal on reconnect).");
        }

        // A fresh treasury snapshot still showing pending we've saved -> re-confirm right away, don't wait for the tick.
        public void OnSnapshotApplied() => SelfHeal("snapshot");

        public override void GameComponentUpdate()
        {
            if (!KmhDispatcher.IsKmhServer) { _reconciledThisSession = false; return; }

            if (!_reconciledThisSession) { _reconciledThisSession = true; SendReconcile(); }

            if (_saveArmed) { _saveArmed = false; NotifySaved("scribe-post"); }

            DateTime now = DateTime.UtcNow;
            if ((now - _lastHeartbeatUtc).TotalSeconds >= HeartbeatSeconds)
            {
                _lastHeartbeatUtc = now;
                SelfHeal("heartbeat");
            }
        }

        // Re-confirm any durably-saved deposit the server still shows pending. Self-terminating (server commits ->
        // next snapshot shows 0 pending -> no-op) and rollback-safe (never confirms a txn not in the durable set).
        private void SelfHeal(string source)
        {
            List<PendingDeposit> pending = TreasuryCache.HasSnapshot ? TreasuryCache.Snapshot?.PendingDeposits : null;
            int count = pending?.Count ?? 0;

            if (count != _lastSeenPending)
            {
                if (count == 0 && _lastSeenPending > 0) KmhLog.Debug("[KMH Treasury] Pending deposits cleared - server finalized them.");
                _lastSeenPending = count;
            }
            if (count == 0) return;

            List<string> confirmable = new List<string>();
            foreach (PendingDeposit p in pending)
            {
                if (p == null || string.IsNullOrEmpty(p.TxnId)) continue;
                if (_durable.Contains(p.TxnId)) { confirmable.Add(p.TxnId); continue; }
                // Adopt guild donations we didn't start from this UI (e.g. a chat command) - unlike deposits there's
                // no local goods-removal to guard, so the next save may finalize them.
                if (p.Kind == PendingDeposit.KindGuildDonate && !_sessionAdded.Contains(p.TxnId))
                    RecordDeposit(p.TxnId);
            }

            if (confirmable.Count == 0)
            {
                KmhLog.Debug($"[KMH Treasury] {count} deposit(s) pending on server, none durably saved locally yet - save to finalize (self-heal={source}).");
                return;
            }
            KmhLog.Debug($"[KMH Treasury] Self-heal ({source}): {count} pending on server, re-confirming {confirmable.Count} durably-saved.");
            SendConfirm(confirmable, $"self-heal:{source}");   // server pushes a fresh snapshot when it commits (committed>0)
        }

        private void SendReconcile()
        {
            try
            {
                bool ok = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositReconcile, new { committed = _durable, epoch = _epoch });
                KmhLog.Debug($"[KMH Treasury] Session reconcile of {_durable.Count} durable txn(s) at gen {_epoch}: {(ok ? "sent" : "FAILED to send")}.");
            }
            catch (Exception ex) { KmhLog.Warn($"Deposit reconcile send threw: {ex.Message}"); }
        }

        private bool SendConfirm(List<string> ids, string source)
        {
            if (ids == null || ids.Count == 0) { KmhLog.Debug($"[KMH Treasury] Confirm skipped ({source}): no durable txns."); return false; }
            try
            {
                bool ok = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositConfirm, new { txn_ids = ids, epoch = _epoch });
                KmhLog.Debug($"[KMH Treasury] Finalize confirm for {ids.Count} deposit txn(s) at gen {_epoch} ({source}): {(ok ? "sent" : "FAILED to send")}.");
                return ok;
            }
            catch (Exception ex) { KmhLog.Warn($"Deposit confirm send threw: {ex.Message}"); return false; }
        }
    }
}
