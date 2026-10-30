using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Global;
using KMHPatch.UI;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Mods
{
    internal enum PrepLineState { Pending, Working, Waiting, Done, Skipped, Failed }

    internal sealed class PrepLine
    {
        public string Label = "";
        public PrepLineState State = PrepLineState.Pending;
        public string Detail = "";
        public float Progress = -1f;
    }

    // One job at a time, persisted at every step a restart has to survive.
    internal static class KmhJoinPreparation
    {
        private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(500);
        internal static readonly TimeSpan SubscribeGrace = TimeSpan.FromSeconds(60);
        internal static readonly TimeSpan DownloadNudge = TimeSpan.FromSeconds(20);
        internal static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(120);
        internal const int MaxStalls = 3;
        internal const int MaxRestarts = 3;
        private static readonly TimeSpan FindServerFor = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan ConnectFor = TimeSpan.FromSeconds(90);
        internal static readonly TimeSpan JoinCountdown = TimeSpan.FromSeconds(5);

        private static KmhPrepareJob _job;
        private static DateTime _next = DateTime.MinValue;
        private static bool _bootChecked;
        private static bool _justRestarted;
        private static bool _verified;
        private static bool _failed;
        private static bool _loginPrompted;
        private static bool _rwtStarted;
        private static bool _listWrittenThisBoot;
        private static bool _rwtStagedThisBoot;
        private static DateTime _startedUtc, _connectUtc, _joinAtUtc;
        private static bool _sawConnecting;
        private static readonly Dictionary<string, DateTime> _subscribedAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly Dictionary<string, DateTime> _nudgedAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Progress> _progress = new Dictionary<string, Progress>(StringComparer.Ordinal);

        private sealed class Progress { public ulong Bytes; public DateTime ChangedUtc; public int Stalls; }

        public static readonly PrepLine Check = new PrepLine(), Rwt = new PrepLine(), Workshop = new PrepLine(),
                                         ModList = new PrepLine(), Restart = new PrepLine(), Account = new PrepLine(),
                                         Connect = new PrepLine();
        public static IEnumerable<PrepLine> Lines { get { yield return Check; yield return Rwt; yield return Workshop; yield return ModList; yield return Restart; yield return Account; yield return Connect; } }

        public static string Headline { get; private set; } = "";
        public static bool Active => _job != null;
        public static bool Failed => _failed;
        public static bool AwaitingRestart => _job != null && _job.Stage == KmhPrepareStage.Restarting;
        public static bool Finished { get; private set; }
        public static string ServerId => _job?.ServerId ?? "";
        public static string ServerName => _job?.ServerName ?? "";
        public static bool Connecting => _job != null && _job.Stage == KmhPrepareStage.Connecting;
        public static int JoinCountdownLeft => _joinAtUtc == DateTime.MinValue ? -1 : Math.Max(0, (int)Math.Ceiling((_joinAtUtc - DateTime.UtcNow).TotalSeconds));

        public static bool IsFor(string serverId) => _job != null && string.Equals(_job.ServerId, serverId, StringComparison.Ordinal);

        public static string RefusalToStart()
        {
            // Two writers of one ModsConfig.xml: Config Enforcement has to be restored first.
            if (Features.Enforcement.EnforcementProfileApplier.IsApplied || Features.Enforcement.EnforcementProfileApplier.RecoveryRequired)
                return KmhText.Get("KMH_Prep_EnforcementApplied", "Another server's Config Enforcement is still applied to this game. Restore your settings from Colony Nexus > Advanced tools first.");
            if (Current.ProgramState != ProgramState.Entry)
                return KmhText.Get("KMH_Prep_MainMenuOnly", "Preparing a server changes your mod list, so it only runs from the main menu.");
            return null;
        }

        // Called once the player has seen and accepted the plan; reviewedProfile is the list that review showed.
        public static void Start(ServerCardDto card, List<string> optionalIds, bool acceptUnverifiedRwt, ModProfileDto reviewedProfile)
        {
            if (card == null || string.IsNullOrEmpty(card.ServerId) || RefusalToStart() != null) return;
            // Nothing here can bridge two RimWorld versions, so nothing on disk is touched for such a server.
            if (Compat.KmhGameVersion.CompareWithMine(card.RimWorldVersion) == Compat.GameMatch.Differs) return;
            DateTime now = DateTime.UtcNow;
            _job = new KmhPrepareJob
            {
                ServerId = card.ServerId,
                ServerName = card.Name ?? "",
                Endpoint = card.JoinEndpoint ?? "",
                RwtVersion = card.RwtVersion ?? "",
                GameVersion = Compat.KmhGameVersion.MajorMinor,
                CreatedUnix = new DateTimeOffset(now).ToUnixTimeSeconds(),
                OptionalChosen = optionalIds ?? new List<string>(),
                AcceptUnverifiedRwt = acceptUnverifiedRwt,
                Approved = true,
                ApprovedPolicy = card.ModPolicy ?? "",
                ApprovedProfile = ProfileDigest(reviewedProfile),
            };
            ResetSession();
            _startedUtc = now;
            KmhPrepareJobStore.Save(_job, now);
            KmhLog.Info($"Join preparation started for {_job.ServerName} ({_job.ServerId}).");
            _next = DateTime.MinValue;
        }

        public static void Cancel()
        {
            if (_job == null) return;
            KmhLog.Info($"Join preparation for {_job.ServerName} cancelled by the player.");
            if (Rwt.State == PrepLineState.Working && Features.Rwt.RwtInstaller.Busy) Features.Rwt.RwtInstaller.Cancel();
            _job = null;
            KmhPrepareJobStore.Clear();
            ResetSession();
            Headline = KmhText.Get("KMH_Prep_Cancelled", "Cancelled. Anything already downloaded stays; your mod list can be restored below.");
        }

        public static void Retry()
        {
            if (_job == null) return;
            _failed = false;
            _job.LastError = "";
            _subscribedAt.Clear(); _nudgedAt.Clear(); _progress.Clear();
            _rwtStarted = false; _loginPrompted = false; _joinAtUtc = DateTime.MinValue;
            if (_job.Stage == KmhPrepareStage.Connecting) _job.Stage = KmhPrepareStage.Running;
            if (Features.Rwt.RwtInstaller.Stage == Features.Rwt.RwtInstallStage.Failed) Features.Rwt.RwtInstaller.ResetFailure();
            ModProfileClient.Invalidate();
            KmhSubscribePlan.Invalidate();
            _startedUtc = DateTime.UtcNow;
            KmhPrepareJobStore.Save(_job, DateTime.UtcNow);
            _next = DateTime.MinValue;
        }

        public static void RestartNow()
        {
            if (_job == null || _job.Stage != KmhPrepareStage.Restarting) return;
            _job.Restarts++;
            if (!KmhPrepareJobStore.Save(_job, DateTime.UtcNow))
            { Fail(KmhText.Get("KMH_Prep_NotSaved", "Colony Nexus could not write down where it got to, so it will not restart RimWorld for you. Restart it yourself and choose the server again.")); return; }
            KmhLog.Info($"Join preparation for {_job.ServerName}: restarting RimWorld (restart {_job.Restarts}).");
            try { GenCommandLine.Restart(); }
            catch (Exception ex)
            {
                KmhLog.Warn($"Join preparation: restart failed: {ex.Message}");
                Fail(KmhText.Get("KMH_Prep_RestartFailed", "Colony Nexus could not restart RimWorld. Close and reopen it yourself; preparation carries on when it starts."));
            }
        }

        public static void JoinNowInstead() { if (_joinAtUtc != DateTime.MinValue) _joinAtUtc = DateTime.UtcNow; }

        public static void StopAutoJoin()
        {
            if (_job == null) return;
            _job.AutoJoin = false;
            _joinAtUtc = DateTime.MinValue;
            KmhPrepareJobStore.Save(_job, DateTime.UtcNow);
        }

        // Shared with Config Enforcement, so several servers in a row still restore the player's own list.
        public static bool CanRestoreModList => Features.Enforcement.ModsConfigJournal.HasOriginal(Features.Enforcement.EnforcementProfileApplier.ModsJournalRoot)
                                             || Features.Rwt.RwtCopies.HasOriginal;

        // The RWT copy KMH put in Mods goes too: it outranks the Workshop copy in every list, restored ones included.
        public static bool RestoreModList(out string why) => RestoreModList(out why, out _);

        public static bool RestoreModList(out string why, out Features.Enforcement.ModsConfigJournal.RestoreOutcome outcome)
        {
            bool ok = Features.Enforcement.ModsConfigJournal.RestoreOriginal(Features.Enforcement.EnforcementProfileApplier.ModsJournalRoot, out why, out outcome);
            // Half done is said as half done: the list is back even when the RWT copy is not.
            if (ok && !Features.Rwt.RwtCopies.RestoreOriginal(out bool _, out string rwtWhy))
            {
                ok = false;
                why = KmhText.Get("KMH_Restore_ListOnly", "Your mod list was put back, but the RimWorld Together copy could not be: {0}", rwtWhy);
                KmhLog.Warn($"Join preparation: the mod list was restored ({outcome}) but the RimWorld Together copy was not: {rwtWhy}");
            }
            if (ok) KmhLog.Info($"Join preparation: restore finished ({outcome}); the RimWorld Together copy was put back and both load on the next restart.");
            return ok;
        }

        // Said instead of "restored" when nothing of the player's could be put back, so they are never told it was.
        public static string RestoreOutcomeNote(Features.Enforcement.ModsConfigJournal.RestoreOutcome outcome)
        {
            switch (outcome)
            {
                case Features.Enforcement.ModsConfigJournal.RestoreOutcome.KeptYourChange:
                    return KmhText.Get("KMH_Restore_KeptChange", "You changed your mod list after Colony Nexus set it, so your current list was kept. The list from before is still saved: \"Recover an older mod list\" in Colony Nexus's settings can bring it back.");
                case Features.Enforcement.ModsConfigJournal.RestoreOutcome.SavedCopyWasAServerList:
                    return KmhText.Get("KMH_Restore_CopyWasServers", "The copy Colony Nexus kept of your own mod list turned out to be a server's list: an earlier Colony Nexus build replaced it. Your current list was kept; the lists that can still be recovered are shown next.");
                default: return "";
            }
        }

        private static void ResetSession()
        {
            _verified = false; _failed = false; _loginPrompted = false; _rwtStarted = false; _listWrittenThisBoot = false; _rwtStagedThisBoot = false;
            _sawConnecting = false; _connectUtc = DateTime.MinValue; _joinAtUtc = DateTime.MinValue;
            _subscribedAt.Clear(); _nudgedAt.Clear(); _progress.Clear();
            Finished = false;
            foreach (PrepLine l in Lines) { l.State = PrepLineState.Pending; l.Detail = ""; l.Progress = -1f; }
            Check.Label = KmhText.Get("KMH_Prep_Line_Check", "Read the server's requirements");
            Rwt.Label = KmhText.Get("KMH_Prep_Line_Rwt", "RimWorld Together version");
            Workshop.Label = KmhText.Get("KMH_Prep_Line_Workshop", "Workshop mods");
            ModList.Label = KmhText.Get("KMH_Prep_Line_ModList", "Mod list and load order");
            Restart.Label = KmhText.Get("KMH_Prep_Line_Restart", "Restart RimWorld");
            Account.Label = KmhText.Get("KMH_Prep_Line_Account", "RimWorld Together account");
            Connect.Label = KmhText.Get("KMH_Prep_Line_Connect", "Join the server");
            Headline = "";
        }

        // Main-thread pump, every frame; idle between half-second steps.
        public static void Tick()
        {
            DateTime now = DateTime.UtcNow;
            if (!_bootChecked) { BootCheck(now); return; }
            if (_job == null || now < _next) return;
            _next = now + Every;
            try { Advance(now); }
            catch (Exception ex)
            {
                KmhLog.Error($"Join preparation: step failed: {ex}");
                Fail(KmhText.Get("KMH_Prep_Crashed", "Something went wrong preparing this server ({0}). Retry, or cancel and join by hand.", ex.GetType().Name));
            }
        }

        // A job found at start-up is one a restart interrupted, on purpose or not.
        private static void BootCheck(DateTime now)
        {
            if (Current.ProgramState != ProgramState.Entry || LongEventHandler.AnyEventNowOrWaiting || Find.WindowStack == null) return;
            _bootChecked = true;
            KmhPrepareJob job = KmhPrepareJobStore.Load(now);
            if (job == null) return;
            _job = job;
            ResetSession();
            _startedUtc = now;
            bool wasRestarting = job.Stage == KmhPrepareStage.Restarting;
            _justRestarted = wasRestarting || job.Restarts > 0;
            if (job.Stage != KmhPrepareStage.Running) job.Stage = KmhPrepareStage.Running;
            // RimWorld scanned its mods on this start, so the downloads it could not see before are visible now.
            if (_justRestarted) job.DiscoverAfterRestart = false;
            KmhPrepareJobStore.Save(job, now);
            // A job that failed before RimWorld closed comes back failed; only a restart KMH asked for carries on.
            if (ResumesFailed(wasRestarting, job.LastError))
            {
                _failed = true;
                Headline = KmhText.Get("KMH_Prep_ResumedFailed", "Preparing {0} stopped last time: {1} Retry, or cancel it.", Inert(job.ServerName), KmhDisplayText.Inert(job.LastError, 400));
                KmhLog.Info($"Join preparation for {job.ServerName} found failed ({job.LastError}); waiting for Retry or Cancel.");
            }
            else KmhLog.Info($"Join preparation for {job.ServerName} resumed after a restart ({job.Restarts} so far).");
            Find.WindowStack.Add(new Dialog_KMHPrepareJoin());
        }

        internal static bool ResumesFailed(bool wasRestarting, string lastError) => !wasRestarting && !string.IsNullOrEmpty(lastError);

        internal static string ChangedSinceApproval(ServerCardDto card, bool enforced, ModProfileDto profile)
        {
            string again = KmhText.Get("KMH_Prep_ChangedSinceApproval", "This server changed what it asks for since you accepted ({0}). Nothing more was changed on this computer. Cancel, then open Prepare and join again to see the new plan.");
            if (_job == null || !_job.Approved) return string.Format(again, KmhText.Get("KMH_Prep_Changed_Old", "this preparation was started by an older Colony Nexus"));
            if (!Features.Rwt.RwtVersionText.Same(card.RwtVersion ?? "", _job.RwtVersion ?? ""))
                return string.Format(again, KmhText.Get("KMH_Prep_Changed_Rwt", "RimWorld Together {0} instead of {1}", Inert(card.RwtVersion ?? "?"), Inert(_job.RwtVersion ?? "?")));
            if (!string.Equals(card.ModPolicy ?? "", _job.ApprovedPolicy ?? "", StringComparison.Ordinal))
                return string.Format(again, KmhText.Get("KMH_Prep_Changed_Policy", "its mod rules"));
            if (enforced && !string.Equals(ProfileDigest(profile), _job.ApprovedProfile ?? "", StringComparison.Ordinal))
                return string.Format(again, KmhText.Get("KMH_Prep_Changed_List", "its mod list"));
            return null;
        }

        // Computed here over everything the plan is built from; the server's own hash is the server's word.
        internal static string ProfileDigest(ModProfileDto p)
        {
            if (p == null) return "";
            var sb = new System.Text.StringBuilder("kmh.prepare.approval.v1\n");
            sb.Append(p.Policy).Append('\n').Append(p.EnforceOrder).Append('\n');
            foreach (string s in p.Order ?? new List<string>()) sb.Append("o:").Append(s).Append('\n');
            foreach (string s in p.Required ?? new List<string>()) sb.Append("r:").Append(s).Append('\n');
            foreach (string s in p.Optional ?? new List<string>()) sb.Append("p:").Append(s).Append('\n');
            foreach (ModEntryDto e in p.Entries ?? new List<ModEntryDto>())
                if (e != null) sb.Append("e:").Append(e.Name).Append('|').Append(e.Id).Append('|').Append(e.PackageId)
                                 .Append('|').Append(e.Role).Append('|').Append(e.Kind).Append('\n');
            return Features.Enforcement.ConfigProfileUtility.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static void Advance(DateTime now)
        {
            if (_failed || Finished) return;
            if (_job.Stage == KmhPrepareStage.Connecting) { WatchConnect(now); return; }
            if (_job.Stage == KmhPrepareStage.Restarting) { Headline = KmhText.Get("KMH_Prep_Headline_Restart", "Ready for a restart. RimWorld loads the new setup when it starts again, then Colony Nexus carries on."); return; }
            // The mod list is RimWorld's own file; it is only ever written from the main menu.
            if (Current.ProgramState != ProgramState.Entry)
            { Headline = KmhText.Get("KMH_Prep_Headline_Menu", "Paused while a game is open. Return to the main menu to carry on."); return; }

            ServerCardDto card = KmhDirectoryClient.CardById(_job.ServerId);
            if (card == null)
            {
                KmhDirectoryClient.EnsureCardsFor(new[] { _job.ServerId });
                Set(Check, PrepLineState.Working, KmhText.Get("KMH_Prep_FindingServer", "Looking the server up on Colony Nexus Global…"));
                if (now - _startedUtc > FindServerFor)
                    Fail(KmhText.Get("KMH_Prep_ServerGone", "Colony Nexus Global no longer lists this server, or cannot be reached. Retry later."));
                return;
            }
            _job.Endpoint = card.JoinEndpoint ?? _job.Endpoint;
            CompatState state = KmhCompatibility.Evaluate(card);
            switch (state)
            {
                // A different RimWorld version is the Steam branch, which this flow never switches.
                case CompatState.GameVersionMismatch:
                case CompatState.ProtocolMismatch:
                case CompatState.NoAddress:
                    Fail(KmhCompatibility.Explain(card, state)); return;
                case CompatState.Offline:
                case CompatState.Starting:
                case CompatState.Maintenance:
                case CompatState.Full:
                    Set(Check, PrepLineState.Waiting, KmhCompatibility.Explain(card, state));
                    Headline = KmhText.Get("KMH_Prep_Headline_Waiting", "Waiting for the server. Colony Nexus keeps checking; cancel any time.");
                    return;
            }

            ModProfileClient.EnsureFetched(card);
            ModProfileClient.Entry entry = ModProfileClient.Peek(card.ServerId);
            bool enforced = !ModPolicyNames.EnforcesNothing(card.ModPolicy);
            if (enforced && (entry == null || entry.Pending))
            { Set(Check, PrepLineState.Working, KmhText.Get("KMH_Prep_ReadingMods", "Reading the server's mod list…")); return; }
            ModProfileDto profile = entry?.Profile;
            ModComparison c = enforced ? entry?.Comparison : null;
            if (enforced && profile != null && c == null)
            { Set(Check, PrepLineState.Working, KmhText.Get("KMH_Prep_ReadingMods", "Reading the server's mod list…")); return; }
            // The player approved one plan; a server that changes its list or RWT version afterwards gets a new review,
            // never downloads or switches the player did not see.
            string changed = ChangedSinceApproval(card, enforced, profile);
            if (changed != null) { Fail(changed); return; }
            Set(Check, PrepLineState.Done, enforced && profile == null
                ? KmhText.Get("KMH_Prep_NoModList", "The server has not published its mod list; RimWorld Together checks it when you join.")
                : "");

            if (c != null && c.MissingExpansions.Count > 0)
            { Fail(KmhCompatibility.Explain(card, CompatState.ModsNeedExpansion) + " " + Names(c.MissingExpansions)); return; }
            if (c != null && c.MissingLocal.Count > 0)
            { Fail(KmhText.Get("KMH_Prep_Manual", "These mods are not on the Steam Workshop and have to be installed by hand: {0}", Names(c.MissingLocal))); return; }

            // RWT runs beside the Workshop downloads; the list is written only once both have settled.
            bool rwtSettled = StepRwt(card, now, out bool rwtRestart);
            if (_failed) return;

            bool workshopSettled = StepWorkshop(c, now);
            if (_failed) return;

            bool listRestart = false;
            if (!rwtSettled || !workshopSettled)
                Set(ModList, PrepLineState.Pending, KmhText.Get("KMH_Prep_ListAfter", "Set once the downloads above have finished."));
            else if (!StepModList(profile, c, out listRestart)) return;

            if (!rwtSettled || !workshopSettled) { Headline = KmhText.Get("KMH_Prep_Headline_Working", "Preparing. You can leave this window open or close it; Colony Nexus keeps going."); return; }

            if (rwtRestart || listRestart || _job.DiscoverAfterRestart)
            {
                if (_job.Restarts >= MaxRestarts)
                { Fail(KmhText.Get("KMH_Prep_TooManyRestarts", "RimWorld has restarted {0} times and this server's requirements are still not met. Check the details above, or restore your mod list.", _job.Restarts)); return; }
                _job.Stage = KmhPrepareStage.Restarting;
                KmhPrepareJobStore.Save(_job, now);
                Set(Restart, PrepLineState.Waiting, KmhText.Get("KMH_Prep_RestartNeeded", "RimWorld has to restart to load the new setup."));
                Headline = KmhText.Get("KMH_Prep_Headline_Restart", "Ready for a restart. RimWorld loads the new setup when it starts again, then Colony Nexus carries on.");
                return;
            }

            if (_justRestarted && !_verified)
            {
                List<string> problems = VerifyLoaded(_job.ExpectedOrder, KmhLocalMods.ActivePackageIds(), _job.ExpectedRwt, KmhRwtVersions.Mine);
                if (problems.Count > 0)
                { Fail(KmhText.Get("KMH_Prep_VerifyFailed", "RimWorld did not load what Colony Nexus set up: {0}", string.Join("; ", problems.ToArray()))); return; }
                _verified = true;
                Set(Restart, PrepLineState.Done, KmhText.Get("KMH_Prep_Verified", "RimWorld loaded the new setup."));
            }
            else if (Restart.State == PrepLineState.Pending) Set(Restart, PrepLineState.Skipped, KmhText.Get("KMH_Prep_NoRestart", "Not needed."));

            // The verdict on this machine's running setup, which is what the server checks at the door.
            CompatState now2 = KmhCompatibility.Evaluate(card);
            if (KmhCompatibility.IsBlocking(now2))
            {
                if (now2 == CompatState.Checking) { Set(Connect, PrepLineState.Working, KmhText.Get("KMH_Prep_Rechecking", "Checking again…")); return; }
                Fail(KmhCompatibility.Explain(card, now2)); return;
            }

            // RWT's Join asks for its own account before connecting.
            bool? login = RwtCompat.LoginLooksValid();
            if (login == false)
            {
                if (!_loginPrompted) { _loginPrompted = RwtCompat.PromptLogin(); }
                Set(Account, PrepLineState.Waiting, KmhText.Get("KMH_Prep_Account", "RimWorld Together needs a player name and password before it can join. Fill in its Account Setup window; Colony Nexus carries on when it is saved."));
                Headline = KmhText.Get("KMH_Prep_Headline_Account", "Waiting for your RimWorld Together account.");
                if (!_loginPrompted) Fail(KmhText.Get("KMH_Prep_NoAccountPrompt", "RimWorld Together has no saved account and Colony Nexus could not open its account window. Use RimWorld Together's own Join once to create one."));
                return;
            }
            Set(Account, PrepLineState.Done, "");

            // A visible countdown, so nobody is pulled into a server unawares.
            if (!_job.AutoJoin)
            {
                Set(Connect, PrepLineState.Waiting, KmhText.Get("KMH_Prep_ReadyManual", "Everything is ready. Press Join now."));
                Headline = KmhText.Get("KMH_Prep_Headline_Ready", "Ready to join.");
                return;
            }
            if (_joinAtUtc == DateTime.MinValue) _joinAtUtc = now + JoinCountdown;
            if (now < _joinAtUtc)
            {
                Set(Connect, PrepLineState.Waiting, KmhText.Get("KMH_Prep_JoiningIn", "Joining in {0} s…", JoinCountdownLeft));
                Headline = KmhText.Get("KMH_Prep_Headline_Ready", "Ready to join.");
                return;
            }
            BeginConnect(card, now);
        }

        private static bool StepRwt(ServerCardDto card, DateTime now, out bool needsRestart)
        {
            needsRestart = false;
            string want = card.RwtVersion ?? "";
            if (KmhRwtVersions.Supports(want))
            {
                Set(Rwt, PrepLineState.Done, string.Format(KmhText.Get("KMH_Prep_RwtMatches", "{0} - matches the server."), Show(KmhRwtVersions.Mine)));
                return true;
            }
            if (Features.Rwt.RwtVersionText.Same(_job.ExpectedRwt, want))
            {
                // Staged before this start and still not what loaded: another restart would not change that.
                if (RwtStagedButNotLoaded(_rwtStagedThisBoot, _justRestarted))
                {
                    Fail(KmhText.Get("KMH_Prep_VerifyFailed", "RimWorld did not load what Colony Nexus set up: {0}",
                        KmhText.Get("KMH_Prep_RwtStillOld", "RimWorld Together is {0}, not {1}", Show(KmhRwtVersions.Mine), want)));
                    return false;
                }
                needsRestart = true;
                Set(Rwt, PrepLineState.Done, KmhText.Get("KMH_Prep_RwtStaged", "{0} is installed and loads after the restart.", want));
                return true;
            }

            var stage = Features.Rwt.RwtInstaller.Stage;
            if (Features.Rwt.RwtInstaller.Busy)
            {
                Set(Rwt, PrepLineState.Working, KmhText.Get("KMH_Prep_RwtBusy", "Downloading RimWorld Together {0}: {1}", want, RwtStageText(stage)));
                return false;
            }
            if (_rwtStarted && stage == Features.Rwt.RwtInstallStage.Done && Features.Rwt.RwtVersionText.Same(Features.Rwt.RwtInstaller.ActivatedVersion, want))
            {
                needsRestart = true;
                return StagedRwt(Features.Rwt.RwtInstaller.ActivatedVersion, want, now);
            }
            if (_rwtStarted && stage == Features.Rwt.RwtInstallStage.Failed)
            { Fail(KmhText.Get("KMH_Prep_RwtFailed", "RimWorld Together {0} could not be installed: {1}", want, Features.Rwt.RwtInstaller.Detail)); return false; }

            // Reuse a copy already on disk (Workshop or set aside) rather than download it again into Mods.
            if (!_rwtStarted)
            {
                Features.Rwt.RwtSwitchKind kind = Features.Rwt.RwtCopies.DecideOnDisk(want, out string previous);
                if (kind != Features.Rwt.RwtSwitchKind.Download)
                {
                    string done = null, why = "";
                    switch (kind)
                    {
                        case Features.Rwt.RwtSwitchKind.RestartOnly:
                            done = KmhText.Get("KMH_Prep_RwtStaged", "{0} is installed and loads after the restart.", want);
                            break;
                        case Features.Rwt.RwtSwitchKind.UseWorkshop:
                            if (Features.Rwt.RwtCopies.UseWorkshop(out why))
                                done = KmhText.Get("KMH_Prep_RwtWorkshop", "Your Steam Workshop copy ({0}) loads after the restart - nothing to download.", want);
                            break;
                        case Features.Rwt.RwtSwitchKind.UsePrevious:
                            if (Features.Rwt.RwtCopies.UsePrevious(previous, out why))
                                done = KmhText.Get("KMH_Prep_RwtPrevious", "{0}, which Colony Nexus installed before, loads after the restart - nothing to download.", want);
                            break;
                    }
                    if (done == null)
                    { Fail(KmhText.Get("KMH_Prep_RwtSwitchFailed", "Colony Nexus could not switch RimWorld Together to {0}: {1}", want, Inert(why))); return false; }
                    Features.Rwt.RwtInstaller.NoteStaged(want);
                    _job.ExpectedRwt = want;
                    _rwtStagedThisBoot = true;
                    KmhPrepareJobStore.Save(_job, now);
                    KmhLog.Info($"Join preparation for {_job.ServerName}: RimWorld Together {want} staged ({kind}).");
                    needsRestart = true;
                    Set(Rwt, PrepLineState.Done, done);
                    return true;
                }
            }

            Features.Rwt.RwtReleaseCatalog.EnsureLoaded();
            Features.Rwt.RwtReleaseCatalog.EnsureExact(want);
            Features.Rwt.RwtInstallPlan plan = Features.Rwt.RwtResolver.PlanClient(Features.Rwt.RwtVersion.From(want), Features.Rwt.RwtVersion.From(KmhRwtVersions.Mine));
            if (plan.State == Features.Rwt.RwtPlanState.AlreadyInstalled && plan.PendingRestart)
            {
                needsRestart = true;
                return StagedRwt(want, want, now);
            }
            if (!plan.CanInstall)
            {
                bool looking = Features.Rwt.RwtReleaseCatalog.State == Features.Rwt.RwtReleaseCatalog.CatalogState.Fetching
                    || Features.Rwt.RwtReleaseCatalog.State == Features.Rwt.RwtReleaseCatalog.CatalogState.Unknown
                    || Features.Rwt.RwtReleaseCatalog.ExactPending(want);
                if (looking) { Set(Rwt, PrepLineState.Working, KmhText.Get("KMH_Prep_RwtLookup", "Looking up RimWorld Together {0} on its official release page…", want)); return false; }
                Fail(plan.Explain());
                return false;
            }
            if (plan.NeedsOptIn && !_job.AcceptUnverifiedRwt)
            {
                Fail(plan.Verifiable
                    ? plan.UnusualText() + " " + KmhText.Get("KMH_Prep_RwtOptInAgain", "Start again from the browser and accept installing it, or install it yourself.")
                    : KmhText.Get("KMH_Prep_RwtUnverified", "RimWorld Together {0} has no published checksum. Start again from the browser and accept installing it unverified, or install it yourself.", want));
                return false;
            }
            _rwtStarted = true;
            Features.Rwt.RwtInstaller.Begin(plan, acceptUnverified: _job.AcceptUnverifiedRwt);
            Set(Rwt, PrepLineState.Working, KmhText.Get("KMH_Prep_RwtBusy", "Downloading RimWorld Together {0}: {1}", want, RwtStageText(Features.Rwt.RwtInstallStage.Downloading)));
            return false;
        }

        private static bool StagedRwt(string expected, string want, DateTime now)
        {
            _job.ExpectedRwt = expected;
            _rwtStagedThisBoot = true;
            KmhPrepareJobStore.Save(_job, now);
            Set(Rwt, PrepLineState.Done, KmhText.Get("KMH_Prep_RwtStaged", "{0} is installed and loads after the restart.", want));
            return true;
        }

        private static bool StepWorkshop(ModComparison c, DateTime now)
        {
            List<ModEntryDto> items = new List<ModEntryDto>();
            if (c != null)
            {
                foreach (ModEntryDto e in c.Subscribable()) items.Add(e);
                foreach (ModEntryDto e in c.OptionalSubscribable())
                    if (_job.OptionalChosen.Contains(e.Id)) items.Add(e);
            }
            if (items.Count == 0 && _job.Subscribed.Count == 0)
            {
                Set(Workshop, PrepLineState.Done, KmhText.Get("KMH_Prep_NothingToDownload", "Nothing to download."));
                return true;
            }
            if (items.Count > 0 && !KmhWorkshop.Available)
            { Fail(KmhText.Get("KMH_Prep_NoSteam", "Steam is not running or not signed in, so the server's Workshop mods cannot be subscribed. Start Steam, then Retry.")); return false; }

            int installed = 0, total = items.Count;
            ulong bytesDone = 0, bytesTotal = 0;
            string current = "";
            List<string> unknownToRimWorld = new List<string>();
            List<InstalledMod> known = KmhLocalMods.InstalledCached();
            foreach (ModEntryDto e in items)
            {
                WorkshopItemState s = KmhWorkshop.StateOf(e.Id);
                switch (WorkshopAction(s, _subscribedAt.TryGetValue(e.Id, out DateTime at) ? at : (DateTime?)null, now, SubscribeGrace))
                {
                    case WorkshopStep.Subscribe:
                        if (KmhWorkshopDetails.Get(e.Id)?.Refused == true)
                        { Fail(KmhText.Get("KMH_Prep_WorkshopRefused", "Steam does not list {0} (Workshop {1}) as a RimWorld mod, so it cannot be subscribed.", Inert(e.Name), e.Id)); return false; }
                        if (KmhWorkshop.Subscribe(e.Id))
                        {
                            _subscribedAt[e.Id] = now;
                            if (!_job.Subscribed.Contains(e.Id)) { _job.Subscribed.Add(e.Id); KmhPrepareJobStore.Save(_job, now); }
                        }
                        else { Fail(KmhText.Get("KMH_Prep_SubscribeRefused", "Steam refused the subscription to {0} (Workshop {1}).", Inert(e.Name), e.Id)); return false; }
                        current = e.Name;
                        break;
                    case WorkshopStep.NotAccepted:
                        Fail(KmhText.Get("KMH_Prep_SubscribeIgnored", "Steam did not take the subscription to {0} (Workshop {1}). The item may have been removed, made private, or be unavailable in your region.", Inert(e.Name), e.Id));
                        return false;
                    case WorkshopStep.WaitSubscribe:
                        current = e.Name;
                        break;
                    case WorkshopStep.RequestDownload:
                        if (!_nudgedAt.TryGetValue(e.Id, out DateTime nudged) || now - nudged > DownloadNudge)
                        { KmhWorkshop.RequestDownload(e.Id); _nudgedAt[e.Id] = now; }
                        current = e.Name;
                        break;
                    case WorkshopStep.Downloading:
                        current = e.Name;
                        if (KmhWorkshop.DownloadInfo(e.Id, out ulong d, out ulong t))
                        {
                            bytesDone += d; bytesTotal += t;
                            if (!_progress.TryGetValue(e.Id, out Progress p)) _progress[e.Id] = p = new Progress { Bytes = d, ChangedUtc = now };
                            if (d != p.Bytes) { p.Bytes = d; p.ChangedUtc = now; }
                            else if (now - p.ChangedUtc > StallAfter)
                            {
                                p.Stalls++; p.ChangedUtc = now;
                                KmhLog.Warn($"Join preparation: Workshop {e.Id} has not moved for {StallAfter.TotalSeconds:0} s (stall {p.Stalls}); asking Steam again.");
                                if (p.Stalls >= MaxStalls)
                                { Fail(KmhText.Get("KMH_Prep_Stalled", "The download of {0} (Workshop {1}) has stopped moving. Check Steam's Downloads page, then Retry.", Inert(e.Name), e.Id)); return false; }
                                KmhWorkshop.RequestDownload(e.Id);
                            }
                        }
                        break;
                    case WorkshopStep.Done:
                        installed++;
                        if (!ModComparison.IsInstalled(e, known)) unknownToRimWorld.Add(e.Name);
                        break;
                    default:
                        Fail(KmhText.Get("KMH_Prep_NoSteam", "Steam is not running or not signed in, so the server's Workshop mods cannot be subscribed. Start Steam, then Retry."));
                        return false;
                }
            }

            if (installed < total)
            {
                string detail = KmhText.Get("KMH_Prep_Downloading", "{0} of {1} on disk", installed, total)
                              + (bytesTotal > 0 ? $"  ·  {bytesDone / 1048576f:0.0} / {bytesTotal / 1048576f:0.0} MB" : "")
                              + (current.Length > 0 ? "  ·  " + Inert(current) : "");
                Set(Workshop, PrepLineState.Working, detail);
                Workshop.Progress = bytesTotal > 0 ? (float)bytesDone / bytesTotal : (float)installed / Math.Max(1, total);
                return false;
            }

            // On disk is not known to RimWorld: it only lists mods when it scans, so it is asked to scan once.
            if (unknownToRimWorld.Count > 0)
            {
                if (!_job.Rescanned)
                {
                    _job.Rescanned = true;
                    KmhPrepareJobStore.Save(_job, now);
                    try { ModLister.RebuildModList(); }
                    catch (Exception ex) { KmhLog.Warn($"Join preparation: RimWorld's mod rescan failed: {ex.Message}"); }
                    KmhLocalMods.Rescanned();
                    ModProfileClient.Invalidate();
                    KmhSubscribePlan.Invalidate();
                    Set(Workshop, PrepLineState.Working, KmhText.Get("KMH_Prep_Rescanning", "Downloaded. Asking RimWorld to find the new mods…"));
                    return false;
                }
                if (!_job.DiscoverAfterRestart)
                {
                    _job.DiscoverAfterRestart = true;
                    KmhPrepareJobStore.Save(_job, now);
                    KmhLog.Info($"Join preparation: {unknownToRimWorld.Count} downloaded mod(s) are only found after a restart.");
                }
                Set(Workshop, PrepLineState.Done, KmhText.Get("KMH_Prep_FoundOnRestart", "Downloaded. RimWorld finds them when it restarts."));
                return true;
            }
            Set(Workshop, PrepLineState.Done, KmhText.Get("KMH_Prep_Downloaded", "{0} mod(s) on disk.", DownloadedCount(total, _job.Subscribed.Count)));
            return true;
        }

        // RimWorld's rescan drops found mods from the to-subscribe list, so this job's subscriptions count too.
        internal static int DownloadedCount(int stillListed, int subscribedThisJob) => Math.Max(stillListed, subscribedThisJob);

        internal enum WorkshopStep { Subscribe, WaitSubscribe, NotAccepted, RequestDownload, Downloading, Done, Unavailable }

        internal static WorkshopStep WorkshopAction(WorkshopItemState s, DateTime? subscribedAtUtc, DateTime nowUtc, TimeSpan grace)
        {
            switch (s)
            {
                case WorkshopItemState.NotSubscribed:
                    if (subscribedAtUtc == null) return WorkshopStep.Subscribe;
                    return nowUtc - subscribedAtUtc.Value > grace ? WorkshopStep.NotAccepted : WorkshopStep.WaitSubscribe;
                case WorkshopItemState.Queued:
                case WorkshopItemState.NeedsUpdate: return WorkshopStep.RequestDownload;
                case WorkshopItemState.Downloading: return WorkshopStep.Downloading;
                case WorkshopItemState.Installed: return WorkshopStep.Done;
                default: return WorkshopStep.Unavailable;
            }
        }

        // Returns false when it failed or is still waiting.
        private static bool StepModList(ModProfileDto profile, ModComparison c, out bool needsRestart)
        {
            needsRestart = false;
            // Only a list written this boot needs a restart; asking again for an older one would restart forever.
            if (_job.ListWritten)
            {
                needsRestart = ListStillNeedsRestart(_job.ListWritten, _listWrittenThisBoot);
                Set(ModList, PrepLineState.Done, needsRestart ? KmhText.Get("KMH_Prep_ListWritten", "Set. RimWorld loads it after the restart.") : KmhText.Get("KMH_Prep_ListLoaded", "Loaded."));
                return true;
            }
            if (_job.DiscoverAfterRestart && !_justRestarted)
            {
                Set(ModList, PrepLineState.Pending, KmhText.Get("KMH_Prep_ListAfterRestart", "Set after the restart, once RimWorld has found the new mods."));
                return true;
            }
            if (profile == null || c == null)
            {
                bool repointOnly = !string.IsNullOrEmpty(_job.ExpectedRwt);
                if (!repointOnly) { Set(ModList, PrepLineState.Skipped, KmhText.Get("KMH_Prep_NoListToSet", "Nothing to set.")); return true; }
            }

            List<string> order;
            List<string> enable = new List<string>(), disable = new List<string>();
            bool changes;
            List<string> active = KmhLocalMods.ActiveOrder();
            if (profile != null && c != null && profile.Entries.Count > 0)
            {
                KmhModPlan plan = KmhModPlan.Build(profile, KmhLocalMods.InstalledCached(), active, _job.OptionalChosen);
                if (!plan.Ok)
                {
                    if (c.Blocking || plan.NotInstalled.Count > 0)
                    { Fail(KmhText.Get("KMH_Prep_ListBlocked", "Colony Nexus cannot build a mod list for this server: {0}.", Inert(plan.Blocked))); return false; }
                    Set(ModList, PrepLineState.Skipped, KmhText.Get("KMH_Prep_NoListToSet", "Nothing to set."));
                    return true;
                }
                order = plan.Order; enable = plan.WillEnable; disable = plan.WillDisable; changes = plan.Changes;
            }
            else
            {
                order = new List<string>(active);
                changes = false;
            }

            // After restart a fresh local RWT copy takes the plain id and Workshop's becomes "_steam"; a list
            // still naming "_steam" would load the old version again.
            List<string> repointed = RepointRwt(order, !string.IsNullOrEmpty(_job.ExpectedRwt));
            bool repoint = !Features.Enforcement.ModSyncApply.SameSequence(repointed, order);
            order = repointed;
            if (!changes && !repoint)
            {
                Set(ModList, PrepLineState.Done, KmhText.Get("KMH_Prep_ListMatches", "Already matches the server."));
                return true;
            }

            string root = Features.Enforcement.EnforcementProfileApplier.ModsJournalRoot;
            Features.Enforcement.ModsConfigJournal.Begin(root, "browser:" + _job.ServerId);
            Features.Enforcement.ModSyncApply.Result r = Features.Enforcement.ModSyncApply.Apply(order);
            if (!r.Applied)
            {
                Features.Enforcement.ModsConfigJournal.RollBack(root);
                Fail(KmhText.Get("KMH_Prep_ListFailed", "Colony Nexus could not write your mod list: {0}", Inert(r.Failure)));
                return false;
            }
            if (r.Changed) Features.Enforcement.ModsConfigJournal.Commit(root, order);
            else Features.Enforcement.ModsConfigJournal.Discard(root);

            _job.ListWritten = r.Changed;
            _listWrittenThisBoot = r.Changed;
            _job.ExpectedOrder = new List<string>(order);
            KmhPrepareJobStore.Save(_job, DateTime.UtcNow);
            KmhLog.Info($"Join preparation for {_job.ServerName}: mod list written ({enable.Count} on, {disable.Count} off, {order.Count} active).");
            needsRestart = r.Changed;
            string summary = KmhText.Get("KMH_Prep_ListSet", "Set: {0} switched on, {1} switched off. Your own list is kept and can be restored.", enable.Count, disable.Count);
            Set(ModList, PrepLineState.Done, summary);
            ModProfileClient.Invalidate();
            return true;
        }

        internal static bool ListStillNeedsRestart(bool listWritten, bool writtenThisBoot) => listWritten && writtenThisBoot;

        internal static bool RwtStagedButNotLoaded(bool stagedThisBoot, bool justRestarted) => justRestarted && !stagedThisBoot;

        internal static List<string> RepointRwt(List<string> order, bool localCopyInstalled)
        {
            List<string> outList = new List<string>(order.Count);
            foreach (string id in order)
                outList.Add(localCopyInstalled && string.Equals(KmhModPlan.Bare(id), KmhModPlan.RwtId, StringComparison.OrdinalIgnoreCase)
                    ? KmhModPlan.RwtId : id);
            return outList;
        }

        internal static List<string> VerifyLoaded(IList<string> expectedOrder, IList<string> running, string expectedRwt, string runningRwt)
        {
            List<string> problems = new List<string>();
            if (expectedOrder != null && expectedOrder.Count > 0)
            {
                HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (running != null) foreach (string r in running) have.Add(KmhModPlan.Bare(r));
                List<string> missing = new List<string>();
                foreach (string e in expectedOrder) if (!have.Contains(KmhModPlan.Bare(e))) missing.Add(e);
                if (missing.Count > 0)
                    problems.Add(KmhText.Get("KMH_Prep_NotLoaded", "not loaded: {0}", string.Join(", ", missing.ToArray())));
            }
            if (!string.IsNullOrEmpty(expectedRwt) && !Features.Rwt.RwtVersionText.Same(expectedRwt, runningRwt))
                problems.Add(KmhText.Get("KMH_Prep_RwtStillOld", "RimWorld Together is {0}, not {1}", Show(runningRwt), expectedRwt));
            return problems;
        }

        private static void BeginConnect(ServerCardDto card, DateTime now)
        {
            _joinAtUtc = DateTime.MinValue;
            Features.Discord.JoinDecision decision = Features.Discord.KmhJoinGate.MayJoin(card, directoryFresh: true, source: Features.Discord.JoinSource.Browser);
            if (!decision.Allowed) { Fail(decision.Explain(card)); return; }
            if (!card.TrySplitEndpoint(out string host, out int port))
            { Fail(KmhText.Get("KMH_Browser_Join_NoAddress", "This server has not published a connection address.")); return; }

            Features.Servers.SeenServersStore.Record(card.ServerId, card.Name, card.JoinEndpoint, card.KmhVersion, card.RwtVersion);
            _job.Stage = KmhPrepareStage.Connecting;
            KmhPrepareJobStore.Save(_job, now);
            _connectUtc = now;
            _sawConnecting = false;
            KmhLog.Info($"Join preparation for {_job.ServerName}: connecting to {host}:{port}.");
            if (!RwtCompat.ConnectTo(host, port, card.ServerId))
            { Fail(KmhText.Get("KMH_Browser_Join_Failed", "RimWorld Together would not start the connection.")); return; }
            Set(Connect, PrepLineState.Working, KmhText.Get("KMH_Prep_Connecting", "Connecting to {0}…", card.JoinEndpoint));
            Headline = KmhText.Get("KMH_Prep_Headline_Connecting", "Joining. RimWorld Together takes it from here.");
        }

        private static void WatchConnect(DateTime now)
        {
            string net = RwtCompat.NetworkState;
            if (net == "Connecting" || net == "Connected") _sawConnecting = true;
            bool joined = SubProtocol.KmhDispatcher.IsKmhServer && string.Equals(SubProtocol.KmhDispatcher.ServerId, _job.ServerId, StringComparison.Ordinal);
            if (joined || (net == "Connected" && now - _connectUtc > TimeSpan.FromSeconds(5)) || Current.ProgramState == ProgramState.Playing)
            {
                Set(Connect, PrepLineState.Done, KmhText.Get("KMH_Prep_Joined", "Joined."));
                KmhLog.Info($"Join preparation for {_job.ServerName}: joined.");
                Finished = true;
                Headline = KmhText.Get("KMH_Prep_Headline_Done", "Done. Have fun on {0}!", Inert(_job.ServerName));
                _job = null;
                KmhPrepareJobStore.Clear();
                return;
            }
            bool dropped = _sawConnecting && net == "Disconnected" && now - _connectUtc > TimeSpan.FromSeconds(3);
            if (dropped || now - _connectUtc > ConnectFor)
            {
                _job.Stage = KmhPrepareStage.Running;
                KmhPrepareJobStore.Save(_job, now);
                Fail(KmhText.Get("KMH_Prep_ConnectFailed", "RimWorld Together could not join (its own message says why - a taken player name, a wrong server password, or a server that stopped answering). Retry when it is sorted."));
            }
        }

        private static void Fail(string why)
        {
            _failed = true;
            _joinAtUtc = DateTime.MinValue;
            Headline = why ?? "";
            if (_job != null)
            {
                _job.LastError = why ?? "";
                KmhPrepareJobStore.Save(_job, DateTime.UtcNow);
            }
            // The step's own text named what it was doing ("downloading…"), which is no longer true.
            foreach (PrepLine l in Lines)
                if (l.State == PrepLineState.Working || l.State == PrepLineState.Waiting) { l.State = PrepLineState.Failed; l.Detail = why ?? ""; l.Progress = -1f; break; }
            KmhLog.Warn($"Join preparation for {_job?.ServerName}: {why}");
        }

        private static void Set(PrepLine line, PrepLineState state, string detail)
        {
            line.State = state;
            line.Detail = detail ?? "";
            if (state != PrepLineState.Working) line.Progress = -1f;
        }

        private static string RwtStageText(Features.Rwt.RwtInstallStage stage)
        {
            switch (stage)
            {
                case Features.Rwt.RwtInstallStage.Verifying:  return KmhText.Get("KMH_Prep_Rwt_Verifying", "checking the download");
                case Features.Rwt.RwtInstallStage.Extracting: return KmhText.Get("KMH_Prep_Rwt_Extracting", "unpacking");
                case Features.Rwt.RwtInstallStage.Activating: return KmhText.Get("KMH_Prep_Rwt_Activating", "installing");
                default:                                       return KmhText.Get("KMH_Prep_Rwt_Downloading", "downloading");
            }
        }

        private static string Names(List<ModEntryDto> mods)
        {
            List<string> names = new List<string>();
            foreach (ModEntryDto e in mods) names.Add(Inert(e.Name));
            return string.Join(", ", names.ToArray());
        }

        private static string Inert(string s) => KmhDisplayText.Inert(s ?? "", 64);
        private static string Show(string s) => string.IsNullOrEmpty(s) ? "?" : s;

        internal static void ResetForTest()
        {
            _job = null; _bootChecked = true; _justRestarted = false;
            ResetSession();
        }
    }
}
