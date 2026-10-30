using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Treasury.Dto;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Features.Treasury
{
    // Item-loss guard: a deposit stays PENDING server-side until its goods-removal is durably saved here.
    public class GameComponent_KMHDepositLedger : GameComponent
    {
        private const int    MaxLedger        = 256;
        private const double HeartbeatSeconds = 5.0;

        private List<string> _durable = new List<string>();
        private readonly HashSet<string> _sessionAdded = new HashSet<string>();

        // Trims the wire, not the history: three hooks observe one save and would each resend all 256 entries.
        private readonly List<string> _newlyDurable = new List<string>();
        private long _confirmedEpoch = -1;

        private bool     _saveArmed;
        private bool     _reconciledThisSession;
        private DateTime _lastHeartbeatUtc = DateTime.MinValue;
        private int      _lastSeenPending  = -1;

        // Past the server's 120 s ReconcileGraceSeconds, so a deposit made just before a drop gets reconciled again.
        internal const double ReconcileAgainSeconds = 150.0;
        private DateTime _lastReconcileUtc = DateTime.MinValue;
        internal int ReconcilesForTest { get; private set; }

        // Scribed with the goods-removal it commits, so the server can spot a save-scum and reverse later deposits.
        private long     _epoch;

        // The colony _epoch counts saves of; a save from before ledger ids gets one at its next save.
        private string   _ledgerId;

        public GameComponent_KMHDepositLedger(Game game) { _ledgerId = NewLedgerId(); }

        internal static string NewLedgerId() => Guid.NewGuid().ToString("N");
        internal string LedgerId => _ledgerId ?? "";

        public static GameComponent_KMHDepositLedger Instance => Current.Game?.GetComponent<GameComponent_KMHDepositLedger>();

        public override void ExposeData()
        {
            base.ExposeData();
            // Folded during the write so txn ids and their goods-removal persist - and roll back - together.
            if (Scribe.mode == LoadSaveMode.Saving && _sessionAdded.Count > 0)
            {
                _newlyDurable.Clear();
                foreach (string t in _sessionAdded)
                    if (!_durable.Contains(t)) { _durable.Add(t); _newlyDurable.Add(t); }
                while (_durable.Count > MaxLedger) _durable.RemoveAt(0);
                KmhLog.Debug($"[KMH Treasury] Save detected (scribe write): folded {_newlyDurable.Count} pending deposit txn(s) into durable ({_durable.Count} total).");
            }
            // Bump BEFORE scribing, so the written value is this save's generation and older saves load a lower one.
            if (Scribe.mode == LoadSaveMode.Saving) _epoch++;
            if (Scribe.mode == LoadSaveMode.Saving && string.IsNullOrEmpty(_ledgerId)) _ledgerId = NewLedgerId();
            Scribe_Collections.Look(ref _durable, "kmhDepositLedger", LookMode.Value);
            Scribe_Values.Look(ref _epoch, "kmhDepositEpoch", 0L);
            Scribe_Values.Look(ref _ledgerId, "kmhDepositLedgerId");
            if (_durable == null) _durable = new List<string>();
            // Armed, not confirmed: a save that never finishes must not confirm, so that waits for the post-write hook.
            if (Scribe.mode == LoadSaveMode.Saving) { _sessionAdded.Clear(); _saveArmed = true; }
        }

        public void RecordDeposit(string txnId)
        {
            if (string.IsNullOrEmpty(txnId)) return;
            _sessionAdded.Add(txnId);
            KmhLog.Debug($"[KMH Treasury] Deposit pending until save txn={txnId}");
        }

        public void NotifySaved(string source)
        {
            // Several save hooks fire per save; confirm once per save generation, whichever notices first.
            if (_epoch == _confirmedEpoch)
            {
                KmhLog.Debug($"[KMH Treasury] Save detected (source={source}); generation {_epoch} already confirmed - not resending.");
                return;
            }
            if (!KmhDispatcher.IsKmhServer)
            {
                KmhLog.Debug("[KMH Treasury] Confirm deferred - not connected to a KMH server (self-heal on reconnect).");
                return;   // epoch stays unconfirmed, so a reconnect still sends it
            }

            // Safe to send only the delta: SelfHeal catches stragglers and a reconnect resends the full history.
            List<string> toSend = _newlyDurable.Count > 0 ? new List<string>(_newlyDurable) : null;
            if (toSend == null)
            {
                _confirmedEpoch = _epoch;
                KmhLog.Debug($"[KMH Treasury] Save detected (source={source}); nothing newly durable this generation.");
                return;
            }

            KmhLog.Debug($"[KMH Treasury] Save detected (source={source}); confirming {toSend.Count} newly durable txn(s) of {_durable.Count} held.");
            SendConfirm(toSend, source);
            _confirmedEpoch = _epoch;
            _newlyDurable.Clear();
        }

        public void OnSnapshotApplied() => SelfHeal("snapshot");

        public override void GameComponentUpdate()
        {
            if (!KmhDispatcher.IsKmhServer) { _reconciledThisSession = false; return; }

            if (!_reconciledThisSession) { _reconciledThisSession = true; SendReconcile(DateTime.UtcNow); }

            if (_saveArmed) { _saveArmed = false; NotifySaved("scribe-post"); }

            DateTime now = DateTime.UtcNow;
            if ((now - _lastHeartbeatUtc).TotalSeconds >= HeartbeatSeconds)
            {
                _lastHeartbeatUtc = now;
                SelfHeal("heartbeat");
            }
        }

        private void SelfHeal(string source) => SelfHeal(source, TreasuryCache.HasSnapshot ? TreasuryCache.Snapshot?.PendingDeposits : null, DateTime.UtcNow);

        // Rollback-safe by construction: it only ever re-confirms txns already in the durable set.
        internal void SelfHeal(string source, List<PendingDeposit> pending, DateTime now)
        {
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
                // Donations started elsewhere (chat) have no local goods-removal to guard, so adopt them.
                if (p.Kind == PendingDeposit.KindGuildDonate && !_sessionAdded.Contains(p.TxnId))
                    RecordDeposit(p.TxnId);
            }

            // Pending there but neither saved nor made here: this game loaded an older save, so the server returns it.
            if (HasUnknownPending(pending, _durable, _sessionAdded) && (now - _lastReconcileUtc).TotalSeconds >= ReconcileAgainSeconds)
            {
                KmhLog.Debug($"[KMH Treasury] A deposit pending on the server is not in this game; reconciling again ({source}).");
                SendReconcile(now);
            }

            if (confirmable.Count == 0)
            {
                KmhLog.Debug($"[KMH Treasury] {count} deposit(s) pending on server, none durably saved locally yet - save to finalize (self-heal={source}).");
                return;
            }
            KmhLog.Debug($"[KMH Treasury] Self-heal ({source}): {count} pending on server, re-confirming {confirmable.Count} durably-saved.");
            SendConfirm(confirmable, $"self-heal:{source}");
        }

        internal static bool HasUnknownPending(IEnumerable<PendingDeposit> pending, ICollection<string> durable, ICollection<string> sessionAdded)
        {
            if (pending == null) return false;
            foreach (PendingDeposit p in pending)
                if (p != null && !string.IsNullOrEmpty(p.TxnId) && !durable.Contains(p.TxnId) && !sessionAdded.Contains(p.TxnId)) return true;
            return false;
        }

        private void SendReconcile(DateTime now)
        {
            _lastReconcileUtc = now;
            ReconcilesForTest++;
            try
            {
                // Goods already gone but unsaved: sending them stops a racing reconnect reverting a real deposit.
                var unsaved = new List<string>(_sessionAdded);
                bool ok = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositReconcile, new { committed = _durable, pending = unsaved, epoch = _epoch, ledger = LedgerId });
                KmhLog.Debug($"[KMH Treasury] Session reconcile of {_durable.Count} durable + {unsaved.Count} unsaved txn(s) at gen {_epoch}: {(ok ? "sent" : "FAILED to send")}.");
            }
            catch (Exception ex) { KmhLog.Warn($"Deposit reconcile send threw: {ex.Message}"); }
        }

        private bool SendConfirm(List<string> ids, string source)
        {
            if (ids == null || ids.Count == 0) { KmhLog.Debug($"[KMH Treasury] Confirm skipped ({source}): no durable txns."); return false; }
            try
            {
                bool ok = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositConfirm, new { txn_ids = ids, epoch = _epoch, ledger = LedgerId });
                KmhLog.Debug($"[KMH Treasury] Finalize confirm for {ids.Count} deposit txn(s) at gen {_epoch} ({source}): {(ok ? "sent" : "FAILED to send")}.");
                return ok;
            }
            catch (Exception ex) { KmhLog.Warn($"Deposit confirm send threw: {ex.Message}"); return false; }
        }
    }
}
