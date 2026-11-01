using System;
using System.Collections.Generic;
using KMHPatch.Features.Delivery;
using KMHPatch.Features.Roadworks;
using KMHPatch.Features.Roadworks.Dto;
using KMHPatch.Features.Treasury.Dto;
using KMHPatch.Features.Wealth;
using KMHPatch.Items;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using A = KMHPatch.Features.Roadworks.KmhRoadReconcile.Applied;
using Act = KMHPatch.Features.Roadworks.KmhRoadReconcile.Action;
using V = KMHPatch.Features.Roadworks.KmhRoadReconcile.Verdict;

namespace KMHPatch.Diagnostics
{
    // Never touches live session state: no capability reads or writes, no network, no game mutation.
    internal static class KmhSelfTest
    {
        // Asked for, never inferred: logging on is not a request for diagnostics, and admission must never wait on them.
        public static void RunOnRequest()
        {
            List<(string name, bool pass, string detail)> results;
            KmhLog.Muted = true;
            try { results = Run(); }
            catch (Exception ex) { KmhLog.Muted = false; KmhLog.Warn($"Colony Nexus self-test threw before completing: {ex.Message}"); return; }
            finally { KmhLog.Muted = false; }

            // Only failures name themselves, or a debug-on server receives every client's PASS lines on each connect.
            int passed = 0;
            foreach (var r in results)
            {
                if (r.pass) passed++;
                else KmhLog.Warn($"  FAIL  {r.name} - {r.detail}");
            }
            string line = $"Colony Nexus client self-test: {passed}/{results.Count} passed";
            if (passed == results.Count) KmhLog.Info(line); else KmhLog.Warn($"{line} (see FAILs above)");

            if (passed == results.Count) Notifications.KmhNotifications.Positive(line);
            else Notifications.KmhNotifications.Negative($"{line} - see the Colony Nexus log");
        }

        // Restore must survive any throw: a leftover SkipDurableFlushForTest stops the REAL mod list being written durably.
        internal readonly struct Suppression : IDisposable
        {
            private readonly bool _send, _http, _flush, _mute, _english;

            private Suppression(bool send, bool http, bool flush, bool mute, bool english)
            { _send = send; _http = http; _flush = flush; _mute = mute; _english = english; }

            public static Suppression Begin()
            {
                var was = new Suppression(SubProtocol.KmhDispatcher.SuppressSendForTest,
                                          Features.Global.GlobalHttp.SuppressForTest,
                                          Persistence.KmhAtomicFile.SkipDurableFlushForTest,
                                          Notifications.KmhNotifications.Muted,
                                          UI.KmhText.EnglishOnlyForTest);
                SubProtocol.KmhDispatcher.SuppressSendForTest = true;
                Features.Global.GlobalHttp.SuppressForTest = true;
                Persistence.KmhAtomicFile.SkipDurableFlushForTest = true;
                Notifications.KmhNotifications.Muted = true;
                UI.KmhText.EnglishOnlyForTest = true;
                return was;
            }

            public void Dispose()
            {
                SubProtocol.KmhDispatcher.SuppressSendForTest = _send;
                Features.Global.GlobalHttp.SuppressForTest = _http;
                Persistence.KmhAtomicFile.SkipDurableFlushForTest = _flush;
                Notifications.KmhNotifications.Muted = _mute;
                UI.KmhText.EnglishOnlyForTest = _english;
            }
        }

        private static void SuppressionLifetime(List<(string, bool, string)> r)
        {
            bool sendOuter  = SubProtocol.KmhDispatcher.SuppressSendForTest;
            bool httpOuter  = Features.Global.GlobalHttp.SuppressForTest;
            bool flushOuter = Persistence.KmhAtomicFile.SkipDurableFlushForTest;
            bool muteOuter  = Notifications.KmhNotifications.Muted;
            bool englishOuter = UI.KmhText.EnglishOnlyForTest;
            try
            {
                // Forced OFF first, or a scope that simply left everything set (a nested run) passes as a restore.
                SubProtocol.KmhDispatcher.SuppressSendForTest = false;
                Features.Global.GlobalHttp.SuppressForTest = false;
                Persistence.KmhAtomicFile.SkipDurableFlushForTest = false;
                Notifications.KmhNotifications.Muted = false;
                UI.KmhText.EnglishOnlyForTest = false;

                bool setInside = false, threw = false;
                try
                {
                    using (Suppression.Begin())
                    {
                        setInside = SubProtocol.KmhDispatcher.SuppressSendForTest
                                 && Features.Global.GlobalHttp.SuppressForTest
                                 && Persistence.KmhAtomicFile.SkipDurableFlushForTest
                                 && Notifications.KmhNotifications.Muted
                                 && UI.KmhText.EnglishOnlyForTest;
                        throw new InvalidOperationException("selftest: deliberate");
                    }
                }
                catch (InvalidOperationException) { threw = true; }

                bool sendBack  = !SubProtocol.KmhDispatcher.SuppressSendForTest;
                bool httpBack  = !Features.Global.GlobalHttp.SuppressForTest;
                bool flushBack = !Persistence.KmhAtomicFile.SkipDurableFlushForTest;
                bool muteBack  = !Notifications.KmhNotifications.Muted;
                // Left on, every Colony Nexus window would stay English for the rest of the session.
                bool englishBack = !UI.KmhText.EnglishOnlyForTest;

                string leaked = (sendBack ? "" : " send") + (httpBack ? "" : " http") + (flushBack ? "" : " flush") + (muteBack ? "" : " mute")
                              + (englishBack ? "" : " english-only");
                r.Add(("self-test: every suppression is restored even when a check throws",
                       threw && setInside && leaked.Length == 0,
                       leaked.Length == 0 ? "" : "LEAKED OUT OF THE SUITE:" + leaked));
            }
            finally
            {
                SubProtocol.KmhDispatcher.SuppressSendForTest = sendOuter;
                Features.Global.GlobalHttp.SuppressForTest = httpOuter;
                Persistence.KmhAtomicFile.SkipDurableFlushForTest = flushOuter;
                Notifications.KmhNotifications.Muted = muteOuter;
                UI.KmhText.EnglishOnlyForTest = englishOuter;
            }
        }

        public static List<(string, bool, string)> Run()
        {
            // Some paths answer by sending, which can deadlock an enforcing join: nothing here may leave the process.
            int sendsBefore = SubProtocol.KmhDispatcher.SuppressedSendCount;
            int callsBefore = Features.Global.GlobalHttp.SuppressedCallCount;
            int flushesBefore = Persistence.KmhAtomicFile.DurableFlushCount;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using (Suppression.Begin())
            {
                List<(string, bool, string)> r = RunSuite(sendsBefore, callsBefore);
                clock.Stop();

                // Counted, not timed: a durable flush costs 300ms-4s depending on disk load, so a time budget is flaky.
                int flushed = Persistence.KmhAtomicFile.DurableFlushCount - flushesBefore;
                r.Add(("self-test: nothing this suite runs forces the disk", flushed == 0,
                       flushed == 0 ? $"{clock.ElapsedMilliseconds} ms" : $"{flushed} DURABLE FLUSH(ES)"));

                SuppressionLifetime(r);
                return r;
            }
        }

        private static List<(string, bool, string)> RunSuite(int sendsBefore, int callsBefore)
        {
            var r = new List<(string, bool, string)>();

            void KeyRoundTrip(string def, string stuff, int q)
            {
                string key = ItemKeys.Compose(def, stuff, q);
                ItemKeys.Split(key, out string d, out string s, out int qi);
                string wantStuff = stuff ?? "";
                bool ok = d == def && s == wantStuff && qi == q;
                r.Add(($"itemkey round-trip ({def}|{stuff}|{q})", ok, ok ? "" : $"'{key}' -> ({d},{s},{qi})"));
            }
            KeyRoundTrip("Steel", null, 0);
            KeyRoundTrip("Plasteel", "", 3);
            KeyRoundTrip("MeleeWeapon_LongSword", "Plasteel", 6);
            r.Add(("itemkey bare def has no separator", ItemKeys.Compose("Silver", null, 0) == "Silver",
                   ItemKeys.Compose("Silver", null, 0)));

            // Every minifiable building shares one minifiedDef, so without the fourth segment a bed and a cell are one key.
            {
                string cell = ItemKeys.Compose("MinifiedThing", "", 0, "VanometricPowerCell");
                string bed  = ItemKeys.Compose("MinifiedThing", "", 0, "Bed");
                ItemKeys.Split(cell, out string cd, out _, out _, out string ci);
                r.Add(("itemkey: two minified buildings are two distinct keys", cell != bed, $"{cell} vs {bed}"));
                r.Add(("itemkey: a minified key still reports MinifiedThing as its def", cd == "MinifiedThing", cd));
                r.Add(("itemkey: a minified key carries the inner building", ci == "VanometricPowerCell", ci));
                r.Add(("itemkey: the display def of a minified key is the building inside",
                       ItemKeys.DisplayDefOf(cell) == "VanometricPowerCell", ItemKeys.DisplayDefOf(cell)));
                ItemKeys.Split(ItemKeys.Compose("Bed", "WoodLog", 4), out string pd, out string ps, out int pq, out string pi);
                r.Add(("itemkey: an ordinary key is unchanged by the new segment",
                       pd == "Bed" && ps == "WoodLog" && pq == 4 && pi == "", $"{pd}|{ps}|{pq}|{pi}"));
            }

            {
                string a = Features.Sites.SiteWorkerKey.For("ada", 42);
                string b = Features.Sites.SiteWorkerKey.For("ada", 77);
                r.Add(("site worker: one account's two colonists are two keys", a != b, $"{a} vs {b}"));
                r.Add(("site worker: both keys still belong to that account",
                       Features.Sites.SiteWorkerKey.IsOwnedBy(a, "ADA") && Features.Sites.SiteWorkerKey.IsOwnedBy(b, "ada"),
                       Features.Sites.SiteWorkerKey.AccountOf(a)));
                r.Add(("site worker: a legacy account key keeps working",
                       Features.Sites.SiteWorkerKey.AccountOf("ada") == "ada" && !Features.Sites.SiteWorkerKey.IsPerPawn("ada"), ""));
                r.Add(("site worker: a name prefix does not make an owner",
                       !Features.Sites.SiteWorkerKey.IsOwnedBy(Features.Sites.SiteWorkerKey.For("adam", 1), "ada"), ""));
            }

            FrameCodec(r);

            r.Add(("protocol: this client speaks v3", SubProtocol.KmhProtocol.CurrentVersion == 3,
                   "v" + SubProtocol.KmhProtocol.CurrentVersion));
            r.Add(("protocol: the incompatibility kind is admission control traffic",
                   Features.Enforcement.EnforcementAdmission.IsControl(SubProtocol.KmhProtocol.Kind.Incompatible),
                   "a held session could not report its own mismatch"));

            r.Add(("payload value 50x3=150", Approx(KmhWealthValue.Payload(Payload(50, 3)), 150f), ""));
            r.Add(("payload unpriced -> 0", Approx(KmhWealthValue.Payload(Payload(0, 4)), 0f), ""));
            r.Add(("payload stack<1 floors to 1", Approx(KmhWealthValue.Payload(Payload(10, 0)), 10f), ""));
            r.Add(("payload null -> 0", Approx(KmhWealthValue.Payload(null), 0f), ""));

            r.Add(("payloads null list -> 0", Approx(KmhWealthValue.Payloads(null), 0f), ""));
            r.Add(("payloads skip null entries",
                   Approx(KmhWealthValue.Payloads(new List<KmhThingPayload> { Payload(50, 2), null, Payload(25, 1) }), 125f), ""));

            // Reported rather than skipped: a check that silently disappears reads as one that passed.
            var silver = ColonyGoods.Def("Silver");
            r.Add(("compact Silver x5 = base x5",
                   silver == null || Approx(KmhWealthValue.CompactDef("Silver", 5), silver.BaseMarketValue * 5f),
                   silver == null ? "not asked - no def database (running outside the game)" : ""));
            r.Add(("compact unknown def -> 0", Approx(KmhWealthValue.CompactDef("__kmh_no_such_def__", 9), 0f), ""));
            r.Add(("compact count<=0 -> 0", Approx(KmhWealthValue.CompactDef("Silver", 0), 0f), ""));

            r.Add(("grant delivered", KmhGrantDecision.Decide(true, false) == KmhGrantOutcome.Delivered, ""));
            r.Add(("grant delivered ignores site", KmhGrantDecision.Decide(true, true) == KmhGrantOutcome.Delivered, ""));
            r.Add(("grant no drop site -> held", KmhGrantDecision.Decide(false, false) == KmhGrantOutcome.Held, ""));
            r.Add(("grant site-but-failed -> undeliverable", KmhGrantDecision.Decide(false, true) == KmhGrantOutcome.Undeliverable, ""));

            r.Add(("treasury value null -> 0", Approx(KmhWealthValue.OfTreasury(null), 0f), ""));
            r.Add(("treasury silver-only = balance",
                   Approx(KmhWealthValue.OfTreasury(Snap(100, null, null)), 100f), ""));
            r.Add(("treasury silver + payload composes",
                   Approx(KmhWealthValue.OfTreasury(Snap(100, null, new List<KmhThingPayload> { Payload(50, 2) })), 200f), ""));
            r.Add(("treasury unknown item adds 0",
                   Approx(KmhWealthValue.OfTreasury(Snap(100, new Dictionary<string, int> { { "__kmh_no_such_def__", 3 } }, null)), 100f), ""));

            var srcs = new List<IKmhWealthSource>
            {
                new FakeSource(100f), new FakeSource(null /*throws*/), new FakeSource(-50f), new FakeSource(0f), new FakeSource(25f),
            };
            r.Add(("wealth fold sums positives, isolates throw", Approx(KmhWealthLedger.Sum(srcs), 125f), ""));
            r.Add(("wealth fold null list -> 0", Approx(KmhWealthLedger.Sum(null), 0f), ""));

            float shortPanel = 400f;   // a small window: Mathf.Min(640, screenHeight - 120) on a ~520px-tall screen
            r.Add(("panel: several actions visible without scrolling on a short window",
                   KMHPatch.Tabs.MainTabWindow_KMH.ActionsReachableWithoutScrolling(shortPanel, 2, 6),
                   $"header {KMHPatch.Tabs.MainTabWindow_KMH.HeaderHeight(2)}px of {shortPanel}px"));
            r.Add(("panel: every action visible without scrolling at full height",
                   KMHPatch.Tabs.MainTabWindow_KMH.ActionsReachableWithoutScrolling(
                       KMHPatch.Tabs.MainTabWindow_KMH.MaxTabHeight, 2, 12), ""));
            r.Add(("panel: actions survive the log-sharing notice",
                   KMHPatch.Tabs.MainTabWindow_KMH.ActionsReachableWithoutScrolling(shortPanel, 3, 6),
                   $"header {KMHPatch.Tabs.MainTabWindow_KMH.HeaderHeight(3)}px of {shortPanel}px"));
            // A 13-row status table above the buttons pushes them off a short panel.
            float tableHeight = 6f * 2f + 13f * (23f + 2f);
            r.Add(("panel: a status table above the actions would break it",
                   !KMHPatch.Tabs.MainTabWindow_KMH.ActionsReachableWithoutScrolling(
                       shortPanel - tableHeight, 2, 6), $"table {tableHeight}px"));

            r.Add(("log share: Never is a distinct mode from Ask",
                   KMHPatchSettings.LogShareNever != KMHPatchSettings.LogShareAsk
                   && KMHPatchSettings.LogShareAsk != KMHPatchSettings.LogShareAutomatic, ""));

            // Constructing a KMHPatchSettings would drag in the loader assembly this offline suite must not need.
            int Migrate(bool legacyOptIn, int stored)
                => stored >= KMHPatchSettings.LogShareNever && stored <= KMHPatchSettings.LogShareAutomatic
                   ? stored
                   : (legacyOptIn ? KMHPatchSettings.LogShareAutomatic : KMHPatchSettings.LogShareAsk);

            r.Add(("log share: a legacy opt-in migrates to Automatic",
                   Migrate(true, -1) == KMHPatchSettings.LogShareAutomatic, ""));
            r.Add(("log share: legacy off migrates to Ask, never to Never",
                   Migrate(false, -1) == KMHPatchSettings.LogShareAsk, ""));
            r.Add(("log share: an explicit stored mode is preserved",
                   Migrate(true, KMHPatchSettings.LogShareNever) == KMHPatchSettings.LogShareNever, ""));

            // Unless the mode is read where sharing is decided, Never behaves exactly like Ask.
            bool Asks(int mode, bool standingOptIn) => mode == KMHPatchSettings.LogShareAsk
                                                       && !(mode != KMHPatchSettings.LogShareNever && standingOptIn);
            bool Shares(int mode, bool standingOptIn, bool sessionConsent)
                => mode != KMHPatchSettings.LogShareNever && (sessionConsent || standingOptIn);
            r.Add(("log share: Never neither asks nor sends, whatever else is set",
                   !Asks(KMHPatchSettings.LogShareNever, false)
                   && !Shares(KMHPatchSettings.LogShareNever, true,  false)
                   && !Shares(KMHPatchSettings.LogShareNever, false, true)
                   && Asks(KMHPatchSettings.LogShareAsk, false)
                   && Shares(KMHPatchSettings.LogShareAutomatic, true, false), ""));

            var dcLinked   = new Features.Chat.Dto.ChatMessage { Origin = "discord", SenderVerified = true };
            var dcUnlinked = new Features.Chat.Dto.ChatMessage { Origin = "discord", SenderVerified = false };
            var inGame     = new Features.Chat.Dto.ChatMessage { Origin = "ingame",  SenderVerified = true };
            var legacyDc   = JsonConvert.DeserializeObject<Features.Chat.Dto.ChatMessage>("{\"origin\":\"discord\"}");

            r.Add(("discord id: a linked relay is marked as from Discord",       dcLinked.FromDiscord, ""));
            r.Add(("discord id: a linked relay is NOT flagged unverified",       !dcLinked.UnverifiedSender, ""));
            r.Add(("discord id: an unlinked relay IS flagged unverified",        dcUnlinked.UnverifiedSender, ""));
            r.Add(("discord id: an in-game line is never flagged unverified",    !inGame.UnverifiedSender, ""));
            r.Add(("discord id: a relay with no flag at all reads unverified",   legacyDc != null && legacyDc.UnverifiedSender, ""));

            r.Add(("theme: a six-digit hex parses",       KMHPatch.UI.KmhTheme.TryHex("E2C16B", out _), ""));
            r.Add(("theme: a leading # is accepted",      KMHPatch.UI.KmhTheme.TryHex("#E2C16B", out _), ""));
            r.Add(("theme: a short value is rejected",    !KMHPatch.UI.KmhTheme.TryHex("E2C", out _), ""));
            r.Add(("theme: a non-hex value is rejected",  !KMHPatch.UI.KmhTheme.TryHex("ZZZZZZ", out _), ""));
            r.Add(("theme: blank is rejected (= use the layer below)",
                   !KMHPatch.UI.KmhTheme.TryHex("", out _) && !KMHPatch.UI.KmhTheme.TryHex(null, out _), ""));

            KMHPatch.UI.KmhTheme.TryHex("010101", out UnityEngine.Color dark);
            float darkLum = 0.2126f * dark.r + 0.7152f * dark.g + 0.0722f * dark.b;
            r.Add(("theme: a near-black colour is lifted to stay readable", darkLum >= 0.34f, darkLum.ToString("0.00")));

            r.Add(("theme: hex round-trips through the rich-text formatter",
                   KMHPatch.UI.KmhTheme.Hex(new UnityEngine.Color(1f, 0f, 0.5f)) == "#FF0080", ""));

            r.Add(("marker: a blank server value falls back to KMH's own",
                   KMHPatch.UI.KmhTheme.ClampMarker("") == KMHPatch.UI.KmhTheme.DefaultDiscordMarker
                   && KMHPatch.UI.KmhTheme.ClampMarker(null) == KMHPatch.UI.KmhTheme.DefaultDiscordMarker
                   && KMHPatch.UI.KmhTheme.ClampMarker("   ") == KMHPatch.UI.KmhTheme.DefaultDiscordMarker, ""));
            r.Add(("marker: a value carrying markup is refused, not mangled",
                   KMHPatch.UI.KmhTheme.ClampMarker("<color=red>") == KMHPatch.UI.KmhTheme.DefaultDiscordMarker
                   && KMHPatch.UI.KmhTheme.ClampMarker("<b>D</b>") == KMHPatch.UI.KmhTheme.DefaultDiscordMarker, ""));
            r.Add(("marker: an over-long value is cut, not accepted whole",
                   KMHPatch.UI.KmhTheme.ClampMarker("ABCDEFGHIJ").Length == 4, ""));
            r.Add(("marker: an ordinary glyph is kept as the owner set it",
                   KMHPatch.UI.KmhTheme.ClampMarker("**") == "**", ""));

            // Measured in visible characters here; the game measures pixels the same way through the same seam.
            string richCut = KMHPatch.UI.DialogLayout.TruncateRich("<color=#f0c04f>Paused</color> <color=#A9AFB9>· long tail here</color>", 12f,
                                                               t => KMHPatch.UI.DialogLayout.StripTags(t).Length);
            r.Add(("layout: a truncated status line keeps its colours and closes every tag it opened",
                   richCut.StartsWith("<color=#f0c04f>Paused</color>") && richCut.EndsWith("...</color>")
                   && KMHPatch.UI.DialogLayout.StripTags(richCut).Length <= 12, richCut));

            // A line break in a one-line row (a quest description) draws a second line over the row below.
            const System.Reflection.BindingFlags AnyStatic = System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.MethodInfo labelTrunc = typeof(DialogLayout).GetMethod("LabelTrunc", AnyStatic);
            System.Reflection.MethodInfo oneLine    = typeof(DialogLayout).GetMethod("OneLine", AnyStatic);
            string flat    = DialogLayout.OneLine("first\r\nsecond\nthird\rfourth");
            string flatCut = DialogLayout.TruncateRich("<b>quest</b>\nline two of a long description", 14f, t => DialogLayout.StripTags(t).Length);
            r.Add(("layout: a one-line label turns line breaks into spaces before it measures or cuts",
                   flat == "first second third fourth" && flatCut.IndexOfAny(new[] { '\n', '\r' }) < 0
                   && labelTrunc != null && oneLine != null && MethodBodyCalls(labelTrunc, oneLine), flat + " | " + flatCut));

            // With no anchor asked for, LabelTrunc keeps the caller's, so a right-aligned column stays right-aligned.
            System.Reflection.MethodInfo resolveAnchor = typeof(DialogLayout).GetMethod("ResolveAnchor", AnyStatic);
            r.Add(("layout: a truncated label keeps the caller's alignment unless one is passed",
                   DialogLayout.ResolveAnchor(null, UnityEngine.TextAnchor.UpperRight) == UnityEngine.TextAnchor.MiddleRight
                   && DialogLayout.ResolveAnchor(null, UnityEngine.TextAnchor.MiddleCenter) == UnityEngine.TextAnchor.MiddleCenter
                   && DialogLayout.ResolveAnchor(UnityEngine.TextAnchor.UpperLeft, UnityEngine.TextAnchor.MiddleRight) == UnityEngine.TextAnchor.MiddleLeft
                   && labelTrunc != null && resolveAnchor != null && MethodBodyCalls(labelTrunc, resolveAnchor), ""));

            // With no guild list yet, a throwing empty message turns the picker into the error panel.
            string pickerNull, pickerEmpty = null, pickerMiss = null;
            try
            {
                pickerNull  = Features.Guilds.Dialog_KMHGuildPicker.EmptyLabel(null);
                pickerEmpty = Features.Guilds.Dialog_KMHGuildPicker.EmptyLabel(new List<Features.Guilds.Dto.GuildLeaderboardEntry>());
                pickerMiss  = Features.Guilds.Dialog_KMHGuildPicker.EmptyLabel(
                    new List<Features.Guilds.Dto.GuildLeaderboardEntry> { new Features.Guilds.Dto.GuildLeaderboardEntry { Name = "Iron" } });
            }
            catch (Exception ex) { pickerNull = "threw " + ex.GetType().Name; }
            r.Add(("guild picker: no guild list yet reads as loading instead of throwing",
                   pickerNull.Contains("Loading") && pickerEmpty != null && !pickerEmpty.Contains("Loading")
                   && pickerMiss != null && pickerMiss.Contains("No guild matches"), pickerNull));

            // The podium speaks as the website and Discord images do, and a face is the same two letters everywhere.
            var lb = new Features.Leaderboards.Dto.LeaderboardBoard();
            foreach (long v in new long[] { 16443, 12581, 6290 }) lb.Rows.Add(new Features.Leaderboards.Dto.LeaderboardRow { Rank = lb.Rows.Count + 1, Value = v });
            string lead = Features.Standings.LeaderboardsView.PodiumDetail(lb, 0), third = Features.Standings.LeaderboardsView.PodiumDetail(lb, 2);
            r.Add(("standings: the podium says how far first leads and how near the others come",
                   lead == KMHPatch.UI.KmhText.Get("KMH_Lb_Leads", "Leads #2 by {0}", "31%") && third == KMHPatch.UI.KmhText.Get("KMH_Lb_OfFirst", "{0}% of #1", 38)
                   && Features.Standings.LeaderboardsView.Initials("kmhtest-u17") == "KU" && Features.Standings.LeaderboardsView.Initials("KNAPPE0") == "KN"
                   && Features.Standings.LeaderboardsView.Initials("SimEzra") == "SE",
                   $"{lead} | {third}"));

            var offered = new HashSet<string> { "seen before" };
            bool offerOk = Features.Reports.KmhErrorOffer.ShouldOffer("new error", true, false, 0, offered)
                && !Features.Reports.KmhErrorOffer.ShouldOffer("seen before", true, false, 0, offered)
                && !Features.Reports.KmhErrorOffer.ShouldOffer("new error", false, false, 0, offered)
                && !Features.Reports.KmhErrorOffer.ShouldOffer("new error", true, true, 0, offered)
                && !Features.Reports.KmhErrorOffer.ShouldOffer("new error", true, false, Features.Reports.KmhErrorOffer.MaxOffersPerSession, offered)
                && !Features.Reports.KmhErrorOffer.ShouldOffer("", true, false, 0, offered);
            r.Add(("error offer: once per kind of error, a few a session, never while busy or switched off", offerOk, ""));

            var rep = new Features.Reports.KmhMyReports.Remote { Status = "acknowledged" };
            string before = Features.Reports.KmhMyReports.Mark(rep);
            rep.Messages.Add(new Features.Reports.KmhMyReports.Reply { Body = "thanks" });
            string afterReply = Features.Reports.KmhMyReports.Mark(rep);
            rep.Status = "fixed"; rep.FixedIn = "1.3.1+rel-281";
            r.Add(("my reports: a status, a fixed-in version or a reply is news; the label names the version",
                   before != afterReply && afterReply != Features.Reports.KmhMyReports.Mark(rep)
                   && Features.Reports.KmhMyReports.StatusLabel("fixed", "1.3.1+rel-281") == "Fixed in 1.3.1+rel-281",
                   Features.Reports.KmhMyReports.StatusLabel("fixed", "1.3.1+rel-281")));

            // The bid and donate dialogs pass a unit but no maximum; their "silver (min …)" hint must still show.
            string bidHint = Dialog_KMHAmountInput.HintFor(0, "silver (min 500)");
            r.Add(("amount input: a unit hint shows even without a maximum",
                   bidHint.Contains("silver (min 500)") && Dialog_KMHAmountInput.HintFor(0, "") == ""
                   && Dialog_KMHAmountInput.HintFor(7, "units").Contains("7 units"), bidHint));

            // A narrow panel truncates the end of the line, so the one thing to act on must lead it.
            string questsLine = DialogLayout.StripTags(KMHDashboard.QuestsLine(3, 1, 2, 4));
            r.Add(("dashboard: quests awaiting your review lead the quest line",
                   questsLine.StartsWith("4 awaiting your review")
                   && !DialogLayout.StripTags(KMHDashboard.QuestsLine(3, 1, 2, 0)).Contains("review"), questsLine));

            // Titles and descriptions are typed by players and drawn as rich text on every board; a hunt target is a key.
            var questSnap = new Features.Quests.Dto.QuestSnapshot { Quests = new List<Features.Quests.Dto.QuestEntry>
            {
                new Features.Quests.Dto.QuestEntry { Title = "<size=300>BIG", Description = "<color=red>x</color>", HuntTargetDefName = "<b>Raider" },
            } };
            Features.Quests.QuestCache.MakeInert(questSnap);
            Features.Quests.Dto.QuestEntry inertQuest = questSnap.Quests[0];
            System.Reflection.MethodInfo questApply = typeof(Features.Quests.QuestCache).GetMethod("Apply", AnyStatic);
            System.Reflection.MethodInfo questInert = typeof(Features.Quests.QuestCache).GetMethod("MakeInert", AnyStatic);
            r.Add(("quests: player-written titles and descriptions are inert once cached",
                   !inertQuest.Title.Contains("<") && !inertQuest.Description.Contains("<") && inertQuest.HuntTargetDefName == "<b>Raider"
                   && questApply != null && questInert != null && MethodBodyCalls(questApply, questInert), inertQuest.Title));

            KMHPatch.Features.Identity.KmhStaff.ApplyWire("owner:Owner:E2C16B;moderator:Mod:8FD98F;op::C0C6CF;bogus");
            r.Add(("staff: a well-formed entry becomes a badge",
                   KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("owner", out string ownerLabel, out _)
                   && ownerLabel == "Owner", ""));
            r.Add(("staff: role matching ignores casing",
                   KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("MODERATOR", out _, out _), ""));
            r.Add(("staff: an entry with no label is skipped rather than drawn empty",
                   !KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("op", out _, out _), ""));
            r.Add(("staff: a malformed entry is skipped without taking the rest with it",
                   !KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("bogus", out _, out _)
                   && KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("owner", out _, out _), ""));
            r.Add(("staff: nobody is staff by default",
                   !KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("", out _, out _)
                   && !KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole(null, out _, out _), ""));

            // A label goes straight into a chat line's rich text. Markup in it is refused, not stripped.
            KMHPatch.Features.Identity.KmhStaff.ApplyWire("admin:<b>Boss</b>:FF8A6B");
            r.Add(("staff: a label carrying markup is refused outright",
                   !KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("admin", out _, out _), ""));

            // An unreadable colour must not blank the badge - the label is the part that has to survive.
            KMHPatch.Features.Identity.KmhStaff.ApplyWire("admin:Admin:not-a-colour");
            r.Add(("staff: an invalid colour still leaves a readable badge",
                   KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("admin", out string adminLabel, out _)
                   && adminLabel == "Admin", ""));

            // The server lists a simulated account last, so its SIM role wins over any staff entry for the same name.
            KMHPatch.Features.Identity.KmhStaff.ApplyWire("admin:Admin:FF8A6B;sim:SIM:929bab");
            KMHPatch.Features.Identity.KmhStaff.ApplyRoles("SimAda:admin;SimAda:sim");
            r.Add(("staff: a simulated account reads as SIM, never as staff, and says so on hover",
                   KMHPatch.Features.Identity.KmhStaff.RoleOf("simada") == "sim"
                   && !KMHPatch.Features.Identity.KmhStaff.IsStaff("SimAda")
                   && !KMHPatch.Features.Identity.KmhStaff.CanModerate("SimAda")
                   && KMHPatch.Features.Identity.KmhStaff.TryBadge("SimAda", out string simLabel, out _) && simLabel == "SIM"
                   && (KMHPatch.Features.Identity.KmhStaff.Tooltip("SimAda") ?? "").Contains("Simulated"), ""));

            // The owner's own server on this PC: the public address does not loop back, so the join dials it locally.
            KMHPatch.Features.Global.KmhLocalServer.ResetForTest();
            KMHPatch.Features.Global.KmhLocalServer.ListenerForTest = p => p == 25652 ? System.Net.IPAddress.Any : null;
            DateTime joinT0 = new DateTime(2026, 10, 14, 19, 0, 0, DateTimeKind.Utc);
            string viaRemote  = KMHPatch.Features.Global.KmhLocalServer.DialHost("play.example.org", 25000, "abc", joinT0);
            string viaNoId    = KMHPatch.Features.Global.KmhLocalServer.DialHost("play.example.org", 25652, "", joinT0);
            string viaLocal   = KMHPatch.Features.Global.KmhLocalServer.DialHost("play.example.org", 25652, "abc", joinT0);
            string listed     = KMHPatch.Features.Global.KmhLocalServer.ListedFor("127.0.0.1", 25652);
            bool wrongRefused = !KMHPatch.Features.Global.KmhLocalServer.HelloMatches("other", joinT0.AddSeconds(5));
            KMHPatch.Features.Global.KmhLocalServer.DialHost("play.example.org", 25652, "abc", joinT0);
            bool rightKept    = KMHPatch.Features.Global.KmhLocalServer.HelloMatches("ABC", joinT0.AddSeconds(5))
                             && KMHPatch.Features.Global.KmhLocalServer.HelloMatches("other", joinT0.AddSeconds(6));   // one-shot
            KMHPatch.Features.Global.KmhLocalServer.DialHost("play.example.org", 25652, "abc", joinT0);
            bool staleIgnored = KMHPatch.Features.Global.KmhLocalServer.HelloMatches("other", joinT0.AddMinutes(11));
            KMHPatch.Features.Global.KmhLocalServer.ResetForTest();
            r.Add(("join: a server on this PC is dialled locally, the hello must name the picked server, and tickets keep the listed address",
                   viaRemote == "play.example.org" && viaNoId == "play.example.org" && viaLocal == "127.0.0.1"
                   && listed == "play.example.org:25652" && wrongRefused && rightKept && staleIgnored,
                   $"remote {viaRemote}, no id {viaNoId}, local {viaLocal}, listed {listed}, wrong refused {wrongRefused}, right kept {rightKept}, stale ignored {staleIgnored}"));

            KMHPatch.Features.Identity.KmhStaff.Clear();
            r.Add(("staff: a server switch takes its badges with it",
                   !KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("admin", out _, out _), ""));

            byte[] gif = BuildTestGif();
            // Animated PNG: Discord serves it for stickers and many emoji, and Unity decodes only its first frame.
            byte[] ApngChunk(string type, byte[] data) => KMHPatch.Features.Chat.KmhApng.Chunk(type, data);
            byte[] Be32(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
            byte[] Join(params byte[][] parts)
            {
                int n = 0;
                foreach (byte[] p in parts) n += p.Length;
                var all = new byte[n];
                int at = 0;
                foreach (byte[] p in parts) { System.Buffer.BlockCopy(p, 0, all, at, p.Length); at += p.Length; }
                return all;
            }
            byte[] Fctl(int seq, int w, int h, int x, int y, int num, int den, byte dispose, byte blend)
                => Join(Be32(seq), Be32(w), Be32(h), Be32(x), Be32(y),
                        new[] { (byte)(num >> 8), (byte)num, (byte)(den >> 8), (byte)den, dispose, blend });

            byte[] pngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            byte[] ihdr = Join(Be32(8), Be32(8), new byte[] { 8, 6, 0, 0, 0 });
            byte[] pixels = { 1, 2, 3, 4 };
            byte[] plainPng = Join(pngSig, ApngChunk("IHDR", ihdr), ApngChunk("IDAT", pixels), ApngChunk("IEND", new byte[0]));
            byte[] apng = Join(pngSig, ApngChunk("IHDR", ihdr),
                               ApngChunk("acTL", Join(Be32(2), Be32(0))),
                               ApngChunk("fcTL", Fctl(0, 8, 8, 0, 0, 0, 100, 0, 0)),
                               ApngChunk("IDAT", pixels),
                               ApngChunk("fcTL", Fctl(1, 4, 4, 2, 3, 50, 100, 1, 1)),
                               ApngChunk("fdAT", Join(Be32(2), pixels)),
                               ApngChunk("IEND", new byte[0]));

            // PNG's own CRC, checked against the one constant every png in the world ends with.
            r.Add(("apng: chunks are written with a real png crc",
                   KMHPatch.Features.Chat.KmhApng.Crc32(new byte[] { 0x49, 0x45, 0x4E, 0x44 }, 0, 4) == 0xAE426082u, ""));

            r.Add(("apng: an animated png is told apart from a still one",
                   KMHPatch.Features.Chat.KmhApng.LooksLikeApng(apng)
                   && !KMHPatch.Features.Chat.KmhApng.LooksLikeApng(plainPng)
                   && !KMHPatch.Features.Chat.KmhApng.LooksLikeApng(gif)
                   && !KMHPatch.Features.Chat.KmhApng.LooksLikeApng(null)
                   && !KMHPatch.Features.Chat.KmhApng.LooksLikeApng(new byte[] { 0x89, 0x50 }), ""));

            var sheet = KMHPatch.Features.Chat.KmhApng.Split(apng);
            r.Add(("apng: every frame comes out as a png of its own, at its own size and place",
                   sheet != null && sheet.Width == 8 && sheet.Height == 8 && sheet.Parts.Count == 2
                   && sheet.Parts[0].Width == 8 && sheet.Parts[0].Height == 8
                   && sheet.Parts[1].Width == 4 && sheet.Parts[1].Height == 4
                   && sheet.Parts[1].X == 2 && sheet.Parts[1].Y == 3
                   && sheet.Parts[1].Dispose == 1 && sheet.Parts[1].Blend == 1,
                   sheet == null ? "no sheet" : sheet.Parts.Count + " frame(s)"));

            r.Add(("apng: a cut-out frame is a whole png, sized to the frame rather than the canvas",
                   sheet != null && sheet.Parts.Count == 2
                   && sheet.Parts[1].Png[0] == 0x89 && sheet.Parts[1].Png[1] == 0x50
                   && sheet.Parts[1].Png[16] == 0 && sheet.Parts[1].Png[19] == 4      // IHDR width  = 4
                   && sheet.Parts[1].Png[23] == 4                                      // IHDR height = 4
                   && sheet.Parts[1].Png[sheet.Parts[1].Png.Length - 1] == 0x82, ""));  // ...IEND's crc

            r.Add(("apng: a frame that asks for no delay gets the tenth of a second every browser gives it",
                   Math.Abs(KMHPatch.Features.Chat.KmhApng.Delay(0f) - 0.1f) < 0.001f
                   && Math.Abs(KMHPatch.Features.Chat.KmhApng.Delay(0.5f) - 0.5f) < 0.001f
                   && sheet != null && Math.Abs(sheet.Parts[0].DelaySeconds - 0.1f) < 0.001f
                   && Math.Abs(sheet.Parts[1].DelaySeconds - 0.5f) < 0.001f, ""));

            bool apngSurvivesJunk = true;
            try
            {
                KMHPatch.Features.Chat.KmhApng.Split(null);
                KMHPatch.Features.Chat.KmhApng.Split(new byte[0]);
                KMHPatch.Features.Chat.KmhApng.Split(plainPng);
                var cut = new byte[apng.Length / 2];
                System.Buffer.BlockCopy(apng, 0, cut, 0, cut.Length);
                KMHPatch.Features.Chat.KmhApng.Split(cut);
            }
            catch { apngSurvivesJunk = false; }
            r.Add(("apng: a truncated or hostile file is refused, never thrown out of", apngSurvivesJunk, ""));

            var opaque = new UnityEngine.Color32(200, 100, 50, 255);
            var clear0 = new UnityEngine.Color32(0, 0, 0, 0);
            var half   = new UnityEngine.Color32(0, 0, 255, 128);
            UnityEngine.Color32 blended = KMHPatch.Features.Chat.KmhApng.Over(half, opaque);
            r.Add(("apng: OVER blends, and leaves an opaque or empty pixel alone",
                   KMHPatch.Features.Chat.KmhApng.Over(opaque, clear0).r == 200
                   && KMHPatch.Features.Chat.KmhApng.Over(clear0, opaque).r == 200
                   && blended.a == 255 && blended.b > 100 && blended.r < 200,
                   $"{blended.r},{blended.g},{blended.b},{blended.a}"));

            var canvas8 = new UnityEngine.Color32[8 * 8];
            for (int i = 0; i < canvas8.Length; i++) canvas8[i] = opaque;
            KMHPatch.Features.Chat.KmhApng.ClearRect(canvas8, 8, 8,
                new KMHPatch.Features.Chat.KmhApng.Part { X = 2, Y = 3, Width = 3, Height = 2 });
            bool clearedInside = canvas8[3 * 8 + 2].a == 0 && canvas8[4 * 8 + 4].a == 0;
            bool keptOutside   = canvas8[0].a == 255 && canvas8[2 * 8 + 2].a == 255
                              && canvas8[5 * 8 + 2].a == 255 && canvas8[3 * 8 + 5].a == 255;
            r.Add(("apng: disposing to background clears that frame's rectangle and nothing else",
                   clearedInside && keptOutside, ""));

            // Discord stickers are animated pngs, not gifs.
            r.Add(("chat media: a moving picture is badged for what it actually is",
                   KMHPatch.Features.Chat.ChatMediaLabel.Badge("https://h/a.gif") == "GIF"
                   && KMHPatch.Features.Chat.ChatMediaLabel.Badge("https://h/a.gif?x=1") == "GIF"
                   && KMHPatch.Features.Chat.ChatMediaLabel.Badge("https://h/stickers/1.png") == "ANIM"
                   && KMHPatch.Features.Chat.ChatMediaLabel.Badge("") == "ANIM", ""));

            // Held, not lost: a delivery that cannot land yet must be retried, and only "delivered" may be acked.
            r.Add(("delivery: a grant with nowhere to land is held rather than dropped or acked",
                   KMHPatch.Features.Delivery.KmhGrantDecision.Decide(false, false)
                       == KMHPatch.Features.Delivery.KmhGrantOutcome.Held
                   && KMHPatch.Features.Delivery.KmhGrantDecision.Decide(true, true)
                       == KMHPatch.Features.Delivery.KmhGrantOutcome.Delivered, ""));

            int ranBefore = 0, ranAfter = 0;
            var frameNames = new[] { "selftest-a", "selftest-throws", "selftest-b" };
            var frameSteps = new Action[]
            {
                () => ranBefore++,
                () => throw new InvalidOperationException("deliberate"),
                () => ranAfter++,
            };
            KmhFrameSteps.Clear();
            KmhFrameSteps.Run(frameNames, frameSteps);
            KmhFrameSteps.Run(frameNames, frameSteps);
            KmhFrameSteps.Clear();
            r.Add(("frame: a step that throws does not stop the steps after it",
                   ranBefore == 2 && ranAfter == 2, $"before={ranBefore} after={ranAfter} (2 passes expected)"));
            // Mismatched lengths are the shape a later edit produces, and must not read past either array.
            KmhFrameSteps.Run(new[] { "only-one" }, frameSteps);
            KmhFrameSteps.Run(frameNames, new Action[] { () => { } });
            KmhFrameSteps.Run(null, frameSteps);
            KmhFrameSteps.Run(frameNames, null);
            KmhFrameSteps.Clear();
            r.Add(("frame: a short or missing step list is survivable", true, "no throw escaped Run"));

            // Treating every failure as retryable turns one dead link into a request loop.
            r.Add(("chat media: a settled refusal is never retried, a passing one is",
                   KMHPatch.Features.Chat.ChatImageCache.IsPermanentStatus(404)
                   && KMHPatch.Features.Chat.ChatImageCache.IsPermanentStatus(403)
                   && KMHPatch.Features.Chat.ChatImageCache.IsPermanentStatus(415)
                   && KMHPatch.Features.Chat.ChatImageCache.IsPermanentStatus(410)
                   && !KMHPatch.Features.Chat.ChatImageCache.IsPermanentStatus(429)
                   && !KMHPatch.Features.Chat.ChatImageCache.IsPermanentStatus(500)
                   && !KMHPatch.Features.Chat.ChatImageCache.IsPermanentStatus(503)
                   && !KMHPatch.Features.Chat.ChatImageCache.IsPermanentStatus(0), ""));
            r.Add(("chat media: a url nothing has tried offers no retry",
                   !KMHPatch.Features.Chat.ChatImageCache.CanRetry("https://h/never-asked.png")
                   && !KMHPatch.Features.Chat.ChatImageCache.CanRetry(""), ""));

            r.Add(("gif: a GIF89a header is recognised", KMHPatch.Features.Chat.KmhGif.LooksLikeGif(gif), ""));
            r.Add(("gif: a PNG is not mistaken for a gif",
                   !KMHPatch.Features.Chat.KmhGif.LooksLikeGif(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), ""));
            r.Add(("gif: null and truncated input are refused",
                   !KMHPatch.Features.Chat.KmhGif.LooksLikeGif(null)
                   && !KMHPatch.Features.Chat.KmhGif.LooksLikeGif(new byte[] { 0x47, 0x49, 0x46 }), ""));

            var raw = KMHPatch.Features.Chat.KmhGif.DecodeFrames(gif);
            r.Add(("gif: a two-frame gif decodes to two frames", raw != null && raw.Frames.Count == 2,
                   raw == null ? "decode returned null" : raw.Frames.Count + " frame(s)"));

            if (raw != null && raw.Frames.Count == 2)
            {
                r.Add(("gif: the logical screen size is read", raw.Width == 2 && raw.Height == 2,
                       raw.Width + "x" + raw.Height));
                r.Add(("gif: every frame carries a full canvas of pixels",
                       raw.Frames[0].Pixels.Length == 4 && raw.Frames[1].Pixels.Length == 4, ""));

                // LZW actually ran: the palette is red and blue, so a decoded pixel must be one of them and opaque.
                UnityEngine.Color32 px = raw.Frames[0].Pixels[0];
                r.Add(("gif: LZW produces real palette colours, not blank pixels",
                       px.a == 255 && (px.r == 255 || px.b == 255), $"rgba({px.r},{px.g},{px.b},{px.a})"));

                // The canvas is reused, so a frame that referenced it instead of copying shows only the last one.
                bool shared = ReferenceEquals(raw.Frames[0].Pixels, raw.Frames[1].Pixels);
                r.Add(("gif: frames are snapshots, not views onto one mutating canvas", !shared, ""));

                // Browsers clamp 0 and 1 hundredths to 100ms, so KMH must too or such a gif spins uselessly.
                r.Add(("gif: a zero frame delay is clamped, not honoured",
                       raw.Frames[0].DelaySeconds >= 0.09f, raw.Frames[0].DelaySeconds.ToString("0.00")));
            }

            // Garbage must fail closed. This is fed by whatever a host chose to serve.
            bool junkThrew = false;
            try
            {
                KMHPatch.Features.Chat.KmhGif.Decode(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0xFF, 0xFF });
                KMHPatch.Features.Chat.KmhGif.Decode(new byte[0]);
                KMHPatch.Features.Chat.KmhGif.Decode(null);
            }
            catch { junkThrew = true; }
            r.Add(("gif: malformed bytes yield nothing and never throw", !junkThrew, ""));

            // Every prefix, not three hand-picked ones: a truncated download is a valid header over a partial body.
            bool truncThrew = false; int truncBad = -1;
            for (int cut = 0; cut <= gif.Length; cut++)
            {
                byte[] part = new byte[cut];
                Array.Copy(gif, part, cut);
                try { KMHPatch.Features.Chat.KmhGif.DecodeFrames(part); }
                catch { truncThrew = true; truncBad = cut; break; }
            }
            r.Add(("gif: every truncation of a real gif is refused without throwing",
                   !truncThrew, truncThrew ? $"threw at {truncBad} byte(s)" : $"{gif.Length + 1} prefixes"));

            // Real cases where the url lied about the bytes: a tenor share link is HTML, Discord serves webp from .png.
            byte[] htmlPage = System.Text.Encoding.ASCII.GetBytes(
                "<!DOCTYPE html>\n<html><head><title>klipy</title></head><body>nope</body></html>");
            r.Add(("media: an HTML page is recognised and never reaches the texture loader",
                   KMHPatch.Features.Chat.ChatImageCache.LooksLikeHtml(htmlPage, null), ""));
            r.Add(("media: an HTML content-type is enough on its own",
                   KMHPatch.Features.Chat.ChatImageCache.LooksLikeHtml(new byte[] { 1, 2, 3, 4, 5 }, "text/html; charset=utf-8"), ""));
            r.Add(("media: a png is not mistaken for a web page",
                   !KMHPatch.Features.Chat.ChatImageCache.LooksLikeHtml(
                       new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }, "image/png"), ""));

            // A minimal RIFF/WEBP; the animated one carries an ANMF frame chunk, the static one does not.
            byte[] webpStatic = WebpBytes(animated: false);
            byte[] webpAnim   = WebpBytes(animated: true);
            r.Add(("media: an animated WebP is told apart from a static one",
                   KMHPatch.Features.Chat.ChatImageCache.IsAnimatedWebp(webpAnim)
                   && !KMHPatch.Features.Chat.ChatImageCache.IsAnimatedWebp(webpStatic), ""));
            r.Add(("media: a gif is not reported as webp",
                   !KMHPatch.Features.Chat.ChatImageCache.IsAnimatedWebp(BuildTestGif()), ""));
            r.Add(("media: formats are named for the log",
                   KMHPatch.Features.Chat.ChatImageCache.DescribeFormat(BuildTestGif()) == "gif"
                   && KMHPatch.Features.Chat.ChatImageCache.DescribeFormat(webpAnim) == "webp/animated"
                   && KMHPatch.Features.Chat.ChatImageCache.DescribeFormat(webpStatic) == "webp/static"
                   && KMHPatch.Features.Chat.ChatImageCache.DescribeFormat(htmlPage) == "html",
                   KMHPatch.Features.Chat.ChatImageCache.DescribeFormat(webpAnim)));

            r.Add(("media: a 415 is explained as a refused conversion, not a failed request",
                   KMHPatch.Features.Chat.ChatImageCache.HttpFailure(415, "err").Contains("415")
                   && KMHPatch.Features.Chat.ChatImageCache.HttpFailure(415, "err").Contains("conversion"), ""));
            r.Add(("media: 403 and 404 are told apart",
                   KMHPatch.Features.Chat.ChatImageCache.HttpFailure(403, "").Contains("403")
                   && KMHPatch.Features.Chat.ChatImageCache.HttpFailure(404, "").Contains("404"), ""));

            // The cap is the server's number, so an owner's raised limit is not overridden by a hidden smaller one.
            r.Add(("media: the server's size cap is used when it sends one",
                   KMHPatch.Features.Chat.ChatImageCache.ClampMaxBytes(12 * 1024 * 1024) == 12 * 1024 * 1024, ""));
            r.Add(("media: no server value falls back to KMH's own default",
                   KMHPatch.Features.Chat.ChatImageCache.ClampMaxBytes(0) == 4 * 1024 * 1024
                   && KMHPatch.Features.Chat.ChatImageCache.ClampMaxBytes(-5) == 4 * 1024 * 1024, ""));
            r.Add(("media: an absurd server cap cannot make a client hold arbitrary memory",
                   KMHPatch.Features.Chat.ChatImageCache.ClampMaxBytes(int.MaxValue) == 32 * 1024 * 1024
                   && KMHPatch.Features.Chat.ChatImageCache.ClampMaxBytes(1) == 256 * 1024, ""));

            const string Url  = "https://cdn.discordapp.com/attachments/1/2/pic.webp";
            const string Id   = "abc123def456";
            const string RefreshedUrl = "https://cdn.discordapp.com/attachments/1/2/pic.webp?ex=deadbeef";
            string Key(bool video, string url, string id, string fresh, bool avail, bool failed)
                => KMHPatch.Features.Chat.ChatPanel.MediaKeyFor(video, url, id, fresh, avail, failed);

            r.Add(("media key: a resolved id is preferred while the resolver is available and healthy",
                   Key(false, Url, Id, "", true, false) == Id, Key(false, Url, Id, "", true, false)));
            r.Add(("media key: a refreshed link wins over the resolved id, because the id's source is the dead url",
                   Key(false, Url, Id, RefreshedUrl, true, false) == RefreshedUrl, Key(false, Url, Id, RefreshedUrl, true, false)));
            r.Add(("media key: a video always plays from its own url, refreshed link or not",
                   Key(true, Url, Id, RefreshedUrl, true, false) == Url && Key(true, Url, "", "", false, false) == Url, ""));
            r.Add(("media key: no id, or a server with no resolver, falls back to the vetted url",
                   Key(false, Url, "", "", true, false) == Url && Key(false, Url, Id, "", false, false) == Url, ""));
            r.Add(("media key: a failed conversion falls back to the url - a still beats a broken row",
                   Key(false, Url, Id, "", true, true) == Url, Key(false, Url, Id, "", true, true)));
            r.Add(("media key: a failed conversion with no url keeps the id, so the failure can still be reported",
                   Key(false, "", Id, "", true, true) == Id, Key(false, "", Id, "", true, true)));
            r.Add(("media key: nothing to draw yields an empty key, never null",
                   Key(false, null, "", "", true, false) == "" && Key(true, null, "", "", true, false) == "", ""));

            // A pure rule protects nothing if the draw path stops consulting it, so the accessor is exercised too.
            KMHPatch.Features.Chat.ChatMediaClient.SetAvailable(false);
            var row = new KMHPatch.Features.Chat.Dto.ChatMessage { ImageUrl = Url, MediaId = Id, IsVideo = false };
            var vid = new KMHPatch.Features.Chat.Dto.ChatMessage { ImageUrl = Url, MediaId = "", IsVideo = true };
            r.Add(("media key: the row's own accessor follows the same rule",
                   KMHPatch.Features.Chat.ChatPanel.MediaKey(row) == Url
                   && KMHPatch.Features.Chat.ChatPanel.MediaKey(vid) == Url
                   && KMHPatch.Features.Chat.ChatPanel.MediaKey(null) == "",
                   KMHPatch.Features.Chat.ChatPanel.MediaKey(row)));
            ChatMediaSchemes(r);

            // A server counts revisions from 1 on every start, so a fresh hello can look older than the last one.
            r.Add(("comms revision: a newer or equal generation applies",
                   !KMHPatch.SubProtocol.KmhHandshakeHandler.IsStaleRevision(5, 4)
                   && !KMHPatch.SubProtocol.KmhHandshakeHandler.IsStaleRevision(4, 4), ""));
            r.Add(("comms revision: an older generation within the same session is skipped",
                   KMHPatch.SubProtocol.KmhHandshakeHandler.IsStaleRevision(3, 7), ""));
            r.Add(("comms revision: with nothing applied yet, any generation applies",
                   !KMHPatch.SubProtocol.KmhHandshakeHandler.IsStaleRevision(1, 0)
                   && !KMHPatch.SubProtocol.KmhHandshakeHandler.IsStaleRevision(99, 0), ""));
            r.Add(("comms revision: outside a session the old count is dropped, so a restarted server is not called stale",
                   KMHPatch.SubProtocol.KmhHandshakeHandler.AppliedRevisionFor(false, 99) == 0
                   && !KMHPatch.SubProtocol.KmhHandshakeHandler.IsStaleRevision(
                          1, KMHPatch.SubProtocol.KmhHandshakeHandler.AppliedRevisionFor(false, 99)), ""));
            r.Add(("comms revision: inside a session the count is kept, so a second transport cannot rewind it",
                   KMHPatch.SubProtocol.KmhHandshakeHandler.AppliedRevisionFor(true, 99) == 99
                   && KMHPatch.SubProtocol.KmhHandshakeHandler.IsStaleRevision(
                          1, KMHPatch.SubProtocol.KmhHandshakeHandler.AppliedRevisionFor(true, 99)), ""));
            r.Add(("comms revision: a server that sends no revision always applies",
                   !KMHPatch.SubProtocol.KmhHandshakeHandler.IsStaleRevision(0, 7), ""));

            System.Type[] watchers = KMHPatch.Features.Chat.ChatVideoPlayer.WatcherTypes;
            r.Add(("video watchers: every window that can draw the stream is counted",
                   System.Array.IndexOf(watchers, typeof(KMHPatch.Features.Comms.Dialog_KMHComms)) >= 0
                   && System.Array.IndexOf(watchers, typeof(KMHPatch.Features.Comms.Dialog_KMHChatPopout)) >= 0
                   && System.Array.IndexOf(watchers, typeof(KMHPatch.Features.Chat.Dialog_KMHVideo)) >= 0
                   && System.Array.IndexOf(watchers, typeof(KMHPatch.Features.Chat.Dialog_KMHVideoWindow)) >= 0,
                   watchers.Length + " watcher(s)"));

            r.Add(("chat focus: nothing is asked for while the field already holds the caret",
                   !KMHPatch.Features.Chat.ChatPanel.ShouldTakeFocus(true, 1), ""));
            r.Add(("chat focus: an unfocused field is asked for",
                   KMHPatch.Features.Chat.ChatPanel.ShouldTakeFocus(false, 1), ""));
            r.Add(("chat focus: the ask gives up rather than fighting another window for the caret forever",
                   !KMHPatch.Features.Chat.ChatPanel.ShouldTakeFocus(false, 10000), ""));

            bool Drift(bool had, bool has, string name, int kbd, int held = 42)
                => KMHPatch.Features.Chat.ChatPanel.DriftedOffField(had, has, name, kbd, held);
            r.Add(("chat focus: a shifted control id is recovered from",
                   Drift(true, false, "", 42), ""));
            // An unnamed field in another window (the Friends id box) keeps its caret; chat must not take it back.
            r.Add(("chat focus: a click into another window's unnamed field is left alone",
                   !Drift(true, false, "", 77, 42), ""));
            r.Add(("chat focus: a deliberate unfocus is left alone",
                   !Drift(true, false, "", 0), ""));
            r.Add(("chat focus: another field taking the caret is left alone",
                   !Drift(true, false, "kmhChat_other", 42) && !Drift(true, false, "someMailField", 42), ""));
            r.Add(("chat focus: a field that never had the caret does not grab it",
                   !Drift(false, false, "", 42), ""));
            r.Add(("chat focus: a field that still has the caret asks for nothing",
                   !Drift(true, true, "kmhChat_server", 42), ""));

            void Pic(int iw, int ih, float viewW, out float w, out float h)
                => KMHPatch.Features.Chat.ChatPanel.PictureSize(iw, ih, viewW, out w, out h);
            Pic(1280, 720, 800f, out float pw, out float ph);
            r.Add(("chat picture: an image capped by height gives up width to match, so its rect is not empty space",
                   UnityEngine.Mathf.Abs(ph - 220f) < 0.5f
                   && UnityEngine.Mathf.Abs(pw / ph - 1280f / 720f) < 0.01f, pw + "x" + ph));
            Pic(64, 64, 800f, out float sw, out float sh);
            r.Add(("chat picture: a small image is never blown up to fill the row",
                   UnityEngine.Mathf.Abs(sw - 64f) < 0.01f && UnityEngine.Mathf.Abs(sh - 64f) < 0.01f, sw + "x" + sh));
            Pic(4000, 100, 300f, out float ww, out float wh);
            r.Add(("chat picture: a very wide image is bounded by the view and keeps its shape",
                   ww <= 300f - 24f + 0.01f && UnityEngine.Mathf.Abs(ww / wh - 40f) < 0.5f, ww + "x" + wh));
            Pic(0, 0, 800f, out float zw, out float zh);
            r.Add(("chat picture: a size nothing has measured yet still yields a drawable rect",
                   zw >= 1f && zh >= 1f, zw + "x" + zh));

            r.Add(("chat video: a failure keeps its reason and drops the url",
                   KMHPatch.Features.Chat.ChatVideoPlayer.Shorten("VideoPlayer cannot play url : https://rr3---sn-x.googlevideo.com/a?b=" + new string('x', 900))
                   == "VideoPlayer cannot play url", ""));
            r.Add(("chat video: a failure with nothing but a url still says something",
                   KMHPatch.Features.Chat.ChatVideoPlayer.Shorten("https://" + new string('y', 500)) == "could not play"
                   && KMHPatch.Features.Chat.ChatVideoPlayer.Shorten("") == "could not play", ""));
            r.Add(("chat video: a long reason is cut to something a row can hold",
                   KMHPatch.Features.Chat.ChatVideoPlayer.Shorten(new string('z', 400)).Length <= 120, ""));

            string Vid(string u) => KMHPatch.Features.Chat.ChatYouTube.VideoIdOf(u);
            r.Add(("youtube: every link shape yields the same id",
                   Vid("https://www.youtube.com/watch?v=cUKwvxrxZFE") == "cUKwvxrxZFE"
                   && Vid("https://youtu.be/cUKwvxrxZFE?si=Qp2KTm6yz4IX3Wsf") == "cUKwvxrxZFE"
                   && Vid("https://m.youtube.com/watch?app=desktop&v=cUKwvxrxZFE&t=9") == "cUKwvxrxZFE"
                   && Vid("https://www.youtube.com/shorts/cUKwvxrxZFE") == "cUKwvxrxZFE"
                   && Vid("https://www.youtube.com/embed/cUKwvxrxZFE") == "cUKwvxrxZFE",
                   Vid("https://m.youtube.com/watch?app=desktop&v=cUKwvxrxZFE&t=9")));
            r.Add(("youtube: anything that is not a watch link yields nothing",
                   Vid("https://example.com/watch?v=cUKwvxrxZFE") == ""
                   && Vid("https://www.youtube.com/results?find=x") == ""
                   && Vid("https://youtube.com.evil.example/watch?v=cUKwvxrxZFE") == ""
                   && Vid("") == "" && Vid("not a url") == "", ""));
            r.Add(("youtube: an id that is not an id is refused before it reaches a request",
                   Vid("https://youtu.be/short") == ""
                   && Vid("https://www.youtube.com/watch?v=has spaces!") == ""
                   && Vid("https://www.youtube.com/watch?v=twelvechars1") == "", ""));

            int Start(string u) => KMHPatch.Features.Chat.ChatYouTube.StartSecondsOf(u);
            r.Add(("youtube: a start offset is read in both forms it comes in",
                   Start("https://youtu.be/abc12345678?t=90") == 90
                   && Start("https://www.youtube.com/watch?v=abc12345678&t=1h2m3s") == 3723
                   && Start("https://www.youtube.com/watch?v=abc12345678&start=45") == 45
                   && Start("https://youtu.be/abc12345678") == 0, Start("https://www.youtube.com/watch?v=abc12345678&t=1h2m3s").ToString()));

            bool Act(bool paused, float idle) => KMHPatch.Features.PlayerStats.KmhActivity.IsActive(paused, idle);
            r.Add(("activity: a paused game with nobody touching it stops counting, a running one keeps going",
                   Act(true, 5f) && Act(true, 119f) && !Act(true, 121f)
                   && Act(false, 121f) && Act(false, 599f) && !Act(false, 601f), ""));

            r.Add(("activity: a span reads in whole units a person can take in at a glance",
                   KMHPatch.Features.Chat.KmhAgo.Span(30) == "1m"
                   && KMHPatch.Features.Chat.KmhAgo.Span(90) == "1m"
                   && KMHPatch.Features.Chat.KmhAgo.Span(3600) == "1h"
                   && KMHPatch.Features.Chat.KmhAgo.Span(90000) == "1d"
                   && KMHPatch.Features.Chat.KmhAgo.Span(0) == "0m",
                   KMHPatch.Features.Chat.KmhAgo.Span(90000)));

            r.Add(("activity: a time never recorded reads as nothing rather than 1970",
                   KMHPatch.Features.Chat.KmhAgo.Since(0) == ""
                   && KMHPatch.Features.Chat.KmhAgo.Since(DateTime.UtcNow.Ticks) == KMHPatch.UI.KmhText.Get("KMH_Ago_JustNow", "just now")
                   && KMHPatch.Features.Chat.KmhAgo.Since(DateTime.UtcNow.AddHours(-3).Ticks) == KMHPatch.UI.KmhText.Get("KMH_Ago_Ago", "{0} ago", "3h"),
                   KMHPatch.Features.Chat.KmhAgo.Since(DateTime.UtcNow.AddHours(-3).Ticks)));

            // Crediting every member the whole vault multiplies wealth by guild size; crediting nobody makes it a shelter.
            var guild = new KMHPatch.Features.Guilds.Dto.GuildSnapshot();
            guild.Members.Add(new KMHPatch.Features.Guilds.Dto.GuildMemberDto { Username = "ann", SilverContributed = 300 });
            guild.Members.Add(new KMHPatch.Features.Guilds.Dto.GuildMemberDto { Username = "bo",  SilverContributed = 100 });
            float annShare = KMHPatch.Features.Wealth.KmhGuildShare.Of(1000f, guild, "ann");
            float boShare  = KMHPatch.Features.Wealth.KmhGuildShare.Of(1000f, guild, "bo");
            float outsider = KMHPatch.Features.Wealth.KmhGuildShare.Of(1000f, guild, "cy");
            r.Add(("wealth: guild-held value is shared by contribution and never credited to a non-member",
                   Math.Abs(annShare - 750f) < 0.5f && Math.Abs(boShare - 250f) < 0.5f && outsider == 0f
                   && Math.Abs(annShare + boShare - 1000f) < 1f,
                   $"{annShare:0} + {boShare:0}"));

            var untracked = new KMHPatch.Features.Guilds.Dto.GuildSnapshot();
            untracked.Members.Add(new KMHPatch.Features.Guilds.Dto.GuildMemberDto { Username = "ann" });
            untracked.Members.Add(new KMHPatch.Features.Guilds.Dto.GuildMemberDto { Username = "bo" });
            r.Add(("wealth: a guild with no recorded contributions splits evenly rather than crediting nobody",
                   Math.Abs(KMHPatch.Features.Wealth.KmhGuildShare.Of(1000f, untracked, "ann") - 500f) < 0.5f
                   && KMHPatch.Features.Wealth.KmhGuildShare.Of(0f, untracked, "ann") == 0f, ""));

            // The vault arrives on the guild snapshot; a source reading the personal-only treasury cache always saw zero.
            guild.GuildSilver = 1000;
            float vaultShare = KMHPatch.Features.Wealth.Sources.GuildVaultWealthSource.ShareOf(guild, "ann");
            r.Add(("wealth: a member's share of the guild vault counts toward raid scaling", Math.Abs(vaultShare - 750f) < 0.5f,
                   $"ann's share of a 1000 silver vault: {vaultShare:0}"));

            // A row with pixels in view that is not drawn is the bottom row flickering blank while a list scrolls.
            {
                const float rowH = 50f, viewH = 345f;
                const int rows = 100;
                int missed = 0;
                string firstMiss = "";
                for (float s = 0f; s <= rows * rowH - viewH; s += 7.3f)
                {
                    KMHPatch.UI.DialogLayout.VisibleRange(new UnityEngine.Vector2(0f, s), viewH, rowH, rows, out int top, out int end);
                    for (int i = 0; i < rows; i++)
                        if (i * rowH < s + viewH && (i + 1) * rowH > s && (i < top || i >= end))
                        {
                            missed++;
                            if (firstMiss.Length == 0) firstMiss = $"row {i} at scroll {s:0.#}";
                        }
                }
                r.Add(("lists: every row with pixels in view is drawn", missed == 0,
                       missed == 0 ? "none missed across the sweep" : $"{missed} missed, first {firstMiss}"));
            }

            var roster = new List<string> { "ann", "bo", "cy" };
            var times = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            bool exact = KMHPatch.Features.Chat.ChatRosterCache.Join(times, roster, new long[] { 10, 20, 30 });
            bool clipped = KMHPatch.Features.Chat.ChatRosterCache.Join(times, roster, new long[] { 10, 20 });
            bool empty   = KMHPatch.Features.Chat.ChatRosterCache.Join(times, roster, new long[0]);
            r.Add(("roster: a mismatched time array is dropped whole rather than shifted onto the wrong player",
                   exact && !clipped && !empty && times.Count == 3 && times["cy"] == 30,
                   times.Count + "/" + (times.TryGetValue("cy", out long cy) ? cy : -1)));

            // A comma-decimal locale writes 0,3000 where en-US writes 0.3000, and two clients must hash it the same.
            string fpInvariant = KMHPatch.Features.Frontier.KmhPlacementFinder.FingerprintOf("seed", 0.3f, 12345, 65000);
            var wasCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            string fpComma;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                fpComma = KMHPatch.Features.Frontier.KmhPlacementFinder.FingerprintOf("seed", 0.3f, 12345, 65000);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = wasCulture; }
            r.Add(("frontier: the world fingerprint is the same in every locale",
                   fpInvariant == fpComma && fpInvariant.Length == 16,
                   $"{fpInvariant} vs {fpComma}"));

            int Foc(long active, long connected) => KMHPatch.Features.Standings.StandingsData.Focus(active, connected);
            r.Add(("activity: focus is a share of connected time, and never reads over 100%",
                   Foc(1800, 3600) == 50 && Foc(3600, 3600) == 100 && Foc(4800, 3600) == 100
                   && Foc(0, 3600) == -1 && Foc(600, 0) == -1, Foc(1800, 3600).ToString()));

            // Roads and markers are drawn without opening anything, so first-connect hydration has to ask for them.
            var hydrated = new List<string>();
            foreach ((string name, System.Func<bool> send) in KMHPatch.SubProtocol.KmhRefresh.All) hydrated.Add(name);
            r.Add(("hydration: first connect requests every world-visible feature",
                   hydrated.Contains("roadworks") && hydrated.Contains("sites")
                   && hydrated.Contains("guild") && hydrated.Contains("world"),
                   string.Join(",", hydrated)));

            // Every dialog that can sit on "Loading…" has to be retryable too, not just the world-visible four.
            r.Add(("hydration: every feature request is tracked, not fired and forgotten",
                   hydrated.Contains("treasury") && hydrated.Contains("marketplace") && hydrated.Contains("auctions")
                   && hydrated.Contains("wants") && hydrated.Contains("mail") && hydrated.Contains("chat")
                   && hydrated.Contains("quests") && hydrated.Contains("standings") && hydrated.Contains("reputation")
                   && hydrated.Contains("accounts") && hydrated.Contains("enforcement"),
                   $"{hydrated.Count} tracked: {string.Join(",", hydrated)}"));

            // A send refused during transport startup must stay retryable, or that feature is blank all session.
            r.Add(("hydration: a refused request does not count as hydrated",
                   !KMHPatch.SubProtocol.KmhHandshakeHandler.HydrationCompleteWhen(true, 1)
                   && KMHPatch.SubProtocol.KmhHandshakeHandler.HydrationCompleteWhen(true, 0),
                   "pending>0 must not read complete"));

            r.Add(("hydration: retries are bounded", KMHPatch.SubProtocol.KmhHandshakeHandler.MaxHydrationAttempts > 0
                   && KMHPatch.SubProtocol.KmhHandshakeHandler.MaxHydrationAttempts <= 10,
                   KMHPatch.SubProtocol.KmhHandshakeHandler.MaxHydrationAttempts.ToString()));

            // A push during pending admission is dropped silently, so retrying then only burns the bounded attempts.
            r.Add(("hydration: no retry is attempted while admission is pending",
                   !KMHPatch.SubProtocol.KmhHandshakeHandler.MayRetryHydration(true, true)
                   && KMHPatch.SubProtocol.KmhHandshakeHandler.MayRetryHydration(true, false)
                   && !KMHPatch.SubProtocol.KmhHandshakeHandler.MayRetryHydration(false, false),
                   "admission pending must spend no attempt"));

            // One art identity per marker at both zoom ranges: a Frontier outpost must not become a vanilla camp.
            string ArtKey(string archetype, string outpostState)
                => KMHPatch.Features.Sites.KmhMarkerArt.ArtKeyFor(new KMHPatch.Features.Sites.Dto.SiteEntry
                   { Archetype = archetype, OutpostState = outpostState });
            var states = new[]
            {
                KMHPatch.Features.Sites.Dto.SiteEntry.OutpostHostile,
                KMHPatch.Features.Sites.Dto.SiteEntry.OutpostDefeated,
                KMHPatch.Features.Sites.Dto.SiteEntry.OutpostDerelict,
                KMHPatch.Features.Sites.Dto.SiteEntry.OutpostClaimable,
                KMHPatch.Features.Sites.Dto.SiteEntry.OutpostCaptured,
                KMHPatch.Features.Sites.Dto.SiteEntry.OutpostDormant,
            };
            bool everyStateStaysKmh = true;
            var stateKeys = new List<string>();
            foreach (string st in states) { string k = ArtKey("", st); stateKeys.Add(k); if (k != "Outpost") everyStateStaysKmh = false; }
            r.Add(("markers: every Frontier state keeps the KMH outpost art", everyStateStaysKmh,
                   string.Join(",", stateKeys)));

            r.Add(("markers: each archetype has its own art, and an unknown one falls back",
                   ArtKey(KMHPatch.Features.Sites.Dto.SiteEntry.ArchetypeFarmland, null) == "Farmland"
                   && ArtKey(KMHPatch.Features.Sites.Dto.SiteEntry.ArchetypeQuarry, null) == "Quarry"
                   && ArtKey("nonsense", null) == "Generic",
                   ArtKey("nonsense", null)));

            // The two sets look alike on screen, so a marker wearing the wrong one shows only in the path it loads.
            KMHPatch.Features.Sites.Dto.SiteEntry Marker(string archetype, string outpostState)
                => new KMHPatch.Features.Sites.Dto.SiteEntry { Archetype = archetype, OutpostState = outpostState };

            bool Resolves(string serverHex, UnityEngine.Color expected)
            {
                KMHPatch.Features.Sites.KmhMarkerColors.ApplyServerColors(serverHex, serverHex);
                KMHPatch.Features.Sites.KmhMarkerArt.TryHaloFor(Marker("", null), true, out UnityEngine.Color got);
                KMHPatch.Features.Sites.KmhMarkerColors.ClearServerColors();
                return got == expected;
            }
            var slots = new List<(string Label, KMHPatch.Features.Sites.Dto.SiteEntry Entry)>();
            foreach (string st in states) slots.Add(("outpost/" + st, Marker("", st)));
            foreach (string a in new[]
                     {
                         KMHPatch.Features.Sites.Dto.SiteEntry.ArchetypeFarmland,
                         KMHPatch.Features.Sites.Dto.SiteEntry.ArchetypeQuarry,
                         KMHPatch.Features.Sites.Dto.SiteEntry.ArchetypeWoodland,
                         KMHPatch.Features.Sites.Dto.SiteEntry.ArchetypeRanch,
                         KMHPatch.Features.Sites.Dto.SiteEntry.ArchetypeRoadworks,
                         KMHPatch.Features.Sites.Dto.SiteEntry.ArchetypeCustom,
                         "nonsense",
                     })
                slots.Add(("archetype/" + a, Marker(a, null)));

            bool everySlotExpands = true; string firstWrongSlot = "";
            foreach ((string label, KMHPatch.Features.Sites.Dto.SiteEntry entry) in slots)
            {
                string expanding = KMHPatch.Features.Sites.KmhMarkerArt.ExpandingPathFor(entry);
                string normal    = KMHPatch.Features.Sites.KmhMarkerArt.NormalPathFor(entry);
                if (expanding.StartsWith(KMHPatch.Features.Sites.KmhMarkerArt.Expanding, StringComparison.Ordinal)
                    && expanding != normal
                    && normal.StartsWith(KMHPatch.Features.Sites.KmhMarkerArt.Normal, StringComparison.Ordinal)
                    && !normal.StartsWith(KMHPatch.Features.Sites.KmhMarkerArt.Expanding, StringComparison.Ordinal))
                    continue;
                everySlotExpands = false;
                if (firstWrongSlot.Length == 0) firstWrongSlot = $"{label} -> {expanding}";
            }
            r.Add(("markers: every state resolves the Expanding slot, never the small-dot texture",
                   everySlotExpands,
                   firstWrongSlot.Length > 0 ? firstWrongSlot : $"{slots.Count} slot(s) checked"));

            // Caching the null a pre-content lookup answers leaves the marker on its def texture all session.
            KMHPatch.Features.Sites.KmhMarkerArt.ResetForTest();
            int lookups = 0;
            KMHPatch.Features.Sites.KmhMarkerArt.LookupForTest = _ => { lookups++; return null; };
            try { for (int i = 0; i < 10; i++) KMHPatch.Features.Sites.KmhMarkerArt.IconFor(Marker("", null)); }
            finally { KMHPatch.Features.Sites.KmhMarkerArt.LookupForTest = null;
                      KMHPatch.Features.Sites.KmhMarkerArt.ResetForTest(); }
            r.Add(("markers: an early art miss is retried, then believed and stops costing a lookup per frame",
                   lookups == KMHPatch.Features.Sites.KmhMarkerArt.ResolveAttempts
                   && KMHPatch.Features.Sites.KmhMarkerArt.ResolveAttempts > 1,
                   $"{lookups} lookup(s) over 10 draws"));

            // Built char by char and length-checked first: Mono's new string('\0', n) hands back recycled heap.
            const string socketHead = "Error: ConnectFailure (host has failed to respond.";
            const int    nulRun     = 8;
            var fixtureChars = new List<char>(socketHead.ToCharArray()) { '\r', '\n' };
            for (int i = 0; i < nulRun; i++) fixtureChars.Add('\0');
            fixtureChars.Add(')');
            string rawSocketMessage = new string(fixtureChars.ToArray());
            string cleaned = KmhLog.OneLine(rawSocketMessage);
            r.Add(("logging: a record is one line, and carries no control characters",
                   rawSocketMessage.Length == socketHead.Length + 2 + nulRun + 1
                   && cleaned.IndexOf('\0') < 0 && cleaned.IndexOf('\n') < 0 && cleaned.IndexOf('\r') < 0
                   && cleaned == socketHead + " | )",
                   $"{rawSocketMessage.Length} raw char(s) -> '{cleaned}'"));

            r.Add(("logging: cleaning a record keeps its text, and an empty one stays empty",
                   KmhLog.OneLine("plain message") == "plain message"
                   && KmhLog.OneLine("") == "" && KmhLog.OneLine(null) == ""
                   && KmhLog.OneLine("trailing\r\n") == "trailing",
                   $"'{KmhLog.OneLine("trailing\r\n")}'"));

            // Ownership is a rim, not a wash: a place nobody holds and in no notable state must not draw one at all.
            KMHPatch.Features.Sites.KmhMarkerColors.ClearServerColors();
            var systemSite = new KMHPatch.Features.Sites.Dto.SiteEntry
            { OwnerKind = KMHPatch.Features.Sites.Dto.SiteEntry.OwnerSystem };
            bool noRim    = !KMHPatch.Features.Sites.KmhMarkerArt.TryHaloFor(systemSite, false, out _);
            bool mineRim  = KMHPatch.Features.Sites.KmhMarkerArt.TryHaloFor(Marker("", null), true, out UnityEngine.Color mineCol);
            bool theirRim = KMHPatch.Features.Sites.KmhMarkerArt.TryHaloFor(Marker("", null), false, out UnityEngine.Color theirCol);
            r.Add(("markers: a rim marks who holds a place, and an unheld one gets none",
                   noRim && mineRim && theirRim && mineCol != theirCol
                   && mineCol == KMHPatch.Features.Sites.KmhMarkerColors.FallbackMine
                   && theirCol == KMHPatch.Features.Sites.KmhMarkerColors.FallbackTheirs,
                   $"system={!noRim} mine=#{UnityEngine.ColorUtility.ToHtmlStringRGB(mineCol)} "
                 + $"theirs=#{UnityEngine.ColorUtility.ToHtmlStringRGB(theirCol)}"));

            // A server may suggest ownership colours; an outpost state may never be restyled into something calm.
            KMHPatch.Features.Sites.KmhMarkerColors.ApplyServerColors("FF00FF", "00FFFF");
            KMHPatch.Features.Sites.KmhMarkerArt.TryHaloFor(Marker("", null), true, out UnityEngine.Color serverMine);
            KMHPatch.Features.Sites.KmhMarkerArt.TryHaloFor(
                Marker("", KMHPatch.Features.Sites.Dto.SiteEntry.OutpostHostile), false, out UnityEngine.Color hostile);
            KMHPatch.Features.Sites.KmhMarkerColors.ClearServerColors();
            r.Add(("markers: a server can suggest ownership colours but never restyle a state",
                   serverMine != KMHPatch.Features.Sites.KmhMarkerColors.FallbackMine
                   && hostile == KMHPatch.Features.Sites.KmhMarkerColors.Hostile,
                   $"serverMine=#{UnityEngine.ColorUtility.ToHtmlStringRGB(serverMine)}"));

            r.Add(("markers: a malformed server colour falls through to KMH's own",
                   Resolves("nonsense", KMHPatch.Features.Sites.KmhMarkerColors.FallbackMine)
                   && Resolves("", KMHPatch.Features.Sites.KmhMarkerColors.FallbackMine),
                   "a bad hex must not draw an invisible rim"));

            // RWT lets a player wear any faction's icon, so only the rim tells their settlement from an NPC one.
            {
                const string rwtRim = "markers: another player's RWT settlement wears the other-players rim, and nothing else does";
                // Found here, not taken from the patch: a patch that never installed must fail this, not skip it.
                Type rwtType = RwtCompat.ResolveType(RwtCompat.ClientNsRoot, "WO_Settlement", RwtCompat.ClientNsRoot + ".WorldObjects.WO_Settlement");
                RimWorld.WorldObjectDef rwtDef = null;
                if (rwtType != null)
                    foreach (RimWorld.WorldObjectDef d in Verse.DefDatabase<RimWorld.WorldObjectDef>.AllDefsListForReading)
                        if (d.worldObjectClass == rwtType) { rwtDef = d; break; }
                if (rwtDef == null || Verse.Current.Game == null)
                    r.Add(KmhSelfTestResult.Skipped(rwtRim, rwtDef == null ? "this RWT has no player settlement object" : "no game loaded"));
                else
                {
                    // Built bare, never added to the world: no id is taken from the loaded game.
                    var theirs = (RimWorld.Planet.WorldObject)Activator.CreateInstance(rwtType);
                    theirs.def = rwtDef;
                    var npc = new RimWorld.Planet.Settlement { def = RimWorld.WorldObjectDefOf.Settlement };
                    KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
                    bool was = s.MarkerShowRim;
                    UnityEngine.Material on, off, plain;
                    try { s.MarkerShowRim = true; on = theirs.ExpandingMaterial; plain = npc.ExpandingMaterial; s.MarkerShowRim = false; off = theirs.ExpandingMaterial; }
                    finally { s.MarkerShowRim = was; }
                    UnityEngine.Material want = KMHPatch.Features.Sites.KmhMarkerRender.Halo(KMHPatch.Features.Sites.KmhMarkerColors.Theirs);
                    r.Add((rwtRim, on != null && on == want && off == null && plain == null,
                           $"rim={(on == null ? "none" : on == want ? "other players" : "another colour")}, with rims off={(off == null ? "none" : "drawn")}, NPC={(plain == null ? "none" : "drawn")}"));
                }
            }

            // A start that finds no place for its colonists leaves them off the map; only the starting ones are set down.
            {
                var init = new Verse.GameInitData { startingPawnCount = 2 };
                init.startingAndOptionalPawns = new List<Verse.Pawn> { new Verse.Pawn(), new Verse.Pawn(), new Verse.Pawn() };
                int left = KMHPatch.Patches.Patch_ScenPart_ArriveMethod_KmhStragglers.Unplaced(init).Count;
                int none = KMHPatch.Patches.Patch_ScenPart_ArriveMethod_KmhStragglers.Unplaced(null).Count;
                r.Add(("start: colonists a scenario left off the map are found, and only the starting ones", left == 2 && none == 0,
                       $"{left} of 3 found with 2 starting, {none} with no game data"));
            }

            // Vanilla starts a world quad's art along an east-west tangent, which lays KMH's directional art on its side.
            UnityEngine.Vector3 onEquator = new UnityEngine.Vector3(100f, 0f, 0f);
            float uprightHere = KMHPatch.Features.Sites.KmhMarkerRender.AngleFor(
                onEquator, new UnityEngine.Vector3(0f, 1f, 0f), new UnityEngine.Vector3(-1f, 0f, 0f));
            r.Add(("markers: close-zoom art is turned upright, not left on vanilla's east-west axis",
                   UnityEngine.Mathf.Abs(UnityEngine.Mathf.Abs(uprightHere) - 90f) < 0.01f,
                   $"{uprightHere:0.##} degrees"));

            // Screen-up is not the north tangent away from the equator, so a fixed angle would only be right in one place.
            float tilted = KMHPatch.Features.Sites.KmhMarkerRender.AngleFor(
                onEquator,
                new UnityEngine.Vector3(0f, UnityEngine.Mathf.Cos(30f * UnityEngine.Mathf.Deg2Rad),
                                            UnityEngine.Mathf.Sin(30f * UnityEngine.Mathf.Deg2Rad)),
                new UnityEngine.Vector3(-1f, 0f, 0f));
            r.Add(("markers: the upright angle tracks the camera rather than a fixed offset",
                   UnityEngine.Mathf.Abs((tilted - uprightHere) - 30f) < 0.01f,
                   $"{uprightHere:0.##} -> {tilted:0.##} for a 30 degree roll"));

            // Over a pole there is no east, and a camera looking straight down a marker has no up to project.
            float atPole = KMHPatch.Features.Sites.KmhMarkerRender.AngleFor(
                new UnityEngine.Vector3(0f, 100f, 0f), new UnityEngine.Vector3(0f, 1f, 0f),
                new UnityEngine.Vector3(0f, -1f, 0f));
            float lookingDown = KMHPatch.Features.Sites.KmhMarkerRender.AngleFor(
                onEquator, new UnityEngine.Vector3(1f, 0f, 0f), new UnityEngine.Vector3(0f, -1f, 0f));
            r.Add(("markers: degenerate camera geometry yields an angle, never NaN",
                   atPole == 0f && !float.IsNaN(lookingDown)
                   && UnityEngine.Mathf.Abs(lookingDown - uprightHere) < 0.01f,
                   $"pole={atPole:0.##} overhead={lookingDown:0.##}"));

            // "Sent" and "applied" as one latch leaves the client trusting a catalog the server never held.
            KMHPatch.Features.Catalog.SiteMetadataSender.ResetForServerSwitch();
            bool needsFirst = KMHPatch.Features.Catalog.SiteMetadataSender.NeedsPush;
            KMHPatch.Features.Catalog.SiteMetadataSender.MarkSentForTest("fp-mine");

            KMHPatch.Features.Catalog.SiteMetadataSender.OnServerCatalog("fp-mine");
            bool confirmed = KMHPatch.Features.Catalog.SiteMetadataSender.Confirmed
                          && !KMHPatch.Features.Catalog.SiteMetadataSender.Refused;

            // A DIFFERENT established catalog is a deliberate refusal, and re-sending would only be refused again.
            KMHPatch.Features.Catalog.SiteMetadataSender.ResetForServerSwitch();
            KMHPatch.Features.Catalog.SiteMetadataSender.MarkSentForTest("fp-mine");
            KMHPatch.Features.Catalog.SiteMetadataSender.OnServerCatalog("fp-theirs");
            bool refused = KMHPatch.Features.Catalog.SiteMetadataSender.Refused
                        && !KMHPatch.Features.Catalog.SiteMetadataSender.Confirmed;

            // A server holding NO catalog means the push never landed - that is the retryable case.
            KMHPatch.Features.Catalog.SiteMetadataSender.ResetForServerSwitch();
            KMHPatch.Features.Catalog.SiteMetadataSender.MarkSentForTest("fp-mine");
            KMHPatch.Features.Catalog.SiteMetadataSender.OnServerCatalog("");
            bool notAccepted = !KMHPatch.Features.Catalog.SiteMetadataSender.Confirmed
                            && !KMHPatch.Features.Catalog.SiteMetadataSender.Refused;
            KMHPatch.Features.Catalog.SiteMetadataSender.ResetForServerSwitch();

            r.Add(("catalog: the client believes its metadata applied only when the server names it",
                   needsFirst && confirmed && refused && notAccepted,
                   $"needsFirstPush={needsFirst}, confirmedOnMatch={confirmed}, refusedOnMismatch={refused}, retryableWhenServerHasNone={notAccepted}"));

            // The metadata push measured ~137KB over the RWT chat channel, so a reconnect must not stream it again.
            const string GuardProbe = "kmh-selftest-guard";
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            KMHPatch.Features.Catalog.CatalogPushGuard.Forget(GuardProbe);
            bool coldPushes = !KMHPatch.Features.Catalog.CatalogPushGuard.AlreadySent(GuardProbe, "host:1", t0, out _);
            KMHPatch.Features.Catalog.CatalogPushGuard.MarkSent(GuardProbe, "host:1", t0);
            bool skipsSame = KMHPatch.Features.Catalog.CatalogPushGuard.AlreadySent(
                                 GuardProbe, "host:1", t0.AddMinutes(5), out int ago) && ago == 300;
            bool sendsOther = !KMHPatch.Features.Catalog.CatalogPushGuard.AlreadySent(GuardProbe, "other:2", t0.AddMinutes(5), out _);
            bool expires = !KMHPatch.Features.Catalog.CatalogPushGuard.AlreadySent(GuardProbe, "host:1", t0.AddMinutes(11), out _);
            // An unknown endpoint must always push: guessing would withhold a catalog the server needs to price sites.
            bool unknownPushes = !KMHPatch.Features.Catalog.CatalogPushGuard.AlreadySent(GuardProbe, "", t0.AddMinutes(1), out _);
            KMHPatch.Features.Catalog.CatalogPushGuard.Forget(GuardProbe);
            bool forgotten = !KMHPatch.Features.Catalog.CatalogPushGuard.AlreadySent(GuardProbe, "host:1", t0.AddMinutes(1), out _);
            r.Add(("catalog: the same catalog is not streamed to the same server twice inside the window",
                   coldPushes && skipsSame && sendsOther && expires && unknownPushes && forgotten,
                   $"cold={coldPushes} skipsSame={skipsSame} othersPush={sendsOther} expires={expires} "
                   + $"unknownPushes={unknownPushes} forgettable={forgotten}"));
            r.Add(("catalog: both pushers share one throttle, so neither can lose it",
                   KMHPatch.Features.Catalog.ItemLabelsSender.GuardKind
                       != KMHPatch.Features.Catalog.SiteMetadataSender.GuardKind, "they must not share a key either"));

            // A marker that cannot be placed is retried every reconcile, so the log must not repeat the same reason.
            var seen = new Dictionary<int, string>();
            bool first  = KMHPatch.Features.Sites.WorldComponent_KMHSiteMarkers.ShouldWarnPlacement(seen, 34860, "NRE");
            bool repeat = KMHPatch.Features.Sites.WorldComponent_KMHSiteMarkers.ShouldWarnPlacement(seen, 34860, "NRE");
            bool changed = KMHPatch.Features.Sites.WorldComponent_KMHSiteMarkers.ShouldWarnPlacement(seen, 34860, "out of range");
            bool other  = KMHPatch.Features.Sites.WorldComponent_KMHSiteMarkers.ShouldWarnPlacement(seen, 124049, "NRE");
            r.Add(("markers: an unchanged placement failure is logged once, a changed reason still reports",
                   first && !repeat && changed && other, $"{first}/{repeat}/{changed}/{other}"));

            var rich = new KMHPatch.Features.PlayerStats.Dto.PlayerLeaderboardEntry { Wealth = 120_000, KmhWealth = 500_000 };
            // A server too old to send a KMH figure must read as map wealth, not as a colony that lost half its worth.
            var legacy = new KMHPatch.Features.PlayerStats.Dto.PlayerLeaderboardEntry { Wealth = 120_000 };
            r.Add(("standings: total wealth is settlements plus KMH holdings, and falls back to settlements alone",
                   rich.TotalWealth == 620_000 && legacy.TotalWealth == 120_000,
                   $"{rich.TotalWealth} / {legacy.TotalWealth}"));

            var mentioned = KMHPatch.Features.Chat.ChatMentions.Find("hey @Alice and @bob-2, ask @Alice again");
            r.Add(("mentions: every name is found once, in the order it was typed",
                   mentioned.Count == 2 && mentioned[0] == "Alice" && mentioned[1] == "bob-2",
                   string.Join(",", mentioned)));

            r.Add(("mentions: punctuation ends a name, and an address is not a mention",
                   KMHPatch.Features.Chat.ChatMentions.MentionsMe("thanks @Carol!", "Carol")
                   && KMHPatch.Features.Chat.ChatMentions.MentionsMe("@Carol", "carol")
                   && !KMHPatch.Features.Chat.ChatMentions.MentionsMe("mail me at bob@carol.com", "Carol")
                   && !KMHPatch.Features.Chat.ChatMentions.MentionsMe("@Carolyn is here", "Carol"), ""));

            // A line relayed from Discord must never be able to notify a whole server from outside it.
            r.Add(("mentions: everyone, here and all never resolve to a player",
                   KMHPatch.Features.Chat.ChatMentions.Find("@everyone @here @all @channel").Count == 0
                   && !KMHPatch.Features.Chat.ChatMentions.MentionsMe("@everyone get in here", "everyone")
                   && KMHPatch.Features.Chat.ChatMentions.IsReserved("HERE"), ""));

            // A finished mention must stop offering, or it completes twice.
            r.Add(("mentions: a name being typed is offered, and a finished one is not",
                   KMHPatch.Features.Chat.ChatMentions.PartialAt("hey @ali") == "ali"
                   && KMHPatch.Features.Chat.ChatMentions.PartialAt("@") == ""
                   && KMHPatch.Features.Chat.ChatMentions.PartialAt("hey @alice ") == null
                   && KMHPatch.Features.Chat.ChatMentions.PartialAt("no at sign here") == null
                   && KMHPatch.Features.Chat.ChatMentions.PartialAt("mail bob@carol") == null,
                   KMHPatch.Features.Chat.ChatMentions.PartialAt("hey @ali") ?? "null"));

            r.Add(("mentions: completing replaces what was typed, never the text before it",
                   KMHPatch.Features.Chat.ChatMentions.Complete("hey @ali", "Alice") == "hey @Alice "
                   && KMHPatch.Features.Chat.ChatMentions.Complete("@", "Alice") == "@Alice "
                   && KMHPatch.Features.Chat.ChatMentions.Complete("hey @alice ", "Bob") == "hey @alice @Bob ",
                   KMHPatch.Features.Chat.ChatMentions.Complete("hey @ali", "Alice")));

            r.Add(("mentions: clicking a name leaves a space either side of what is already typed",
                   KMHPatch.Features.Chat.ChatMentions.Insert("", "Alice") == "@Alice "
                   && KMHPatch.Features.Chat.ChatMentions.Insert("hello", "Alice") == "hello @Alice "
                   && KMHPatch.Features.Chat.ChatMentions.Insert("hello ", "Alice") == "hello @Alice "
                   && KMHPatch.Features.Chat.ChatMentions.Insert("hi", "") == "hi",
                   KMHPatch.Features.Chat.ChatMentions.Insert("hello", "Alice")));

            // A Play button that does nothing when clicked is worse than no button at all.
            r.Add(("video link: a page KMH recognises is not always one it can fetch",
                   KMHPatch.Features.Chat.ChatVideoLink.IsWatchPage("https://www.twitch.tv/somestreamer")
                   && KMHPatch.Features.Chat.ChatVideoLink.IsWatchPage("https://vimeo.com/123456789")
                   && KMHPatch.Features.Chat.ChatYouTube.VideoIdOf("https://www.twitch.tv/somestreamer").Length == 0
                   && KMHPatch.Features.Chat.ChatYouTube.VideoIdOf("https://vimeo.com/123456789").Length == 0
                   && KMHPatch.Features.Chat.ChatYouTube.VideoIdOf("https://youtu.be/abc123defgh").Length == 11
                   && !KMHPatch.Features.Chat.ChatPanel.CanPlayHere("https://www.twitch.tv/somestreamer"), ""));

            // A watch page is a page, not a stream, so the row has to say so and offer the browser instead.
            r.Add(("video link: a youtube watch page is recognised in either form",
                   KMHPatch.Features.Chat.ChatVideoLink.IsWatchPage("look https://www.youtube.com/watch?v=abc123defgh")
                   && KMHPatch.Features.Chat.ChatVideoLink.IsWatchPage("https://youtu.be/abc123defgh?si=xy"), ""));
            r.Add(("youtube: the player's quality choice is honoured but never beats the owner's ceiling",
                   KMHPatch.Features.Chat.ChatYouTube.HeightWithin(360, 720) == 360
                   && KMHPatch.Features.Chat.ChatYouTube.HeightWithin(1080, 720) == 720
                   && KMHPatch.Features.Chat.ChatYouTube.HeightWithin(0, 480) == 480
                   && KMHPatch.Features.Chat.ChatYouTube.HeightWithin(0, 1080) == 1080
                   && KMHPatch.Features.Chat.ChatYouTube.HeightWithin(0, 0) == 1080, ""));

            r.Add(("video cache: a pulled-down file is named after the video and its height, and never anything else",
                   KMHPatch.Features.Chat.ChatVideoCache.PathFor("abc123defgh", 480).EndsWith("abc123defgh_480.mp4")
                   && KMHPatch.Features.Chat.ChatVideoCache.Safe("../../etc/passwd") == "______etc_passwd"
                   && KMHPatch.Features.Chat.ChatVideoCache.Safe("") == "unknown",
                   KMHPatch.Features.Chat.ChatVideoCache.Safe("../../etc/passwd")));

            // Half a file plays as a truncated video, so a download still running is not a cache hit.
            string vidDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KMH-SelfTest-Video");
            string vidFile = System.IO.Path.Combine(vidDir, "clip.mp4");
            bool readyRules;
            try
            {
                System.IO.Directory.CreateDirectory(vidDir);
                System.IO.File.WriteAllText(vidFile, "x");
                bool whole = KMHPatch.Features.Chat.ChatVideoCache.Ready(vidFile);
                System.IO.File.WriteAllText(vidFile + ".part", "x");
                bool partial = KMHPatch.Features.Chat.ChatVideoCache.Ready(vidFile);
                readyRules = whole && !partial
                             && !KMHPatch.Features.Chat.ChatVideoCache.Ready(vidFile + "-missing")
                             && !KMHPatch.Features.Chat.ChatVideoCache.Ready("");
            }
            catch { readyRules = false; }
            finally { try { System.IO.Directory.Delete(vidDir, true); } catch { } }
            r.Add(("video cache: a file still downloading is not offered as one that is ready", readyRules, ""));

            // Run against its own folder: sweeping the real one would evict what the player is keeping.
            bool trimRules;
            string trimWhy = "";
            string sandbox = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KMH-SelfTest-VideoCache");
            try
            {
                KMHPatch.Features.Chat.ChatVideoCache.FolderOverride = sandbox;
                System.IO.Directory.CreateDirectory(sandbox);
                string oldClip = KMHPatch.Features.Chat.ChatVideoCache.PathFor("selftestold1", 144);
                string newClip = KMHPatch.Features.Chat.ChatVideoCache.PathFor("selftestnew1", 144);
                System.IO.File.WriteAllText(oldClip, "x");
                System.IO.File.WriteAllText(newClip, "x");
                System.IO.File.SetLastWriteTimeUtc(oldClip, DateTime.UtcNow.AddDays(-30));
                KMHPatch.Features.Chat.ChatVideoCache.Touch(newClip);
                KMHPatch.Features.Chat.ChatVideoCache.Trim();
                trimRules = !System.IO.File.Exists(oldClip) && System.IO.File.Exists(newClip);
            }
            catch (Exception ex) { trimRules = false; trimWhy = ex.GetBaseException().Message; }
            finally
            {
                KMHPatch.Features.Chat.ChatVideoCache.FolderOverride = null;
                try { System.IO.Directory.Delete(sandbox, true); } catch { }
            }
            r.Add(("video cache: the sweep drops a stale copy and keeps the one in use", trimRules, trimWhy));

            // The relay speaks plain http, which UnityWebRequest refuses outright - so the pull is proven against a real one.
            bool pulled;
            string pullWhy = "";
            string pullBox = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KMH-SelfTest-VideoPull");
            var pullBytes = new byte[64 * 1024 + 7];
            for (int i = 0; i < pullBytes.Length; i++) pullBytes[i] = (byte)(i % 251);
            System.Net.Sockets.TcpListener http = null;
            try
            {
                KMHPatch.Features.Chat.ChatVideoCache.FolderOverride = pullBox;
                http = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                http.Start();
                int httpPort = ((System.Net.IPEndPoint)http.LocalEndpoint).Port;
                var serve = new System.Threading.Thread(() =>
                {
                    try
                    {
                        using (var c = http.AcceptTcpClient())
                        using (var s = c.GetStream())
                        {
                            var one = new byte[1];
                            int nl = 0;
                            while (nl < 2 && s.Read(one, 0, 1) == 1) nl = one[0] == (byte)'\n' ? nl + 1 : (one[0] == (byte)'\r' ? nl : 0);
                            byte[] head = System.Text.Encoding.ASCII.GetBytes(
                                "HTTP/1.1 200 OK\r\nContent-Length: " + pullBytes.Length + "\r\nConnection: close\r\n\r\n");
                            s.Write(head, 0, head.Length);
                            s.Write(pullBytes, 0, pullBytes.Length);
                            s.Flush();
                        }
                    }
                    catch { }
                })
                { IsBackground = true };
                serve.Start();

                var pull = KMHPatch.Features.Chat.ChatVideoCache.Begin(
                    "http://127.0.0.1:" + httpPort + "/clip.mp4", "selftestpull", 144);
                DateTime until = DateTime.UtcNow.AddSeconds(20);
                while (!pull.WorkerDone && DateTime.UtcNow < until) System.Threading.Thread.Sleep(20);

                string got = KMHPatch.Features.Chat.ChatVideoCache.PathFor("selftestpull", 144) + ".part";
                byte[] onDisk = System.IO.File.Exists(got) ? System.IO.File.ReadAllBytes(got) : new byte[0];
                pulled = pull.WorkerDone && string.IsNullOrEmpty(pull.WorkerError) && onDisk.Length == pullBytes.Length;
                for (int i = 0; pulled && i < pullBytes.Length; i++) if (onDisk[i] != pullBytes[i]) pulled = false;
                if (!pulled) pullWhy = $"done={pull.WorkerDone}, err={pull.WorkerError}, bytes={onDisk.Length}/{pullBytes.Length}";
            }
            catch (Exception ex) { pulled = false; pullWhy = ex.GetBaseException().Message; }
            finally
            {
                try { http?.Stop(); } catch { }
                KMHPatch.Features.Chat.ChatVideoCache.FolderOverride = null;
                try { System.IO.Directory.Delete(pullBox, true); } catch { }
            }
            r.Add(("video cache: a plain-http stream is pulled down byte for byte", pulled, pullWhy));

            r.Add(("video cache: a local copy is played as a file, not fetched over the network again",
                   KMHPatch.Features.Chat.ChatVideoCache.PlayableUrl(@"C:\tmp\a b\clip.mp4")
                       == "file://C:/tmp/a b/clip.mp4",
                   KMHPatch.Features.Chat.ChatVideoCache.PlayableUrl(@"C:\tmp\a b\clip.mp4")));

            r.Add(("youtube: a server-served stream is addressed to the connected host, and only by token",
                   KMHPatch.Features.Chat.ChatVideoServer.StreamUrl("10.0.0.4", 5098, "a1b2c3")
                       == "http://10.0.0.4:5098/a1b2c3.mp4"
                   && KMHPatch.Features.Chat.ChatVideoServer.StreamUrl("10.0.0.4", 5098, "../secret").Length == 0
                   && KMHPatch.Features.Chat.ChatVideoServer.StreamUrl("10.0.0.4", 5098, "a b").Length == 0
                   && KMHPatch.Features.Chat.ChatVideoServer.StreamUrl("", 5098, "a1b2c3").Length == 0
                   && KMHPatch.Features.Chat.ChatVideoServer.StreamUrl("10.0.0.4", 0, "a1b2c3").Length == 0, ""));

            // Off-screen rows are skipped, or every picture in a long history is fetched at once.
            bool Seen(float top, float h, float scroll, float view)
                => KMHPatch.Features.Chat.ChatPanel.OnScreen(top, h, scroll, view);
            r.Add(("chat log: rows on screen are drawn, and rows far above or below are not",
                   Seen(1000f, 40f, 900f, 300f) && Seen(880f, 40f, 900f, 300f) && Seen(1250f, 40f, 900f, 300f)
                   && !Seen(0f, 40f, 5000f, 300f) && !Seen(9000f, 40f, 900f, 300f), ""));
            r.Add(("chat log: a row taller than the view still counts as on screen",
                   Seen(500f, 900f, 900f, 300f), ""));

            // Rows measured in one font and drawn in another are cut off; the caller's font must not leak in or out.
            Verse.GameFont callerFont = Verse.Text.Font, inside = Verse.GameFont.Tiny;
            Verse.Text.Font = Verse.GameFont.Medium;
            KMHPatch.Features.Chat.ChatPanel.InLogFont(_ => inside = Verse.Text.Font, default);
            Verse.GameFont after = Verse.Text.Font;
            Verse.Text.Font = callerFont;
            r.Add(("chat log: measured and drawn in its own font whatever the caller left set",
                   inside == KMHPatch.Features.Chat.ChatPanel.LogFont && after == Verse.GameFont.Medium, $"{inside}/{after}"));

            TimeSpan show = KMHPatch.Features.Chat.ChatOverlay.ShowFor, fadeFor = KMHPatch.Features.Chat.ChatOverlay.FadeFor;
            float fadeHalf = KMHPatch.Features.Chat.ChatOverlay.Alpha(show + TimeSpan.FromTicks(fadeFor.Ticks / 2));
            r.Add(("chat overlay: a line holds, then fades, then is gone",
                   KMHPatch.Features.Chat.ChatOverlay.Alpha(TimeSpan.Zero) == 1f && fadeHalf > 0.4f && fadeHalf < 0.6f
                   && KMHPatch.Features.Chat.ChatOverlay.Alpha(show + fadeFor + TimeSpan.FromSeconds(1)) <= 0f, fadeHalf.ToString("0.00")));
            r.Add(("chat overlay: direct messages never go on the screen",
                   KMHPatch.Features.Chat.ChatOverlay.Shows("server") && KMHPatch.Features.Chat.ChatOverlay.Shows("guild:Universe")
                   && KMHPatch.Features.Chat.ChatOverlay.Shows("global") && !KMHPatch.Features.Chat.ChatOverlay.Shows("dm:ada|bo")
                   && !KMHPatch.Features.Chat.ChatOverlay.Shows("gdm:abc"), ""));
            const float screenH = 1080f;
            float clear = KMHPatch.Features.Chat.ChatOverlay.BottomFor(screenH, 10f, 460f, 120f, new List<UnityEngine.Rect>());
            float overInspect = KMHPatch.Features.Chat.ChatOverlay.BottomFor(screenH, 10f, 460f, 120f,
                new List<UnityEngine.Rect> { new UnityEngine.Rect(0f, screenH - 35f - 165f, 432f, 165f), new UnityEngine.Rect(1500f, screenH - 235f, 400f, 200f),
                                             new UnityEngine.Rect(200f, 300f, 600f, 400f) });
            // The Architect menu: its column at the bottom and the description pane floating above it.
            float overArchitect = KMHPatch.Features.Chat.ChatOverlay.BottomFor(screenH, 10f, 460f, 120f,
                new List<UnityEngine.Rect> { new UnityEngine.Rect(0f, 760f, 200f, 285f), new UnityEngine.Rect(0f, 490f, 200f, 270f) });
            r.Add(("chat overlay: sits above the bottom bar and its readout, and above every window it would overlap but no other",
                   clear == screenH - KMHPatch.Features.Chat.ChatOverlay.BottomBarH - KMHPatch.Features.Chat.ChatOverlay.ReadoutRoom
                   && overInspect == screenH - 35f - 165f - 8f && overArchitect == 490f - 8f, $"{clear}/{overInspect}/{overArchitect}"));

            string flatLine = KMHPatch.Features.Chat.ChatOverlay.OneLine("a\nb   c\r\n\r\nd", 50);
            string capped = KMHPatch.Features.Chat.ChatOverlay.OneLine(new string('x', 300), 40);
            r.Add(("chat overlay: a pasted block shows as one line, and a long one is cut",
                   flatLine == "a / b c / d" && capped.Length == 41 && capped.EndsWith("…"), flatLine));

            r.Add(("chat: a line the overlay is showing raises no second toast, unless it names you",
                   !KMHPatch.Features.Chat.KmhCommsNotifications.ToastWanted(false, true)
                   && KMHPatch.Features.Chat.KmhCommsNotifications.ToastWanted(true, true)
                   && KMHPatch.Features.Chat.KmhCommsNotifications.ToastWanted(false, false), ""));

            float o1 = KMHPatchSettings.NextChatOpacity(1f), o2 = KMHPatchSettings.NextChatOpacity(o1),
                  o3 = KMHPatchSettings.NextChatOpacity(o2), o4 = KMHPatchSettings.NextChatOpacity(o3);
            r.Add(("chat window opacity: the button steps 80, 60, 40 and back to solid, and never below the floor",
                   o1 == 0.8f && o2 == 0.6f && o3 == 0.4f && o4 == 1f && KMHPatchSettings.ChatOpacity(null) == 1f
                   && KMHPatchSettings.ChatOpacity(new KMHPatchSettings { ChatWindowOpacity = 0.05f }) == KMHPatchSettings.MinChatOpacity,
                   $"{o1}/{o2}/{o3}/{o4}"));
            // Read from the running game: without both members every window silently stays solid.
            r.Add(("chat window opacity: the window background hooks exist in this RimWorld",
                   KMHPatch.Patches.Patch_WindowDrawing_KmhGlass.Target() != null
                   && HarmonyLib.AccessTools.Method(typeof(Verse.Window), "InnerWindowOnGUI") != null, ""));

            // Opening the tab asks for every snapshot; once a minute per server is enough.
            DateTime tabNow = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            r.Add(("tab: a full refresh on open happens at most once a minute, and at once on another server",
                   KMHPatch.Tabs.MainTabWindow_KMH.TabRefreshDue(null, DateTime.MinValue, "s1", tabNow)
                   && !KMHPatch.Tabs.MainTabWindow_KMH.TabRefreshDue("s1", tabNow.AddSeconds(-10), "s1", tabNow)
                   && KMHPatch.Tabs.MainTabWindow_KMH.TabRefreshDue("s1", tabNow.AddSeconds(-61), "s1", tabNow)
                   && KMHPatch.Tabs.MainTabWindow_KMH.TabRefreshDue("s1", tabNow.AddSeconds(-10), "s2", tabNow), ""));

            r.Add(("tab: the Colony Nexus tab rebuilds its feature tiles twice a second, not on every GUI event",
                   !KMHPatch.Tabs.MainTabWindow_KMH.TilesDue(10.2f, 10f) && KMHPatch.Tabs.MainTabWindow_KMH.TilesDue(10.5f, 10f)
                   && KMHPatch.Tabs.MainTabWindow_KMH.TilesDue(1f, -1f) && KMHPatch.Tabs.MainTabWindow_KMH.TilesDue(5f, 10f), ""));

            var dashLines = new List<(string Label, string Value, UnityEngine.Color color, Func<string> Tip)> { ("Treasury", "1", UnityEngine.Color.white, null) };
            List<string> dashShownA = KMHPatch.UI.KMHDashboard.ShownLabels(dashLines), dashShownB = KMHPatch.UI.KMHDashboard.ShownLabels(dashLines);
            r.Add(("dashboard: row labels are wrapped once per rebuilt row set, not on every GUI event",
                   ReferenceEquals(dashShownA, dashShownB) && dashShownA.Count == 1 && dashShownA[0].Contains("Treasury"),
                   dashShownA.Count > 0 ? dashShownA[0] : "(none)"));

            // Perishables are full-state copies; the server pays one delivery from compact stock OR copies, never their sum.
            var vault = new KMHPatch.Features.Treasury.Dto.TreasurySnapshot
            {
                Items = new Dictionary<string, int> { ["Meat_Pig"] = 10 },
                ItemPayloads = new List<KMHPatch.Items.KmhThingPayload>
                {
                    new KMHPatch.Items.KmhThingPayload { DefName = "Meat_Pig", StackCount = 30 },
                    new KMHPatch.Items.KmhThingPayload { DefName = "Meat_Pig", StackCount = 30 },
                    new KMHPatch.Items.KmhThingPayload { DefName = "Meat_Cow", StackCount = 5 },
                    null,
                },
            };
            int pork = KMHPatch.Features.Treasury.TreasuryCache.DeliverableCountOf(vault, "Meat_Pig");
            vault.Items["Meat_Pig"] = 80;
            int porkCompact = KMHPatch.Features.Treasury.TreasuryCache.DeliverableCountOf(vault, "Meat_Pig");
            r.Add(("treasury: a global quest delivery offers what one source can pay, full-state copies included",
                   pork == 60 && porkCompact == 80
                   && KMHPatch.Features.Treasury.TreasuryCache.DeliverableCountOf(vault, "Steel") == 0
                   && KMHPatch.Features.Treasury.TreasuryCache.DeliverableCountOf(null, "Meat_Pig") == 0, $"{pork}, {porkCompact}"));

            KmhSlowWork.ResetForTest();
            bool slowFirst = KmhSlowWork.ShouldLog("kmh.selftest.slow", 40, tabNow);
            bool slowAgain = KmhSlowWork.ShouldLog("kmh.selftest.slow", 40, tabNow.AddSeconds(10));
            bool slowLater = KmhSlowWork.ShouldLog("kmh.selftest.slow", 40, tabNow.AddSeconds(61));
            bool fast = KmhSlowWork.ShouldLog("kmh.selftest.fast", 5, tabNow);
            KmhSlowWork.ResetForTest();
            r.Add(("perf: work past a frame is named in the log once a minute, and fast work never",
                   slowFirst && !slowAgain && slowLater && !fast, $"{slowFirst}/{slowAgain}/{slowLater}/{fast}"));

            // Detection and playback must agree, or a link gets a card with no way to play it.
            string longForm  = KMHPatch.Features.Chat.ChatVideoLink.FirstWatchUrl("watch https://www.youtube.com/watch?v=wc-aeLXFZSw&t=30 now");
            string shortForm = KMHPatch.Features.Chat.ChatVideoLink.FirstWatchUrl("https://youtu.be/wc-aeLXFZSw?si=fL-vXekx0nPaiUrg");
            r.Add(("video link: the short and long forms both reach playback with the same id",
                   KMHPatch.Features.Chat.ChatYouTube.VideoIdOf(longForm) == "wc-aeLXFZSw"
                   && KMHPatch.Features.Chat.ChatYouTube.VideoIdOf(shortForm) == "wc-aeLXFZSw"
                   && KMHPatch.Features.Chat.ChatYouTube.StartSecondsOf(longForm) == 30, shortForm));

            r.Add(("video link: the site is named for the label",
                   KMHPatch.Features.Chat.ChatVideoLink.SiteOf("https://youtu.be/abc123defgh") == "YouTube"
                   && KMHPatch.Features.Chat.ChatVideoLink.SiteOf("https://clips.twitch.tv/x") == "Twitch", ""));
            r.Add(("video link: an image link and ordinary text are not watch pages",
                   !KMHPatch.Features.Chat.ChatVideoLink.IsWatchPage("https://i.imgur.com/a.png")
                   && !KMHPatch.Features.Chat.ChatVideoLink.IsWatchPage("just talking about youtube")
                   && !KMHPatch.Features.Chat.ChatVideoLink.IsWatchPage(""), ""));
            r.Add(("video link: a lookalike host is not treated as youtube",
                   !KMHPatch.Features.Chat.ChatVideoLink.IsWatchPage("https://youtube.com.evil.example/watch?v=x"), ""));

            // Naming media before its bytes exist can only read the url; naming it afterwards knows.
            r.Add(("media label: a gif url is called a GIF before it loads",
                   KMHPatch.Features.Chat.ChatMediaLabel.Kind("https://media.tenor.com/a/b.gif") == "GIF"
                   && KMHPatch.Features.Chat.ChatMediaLabel.Kind("https://media.tenor.com/a/b.gif?ex=1") == "GIF", ""));
            r.Add(("media label: anything else is just an image until it loads",
                   KMHPatch.Features.Chat.ChatMediaLabel.Kind("https://x.example/a.webp") == "image"
                   && KMHPatch.Features.Chat.ChatMediaLabel.Kind("") == "image", ""));
            r.Add(("media label: once decoded, animated is stated rather than guessed",
                   KMHPatch.Features.Chat.ChatMediaLabel.KindOf("https://x.example/a.webp", true) == "animated image"
                   && KMHPatch.Features.Chat.ChatMediaLabel.KindOf("https://x.example/a.webp", false) == "image"
                   && KMHPatch.Features.Chat.ChatMediaLabel.KindOf("https://x.example/a.gif", true) == "GIF", ""));

            // Two windows can show chat at once, so "am I reading this channel" must survive one of them closing.
            KMHPatch.Features.Comms.CommsFocus.Clear();
            KMHPatch.Features.Comms.CommsFocus.Enter("server");
            KMHPatch.Features.Comms.CommsFocus.Enter("server");
            KMHPatch.Features.Comms.CommsFocus.Leave("server");
            r.Add(("comms focus: a channel two windows show is still being viewed when one closes",
                   KMHPatch.Features.Comms.CommsFocus.IsViewing("server"),
                   KMHPatch.Features.Comms.CommsFocus.ViewerCount("server").ToString()));
            KMHPatch.Features.Comms.CommsFocus.Leave("server");
            r.Add(("comms focus: the last window closing ends the view",
                   !KMHPatch.Features.Comms.CommsFocus.IsViewing("server"), ""));
            KMHPatch.Features.Comms.CommsFocus.Leave("server");
            r.Add(("comms focus: an extra close never drives the count negative",
                   KMHPatch.Features.Comms.CommsFocus.ViewerCount("server") == 0, ""));

            string held = null;
            KMHPatch.Features.Comms.CommsFocus.Switch(ref held, "guild:Iron");
            KMHPatch.Features.Comms.CommsFocus.Switch(ref held, "dm:a|b");
            r.Add(("comms focus: switching channels leaves no stale viewer behind",
                   KMHPatch.Features.Comms.CommsFocus.IsViewing("dm:a|b")
                   && !KMHPatch.Features.Comms.CommsFocus.IsViewing("guild:Iron"), held));
            KMHPatch.Features.Comms.CommsFocus.Switch(ref held, null);
            r.Add(("comms focus: a window on a non-chat view (mail) is viewing nothing",
                   !KMHPatch.Features.Comms.CommsFocus.IsViewing("dm:a|b")
                   && !KMHPatch.Features.Comms.CommsFocus.IsViewing(null), held ?? "(none)"));
            KMHPatch.Features.Comms.CommsFocus.Clear();

            // A Discord CDN signature is hex unix seconds in `ex`; without reading it the client retries a dead link.
            System.DateTime probe = new System.DateTime(2026, 1, 2, 3, 4, 5, System.DateTimeKind.Utc);
            long probeUnix = (long)(probe - new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)).TotalSeconds;
            string expiredUrl = "https://cdn.discordapp.com/attachments/1/2/a.webp?ex=" + (probeUnix - 3600).ToString("x");
            string liveUrl    = "https://cdn.discordapp.com/attachments/1/2/a.webp?ex=" + (probeUnix + 3600).ToString("x");
            r.Add(("media: an expired Discord signature is recognised so no retry is offered",
                   KMHPatch.Features.Chat.ChatSignedLink.IsExpired(expiredUrl, probe), expiredUrl));
            r.Add(("media: a signature still in date is not called expired",
                   !KMHPatch.Features.Chat.ChatSignedLink.IsExpired(liveUrl, probe), liveUrl));
            r.Add(("media: a url that carries no signature is never called expired",
                   !KMHPatch.Features.Chat.ChatSignedLink.IsExpired("https://i.imgur.com/a.png", probe)
                   && !KMHPatch.Features.Chat.ChatSignedLink.IsExpired("", probe)
                   && !KMHPatch.Features.Chat.ChatSignedLink.IsExpired("https://cdn.discordapp.com/a.png?ex=zzz", probe), ""));

            byte[] payload = new byte[70];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7);
            string goodHash = Sha256Hex(payload);

            KMHPatch.Features.Chat.ChatMediaClient.Assembly Fresh()
                => KMHPatch.Features.Chat.ChatMediaClient.Assembly.Begin("image/gif", 70, 3, goodHash, 4 * 1024 * 1024, out _);

            byte[] Part(int i) => i == 0 ? Slice(payload, 0, 30) : i == 1 ? Slice(payload, 30, 30) : Slice(payload, 60, 10);

            var ok = Fresh();
            ok.Add(0, 3, Part(0)); ok.Add(1, 3, Part(1)); ok.Add(2, 3, Part(2));
            byte[] joined = ok.Take();
            r.Add(("media: a complete, correct stream assembles and verifies",
                   joined != null && joined.Length == 70 && joined[69] == payload[69], ok.Error ?? ""));

            var shuffled = Fresh();
            shuffled.Add(2, 3, Part(2)); shuffled.Add(0, 3, Part(0)); shuffled.Add(1, 3, Part(1));
            byte[] reordered = shuffled.Take();
            r.Add(("media: out-of-order chunks still assemble in the right order",
                   reordered != null && Same(reordered, payload), shuffled.Error ?? ""));

            var dup = Fresh();
            dup.Add(0, 3, Part(0));
            bool dupRefused = !dup.Add(0, 3, Part(0));
            r.Add(("media: a repeated chunk is refused", dupRefused && dup.Error.Contains("repeated"), dup.Error ?? ""));

            var oor = Fresh();
            r.Add(("media: a chunk index outside the stream is refused",
                   !oor.Add(9, 3, Part(0)) && oor.Error.Contains("out of range"), oor.Error ?? ""));

            var shape = Fresh();
            r.Add(("media: a chunk that disagrees about the count is refused",
                   !shape.Add(0, 4, Part(0)) && shape.Error.Contains("shape"), shape.Error ?? ""));

            var over = Fresh();
            over.Add(0, 3, Part(0)); over.Add(1, 3, Part(1));
            r.Add(("media: more bytes than declared is refused",
                   !over.Add(2, 3, new byte[999]) && over.Error.Contains("longer than declared"), over.Error ?? ""));

            var missing = Fresh();
            missing.Add(0, 3, Part(0)); missing.Add(2, 3, Part(2));
            r.Add(("media: a missing chunk never completes and never yields bytes",
                   !missing.Complete && missing.Take() == null, missing.Error ?? ""));

            var tampered = KMHPatch.Features.Chat.ChatMediaClient.Assembly.Begin(
                "image/gif", 70, 3, Sha256Hex(new byte[] { 9, 9, 9 }), 4 * 1024 * 1024, out _);
            tampered.Add(0, 3, Part(0)); tampered.Add(1, 3, Part(1)); tampered.Add(2, 3, Part(2));
            r.Add(("media: bytes that fail the hash are refused, not decoded",
                   tampered.Take() == null && tampered.Error.Contains("integrity"), tampered.Error ?? ""));

            // The meta is the server's claim; the client's own bounds decide whether to act on it.
            r.Add(("media: an over-large declared size is refused before allocating",
                   KMHPatch.Features.Chat.ChatMediaClient.Assembly.Begin("image/gif", 999_999_999, 2, goodHash, 4 * 1024 * 1024, out string tooBigWhy) == null
                   && tooBigWhy.Contains("too large"), tooBigWhy));
            r.Add(("media: a nonsensical chunk count is refused",
                   KMHPatch.Features.Chat.ChatMediaClient.Assembly.Begin("image/gif", 70, 0, goodHash, 4 * 1024 * 1024, out _) == null
                   && KMHPatch.Features.Chat.ChatMediaClient.Assembly.Begin("image/gif", 70, 99999, goodHash, 4 * 1024 * 1024, out _) == null, ""));
            r.Add(("media: media with no integrity hash is refused outright",
                   KMHPatch.Features.Chat.ChatMediaClient.Assembly.Begin("image/gif", 70, 3, "", 4 * 1024 * 1024, out string noHashWhy) == null
                   && noHashWhy.Contains("integrity hash"), noHashWhy));

            // The two key kinds differ in who the media's host gets to see, so they must never collide by accident.
            r.Add(("media: a resolver id and a url are told apart",
                   KMHPatch.Features.Chat.ChatImageCache.IsResolvedId("a1b2c3d4e5f6")
                   && !KMHPatch.Features.Chat.ChatImageCache.IsResolvedId("https://cdn.discordapp.com/a/b.png")
                   && !KMHPatch.Features.Chat.ChatImageCache.IsResolvedId(""), ""));
            r.Add(("media: resolved media names this server, never a third-party host",
                   KMHPatch.Features.Chat.ChatImageCache.HostOf("a1b2c3d4e5f6") == "this server"
                   && KMHPatch.Features.Chat.ChatImageCache.HostOf("https://cdn.discordapp.com/a/b.png") == "cdn.discordapp.com", ""));

            r.Add(("video: a clip over the owner's limit is refused",
                   KMHPatch.Features.Chat.ChatVideoPlayer.ExceedsLimit(700d, 600), ""));
            r.Add(("video: a clip inside the limit plays",
                   !KMHPatch.Features.Chat.ChatVideoPlayer.ExceedsLimit(120d, 600)
                   && !KMHPatch.Features.Chat.ChatVideoPlayer.ExceedsLimit(600d, 600), ""));
            r.Add(("video: a zero limit means no limit at all",
                   !KMHPatch.Features.Chat.ChatVideoPlayer.ExceedsLimit(99999d, 0), ""));
            // A live stream reports no length; refusing that would block streams rather than long files.
            r.Add(("video: an unknown length is not treated as over the limit",
                   !KMHPatch.Features.Chat.ChatVideoPlayer.ExceedsLimit(0d, 600), ""));

            const string signedUrl = "https://media.discordapp.net/attachments/111111111111111111/222222222222222222/"
                                   + "anim.gif?ex=6a8736a0&is=6a85e520&hm=00000000000000000000";
            string shownA = KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(signedUrl, signedUrl, out bool didA);
            r.Add(("media: a body that is only a long signed media url becomes a short label",
                   didA && shownA == "anim.gif · media.discordapp.net", shownA));

            // The vetted url a message carries can differ from the body's by a ?format= hint the server appended.
            string shownB = KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(
                signedUrl, signedUrl + "&format=png", out bool didB);
            r.Add(("media: a ?format= hint does not stop the body being recognised as the same media",
                   didB, shownB));

            string caption = "look at this " + signedUrl;
            // The url FIRST, then words - so the "starts with http" guard cannot be what refuses it.
            string trailing = signedUrl + " nice one";
            r.Add(("media: a url followed by someone's words is never hidden",
                   KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(trailing, signedUrl, out bool didT) == trailing
                   && !didT, ""));
            r.Add(("media: a caption alongside the url is never hidden",
                   KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(caption, signedUrl, out bool didC) == caption && !didC, ""));
            // Deliberately LONG, so the length guard is not what saves it - only "this is a page, not a file" is.
            const string yt = "https://www.youtube.com/watch?v=mkggXE5e2yk&list=PLQMknD_RFuSBmU5c6Yfza_ttxl5oXSGm0"
                            + "&index=1&pp=gAQBiAQBsAQB&shareref=kmh&feature=shared";
            r.Add(("media: a long YouTube link stays readable, because it is a page and not a file",
                   KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(yt, yt, out bool didD) == yt && !didD,
                   yt.Length + " chars"));
            const string tenorPage = "https://tenor.com/view/logan-paul-sorry-gif-22857221";
            r.Add(("media: a tenor or klipy share page stays readable",
                   KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(tenorPage, tenorPage, out _) == tenorPage
                   && KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(
                       "https://klipy.com/gifs/thing", "https://klipy.com/gifs/thing", out _) == "https://klipy.com/gifs/thing", ""));
            r.Add(("media: a short unsigned media url is left alone",
                   KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(
                       "https://i.imgur.com/a.png", "https://i.imgur.com/a.png", out bool didE) == "https://i.imgur.com/a.png"
                   && !didE, ""));
            // SAME host, different path - so only the path comparison can tell them apart.
            r.Add(("media: a body naming DIFFERENT media on the same host is never replaced",
                   !KMHPatch.Features.Chat.ChatMediaLabel.SameMedia(
                       signedUrl, "https://media.discordapp.net/attachments/1/2/somethingelse.gif")
                   && KMHPatch.Features.Chat.ChatMediaLabel.SameMedia(signedUrl, signedUrl + "&format=png"), ""));
            r.Add(("media: an empty body and a message with no media are both left alone",
                   KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody("", signedUrl, out _) == ""
                   && KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(signedUrl, "", out _) == signedUrl, ""));

            r.Add(("video: the clock reads mm:ss, and hours only when there are hours",
                   KMHPatch.Features.Chat.ChatVideoPlayer.FormatTime(0d) == "0:00"
                   && KMHPatch.Features.Chat.ChatVideoPlayer.FormatTime(65d) == "1:05"
                   && KMHPatch.Features.Chat.ChatVideoPlayer.FormatTime(3725d) == "1:02:05",
                   KMHPatch.Features.Chat.ChatVideoPlayer.FormatTime(3725d)));

            // VideoPlayer.length is NaN until the stream reports one, and that would otherwise print as garbage.
            r.Add(("video: an unknown or negative duration reads as zero, not as junk",
                   KMHPatch.Features.Chat.ChatVideoPlayer.FormatTime(double.NaN) == "0:00"
                   && KMHPatch.Features.Chat.ChatVideoPlayer.FormatTime(double.PositiveInfinity) == "0:00"
                   && KMHPatch.Features.Chat.ChatVideoPlayer.FormatTime(-12d) == "0:00", ""));

            r.Add(("video: volume cannot be dragged below silence or above full",
                   KMHPatch.Features.Chat.ChatVideoPlayer.ClampVolume(-3f) == 0f
                   && KMHPatch.Features.Chat.ChatVideoPlayer.ClampVolume(7f) == 1f
                   && KMHPatch.Features.Chat.ChatVideoPlayer.ClampVolume(0.5f) == 0.5f, ""));

            // Seeking exactly to length ends the clip, so a forward jump must land just inside the end.
            double past = KMHPatch.Features.Chat.ChatVideoPlayer.SeekTarget(58d, 5d, 60d);
            r.Add(("video: seeking past the end lands inside it, not on it",
                   past > 0d && past < 60d, past.ToString("0.00")));
            r.Add(("video: seeking before the start clamps to zero",
                   KMHPatch.Features.Chat.ChatVideoPlayer.SeekTarget(2d, -5d, 60d) == 0d, ""));
            r.Add(("video: a stream with no length has nowhere to seek to",
                   KMHPatch.Features.Chat.ChatVideoPlayer.SeekTarget(10d, 5d, 0d) == 0d, ""));

            // A portrait clip in a wide window is the case that gets stretched if the fit is done carelessly.
            UnityEngine.Rect box = new UnityEngine.Rect(0f, 0f, 1600f, 900f);
            UnityEngine.Rect fit = KMHPatch.Features.Chat.ChatVideoPlayer.FitInto(720, 1280, box);
            float wantAspect = 720f / 1280f;
            r.Add(("video: fullscreen letterboxes a portrait clip instead of stretching it",
                   fit.width <= box.width + 0.5f && fit.height <= box.height + 0.5f
                   && System.Math.Abs(fit.width / fit.height - wantAspect) < 0.01f,
                   $"{fit.width:0}x{fit.height:0}"));
            r.Add(("video: the fitted picture is centred in the space it was given",
                   System.Math.Abs(fit.center.x - box.center.x) < 0.5f
                   && System.Math.Abs(fit.center.y - box.center.y) < 0.5f, ""));

            // A stream that never reported its size would divide by zero and blank the picture entirely.
            UnityEngine.Rect degenerate = KMHPatch.Features.Chat.ChatVideoPlayer.FitInto(0, 0, box);
            r.Add(("video: a stream with no reported size still gets a real rectangle",
                   degenerate.width > 1f && degenerate.height > 1f, ""));

            KMHPatch.UI.KmhTheme.ApplyServerChatTheme("FF0000", "00FF00", "0000FF", "FFFF00", "##");
            bool applied = KMHPatch.UI.KmhTheme.ServerNameNormal == "FF0000" && KMHPatch.UI.KmhTheme.DiscordMarker == "##";
            KMHPatch.UI.KmhTheme.ClearServerTheme();
            r.Add(("theme: server chat colours apply then clear on server switch",
                   applied && KMHPatch.UI.KmhTheme.ServerNameNormal == ""
                           && KMHPatch.UI.KmhTheme.ServerTextDiscord == ""
                           && KMHPatch.UI.KmhTheme.DiscordMarker == KMHPatch.UI.KmhTheme.DefaultDiscordMarker, ""));

            // DoWindowContents runs once per event, so a height published every pass gives BeginScrollView two rects.
            int frameA = -1;
            r.Add(("frame gate: first pass of a frame publishes", KMHPatch.UI.KmhScroll.NewFrame(ref frameA, 100), ""));
            r.Add(("frame gate: later passes of the SAME frame do not",
                   !KMHPatch.UI.KmhScroll.NewFrame(ref frameA, 100) && !KMHPatch.UI.KmhScroll.NewFrame(ref frameA, 100), ""));
            r.Add(("frame gate: the next frame publishes again", KMHPatch.UI.KmhScroll.NewFrame(ref frameA, 101), ""));

            float collapsedH = 240f, expandedH = 240f + 5f * KMHPatch.UI.IconButton.ListingLineHeight;
            r.Add(("advanced: expanded content is taller than collapsed", expandedH > collapsedH, $"{expandedH} > {collapsedH}"));

            float viewport = 300f;
            float atBottom = KMHPatch.UI.KmhScroll.MaxOffset(expandedH, viewport);
            r.Add(("advanced: scroll max grows when expanded", atBottom > KMHPatch.UI.KmhScroll.MaxOffset(collapsedH, viewport), $"{atBottom}"));
            float afterCollapse = KMHPatch.UI.KmhScroll.Clamp(atBottom, collapsedH, viewport);
            r.Add(("advanced: an offset from the expanded view clamps after collapsing",
                   afterCollapse <= KMHPatch.UI.KmhScroll.MaxOffset(collapsedH, viewport), $"{atBottom} -> {afterCollapse}"));
            r.Add(("advanced: content shorter than its viewport cannot scroll",
                   Approx(KMHPatch.UI.KmhScroll.MaxOffset(100f, viewport), 0f), ""));
            r.Add(("advanced: a negative offset clamps to the top",
                   Approx(KMHPatch.UI.KmhScroll.Clamp(-40f, expandedH, viewport), 0f), ""));

            // The body rect must never go negative however small the window gets - BeginScrollView throws on one.
            r.Add(("advanced: body height never goes negative on a tiny window",
                   KMHPatch.UI.DialogLayout.BodyHeight(new UnityEngine.Rect(0f, 0f, 200f, 30f), 200f) > 0f, ""));

            UnityEngine.Vector2 wide = KMHPatch.UI.DialogLayout.FitToScreen(1280f, 680f, 1000f, 700f);
            r.Add(("dialog: an oversized width is clamped to the screen", wide.x <= 1000f, $"{wide.x} on a 1000px screen"));
            r.Add(("dialog: an oversized height is clamped to the screen", wide.y <= 700f, $"{wide.y} on a 700px screen"));
            UnityEngine.Vector2 fits = KMHPatch.UI.DialogLayout.FitToScreen(600f, 500f, 1920f, 1080f);
            r.Add(("dialog: a size that already fits is left alone", Approx(fits.x, 600f) && Approx(fits.y, 500f), $"{fits}"));
            UnityEngine.Vector2 tiny = KMHPatch.UI.DialogLayout.FitToScreen(900f, 620f, 200f, 200f);
            r.Add(("dialog: never clamped below a usable minimum",
                   tiny.x >= KMHPatch.UI.DialogLayout.MinDialogWidth && tiny.y >= KMHPatch.UI.DialogLayout.MinDialogHeight, $"{tiny}"));

            // Widths are supplied rather than measured: Text.CalcSize needs a running game's font system.
            float[] eight = { 90f, 80f, 95f, 85f, 100f, 75f, 110f, 85f };   // ~740px of tabs
            float wideH   = KMHPatch.Features.Standings.StandingsTable.TabsHFrom(eight, 4000f);
            float narrowH = KMHPatch.Features.Standings.StandingsTable.TabsHFrom(eight, 300f);
            r.Add(("tabs: one line when they fit", Approx(wideH, 26f), $"{wideH}"));
            r.Add(("tabs: wrap to more lines when they do not fit", narrowH > wideH, $"{narrowH} vs {wideH}"));
            r.Add(("tabs: a single tab reports one line",
                   Approx(KMHPatch.Features.Standings.StandingsTable.TabsHFrom(new[] { 70f }, 200f), 26f), ""));
            r.Add(("tabs: no labels still reserves a row",
                   KMHPatch.Features.Standings.StandingsTable.TabsHFrom(null, 200f) > 0f, ""));
            // A tab wider than its container must not loop or report zero lines.
            r.Add(("tabs: an overwide tab still reports a usable height",
                   KMHPatch.Features.Standings.StandingsTable.TabsHFrom(new[] { 500f, 500f }, 200f) >= 26f, ""));

            // The server's rule for its boards: the game's record tables number rows the same way.
            long[] sorted = { 90, 90, 50, 10, 0, 0 };
            string placed = string.Join(" ", KMHPatch.Features.Standings.StandingsTable.Ranks(sorted, v => v));
            string lowest = string.Join(" ", KMHPatch.Features.Standings.StandingsTable.Ranks(new long[] { -7, -7, -2, 0, 4 }, v => -v));
            r.Add(("standings: equal values share a place, the next skips, and nothing ranks at zero",
                   placed == "1 1 3 4 0 0" && lowest == "1 1 3 0 0", placed + " | " + lowest));

            // The server names its website board; the game opens it only on the directory it already talks to.
            string Site(string page, string dir, string board) => KMHPatch.Features.Leaderboards.LeaderboardCache.WebsitePage(page, dir, board);
            const string dirUrl = "https://preview.knappe0.games", boardsUrl = dirUrl + "/servers/0a1b2c/leaderboards";
            bool siteOk = Site(boardsUrl, dirUrl, "trade") == boardsUrl + "/trade" && Site(boardsUrl, dirUrl, "") == boardsUrl
                       && Site("https://evil.example/servers/0a1b2c/leaderboards", dirUrl, "trade") == ""
                       && Site("https://x@preview.knappe0.games/servers/0a1b2c/leaderboards", dirUrl, "trade") == ""
                       && Site("http://preview.knappe0.games/servers/0a1b2c/leaderboards", dirUrl, "trade") == ""
                       && Site(boardsUrl + "?next=evil", dirUrl, "trade") == ""
                       && Site(dirUrl + "/servers/../leaderboards", dirUrl, "trade") == ""
                       && Site("", dirUrl, "trade") == ""
                       && Site("http://127.0.0.1:5099/servers/ab/leaderboards", "http://127.0.0.1:5099", "") == "http://127.0.0.1:5099/servers/ab/leaderboards";
            r.Add(("leaderboards: the website link opens only a board page on the player's own directory", siteOk, ""));

            string griefer = "<size=300>HUGE</size></color><b>";
            string inert   = KMHPatch.UI.KmhDisplayText.Inert(griefer);
            r.Add(("display text: tags are made inert", !inert.Contains("<") && !inert.Contains(">"), inert));
            r.Add(("display text: content survives readably",
                   inert.Contains("size=300") && inert.Contains("HUGE"), inert));
            r.Add(("display text: plain text is untouched",
                   KMHPatch.UI.KmhDisplayText.Inert("hello there") == "hello there", ""));
            r.Add(("display text: null/empty safe",
                   KMHPatch.UI.KmhDisplayText.Inert(null) == null && KMHPatch.UI.KmhDisplayText.Inert("") == "", ""));
            r.Add(("display text: cap truncates then neutralises",
                   KMHPatch.UI.KmhDisplayText.Inert("<b>abcdef", 4) == "‹b›a…", KMHPatch.UI.KmhDisplayText.Inert("<b>abcdef", 4)));

            foreach ((string name, object data) in new List<(string, object)>
            {
                ("null", null),
                ("empty", new { }),
                ("request shape", new { fingerprint = "abc", qty = 3 }),
                ("nested + specials", new { a = new { b = 1L, c = true }, list = new[] { 1, 2, 3 }, s = "q\"u\neé" }),
                ("dto attrs + null", new EnvSample { A = "x", B = 99, C = null, D = new List<int> { 7, 8 } }),
            })
            {
                string oldStr = EnvOld("kmh.x", data, 2);
                string newStr = new KmhEnvelope("kmh.x", data, 2).Serialize();
                r.Add(($"envelope byte-identical: {name}", oldStr == newStr, oldStr == newStr ? "" : $"NEW={newStr} OLD={oldStr}"));
            }
            string sent = new KmhEnvelope("kmh.note", new { level = "negative", n = 7 }, 2).Serialize();
            KmhEnvelope parsed = KmhEnvelope.TryParse(sent);
            r.Add(("envelope outbound round-trips", parsed != null && parsed.Kind == "kmh.note"
                   && parsed.GetString("level") == "negative" && parsed.GetInt("n") == 7, sent));

            r.AddRange(RoadReconcileChecks());
            KmhClientHardeningChecks.Run(r);
            // Last, so it covers every check above: a fixture frame must never reach a real server.
            int tried = SubProtocol.KmhDispatcher.SuppressedSendCount - sendsBefore;
            int called = Features.Global.GlobalHttp.SuppressedCallCount - callsBefore;
            r.Add(("self-test: nothing this suite runs reaches the wire",
                   SubProtocol.KmhDispatcher.SuppressSendForTest && Features.Global.GlobalHttp.SuppressForTest,
                   (tried == 0 ? "no send attempted" : $"{tried} send(s) attempted and suppressed")
                   + ", " + (called == 0 ? "no Global call attempted" : $"{called} Global call(s) suppressed")));

            return r;
        }

        private static List<(string, bool, string)> RoadReconcileChecks()
        {
            var r = new List<(string, bool, string)>();
            const string trail = "trail", highway = "highway";
            const string dirt = "DirtPath", stone = "StoneRoad";
            A Mine(string applied, string prev) => new A { AppliedDefName = applied, PreviousDefName = prev };

            V v = KmhRoadReconcile.Decide(trail, dirt, 10, null, null, 0);
            r.Add(("road: an empty pair gets our road and a record of it",
                   v.Action == Act.Add && v.Record?.AppliedDefName == dirt && v.Record.PreviousDefName == null, ""));

            // The player was charged for this segment, so it is laid even over a better road already there.
            v = KmhRoadReconcile.Decide(trail, dirt, 10, null, stone, 30);
            r.Add(("road: a segment the server recorded is laid even over a better road, remembering it",
                   v.Action == Act.Upgrade && v.Record?.AppliedDefName == dirt && v.Record.PreviousDefName == stone,
                   "vanilla's ancient roads outrank every KMH tier, so deferring made a paid-for route invisible"));

            // But not forever: something that keeps putting its own road back owns that tile, and a write war helps nobody.
            v = KmhRoadReconcile.Decide(trail, dirt, 10, null, stone, 30, KmhRoadReconcile.MaxRebuilds);
            r.Add(("road: a segment another producer keeps reclaiming is left to it",
                   v.Action == Act.Defer && v.Record == null,
                   $"after {KmhRoadReconcile.MaxRebuilds} rebuild(s) KMH stops trading writes"));

            // A contested key that is still bare ground has nothing to fight over, so the road is still laid.
            v = KmhRoadReconcile.Decide(trail, dirt, 10, null, null, 0, KmhRoadReconcile.MaxRebuilds);
            r.Add(("road: the rebuild budget only holds KMH off an occupied tile",
                   v.Action == Act.Add && v.Record?.AppliedDefName == dirt, ""));

            v = KmhRoadReconcile.Decide(highway, stone, 30, null, dirt, 10);
            r.Add(("road: upgrading over another road remembers what was there",
                   v.Action == Act.Upgrade && v.Record?.PreviousDefName == dirt, ""));

            v = KmhRoadReconcile.Decide(null, null, 0, Mine(stone, dirt), stone, 30);
            r.Add(("road: dropping an upgraded segment restores the road it replaced",
                   v.Action == Act.Restore && v.WriteDefName == dirt && v.Record == null, "a hole would be worse than the original"));

            v = KmhRoadReconcile.Decide(null, null, 0, Mine(dirt, null), dirt, 10);
            r.Add(("road: dropping a segment we added removes it",
                   v.Action == Act.Remove && v.Record == null, ""));

            v = KmhRoadReconcile.Decide(null, null, 0, null, stone, 30);
            r.Add(("road: a road we never touched is never removed",
                   v.Action == Act.None && v.Record == null, "the whole point of tracking what we wrote"));

            v = KmhRoadReconcile.Decide(null, null, 0, Mine(dirt, null), stone, 30);
            r.Add(("road: a claim is released when the world no longer matches what we wrote",
                   v.ReleasedStaleClaim && v.Action == Act.None && v.Record == null, "another producer owns it now"));

            v = KmhRoadReconcile.Decide(highway, stone, 30, Mine(dirt, null), stone, 30, KmhRoadReconcile.MaxRebuilds);
            r.Add(("road: a stale claim on a contested segment is released rather than fought over",
                   v.ReleasedStaleClaim && v.Action == Act.Defer && v.Record == null,
                   "trading writes with another mod would flicker the map"));

            // Something wiping KMH's road is not a rival producer: counting a wipe would spend the budget and give up.
            v = KmhRoadReconcile.Decide(trail, dirt, 10, Mine(dirt, null), null, 0);
            bool wipeIsFree = v.ReleasedStaleClaim && !v.Contested && v.Action == Act.Add;
            v = KmhRoadReconcile.Decide(trail, dirt, 10, Mine(dirt, null), stone, 30);
            r.Add(("road: a wiped road is re-laid for free, a road replaced by another counts against the budget",
                   wipeIsFree && v.ReleasedStaleClaim && v.Contested,
                   "a repeated wipe must never exhaust the anti-write-war budget"));

            // RWT regenerates the planet on join, so every claim restored from the save names a road that is now gone.
            v = KmhRoadReconcile.Decide(trail, dirt, 10, Mine(dirt, null), null, 0);
            r.Add(("road: releasing a stale claim still writes the road the server wants",
                   v.ReleasedStaleClaim && v.Action == Act.Add && v.WriteDefName == dirt
                   && v.Record?.AppliedDefName == dirt,
                   "releasing the claim and the road together left the map blank until an unrelated revision bump"));

            v = KmhRoadReconcile.Decide(highway, stone, 30, Mine(stone, null), dirt, 10);
            r.Add(("road: a stale claim over a worse road still upgrades, and remembers that road",
                   v.ReleasedStaleClaim && v.Action == Act.Upgrade && v.Record?.PreviousDefName == dirt, ""));

            v = KmhRoadReconcile.Decide(trail, dirt, 10, Mine(dirt, null), dirt, 10);
            r.Add(("road: a claim the world still agrees with is not released",
                   !v.ReleasedStaleClaim && v.Action == Act.None, "releasing every pass would never converge"));

            v = KmhRoadReconcile.Decide(highway, stone, 30, Mine(dirt, "DirtRoad"), dirt, 10);
            r.Add(("road: re-tiering our own road keeps what we originally displaced",
                   v.Action == Act.Upgrade && v.Record?.PreviousDefName == "DirtRoad", ""));

            v = KmhRoadReconcile.Decide(trail, dirt, 10, Mine(dirt, null), dirt, 10);
            r.Add(("road: a segment already correct is left alone but stays claimed",
                   v.Action == Act.None && v.Record?.AppliedDefName == dirt, ""));

            // No usable RoadDef in this game: wanting a road KMH cannot name must not delete an existing one.
            v = KmhRoadReconcile.Decide(trail, null, 0, null, stone, 30);
            r.Add(("road: a tier with no usable def touches nothing",
                   v.Action == Act.None && v.Record == null, "a missing def is not a reason to clear the world"));

            // Keys must be order-independent and layer-aware, matching the server byte for byte.
            r.Add(("road: a segment key is order-independent",
                   RoadKeys.For(0, 10, 0, 99) == RoadKeys.For(0, 99, 0, 10), RoadKeys.For(0, 10, 0, 99)));
            r.Add(("road: layer is part of tile identity",
                   RoadKeys.For(0, 10, 0, 11) != RoadKeys.For(1, 10, 1, 11), "a bare tile id would conflate layers"));

            r.AddRange(RoadQuoteChecks());
            r.AddRange(OutpostLabelChecks());
            return r;
        }

        private static List<(string, bool, string)> OutpostLabelChecks()
        {
            var r = new List<(string, bool, string)>();
            var neutral = new KMHPatch.Features.Sites.Dto.SiteEntry
            {
                OwnerKind = KMHPatch.Features.Sites.Dto.SiteEntry.OwnerNeutral,
                OutpostTemplate = KMHPatch.Features.Sites.Dto.SiteEntry.TemplateRuins,
                OutpostState = KMHPatch.Features.Sites.Dto.SiteEntry.OutpostClaimable,
                SiteName = "Blackridge Ruins",
            };
            r.Add(("outpost: an outpost's name is not its controller",
                   KMHPatch.Features.Sites.SiteLabels.Name(neutral) == "Blackridge Ruins"
                   && KMHPatch.Features.Sites.SiteLabels.Controller(neutral) == "Unclaimed",
                   "the place name once stood in as the controller, so a ruin was held by itself"));
            r.Add(("outpost: a nameless place still has something to call it",
                   KMHPatch.Features.Sites.SiteLabels.Name(
                       new KMHPatch.Features.Sites.Dto.SiteEntry { Tile = 77 }) == "Tile 77", ""));
            r.Add(("outpost: a neutral outpost with no name never renders blank",
                   KMHPatch.Features.Sites.SiteLabels.Controller(
                       new KMHPatch.Features.Sites.Dto.SiteEntry
                       { OwnerKind = KMHPatch.Features.Sites.Dto.SiteEntry.OwnerNeutral }) == "Unclaimed",
                   "formatting an empty OwnerUsername is what would show nothing at all"));
            r.Add(("outpost: a hostile outpost falls back to a controller word",
                   KMHPatch.Features.Sites.SiteLabels.Controller(
                       new KMHPatch.Features.Sites.Dto.SiteEntry
                       { OwnerKind = KMHPatch.Features.Sites.Dto.SiteEntry.OwnerSystem }) == "Hostile", ""));
            r.Add(("outpost: a guild site shows the controlling guild",
                   KMHPatch.Features.Sites.SiteLabels.Controller(
                       new KMHPatch.Features.Sites.Dto.SiteEntry
                       { OwnerKind = KMHPatch.Features.Sites.Dto.SiteEntry.OwnerGuildKind, ControllingGuild = "Wardens" }) == "Wardens", ""));

            r.Add(("outpost: an ordinary site is not an outpost",
                   !KMHPatch.Features.Sites.SiteLabels.IsOutpost(new KMHPatch.Features.Sites.Dto.SiteEntry())
                   && KMHPatch.Features.Sites.SiteLabels.IsOutpost(neutral), ""));
            // Explicitly "": an older server omits the field entirely, and both cases must read as player-controlled.
            r.Add(("outpost: a system outpost is not player-controlled",
                   !KMHPatch.Features.Sites.SiteOwnershipClient.IsPlayerControlled(neutral)
                   && KMHPatch.Features.Sites.SiteOwnershipClient.IsPlayerControlled(
                          new KMHPatch.Features.Sites.Dto.SiteEntry { OwnerKind = "" }),
                   "an absent or empty kind must still read as player-controlled"));

            r.Add(("outpost: no claim window reports no countdown",
                   KMHPatch.Features.Sites.SiteLabels.ClaimMinutesLeft(new KMHPatch.Features.Sites.Dto.SiteEntry()) == -1, ""));
            r.Add(("outpost: a closed window reports zero, not a negative countdown",
                   KMHPatch.Features.Sites.SiteLabels.ClaimMinutesLeft(
                       new KMHPatch.Features.Sites.Dto.SiteEntry { ClaimWindowEndsUtcTicks = 1 }) == 0, ""));
            return r;
        }

        private static List<(string, bool, string)> RoadQuoteChecks()
        {
            var r = new List<(string, bool, string)>();
            var snap = new RoadworksSnapshot
            {
                AllowRoadworks = true,
                MaxRouteSegments = 4,
                SilverPerSegment = new Dictionary<string, int> { { "trail", 25 }, { "road", 75 }, { "highway", 200 } },
                Segments = new List<RoadSegmentDto>
                {
                    new RoadSegmentDto { LayerA = 0, TileA = 2, LayerB = 0, TileB = 3, Tier = "trail" },
                },
            };
            var route = new List<(int, int)> { (0, 1), (0, 2), (0, 3), (0, 4) };
            List<string> keys = KmhRoadQuote.RouteKeys(route);
            r.Add(("quote: a route of four tiles is three segments", keys.Count == 3, $"{keys.Count}"));
            r.Add(("quote: an already-built portion is not charged again",
                   KmhRoadQuote.SilverFor(snap, "trail", keys) == 50, "2 new segments at 25, not 3"));
            r.Add(("quote: tier changes the price, not the segment count",
                   KmhRoadQuote.SilverFor(snap, "highway", keys) == 400, ""));
            r.Add(("quote: a tier the server did not price quotes nothing",
                   KmhRoadQuote.SilverFor(snap, "__kmh_not_a_tier__", keys) == 25 * 2,
                   "unknown normalizes to trail rather than inventing a price"));
            r.Add(("quote: a route doubling back counts a segment once",
                   KmhRoadQuote.RouteKeys(new List<(int, int)> { (0, 7), (0, 8), (0, 7) }).Count == 2
                   && RoadKeys.NewSegmentKeys(KmhRoadQuote.RouteKeys(
                          new List<(int, int)> { (0, 7), (0, 8), (0, 7) }), null).Count == 1, ""));

            // Refusals must match the server's, or a player pays for a route that is then rejected.
            r.Add(("quote: a fully built route is refused before paying",
                   KmhRoadQuote.RefusalFor(snap, "trail", KmhRoadQuote.RouteKeys(
                       new List<(int, int)> { (0, 2), (0, 3) })) == "That route is already built.", ""));
            r.Add(("quote: a route past the server limit is refused before paying",
                   (KmhRoadQuote.RefusalFor(snap, "trail", KmhRoadQuote.RouteKeys(
                       new List<(int, int)> { (0, 1), (0, 2), (0, 3), (0, 4), (0, 5), (0, 6) })) ?? "").Contains("too long"), ""));
            r.Add(("quote: nothing is offered before the server has answered",
                   KmhRoadQuote.RefusalFor(null, "trail", keys) != null, "an empty snapshot is not an empty network"));
            snap.AllowRoadworks = false;
            r.Add(("quote: Roadworks turned off refuses the route",
                   (KmhRoadQuote.RefusalFor(snap, "trail", keys) ?? "").Contains("turned off"), ""));
            snap.AllowRoadworks = true;
            r.Add(("quote: a valid route has no refusal",
                   KmhRoadQuote.RefusalFor(snap, "trail", keys) == null, ""));

            AddSdkCompatChecks(r);
            AddUnreadChecks(r);
            AddWealthLedgerChecks(r);
            AddMarketplaceLayoutChecks(r);
            AddSharedControlChecks(r);
            AddToolbarLayoutChecks(r);
            AddBulkCaptureChecks(r);
            AddTransportChecks(r);
            return r;
        }

        // A request's snapshot and a mutation's newer one can cross in flight; a new server's first must still apply.
        private static void AddRevisionChecks(List<(string, bool, string)> r)
        {
            try
            {
                Features.Marketplace.MarketplaceCache.Clear();
                Features.Marketplace.MarketplaceCache.Apply(new Features.Marketplace.Dto.MarketplaceSnapshot { Revision = 20, HouseSilverPool = 200 });
                Features.Marketplace.MarketplaceCache.Apply(new Features.Marketplace.Dto.MarketplaceSnapshot { Revision = 19, HouseSilverPool = 100 });
                long held = Features.Marketplace.MarketplaceCache.Snapshot?.HouseSilverPool ?? -1;
                r.Add(("snapshots: a late revision 19 does not undo revision 20", held == 200, $"pool {held}, expected 200"));

                Features.Marketplace.MarketplaceCache.Apply(new Features.Marketplace.Dto.MarketplaceSnapshot { Revision = 21, HouseSilverPool = 300 });
                held = Features.Marketplace.MarketplaceCache.Snapshot?.HouseSilverPool ?? -1;
                r.Add(("snapshots: a newer revision still applies", held == 300, $"pool {held}, expected 300"));

                // A different server starts its counter over, so the first snapshot after a disconnect must land.
                Features.Marketplace.MarketplaceCache.Clear();
                Features.Marketplace.MarketplaceCache.Apply(new Features.Marketplace.Dto.MarketplaceSnapshot { Revision = 1, HouseSilverPool = 7 });
                held = Features.Marketplace.MarketplaceCache.Snapshot?.HouseSilverPool ?? -1;
                r.Add(("snapshots: a new server's lower revision still applies after a disconnect", held == 7, $"pool {held}, expected 7"));
                Features.Marketplace.MarketplaceCache.Clear();
            }
            catch (Exception ex) { r.Add(("snapshots: revision guard", false, $"threw: {ex.Message}")); }

            try
            {
                // The two vaults share one message kind, so each must keep its own applied revision.
                Features.Treasury.TreasuryCache.Clear();
                Features.Treasury.TreasuryCache.Apply(new Features.Treasury.Dto.TreasurySnapshot { Revision = 30, IsGuildOwned = false, SilverBalance = 500 });
                Features.Treasury.TreasuryCache.Apply(new Features.Treasury.Dto.TreasurySnapshot { Revision = 31, IsGuildOwned = true,  SilverBalance = 900 });
                Features.Treasury.TreasuryCache.Apply(new Features.Treasury.Dto.TreasurySnapshot { Revision = 32, IsGuildOwned = false, SilverBalance = 400 });
                int personal = Features.Treasury.TreasuryCache.Personal?.SilverBalance ?? -1;
                int guild    = Features.Treasury.TreasuryCache.Guild?.SilverBalance ?? -1;
                r.Add(("snapshots: a guild vault does not block the personal one",
                       personal == 400 && guild == 900, $"personal {personal} (expected 400), guild {guild} (expected 900)"));

                Features.Treasury.TreasuryCache.Apply(new Features.Treasury.Dto.TreasurySnapshot { Revision = 31, IsGuildOwned = false, SilverBalance = 500 });
                personal = Features.Treasury.TreasuryCache.Personal?.SilverBalance ?? -1;
                r.Add(("snapshots: a stale personal balance does not come back", personal == 400, $"personal {personal}, expected 400"));
                Features.Treasury.TreasuryCache.Clear();
            }
            catch (Exception ex) { r.Add(("snapshots: treasury revision guard", false, $"threw: {ex.Message}")); }

            try
            {
                // The server takes only this form; anything else falls back to the player-wide generation.
                string a = new Features.Treasury.GameComponent_KMHDepositLedger(null).LedgerId;
                string b = new Features.Treasury.GameComponent_KMHDepositLedger(null).LedgerId;
                bool hex = a.Length == 32 && System.Linq.Enumerable.All(a, c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
                r.Add(("treasury: every new colony gets its own 32-hex deposit ledger id", hex && a != b, $"{a} / {b}"));
            }
            catch (Exception ex) { r.Add(("treasury: deposit ledger id", false, $"threw: {ex.Message}")); }

            try
            {
                // A deposit made just before a drop stays pending if the reconnect reconciles inside the server's grace.
                var led = new Features.Treasury.GameComponent_KMHDepositLedger(null);
                led.RecordDeposit("mine");
                var t0 = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
                List<Features.Treasury.Dto.PendingDeposit> Pend(params string[] ids)
                {
                    var list = new List<Features.Treasury.Dto.PendingDeposit>();
                    foreach (string i in ids) list.Add(new Features.Treasury.Dto.PendingDeposit { TxnId = i, Kind = Features.Treasury.Dto.PendingDeposit.KindItem });
                    return list;
                }
                led.SelfHeal("test", Pend("mine"), t0);
                int known = led.ReconcilesForTest;
                led.SelfHeal("test", Pend("mine", "lost"), t0);
                int first = led.ReconcilesForTest;
                led.SelfHeal("test", Pend("mine", "lost"), t0.AddSeconds(100));
                int early = led.ReconcilesForTest;
                led.SelfHeal("test", Pend("mine", "lost"), t0.AddSeconds(151));
                int again = led.ReconcilesForTest;
                bool ok = known == 0 && first == 1 && early == 1 && again == 2;
                r.Add(("treasury: a deposit pending on the server that this game never saved or made is reconciled again once the server's grace has passed",
                       ok, $"known only: {known}; unknown: {first}, +100 s: {early}, +151 s: {again}"));
            }
            catch (Exception ex) { r.Add(("treasury: deposit reconcile again", false, $"threw: {ex.Message}")); }

            try
            {
                // An unset icon (int.MinValue) makes RWT throw for every other player's settlement, so none appear.
                r.Add(("settlement icons: an unset or out-of-range icon falls back to RWT's first, a valid one is kept",
                       Patches.Patch_RwtSettlementIcon.SafeIconIndex(int.MinValue, 5) == 0 && Patches.Patch_RwtSettlementIcon.SafeIconIndex(-1, 5) == 0
                       && Patches.Patch_RwtSettlementIcon.SafeIconIndex(5, 5) == 0 && Patches.Patch_RwtSettlementIcon.SafeIconIndex(3, 5) == 3, ""));
                Type pm = RwtCompat.ResolveType(RwtCompat.ClientNsRoot, "PM_Settlements", RwtCompat.ClientNsRoot + ".PacketManagers.PM_Settlements");
                System.Reflection.MethodInfo spawn = pm == null ? null : HarmonyLib.AccessTools.Method(pm, "SpawnSingleSettlement");
                bool icons = spawn != null && spawn.GetParameters().Length == 1
                          && HarmonyLib.AccessTools.Property(spawn.GetParameters()[0].ParameterType, "IconID") != null;
                bool armed = false;
                HarmonyLib.Patches info = icons ? HarmonyLib.Harmony.GetPatchInfo(spawn) : null;
                if (info != null)
                    foreach (HarmonyLib.Patch p in info.Prefixes) if (p.owner == Constants.HarmonyId) armed = true;
                r.Add(("settlement icons: the fallback is armed wherever RWT draws settlement icons", !icons || armed,
                       icons ? (armed ? "armed" : "RWT draws icons but KMH's fallback is not on SpawnSingleSettlement") : "this RWT draws no icons"));
            }
            catch (Exception ex) { r.Add(("settlement icons: fallback", false, $"threw: {ex.Message}")); }
        }

        private static void AddOpIdChecks(List<(string, bool, string)> r)
        {
            try
            {
                KmhOpId.Clear();
                DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                string first = KmhOpId.For("mkt.buy|7|5", t0);
                string retry = KmhOpId.For("mkt.buy|7|5", t0.AddSeconds(3));
                r.Add(("op ids: retrying the same action reuses its id",
                       first == retry && !string.IsNullOrEmpty(first), first == retry ? "same id" : $"{first} then {retry}"));

                string other = KmhOpId.For("mkt.buy|7|9", t0.AddSeconds(3));
                r.Add(("op ids: a different quantity is a different action", other != first, other == first ? "IDS COLLIDED" : "distinct ids"));

                var steel = new Dictionary<string, int> { ["Steel"] = 5 };
                var plasteel = new Dictionary<string, int> { ["Plasteel"] = 5 };
                var twoA = new Dictionary<string, int> { ["Steel"] = 5, ["Gold"] = 1 };
                var twoB = new Dictionary<string, int> { ["Gold"] = 1, ["Steel"] = 5 };
                string mailSteel = Features.Mail.MailHandler.SendKey("Bob", "hi", "", 0, steel, null);
                string mailPlasteel = Features.Mail.MailHandler.SendKey("Bob", "hi", "", 0, plasteel, null);
                string mailGear = Features.Mail.MailHandler.SendKey("Bob", "hi", "", 0, null, steel);
                bool goodsCount = mailSteel != mailPlasteel && mailSteel != mailGear
                               && Features.Mail.MailHandler.SendKey("Bob", "hi", "", 0, twoA, null)
                                  == Features.Mail.MailHandler.SendKey("Bob", "hi", "", 0, twoB, null)
                               && mailSteel.StartsWith("mail.send|", StringComparison.Ordinal);
                r.Add(("op ids: two mails differing only in their goods are two actions", goodsCount,
                       goodsCount ? "goods are part of the key, in a fixed order" : $"{mailSteel} / {mailPlasteel} / {mailGear}"));

                string later = KmhOpId.For("mkt.buy|7|5", t0 + KmhOpId.Hold + TimeSpan.FromSeconds(1));
                r.Add(("op ids: a deliberate repeat past the hold is a new action",
                       later != first, later == first ? "the repeat was swallowed" : "new id"));

                r.Add(("op ids: an action is only in flight inside the hold",
                       KmhOpId.IsInFlight("mkt.buy|7|5", t0 + KmhOpId.Hold + TimeSpan.FromSeconds(2))
                       && !KmhOpId.IsInFlight("mkt.buy|7|5", t0 + KmhOpId.Hold + KmhOpId.Hold + TimeSpan.FromSeconds(3)),
                       "the hold is what makes a re-click a retry"));

                KmhOpId.Settled("mkt.buy|7|5");
                string afterSettle = KmhOpId.For("mkt.buy|7|5", t0 + KmhOpId.Hold + TimeSpan.FromSeconds(2));
                r.Add(("op ids: a settled action mints a new id", afterSettle != later, afterSettle == later ? "STILL HELD" : "new id"));

                // The terminal result the server sends back, which is what separates a repeat from a retry.
                KmhOpId.Clear();
                string first2 = KmhOpId.For("mkt.buy|10|5", t0);
                string retry2 = KmhOpId.For("mkt.buy|10|5", t0.AddSeconds(1));
                KmhOpId.SettledById(first2);
                string repeat2 = KmhOpId.For("mkt.buy|10|5", t0.AddSeconds(3));
                r.Add(("op ids: an intentional repeat after the server's result is a NEW action",
                       first2 == retry2 && repeat2 != first2,
                       first2 == retry2 && repeat2 != first2
                           ? "retry reused, repeat minted fresh"
                           : $"retry-reused={first2 == retry2}, repeat-is-new={repeat2 != first2} - A REAL SECOND ACTION WOULD BE REFUSED"));

                string held2 = KmhOpId.For("mkt.buy|11|1", t0.AddSeconds(4));
                KmhOpId.SettledById("never-issued");
                KmhOpId.SettledById(first2);
                r.Add(("op ids: settling an unknown id leaves an outstanding one alone",
                       KmhOpId.For("mkt.buy|11|1", t0.AddSeconds(5)) == held2, "still outstanding"));

                // Ids belong to the connection that made them; the next server has never seen them.
                KmhOpId.Clear();
                string afterClear = KmhOpId.For("mkt.buy|7|5", t0.AddSeconds(4));
                r.Add(("op ids: a disconnect drops every held id", afterClear != afterSettle && KmhOpId.OpenCount == 1,
                       $"{KmhOpId.OpenCount} held"));
                KmhOpId.Clear();

                // The op id rides the envelope, not the payload, so the transport can see it without parsing data.
                string wire = new KmhEnvelope(KmhProtocol.Kind.MarketplaceBuy, new { listing_id = 7, qty = 5 }, opId: "abc123").Serialize();
                KmhEnvelope back = KmhEnvelope.TryParse(wire);
                string bare = new KmhEnvelope(KmhProtocol.Kind.MarketplaceRequest, null).Serialize();
                r.Add(("op ids: an op id survives the wire and is absent when unused",
                       wire.Contains("\"op\":\"abc123\"") && back?.OpId == "abc123" && !bare.Contains("\"op\""),
                       back?.OpId ?? "did not round-trip"));
            }
            catch (Exception ex) { r.Add(("op ids", false, $"threw: {ex.Message}")); }
        }

        // Both decide value: a retried mutation can submit twice, and a tunnelled one ignores the owner's policy.
        private static void AddTransportChecks(List<(string, bool, string)> r)
        {
            r.Add(("transport: without an op id, only a send that never left the client may be retried",
                   KmhDispatcher.MayRetryOnChat(KmhSendResult.NotConnected)
                   && !KmhDispatcher.MayRetryOnChat(KmhSendResult.AmbiguousIoFailure)
                   && !KmhDispatcher.MayRetryOnChat(KmhSendResult.TooLarge)
                   && !KmhDispatcher.MayRetryOnChat(KmhSendResult.SerializationFailure)
                   && !KmhDispatcher.MayRetryOnChat(KmhSendResult.Sent),
                   "an ambiguous write may already have reached the server"));

            r.Add(("transport: an op id makes an ambiguous write safe to repeat, nothing else",
                   KmhDispatcher.MayRetryOnChat(KmhSendResult.AmbiguousIoFailure, idempotent: true)
                   && !KmhDispatcher.MayRetryOnChat(KmhSendResult.AmbiguousIoFailure, idempotent: false)
                   && !KmhDispatcher.MayRetryOnChat(KmhSendResult.TooLarge, idempotent: true)
                   && !KmhDispatcher.MayRetryOnChat(KmhSendResult.SerializationFailure, idempotent: true),
                   "the server refuses the second copy of an op id"));

            // A server that runs on chat and one whose KMH port is not answering read identically without this.
            var degradedWas = SubProtocol.KmhTransport.DegradedReason;
            var statusWas   = SubProtocol.KmhTransport.Status;
            bool degradedTells;
            try
            {
                SubProtocol.KmhTransport.Status = SubProtocol.KmhTransportStatus.ChatFallback;
                SubProtocol.KmhTransport.DegradedReason = null;
                string plain = SubProtocol.KmhTransport.StatusLabel;
                SubProtocol.KmhTransport.DegradedReason = "host:5099 - timed out";
                string degraded = SubProtocol.KmhTransport.StatusLabel;
                degradedTells = plain == "RWT chat fallback" && degraded != plain
                                && degraded.IndexOf("unreachable", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            finally
            {
                SubProtocol.KmhTransport.Status = statusWas;
                SubProtocol.KmhTransport.DegradedReason = degradedWas;
            }
            r.Add(("transport: an unreachable KMH port does not read as an ordinary chat server", degradedTells, ""));

            r.Add(("transport: the handshake is control traffic, features are not",
                   KmhDispatcher.IsTransportControlKind(KmhProtocol.Kind.Hello)
                   && KmhDispatcher.IsTransportControlKind(KmhProtocol.Kind.HelloAck)
                   && KmhDispatcher.IsTransportControlKind(KmhProtocol.Kind.Ping)
                   && KmhDispatcher.IsTransportControlKind(KmhProtocol.Kind.Pong)
                   && !KmhDispatcher.IsTransportControlKind(KmhProtocol.Kind.TreasuryWithdrawSilver), ""));

            // The server drops a chat envelope past this, so splitting anywhere else fails on arrival.
            r.Add(("transport: the chat ceiling matches the one the server enforces",
                   KmhDispatcher.MaxChatEnvelopeBytes == 64 * 1024, $"{KmhDispatcher.MaxChatEnvelopeBytes}"));

            // A large request must not be limited to whichever transport happens to be up.
            string big = new KmhEnvelope(KmhProtocol.Kind.TreasuryWithdrawSilver, new { pad = new string('x', 90_000) }).Serialize();
            List<KmhEnvelope> parts = KmhFragments.Split(KmhProtocol.Kind.TreasuryWithdrawSilver, big, KmhDispatcher.MaxChatEnvelopeBytes);
            bool allFit = parts != null && parts.Count > 1;
            if (allFit)
                foreach (KmhEnvelope p in parts)
                    if (p.Serialize().Length > KmhDispatcher.MaxChatEnvelopeBytes) { allFit = false; break; }
            r.Add(("transport: an oversized request splits to fit the chat ceiling",
                   allFit, parts == null ? "did not split" : $"{parts.Count} fragment(s)"));

            // Both transports capture the session generation at enqueue and re-check it at the pump.
            int genBefore = KmhDispatcher.SessionGeneration;
            int ran = 0;
            KmhMainThread.PostForTest(genBefore, () => ran++);
            KmhMainThread.PumpForTest();
            int afterSame = ran;
            KmhMainThread.PostForTest(genBefore - 1, () => ran++);
            KmhMainThread.PumpForTest();
            r.Add(("transport: a queued packet from an ended session is dropped at the pump",
                   afterSame == 1 && ran == 1, $"same-session ran {afterSame}, stale added {ran - afterSame}"));

            AddRevisionChecks(r);
            AddOpIdChecks(r);
            AddDeliveryReceiptChecks(r);
            AddBrowserSortChecks(r);
        }

        private static void AddBrowserSortChecks(List<(string, bool, string)> r)
        {
            try
            {
                Features.Global.SortKey Key(string id, bool official, int rank, int players, int peak, float avg,
                                            long beat = 0, string name = null)
                    => new Features.Global.SortKey
                    {
                        Official = official, Rank = rank, Players = players, PlayersPeak = peak,
                        PlayersAverage = avg, LastHeartbeat = beat, Name = name ?? id, ServerId = id,
                    };

                // An empty official server outranks a full community one in every mode, or a re-sort voids the designation.
                var official = Key("a", true, 4, 0, 0, 0f, 1, "Official");
                var busy     = Key("b", false, 0, 99, 99, 99f, 999, "Busy");
                bool pinned = true;
                foreach (Features.Global.BrowserSort s in new[]
                {
                    Features.Global.BrowserSort.Recommended, Features.Global.BrowserSort.Players,
                    Features.Global.BrowserSort.Active, Features.Global.BrowserSort.Name,
                    Features.Global.BrowserSort.Recent,
                })
                    if (Features.Global.BrowserSorting.Compare(official, busy, s) >= 0) pinned = false;
                r.Add(("browser: an official server sorts above every other server in every mode", pinned, ""));

                // An official server on another RimWorld version must not sit above the only server this game can join.
                var officialElsewhere = Key("e", true, Features.Global.BrowserSorting.CannotJoinRank, 15, 15, 15f, 1, "Official 1.6");
                var joinable          = Key("f", false, 0, 1, 1, 1f, 1, "Test 1.7");
                r.Add(("browser: Recommended puts a server this game can join above an official one it cannot",
                       Features.Global.BrowserSorting.Compare(joinable, officialElsewhere, Features.Global.BrowserSort.Recommended) < 0
                       && Features.Global.BrowserSorting.Compare(officialElsewhere, joinable, Features.Global.BrowserSort.Name) < 0, ""));

                // The whole point of the activity sort: quiet right now, busy earlier, still ranks.
                var quietButBusy = Key("c", false, 0, 0, 40, 12f);
                var steadyFew    = Key("d", false, 0, 3, 3, 3f);
                bool active = Features.Global.BrowserSorting.Compare(quietButBusy, steadyFew,
                                  Features.Global.BrowserSort.Active) < 0
                           && Features.Global.BrowserSorting.Compare(quietButBusy, steadyFew,
                                  Features.Global.BrowserSort.Players) > 0;
                r.Add(("browser: 'most active' ranks on the day's peak while 'most players' ranks on right now",
                       active, ""));

                // Two servers tied on the sort value must not trade places between frames.
                var tieA = Key("id-a", false, 0, 5, 5, 5f, 0, "Same");
                var tieB = Key("id-b", false, 0, 5, 5, 5f, 0, "Same");
                bool stable = Features.Global.BrowserSorting.Compare(tieA, tieB, Features.Global.BrowserSort.Players) < 0
                           && Features.Global.BrowserSorting.Compare(tieB, tieA, Features.Global.BrowserSort.Players) > 0
                           && Features.Global.BrowserSorting.Compare(tieA, tieA, Features.Global.BrowserSort.Players) == 0;
                r.Add(("browser: tied servers keep a stable, total order", stable, ""));

                var tags = new List<string> { "vanilla", "hardcore" };
                bool search = Features.Global.BrowserSorting.Matches("VANILLA", "Some Server", tags, "NA")
                           && Features.Global.BrowserSorting.Matches("na", "Some Server", tags, "NA")
                           && Features.Global.BrowserSorting.Matches("some", "Some Server", tags, "NA")
                           && !Features.Global.BrowserSorting.Matches("zzz", "Some Server", tags, "NA")
                           && Features.Global.BrowserSorting.Matches("", "Some Server", tags, "NA");
                r.Add(("browser: search matches a server's name, its tags and its region", search, ""));

                // A protocol mismatch proves a serving status falls through: a joinable card needs the running RWT.
                var statusExpectations = new List<(string Status, Features.Global.CompatState Expected)>
                {
                    ("Maintenance", Features.Global.CompatState.Maintenance),
                    ("Restarting",  Features.Global.CompatState.Starting),
                    ("Stale",       Features.Global.CompatState.Starting),
                    ("Offline",     Features.Global.CompatState.Offline),
                    ("Online",      Features.Global.CompatState.ProtocolMismatch),
                    ("Degraded",    Features.Global.CompatState.ProtocolMismatch),
                };
                bool statuses = true;
                string statusDetail = "";
                foreach ((string status, Features.Global.CompatState expected) in statusExpectations)
                {
                    var card = new Features.Global.ServerCardDto
                    {
                        Status = status, JoinEndpoint = "host:25555", MaxPlayers = 8,
                        Protocol = SubProtocol.KmhProtocol.CurrentVersion + 1, ModPolicy = "None",
                    };
                    Features.Global.CompatState got = Features.Global.KmhCompatibility.Evaluate(card);
                    if (got != expected) { statuses = false; statusDetail += $"{status}->{got} "; }
                }
                r.Add(("browser: every status the server can report decides Join correctly", statuses, statusDetail));

                // Listings are other owners' text and Widgets.Label parses markup: an unclosed <size> restyles the browser.
                string hostile = "<size=400><color=#ff0000>PWN";
                var poisoned = new Newtonsoft.Json.Linq.JObject
                {
                    ["server_id"] = new string('a', 32),
                    ["name"] = hostile,
                    ["region"] = hostile,
                    ["primary_language"] = hostile,
                    ["tags"] = new Newtonsoft.Json.Linq.JArray { hostile, "vanilla" },
                    ["description"] = hostile,
                    ["discord_url"] = hostile,
                    ["website_url"] = hostile,
                };
                var wrapper = new Newtonsoft.Json.Linq.JObject
                {
                    ["servers"] = new Newtonsoft.Json.Linq.JArray { poisoned },
                };
                List<Features.Global.ServerCardDto> parsed = Features.Global.DirectoryParse.Cards(wrapper, out _);
                bool inert = parsed.Count == 1
                          && parsed[0].Name.IndexOf('<') < 0 && parsed[0].Name.IndexOf('>') < 0
                          && parsed[0].Region.IndexOf('<') < 0
                          && parsed[0].PrimaryLanguage.IndexOf('<') < 0
                          && parsed[0].Description.IndexOf('<') < 0 && parsed[0].Description.IndexOf('>') < 0
                          && parsed[0].DiscordUrl.IndexOf('<') < 0
                          && parsed[0].WebsiteUrl.IndexOf('<') < 0
                          && parsed[0].Tags.TrueForAll(t => t.IndexOf('<') < 0 && t.IndexOf('>') < 0);
                r.Add(("browser: markup in a listing is made inert before anything draws it", inert,
                       parsed.Count == 1 ? parsed[0].Name : $"{parsed.Count} card(s)"));

                ListingPresentation(r);

                // "tag:" has to narrow to tags alone, or a word that is also in server names is useless as a filter.
                bool tagSearch = Features.Global.BrowserSorting.Matches("tag:hardcore", "Some Server", tags, "NA")
                              && !Features.Global.BrowserSorting.Matches("tag:some", "Some Server", tags, "NA")
                              && !Features.Global.BrowserSorting.Matches("tag:na", "Some Server", tags, "NA")
                              && Features.Global.BrowserSorting.Matches("tag:", "Some Server", tags, "NA")
                              && !Features.Global.BrowserSorting.Matches("tag:pvp", "Some Server", null, "NA");
                r.Add(("browser: 'tag:' searches tags only, never the name or region", tagSearch, ""));

                bool labels = Features.Global.BrowserTags.Label("pve") == "PvE"
                           && Features.Global.BrowserTags.Label("beginner-friendly") == "Beginner friendly"
                           && Features.Global.BrowserTags.Label("SomeOwnerTag") == "Someownertag"
                           && Features.Global.BrowserTags.IsKnown("PvP")
                           && !Features.Global.BrowserTags.IsKnown("whatever");
                r.Add(("browser: tag labels are stable and case-insensitive", labels, ""));

                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var cards = new List<Features.Global.ServerCardDto>
                {
                    new Features.Global.ServerCardDto { Tags = new List<string> { "zebra", "PvP" } },
                    new Features.Global.ServerCardDto { Tags = new List<string> { "pvp", "vanilla" } },
                };
                List<string> inUse = Features.Global.BrowserTags.InUse(cards, counts);
                bool vocab = inUse.Count == 3 && inUse[0] == "vanilla" && inUse[1] == "pvp" && inUse[2] == "zebra"
                          && counts["pvp"] == 2 && counts["vanilla"] == 1
                          && !counts.ContainsKey("hardcore");
                r.Add(("browser: known tags lead, invented tags follow, unused tags are absent", vocab,
                       string.Join(",", inUse.ToArray())));

                // A directory that predates the activity fields sends neither; the sort must still be a total order.
                var older = Key("e", false, 0, 0, 0, 0f);
                r.Add(("browser: a directory with no activity figures still sorts",
                       Features.Global.BrowserSorting.Compare(older, older, Features.Global.BrowserSort.Active) == 0, ""));
            }
            catch (Exception ex) { r.Add(("browser: the ordering rules run outside the game", false, ex.Message)); }
        }

        private static void AddDeliveryReceiptChecks(List<(string, bool, string)> r)
        {
            try
            {
                // The grant handler asks exactly these two questions before it materialises anything.
                var receipts = new Features.Delivery.GameComponent_KMHDeliveryReceipts(null);
                bool unseenFirst = !receipts.AlreadyHeld("srv-a:1");
                receipts.Record("srv-a:1");
                bool heldAfter = receipts.AlreadyHeld("srv-a:1");
                r.Add(("deliveries: a replayed grant is recognised within the session",
                       unseenFirst && heldAfter,
                       unseenFirst && heldAfter ? "recorded on first receipt" : $"unseen={unseenFirst}, held={heldAfter}"));

                bool otherServer = !receipts.AlreadyHeld("srv-b:1");
                r.Add(("deliveries: another server's identical sequence is a different delivery",
                       otherServer, otherServer ? "namespaced" : "A RECEIPT SUPPRESSED ANOTHER SERVER'S DELIVERY"));

                bool blankIgnored = !receipts.AlreadyHeld("") && !receipts.AlreadyHeld(null);
                r.Add(("deliveries: a grant with no id is never treated as already held", blankIgnored, ""));

                // A replay lands mid-load with no component to ask: both static forms must answer, or the id is lost.
                // Only without a game: with one, this would write a made-up receipt into the player's save.
                const string noGame = "deliveries: recording without a loaded game is survivable, and claims nothing";
                if (Verse.Current.Game != null) r.Add(KmhSelfTestResult.Skipped(noGame, "a game is loaded; run from the main menu"));
                else
                {
                    bool nullSafe = true;
                    try
                    {
                        Features.Delivery.GameComponent_KMHDeliveryReceipts.RecordDelivered("srv-a:2");
                        nullSafe = !Features.Delivery.GameComponent_KMHDeliveryReceipts.HeldAlready("srv-a:2");
                    }
                    catch { nullSafe = false; }
                    r.Add((noGame, nullSafe, nullSafe ? "" : "A MISSING GAME MUST NOT THROW NOR REPORT A DELIVERY AS HELD"));
                }

                // The held copy carries the id, or a grant that lands late is never acknowledged and replays for ever.
                bool holdCarriesId = false;
                foreach (System.Reflection.ParameterInfo pi in typeof(Features.Delivery.KmhPendingDelivery)
                             .GetMethod("HoldSilver")?.GetParameters() ?? new System.Reflection.ParameterInfo[0])
                    if (pi.Name == "deliveryId") holdCarriesId = true;
                r.Add(("deliveries: a held grant carries the id it will be acknowledged with",
                       holdCarriesId, holdCarriesId ? "" : "A HELD GRANT CANNOT BE ACKED WHEN IT FINALLY LANDS"));

                // A hand-kept component list can miss the receipts, so owed deliveries land again on each join.
                Type[] games = Patches.Patch_Game_FillComponents_KmhComponents.GameComponentTypes();
                Type[] worlds = Patches.Patch_Game_FillComponents_KmhComponents.WorldComponentTypes();
                bool inGame = Array.IndexOf(games, typeof(Features.Delivery.GameComponent_KMHDeliveryReceipts)) >= 0
                           && Array.IndexOf(games, typeof(Features.Delivery.GameComponent_KMHDelivery)) >= 0;
                bool ctors = Array.TrueForAll(games, t => t.GetConstructor(new[] { typeof(Verse.Game) }) != null)
                          && Array.TrueForAll(worlds, t => t.GetConstructor(new[] { typeof(RimWorld.Planet.World) }) != null);
                bool all = games.Length >= 10 && worlds.Length >= 3;
                r.Add(("deliveries: the receipt and delivery components are added to every real game, with every other KMH component",
                       inGame && ctors && all, $"{games.Length} game and {worlds.Length} world component(s); receipts={inGame}, ctors={ctors}"));
            }
            catch (Exception ex) { r.Add(("deliveries: receipt ledger", false, $"threw: {ex.Message}")); }
        }

        private static Features.Chat.Dto.ChatMessage Line(string channel, long id, string from)
            => new Features.Chat.Dto.ChatMessage { Id = id, Channel = channel, FromUsername = from, Body = "x" };

        // Shares taken from the full width squeeze the last column against the action button until its header truncates.
        private static void AddMarketplaceLayoutChecks(List<(string, bool, string)> r)
        {
            float[] widths = { 640f, 760f, 1004f, 1040f, 1600f };
            bool everyColumnOrdered = true, lastColumnReadable = true, noOverlap = true;
            string worst = "";

            foreach (float w in widths)
            {
                float[] cols = Features.Marketplace.Dialog_KMHMarketplace.ColumnXs(w);
                for (int i = 1; i < cols.Length; i++)
                    if (cols[i] <= cols[i - 1]) everyColumnOrdered = false;

                float listed = Features.Marketplace.Dialog_KMHMarketplace.ListedW(w, cols[5]);
                // 95, not 96: the width is subtracted back out of the row, so the round trip lands a hair under.
                if (listed < 95f) { lastColumnReadable = false; worst = $"{w}px -> {listed:0.0}px"; }
                if (cols[5] + listed > w) noOverlap = false;   // must not run under the action button
            }

            r.Add(("marketplace columns are strictly left-to-right", everyColumnOrdered, ""));
            r.Add(("marketplace last column stays readable at every width", lastColumnReadable, worst));
            r.Add(("marketplace last column never runs under the action button", noOverlap, ""));

            // Non-vacuity: the old full-width shares must fail the same assertion, or it pins nothing.
            float oldCol5 = 1004f * 0.84f;
            r.Add(("marketplace layout check is not vacuous (old shares fail it)",
                   Features.Marketplace.Dialog_KMHMarketplace.ListedW(1004f, oldCol5) < 90f,
                   Features.Marketplace.Dialog_KMHMarketplace.ListedW(1004f, oldCol5).ToString("0")));
        }

        // The server keeps one blob per fungible stack, so shipping the rest spends the transaction budget for nothing.
        private static void AddBulkCaptureChecks(List<(string, bool, string)> r)
        {
            KmhThingPayload Stack(int count, long rot, int hp, string blob, bool mergeable = true)
                => new KmhThingPayload
                {
                    DefName = "RawFungus", StackCount = count, RotProgressTicks = rot,
                    HitPoints = hp, MaxHitPoints = 100, ScribeXml = blob, Mergeable = mergeable,
                };

            var caps = new List<KmhThingPayload>();
            var first = Stack(75, 1000, 100, "<blob1/>");
            r.Add(("Capture: the first stack of an identity opens a group",
                   !KmhThingCapture.TryFoldFungible(caps, first), ""));
            caps.Add(first);

            bool foldedAll = true;
            for (int i = 0; i < 133; i++)
                if (!KmhThingCapture.TryFoldFungible(caps, Stack(75, 1000, 100, $"<blob{i + 2}/>"))) foldedAll = false;
            r.Add(("Capture: 134 equal stacks collapse to one representative blob",
                   foldedAll && caps.Count == 1, $"{caps.Count} group(s)"));
            r.Add(("Capture: every unit survives the fold",
                   caps.Count == 1 && caps[0].StackCount == 134 * 75, $"{(caps.Count == 1 ? caps[0].StackCount : 0)} units"));

            // Rot must be averaged, never reset - a fresh stack folded into a rotten one cannot refresh it.
            var aged = new List<KmhThingPayload> { Stack(75, 2000, 60, "<a/>") };
            KmhThingCapture.TryFoldFungible(aged, Stack(25, 0, 100, "<b/>"));
            long avgRot = aged[0].RotProgressTicks;
            r.Add(("Capture: folding weight-averages rot rather than refreshing it",
                   avgRot == 1500 && aged[0].StackCount == 100, $"rot={avgRot} count={aged[0].StackCount}"));
            r.Add(("Capture: folding weight-averages hit points too",
                   aged[0].HitPoints == 70, $"hp={aged[0].HitPoints}"));

            var ident = new List<KmhThingPayload> { Stack(10, 0, 100, "<x/>") };
            var otherStuff = Stack(10, 0, 100, "<y/>"); otherStuff.StuffDefName = "Steel";
            var otherQual  = Stack(10, 0, 100, "<z/>"); otherQual.Quality = 4;
            var otherTaint = Stack(10, 0, 100, "<w/>"); otherTaint.Tainted = true;
            r.Add(("Capture: material, quality and taint never fold together",
                   !KmhThingCapture.TryFoldFungible(ident, otherStuff)
                   && !KmhThingCapture.TryFoldFungible(ident, otherQual)
                   && !KmhThingCapture.TryFoldFungible(ident, otherTaint), ""));

            var unique = new List<KmhThingPayload> { Stack(1, -1, 90, "<gear/>", mergeable: false) };
            r.Add(("Capture: a non-fungible item is never folded",
                   !KmhThingCapture.TryFoldFungible(unique, Stack(1, -1, 90, "<gear2/>", mergeable: false)), ""));

            var unknown = new List<KmhThingPayload> { Stack(10, -1, -1, "<u/>") };
            KmhThingCapture.TryFoldFungible(unknown, Stack(10, -1, -1, "<v/>"));
            r.Add(("Capture: unknown wear survives a fold as unknown",
                   unknown[0].RotProgressTicks == -1, $"rot={unknown[0].RotProgressTicks}"));
        }

        // Read from IL: an AudioSource cannot be built headlessly, and camera zoom drives listener filters.
        private static void AddVideoAudioChecks(List<(string, bool, string)> r)
        {
            Type vp = typeof(KMHPatch.Features.Chat.ChatVideoPlayer);
            System.Reflection.MethodInfo cfg = vp.GetMethod("ConfigureAsUiAudio",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
            r.Add(("video audio: a single UI-audio configurator exists", cfg != null, ""));
            if (cfg == null) return;

            Type src = typeof(UnityEngine.AudioSource);
            string[] required =
            {
                "set_spatialBlend", "set_bypassListenerEffects", "set_bypassReverbZones",
                "set_reverbZoneMix", "set_dopplerLevel", "set_panStereo",
            };
            var missing = new List<string>();
            const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                                                     | System.Reflection.BindingFlags.NonPublic;
            foreach (string setter in required)
            {
                System.Reflection.MethodInfo m = src.GetMethod(setter,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                bool set;
                if (setter == "set_reverbZoneMix")
                {
                    // Via the compat helper: RimWorld 1.7's Unity has no reverb zones; where the member exists it is set.
                    System.Reflection.MethodInfo via = typeof(Compat.KmhUnityCompat).GetMethod("SetReverbZoneMix", Any);
                    System.Reflection.MethodInfo inner = typeof(Compat.KmhUnityCompat).GetMethod("ReverbZoneMix", Any);
                    set = via != null && MethodBodyCalls(cfg, via) && (m == null || (inner != null && MethodBodyCalls(inner, m)));
                }
                else set = m != null && MethodBodyCalls(cfg, m);
                if (!set) missing.Add(setter.Substring(4));
            }
            r.Add(("video audio: the configurator opts out of every positional and listener effect",
                   missing.Count == 0, missing.Count == 0 ? "" : "unset: " + string.Join(", ", missing)));

            r.Add(("video audio: the player's host actually applies that configuration",
                   ReferencesMethod(vp, cfg), ""));
        }

        private static bool MethodBodyCalls(System.Reflection.MethodInfo caller, System.Reflection.MethodInfo target)
        {
            byte[] il;
            try { il = caller.GetMethodBody()?.GetILAsByteArray(); } catch { return false; }
            if (il == null) return false;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue;
                int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                try { if (caller.Module.ResolveMethod(token) == target) return true; } catch { }
            }
            return false;
        }

        // Read from each dialog's own IL, or the check only proves the helper still exists, not that anyone calls it.
        private static readonly string[] FilterDialogs =
        {
            "KMHPatch.Features.Marketplace.Dialog_KMHMarketplace",
            "KMHPatch.Features.Auctions.Dialog_KMHAuctions",
            "KMHPatch.Features.Quests.Dialog_KMHQuestBoard",
            "KMHPatch.Features.WantBoard.Dialog_KMHWantBoard",
            "KMHPatch.Features.Sites.Dialog_KMHSiteOutputPicker",
        };

        private static void AddSharedControlChecks(List<(string, bool, string)> r)
        {
            System.Reflection.MethodInfo helper = typeof(DialogLayout).GetMethod(
                "DrawTightCheckbox",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            foreach (string typeName in FilterDialogs)
            {
                Type t = typeof(DialogLayout).Assembly.GetType(typeName, throwOnError: false);
                r.Add(($"{ShortName(typeName)} boolean filters use the shared checkbox helper",
                       t != null && helper != null && ReferencesMethod(t, helper),
                       t == null ? "type not found" : ""));
            }

            Type seg = typeof(DialogLayout);
            System.Reflection.MethodInfo segment = seg.GetMethod("DrawSegment",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Type road = seg.Assembly.GetType("KMHPatch.Features.Roadworks.Dialog_KMHRoadworks", throwOnError: false);
            r.Add(("roadworks tiers use the select-one segment helper, not a checkbox",
                   road != null && segment != null && ReferencesMethod(road, segment)
                   && helper != null && !ReferencesMethod(road, helper), ""));

            // A dropdown that keeps its "Category: " prefix truncates the part that actually varies.
            bool prefixFree = !DialogLayout.DropdownLabel("Components / Manufactured").StartsWith("Category");
            r.Add(("category dropdowns show the value, not a redundant prefix",
                   prefixFree && DialogLayout.DropdownLabel("Misc").EndsWith("▼"),
                   DialogLayout.DropdownLabel("Misc")));
        }

        private static string ShortName(string typeName)
        {
            int i = typeName.LastIndexOf("Dialog_KMH", StringComparison.Ordinal);
            return i < 0 ? typeName : typeName.Substring(i + 10);
        }

        // True when any method on the type (or a compiler-generated nested closure) carries a token for `target`.
        private static bool ReferencesMethod(Type t, System.Reflection.MethodInfo target)
        {
            const System.Reflection.BindingFlags All =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly;

            List<Type> types = new List<Type> { t };
            try { types.AddRange(t.GetNestedTypes(All)); } catch { }

            foreach (Type scope in types)
            {
                System.Reflection.MethodInfo[] methods;
                try { methods = scope.GetMethods(All); } catch { continue; }
                foreach (System.Reflection.MethodInfo m in methods)
                {
                    byte[] il;
                    try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch { continue; }
                    if (il == null || il.Length < 5) continue;
                    for (int i = 0; i + 4 < il.Length; i++)
                    {
                        if (il[i] != 0x28 && il[i] != 0x6F) continue;   // call / callvirt
                        int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                        try
                        {
                            if (scope.Module.ResolveMethod(token) == target) return true;
                        }
                        catch { }
                    }
                }
            }
            return false;
        }

        private static void AddToolbarLayoutChecks(List<(string, bool, string)> r)
        {
            float[] widths = { 420f, 640f, 900f, 1280f, 1600f };

            bool tiersOrdered = true, tiersInside = true, tiersSized = true;
            string worstTier = "";
            foreach (float w in widths)
                foreach (float tierW in new[] { 130f, 190f, 250f })
                {
                    float cell = Dialog_KMHRoadworks.TierCellW(w, tierW);
                    if (cell < 60f) tiersSized = false;
                    for (int i = 0; i < Dialog_KMHRoadworks.TierCount; i++)
                    {
                        float x = Dialog_KMHRoadworks.TierCellX(w, tierW, i);
                        if (i > 0 && x < Dialog_KMHRoadworks.TierCellX(w, tierW, i - 1)
                                     + Dialog_KMHRoadworks.TierCellW(w, tierW))
                        { tiersOrdered = false; worstTier = $"{w}px/tierW {tierW}"; }
                        if (x + cell > w + 0.01f) { tiersInside = false; worstTier = $"{w}px/tierW {tierW}"; }
                    }
                }

            r.Add(("roadworks: three tier cells never overlap", tiersOrdered, worstTier));
            r.Add(("roadworks: every tier cell stays inside the dialog", tiersInside, worstTier));
            r.Add(("roadworks: every tier cell stays clickable-wide", tiersSized, ""));
            r.Add(("roadworks: a tier cell is the whole option, not a marker",
                   Dialog_KMHRoadworks.TierCellW(1280f, 190f) >= 100f
                   && Dialog_KMHRoadworks.TierCellH >= 20f, ""));

            bool pickerNoOverlap = true, pickerSearchUsable = true, pickerCatUsable = true;
            string worstPicker = "";
            foreach (float w in widths)
                foreach (float measured in new[] { 110f, 180f, 220f, 400f })
                {
                    float cat    = Dialog_KMHItemPicker.CategoryW(w, measured);
                    float search = Dialog_KMHItemPicker.SearchW(w, cat);
                    if (search + Dialog_KMHItemPicker.ToolbarGap + cat > w + 0.01f)
                    { pickerNoOverlap = false; worstPicker = $"{w}px/measured {measured}"; }
                    if (cat < Dialog_KMHItemPicker.MinCategoryW - 0.01f && measured >= Dialog_KMHItemPicker.MinCategoryW)
                        pickerCatUsable = false;
                    if (w >= 420f && search < 100f) { pickerSearchUsable = false; worstPicker = $"{w}px"; }
                }

            r.Add(("item picker: search and category never overlap", pickerNoOverlap, worstPicker));
            r.Add(("item picker: the category button keeps a usable width", pickerCatUsable, ""));
            r.Add(("item picker: the search field keeps a usable width", pickerSearchUsable, worstPicker));

            // Non-vacuity: the old fixed 210px category would overflow the narrow widths this now clamps.
            r.Add(("item picker layout check is not vacuous (the old fixed width fails it)",
                   210f + Dialog_KMHItemPicker.ToolbarGap > 200f
                   && Dialog_KMHItemPicker.CategoryW(200f, 210f) + Dialog_KMHItemPicker.ToolbarGap <= 200f,
                   Dialog_KMHItemPicker.CategoryW(200f, 210f).ToString("0")));

            r.Add(("toolbar rows share one pitch across dialogs", DialogLayout.ToolbarRowH >= 30f, ""));

            // A hardcoded row pitch is shorter than the line drawn into it at any UI scale above 1x.
            bool pitchOk = true; float worstLine = 0f;
            foreach (float line in new[] { 16f, 18f, 22f, 24f, 28f, 34f, 44f })
            {
                float h = KmhStack.LabelRowHeight(line);
                if (h < line) { pitchOk = false; worstLine = line; }
            }
            r.Add(("wealth rows are pitched by the font, never shorter than the line", pitchOk,
                   pitchOk ? "16-44px lines all fit" : $"line {worstLine} overflows its row"));
            r.Add(("wealth row pitch is not vacuous (a fixed 22px fails a large font)",
                   22f < 34f && KmhStack.LabelRowHeight(34f) > 22f, KmhStack.LabelRowHeight(34f).ToString("0")));

            // A consent prompt raised mid-join is closed again by the map transition, so the player only sees it flash.
            bool held = !KmhDebugConsent.CanPrompt(Verse.ProgramState.Entry,   playUi: false, haveMap: false, windowStack: true)
                     && !KmhDebugConsent.CanPrompt(Verse.ProgramState.MapInitializing, playUi: true, haveMap: false, windowStack: true)
                     && !KmhDebugConsent.CanPrompt(Verse.ProgramState.Playing, playUi: false, haveMap: true,  windowStack: true)
                     && !KmhDebugConsent.CanPrompt(Verse.ProgramState.Playing, playUi: true,  haveMap: true,  windowStack: false);
            r.Add(("consent: the prompt is held through entry, load and map init", held, ""));
            r.Add(("consent: the prompt is shown once the game is interactive",
                   KmhDebugConsent.CanPrompt(Verse.ProgramState.Playing, playUi: true, haveMap: true, windowStack: true), ""));

            // Stretching an icon with its parent is how a square asset ends up distorted.
            bool square = true, inside = true, capped = true; string worstIcon = "";
            foreach (float w in new[] { 60f, 90f, 140f, 260f, 600f })
                foreach (float h in new[] { 20f, 26f, 30f, 48f, 120f })
                {
                    UnityEngine.Rect parent = new UnityEngine.Rect(10f, 5f, w, h);
                    UnityEngine.Rect ic = IconButton.IconRectFor(parent);
                    if (UnityEngine.Mathf.Abs(ic.width - ic.height) > 0.01f) { square = false; worstIcon = $"{w}x{h}"; }
                    if (ic.xMin < parent.xMin || ic.xMax > parent.xMax
                        || ic.yMin < parent.yMin - 0.01f || ic.yMax > parent.yMax + 0.01f)
                    { inside = false; worstIcon = $"{w}x{h}"; }
                    if (ic.width > 22f + 0.01f) { capped = false; worstIcon = $"{w}x{h}"; }
                }
            r.Add(("icons: the icon box stays square at every button size", square, worstIcon));
            r.Add(("icons: the icon box never leaves its button", inside, worstIcon));
            r.Add(("icons: a wider or taller button does not grow the icon", capped, worstIcon));
            r.Add(("icons: a button too short for a full icon shrinks it rather than overflowing",
                   IconButton.IconRectFor(new UnityEngine.Rect(0f, 0f, 200f, 12f)).height <= 12f, ""));

            AddVideoAudioChecks(r);

            // A primitive that paints while measuring draws the body twice at two offsets, so every row overlaps.
            var probe = new KmhStack(400f, true);
            probe.ValueRow("Treasury", "1,234", 24f);
            probe.ValueRow("Guild vault", "0", 24f);
            probe.Gap(6f);
            r.Add(("wealth stack: a measure pass paints nothing", probe.DrawOps == 0, $"{probe.DrawOps} draw(s)"));
            r.Add(("wealth stack: a measure pass still advances the cursor", probe.Y >= 54f, probe.Y.ToString("0")));

            bool ordered = true; string worstStack = "";
            foreach (float line in new[] { 16f, 22f, 28f, 34f, 44f })
            {
                float h = KmhStack.LabelRowHeight(line);
                var s2 = new KmhStack(400f, true);
                float prev = s2.Y;
                for (int i = 0; i < 12; i++)
                {
                    s2.ValueRow($"row{i}", "1", h);
                    if (s2.Y <= prev) { ordered = false; worstStack = $"line {line}"; }
                    prev = s2.Y;
                }
                if (System.Math.Abs(s2.Y - 12f * h) > 0.01f) { ordered = false; worstStack = $"line {line} total {s2.Y}"; }
            }
            r.Add(("wealth stack: rows stack downwards and the total covers them all", ordered, worstStack));

            // Vanilla recounts map wealth only every ~83s, so a deposit is double-counted until the nudge lands.
            r.Add(("wealth: a recount is due once the debounce has elapsed",
                   KmhWealthLedger.RecountDue(1000, 700) && KmhWealthLedger.RecountDue(5000, 0), ""));
            r.Add(("wealth: a burst of deposits cannot force a recount every time",
                   !KmhWealthLedger.RecountDue(1000, 999) && !KmhWealthLedger.RecountDue(1000, 800), ""));
            r.Add(("wealth: the debounce is far shorter than vanilla's own 5000-tick interval",
                   KmhWealthLedger.RecountDue(300, 0) && !KmhWealthLedger.RecountDue(299, 0), ""));
        }

        // Exactness, not a tolerance band: the storyteller's figure has to be checkable against a single 10,000 deposit.
        private static void AddWealthLedgerChecks(List<(string, bool, string)> r)
        {
            var none = new List<IKmhWealthSource>();
            r.Add(("wealth empty ledger -> 0", KmhWealthLedger.Sum(none) == 0f, ""));
            r.Add(("wealth empty breakdown has no rows", KmhWealthLedger.Breakdown(none).Count == 0, ""));
            r.Add(("wealth null breakdown -> empty", KmhWealthLedger.Breakdown(null).Count == 0, ""));

            float depositOnly = KmhWealthValue.OfTreasury(Snap(10000, null, null));
            r.Add(("wealth 10000 silver values as exactly 10000", depositOnly == 10000f, depositOnly.ToString("R")));
            r.Add(("wealth 10000 silver folds to exactly 10000",
                   KmhWealthLedger.Sum(new List<IKmhWealthSource> { new FakeSource("Treasury", depositOnly) }) == 10000f, ""));

            var mixed = new List<IKmhWealthSource>
            {
                new FakeSource("Treasury",    10000f),
                new FakeSource("Guild vault",   250f),
                new FakeSource("Broken",       null /*throws*/),
                new FakeSource("Negative",     -900f),
                new FakeSource("Site storage",   75f),
            };

            List<KeyValuePair<string, float>> parts = KmhWealthLedger.Breakdown(mixed);
            float partsSum = 0f;
            foreach (KeyValuePair<string, float> p in parts) partsSum += p.Value;
            r.Add(("wealth breakdown totals match the fold", partsSum == KmhWealthLedger.Sum(mixed), $"{partsSum} vs {KmhWealthLedger.Sum(mixed)}"));
            r.Add(("wealth breakdown row per source", parts.Count == mixed.Count, parts.Count.ToString()));
            r.Add(("wealth breakdown keeps source order and names",
                   parts[0].Key == "Treasury" && parts[1].Key == "Guild vault" && parts[4].Key == "Site storage", ""));
            r.Add(("wealth breakdown reports a throwing source as 0", parts[2].Value == 0f, ""));
            r.Add(("wealth breakdown clamps a negative source to 0", parts[3].Value == 0f, ""));
            r.Add(("wealth site storage carries its own weight", parts[4].Value == 75f, ""));

            // NaN in the total makes every downstream threat comparison false, silently disabling the whole feature.
            float nan = KmhWealthLedger.Sum(new List<IKmhWealthSource> { new FakeSource("Treasury", 500f), new FakeSource("Nan", float.NaN) });
            r.Add(("wealth NaN source cannot poison the total", nan == 500f, nan.ToString("R")));

            // If the fold accumulated across calls, a refresh alone would climb the raid scale with the player idle.
            float first = KmhWealthLedger.Sum(mixed), second = KmhWealthLedger.Sum(mixed);
            r.Add(("wealth re-read does not accumulate", first == second && first == 10325f, $"{first} then {second}"));

            var after = new List<IKmhWealthSource>
            {
                new FakeSource("Treasury",    6000f),
                new FakeSource("Guild vault",  250f),
                new FakeSource("Broken",      null),
                new FakeSource("Negative",    -900f),
                new FakeSource("Site storage",  75f),
            };
            r.Add(("wealth withdrawing 4000 lowers the total by 4000", first - KmhWealthLedger.Sum(after) == 4000f, ""));

            // Non-vacuity: the same assertions must fail against a fold that double-counts, or they prove nothing.
            var twice = new List<IKmhWealthSource>();
            twice.AddRange(mixed);
            twice.AddRange(mixed);
            r.Add(("wealth checks are not vacuous (double-count is caught)",
                   KmhWealthLedger.Sum(twice) != first && KmhWealthLedger.Breakdown(twice).Count != parts.Count, ""));
        }

        private static void AddUnreadChecks(List<(string, bool, string)> r)
        {
            const string server = "server", guild = "guild:Universe", dm = "dm:ada|bo";
            Features.Chat.ChatCache.Clear();

            Features.Chat.ChatCache.ApplySnapshot(server, new List<Features.Chat.Dto.ChatMessage> { Line(server, 1, "ada") });
            Features.Chat.ChatCache.ApplySnapshot(guild,  new List<Features.Chat.Dto.ChatMessage>());
            r.Add(("unread: a first snapshot is not unread",
                   Features.Chat.ChatCache.TotalUnread() == 0, Features.Chat.ChatCache.TotalUnread().ToString()));

            Features.Chat.ChatCache.Append(Line(server, 2, "ada"));
            r.Add(("unread: a live message raises the channel and the aggregate together",
                   Features.Chat.ChatCache.Unread(server) == 1 && Features.Chat.ChatCache.TotalUnread() == 1, ""));

            Features.Chat.ChatCache.Append(Line(dm, 10, "bo"));
            Features.Chat.ChatCache.Append(Line(dm, 11, "bo"));
            Features.Chat.ChatCache.Append(Line(dm, 12, "bo"));
            r.Add(("unread: the aggregate is the sum of its sources",
                   Features.Chat.ChatCache.Unread(dm) == 3 && Features.Chat.ChatCache.TotalUnread() == 4, ""));

            // A refresh while the hub sits on another channel: the arriving snapshot must acknowledge nothing.
            Features.Chat.ChatCache.ApplySnapshot(server, new List<Features.Chat.Dto.ChatMessage>
                { Line(server, 1, "ada"), Line(server, 2, "ada") });
            Features.Chat.ChatCache.ApplySnapshot(guild, new List<Features.Chat.Dto.ChatMessage>());
            r.Add(("unread: opening or refreshing Communications clears nothing",
                   Features.Chat.ChatCache.Unread(server) == 1 && Features.Chat.ChatCache.Unread(dm) == 3
                   && Features.Chat.ChatCache.TotalUnread() == 4, Features.Chat.ChatCache.TotalUnread().ToString()));

            Features.Chat.ChatCache.MarkRead(server);
            r.Add(("unread: viewing a channel clears only that channel",
                   Features.Chat.ChatCache.Unread(server) == 0 && Features.Chat.ChatCache.Unread(dm) == 3
                   && Features.Chat.ChatCache.TotalUnread() == 3, ""));

            Features.Chat.ChatCache.ApplySnapshot(dm, new List<Features.Chat.Dto.ChatMessage>
                { Line(dm, 10, "bo"), Line(dm, 11, "bo"), Line(dm, 12, "bo") });
            r.Add(("unread: a reconnect neither clears nor duplicates a DM's unread",
                   Features.Chat.ChatCache.Unread(dm) == 3 && Features.Chat.ChatCache.TotalUnread() == 3, ""));

            Features.Chat.ChatCache.MarkRead(dm);
            r.Add(("unread: viewing the DM clears the rest",
                   Features.Chat.ChatCache.TotalUnread() == 0, ""));

            Features.Chat.ChatCache.Append(Line(server, 3, "ada"));
            Features.Chat.ChatCache.Append(Line(guild, 4, "ada"));
            Features.Chat.ChatCache.MarkAllRead();
            r.Add(("unread: Mark all read clears every source at once",
                   Features.Chat.ChatCache.TotalUnread() == 0
                   && Features.Chat.ChatCache.Unread(server) == 0 && Features.Chat.ChatCache.Unread(guild) == 0, ""));

            Features.Chat.ChatCache.Append(Line(server, 5, "ada"));
            Features.Chat.ChatCache.Append(Line(server, 5, "ada"));
            r.Add(("unread: a redelivered message is not counted twice",
                   Features.Chat.ChatCache.Unread(server) == 1, Features.Chat.ChatCache.Unread(server).ToString()));

            // Unbounded, Global Chat grows all session and the panel re-lays out every line a channel holds.
            const string flood = "global:selftest";
            int cap = Features.Chat.ChatCache.MaxPerChannel;
            for (int i = 1; i <= cap + 40; i++) Features.Chat.ChatCache.Append(Line(flood, 1000 + i, "ada"), notify: false);
            Features.Chat.ChatCache.Append(Line(flood, 1000 + cap + 39, "ada"), notify: false);
            Features.Chat.ChatCache.Append(Line(flood, 1000 + cap + 42, "ada"), notify: false);
            Features.Chat.ChatCache.Append(Line(flood, 1000 + cap + 41, "ada"), notify: false);
            List<Features.Chat.Dto.ChatMessage> kept = Features.Chat.ChatCache.Recent(flood);
            bool ordered = true;
            for (int i = 1; i < kept.Count; i++) ordered &= kept[i - 1].Id < kept[i].Id;
            r.Add(("chat: a channel keeps only its newest lines, in order",
                   kept.Count == cap && ordered && kept[kept.Count - 1].Id == 1000 + cap + 42, $"kept={kept.Count} ordered={ordered}"));

            // An open chat polls every 12 s; the same lines again must not re-lay out the whole log.
            Features.Chat.ChatCache.ApplySnapshot(server, new List<Features.Chat.Dto.ChatMessage> { Line(server, 50, "ada"), Line(server, 51, "bo") });
            long g0 = Features.Chat.ChatCache.Generation;
            Features.Chat.ChatCache.ApplySnapshot(server, new List<Features.Chat.Dto.ChatMessage> { Line(server, 50, "ada"), Line(server, 51, "bo") });
            long g1 = Features.Chat.ChatCache.Generation;
            Features.Chat.Dto.ChatMessage edited = Line(server, 51, "bo");
            edited.Body = "edited";
            Features.Chat.ChatCache.ApplySnapshot(server, new List<Features.Chat.Dto.ChatMessage> { Line(server, 50, "ada"), edited });
            long g2 = Features.Chat.ChatCache.Generation;
            r.Add(("chat: a poll bringing back the same lines leaves the log as it was, and a changed line still lands",
                   g1 == g0 && g2 > g1, $"{g0}/{g1}/{g2}"));

            List<string> blockedBefore = Features.Chat.ChatModerationCache.Blocked();
            bool enabledBefore = Features.Chat.ChatModerationCache.BlockingEnabled;
            Features.Chat.ChatModerationCache.Apply(new[] { "kmh-selftest-a" }, true);
            int v0 = Features.Chat.ChatModerationCache.Version;
            Features.Chat.ChatModerationCache.Apply(new[] { "KMH-SELFTEST-A" }, true);
            int v1 = Features.Chat.ChatModerationCache.Version;
            Features.Chat.ChatModerationCache.Apply(new[] { "kmh-selftest-a", "kmh-selftest-b" }, true);
            int v2 = Features.Chat.ChatModerationCache.Version;
            Features.Chat.ChatModerationCache.Apply(blockedBefore, enabledBefore);
            r.Add(("chat: an unchanged block list rebuilds no rows, and a change still does", v1 == v0 && v2 > v1, $"{v0}/{v1}/{v2}"));

            Features.Chat.ChatCache.Clear();
        }

        // Both public overloads mean silver, so the only thing separating them is the CLR argument type.
        private static void AddSdkCompatChecks(List<(string, bool, string)> r)
        {
            System.Type api = typeof(KMH.Sdk.Client.Apis.IMarketplaceCache);
            System.Reflection.MethodInfo silver = api
                .GetMethod("TryPost", new[] { typeof(string), typeof(int), typeof(int), typeof(string), typeof(int) });
            System.Reflection.MethodInfo dec = api
                .GetMethod("TryPost", new[] { typeof(string), typeof(int), typeof(decimal), typeof(string), typeof(int) });

            r.Add(("sdk: the v1.2.x marketplace TryPost signature still exists", silver != null, ""));
            r.Add(("sdk: the decimal-silver overload exists with its own CLR signature", dec != null, ""));
            r.Add(("sdk: the two overloads are genuinely distinct methods",
                   silver != null && dec != null && silver != dec, ""));
            r.Add(("sdk: the historical parameter is still named for silver",
                   silver != null && silver.GetParameters()[2].Name == "unitPriceSilver",
                   silver == null ? "missing" : silver.GetParameters()[2].Name));
            r.Add(("sdk: no milli-silver method is published to extensions",
                   api.GetMethod("TryPostMilli") == null, ""));

            System.Type rec = typeof(KMH.Sdk.Client.Records.MarketplaceListingRecord);
            r.Add(("sdk: the v1.2.x UnitPriceSilver property remains",
                   rec.GetProperty("UnitPriceSilver")?.PropertyType == typeof(int), ""));
            r.Add(("sdk: the preferred decimal UnitPrice exists",
                   rec.GetProperty("UnitPrice")?.PropertyType == typeof(decimal), ""));
            r.Add(("sdk: no milli-silver price is published to extensions",
                   rec.GetProperty("UnitPriceMilli") == null, ""));
            r.Add(("sdk: the internal wire DTO keeps its milli field",
                   typeof(Features.Marketplace.Dto.MarketplaceListing).GetProperty("UnitPriceMilli") != null, ""));

            decimal mapped = Extensibility.MarketplaceCacheAdapter.PublicSilverFromWireMilli(100_230);
            r.Add(("sdk: an internal 100230 milli reads back as 100.23 silver", mapped == 100.23m, mapped.ToString()));
            r.Add(("sdk: a sub-silver listing keeps its exact price",
                   Extensibility.MarketplaceCacheAdapter.PublicSilverFromWireMilli(550) == 0.55m, ""));

            int fromSilverApi = Extensibility.MarketplaceCacheAdapter.WireMilliFromSilverApi(100);
            r.Add(("sdk: the historical post still means 100 silver", fromSilverApi == 100_000, fromSilverApi.ToString()));

            bool decOk = Extensibility.KmhSilver.TryToMilli(100.23m, out int decMilli);
            r.Add(("sdk: 100.23 posts as 100.23 silver", decOk && decMilli == 100_230, decMilli.ToString()));
            r.Add(("sdk: 100.23 is not reinterpreted as 100230 silver", decMilli != 100_230_000, decMilli.ToString()));

            bool wholeOk = Extensibility.KmhSilver.TryToMilli(100m, out int wholeMilli);
            r.Add(("sdk: both overloads price a plain 100 identically",
                   wholeOk && wholeMilli == fromSilverApi, $"{wholeMilli} vs {fromSilverApi}"));

            r.Add(("sdk: a thousandth is representable",
                   Extensibility.KmhSilver.TryToMilli(100.125m, out int fine) && fine == 100_125, fine.ToString()));
            r.Add(("sdk: finer than a thousandth is refused, not rounded",
                   !Extensibility.KmhSilver.TryToMilli(100.1234m, out _), ""));
            r.Add(("sdk: zero and negative prices are refused",
                   !Extensibility.KmhSilver.TryToMilli(0m, out _) && !Extensibility.KmhSilver.TryToMilli(-1m, out _), ""));
            r.Add(("sdk: a decimal price too large for the wire is refused",
                   !Extensibility.KmhSilver.TryToMilli(int.MaxValue, out _), ""));
            r.Add(("sdk: a silver conversion cannot overflow into a negative price",
                   Extensibility.KmhSilver.ToMilli(int.MaxValue) > 0, Extensibility.KmhSilver.ToMilli(int.MaxValue).ToString()));
            r.Add(("sdk: the marketplace dialog prices through the same seam",
                   Extensibility.KmhSilver.TryToMilli(0.55m, out int sub) && sub == 550, sub.ToString()));

            EnforcementBounds(r);
            ServerSwitchState(r);
            DiagnosticBounds(r);
        }

        // A diagnostic that lies about its own size, or gives up on a locked file, goes quiet exactly when it is wanted.
        private static void DiagnosticBounds(List<(string, bool, string)> r)
        {
            // Four bytes per emoji, so a char-count cap would let four times the intended payload onto the wire.
            string emoji = string.Concat(System.Linq.Enumerable.Repeat("🎮", 50));
            int chars = emoji.Length, bytes = System.Text.Encoding.UTF8.GetByteCount(emoji);
            r.Add(("uplink: a multi-byte string is measured in bytes, not characters",
                   bytes == 200 && chars == 100, $"chars={chars}, bytes={bytes}"));

            string trimmed = TrimProbe(emoji, 10);
            int trimmedBytes = System.Text.Encoding.UTF8.GetByteCount(trimmed.TrimEnd('…'));
            bool whole = trimmed.TrimEnd('…').Length % 2 == 0;   // whole surrogate pairs only
            r.Add(("uplink: a byte cap cuts on a codepoint boundary, never mid-character",
                   trimmedBytes <= 10 && whole, $"bytes={trimmedBytes} (cap 10), wholePairs={whole}"));

            string ascii = new string('a', 500);
            r.Add(("uplink: an ASCII line under the cap is left alone",
                   TrimProbe(ascii, 2000) == ascii, TrimProbe(ascii, 2000).Length.ToString()));

            r.Add(("uplink: per-line and per-session byte budgets are both bounded",
                   KmhDebugUplink.MaxLineBytes > 0 && KmhDebugUplink.MaxLineBytes <= 8192
                   && KmhDebugUplink.MaxSessionBytes > 0 && KmhDebugUplink.MaxSessionBytes <= 32L * 1024 * 1024,
                   $"line={KmhDebugUplink.MaxLineBytes}, session={KmhDebugUplink.MaxSessionBytes}"));

            KmhDebugUplink.ServerRequested = true;
            KmhDebugUplink.ResetForNewServer();
            r.Add(("uplink: a server switch clears consent and anything still queued",
                   !KmhDebugUplink.ServerRequested && !KmhDebugUplink.SessionConsent, ""));
        }

        // Mirrors the uplink's own trim so the rule is exercised without reaching into a private method.
        private static string TrimProbe(string s, int maxBytes)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(s) <= maxBytes) return s;
            int lo = 0, hi = s.Length;
            char[] chars = s.ToCharArray();
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (System.Text.Encoding.UTF8.GetByteCount(chars, 0, mid) <= maxBytes) lo = mid; else hi = mid - 1;
            }
            if (lo > 0 && char.IsHighSurrogate(s[lo - 1])) lo--;
            return s.Substring(0, lo) + "…";
        }

        // A revision means nothing across servers: carrying A's to B would make B's first snapshot look stale.
        private static void ServerSwitchState(List<(string, bool, string)> r)
        {
            Features.Treasury.TreasuryCache.Clear();
            Features.Marketplace.MarketplaceCache.Clear();

            Features.Treasury.TreasuryCache.Apply(new Features.Treasury.Dto.TreasurySnapshot
                { Revision = 100, IsGuildOwned = false, SilverBalance = 5000 });
            Features.Marketplace.MarketplaceCache.Apply(new Features.Marketplace.Dto.MarketplaceSnapshot { Revision = 100 });
            bool onA = Features.Treasury.TreasuryCache.Personal?.SilverBalance == 5000;

            // Switching servers clears session-scoped state; without that the next server is invisible.
            Features.Treasury.TreasuryCache.Clear();
            Features.Marketplace.MarketplaceCache.Clear();
            Features.Treasury.TreasuryCache.Apply(new Features.Treasury.Dto.TreasurySnapshot
                { Revision = 1, IsGuildOwned = false, SilverBalance = 12 });
            Features.Marketplace.MarketplaceCache.Apply(new Features.Marketplace.Dto.MarketplaceSnapshot { Revision = 1 });
            bool onB = Features.Treasury.TreasuryCache.Personal?.SilverBalance == 12
                    && Features.Marketplace.MarketplaceCache.Snapshot?.Revision == 1;

            Features.Treasury.TreasuryCache.Clear();
            Features.Treasury.TreasuryCache.Apply(new Features.Treasury.Dto.TreasurySnapshot
                { Revision = 2, IsGuildOwned = false, SilverBalance = 5000 });
            bool backOnA = Features.Treasury.TreasuryCache.Personal?.SilverBalance == 5000;

            r.Add(("server switch: B's revision 1 is not discarded because A had reached 100",
                   onA && onB, $"appliedOnA={onA}, appliedOnB={onB}"));
            r.Add(("server switch: returning to A rehydrates from its own revision",
                   backOnA, Features.Treasury.TreasuryCache.Personal?.SilverBalance.ToString() ?? "null"));

            // Within one server the guard still has to hold, or an out-of-order push resurrects spent silver.
            Features.Treasury.TreasuryCache.Apply(new Features.Treasury.Dto.TreasurySnapshot
                { Revision = 1, IsGuildOwned = false, SilverBalance = 999999 });
            r.Add(("server switch: an older revision from the same server is still refused",
                   Features.Treasury.TreasuryCache.Personal?.SilverBalance == 5000,
                   Features.Treasury.TreasuryCache.Personal?.SilverBalance.ToString() ?? "null"));

            long heldRevision = Features.Treasury.TreasuryCache.Personal?.Revision ?? 0;
            Features.Treasury.TreasuryCache.Apply(null);
            bool unchanged = Features.Treasury.TreasuryCache.Personal?.SilverBalance == 5000
                          && (Features.Treasury.TreasuryCache.Personal?.Revision ?? 0) == heldRevision;
            Features.Treasury.TreasuryCache.Apply(new Features.Treasury.Dto.TreasurySnapshot
                { Revision = heldRevision, IsGuildOwned = false, SilverBalance = 4000 });
            bool retryApplied = Features.Treasury.TreasuryCache.Personal?.SilverBalance == 4000;
            r.Add(("snapshot apply: a failed apply advances no revision, so the retry still lands",
                   unchanged && retryApplied, $"unchangedOnFailure={unchanged}, retryApplied={retryApplied}"));

            Features.Treasury.TreasuryCache.Clear();
            Features.Marketplace.MarketplaceCache.Clear();
        }

        // An apply that swallows per-file failures and clears its marker anyway makes a partial apply look clean.
        private static void ConfigApplyIsTransactional(List<(string, bool, string)> r)
        {
            var A = typeof(Features.Enforcement.EnforcementProfileApplier);
            const System.Reflection.BindingFlags NS =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

            bool buildsWhole = A.GetMethod("BuildCandidate", NS) != null;
            bool commits     = A.GetMethod("CommitCandidate", NS) != null;
            // The in-place copier is gone, not merely bypassed by one caller.
            bool oldGone     = A.GetMethod("CopyProfileToConfig", NS) == null;

            // Every path that writes Config must go through the commit, or one of them stays non-transactional.
            bool allStaged = true;
            var notStaged = new List<string>();
            foreach (string m in new[] { "ApplyProfile", "ReapplyIfActive", "RestoreBackupToConfig" })
                if (A.GetMethod(m, NS) == null) { allStaged = false; notStaged.Add(m + " missing"); }

            r.Add(("enforcement: a config profile is built whole before the live folder is touched",
                   buildsWhole && commits && oldGone,
                   $"build={buildsWhole}, commit={commits}, inPlaceCopierRemoved={oldGone}"));
            r.Add(("enforcement: every path that rewrites Config still exists to route through the commit",
                   allStaged, allStaged ? "apply, reapply and restore all present" : string.Join(", ", notStaged)));
        }

        // The browser must not invent state: an outage still shows cached rows, an unevaluated mod list never reads "ready".
        private static void GlobalBrowserState(List<(string, bool, string)> r)
        {
            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "kmh-dir-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(scratch);
            try
            {
                Features.Global.KmhDirectoryCache.RootOverrideForTest = scratch;
                Features.Global.KmhDirectoryClient.ResetForTest();

                var cards = new List<Features.Global.ServerCardDto>
                {
                    new Features.Global.ServerCardDto
                    {
                        ServerId = "0123456789abcdef0123456789abcdef", Name = "Medieval Fantasy",
                        Players = 18, MaxPlayers = 40, Status = "Online",
                        Protocol = SubProtocol.KmhProtocol.CurrentVersion, ManifestState = "Published",
                        JoinEndpoint = "play.example.com:25555",
                        ModPolicy = Features.Global.ModPolicyNames.Enforced, ModCount = 2,
                    },
                };

                Features.Global.KmhDirectoryCache.Save(cards);
                Features.Global.KmhDirectoryClient.ResetForTest();
                Features.Global.KmhDirectoryClient.LoadCacheIfEmpty();

                bool cached = Features.Global.KmhDirectoryClient.Cards.Count == 1
                           && Features.Global.KmhDirectoryClient.ShowingCache;
                r.Add(("browser: a saved directory loads when Global cannot be reached", cached,
                       cached ? "cards restored from disk and flagged as cache"
                              : $"{Features.Global.KmhDirectoryClient.Cards.Count} card(s), cache flag {Features.Global.KmhDirectoryClient.ShowingCache}"));

                string label = Features.Global.KmhCompatibility.Describe(cards[0]);
                bool honest = Features.Global.KmhCompatibility.Evaluate(cards[0]) == Features.Global.CompatState.Checking
                           && label.IndexOf("ready", StringComparison.OrdinalIgnoreCase) < 0;
                r.Add(("browser: an unevaluated mod list never reads as READY", honest,
                       honest ? $"reports '{label}'" : $"REPORTED '{label}' WITHOUT A MOD CHECK"));

                var wrongProtocol = new Features.Global.ServerCardDto
                {
                    ServerId = "ffffffffffffffffffffffffffffffff", Name = "Old",
                    Status = "Online", Protocol = SubProtocol.KmhProtocol.CurrentVersion + 1,
                };
                bool refuses = Features.Global.KmhCompatibility.Evaluate(wrongProtocol)
                               == Features.Global.CompatState.ProtocolMismatch;
                r.Add(("browser: a protocol KMH cannot speak is reported as unjoinable", refuses,
                       refuses ? "cannot join" : "OFFERED A SERVER THE CLIENT CANNOT TALK TO"));

                BrowserModStates(r, cards[0]);
                ModEntryActions(r);
                SteamJoinAccept(r);
                ModListPlan(r);
                BrowserActionRow(r);
                JoinPreparationRules(r);
                NoAccidentalHarmonyHooks(r);
                WhatsNewIsCurrent(r);

                // Selection is by ServerId, so a realtime reorder cannot move the highlight onto another server.
                bool byId = typeof(Features.Global.Dialog_KMHServerBrowser)
                    .GetField("_selectedServerId", System.Reflection.BindingFlags.Instance
                                                 | System.Reflection.BindingFlags.NonPublic)?.FieldType == typeof(string);
                r.Add(("browser: selection is held as a ServerId, not a row index", byId,
                       byId ? "_selectedServerId is a string id" : "SELECTION IS POSITIONAL"));
            }
            finally
            {
                Features.Global.KmhDirectoryCache.RootOverrideForTest = null;
                Features.Global.KmhDirectoryClient.ResetForTest();
                try { System.IO.Directory.Delete(scratch, true); } catch { }
            }
        }

        // The browser's whole promise is that no state is indefinite, so every branch is pinned to a named state.
        private static void BrowserModStates(List<(string, bool, string)> r, Features.Global.ServerCardDto card)
        {
            Features.Global.KmhLocalMods.SeedForTest(new List<string> { "Core", "Harmony", "RimWorld Together" });
            string id = card.ServerId;

            void Seed(string policy, List<string> required, List<string> optional)
            {
                var profile = new Features.Global.ModProfileDto
                {
                    ServerId = id, Policy = policy, Revision = card.ManifestRevision,
                    Required = required ?? new List<string>(), Optional = optional ?? new List<string>(),
                };
                Features.Global.ModProfileClient.SeedForTest(id, new Features.Global.ModProfileClient.Entry
                {
                    Revision = card.ManifestRevision, Profile = profile,
                    Comparison = Features.Global.ModComparison.Compare(profile, Features.Global.KmhLocalMods.ActiveNames()),
                });
            }

            try
            {
                Seed(Features.Global.ModPolicyNames.Enforced,
                     new List<string> { "Core", "Harmony", "RimWorld Together" }, null);
                bool ready = Features.Global.KmhCompatibility.Evaluate(card) == Features.Global.CompatState.Ready;
                r.Add(("browser: an exact mod match reports Ready to join", ready,
                       ready ? "ready" : $"REPORTED {Features.Global.KmhCompatibility.Evaluate(card)}"));

                Seed(Features.Global.ModPolicyNames.Enforced,
                     new List<string> { "Core", "Harmony", "RimWorld Together", "Medieval Overhaul" }, null);
                var st = Features.Global.KmhCompatibility.Evaluate(card);
                List<string> why = Features.Global.KmhCompatibility.Details(card);
                bool named = st == Features.Global.CompatState.ModsAction
                          && why.Exists(l => l.IndexOf("Medieval Overhaul", StringComparison.Ordinal) >= 0);
                r.Add(("browser: a missing required mod blocks the join and is named", named,
                       named ? "names the missing mod" : $"state {st}, {why.Count} detail line(s)"));

                // RWT refuses a client running anything that is neither required nor optional, so the browser must too.
                Seed(Features.Global.ModPolicyNames.Enforced, new List<string> { "Core", "Harmony" }, null);
                st = Features.Global.KmhCompatibility.Evaluate(card);
                why = Features.Global.KmhCompatibility.Details(card);
                bool extra = st == Features.Global.CompatState.ModsAction
                          && why.Exists(l => l.IndexOf("RimWorld Together", StringComparison.Ordinal) >= 0);
                r.Add(("browser: a mod the server does not allow blocks the join and is named", extra,
                       extra ? "names the disallowed mod" : $"state {st}, {why.Count} detail line(s)"));

                Seed(Features.Global.ModPolicyNames.Enforced,
                     new List<string> { "Core", "Harmony", "RimWorld Together" }, new List<string> { "Rimatomics" });
                st = Features.Global.KmhCompatibility.Evaluate(card);
                bool optionalOk = st == Features.Global.CompatState.ModsOptionalDiffer
                               && !Features.Global.KmhCompatibility.IsBlocking(st);
                r.Add(("browser: an optional mod the player lacks is reported but never blocks", optionalOk,
                       optionalOk ? "joinable with a note" : $"state {st}"));

                string savedPolicy = card.ModPolicy;
                card.ModPolicy = Features.Global.ModPolicyNames.AllowAll;
                bool allowAll = Features.Global.KmhCompatibility.Evaluate(card) == Features.Global.CompatState.Ready
                             && Features.Global.KmhCompatibility.ModsLabel(card)
                                    .IndexOf("pending", StringComparison.OrdinalIgnoreCase) < 0;
                r.Add(("browser: a server that enforces no mod profile reads as joinable, never pending", allowAll,
                       allowAll ? "any mods allowed" : $"state {Features.Global.KmhCompatibility.Evaluate(card)}"));
                card.ModPolicy = savedPolicy;

                string savedEndpoint = card.JoinEndpoint;
                card.JoinEndpoint = "";
                bool noAddress = Features.Global.KmhCompatibility.Evaluate(card) == Features.Global.CompatState.NoAddress;
                r.Add(("browser: a listing with no published address cannot be joined from the browser", noAddress,
                       noAddress ? "no address" : $"state {Features.Global.KmhCompatibility.Evaluate(card)}"));
                card.JoinEndpoint = savedEndpoint;

                bool split = card.TrySplitEndpoint(out string host, out int port) && host == "play.example.com" && port == 25555
                          && !new Features.Global.ServerCardDto { JoinEndpoint = "play.example.com" }.TrySplitEndpoint(out _, out _)
                          && !new Features.Global.ServerCardDto { JoinEndpoint = "host:70000" }.TrySplitEndpoint(out _, out _);
                r.Add(("browser: a join address only parses when it carries a usable port", split,
                       split ? "host and port split, bad ones refused" : $"host='{host}' port={port}"));

                // Blank is this build's default directory; anything without a scheme would never leave the process.
                bool directory = Features.Global.GlobalHttp.Normalize("") == Features.Global.GlobalHttp.DefaultBaseUrl
                              && Features.Global.GlobalHttp.Normalize("global.example.com") == Features.Global.GlobalHttp.DefaultBaseUrl
                              && (Features.Global.GlobalHttp.DefaultBaseUrl == Features.Global.GlobalHttp.OfficialBaseUrl
                                  || Features.Global.GlobalHttp.DefaultBaseUrl == "https://preview.knappe0.games")
                              && Features.Global.GlobalHttp.Normalize("http://127.0.0.1:5042/") == "http://127.0.0.1:5042"
                              && Features.Global.GlobalHttp.Normalize(" https://g.example ") == "https://g.example";
                r.Add(("browser: a self-hosted directory address is honoured, and a scheme-less one is refused", directory,
                       directory ? "blank and scheme-less both fall back to " + Features.Global.GlobalHttp.DefaultBaseUrl
                                 : $"normalised '' to {Features.Global.GlobalHttp.Normalize("")}"));

                // The player token is a long-lived bearer credential; remote plain http (or its ws://) would leak it.
                string remoteHttp = Features.Global.GlobalHttp.Normalize("http://directory.example.com");
                bool plainHttp = remoteHttp == Features.Global.GlobalHttp.DefaultBaseUrl
                              && Features.Global.GlobalHttp.Normalize("http://localhost:5042") == "http://localhost:5042"
                              && Features.Global.GlobalHttp.Normalize("http://[::1]:5042") == "http://[::1]:5042"
                              && Features.Global.GlobalHttp.Normalize("https://directory.example.com") == "https://directory.example.com";
                r.Add(("browser: plain http reaches a directory only on this machine", plainHttp,
                       plainHttp ? "loopback allowed, remote http refused, https honoured"
                                 : $"a remote http directory normalised to '{remoteHttp}'"));

                ProtocolMismatch(r);

                // Switching directories must drop the saved rows, or the old one's servers return while the new one is slow.
                Features.Global.KmhDirectoryCache.Save(new List<Features.Global.ServerCardDto> { card });
                Features.Global.KmhDirectoryClient.ForgetDirectory();
                Features.Global.KmhDirectoryClient.LoadCacheIfEmpty();
                bool forgotten = Features.Global.KmhDirectoryClient.Cards.Count == 0;
                r.Add(("browser: pointing the client at another directory forgets the first one's saved rows", forgotten,
                       forgotten ? "nothing survives the switch"
                                 : $"{Features.Global.KmhDirectoryClient.Cards.Count} row(s) came back from disk"));

                bool versions = Features.Global.KmhRwtVersions.Normalize("Release 26.8.31.1") == "26.8.31.1"
                             && Features.Global.KmhRwtVersions.Supports("")
                             && Features.Global.KmhRwtVersions.Normalize(" 26.6.9.1 ") == "26.6.9.1";
                r.Add(("browser: RWT release strings compare in one normalised shape", versions,
                       versions ? "'Release X' and 'X' are the same version" : "NORMALISATION DISAGREES"));
            }
            finally
            {
                Features.Global.ModProfileClient.Invalidate();
                Features.Global.KmhLocalMods.SeedForTest(null);
            }
        }

        // A server's mod list is only actionable if the client can tell a Workshop mod from an expansion.
        private static void ModEntryActions(List<(string, bool, string)> r)
        {
            try
            {
                Features.Global.KmhLocalMods.SeedForTest(new List<string> { "Core", "RimWorld Together" });

                var body = Newtonsoft.Json.Linq.JObject.Parse(
                    "{'server_id':'" + new string('c', 32) + "','policy':'Enforced',"
                  + "'required':['Core','RimWorld Together','Harmony','Royalty','HandMade'],"
                  + "'optional':['Rimatomics'],"
                  + "'entries':["
                  + "{'name':'Core','role':'required','kind':'local','id':'Core'},"
                  + "{'name':'Harmony','role':'required','kind':'workshop','id':'2009463077'},"
                  + "{'name':'Royalty','role':'required','kind':'expansion','id':'Royalty'},"
                  + "{'name':'HandMade','role':'required','kind':'local','id':''},"
                  + "{'name':'Rimatomics','role':'optional','kind':'workshop','id':'1095331071'}]}");

                Features.Global.ModProfileDto profile = Features.Global.ModProfileDto.Parse(body);
                bool parsed = profile != null && profile.Entries.Count == 5;
                r.Add(("mods: a published entry list is read off the profile", parsed,
                       parsed ? "5 entries" : $"{profile?.Entries.Count ?? -1} entries"));

                // Nothing on disk, stated: in a real game the player's own installed mods would otherwise decide these.
                var none = new List<Features.Mods.InstalledMod>();
                var c = Features.Global.ModComparison.Compare(profile, Features.Global.KmhLocalMods.ActiveNames(), none);
                bool split = c.MissingWorkshop.Count == 1 && c.MissingWorkshop[0].Name == "Harmony"
                          && c.MissingExpansions.Count == 1 && c.MissingExpansions[0].Name == "Royalty"
                          && c.MissingLocal.Count == 1 && c.MissingLocal[0].Name == "HandMade";
                r.Add(("mods: missing mods are split into subscribe, buy, and install by hand", split,
                       split ? "3 kinds told apart"
                             : $"workshop {c.MissingWorkshop.Count}, expansion {c.MissingExpansions.Count}, local {c.MissingLocal.Count}"));

                List<Features.Global.ModEntryDto> offered = c.Subscribable();
                bool offers = offered.Count == 1 && offered[0].Id == "2009463077";
                r.Add(("mods: Subscribe is offered for the required Workshop mod only, never an expansion", offers,
                       offers ? "the blocker alone" : $"{offered.Count} offered"));

                List<Features.Global.ModEntryDto> optional = c.OptionalSubscribable();
                bool optionalOffered = optional.Count == 1 && optional[0].Id == "1095331071" && c.OptionalWorkshop.Count == 1;
                r.Add(("mods: an optional Workshop mod is offered on its own, never subscribed with the blockers", optionalOffered,
                       optionalOffered ? "separate opt-in" : $"{optional.Count} optional"));

                // An id that is not a Workshop file id downloads nothing, so it must never be offered as one.
                bool noGuess = !new Features.Global.ModEntryDto { Kind = "workshop", Id = "Royalty" }.Subscribable
                            && !new Features.Global.ModEntryDto { Kind = "workshop", Id = "12" }.Subscribable
                            && new Features.Global.ModEntryDto { Kind = "workshop", Id = "2009463077" }.Subscribable;
                r.Add(("mods: only a real Workshop file id is treated as subscribable", noGuess,
                       noGuess ? "digits only, six or more" : "A NON-ID WAS ACCEPTED"));

                // An unknown kind from a newer server must fall back, not reach a branch that treats it as workshop.
                var odd = Newtonsoft.Json.Linq.JObject.Parse(
                    "{'server_id':'" + new string('d', 32) + "','policy':'Enforced',"
                  + "'entries':[{'name':'Future','role':'invented','kind':'invented','id':'2009463077'}]}");
                Features.Global.ModProfileDto fut = Features.Global.ModProfileDto.Parse(odd);
                bool fallback = fut.Entries.Count == 1
                             && fut.Entries[0].Kind == Features.Global.ModKindNames.Local
                             && fut.Entries[0].Role == Features.Global.ModRoleNames.Required
                             && !fut.Entries[0].Subscribable;
                r.Add(("mods: a kind this client does not know falls back instead of being acted on", fallback,
                       fallback ? "treated as local" : $"kind '{fut.Entries[0].Kind}'"));

                // A server that predates entries publishes none: the plain list still answers, and nothing is invented.
                var old = Newtonsoft.Json.Linq.JObject.Parse(
                    "{'server_id':'" + new string('e', 32) + "','policy':'Enforced',"
                  + "'required':['Core','RimWorld Together','Harmony']}");
                var oldC = Features.Global.ModComparison.Compare(Features.Global.ModProfileDto.Parse(old),
                                                                 Features.Global.KmhLocalMods.ActiveNames(), none);
                bool legacy = oldC.Missing.Count == 1 && oldC.MissingWorkshop.Count == 0
                           && oldC.MissingExpansions.Count == 0 && oldC.MissingLocal.Count == 0
                           && oldC.Subscribable().Count == 0;
                r.Add(("mods: a server publishing no entries offers no Subscribe and still names what is missing", legacy,
                       legacy ? "named, nothing invented" : $"{oldC.Subscribable().Count} id(s) invented"));

                // Expansions are the one blocker no button can clear, so they get their own answer.
                var dlcOnly = Newtonsoft.Json.Linq.JObject.Parse(
                    "{'server_id':'" + new string('f', 32) + "','policy':'Enforced',"
                  + "'required':['Core','RimWorld Together','Royalty'],"
                  + "'entries':[{'name':'Royalty','role':'required','kind':'expansion','id':'Royalty'}]}");
                var dlcC = Features.Global.ModComparison.Compare(Features.Global.ModProfileDto.Parse(dlcOnly),
                                                                 Features.Global.KmhLocalMods.ActiveNames(), none);
                r.Add(("mods: a join blocked only by expansions is reported as its own state", dlcC.BlockedOnlyByExpansions,
                       dlcC.BlockedOnlyByExpansions ? "needs an expansion" : "reported as a fixable mod list"));

                var relaxed = Newtonsoft.Json.Linq.JObject.Parse(
                    "{'server_id':'" + new string('a', 32) + "','policy':'Enforced',"
                  + "'required':['Core','RimWorld Together'],'optional':['Royalty'],"
                  + "'entries':[{'name':'Royalty','role':'optional','kind':'expansion','id':'Royalty'}]}");
                var relaxedC = Features.Global.ModComparison.Compare(Features.Global.ModProfileDto.Parse(relaxed),
                                                                     Features.Global.KmhLocalMods.ActiveNames(), none);
                bool optionalDlc = !relaxedC.Blocking && relaxedC.OptionalNotInstalled.Count == 1;
                r.Add(("mods: an expansion the owner made optional no longer blocks a player who lacks it", optionalDlc,
                       optionalDlc ? "joinable" : $"blocking={relaxedC.Blocking}"));

                // Steam already has them on disk, so Subscribe again does nothing; the button must say what is left.
                Features.Mods.KmhSubscribePlan.ResetForTest();
                var queued = new Features.Mods.KmhSubscribePlan { OnDisk = 2 };
                bool restart = queued.RestartIsWhatIsLeft && !queued.Offers && !queued.StillDownloading;
                r.Add(("mods: mods Steam already has on disk ask for a restart, not another subscribe", restart,
                       restart ? "restart is what is left" : "OFFERED A BUTTON THAT DOES NOTHING"));

                var pending = new Features.Mods.KmhSubscribePlan();
                pending.ToSubscribe.Add(new Features.Global.ModEntryDto
                    { Name = "Harmony", Kind = Features.Global.ModKindNames.Workshop, Id = "2009463077" });
                pending.OnDisk = 1;
                bool mixed = pending.Offers && !pending.RestartIsWhatIsLeft
                          && pending.Ids().Count == 1 && pending.Names()[0] == "Harmony";
                r.Add(("mods: with some already on disk, only the rest are offered", mixed,
                       mixed ? "offers the remainder" : $"{pending.Ids().Count} offered"));

                // Steam's item flags: an out-of-date copy is not "installed", an unstarted subscription is not "downloading".
                var W = Features.Mods.WorkshopItemState.NotSubscribed;
                bool flags = Features.Mods.KmhWorkshop.FromFlags(0) == W
                          && Features.Mods.KmhWorkshop.FromFlags(1) == Features.Mods.WorkshopItemState.Queued
                          && Features.Mods.KmhWorkshop.FromFlags(1 | 4) == Features.Mods.WorkshopItemState.Installed
                          && Features.Mods.KmhWorkshop.FromFlags(1 | 4 | 8) == Features.Mods.WorkshopItemState.NeedsUpdate
                          && Features.Mods.KmhWorkshop.FromFlags(1 | 4 | 8 | 16) == Features.Mods.WorkshopItemState.Downloading
                          && Features.Mods.KmhWorkshop.FromFlags(1 | 32) == Features.Mods.WorkshopItemState.Downloading;
                r.Add(("mods: Steam's item flags read as queued, downloading, out of date or on disk - never a guess", flags,
                       flags ? "NeedsUpdate is not Installed" : "A STEAM STATE WAS MISREAD"));

                // What each state asks of the player: a restart only once everything is on disk and unseen by RimWorld.
                var e1 = new Features.Global.ModEntryDto { Name = "A", Kind = Features.Global.ModKindNames.Workshop, Id = "2009463077" };
                var p1 = new Features.Mods.KmhSubscribePlan();
                Features.Mods.KmhSubscribePlan.Place(p1, e1, Features.Mods.WorkshopItemState.Downloading, false);
                Features.Mods.KmhSubscribePlan.Place(p1, e1, Features.Mods.WorkshopItemState.Installed, false);
                bool waiting = p1.StillDownloading && !p1.RestartIsWhatIsLeft;
                var p2 = new Features.Mods.KmhSubscribePlan();
                Features.Mods.KmhSubscribePlan.Place(p2, e1, Features.Mods.WorkshopItemState.NeedsUpdate, false);
                bool update = p2.OffersDownload && !p2.RestartIsWhatIsLeft;
                var p3 = new Features.Mods.KmhSubscribePlan();
                Features.Mods.KmhSubscribePlan.Place(p3, e1, Features.Mods.WorkshopItemState.Installed, true);
                bool enable = p3.OnDiskDisabled == 1 && !p3.RestartIsWhatIsLeft;
                bool states = waiting && update && enable;
                r.Add(("mods: still downloading says wait, out of date offers a download, on disk but switched off is not a restart", states,
                       $"waiting={waiting} update={update} enableNotRestart={enable}"));

                // A plan is cached per server and revision; a republished mod list must not keep serving the old one.
                var cardA = new Features.Global.ServerCardDto { ServerId = new string('1', 32), ManifestRevision = 1 };
                Features.Mods.KmhSubscribePlan.SeedForTest(cardA, queued);
                bool held = ReferenceEquals(Features.Mods.KmhSubscribePlan.For(cardA), queued);
                cardA.ManifestRevision = 2;
                bool rebuilt = !ReferenceEquals(Features.Mods.KmhSubscribePlan.For(cardA), queued);
                r.Add(("mods: the subscribe plan is cached per revision and rebuilt when one is published", held && rebuilt,
                       held && rebuilt ? "cached, then rebuilt" : $"held={held} rebuilt={rebuilt}"));

                Features.Mods.KmhSubscribePlan.SeedForTest(cardA, queued);
                Features.Mods.KmhSubscribePlan.Invalidate();
                bool afterClick = !ReferenceEquals(Features.Mods.KmhSubscribePlan.For(cardA), queued);
                r.Add(("mods: subscribing invalidates the plan rather than waiting out the recheck", afterClick,
                       afterClick ? "rebuilt on the next draw" : "THE OLD PLAN SURVIVED THE CLICK"));
                Features.Mods.KmhSubscribePlan.ResetForTest();
            }
            catch (Exception ex) { r.Add(("mods: the entry rules run outside the game", false, ex.Message)); }
            finally { Features.Global.KmhLocalMods.SeedForTest(null); }
        }

        // A wrong ModsConfig.xml leaves a game that will not start, so every rule here refuses to guess.
        private static void ModListPlan(List<(string, bool, string)> r)
        {
            try
            {
                // These rules are tested alone; the dependency rule reads the running game and has its own check below.
                Features.Mods.KmhModPlan.DependenciesForTest = () => new string[0];
                Features.Global.ModProfileDto Profile(string entriesJson)
                    => Features.Global.ModProfileDto.Parse(Newtonsoft.Json.Linq.JObject.Parse(
                        "{'server_id':'" + new string('9', 32) + "','policy':'Enforced','entries':["
                      + entriesJson + "]}"));

                Features.Mods.InstalledMod Mod(string name, string id, bool active)
                    => new Features.Mods.InstalledMod { Name = name, PackageId = id, Active = active };

                // Every fixture keeps RWT and KMH: a plan switching either off is refused, so omitting them tests the guard.
                const string Keep =
                    "{'name':'RimWorld Together','role':'required','kind':'workshop','id':'3005289691'},"
                  + "{'name':'KMH: Colony Nexus','role':'required','kind':'local','id':'KMHPatch'}";

                const string ServerList =
                    "{'name':'Core','role':'required','kind':'local','id':'Core'},"
                  + "{'name':'Harmony','role':'required','kind':'workshop','id':'2009463077'},"
                  + "{'name':'Nice','role':'optional','kind':'workshop','id':'1095331071'}," + Keep;

                Features.Mods.InstalledMod[] KeepMods() => new[]
                {
                    new Features.Mods.InstalledMod { Name = "RimWorld Together",
                        PackageId = "nova.rimworldtogether", Active = true },
                    new Features.Mods.InstalledMod { Name = "KMH: Colony Nexus",
                        PackageId = "knappe.kmh.patch", Active = true },
                };

                var installed = new List<Features.Mods.InstalledMod>
                {
                    Mod("Core", "ludeon.rimworld", true),
                    Mod("Harmony", "brrainz.harmony", false),
                    Mod("Junk", "someone.junk", true),
                };
                installed.AddRange(KeepMods());
                var plan = Features.Mods.KmhModPlan.Build(Profile(ServerList), installed,
                    new List<string> { "ludeon.rimworld", "someone.junk", "nova.rimworldtogether", "knappe.kmh.patch" });
                bool basic = plan.Ok
                          && plan.WillEnable.Count == 1 && plan.WillEnable[0] == "Harmony"
                          && plan.WillDisable.Count == 1 && plan.WillDisable[0] == "Junk"
                          && plan.Order.Count == 4
                          && plan.Order[0] == "ludeon.rimworld"
                          // Newly enabled, so it goes last rather than into the middle of an order that works.
                          && plan.Order[3] == "brrainz.harmony";
                r.Add(("mod plan: an installed but disabled mod is turned on, and a disallowed one off", basic,
                       basic ? "enable 1, disable 1" : $"ok={plan.Ok} {string.Join(" ", plan.Order.ToArray())}"));

                bool optionalAbsent = plan.Ok && plan.NotInstalled.Count == 0;
                r.Add(("mod plan: an optional mod that is not installed is simply left out", optionalAbsent,
                       optionalAbsent ? "left out" : $"{plan.NotInstalled.Count} reported missing"));

                var optOff = new List<Features.Mods.InstalledMod> { Mod("Core", "ludeon.rimworld", true), Mod("Harmony", "brrainz.harmony", true), Mod("Nice", "someone.nice", false) };
                optOff.AddRange(KeepMods());
                var offPlan = Features.Mods.KmhModPlan.Build(Profile(ServerList), optOff, new List<string> { "ludeon.rimworld", "brrainz.harmony" });
                var optOn = new List<Features.Mods.InstalledMod> { Mod("Core", "ludeon.rimworld", true), Mod("Harmony", "brrainz.harmony", true), Mod("Nice", "someone.nice", true) };
                optOn.AddRange(KeepMods());
                var onPlan = Features.Mods.KmhModPlan.Build(Profile(ServerList), optOn, new List<string> { "ludeon.rimworld", "brrainz.harmony", "someone.nice" });
                bool optionalChoice = offPlan.Ok && !offPlan.WillEnable.Contains("Nice") && !offPlan.Order.Contains("someone.nice")
                                   && onPlan.Ok && onPlan.Order.Contains("someone.nice") && !onPlan.WillDisable.Contains("Nice");
                r.Add(("mod plan: an optional mod is never switched on by applying a server's list, and one already on stays on", optionalChoice,
                       optionalChoice ? "player's choice kept" : $"off: enable={string.Join(",", offPlan.WillEnable.ToArray())} on: order={string.Join(",", onPlan.Order.ToArray())}"));

                // The server named a packageId: a mod that only shares the display name is someone else's mod.
                var fake = new List<Features.Mods.InstalledMod> { Mod("Core", "ludeon.rimworld", true), Mod("Harmony", "someone.fakeharmony", false) };
                fake.AddRange(KeepMods());
                var strict = Features.Mods.KmhModPlan.Build(
                    Profile("{'name':'Core','role':'required','kind':'local','id':'Core'},"
                          + "{'name':'Harmony','role':'required','kind':'workshop','id':'2009463077','package_id':'brrainz.harmony'}," + Keep),
                    fake, new List<string> { "ludeon.rimworld" });
                bool noNameGuess = !strict.Ok && strict.NotInstalled.Contains("Harmony") && !strict.WillEnable.Contains("Harmony");
                r.Add(("mod plan: a published packageId is never matched to another mod by its display name", noNameGuess,
                       noNameGuess ? "refused, named as missing" : "ENABLED A DIFFERENT MOD WITH THE SAME NAME"));

                // RimWorld renames the Workshop copy "_steam" when a local copy shares its id; the one in use wins.
                var twoCopies = new List<Features.Mods.InstalledMod>
                {
                    Mod("Core", "ludeon.rimworld", true),
                    Mod("RimWorld Together", "nova.rimworldtogether", false),
                    Mod("RimWorld Together", "nova.rimworldtogether_steam", true),
                    Mod("KMH: Colony Nexus", "knappe.kmh.patch", true),
                };
                var copies = Features.Mods.KmhModPlan.Build(
                    Profile("{'name':'Core','role':'required','kind':'local','id':'Core'},"
                          + "{'name':'RimWorld Together','role':'required','kind':'workshop','id':'3005289691','package_id':'nova.rimworldtogether'},"
                          + "{'name':'KMH: Colony Nexus','role':'required','kind':'local','id':'KMHPatch','package_id':'knappe.kmh.patch'}"),
                    twoCopies, new List<string> { "ludeon.rimworld", "nova.rimworldtogether_steam", "knappe.kmh.patch" });
                bool keepsWorkshop = copies.Ok && copies.Order.Contains("nova.rimworldtogether_steam") && !copies.Order.Contains("nova.rimworldtogether")
                                  && copies.WillEnable.Count == 0 && copies.WillDisable.Count == 0;
                r.Add(("mod plan: with a local and a Workshop copy of one mod, the copy in use is kept", keepsWorkshop,
                       keepsWorkshop ? "Workshop copy kept" : $"ok={copies.Ok} ({copies.Blocked}) order={string.Join(",", copies.Order.ToArray())}"));

                var withKeep = new List<Features.Mods.InstalledMod> { Mod("Core", "ludeon.rimworld", true) };
                withKeep.AddRange(KeepMods());
                var missing = Features.Mods.KmhModPlan.Build(Profile(ServerList), withKeep,
                    new List<string> { "ludeon.rimworld" });
                bool refusesMissing = !missing.Ok && missing.NotInstalled.Count == 1
                                   && missing.NotInstalled[0] == "Harmony" && missing.Order.Count == 0;
                r.Add(("mod plan: a required mod that is not installed blocks the plan instead of being skipped",
                       refusesMissing, refusesMissing ? "refused, and named" : $"ok={missing.Ok}"));

                var twoNamed = new List<Features.Mods.InstalledMod>
                {
                    Mod("Core", "ludeon.rimworld", true),
                    Mod("Harmony", "brrainz.harmony", false),
                    Mod("Harmony", "someone.fakeharmony", false),
                };
                twoNamed.AddRange(KeepMods());
                var ambiguous = Features.Mods.KmhModPlan.Build(Profile(ServerList), twoNamed,
                    new List<string> { "ludeon.rimworld" });
                bool refusesAmbiguity = !ambiguous.Ok && ambiguous.Blocked.Length > 0
                                     && ambiguous.Order.Count == 0;
                r.Add(("mod plan: two installed mods sharing a name is refused, never guessed", refusesAmbiguity,
                       refusesAmbiguity ? "refused" : "A MOD WAS PICKED AT RANDOM"));

                // RimWorld does not start without Core, and it must lead the list.
                var noCoreMods = new List<Features.Mods.InstalledMod> { Mod("Harmony", "brrainz.harmony", true) };
                noCoreMods.AddRange(KeepMods());
                var noCore = Features.Mods.KmhModPlan.Build(
                    Profile("{'name':'Harmony','role':'required','kind':'workshop','id':'2009463077'}," + Keep),
                    noCoreMods, new List<string> { "brrainz.harmony" });
                r.Add(("mod plan: a list without Core is refused rather than written", !noCore.Ok,
                       !noCore.Ok ? "refused" : "WOULD HAVE WRITTEN A LIST WITHOUT CORE"));

                var coreLateMods = new List<Features.Mods.InstalledMod>
                {
                    Mod("Harmony", "brrainz.harmony", true),
                    Mod("Core", "ludeon.rimworld", true),
                };
                coreLateMods.AddRange(KeepMods());
                var coreLate = Features.Mods.KmhModPlan.Build(
                    Profile("{'name':'Harmony','role':'required','kind':'workshop','id':'2009463077'},"
                          + "{'name':'Core','role':'required','kind':'local','id':'Core'}," + Keep),
                    coreLateMods, new List<string> { "brrainz.harmony", "ludeon.rimworld" });
                bool corePromoted = coreLate.Ok && coreLate.Order[0] == "ludeon.rimworld";
                r.Add(("mod plan: Core leads the written list whatever order the server sent", corePromoted,
                       corePromoted ? "Core first" : $"first is {(coreLate.Order.Count > 0 ? coreLate.Order[0] : "-")}"));

                // A server that publishes no entries publishes no packageIds, and a display name is not one.
                var noEntries = Features.Mods.KmhModPlan.Build(
                    Features.Global.ModProfileDto.Parse(Newtonsoft.Json.Linq.JObject.Parse(
                        "{'server_id':'" + new string('8', 32) + "','policy':'Enforced',"
                      + "'required':['Core','Harmony']}")),
                    new List<Features.Mods.InstalledMod> { Mod("Core", "ludeon.rimworld", true) },
                    new List<string> { "ludeon.rimworld" });
                r.Add(("mod plan: a server publishing no entries cannot produce a load order", !noEntries.Ok,
                       !noEntries.Ok ? "refused" : "BUILT AN ORDER FROM NAMES ALONE"));

                // RWT refuses a mod the server does not list, so it is turned off rather than carried into the written list.
                var bannedMods = new List<Features.Mods.InstalledMod>
                {
                    Mod("Core", "ludeon.rimworld", true),
                    Mod("Cheats", "someone.cheats", true),
                };
                bannedMods.AddRange(KeepMods());
                var banned = Features.Mods.KmhModPlan.Build(
                    Profile("{'name':'Core','role':'required','kind':'local','id':'Core'}," + Keep),
                    bannedMods, new List<string> { "ludeon.rimworld", "someone.cheats" });
                bool bannedOut = banned.Ok && !banned.Order.Contains("someone.cheats")
                              && banned.WillDisable.Contains("Cheats");
                r.Add(("mod plan: a mod the server does not allow is turned off and never written into the list",
                       bannedOut, bannedOut ? "excluded and disabled" : $"ok={banned.Ok} order={banned.Order.Count}"));

                var alreadyMods = new List<Features.Mods.InstalledMod>
                {
                    Mod("Core", "ludeon.rimworld", true),
                    Mod("Harmony", "brrainz.harmony", true),
                };
                alreadyMods.AddRange(KeepMods());
                var already = Features.Mods.KmhModPlan.Build(Profile(ServerList), alreadyMods,
                    new List<string> { "ludeon.rimworld", "brrainz.harmony",
                                       "nova.rimworldtogether", "knappe.kmh.patch" });
                bool noop = already.Ok && !already.Changes;
                r.Add(("mod plan: a list that already matches asks for no change", noop,
                       noop ? "no change" : $"enable={already.WillEnable.Count} disable={already.WillDisable.Count} reordered={already.Reordered}"));

                // The same mods in the wrong order is still a change, and the only signal RWT's order check has.
                var wrongMods = new List<Features.Mods.InstalledMod>
                {
                    Mod("Core", "ludeon.rimworld", true),
                    Mod("Harmony", "brrainz.harmony", true),
                };
                wrongMods.AddRange(KeepMods());
                var wrongOrder = Features.Mods.KmhModPlan.Build(Profile(ServerList), wrongMods,
                    new List<string> { "brrainz.harmony", "ludeon.rimworld",
                                       "nova.rimworldtogether", "knappe.kmh.patch" });
                bool reorder = wrongOrder.Ok && wrongOrder.Reordered && wrongOrder.Changes
                            && wrongOrder.WillEnable.Count == 0 && wrongOrder.WillDisable.Count == 0;
                r.Add(("mod plan: the same mods in a different order counts as a change", reorder,
                       reorder ? "reorder only" : $"reordered={wrongOrder.Reordered}"));

                var byIdMods = new List<Features.Mods.InstalledMod>
                {
                    Mod("Core", "ludeon.rimworld", true),
                    Mod("Harmony", "brrainz.harmony", false),
                };
                byIdMods.AddRange(KeepMods());
                var byId = Features.Mods.KmhModPlan.Build(
                    Profile("{'name':'Core','role':'required','kind':'local','id':'Core','package_id':'ludeon.rimworld'},"
                          + "{'name':'Renamed','role':'required','kind':'workshop','id':'2009463077','package_id':'brrainz.harmony'}," + Keep),
                    byIdMods, new List<string> { "ludeon.rimworld" });
                bool exact = byId.Ok && byId.Order.Count == 4 && byId.Order[1] == "brrainz.harmony";
                r.Add(("mod plan: a published packageId identifies the mod even when the name differs", exact,
                       exact ? "matched by id" : $"ok={byId.Ok} {byId.Blocked}"));

                // Entry order is RWT's listing order, not a load order: without a published one the player's own survives.
                var keepsMine = Features.Mods.KmhModPlan.Build(
                    Profile("{'name':'Harmony','role':'required','kind':'workshop','id':'2009463077'},"
                          + "{'name':'Core','role':'required','kind':'local','id':'Core'},"
                          + "{'name':'Nice','role':'required','kind':'workshop','id':'1095331071'}," + Keep),
                    new List<Features.Mods.InstalledMod>
                    {
                        Mod("Core", "ludeon.rimworld", true),
                        Mod("RimWorld Together", "nova.rimworldtogether", true),
                        Mod("KMH: Colony Nexus", "knappe.kmh.patch", true),
                        Mod("Harmony", "brrainz.harmony", true),
                        Mod("Nice", "someone.nice", false),
                    },
                    // Deliberately not the entry order: Harmony last, and the server lists it first.
                    new List<string> { "ludeon.rimworld", "nova.rimworldtogether",
                                       "knappe.kmh.patch", "brrainz.harmony" });
                bool mine = keepsMine.Ok
                         && keepsMine.Order.Count == 5
                         && keepsMine.Order[0] == "ludeon.rimworld"
                         && keepsMine.Order[1] == "nova.rimworldtogether"
                         && keepsMine.Order[2] == "knappe.kmh.patch"
                         && keepsMine.Order[3] == "brrainz.harmony"
                         && keepsMine.Order[4] == "someone.nice";
                r.Add(("mod plan: with no published order, the player's own load order survives", mine,
                       mine ? "kept, new mod last" : string.Join(" ", keepsMine.Order.ToArray())));

                var published = Features.Global.ModProfileDto.Parse(Newtonsoft.Json.Linq.JObject.Parse(
                    "{'server_id':'" + new string('6', 32) + "','policy':'Enforced',"
                  + "'order':['ludeon.rimworld','brrainz.harmony','nova.rimworldtogether','knappe.kmh.patch'],"
                  + "'entries':[{'name':'Harmony','role':'required','kind':'workshop','id':'2009463077'},"
                  + "{'name':'Core','role':'required','kind':'local','id':'Core'}," + Keep + "]}"));
                var usesPublished = Features.Mods.KmhModPlan.Build(published,
                    new List<Features.Mods.InstalledMod>
                    {
                        Mod("Core", "ludeon.rimworld", true),
                        Mod("RimWorld Together", "nova.rimworldtogether", true),
                        Mod("KMH: Colony Nexus", "knappe.kmh.patch", true),
                        Mod("Harmony", "brrainz.harmony", true),
                    },
                    new List<string> { "ludeon.rimworld", "nova.rimworldtogether",
                                       "knappe.kmh.patch", "brrainz.harmony" });
                bool wins = usesPublished.Ok && usesPublished.Order.Count == 4
                         && usesPublished.Order[1] == "brrainz.harmony"
                         && usesPublished.Reordered;
                r.Add(("mod plan: a server's own published load order wins over the player's", wins,
                       wins ? "server order applied" : string.Join(" ", usesPublished.Order.ToArray())));

                // A published packageId can only SELECT a local mod; it never puts its own text into ModsConfig.xml.
                var hostile = Features.Mods.KmhModPlan.Build(
                    Profile("{'name':'Core','role':'required','kind':'local','id':'Core',"
                          + "'package_id':'../../../etc/passwd'},"
                          + "{'name':'Harmony','role':'required','kind':'workshop','id':'2009463077',"
                          + "'package_id':'<script>evil</script>'}," + Keep),
                    new List<Features.Mods.InstalledMod>
                    {
                        Mod("Core", "ludeon.rimworld", true),
                        Mod("Harmony", "brrainz.harmony", true),
                        Mod("RimWorld Together", "nova.rimworldtogether", true),
                        Mod("KMH: Colony Nexus", "knappe.kmh.patch", true),
                    },
                    new List<string> { "ludeon.rimworld", "brrainz.harmony",
                                       "nova.rimworldtogether", "knappe.kmh.patch" });
                bool onlyLocal = true;
                var local = new List<string> { "ludeon.rimworld", "brrainz.harmony",
                                               "nova.rimworldtogether", "knappe.kmh.patch" };
                foreach (string id in hostile.Order) if (!local.Contains(id)) onlyLocal = false;
                r.Add(("mod plan: only packageIds this machine already has are ever written", onlyLocal,
                       onlyLocal ? "nothing from the wire reaches the file"
                                 : "A PUBLISHED STRING REACHED ModsConfig.xml"));

                // Applying a list without these is not recoverable from inside the game.
                var wouldBrick = Features.Mods.KmhModPlan.Build(
                    Profile("{'name':'Core','role':'required','kind':'local','id':'Core'}"),
                    new List<Features.Mods.InstalledMod>
                    {
                        Mod("Core", "ludeon.rimworld", true),
                        Mod("RimWorld Together", "nova.rimworldtogether", true),
                        Mod("KMH: Colony Nexus", "knappe.kmh.patch", true),
                    },
                    new List<string> { "ludeon.rimworld" });
                r.Add(("mod plan: a list that would switch off RimWorld Together or KMH is refused",
                       !wouldBrick.Ok && wouldBrick.Order.Count == 0,
                       !wouldBrick.Ok ? "refused" : "WOULD HAVE DISABLED THE MOD IT IS RUNNING IN"));

                // What Core, RWT and KMH need to load is protected with them, or the list cannot be undone in game.
                Features.Mods.KmhModPlan.DependenciesForTest = () => new[] { "brrainz.harmony" };
                var four = new List<Features.Mods.InstalledMod> { Mod("Harmony", "brrainz.harmony", true), Mod("Core", "ludeon.rimworld", true) };
                four.AddRange(KeepMods());
                var noHarmony = Features.Mods.KmhModPlan.ForOrder(new List<string> { "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch" }, four);
                var withHarmony = Features.Mods.KmhModPlan.ForOrder(new List<string> { "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch" }, four);
                Features.Mods.KmhModPlan.DependenciesForTest = () => new string[0];
                bool depsKept = !noHarmony.Ok && (noHarmony.Blocked ?? "").Contains("brrainz.harmony") && withHarmony.Ok;
                r.Add(("mod plan: a server's order leaving out what Colony Nexus or RimWorld Together needs is refused", depsKept,
                       depsKept ? "refused" : $"without Harmony ok={noHarmony.Ok} ({noHarmony.Blocked}), with ok={withHarmony.Ok}"));

                // An optional mod is switched on only by the key the player ticked, not a name the server put on another item.
                var optional = new List<Features.Mods.InstalledMod> { Mod("Core", "ludeon.rimworld", true), Mod("Harmony", "brrainz.harmony", true), Mod("Nice", "someone.nice", false) };
                optional.AddRange(KeepMods());
                var viaName = Features.Mods.KmhModPlan.Build(Profile(ServerList), optional, new List<string> { "ludeon.rimworld", "brrainz.harmony" }, new List<string> { "Nice" });
                var viaId = Features.Mods.KmhModPlan.Build(Profile(ServerList), optional, new List<string> { "ludeon.rimworld", "brrainz.harmony" }, new List<string> { "1095331071" });
                bool exactKey = viaName.Ok && !viaName.WillEnable.Contains("Nice") && viaId.Ok && viaId.WillEnable.Contains("Nice");
                r.Add(("mod plan: an optional mod is switched on only by the exact key the player ticked", exactKey,
                       exactKey ? "id ticked, name ignored" : $"by name enables={string.Join(",", viaName.WillEnable.ToArray())}, by id={string.Join(",", viaId.WillEnable.ToArray())}"));

                // The live Fantasy entry (RWT publishes no packageIds): RoM's 1.7 continuation kept its 1.6 original's, and RimWorld loads either.
                var rom = new Features.Global.ModEntryDto { Name = "A RimWorld of Magic (Continued)", Kind = Features.Global.ModKindNames.Workshop, Id = "3823345404" };
                var romNamed = new Features.Global.ModEntryDto { Name = rom.Name, Kind = rom.Kind, Id = rom.Id, PackageId = "Torann.ARimworldOfMagic" };
                Features.Global.ModProfileDto RomList(Features.Global.ModEntryDto e) => new Features.Global.ModProfileDto
                    { Policy = Features.Global.ModPolicyNames.Enforced, Required = new List<string> { e.Name }, Entries = new List<Features.Global.ModEntryDto> { e } };
                var original = new Features.Mods.InstalledMod { Name = "A RimWorld of Magic", PackageId = "Torann.ARimworldOfMagic", WorkshopId = "1201382956", Active = true };
                var originalIdle = new Features.Mods.InstalledMod { Name = original.Name, PackageId = original.PackageId, WorkshopId = original.WorkshopId };
                var handCopy = new Features.Mods.InstalledMod { Name = rom.Name, PackageId = "Torann.ARimworldOfMagic", WorkshopId = "RoM-Continued-3823345404", Active = true };
                Features.Global.ModComparison Against(Features.Global.ModEntryDto e, params Features.Mods.InstalledMod[] mods)
                {
                    var running = new List<string>();
                    foreach (Features.Mods.InstalledMod m in mods) if (m.Active) running.Add(m.Name);
                    return Features.Global.ModComparison.Compare(RomList(e), running, new List<Features.Mods.InstalledMod>(mods));
                }
                Features.Global.ModComparison onlyOriginal, bothCopies, namedOnlyOriginal, namedByHand;
                try
                {
                    // RimWorld lists only the first copy of a packageId: with both subscribed, the continuation is on disk, unlisted.
                    Features.Global.KmhLocalMods.PackageOnDiskForTest = id => null;
                    onlyOriginal = Against(rom, original);
                    namedOnlyOriginal = Against(romNamed, original);
                    namedByHand = Against(romNamed, handCopy, originalIdle);
                    Features.Global.KmhLocalMods.PackageOnDiskForTest = id => id == rom.Id ? "Torann.ARimworldOfMagic" : null;
                    bothCopies = Against(rom, original);
                }
                finally { Features.Global.KmhLocalMods.PackageOnDiskForTest = null; }
                // Until the server's copy is on disk its packageId is unknown, so it is downloaded and the clash found after.
                bool downloads = onlyOriginal.MissingWorkshop.Count == 1 && onlyOriginal.Shadowing.Count == 0
                              && namedOnlyOriginal.MissingWorkshop.Count == 1 && namedOnlyOriginal.Shadowing.Count == 1;
                bool unsubscribes = bothCopies.MissingWorkshop.Count == 1 && bothCopies.Shadowing.Count == 1 && bothCopies.Shadowing[0].Copy == original;
                bool localWins = namedByHand.Shadowing.Count == 0 && !namedByHand.Blocking;
                r.Add(("mod plan: another Workshop item with a server mod's packageId is never taken for it, and is marked to go",
                       downloads && unsubscribes && localWins,
                       $"only the original: subscribe {onlyOriginal.MissingWorkshop.Count}/{namedOnlyOriginal.MissingWorkshop.Count}, drop {onlyOriginal.Shadowing.Count}/{namedOnlyOriginal.Shadowing.Count}; "
                     + $"both subscribed: subscribe {bothCopies.MissingWorkshop.Count}, drop {bothCopies.Shadowing.Count}; "
                     + $"hand-installed: drop {namedByHand.Shadowing.Count}, blocking={namedByHand.Blocking}"));

                // Prepare unsubscribes only what the player was shown; a clash found after the download waits for them.
                var unasked = Features.Mods.KmhJoinPreparation.Unasked(bothCopies.Shadowing, new List<string>());
                var shown = Features.Mods.KmhJoinPreparation.Unasked(bothCopies.Shadowing, Features.Global.Dialog_KMHPrepareJoin.ShownToGo(bothCopies));
                r.Add(("prepare: a Workshop item is unsubscribed only once the player has seen it listed", unasked.Count == 1 && shown.Count == 0,
                       $"not shown: {unasked.Count} waiting; shown in the review: {shown.Count} waiting"));
            }
            catch (Exception ex) { r.Add(("mod plan: the planning rules run outside the game", false, ex.Message)); }
            finally { Features.Mods.KmhModPlan.DependenciesForTest = null; }
        }

        // Harmony runs hook-named statics on every class it is handed: a stray TargetMethod plus Prefix patches RWT twice.
        private static void NoAccidentalHarmonyHooks(List<(string, bool, string)> r)
        {
            // No HarmonyLib here: the self-test host has no 0Harmony, and naming it throws the whole suite.
            var hooks = new HashSet<string>(StringComparer.Ordinal)
                { "Prepare", "Cleanup", "TargetMethod", "TargetMethods", "Prefix", "Postfix", "Transpiler", "Finalizer", "ReversePatch", "ILManipulator" };
            var bad = new List<string>();
            Type[] types;
            try { types = typeof(KmhSelfTest).Assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { types = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(ex.Types, x => x != null)); }
            int scanned = 0;
            foreach (Type t in types)
            {
                bool patchClass;
                try { patchClass = System.Linq.Enumerable.Any(t.GetCustomAttributesData(), d => d.AttributeType.Name.StartsWith("Harmony", StringComparison.Ordinal)); }
                catch { patchClass = true; }
                if (patchClass) continue;
                scanned++;
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                                               | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    if (hooks.Contains(m.Name)) bad.Add(t.FullName + "." + m.Name);
            }
            bool ok = bad.Count == 0 && scanned > 100;
            r.Add(("patching: no ordinary class declares a static Prepare or Prefix-style method, which Harmony runs on every class", ok,
                   bad.Count > 0 ? string.Join(", ", bad.ToArray()) : $"{scanned} class(es) checked"));

            // An RWT signature change (PM_Version.Send losing its argument) skips a patch class with a single warning.
            string[] skipped;
            lock (KmhEntry.SkippedPatchClasses) skipped = KmhEntry.SkippedPatchClasses.ToArray();
            r.Add(("patching: every patch class fits the RimWorld Together that is loaded", skipped.Length == 0,
                   skipped.Length > 0 ? string.Join(", ", skipped) : ""));
        }

        private static void JoinPreparationRules(List<(string, bool, string)> r)
        {
            try
            {
                DateTime t0 = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
                TimeSpan grace = Features.Mods.KmhJoinPreparation.SubscribeGrace;
                Features.Mods.KmhJoinPreparation.WorkshopStep Act(Features.Mods.WorkshopItemState s, DateTime? at, DateTime now)
                    => Features.Mods.KmhJoinPreparation.WorkshopAction(s, at, now, grace);
                bool steps = Act(Features.Mods.WorkshopItemState.NotSubscribed, null, t0) == Features.Mods.KmhJoinPreparation.WorkshopStep.Subscribe
                          && Act(Features.Mods.WorkshopItemState.NotSubscribed, t0, t0.AddSeconds(10)) == Features.Mods.KmhJoinPreparation.WorkshopStep.WaitSubscribe
                          && Act(Features.Mods.WorkshopItemState.NotSubscribed, t0, t0 + grace + TimeSpan.FromSeconds(1)) == Features.Mods.KmhJoinPreparation.WorkshopStep.NotAccepted
                          && Act(Features.Mods.WorkshopItemState.Queued, t0, t0) == Features.Mods.KmhJoinPreparation.WorkshopStep.RequestDownload
                          && Act(Features.Mods.WorkshopItemState.NeedsUpdate, null, t0) == Features.Mods.KmhJoinPreparation.WorkshopStep.RequestDownload
                          && Act(Features.Mods.WorkshopItemState.Downloading, null, t0) == Features.Mods.KmhJoinPreparation.WorkshopStep.Downloading
                          && Act(Features.Mods.WorkshopItemState.Installed, null, t0) == Features.Mods.KmhJoinPreparation.WorkshopStep.Done
                          && Act(Features.Mods.WorkshopItemState.Unavailable, null, t0) == Features.Mods.KmhJoinPreparation.WorkshopStep.Unavailable;
                r.Add(("prepare: each Workshop state leads to exactly one next step, and a subscription Steam never takes fails after its grace",
                       steps, steps ? "" : "WRONG STEP"));

                var expected = new List<string> { "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch" };
                bool clean = Features.Mods.KmhJoinPreparation.VerifyLoaded(expected,
                    new List<string> { "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether_steam", "knappe.kmh.patch" }, "", "26.8.31.1").Count == 0;
                var missing = Features.Mods.KmhJoinPreparation.VerifyLoaded(expected,
                    new List<string> { "brrainz.harmony", "ludeon.rimworld", "knappe.kmh.patch" }, "", "26.8.31.1");
                var oldRwt = Features.Mods.KmhJoinPreparation.VerifyLoaded(null, null, "26.9.1.1", "26.8.31.1");
                bool verify = clean && missing.Count == 1 && missing[0].Contains("nova.rimworldtogether") && oldRwt.Count == 1;
                r.Add(("prepare: after the restart a mod that did not load, or the old RWT still running, is named", verify,
                       $"clean={clean} missing={missing.Count} rwt={oldRwt.Count}"));

                bool loop = !Features.Mods.KmhJoinPreparation.ListStillNeedsRestart(true, false)
                         && Features.Mods.KmhJoinPreparation.ListStillNeedsRestart(true, true)
                         && !Features.Mods.KmhJoinPreparation.ListStillNeedsRestart(false, false);
                r.Add(("prepare: a mod list written before the restart is checked, not restarted for again", loop,
                       loop ? "" : "RESTART LOOP"));

                bool rwtOnce = Features.Mods.KmhJoinPreparation.RwtStagedButNotLoaded(false, true)
                            && !Features.Mods.KmhJoinPreparation.RwtStagedButNotLoaded(true, true)
                            && !Features.Mods.KmhJoinPreparation.RwtStagedButNotLoaded(false, false);
                r.Add(("prepare: an RWT version staged before the restart that did not load fails instead of restarting again", rwtOnce,
                       rwtOnce ? "" : "RESTART LOOP"));

                bool resume = Features.Mods.KmhJoinPreparation.ResumesFailed(false, "could not activate the new copy")
                           && !Features.Mods.KmhJoinPreparation.ResumesFailed(true, "KMH could not restart RimWorld")
                           && !Features.Mods.KmhJoinPreparation.ResumesFailed(false, "");
                r.Add(("prepare: a job that failed before RimWorld closed comes back failed instead of retrying unasked", resume,
                       resume ? "" : "RETRIED UNASKED"));

                bool counted = Features.Mods.KmhJoinPreparation.DownloadedCount(0, 1) == 1
                            && Features.Mods.KmhJoinPreparation.DownloadedCount(2, 0) == 2;
                r.Add(("prepare: the finished Workshop line counts what this job downloaded, after the rescan as before it", counted,
                       counted ? "" : "COUNTS ZERO"));

                var order = new List<string> { "ludeon.rimworld", "nova.rimworldtogether_steam", "knappe.kmh.patch" };
                List<string> kept = Features.Mods.KmhJoinPreparation.RepointRwt(order, false);
                List<string> moved = Features.Mods.KmhJoinPreparation.RepointRwt(order, true);
                bool repoint = kept[1] == "nova.rimworldtogether_steam" && moved[1] == "nova.rimworldtogether" && moved.Count == 3 && moved[0] == "ludeon.rimworld";
                r.Add(("prepare: after installing RWT locally the list names the local copy, not the old Workshop one", repoint,
                       repoint ? "" : string.Join(",", moved.ToArray())));

                // Consent covers the plan that was shown: a server that changes it afterwards is not followed.
                var jobField = typeof(Features.Mods.KmhJoinPreparation).GetField("_job", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                object priorJob = jobField.GetValue(null);
                try
                {
                    Features.Global.ModProfileDto Approved(string id) => Features.Global.ModProfileDto.Parse(Newtonsoft.Json.Linq.JObject.Parse(
                        "{'server_id':'" + new string('7', 32) + "','policy':'Enforced','required':['Harmony'],"
                      + "'entries':[{'name':'Harmony','role':'required','kind':'workshop','id':'" + id + "'}]}"));
                    var shown = Approved("2009463077");
                    var card = new Features.Global.ServerCardDto { RwtVersion = "26.8.31.1", ModPolicy = "Enforced" };
                    jobField.SetValue(null, new Features.Mods.KmhPrepareJob
                    {
                        Approved = true, ApprovedPolicy = "Enforced", RwtVersion = "26.8.31.1",
                        ApprovedProfile = Features.Mods.KmhJoinPreparation.ProfileDigest(shown),
                    });
                    bool asShown = Features.Mods.KmhJoinPreparation.ChangedSinceApproval(card, true, Approved("2009463077")) == null;
                    bool swapped = Features.Mods.KmhJoinPreparation.ChangedSinceApproval(card, true, Approved("1111111111")) != null;
                    card.RwtVersion = "26.9.1.1";
                    bool otherRwt = Features.Mods.KmhJoinPreparation.ChangedSinceApproval(card, true, shown) != null;
                    card.RwtVersion = "26.8.31.1"; card.ModPolicy = "AllowAll";
                    bool otherPolicy = Features.Mods.KmhJoinPreparation.ChangedSinceApproval(card, false, shown) != null;
                    card.ModPolicy = "Enforced";
                    // An older build's job: the same plan in every field, but never approved by this build's review.
                    jobField.SetValue(null, new Features.Mods.KmhPrepareJob
                    {
                        RwtVersion = "26.8.31.1", ApprovedPolicy = "Enforced",
                        ApprovedProfile = Features.Mods.KmhJoinPreparation.ProfileDigest(shown),
                    });
                    bool unapproved = Features.Mods.KmhJoinPreparation.ChangedSinceApproval(card, true, shown) != null;
                    bool bound = asShown && swapped && otherRwt && otherPolicy && unapproved;
                    r.Add(("prepare: a server that changes its list, rules or RWT version after the player agreed is not followed", bound,
                           bound ? "" : $"as shown={asShown} swapped item={swapped} rwt={otherRwt} rules={otherPolicy} older job={unapproved}"));
                }
                finally { jobField.SetValue(null, priorJob); }

                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmh-prep-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                System.IO.Directory.CreateDirectory(dir);
                string prior = Features.Mods.KmhPrepareJobStore.PathOverrideForTest;
                try
                {
                    Features.Mods.KmhPrepareJobStore.PathOverrideForTest = System.IO.Path.Combine(dir, "join-preparation.json");
                    var job = new Features.Mods.KmhPrepareJob { ServerId = new string('b', 32), ServerName = "Srv", Stage = Features.Mods.KmhPrepareStage.Restarting, Restarts = 1,
                                                                 ExpectedOrder = new List<string> { "ludeon.rimworld" }, ExpectedRwt = "26.9.1.1", ListWritten = true };
                    bool saved = Features.Mods.KmhPrepareJobStore.Save(job, t0);
                    var back = Features.Mods.KmhPrepareJobStore.Load(t0.AddHours(1));
                    bool round = saved && back != null && back.Stage == Features.Mods.KmhPrepareStage.Restarting && back.Restarts == 1
                              && back.ExpectedRwt == "26.9.1.1" && back.ListWritten && back.ExpectedOrder.Count == 1;
                    var stale = Features.Mods.KmhPrepareJobStore.Load(t0 + Features.Mods.KmhPrepareJobStore.MaxAge + TimeSpan.FromMinutes(1));
                    bool gone = stale == null && !System.IO.File.Exists(Features.Mods.KmhPrepareJobStore.PathOverrideForTest);
                    System.IO.File.WriteAllText(Features.Mods.KmhPrepareJobStore.PathOverrideForTest, "{ not json");
                    bool junk = Features.Mods.KmhPrepareJobStore.Load(t0) == null;
                    r.Add(("prepare: the record survives a restart, and an old or unreadable one is dropped rather than acted on", round && gone && junk,
                           $"round={round} expired={gone} unreadable={junk}"));
                }
                finally
                {
                    Features.Mods.KmhPrepareJobStore.PathOverrideForTest = prior;
                    try { System.IO.Directory.Delete(dir, true); } catch { }
                }

                // An owned expansion that is switched off is a mod-list fix, not a purchase.
                var profile = new Features.Global.ModProfileDto { Policy = Features.Global.ModPolicyNames.Enforced };
                profile.Required.Add("Biotech"); profile.Required.Add("Core");
                profile.Entries.Add(new Features.Global.ModEntryDto { Name = "Biotech", PackageId = "ludeon.rimworld.biotech", Kind = Features.Global.ModKindNames.Expansion, Role = Features.Global.ModRoleNames.Required });
                profile.Entries.Add(new Features.Global.ModEntryDto { Name = "Core", PackageId = "ludeon.rimworld", Kind = Features.Global.ModKindNames.Expansion, Role = Features.Global.ModRoleNames.Required });
                var owned = new List<Features.Mods.InstalledMod>
                {
                    new Features.Mods.InstalledMod { Name = "Core", PackageId = "ludeon.rimworld", Active = true, Official = true },
                    new Features.Mods.InstalledMod { Name = "Biotech", PackageId = "ludeon.rimworld.biotech", Active = false, Official = true },
                };
                Features.Global.ModComparison withIt = Features.Global.ModComparison.Compare(profile, new List<string> { "Core" }, owned);
                Features.Global.ModComparison without = Features.Global.ModComparison.Compare(profile, new List<string> { "Core" }, owned.GetRange(0, 1));
                bool expansion = withIt.MissingDisabled.Count == 1 && withIt.MissingExpansions.Count == 0 && !withIt.BlockedOnlyByExpansions
                              && without.MissingExpansions.Count == 1 && without.BlockedOnlyByExpansions;
                r.Add(("browser: an expansion you own but switched off is offered as a fix, one you do not own as a purchase", expansion,
                       $"owned: disabled={withIt.MissingDisabled.Count} buy={withIt.MissingExpansions.Count}; not owned: buy={without.MissingExpansions.Count}"));

                var opt = new Features.Global.ModProfileDto { Policy = Features.Global.ModPolicyNames.Enforced };
                foreach (var e in new[] { ("Core", "ludeon.rimworld", Features.Global.ModRoleNames.Required), ("RimWorld Together", "nova.rimworldtogether", Features.Global.ModRoleNames.Required),
                                          ("KMH", Constants.PackageId, Features.Global.ModRoleNames.Required), ("Pretty", "x.pretty", Features.Global.ModRoleNames.Optional) })
                    opt.Entries.Add(new Features.Global.ModEntryDto { Name = e.Item1, PackageId = e.Item2, Id = e.Item2 == "x.pretty" ? "3000000001" : "", Role = e.Item3, Kind = Features.Global.ModKindNames.Workshop });
                var have = new List<Features.Mods.InstalledMod>
                {
                    new Features.Mods.InstalledMod { Name = "Core", PackageId = "ludeon.rimworld", Active = true },
                    new Features.Mods.InstalledMod { Name = "RimWorld Together", PackageId = "nova.rimworldtogether", Active = true },
                    new Features.Mods.InstalledMod { Name = "KMH", PackageId = Constants.PackageId, Active = true },
                    new Features.Mods.InstalledMod { Name = "Pretty", PackageId = "x.pretty", Active = false },
                };
                var active = new List<string> { "ludeon.rimworld", "nova.rimworldtogether", Constants.PackageId };
                // These fixtures carry no Harmony; the dependency rule has its own check in the plan tests.
                Features.Mods.KmhModPlan.DependenciesForTest = () => new string[0];
                var plain = Features.Mods.KmhModPlan.Build(opt, have, active);
                var chosen = Features.Mods.KmhModPlan.Build(opt, have, active, new List<string> { "3000000001" });
                bool optional = plain.Ok && !plain.Order.Contains("x.pretty") && chosen.Ok && chosen.Order.Contains("x.pretty") && chosen.WillEnable.Contains("Pretty");
                r.Add(("prepare: an optional mod is switched on only when the player ticked it", optional,
                       $"untouched={!plain.Order.Contains("x.pretty")} ticked={chosen.Order.Contains("x.pretty")}"));

                // Power Tab renamed itself for 1.7; a server listing the old name still matches it by Workshop id.
                Features.Global.ModProfileDto Listed(params (string name, string id)[] extra)
                {
                    var p = new Features.Global.ModProfileDto { Policy = Features.Global.ModPolicyNames.Enforced };
                    foreach (var e in opt.Entries.GetRange(0, 3)) { p.Required.Add(e.Name); p.Entries.Add(e); }
                    foreach (var (name, id) in extra)
                    {
                        p.Required.Add(name);
                        p.Entries.Add(new Features.Global.ModEntryDto { Name = name, Id = id, Role = Features.Global.ModRoleNames.Required, Kind = Features.Global.ModKindNames.Workshop });
                    }
                    return p;
                }
                List<Features.Mods.InstalledMod> With(bool on, string workshopId)
                {
                    var list = have.GetRange(0, 3);
                    list.Add(new Features.Mods.InstalledMod { Name = "Power Tab (1.7)", PackageId = "x.powertab", WorkshopId = workshopId, Active = on });
                    return list;
                }
                var tab = Listed(("Power Tab", "3528086894"));
                var running = new List<string> { "Core", "RimWorld Together", "KMH", "Power Tab (1.7)" };
                var keptPlan = Features.Mods.KmhModPlan.Build(tab, With(true, "3528086894"), active);
                var offPlan = Features.Mods.KmhModPlan.Build(tab, With(false, "3528086894"), active);
                var onCmp = Features.Global.ModComparison.Compare(tab, running, With(true, "3528086894"));
                var offCmp = Features.Global.ModComparison.Compare(tab, running.GetRange(0, 3), With(false, "3528086894"));
                // A local mod has no Workshop id, so it never takes the one listed entry that has none either.
                var localList = new Features.Global.ModProfileDto { Policy = Features.Global.ModPolicyNames.Enforced };
                localList.Required.Add("Power Tab");
                localList.Entries.Add(new Features.Global.ModEntryDto { Name = "Power Tab", Role = Features.Global.ModRoleNames.Required, Kind = Features.Global.ModKindNames.Workshop });
                var localCmp = Features.Global.ModComparison.Compare(localList, running, With(true, ""));
                var sharedCmp = Features.Global.ModComparison.Compare(Listed(("Power Tab", "3528086894"), ("Power Tab Old", "3528086894")), running, With(true, "3528086894"));
                var takenCmp = Features.Global.ModComparison.Compare(tab, new List<string>(running) { "Power Tab" }, With(true, "3528086894"));
                // A local copy beside the Workshop one: the copy running now is the one kept.
                var twoCopies = With(true, "3528086894");
                twoCopies.Insert(3, new Features.Mods.InstalledMod { Name = "Power Tab (1.7)", PackageId = "x.powertab.local", WorkshopId = "3528086894", Active = false });
                var twoPlan = Features.Mods.KmhModPlan.Build(tab, twoCopies, active);
                bool renamed = keptPlan.Ok && keptPlan.Order.Contains("x.powertab") && !keptPlan.WillDisable.Contains("Power Tab (1.7)")
                            && offPlan.Ok && offPlan.WillEnable.Contains("Power Tab (1.7)")
                            && !onCmp.Blocking && offCmp.MissingDisabled.Count == 1 && offCmp.MissingWorkshop.Count == 0
                            && localCmp.Missing.Contains("Power Tab") && localCmp.Disallowed.Contains("Power Tab (1.7)") && sharedCmp.Disallowed.Contains("Power Tab (1.7)")
                            && takenCmp.Disallowed.Contains("Power Tab (1.7)")
                            && twoPlan.Ok && twoPlan.Order.Contains("x.powertab") && !twoPlan.Order.Contains("x.powertab.local");
                r.Add(("prepare: a Workshop mod renamed in its 1.7 update is the server's listed mod, by Workshop id, in the plan and the browser", renamed,
                       $"plan kept={keptPlan.Ok && keptPlan.Order.Contains("x.powertab")} enables={offPlan.WillEnable.Contains("Power Tab (1.7)")} two={twoPlan.Ok}:{string.Join(",", twoPlan.Order)}; "
                     + $"browser missing={onCmp.Missing.Count} disallowed={onCmp.Disallowed.Count} off={offCmp.MissingDisabled.Count}/{offCmp.MissingWorkshop.Count}; "
                     + $"local={localCmp.Missing.Contains("Power Tab")}/{localCmp.Disallowed.Contains("Power Tab (1.7)")} shared={sharedCmp.Disallowed.Contains("Power Tab (1.7)")} taken={takenCmp.Disallowed.Contains("Power Tab (1.7)")}"));

                // One failed exact lookup is not "no such release" for the rest of the session.
                Features.Rwt.RwtReleaseCatalog.ResetForTest();
                Features.Rwt.RwtReleaseCatalog.FailExactForTest("26.1.1.1", DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5));
                bool notPending = !Features.Rwt.RwtReleaseCatalog.ExactPending("26.1.1.1");
                Features.Rwt.RwtReleaseCatalog.ResetForTest();
                r.Add(("rwt: a failed exact-version lookup reads as finished, so the player is told rather than left waiting", notPending, ""));
            }
            catch (Exception ex) { r.Add(("prepare: the preparation rules run outside the game", false, ex.ToString())); }
            finally { Features.Mods.KmhModPlan.DependenciesForTest = null; }
        }

        // An overlap in this row makes the primary action unclickable.
        private static void BrowserActionRow(List<(string, bool, string)> r)
        {
            try
            {
                const float Gap = 6f;
                // Favourite 126, Join 158 + gap; 1180 is the dialog's cap and 944 what a 1024-wide screen gives it.
                float SpanFor(float window)
                {
                    float listW = UnityEngine.Mathf.Floor(window * 0.42f);
                    float actions = (window - listW - 10f) - 24f;
                    return (actions - 158f - Gap) - 126f;
                }

                // 640 px is the details pane at UI scale 1.5 on 1280x720.
                bool fits = true;
                string worst = "";
                foreach (float window in new[] { 1180f, 1024f, 944f, 800f, 760f, 640f, 520f })
                    foreach (bool restore in new[] { false, true })
                    {
                        float span = SpanFor(window) + 126f + 158f + Gap;
                        Features.Global.Dialog_KMHServerBrowser.ActionLayout(span, restore, out float fav, out float rest, out float primary);
                        float used = fav + primary + (rest > 0f ? rest + Gap : 0f) + Gap;
                        if (fav <= 0f || primary <= 0f || rest < 0f || used > span + 0.01f)
                        {
                            fits = false;
                            worst = $"window {window} restore={restore}: used {used:0.#} of {span:0.#}";
                        }
                    }
                r.Add(("browser: Favourite, Restore and the primary button never overlap or leave the pane", fits,
                       fits ? "fits at every window size" : worst));

                Features.Global.Dialog_KMHServerBrowser.ActionLayout(SpanFor(1180f) + 126f + 158f + Gap, true, out float f1, out float r1, out float p1);
                bool roomy = r1 >= Features.Global.Dialog_KMHServerBrowser.RestoreMin && p1 >= Features.Global.Dialog_KMHServerBrowser.PrimaryMin;
                r.Add(("browser: at the default window the Restore button and a full-width primary both show", roomy, $"fav {f1:0} restore {r1:0} primary {p1:0}"));

                Features.Global.Dialog_KMHServerBrowser.ActionLayout(200f, true, out float f2, out float r2, out float p2);
                bool squeezed = r2 == 0f && f2 + p2 + Gap <= 200.01f;
                r.Add(("browser: with no room Restore is dropped first, never drawn over Join", squeezed, $"fav {f2:0} restore {r2:0} primary {p2:0}"));

                // Only what Prepare can clear is offered as Prepare; an expansion nobody owns is not.
                var card = new Features.Global.ServerCardDto { ServerId = new string('a', 32), ModPolicy = Features.Global.ModPolicyNames.AllowAll };
                bool mods = Features.Global.Dialog_KMHServerBrowser.Fixable(card, Features.Global.CompatState.ModsAction);
                bool rwt = Features.Global.Dialog_KMHServerBrowser.Fixable(card, Features.Global.CompatState.RwtUnsupported);
                bool expansion = Features.Global.Dialog_KMHServerBrowser.Fixable(card, Features.Global.CompatState.ModsNeedExpansion);
                bool full = Features.Global.Dialog_KMHServerBrowser.Fixable(card, Features.Global.CompatState.Full);
                bool fixOk = mods && rwt && !expansion && !full;
                r.Add(("browser: Prepare is offered for a mod list or RWT version, never for an expansion or a full server", fixOk,
                       $"mods={mods} rwt={rwt} expansion={expansion} full={full}"));

                // RWT servers report a bare "1.6", which must not read as different from every 1.6.x build.
                bool rw = !Features.Global.Dialog_KMHServerBrowser.RimWorldDiffers("1.6.4633 rev1261", "1.6.4633 rev590")
                       && Features.Global.Dialog_KMHServerBrowser.RimWorldDiffers("1.6.4633", "1.6.4871")
                       && !Features.Global.Dialog_KMHServerBrowser.RimWorldDiffers("1.6", "1.6.4871 rev590")
                       && Features.Global.Dialog_KMHServerBrowser.RimWorldDiffers("1.5", "1.6.4871 rev590")
                       && !Features.Global.Dialog_KMHServerBrowser.RimWorldDiffers("", "1.6.4871");
                r.Add(("browser: a different RimWorld build is named; a server naming only the major version, a different revision or an unknown one is not", rw, rw ? "" : "WRONG COMPARISON"));
            }
            catch (Exception ex) { r.Add(("browser: the action row layout runs outside the game", false, ex.Message)); }
        }

        // Shipping the previous release's notes tells players the protocol has not moved.
        private static void WhatsNewIsCurrent(List<(string, bool, string)> r)
        {
            // Matched as "proto... vN" so the same check holds in German and Spanish when it runs in game.
            string text = string.Join("\n", Dialogs.Dialog_KMHWhatsNew.Highlights());
            int wire = SubProtocol.KmhProtocol.CurrentVersion;
            string version = "v" + SubProtocol.KmhProtocol.DisplayVersion;
            bool current = System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?i)\bproto\w*\s+v{wire}\b") && text.Contains(version);
            bool stale = System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?i)\bproto\w*\s+v(?!{wire}\b)\d+\b")
                         || text.Contains("migrate automatically");
            r.Add(("what's new: describes this release and its protocol", current && !stale,
                   stale ? "STALE: an older release's notes" : current ? $"{version}, protocol v{wire}" : $"does not name {version} and protocol v{wire}"));
        }

        // Steam hands the connect string back two ways; a crafted one must be refused exactly like a Discord secret.
        private static void SteamJoinAccept(List<(string, bool, string)> r)
        {
            try
            {
                string id = new string('7', 32);
                bool roundTrip = Features.Discord.KmhSteamJoin.TryReadCommandLine(
                                     new[] { "RimWorldWin64.exe", "+kmh", id }, out string got) && got == id;
                r.Add(("steam: a Join Game launch argument is read back as the server it named", roundTrip,
                       roundTrip ? "parsed" : $"got '{got}'"));

                bool joined = Features.Discord.KmhSteamJoin.TryReadCommandLine(
                                  new[] { "RimWorldWin64.exe", "+kmh " + id }, out string got2) && got2 == id;
                r.Add(("steam: the same argument quoted as one string parses identically", joined,
                       joined ? "parsed" : $"got '{got2}'"));

                bool refuses = !Features.Discord.KmhSteamJoin.TryReadCommandLine(
                                   new[] { "+kmh", "../../etc/passwd" }, out _)
                            && !Features.Discord.KmhSteamJoin.TryReadCommandLine(
                                   new[] { "+kmh", new string('z', 32) }, out _)
                            && !Features.Discord.KmhSteamJoin.TryReadCommandLine(
                                   new[] { "+connect", "1.2.3.4:5000" }, out _)
                            && !Features.Discord.KmhSteamJoin.TryReadCommandLine(null, out _);
                r.Add(("steam: a connect string KMH did not issue is refused, never followed", refuses,
                       refuses ? "refused" : "A CRAFTED ARGUMENT WAS ACCEPTED"));

                // Only a 32-hex id is read from an invite; the endpoint comes from this client's own directory.
                bool noAddress = !Features.Discord.KmhSteamJoin.TryReadCommandLine(
                    new[] { "+kmh", "play.evil.example:5000" }, out _);
                r.Add(("steam: an invite can name a server, never an address to connect to", noAddress,
                       noAddress ? "ids only" : "AN ADDRESS WAS ACCEPTED"));

                bool withheld = Features.Discord.KmhSteamPresence.ConnectFor(null) == "";
                r.Add(("steam: nothing is advertised for a server that may not be advertised", withheld,
                       withheld ? "withheld" : "ADVERTISED"));
            }
            catch (Exception ex) { r.Add(("steam: the Join Game rules run outside the game", false, ex.Message)); }
        }

        private static void ListingPresentation(List<(string, bool, string)> r)
        {
            var row = new Newtonsoft.Json.Linq.JObject
            {
                ["server_id"]   = new string('b', 32),
                ["name"]        = "KMH | Vanilla | Official",
                ["description"] = "Vanilla RimWorld, no extra mods. Weekly caravan events.",
                ["discord_url"] = "https://discord.gg/example",
                ["website_url"] = "https://knappe0.games",
            };
            var body = new Newtonsoft.Json.Linq.JObject
                { ["servers"] = new Newtonsoft.Json.Linq.JArray { row } };

            List<Features.Global.ServerCardDto> cards = Features.Global.DirectoryParse.Cards(body, out _);
            bool carried = cards.Count == 1
                        && cards[0].Description.StartsWith("Vanilla RimWorld", StringComparison.Ordinal)
                        && cards[0].DiscordUrl == "https://discord.gg/example"
                        && cards[0].WebsiteUrl == "https://knappe0.games";
            r.Add(("browser: what an owner wrote about their server reaches the card", carried,
                   carried ? "description and links carried" : "THE LISTING STILL DROPS THEM"));

            // A server that wrote nothing gets empty strings, never null: the detail panel tests these directly.
            List<Features.Global.ServerCardDto> bare = Features.Global.DirectoryParse.Cards(
                new Newtonsoft.Json.Linq.JObject
                {
                    ["servers"] = new Newtonsoft.Json.Linq.JArray
                        { new Newtonsoft.Json.Linq.JObject { ["server_id"] = new string('c', 32) } },
                }, out _);
            r.Add(("browser: a server that published nothing yields empty text, not null",
                   bare.Count == 1 && bare[0].Description == "" && bare[0].DiscordUrl == "" && bare[0].WebsiteUrl == "",
                   "no null to guard against at draw time"));

            // The cache deserializes straight into the DTO, so it is a second way owner text reaches a Label.
            var poisoned = new Features.Global.ServerCardDto
            {
                ServerId = new string('d', 32),
                Name = "ok",
                Description = "<size=400><color=#ff0000>PWN",
                DiscordUrl = "<b>x</b>",
            };
            poisoned.MakeInert();
            r.Add(("browser: a card loaded from the cache is made inert too",
                   poisoned.Description.IndexOf('<') < 0 && poisoned.DiscordUrl.IndexOf('<') < 0,
                   poisoned.Description));

            DiscordPresence(r);
            DiscordActivityAndLink(r);
            DiscordDynamicPresence(r);
            PlayerPrivacy(r);
            GlobalRetryFloor(r);
            GlobalChatOrdering(r);
            IdentityTeardown(r);
            FriendsClient(r);
            ModSync(r);
            ModSyncApply(r);
            AtomicFile(r);
            ConfigBaselineChecks(r);
            GlobalLinkStateChecks(r);
            RealtimeBackoffChecks(r);
            RealtimeSequenceChecks(r);
            PrivacyAndIdentityChecks(r);
            ResourceLifecycleChecks(r);
            DirectoryStreamChecks(r);
            ConfigScopeChecks(r);
            ConfigBackupTrial(r);
            ModSyncTransaction(r);
        }

        // ModSync and Config Enforcement share one durable state, one consent and one restart.
        private static void ModSyncTransaction(List<(string, bool, string)> r)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmh-txn-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            string path = System.IO.Path.Combine(root, "ModsConfig.xml");

            string Doc(params string[] ids)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder("<ModsConfigData><version>1.6</version><activeMods>");
                foreach (string id in ids) sb.Append("<li>").Append(id).Append("</li>");
                return sb.Append("</activeMods></ModsConfigData>").ToString();
            }

            bool wasEnabled = Features.Enforcement.EnforcementCache.Enabled;
            Features.Enforcement.ModSyncApply.ResetForTest();
            Features.Enforcement.ModSyncApply.PathOverrideForTest = path;
            Features.Enforcement.EnforcementProfileApplier.InstalledModsForTest = () => new List<Features.Mods.InstalledMod>
            {
                new Features.Mods.InstalledMod { Name = "Harmony", PackageId = "brrainz.harmony", Active = true },
                new Features.Mods.InstalledMod { Name = "Core", PackageId = "ludeon.rimworld", Active = true },
                new Features.Mods.InstalledMod { Name = "RimWorld Together", PackageId = "nova.rimworldtogether", Active = true },
                new Features.Mods.InstalledMod { Name = "KMH: Colony Nexus", PackageId = "knappe.kmh.patch", Active = true },
            };
            try
            {
                Features.Enforcement.EnforcementCache.Apply(false, false, false, false, false, null, "", null, "");
                bool none = !Features.Enforcement.EnforcementCache.HasModOrder
                         && Features.Enforcement.EnforcementProfileApplier.OrderVerified(out _);
                r.Add(("modsync txn: a server that publishes no order verifies as nothing to do", none,
                       none ? "no order, no gate" : "AN ABSENT ORDER BLOCKED ADMISSION"));

                System.IO.File.WriteAllText(path, Doc("brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch"));
                Features.Enforcement.EnforcementCache.Apply(false, false, false, false, false, null, "",
                    new List<string> { "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch" }, "HASH1");
                bool hasOrder = Features.Enforcement.EnforcementCache.HasModOrder
                             && Features.Enforcement.EnforcementCache.ModOrderHash == "HASH1";
                r.Add(("modsync txn: the published order and its hash reach the client from the admission snapshot",
                       hasOrder, hasOrder ? "order carried" : "THE SNAPSHOT DID NOT CARRY THE ORDER"));

                bool stateMissing = !Features.Enforcement.EnforcementProfileApplier.OrderVerified(out string why1);
                r.Add(("modsync txn: a correct file with no durable record of it does not release the world",
                       stateMissing, stateMissing ? why1 : "RELEASED WITHOUT A DURABLE RECORD"));

                // Only mods the server names are ordered; the player's own extras sit wherever RimWorld puts them.
                List<string> filtered = Features.Enforcement.EnforcementProfileApplier.FilterToNamed(
                    new List<string> { "brrainz.harmony", "my.own.mod", "ludeon.rimworld" },
                    new List<string> { "brrainz.harmony", "ludeon.rimworld" });
                r.Add(("modsync txn: verification ignores mods the server never named",
                       Features.Enforcement.ModSyncApply.SameSequence(filtered,
                           new List<string> { "brrainz.harmony", "ludeon.rimworld" }),
                       string.Join(" > ", filtered.ToArray())));

                System.IO.File.WriteAllText(path, Doc("ludeon.rimworld", "brrainz.harmony"));
                List<string> wrong = Features.Enforcement.EnforcementProfileApplier.FilterToNamed(
                    new List<string> { "ludeon.rimworld", "brrainz.harmony" },
                    Features.Enforcement.EnforcementCache.ModOrder);
                r.Add(("modsync txn: an order on disk that differs from the manifest fails verification",
                       !Features.Enforcement.ModSyncApply.SameSequence(wrong, Features.Enforcement.EnforcementCache.ModOrder),
                       "wrong order refused"));

                List<string> huge = new List<string>();
                for (int i = 0; i <= Features.Enforcement.EnforcementCache.MaxOrderEntries + 10; i++) huge.Add("mod." + i);
                Features.Enforcement.EnforcementCache.Apply(false, false, false, false, false, null, "", huge, "H");
                r.Add(("modsync txn: an oversized published order is bounded, not taken whole",
                       Features.Enforcement.EnforcementCache.ModOrder.Count
                           == Features.Enforcement.EnforcementCache.MaxOrderEntries,
                       Features.Enforcement.EnforcementCache.ModOrder.Count.ToString()));

                Features.Enforcement.EnforcementCache.Apply(false, false, false, false, false, null, "",
                    new List<string> { "a.b", "a.b", "c.d" }, "H");
                r.Add(("modsync txn: a duplicated id in the published order collapses to one position",
                       Features.Enforcement.EnforcementCache.ModOrder.Count == 2,
                       string.Join(",", Features.Enforcement.EnforcementCache.ModOrder.ToArray())));

                Features.Enforcement.EnforcementCache.Apply(false, false, false, false, false, null, "",
                    new List<string> { "brrainz.harmony", "ludeon.rimworld" }, "HASH1");
                string both = Features.Enforcement.Dialog_KMHEnforcementConsent.BuildSummary();
                bool says = both.Contains("Mod list and load order") && both.Contains("restart once")
                         && both.Contains("Nothing is changed until you choose Apply");
                r.Add(("modsync ui: one dialog names the mod list, the order and the single restart", says,
                       says ? "combined consent" : "THE CONSENT DIALOG DOES NOT DESCRIBE WHAT IT APPLIES"));

                Features.Enforcement.EnforcementCache.Apply(false, false, false, false, false, null, "", null, "");
                string neither = Features.Enforcement.Dialog_KMHEnforcementConsent.BuildSummary();
                r.Add(("modsync ui: with nothing to change the dialog says so rather than inventing work",
                       neither.Contains("Nothing on this machine has to change"), "honest empty state"));
            }
            catch (Exception ex) { r.Add(("modsync txn: the transaction harness ran", false, ex.Message)); }
            finally
            {
                Features.Enforcement.ModSyncApply.ResetForTest();
                Features.Enforcement.EnforcementProfileApplier.InstalledModsForTest = null;
                Features.Enforcement.EnforcementCache.Apply(wasEnabled, false, false, false, false, null, "", null, "");
                try { System.IO.Directory.Delete(root, true); } catch { }
            }

            // A config edit inside the debounce must stay pending, or that drift goes unchecked until some later file event.
            DateTime now = DateTime.UtcNow;
            try
            {
                Features.Enforcement.EnforcementProfileApplier.SetReapplyForTest(true, now.AddMilliseconds(-500));
                bool tooSoon = !Features.Enforcement.EnforcementProfileApplier.TakePendingReapply(now)
                            && Features.Enforcement.EnforcementProfileApplier.ReapplyPendingForTest;
                bool later = Features.Enforcement.EnforcementProfileApplier.TakePendingReapply(now.AddSeconds(1))
                          && !Features.Enforcement.EnforcementProfileApplier.ReapplyPendingForTest;
                r.Add(("enforcement: a config change inside the re-apply debounce is kept and re-checked after it",
                       tooSoon && later, $"kept while too soon={tooSoon}, taken after={later}"));
            }
            finally { Features.Enforcement.EnforcementProfileApplier.SetReapplyForTest(false, DateTime.MinValue); }
        }

        // ModsConfig.xml is either exactly what KMH meant to write, or untouched; each case is one a real machine produces.
        private static void ModSyncApply(List<(string, bool, string)> r)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmh-modsync-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            string path = System.IO.Path.Combine(root, "ModsConfig.xml");

            string Original =
                "<ModsConfigData><version>1.6.4871</version>"
              + "<activeMods><li>ludeon.rimworld</li><li>brrainz.harmony</li></activeMods>"
              + "<knownExpansions><li>ludeon.rimworld.royalty</li></knownExpansions></ModsConfigData>";

            List<string> Wanted() => new List<string> { "brrainz.harmony", "ludeon.rimworld" };

            void Reset()
            {
                Features.Enforcement.ModSyncApply.ResetForTest();
                Features.Enforcement.ModSyncApply.PathOverrideForTest = path;
                System.IO.File.WriteAllText(path, Original);
                string bak = Features.Enforcement.ModSyncApply.BackupPathFor(path);
                if (System.IO.File.Exists(bak)) System.IO.File.Delete(bak);
            }

            try
            {
                Reset();
                var ok = Features.Enforcement.ModSyncApply.Apply(Wanted());
                Features.Enforcement.ModSyncApply.ActiveModsOf(System.IO.File.ReadAllText(path), out List<string> got, out _);
                bool applied = ok.Applied && ok.Changed && Features.Enforcement.ModSyncApply.SameSequence(got, Wanted())
                            && ok.Hash.Length == 64;
                r.Add(("modsync apply: the order on disk is exactly the order that was asked for", applied,
                       applied ? string.Join(" > ", got.ToArray()) : "THE FILE IS NOT WHAT KMH MEANT TO WRITE: " + ok.Failure));

                string after = System.IO.File.ReadAllText(path);
                bool preserved = after.Contains("1.6.4871") && after.Contains("knownExpansions")
                              && after.Contains("ludeon.rimworld.royalty");
                r.Add(("modsync apply: version and knownExpansions are carried through, not rewritten", preserved,
                       preserved ? "unrelated content preserved" : "RIMWORLD CONTENT WAS LOST"));

                Reset();
                Features.Enforcement.ModSyncApply.Apply(Wanted());
                string firstPass = System.IO.File.ReadAllText(path);
                var again = Features.Enforcement.ModSyncApply.Apply(Wanted());
                r.Add(("modsync apply: applying the same order again changes nothing",
                       again.Applied && !again.Changed && System.IO.File.ReadAllText(path) == firstPass,
                       again.Changed ? "THE FILE WAS REWRITTEN FOR NO REASON" : "no-op"));

                Reset();
                Features.Enforcement.ModSyncApply.ReadBackOverrideForTest = _ => Original;
                var lied = Features.Enforcement.ModSyncApply.Apply(Wanted());
                bool restored = !lied.Applied && lied.RolledBack
                             && System.IO.File.ReadAllText(path) == Original;
                r.Add(("modsync apply: a readback that disagrees with the write rolls the file back", restored,
                       restored ? "rolled back" : "A FAILED WRITE WAS LEFT ON DISK: " + lied.Failure));

                // No recovery copy means no rollback, so nothing may be written in the first place.
                Reset();
                Features.Enforcement.ModSyncApply.FailBackupForTest = () => { throw new System.IO.IOException("no space"); };
                var noBackup = Features.Enforcement.ModSyncApply.Apply(Wanted());
                r.Add(("modsync apply: without a recovery copy nothing is written at all",
                       !noBackup.Applied && System.IO.File.ReadAllText(path) == Original,
                       noBackup.Applied ? "WROTE WITHOUT A WAY BACK" : noBackup.Failure));

                // A recovery copy from an earlier apply is not this call's: restoring it would throw away the player's list.
                Reset();
                const string Stale = "<ModsConfigData><version>1.0</version><activeMods><li>old.list</li></activeMods></ModsConfigData>";
                System.IO.File.WriteAllText(Features.Enforcement.ModSyncApply.BackupPathFor(path), Stale);
                Features.Enforcement.ModSyncApply.BeforeBackupForTest = () => { throw new System.IO.IOException("disk busy"); };
                var early = Features.Enforcement.ModSyncApply.Apply(Wanted());
                bool untouched = !early.Applied && !early.RolledBack && System.IO.File.ReadAllText(path) == Original;
                r.Add(("modsync apply: a failure before its own backup leaves the mod list alone, never an older backup", untouched,
                       untouched ? "untouched" : "AN OLD BACKUP REPLACED THE PLAYER'S MOD LIST"));

                Reset();
                Features.Enforcement.ModSyncApply.BeforeReplaceForTest = _ => { throw new System.IO.IOException("power cut"); };
                var crashed = Features.Enforcement.ModSyncApply.Apply(Wanted());
                r.Add(("modsync apply: a crash before the replace leaves the original mod list intact",
                       !crashed.Applied && System.IO.File.ReadAllText(path) == Original,
                       crashed.Applied ? "A PARTIAL WRITE SURVIVED" : "original intact"));

                Reset();
                Features.Enforcement.ModSyncApply.AfterReplaceForTest = _ => { throw new System.IO.IOException("power cut"); };
                var late = Features.Enforcement.ModSyncApply.Apply(Wanted());
                bool recovered = !late.Applied && late.RolledBack && System.IO.File.ReadAllText(path) == Original;
                r.Add(("modsync apply: a crash after the replace is rolled back from the recovery copy", recovered,
                       recovered ? "recovered" : "COULD NOT RECOVER: " + late.Failure));

                // The only test that tells an atomic write apart from one that truncates the real file in place.
                Reset();
                Features.Enforcement.ModSyncApply.DuringWriteForTest = () => { throw new System.IO.IOException("disk died"); };
                var torn = Features.Enforcement.ModSyncApply.Apply(Wanted());
                string survived = System.IO.File.ReadAllText(path);
                bool intact = !torn.Applied && survived == Original
                           && Features.Enforcement.ModSyncApply.ActiveModsOf(survived, out _, out _);
                r.Add(("modsync apply: a failure mid-write leaves a whole file, never a truncated one", intact,
                       intact ? "atomic" : "THE REAL FILE WAS WRITTEN IN PLACE"));

                Features.Enforcement.ModSyncApply.ResetForTest();
                Features.Enforcement.ModSyncApply.PathOverrideForTest = path;
                System.IO.File.WriteAllText(path, "<ModsConfigData><activeMods><li>a.b");
                var malformed = Features.Enforcement.ModSyncApply.Apply(Wanted());
                r.Add(("modsync apply: a damaged ModsConfig.xml is refused, not replaced",
                       !malformed.Applied && System.IO.File.ReadAllText(path).StartsWith("<ModsConfigData>"),
                       malformed.Applied ? "OVERWROTE A FILE IT COULD NOT READ" : malformed.Failure));

                System.IO.File.WriteAllText(path, "<ModsConfigData><version>1.6</version></ModsConfigData>");
                var noSection = Features.Enforcement.ModSyncApply.Apply(Wanted());
                r.Add(("modsync apply: a document with no activeMods section is refused",
                       !noSection.Applied, noSection.Failure));

                Reset();
                var empty = Features.Enforcement.ModSyncApply.Apply(new List<string>());
                r.Add(("modsync apply: an empty order never empties the player's mod list",
                       !empty.Applied && System.IO.File.ReadAllText(path) == Original,
                       empty.Applied ? "EMPTIED THE MOD LIST" : "refused"));

                Features.Enforcement.ModSyncApply.ResetForTest();
                Features.Enforcement.ModSyncApply.PathOverrideForTest = System.IO.Path.Combine(root, "gone.xml");
                var missing = Features.Enforcement.ModSyncApply.Apply(Wanted());
                r.Add(("modsync apply: a missing ModsConfig.xml is refused rather than invented",
                       !missing.Applied && !System.IO.File.Exists(System.IO.Path.Combine(root, "gone.xml")),
                       missing.Failure));
            }
            catch (Exception ex) { r.Add(("modsync apply: the apply harness ran", false, ex.Message)); }
            finally
            {
                Features.Enforcement.ModSyncApply.ResetForTest();
                try { System.IO.Directory.Delete(root, true); } catch { }
            }
        }

        // The player's own settings cannot be regenerated, so restore is byte-exact and created files must disappear.
        private static void ConfigBaselineChecks(List<(string, bool, string)> r)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmh-baseline-" + System.Guid.NewGuid().ToString("N"));
            string cfg = System.IO.Path.Combine(root, "Config");
            string backup = System.IO.Path.Combine(root, "Originals");
            try
            {
                System.IO.Directory.CreateDirectory(cfg);
                System.IO.File.WriteAllText(System.IO.Path.Combine(cfg, "Mod_Kept.xml"), "<s><v>original</v></s>");
                System.IO.File.WriteAllText(System.IO.Path.Combine(cfg, "Mod_Changed.xml"), "<s><v>mine</v></s>");
                string[] managed = { "Mod_Kept.xml", "Mod_Changed.xml", "Mod_Created.xml" };

                var baseline = Features.Enforcement.ConfigBaseline.Capture(cfg, managed);
                bool shape = baseline.Count == 3
                          && baseline.Entries.Find(e => e.Path == "Mod_Created.xml")?.Existed == false
                          && baseline.Entries.Find(e => e.Path == "Mod_Kept.xml")?.Existed == true;
                r.Add(("config baseline: records which managed files existed before enforcement", shape,
                       shape ? "2 present, 1 absent" : "THE BASELINE MISREAD WHAT EXISTED"));

                bool wrote = Features.Enforcement.ConfigBaseline.Write(baseline, cfg, backup, out string why);
                r.Add(("config baseline: a verified backup is written and proves its own copies", wrote, wrote ? "verified" : why));

                System.IO.File.WriteAllText(System.IO.Path.Combine(cfg, "Mod_Changed.xml"), "<s><v>server</v></s>");
                System.IO.File.WriteAllText(System.IO.Path.Combine(cfg, "Mod_Created.xml"), "<s><v>server-only</v></s>");

                bool restored = Features.Enforcement.ConfigBaseline.Restore(backup, cfg, out string rwhy);
                r.Add(("config baseline: restore reports success only after verifying every file", restored, restored ? "verified" : rwhy));

                string changed = System.IO.File.ReadAllText(System.IO.Path.Combine(cfg, "Mod_Changed.xml"));
                r.Add(("config baseline: a replaced file comes back byte-for-byte", changed == "<s><v>mine</v></s>",
                       changed == "<s><v>mine</v></s>" ? "exact" : "RESTORED TO THE WRONG BYTES: " + changed));

                bool gone = !System.IO.File.Exists(System.IO.Path.Combine(cfg, "Mod_Created.xml"));
                r.Add(("config baseline: a file enforcement created is removed, not left behind", gone,
                       gone ? "removed" : "AN ENFORCED FILE SURVIVED THE RESTORE"));

                bool untouched = System.IO.File.ReadAllText(System.IO.Path.Combine(cfg, "Mod_Kept.xml")) == "<s><v>original</v></s>";
                r.Add(("config baseline: an unchanged file is still its original", untouched, untouched ? "intact" : "AN UNCHANGED FILE WAS ALTERED"));

                System.IO.File.Delete(System.IO.Path.Combine(backup, "Mod_Kept.xml"));
                bool refused = Features.Enforcement.ConfigBaseline.Verify(backup, out string vwhy) == null;
                r.Add(("config baseline: a backup missing a file is refused rather than trusted", refused,
                       refused ? vwhy : "A DAMAGED BACKUP VERIFIED"));

                bool noRestore = !Features.Enforcement.ConfigBaseline.Restore(backup, cfg, out _);
                r.Add(("config baseline: restore refuses to run from an unverified backup", noRestore,
                       noRestore ? "refused" : "RESTORED FROM A DAMAGED BACKUP"));

                // Only managed names are ever captured, so the 118MB of caches beside them stay out of it.
                var scoped = Features.Enforcement.ConfigBaseline.Capture(cfg, new[] { "Mod_Kept.xml", "preview.png", "sub/Mod_X.xml", "Prefs.xml" });
                r.Add(("config baseline: captures only managed names", scoped.Count == 1,
                       scoped.Count == 1 ? "1 of 4" : "CAPTURED SOMETHING UNMANAGED"));
            }
            catch (Exception ex) { r.Add(("config baseline: the harness ran", false, ex.Message)); }
            finally { try { System.IO.Directory.Delete(root, true); } catch { } }
        }

        // A 429 is the directory answering; calling it offline makes the browser and the account panel disagree.
        private static void GlobalLinkStateChecks(List<(string, bool, string)> r)
        {
            var client = typeof(Features.Global.KmhDirectoryClient);
            var mark = client.GetMethod("MarkFailure", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (mark == null) { r.Add(("global link: the failure classifier is reachable", false, "MarkFailure is gone")); return; }

            Features.Global.GlobalLinkState Classify(int status, string code, TimeSpan retryAfter)
            {
                mark.Invoke(null, new object[]
                {
                    new Features.Global.GlobalHttp.Result { Status = status, ErrorCode = code, RetryAfter = retryAfter },
                    Features.Global.KmhDirectoryClient.Generation,
                    null,
                });
                return Features.Global.KmhDirectoryClient.State;
            }

            try
            {
                var limited = Classify(429, "KMH-RATE-001", TimeSpan.FromSeconds(30));
                bool ok = limited == Features.Global.GlobalLinkState.RateLimited;
                r.Add(("global link: a 429 is rate-limited, never offline", ok,
                       ok ? "RateLimited" : $"A 429 REPORTED AS {limited}"));

                DateTime holding = Features.Global.KmhDirectoryClient.HoldingUntilUtc;
                bool held = holding > DateTime.UtcNow.AddSeconds(20) && holding <= DateTime.UtcNow.AddSeconds(31);
                r.Add(("global link: the delay the directory named is the delay we wait", held,
                       held ? "holding ~30s" : $"holding until {holding:O}"));

                var offline = Classify(0, "KMH-NET-001", default);
                bool isOff = offline == Features.Global.GlobalLinkState.Offline;
                r.Add(("global link: nothing answering is offline", isOff, isOff ? "Offline" : $"got {offline}"));

                var five = Classify(503, "KMH-SRV-001", default);
                r.Add(("global link: a 5xx is offline", five == Features.Global.GlobalLinkState.Offline,
                       five.ToString()));

                var four = Classify(400, "KMH-REQ-001", default);
                bool degraded = four == Features.Global.GlobalLinkState.Degraded;
                r.Add(("global link: an answered 4xx is degraded, not offline", degraded,
                       degraded ? "Degraded" : $"A 4xx REPORTED AS {four}"));

                // The hold outranks the curve in both directions, so a held gate is not due however long ago it tried.
                DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                bool notDue = !Features.Global.KmhPollGate.DueAt(now, now.AddHours(-1), now.AddHours(-1), 0,
                                                                TimeSpan.FromSeconds(20), now.AddSeconds(10));
                bool dueAfter = Features.Global.KmhPollGate.DueAt(now.AddSeconds(11), now.AddHours(-1), now.AddHours(-1), 0,
                                                                 TimeSpan.FromSeconds(20), now.AddSeconds(10));
                r.Add(("global link: a gate holding for the directory is not due, and is due once the hold passes",
                       notDue && dueAfter, notDue && dueAfter ? "held then released" : $"held={notDue} released={dueAfter}"));
            }
            catch (Exception ex) { r.Add(("global link: the harness ran", false, ex.Message)); }
            finally { Features.Global.KmhDirectoryClient.ResetForTest(); }
        }

        // A pushed directory must end where a polled one would, or the browser shows rows nobody can reproduce.
        private static void DirectoryStreamChecks(List<(string, bool, string)> r)
        {
            var stream = typeof(Features.Global.KmhDirectoryStream);
            var apply = stream.GetMethod("Apply", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var revision = stream.GetProperty("Revision");
            if (apply == null) { r.Add(("directory stream: the event applier is reachable", false, "Apply is gone")); return; }

            void Push(string json) => apply.Invoke(null, new object[] { json });

            int resyncs = 0;
            Action realResync = Features.Global.KmhDirectoryStream.Resync;
            string priorCacheRoot = Features.Global.KmhDirectoryCache.RootOverrideForTest;
            string cacheRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmh-dirstream-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                // A push writes the cache, and this suite also runs in game where that is the player's real one.
                System.IO.Directory.CreateDirectory(cacheRoot);
                Features.Global.KmhDirectoryCache.RootOverrideForTest = cacheRoot;
                Features.Global.KmhDirectoryStream.Resync = () => resyncs++;
                Features.Global.KmhDirectoryClient.ResetForTest();
                Features.Global.KmhDirectoryStream.Reset();

                string card = "{\"server_id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"name\":\"Pushed\"," +
                              "\"status\":\"Online\",\"players\":4,\"max_players\":16,\"requires_password\":false}";

                Push("{\"type\":\"server\",\"revision\":1,\"server\":" + card + "}");
                var cards = Features.Global.KmhDirectoryClient.Cards;
                bool added = cards.Count == 1 && cards[0].Players == 4;
                r.Add(("directory stream: a pushed server becomes a row", added,
                       added ? "1 row" : $"{cards.Count} row(s)"));

                Push("{\"type\":\"server\",\"revision\":2,\"server\":" +
                     card.Replace("\"players\":4", "\"players\":9") + "}");
                cards = Features.Global.KmhDirectoryClient.Cards;
                bool replaced = cards.Count == 1 && cards[0].Players == 9;
                r.Add(("directory stream: a server that changes replaces its row rather than appearing twice",
                       replaced, replaced ? "1 row, 9 players" : $"{cards.Count} row(s)"));

                bool online = Features.Global.KmhDirectoryClient.State == Features.Global.GlobalLinkState.Online;
                r.Add(("directory stream: a carried event counts as the directory being reachable", online,
                       Features.Global.KmhDirectoryClient.State.ToString()));

                Push("{\"type\":\"counters\",\"revision\":3,\"server_count\":7,\"player_count\":42}");
                bool counted = Features.Global.KmhDirectoryClient.ServerCount == 7
                            && Features.Global.KmhDirectoryClient.PlayerCount == 42;
                r.Add(("directory stream: counters arrive without a page fetch", counted,
                       counted ? "7 servers, 42 players" : "counters were not applied"));

                // Rich text is parsed by Widgets.Label, and these names come from other server owners.
                Push("{\"type\":\"server\",\"revision\":4,\"server\":{\"server_id\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"," +
                     "\"name\":\"<color=#ff0000>red</color>\",\"status\":\"Online\"}}");
                var pushed = Features.Global.KmhDirectoryClient.Cards;
                string name = pushed.Count > 1 ? pushed[1].Name : "";
                bool inert = name.IndexOf("<color", StringComparison.OrdinalIgnoreCase) < 0;
                r.Add(("directory stream: a pushed name is made inert like a polled one", inert,
                       inert ? "markup neutralised" : $"MARKUP SURVIVED: {name}"));

                int before = Features.Global.KmhDirectoryClient.Cards.Count;
                Push("{\"type\":\"server\",\"revision\":5,\"server\":{\"name\":\"no id\"}}");
                Push("{\"type\":\"server\",\"revision\":6}");
                Push("not json at all");
                bool unchanged = Features.Global.KmhDirectoryClient.Cards.Count == before;
                r.Add(("directory stream: an unusable frame costs one event, not the list", unchanged,
                       unchanged ? $"still {before} row(s)" : "A BAD FRAME CHANGED THE LIST"));

                // A gap means something was missed; the revision still advances or every later event would re-read.
                long had = (long)revision.GetValue(null);
                int before99 = resyncs;
                Push("{\"type\":\"counters\",\"revision\":99,\"server_count\":1,\"player_count\":1}");
                bool advanced = (long)revision.GetValue(null) == 99;
                bool resynced = resyncs == before99 + 1;
                r.Add(("directory stream: a revision gap resyncs and takes the new baseline",
                       advanced && resynced && had < 99,
                       advanced && resynced ? "re-read, baseline 99"
                                            : $"revision={revision.GetValue(null)} resyncs={resyncs - before99}"));

                // The next event in sequence must NOT re-read: a resync per event is the request-per-frame loop.
                int quiet = resyncs;
                Push("{\"type\":\"counters\",\"revision\":100,\"server_count\":2,\"player_count\":2}");
                r.Add(("directory stream: an in-sequence event does not re-read", resyncs == quiet,
                       resyncs == quiet ? "no extra fetch" : "EVERY EVENT TRIGGERS A FETCH"));

                bool backoffGrows = Features.Global.KmhDirectoryStream.BackoffFor(0) >= Features.Global.KmhRealtimeBackoff.Min
                                 && Features.Global.KmhDirectoryStream.BackoffFor(50) <= Features.Global.KmhRealtimeBackoff.Max;
                r.Add(("directory stream: reconnects back off and are capped", backoffGrows,
                       backoffGrows ? "2s to 60s" : "THE BACKOFF DOES NOT ESCALATE OR IS UNCAPPED"));
            }
            catch (Exception ex) { r.Add(("directory stream: the harness ran", false, ex.Message)); }
            finally
            {
                Features.Global.KmhDirectoryStream.Resync = realResync;
                Features.Global.KmhDirectoryStream.Reset();
                Features.Global.KmhDirectoryClient.ResetForTest();
                Features.Global.KmhDirectoryCache.RootOverrideForTest = priorCacheRoot;
                try { if (System.IO.Directory.Exists(cacheRoot)) System.IO.Directory.Delete(cacheRoot, true); } catch (Exception) { }
            }
        }

        // Publish must never offer the whole Config folder (a real one: 118MB of caches); KMH_MEASURE_CONFIG measures one.
        private static void ConfigScopeChecks(List<(string, bool, string)> r)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmh-scope-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(root);

                string[] managed = { "Mod_Alpha_Settings.xml", "Mod_Beta_Settings.xml", "SomeMod.json" };
                foreach (string name in managed)
                    System.IO.File.WriteAllText(System.IO.Path.Combine(root, name), new string('x', 40 * 1024));

                foreach (string name in new[] { "ModsConfig.xml", "Prefs.xml", "KeyPrefs.xml", "Knowledge.xml",
                                                "backup_ModsConfig.xml", "TrueTerrainColorsCache.xml",
                                                "LastPlayedVersion.txt", "preview.png" })
                    System.IO.File.WriteAllText(System.IO.Path.Combine(root, name), new string('p', 8 * 1024));

                // The three cache directories, at the same 1:1000 ratio as the real ones.
                foreach (string dir in new[] { "JellyLauncher", "PA_DynamicSeedIcons", "DKPatches" })
                {
                    string d = System.IO.Path.Combine(root, dir);
                    System.IO.Directory.CreateDirectory(d);
                    for (int i = 0; i < 20; i++)
                        System.IO.File.WriteAllText(System.IO.Path.Combine(d, $"cached_{i}.xml"), new string('c', 64 * 1024));
                }

                long whole = 0;
                int wholeFiles = 0;
                foreach (string f in System.IO.Directory.GetFiles(root, "*", System.IO.SearchOption.AllDirectories))
                { whole += new System.IO.FileInfo(f).Length; wholeFiles++; }

                var manifest = Features.Enforcement.ConfigManifest.Build(root, _ => false);

                bool onlyManaged = manifest.Count == managed.Length;
                r.Add(("config scope: a real-shaped Config folder publishes only its managed files", onlyManaged,
                       onlyManaged ? $"{manifest.Count} of {wholeFiles} file(s)"
                                   : $"SELECTED {manifest.Count} OF {wholeFiles}"));

                bool nested = true;
                foreach (var e in manifest.Entries)
                    if (e.Path.IndexOf('/') >= 0 || e.Path.IndexOf('\\') >= 0) { nested = false; break; }
                r.Add(("config scope: nothing from a subdirectory is ever selected", nested,
                       nested ? "flat" : "A NESTED PATH WAS SELECTED"));

                bool tiny = manifest.TotalBytes * 20 < whole;
                r.Add(("config scope: the published set is a fraction of the folder", tiny,
                       $"{whole / 1024}KB on disk, {manifest.TotalBytes / 1024}KB published"));

                // From the variable, not GenFilePaths, which is a Unity internal call that throws outside the game.
                string real = Environment.GetEnvironmentVariable("KMH_MEASURE_CONFIG") ?? "";
                if (real.Length > 0 && System.IO.Directory.Exists(real))
                {
                    long realBytes = 0;
                    int realFiles = 0;
                    foreach (string f in System.IO.Directory.GetFiles(real, "*", System.IO.SearchOption.AllDirectories))
                    { realBytes += new System.IO.FileInfo(f).Length; realFiles++; }
                    var m = Features.Enforcement.ConfigManifest.Build(real, Features.Enforcement.EnforcementMods.IsSafeModConfigFile);
                    r.Add(("config scope: MEASURED against the real Config folder", true,
                           $"{realFiles} file(s) / {realBytes / (1024 * 1024)}MB on disk -> " +
                           $"{m.Count} file(s) / {m.TotalBytes / 1024}KB published"));
                }
            }
            catch (Exception ex) { r.Add(("config scope: the harness ran", false, ex.Message)); }
            finally { try { System.IO.Directory.Delete(root, true); } catch { } }
        }

        // Run on a copy of a REAL Config folder (KMH_BACKUP_TRIAL=<path>): real ones carry BOMs, CRLF and 2000 files.
        private static void ConfigBackupTrial(List<(string, bool, string)> r)
        {
            string source = Environment.GetEnvironmentVariable("KMH_BACKUP_TRIAL") ?? "";
            if (source.Length == 0 || !System.IO.Directory.Exists(source)) return;

            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmh-trial-" + System.Guid.NewGuid().ToString("N"));
            string cfg = System.IO.Path.Combine(root, "Config");
            string backup = System.IO.Path.Combine(root, "Originals");
            try
            {
                // A copy, always: this must never be able to touch the folder it was pointed at.
                System.IO.Directory.CreateDirectory(cfg);
                foreach (string dir in System.IO.Directory.GetDirectories(source, "*", System.IO.SearchOption.AllDirectories))
                    System.IO.Directory.CreateDirectory(dir.Replace(source, cfg));
                foreach (string f in System.IO.Directory.GetFiles(source, "*", System.IO.SearchOption.AllDirectories))
                    System.IO.File.Copy(f, f.Replace(source, cfg), true);

                var manifest = Features.Enforcement.ConfigManifest.Build(cfg, _ => false);
                var before = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in manifest.Entries)
                    before[e.Path] = System.IO.File.ReadAllBytes(System.IO.Path.Combine(cfg, e.Path));

                string witnessTop = System.IO.Path.Combine(cfg, "Prefs.xml");
                string witnessDeep = null;
                foreach (string f in System.IO.Directory.GetFiles(cfg, "*", System.IO.SearchOption.AllDirectories))
                    if (f.Length > cfg.Length + 1 && System.IO.Path.GetDirectoryName(f) != cfg) { witnessDeep = f; break; }
                byte[] topBefore = System.IO.File.Exists(witnessTop) ? System.IO.File.ReadAllBytes(witnessTop) : null;
                byte[] deepBefore = witnessDeep != null ? System.IO.File.ReadAllBytes(witnessDeep) : null;

                var paths = new List<string>();
                foreach (var e in manifest.Entries) paths.Add(e.Path);
                paths.Add("Mod_ThisServerAddedThis.xml");        // a file only enforcement creates

                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool wrote = Features.Enforcement.ConfigBaseline.EnsureCovers(cfg, backup, paths, out string why);
                long captureMs = sw.ElapsedMilliseconds;
                r.Add(("config trial: a real Config folder's originals are recorded and verified", wrote,
                       wrote ? $"{paths.Count} path(s) in {captureMs}ms" : why));
                if (!wrote) return;

                long backupBytes = 0;
                foreach (string f in System.IO.Directory.GetFiles(backup, "*", System.IO.SearchOption.AllDirectories))
                    backupBytes += new System.IO.FileInfo(f).Length;
                long liveBytes = 0;
                foreach (string f in System.IO.Directory.GetFiles(cfg, "*", System.IO.SearchOption.AllDirectories))
                    liveBytes += new System.IO.FileInfo(f).Length;
                r.Add(("config trial: the backup holds the managed set, not the folder", backupBytes * 4 < liveBytes,
                       $"{liveBytes / (1024 * 1024)}MB live, {backupBytes / 1024}KB backed up"));

                foreach (var e in manifest.Entries)
                    System.IO.File.WriteAllText(System.IO.Path.Combine(cfg, e.Path), "<enforced />");
                System.IO.File.WriteAllText(System.IO.Path.Combine(cfg, "Mod_ThisServerAddedThis.xml"), "<enforced />");

                sw.Restart();
                bool restored = Features.Enforcement.ConfigBaseline.Restore(backup, cfg, out string rwhy);
                long restoreMs = sw.ElapsedMilliseconds;
                r.Add(("config trial: restore reports success only after re-reading every file", restored,
                       restored ? $"{restoreMs}ms" : rwhy));

                int same = 0;
                string firstBad = null;
                foreach (var kv in before)
                {
                    byte[] now = System.IO.File.ReadAllBytes(System.IO.Path.Combine(cfg, kv.Key));
                    bool equal = now.Length == kv.Value.Length;
                    if (equal) for (int i = 0; i < now.Length; i++) if (now[i] != kv.Value[i]) { equal = false; break; }
                    if (equal) same++; else if (firstBad == null) firstBad = kv.Key;
                }
                r.Add(("config trial: every managed file comes back byte-for-byte", firstBad == null,
                       firstBad == null ? $"{same} file(s) identical" : $"'{firstBad}' DIFFERS"));

                bool gone = !System.IO.File.Exists(System.IO.Path.Combine(cfg, "Mod_ThisServerAddedThis.xml"));
                r.Add(("config trial: the file enforcement added is gone again", gone,
                       gone ? "removed" : "AN ENFORCED FILE SURVIVED"));

                bool untouched = (topBefore == null || Same(System.IO.File.ReadAllBytes(witnessTop), topBefore))
                              && (deepBefore == null || Same(System.IO.File.ReadAllBytes(witnessDeep), deepBefore));
                r.Add(("config trial: nothing outside the managed set was read back or rewritten", untouched,
                       untouched ? "personal and cached files untouched" : "AN UNMANAGED FILE CHANGED"));
            }
            catch (Exception ex) { r.Add(("config trial: the harness ran", false, ex.Message)); }
            finally { try { System.IO.Directory.Delete(root, true); } catch { } }
        }

        // Every Global realtime connection reconnects on one shared, jittered curve.
        private static void RealtimeBackoffChecks(List<(string, bool, string)> r)
        {
            TimeSpan For(int a, double j) => Features.Global.KmhRealtimeBackoff.For(a, j);

            bool grows = For(0, 1.0) < For(3, 1.0) && For(3, 1.0) < For(8, 1.0);
            r.Add(("realtime backoff: waiting longer after each failed attempt", grows,
                   grows ? $"{For(0,1.0).TotalSeconds}s -> {For(8,1.0).TotalSeconds}s" : "THE CURVE DOES NOT ESCALATE"));

            bool capped = For(50, 1.0) <= Features.Global.KmhRealtimeBackoff.Max
                       && For(1000, 1.0) <= Features.Global.KmhRealtimeBackoff.Max;
            r.Add(("realtime backoff: bounded however long the outage lasts", capped,
                   capped ? $"capped at {Features.Global.KmhRealtimeBackoff.Max.TotalSeconds}s" : "UNBOUNDED"));

            bool spreads = For(6, 0.0) < For(6, 0.5) && For(6, 0.5) < For(6, 1.0);
            r.Add(("realtime backoff: two clients that failed together do not retry together", spreads,
                   spreads ? $"attempt 6 spreads {For(6,0.0).TotalSeconds:0.#}s..{For(6,1.0).TotalSeconds:0.#}s"
                           : "NO JITTER - EVERY CLIENT RETURNS AT THE SAME INSTANT"));

            bool floored = For(9, 0.0) >= Features.Global.KmhRealtimeBackoff.Min
                        && For(0, 0.0) >= Features.Global.KmhRealtimeBackoff.Min;
            r.Add(("realtime backoff: never retries faster than the floor", floored,
                   floored ? $"never under {Features.Global.KmhRealtimeBackoff.Min.TotalSeconds}s" : "RETRIES TOO FAST"));

            bool shared = Features.Global.GlobalChatClient.BackoffFor(0) >= Features.Global.KmhRealtimeBackoff.Min
                       && Features.Global.KmhDirectoryStream.BackoffFor(0) >= Features.Global.KmhRealtimeBackoff.Min
                       && Features.Global.GlobalChatClient.BackoffFor(50) <= Features.Global.KmhRealtimeBackoff.Max
                       && Features.Global.KmhDirectoryStream.BackoffFor(50) <= Features.Global.KmhRealtimeBackoff.Max;
            r.Add(("realtime backoff: chat and directory share the one curve", shared,
                   shared ? "both inside the shared bounds" : "a socket still has its own curve"));
        }

        // Identity-scoped data is polled, so only a generation captured before each request stops a sign-out showing
        // the last account's data; it is checked where the request leaves the process, covering clients added later.
        private static void PrivacyAndIdentityChecks(List<(string, bool, string)> r)
        {
            Features.Global.KmhIdentityGeneration.ResetForTest();
            int start = Features.Global.KmhIdentityGeneration.Current;

            bool stableWhileSameAccount = Features.Global.KmhIdentityGeneration.IsCurrent(start);
            r.Add(("identity: a poll issued and answered under one account applies", stableWhileSameAccount,
                   stableWhileSameAccount ? "an unchanged identity accepts its own answer"
                                          : "a poll is refused even when nothing changed"));

            // Sign-out, unlink, relink and directory switch all bump the counter in the teardown, so a new path can't forget.
            Features.Global.KmhIdentityTeardown.ForgetIdentity();
            bool movedOnTeardown = !Features.Global.KmhIdentityGeneration.IsCurrent(start);
            r.Add(("identity: signing out invalidates every poll that was already in flight", movedOnTeardown,
                   movedOnTeardown ? $"generation {start} -> {Features.Global.KmhIdentityGeneration.Current}"
                                   : "A STALE POLL WOULD STILL APPLY AFTER SIGN-OUT"));

            int aGen = Features.Global.KmhIdentityGeneration.Current;
            Features.Global.KmhIdentityTeardown.ForgetIdentity();
            int bGen = Features.Global.KmhIdentityGeneration.Current;
            bool aRefused = !Features.Global.KmhIdentityGeneration.IsCurrent(aGen);
            bool bAccepted = Features.Global.KmhIdentityGeneration.IsCurrent(bGen);
            r.Add(("identity: account A's late answer is refused while account B is signed in",
                   aRefused && bAccepted,
                   aRefused && bAccepted ? "A's generation is stale, B's is current"
                                         : $"aRefused={aRefused}, bAccepted={bAccepted}"));

            int first = Features.Global.KmhIdentityGeneration.Current;
            int second = Features.Global.KmhIdentityGeneration.Current;
            bool bothApply = Features.Global.KmhIdentityGeneration.IsCurrent(second)
                          && Features.Global.KmhIdentityGeneration.IsCurrent(first);
            r.Add(("identity: two polls from the same account both apply whatever order they finish in",
                   bothApply,
                   bothApply ? "the guard is about identity, not ordering - a later poll simply overwrites"
                             : "an in-order poll was refused"));

            int before = Features.Global.KmhIdentityGeneration.Current;
            for (int i = 0; i < 50; i++) Features.Global.KmhIdentityGeneration.Bump();
            int after = Features.Global.KmhIdentityGeneration.Current;
            bool monotonic = after == before + 50 && !Features.Global.KmhIdentityGeneration.IsCurrent(before);
            r.Add(("identity: the generation only moves forward, so a stale one never becomes current again",
                   monotonic,
                   monotonic ? $"{before} -> {after} across 50 identity changes"
                             : $"{before} -> {after} after 50 bumps"));

            // Assert the call that leaves the process reads the counter: a check in one client says nothing about the next.
            List<string> sendCalls = KmhIlReader.CallsMadeBy(
                "KMHPatch.Features.Global.GlobalHttp", "SendAsync");
            bool chokePointGuards = sendCalls.Contains("get_Current") && sendCalls.Contains("IsCurrent");
            r.Add(("identity: the guard sits where the request leaves the process, not in each client",
                   chokePointGuards,
                   chokePointGuards ? "GlobalHttp captures the identity and refuses a stale answer"
                                    : "EVERY IDENTITY-SCOPED CLIENT WOULD HAVE TO REMEMBER TO CHECK"));

            bool anonymousUnaffected = true;
            string anonDetail = "";
            try
            {
                Features.Global.KmhIdentityGeneration.Bump();
                anonymousUnaffected = Features.Global.KmhDirectoryStream.IsGap(0, 5) == false;
            }
            catch (Exception ex) { anonymousUnaffected = false; anonDetail = ex.Message; }
            r.Add(("identity: an anonymous request is not scoped to an account and is unaffected",
                   anonymousUnaffected,
                   anonymousUnaffected ? "the public directory keeps working while signed out" : anonDetail));

            // A sign-out must invalidate a chat frame even when Stop never ran, so the identity counter is folded in.
            int chatBefore = Features.Global.GlobalChatClient.SessionGeneration;
            Features.Global.KmhIdentityGeneration.Bump();
            int chatAfter = Features.Global.GlobalChatClient.SessionGeneration;
            bool chatFolded = chatAfter != chatBefore;
            r.Add(("identity: a sign-out invalidates a chat frame even if the socket was never stopped",
                   chatFolded,
                   chatFolded ? $"session generation {chatBefore} -> {chatAfter} on an identity change"
                              : "THE CHAT SOCKET ONLY NOTICES ITS OWN STOP"));

            Features.Global.KmhIdentityGeneration.ResetForTest();
        }

        // Every ordering a network produces must end in the same directory; a missed event re-reads, never half-applies.
        private static void RealtimeSequenceChecks(List<(string, bool, string)> r)
        {
            bool Gap(long current, long incoming) => Features.Global.KmhDirectoryStream.IsGap(current, incoming);

            // The directory ran before this connect, so a first event at revision 4821 is the baseline, not a gap.
            bool firstIsBaseline = !Gap(0, 1) && !Gap(0, 4821) && !Gap(0, long.MaxValue);
            r.Add(("realtime sequence: the first event after a connect is the baseline, whatever its number",
                   firstIsBaseline,
                   firstIsBaseline ? "revision 0 never reads as a gap"
                                   : "A FRESH CONNECT RE-READS THE WHOLE DIRECTORY ON ITS FIRST EVENT"));

            bool inOrder = !Gap(10, 11) && !Gap(11, 12);
            r.Add(("realtime sequence: consecutive deltas apply without a re-read", inOrder,
                   inOrder ? "10 -> 11 -> 12 applies in place" : "consecutive deltas force a resync"));

            bool duplicate = !Gap(12, 12);
            bool late = !Gap(12, 11) && !Gap(12, 1);
            r.Add(("realtime sequence: a duplicate or a late delta changes nothing", duplicate && late,
                   duplicate && late ? "a repeat and an out-of-order arrival are both inert"
                                     : $"duplicate={duplicate}, late={late}"));

            bool oneMissing = Gap(11, 13);
            bool bigJump = Gap(11, 9999) && Gap(1, long.MaxValue);
            r.Add(("realtime sequence: a missed event forces a re-read rather than a partial apply",
                   oneMissing && bigJump,
                   oneMissing && bigJump ? "11 -> 13 and 11 -> 9999 both resync"
                                         : $"oneMissing={oneMissing}, largeGap={bigJump} - "
                                           + "A MISSED EVENT WOULD BE HALF-APPLIED"));

            // A stale number from the previous session is smaller, so it cannot look like a forward gap.
            bool previousSession = !Gap(500, 3) && !Gap(500, 499);
            r.Add(("realtime sequence: a number from the previous session never reads as a gap", previousSession,
                   previousSession ? "a smaller revision is late, not missing" : "a stale event forces a resync"));

            // Reset must put it back to the baseline state, or the next connect re-reads on its first event.
            long before = Features.Global.KmhDirectoryStream.Revision;
            Features.Global.KmhDirectoryStream.Reset();
            bool resetToBaseline = Features.Global.KmhDirectoryStream.Revision == 0 && !Gap(0, 12345);
            r.Add(("realtime sequence: reconnecting starts a fresh sequence rather than inheriting the old one",
                   resetToBaseline,
                   resetToBaseline ? $"revision {before} -> 0, and the next first event is a baseline"
                                   : $"revision is {Features.Global.KmhDirectoryStream.Revision} after a reset"));
        }

        // Nothing a player repeats (reconnects, server switches, cache clears) may leave what only a restart clears.
        private static void ResourceLifecycleChecks(List<(string, bool, string)> r)
        {
            const int Cycles = 100;

            // A cancelled CancellationTokenSource leaks its registrations until disposed; every reconnecting owner belongs here.
            foreach ((string Owner, string Method) o in new[]
                     {
                         ("KMHPatch.Features.Global.GlobalChatClient",   "Start"),
                         ("KMHPatch.Features.Global.KmhDirectoryStream", "Start"),
                         ("KMHPatch.SubProtocol.KmhApiClient",           "RunLoop"),
                     })
            {
                System.Collections.Generic.List<string> calls = KmhIlReader.CallsMadeBy(o.Owner, o.Method);
                bool disposes = calls.Contains("Dispose");
                r.Add(($"lifecycle: {o.Owner.Substring(o.Owner.LastIndexOf('.') + 1)} disposes its cancellation source",
                       disposes,
                       disposes ? $"{o.Method} disposes the source it owns"
                                : "CANCELLED BUT NEVER DISPOSED - one leaks per reconnect"));
            }

            // Driven too: a hundred dial-and-drop cycles against a dead port is what a player switching servers does.
            bool cycled = true;
            string cycleDetail = "";
            try
            {
                for (int i = 0; i < Cycles; i++)
                {
                    SubProtocol.KmhApiClient.Connect("127.0.0.1", 59999, "selftest", "", allowChatFallback: false);
                    SubProtocol.KmhApiClient.Disconnect();
                }
            }
            catch (System.Exception ex) { cycled = false; cycleDetail = ex.GetType().Name + ": " + ex.Message; }
            bool settled = cycled && !SubProtocol.KmhApiClient.Active;
            r.Add(($"lifecycle: {Cycles} transport reconnects leave no live link", settled,
                   settled ? $"{Cycles} dial/drop cycles, nothing current afterwards"
                           : cycled ? "a link survived its own Disconnect" : cycleDetail));

            // Stop must take the source under Start's lock, or a racing Stop cancels whichever one it happened to read.
            System.Collections.Generic.List<string> chatStop =
                KmhIlReader.CallsMadeBy("KMHPatch.Features.Global.GlobalChatClient", "Stop");
            bool locked = chatStop.Contains("Enter") || chatStop.Contains("Exit");
            r.Add(("lifecycle: the chat socket swaps its cancellation source under a lock", locked,
                   locked ? "Stop and Start agree on which source they are holding"
                          : "an unlocked swap can cancel the wrong source"));

            // Clearing every cache repeatedly must be free of side effects: it runs on every server switch.
            bool cleared = true;
            string clearDetail = "";
            try { for (int i = 0; i < Cycles; i++) SubProtocol.KmhClientCaches.ClearAll(); }
            catch (System.Exception ex) { cleared = false; clearDetail = ex.GetType().Name + ": " + ex.Message; }
            r.Add(($"lifecycle: {Cycles} server switches clear every cache without throwing", cleared,
                   cleared ? $"{Cycles} full cache clears" : clearDetail));

            int leftover = Features.Chat.ChatCache.Channels()?.Count ?? 0;
            // ClearAll is hand-written: a per-server cache must be in it or registered below as deliberately surviving.
            var survivesSwitch = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["KmhDirectoryCache"] = "belongs to Global, not to any server",
                ["ChatVideoCache"] = "disk cache with its own eviction policy",
            };

            System.Collections.Generic.List<string> clearAllCalls =
                KmhIlReader.CallsMadeBy("KMHPatch.SubProtocol.KmhClientCaches", "ClearAll");
            var unclearedCaches = new System.Collections.Generic.List<string>();
            foreach (Type t in typeof(KmhClientCaches).Assembly.GetTypes())
            {
                if (t.Name.IndexOf("Cache", StringComparison.Ordinal) < 0) continue;
                if (t.GetMethod("Clear", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, Type.EmptyTypes, null) == null)
                    continue;
                if (survivesSwitch.ContainsKey(t.Name)) continue;
                if (!clearAllCalls.Contains("Clear")) { unclearedCaches.Add(t.Name + " (ClearAll reads as empty)"); break; }
            }
            bool everyCacheAccounted = unclearedCaches.Count == 0 && clearAllCalls.Contains("Clear");
            r.Add(("lifecycle: every per-server cache is cleared on a switch or recorded as surviving one",
                   everyCacheAccounted,
                   everyCacheAccounted
                       ? $"{survivesSwitch.Count} cache(s) deliberately survive a server switch, the rest are cleared"
                       : "a cache is neither cleared nor recorded: " + string.Join(", ", unclearedCaches)));

            r.Add(("lifecycle: a cleared cache is empty, not merely marked stale", leftover == 0,
                   leftover == 0 ? "chat cache holds no channel after the clear"
                                 : $"{leftover} channel(s) survived the clear"));

            // The session generation makes a late frame from the previous account harmless; it must never wrap or stick.
            int before = Features.Global.GlobalChatClient.SessionGeneration;
            for (int i = 0; i < Cycles; i++) Features.Global.GlobalChatClient.Stop();
            int after = Features.Global.GlobalChatClient.SessionGeneration;
            bool moved = after - before == Cycles;
            r.Add(($"lifecycle: {Cycles} sign-outs advance the session generation exactly {Cycles} times", moved,
                   moved ? $"{before} -> {after}" : $"{before} -> {after}, expected +{Cycles}"));

            bool idempotent = true;
            string idemDetail = "";
            try
            {
                for (int i = 0; i < Cycles; i++)
                {
                    Features.Global.KmhDirectoryStream.Stop();
                    Features.Global.GlobalChatClient.Stop();
                }
            }
            catch (System.Exception ex) { idempotent = false; idemDetail = ex.GetType().Name + ": " + ex.Message; }
            r.Add(($"lifecycle: stopping an idle socket {Cycles} times is a no-op", idempotent,
                   idempotent ? "no throw, no state change" : idemDetail));
        }

        // Delete-then-move loses the file if the process stops between the calls (offline notices, server history).
        private static void AtomicFile(List<(string, bool, string)> r)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmh-atomic-" + System.Guid.NewGuid().ToString("N"));
            const string Original = "{\"kept\":true}";
            try
            {
                System.IO.Directory.CreateDirectory(root);
                string path = System.IO.Path.Combine(root, "state.json");

                // A replace keeps the destination's creation time; delete-then-move stamps the temp file's. Process death can't be
                // observed in-process, so this stands in, and the contract check refuses the pattern in source.
                System.IO.File.WriteAllText(path, Original);
                System.DateTime born = System.DateTime.UtcNow.AddDays(-30);
                bool creationTimeIsReal = false;
                try
                {
                    System.IO.File.SetCreationTimeUtc(path, born);
                    creationTimeIsReal = System.IO.File.GetCreationTimeUtc(path) == born;
                }
                catch { }

                Persistence.KmhAtomicFile.Write(path, "{\"new\":true}",
                                                new Persistence.KmhAtomicFile.Options { Durable = false });
                if (creationTimeIsReal)
                {
                    bool inPlace = System.IO.File.GetCreationTimeUtc(path) == born
                                && System.IO.File.ReadAllText(path) == "{\"new\":true}";
                    r.Add(("atomic file: a write replaces the file in place rather than re-creating it", inPlace,
                           inPlace ? "replaced in place" : "THE ORIGINAL FILE WAS DELETED AND RE-CREATED"));
                }
                else
                {
                    // Reported rather than silently skipped: a filesystem with no creation time cannot answer this.
                    r.Add(("atomic file: a write replaces the file in place rather than re-creating it", true,
                           "no creation time on this filesystem - covered by the contract source check"));
                }

                System.IO.File.WriteAllText(path, Original);
                bool threw = false;
                try
                {
                    Persistence.KmhAtomicFile.Write(path, "{\"newer\":true}", new Persistence.KmhAtomicFile.Options
                    {
                        Durable = false,
                        BeforeReplace = _ => throw new System.IO.IOException("power cut"),
                    });
                }
                catch (System.IO.IOException) { threw = true; }
                bool intact = threw && System.IO.File.Exists(path) && System.IO.File.ReadAllText(path) == Original;
                r.Add(("atomic file: a failure before the replace leaves the original file intact", intact,
                       intact ? "original intact" : "THE ORIGINAL WAS LOST OR REPLACED"));

                string[] leftovers = System.IO.Directory.GetFiles(root, "*.tmp");
                r.Add(("atomic file: a failed write leaves no temp file behind", leftovers.Length == 0,
                       leftovers.Length == 0 ? "clean" : string.Join(",", leftovers)));

                // A store's first ever save writes into a folder that does not exist yet.
                string nested = System.IO.Path.Combine(root, "sub", "dir", "first.json");
                Persistence.KmhAtomicFile.Write(nested, Original, new Persistence.KmhAtomicFile.Options { Durable = false });
                r.Add(("atomic file: a first write creates the folder it needs",
                       System.IO.File.Exists(nested) && System.IO.File.ReadAllText(nested) == Original, "created"));

                bool wrote = Persistence.KmhAtomicFile.TryWrite(path, "{\"v\":2}", "self-test",
                                                                new Persistence.KmhAtomicFile.Options { Durable = false });
                r.Add(("atomic file: a successful write reports success and lands",
                       wrote && System.IO.File.ReadAllText(path) == "{\"v\":2}", "replaced"));

                bool refused = Persistence.KmhAtomicFile.TryWrite(root, "x", "self-test",
                                                                  new Persistence.KmhAtomicFile.Options { Durable = false });
                r.Add(("atomic file: a write that cannot happen reports false rather than throwing", !refused,
                       refused ? "CLAIMED A WRITE THAT DID NOT HAPPEN" : "reported"));
            }
            catch (Exception ex) { r.Add(("atomic file: the harness ran", false, ex.Message)); }
            finally { try { System.IO.Directory.Delete(root, true); } catch { } }
        }

        // Membership compares the names RWT refuses a join over; order compares package ids, as ModsConfig.xml lists ids.
        private static void ModSync(List<(string, bool, string)> r)
        {
            Features.Global.ModProfileDto Profile() => new Features.Global.ModProfileDto
            {
                ServerId = new string('a', 32),
                Policy = Features.Global.ModPolicyNames.Enforced,
                Required = new List<string> { "Core", "Royalty", "Harmony" },
                Optional = new List<string> { "NiceTextures" },
                Order = new List<string> { "brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty" },
                OrderHash = "96B1CA5E9008E1930A5106749DFD035B1742A0C5B345C6C71AA111B60FAB45B3",
                Hash = "h1", Revision = 1,
            };

            List<string> Names(params string[] n) => new List<string>(n);
            List<string> Ids(params string[] n) => new List<string>(n);

            List<string> matchingIds = Ids("brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty");
            Features.Global.KmhLocalMods.SeedIdsForTest(matchingIds);
            try
            {
                var exact = Features.Global.ModComparison.Compare(Profile(), Names("Core", "Royalty", "Harmony"));
                r.Add(("modsync: an exact match blocks nothing",
                       !exact.Blocking && !exact.OrderMismatch, exact.Blocking ? "A MATCHING CLIENT WAS REFUSED" : "clean"));

                var missing = Features.Global.ModComparison.Compare(Profile(), Names("Core", "Harmony"));
                r.Add(("modsync: a missing required mod is reported as missing, not as an order problem",
                       missing.Missing.Count == 1 && missing.Missing[0] == "Royalty" && !missing.OrderMismatch,
                       string.Join(",", missing.Missing.ToArray())));

                var banned = Features.Global.ModComparison.Compare(Profile(), Names("Core", "Royalty", "Harmony", "CheatMod"));
                r.Add(("modsync: a mod on neither list is disallowed and never required",
                       banned.Disallowed.Contains("CheatMod") && !banned.Missing.Contains("CheatMod") && banned.Blocking,
                       banned.Disallowed.Contains("CheatMod") ? "refused, not demanded" : "A BANNED MOD WAS DEMANDED"));

                var optAbsent = Features.Global.ModComparison.Compare(Profile(), Names("Core", "Royalty", "Harmony"));
                var optPresent = Features.Global.ModComparison.Compare(Profile(), Names("Core", "Royalty", "Harmony", "NiceTextures"));
                r.Add(("modsync: an optional mod blocks nothing whether it is installed or not",
                       optAbsent.OptionalNotInstalled.Count == 1 && !optAbsent.Blocking
                       && optPresent.OptionalNotInstalled.Count == 0 && !optPresent.Blocking, "optional stays optional"));

                var extra = Features.Global.ModComparison.Compare(Profile(), Names("Core", "Royalty", "Harmony", "SomethingElse"));
                r.Add(("modsync: a mod the server never named is disallowed once, not twice",
                       extra.Disallowed.Count == 1 && extra.Disallowed[0] == "SomethingElse",
                       string.Join(",", extra.Disallowed.ToArray())));

                var allowAll = Profile(); allowAll.Policy = Features.Global.ModPolicyNames.AllowAll;
                r.Add(("modsync: a server enforcing nothing refuses nothing, order or otherwise",
                       !Features.Global.ModComparison.Compare(allowAll, Names("Whatever")).Blocking, "AllowAll stays permissive"));
            }
            finally { Features.Global.KmhLocalMods.SeedIdsForTest(null); }

            var p = Profile();
            bool reordered = Features.Global.ModComparison.OrderDiffers(p,
                Ids("ludeon.rimworld", "brrainz.harmony", "ludeon.rimworld.royalty"));
            bool same = !Features.Global.ModComparison.OrderDiffers(p, matchingIds);
            r.Add(("modsync: the same mods in a different order is an order mismatch", reordered && same,
                   reordered ? "order refused" : "AN ORDER MISMATCH WAS NOT DETECTED"));

            // A player's own extras sit between the named ones and are RimWorld's business, not the server's.
            r.Add(("modsync: a player's own extra mods between named ones is not an order mismatch",
                   !Features.Global.ModComparison.OrderDiffers(p,
                       Ids("brrainz.harmony", "my.own.mod", "ludeon.rimworld", "ludeon.rimworld.royalty")),
                   "named only"));

            r.Add(("modsync: a mod the player does not have is not reported as an order problem",
                   !Features.Global.ModComparison.OrderDiffers(p, Ids("brrainz.harmony", "ludeon.rimworld")),
                   "absent is not misordered"));

            var dupes = Profile();
            dupes.Order = Ids("brrainz.harmony", "brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty");
            r.Add(("modsync: a duplicated order entry does not make the order impossible to satisfy",
                   !Features.Global.ModComparison.OrderDiffers(dupes, matchingIds), "duplicates collapse"));

            // A server that publishes no order refuses nothing new, which is the common case on older RWT.
            var noOrder = Profile(); noOrder.Order.Clear(); noOrder.OrderHash = "";
            r.Add(("modsync: a server that publishes no order refuses nothing new",
                   !Features.Global.ModComparison.OrderDiffers(noOrder,
                       Ids("ludeon.rimworld", "brrainz.harmony")), "older servers unchanged"));

            // The order is carried as package ids and an RWT hash, so a client can prove what it wrote.
            r.Add(("modsync: the manifest carries RWT's own hash of the order document",
                   p.OrderHash.Length == 64 && p.Order.Count == 3
                   && p.Order[0].IndexOf('.') > 0, p.OrderHash.Substring(0, 12)));
        }

        // Global decides what a friend may learn; the client renders exactly that and never infers from an absent field.
        private static void FriendsClient(List<(string, bool, string)> r)
        {
            var body = new Newtonsoft.Json.Linq.JArray
            {
                new Newtonsoft.Json.Linq.JObject
                {
                    ["player_global_id"] = new string('a', 32), ["display_name"] = "Ann",
                    ["username"] = "ann", ["state"] = "Accepted", ["direction"] = "none",
                    ["online"] = true, ["server_id"] = new string('b', 32),
                },
                // Online, but their privacy withheld the server: absent must stay absent, not become "somewhere".
                new Newtonsoft.Json.Linq.JObject
                {
                    ["player_global_id"] = new string('c', 32), ["display_name"] = "Ben",
                    ["state"] = "Accepted", ["direction"] = "none", ["online"] = true,
                },
                new Newtonsoft.Json.Linq.JObject { ["display_name"] = "Nobody" },
            };

            List<Features.Friends.FriendDto> parsed = Features.Friends.KmhFriendsClient.Parse(body);
            bool shape = parsed.Count == 2
                      && parsed[0].Online && parsed[0].ServerId == new string('b', 32)
                      && parsed[1].Online && parsed[1].ServerId == ""
                      && parsed[1].LastSeenUnix == null;
            r.Add(("friends: a withheld server stays withheld, and a row with no id is dropped", shape,
                   shape ? $"{parsed.Count} usable row(s)" : "A FRIEND ROW WAS INVENTED OR WIDENED"));

            // Names come from other players and are drawn with Widgets.Label.
            var hostile = Features.Friends.KmhFriendsClient.Parse(new Newtonsoft.Json.Linq.JArray
            {
                new Newtonsoft.Json.Linq.JObject
                {
                    ["player_global_id"] = new string('d', 32),
                    ["username"] = "<size=400><color=#ff0000>PWN",
                },
            });
            bool inert = hostile.Count == 1 && hostile[0].Shown.IndexOf('<') < 0;
            r.Add(("friends: another player's name is made inert before anything draws it", inert,
                   inert ? hostile[0].Shown : "MARKUP REACHED A LABEL"));

            List<Features.Friends.FriendDto> keep = Features.Friends.KmhFriendsClient.Friends;
            try
            {
                Features.Friends.KmhFriendsClient.SetFriendsForTest(new List<Features.Friends.FriendDto>
                {
                    new Features.Friends.FriendDto { PlayerGlobalId = "a", Online = true,  ServerId = new string('b', 32) },
                    new Features.Friends.FriendDto { PlayerGlobalId = "c", Online = true,  ServerId = "" },
                    new Features.Friends.FriendDto { PlayerGlobalId = "e", Online = false, ServerId = new string('f', 32) },
                });
                var on = Features.Friends.KmhFriendsClient.OnServers();
                int hidden = Features.Friends.KmhFriendsClient.OnlineWithoutServer();
                bool tab = on.Count == 1 && on[0].ServerId == new string('b', 32) && hidden == 1;
                r.Add(("friends: the browser tab shows only an online friend's shown server, and counts the rest as hidden", tab,
                       $"on servers={on.Count} online elsewhere={hidden}"));
            }
            finally { Features.Friends.KmhFriendsClient.SetFriendsForTest(keep); }

            // Both answers: offline Steam is absent either way, so a one-sided check stays green with the seam deleted.
            r.Add(("friends: whether Steam can be read is a decision, not a property of this machine",
                   Features.Friends.KmhSteamFriends.CanRead(false, true)
                && !Features.Friends.KmhSteamFriends.CanRead(true, true)
                && !Features.Friends.KmhSteamFriends.CanRead(false, false), ""));

            // Read() runs on every Friends open, including for players on a non-Steam copy, so it must not throw.
            bool wasNoSteam = Features.Friends.KmhSteamFriends.AssumeNoSteamForTest;
            Features.Friends.KmhSteamFriends.AssumeNoSteamForTest = true;
            List<ulong> none = Features.Friends.KmhSteamFriends.Read();
            Features.Friends.KmhSteamFriends.AssumeNoSteamForTest = wasNoSteam;
            r.Add(("friends: a copy with no Steam reads an empty friend list rather than throwing",
                   none != null && none.Count == 0, ""));

            // CSteamID is reachable only by reflection, so the unwrap is what breaks silently if Steamworks reshapes it.
            r.Add(("friends: a Steam id is unwrapped from whatever shape Steamworks hands back",
                   Features.Friends.KmhSteamFriends.UnwrapSteamId(76561198000000123UL) == 76561198000000123UL
                && Features.Friends.KmhSteamFriends.UnwrapSteamId(null) == 0UL
                && Features.Friends.KmhSteamFriends.UnwrapSteamId("not an id") == 0UL, ""));

            // Against the Steamworks the game loaded: its flags parameter is an enum, so an (int) lookup finds nothing.
            Type steamFriends = Features.Account.KmhSteamInterop.Find("Steamworks.SteamFriends");
            if (steamFriends == null)
                r.Add(KmhSelfTestResult.Skipped("steam friends: the friends-list calls resolve on the loaded Steamworks", "no Steamworks loaded"));
            else
            {
                System.Reflection.MethodInfo fc = Features.Friends.KmhSteamFriends.IntOrEnumMethod(steamFriends, "GetFriendCount", 1);
                System.Reflection.MethodInfo fi = Features.Friends.KmhSteamFriends.IntOrEnumMethod(steamFriends, "GetFriendByIndex", 2);
                object flag = fc == null ? null : Features.Friends.KmhSteamFriends.Arg(fc, 0, 4);
                r.Add(("steam friends: the friends-list calls resolve on the loaded Steamworks",
                       fc != null && fi != null && flag != null && flag.GetType() == fc.GetParameters()[0].ParameterType,
                       $"count={fc != null} byIndex={fi != null} flag={flag?.GetType().Name}"));
            }

            // Save now calls RimWorld Together's own force-save, so it is resolved against the RWT really loaded.
            Type rwtSaves = Features.Treasury.KmhSaveNow.RwtType();
            if (rwtSaves == null)
                r.Add(KmhSelfTestResult.Skipped("treasury: Save now reaches RimWorld Together's own save", "no RimWorld Together loaded"));
            else
                r.Add(("treasury: Save now reaches RimWorld Together's own save",
                       Features.Treasury.KmhSaveNow.ForceSaveOn(rwtSaves) != null, rwtSaves.FullName));
            if (rwtSaves == null)
                r.Add(KmhSelfTestResult.Skipped("saves: an online save is written where RimWorld Together uploads from", "no RimWorld Together loaded"));
            else
            {
                string online = "RT - rwt.example.org-25652 - Ada";
                string chosen, offline;
                try
                {
                    Features.Treasury.KmhSaveNow.OnlineSaveNameForTest = online;
                    chosen = Patches.Patch_GameDataSaveLoader_RwtSaveName.PrefixForTest("Ada's colony");
                    Features.Treasury.KmhSaveNow.OnlineSaveNameForTest = "";
                    offline = Patches.Patch_GameDataSaveLoader_RwtSaveName.PrefixForTest("Ada's colony");
                }
                finally { Features.Treasury.KmhSaveNow.OnlineSaveNameForTest = null; }
                r.Add(("saves: an online save is written where RimWorld Together uploads from, and an offline one keeps its own name",
                       Features.Treasury.KmhSaveNow.SaveNameOn(rwtSaves) != null && chosen == online && offline == "Ada's colony",
                       $"name property {(Features.Treasury.KmhSaveNow.SaveNameOn(rwtSaves) != null ? "found" : "MISSING")}; online '{chosen}', offline '{offline}'"));
            }
            // Unguarded, every join logs "has no usable PawnGroupMakers" for each work site RWT's enemy faction owns.
            RimWorld.FactionDef rtEnemy = Verse.DefDatabase<RimWorld.FactionDef>.GetNamedSilentFail("RTEnemy");
            if (rtEnemy == null)
                r.Add(KmhSelfTestResult.Skipped("pawn groups: a RimWorld Together faction asked for workers it has none of gets none, without an error", "no RimWorld Together loaded"));
            else
            {
                RimWorld.PawnGroupKindDef missing = null;
                foreach (RimWorld.PawnGroupKindDef k in Verse.DefDatabase<RimWorld.PawnGroupKindDef>.AllDefs)
                    if (rtEnemy.pawnGroupMakers == null || !rtEnemy.pawnGroupMakers.Exists(m => m.kindDef == k)) { missing = k; break; }
                RimWorld.PawnGroupKindDef combat = RimWorld.PawnGroupKindDefOf.Combat;
                RimWorld.FactionDef vanilla = RimWorld.FactionDefOf.Pirate;
                bool noWorkers = missing != null && Patches.Patch_PawnGroupMakerUtility_RwtFactions.NoMakerOfKind(rtEnemy, missing);
                bool fights = !Patches.Patch_PawnGroupMakerUtility_RwtFactions.NoMakerOfKind(rtEnemy, combat);
                bool vanillaKept = missing == null || !Patches.Patch_PawnGroupMakerUtility_RwtFactions.NoMakerOfKind(vanilla, missing);
                r.Add(("pawn groups: a RimWorld Together faction asked for workers it has none of gets none, without an error; anything else is vanilla's",
                       noWorkers && fights && vanillaKept, $"RTEnemy {missing?.defName}: {noWorkers}, RTEnemy combat left to vanilla: {fights}, pirates left to vanilla: {vanillaKept}"));
            }
            DateTime savedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            r.Add(("treasury: Save now runs at most once a minute",
                   Features.Treasury.KmhSaveNow.CoolingDown(savedAt, savedAt.AddSeconds(30))
                   && !Features.Treasury.KmhSaveNow.CoolingDown(savedAt, savedAt.AddSeconds(61))
                   && !Features.Treasury.KmhSaveNow.CoolingDown(DateTime.MinValue, savedAt), ""));

            // Nothing may be reported before a credential proves which GlobalUserId this is. The account root points
            // somewhere empty: reading the real store would publish a linked player's location to Global.
            string prevRoot = Features.Account.KmhAccountStore.RootOverrideForTest;
            Features.Account.KmhAccountStore.RootOverrideForTest = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "kmh-unlinked-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Features.Account.KmhAccountStore.ResetForTest();
            Features.Friends.KmhPresenceReporter.Reset();
            Features.Friends.KmhPresenceReporter.Tick();
            bool quiet = !Features.Friends.KmhPresenceReporter.PublishedForTest
                      && Features.Friends.KmhPresenceReporter.LastReportedForTest.Length == 0
                      && Features.Friends.KmhPresenceReporter.ReportEvery > TimeSpan.Zero;
            Features.Account.KmhAccountStore.RootOverrideForTest = prevRoot;
            Features.Account.KmhAccountStore.ResetForTest();
            r.Add(("friends: presence is not reported at all without a linked account", quiet,
                   quiet ? "silent while unlinked" : "PRESENCE WAS REPORTED WITHOUT A CREDENTIAL"));
        }

        // Never render a player as more visible than Global said, nor widen an answer this build does not understand.
        private static void PlayerPrivacy(List<(string, bool, string)> r)
        {
            string F = Features.Account.KmhPrivacyClient.Friends;
            bool safe = Features.Account.KmhPrivacyClient.Audience("Everyone", F) == "Everyone"
                     && Features.Account.KmhPrivacyClient.Audience("Friends", F) == "Friends"
                     && Features.Account.KmhPrivacyClient.Audience("Nobody", F) == "Nobody"
                     // A value from a newer Global, a typo, or a truncated body all land on the fallback.
                     && Features.Account.KmhPrivacyClient.Audience("Public", F) == F
                     && Features.Account.KmhPrivacyClient.Audience("", F) == F
                     && Features.Account.KmhPrivacyClient.Audience(null, F) == F
                     && Features.Account.KmhPrivacyClient.Audience("everyone", F) == F;   // case matters
            r.Add(("privacy: an audience this build does not know reads as the fallback, never as Everyone", safe,
                   safe ? "unknown values narrow" : "AN UNKNOWN PRIVACY VALUE WIDENED THE AUDIENCE"));

            // Defaults before any answer must match what Global applies to a player with no row, or the page misstates it.
            Features.Account.KmhPrivacyClient.Reset();
            bool defaults = Features.Account.KmhPrivacyClient.PresenceVisibility == Features.Account.KmhPrivacyClient.Friends
                         && Features.Account.KmhPrivacyClient.ShowServer
                         && Features.Account.KmhPrivacyClient.FriendRequests == Features.Account.KmhPrivacyClient.Everyone
                         && Features.Account.KmhPrivacyClient.DirectMessages == Features.Account.KmhPrivacyClient.Friends
                         && Features.Account.KmhPrivacyClient.State == Features.Account.PrivacyState.Unknown;
            r.Add(("privacy: the client's defaults are the ones Global applies to a player with no settings",
                   defaults, defaults ? "friends-only presence, open requests" : "THE PAGE AND GLOBAL DISAGREE"));

            // The Account page asks every frame; while Global is down that must not become a request per frame.
            int loads = 0;
            Features.Account.KmhPrivacyClient.LoadForTest = ct =>
            {
                System.Threading.Interlocked.Increment(ref loads);
                return System.Threading.Tasks.Task.CompletedTask;
            };
            try
            {
                Features.Account.KmhPrivacyClient.ResetRetryForTest();
                for (int i = 0; i < 3; i++)
                {
                    Features.Account.KmhPrivacyClient.SetStateForTest(Features.Account.PrivacyState.Unavailable);
                    Features.Account.KmhPrivacyClient.RefreshIfNeeded();
                    DateTime until = DateTime.UtcNow.AddSeconds(2);
                    while (Features.Account.KmhPrivacyClient.Busy && DateTime.UtcNow < until) System.Threading.Thread.Sleep(5);
                }
            }
            finally
            {
                Features.Account.KmhPrivacyClient.LoadForTest = null;
                Features.Account.KmhPrivacyClient.ResetRetryForTest();
                Features.Account.KmhPrivacyClient.Reset();
            }
            r.Add(("privacy: an unavailable Global is retried on a floor, not once per frame", loads == 1,
                   $"{loads} load(s) across 3 frames"));
        }

        // A Global outage is exactly when every player opens the browser, so the retry floor bounds the load.
        private static void GlobalRetryFloor(List<(string, bool, string)> r)
        {
            DateTime now = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

            // A due-check that looks only at the last SUCCESS is permanently true after one failure: a request per frame.
            TimeSpan every = Features.Global.KmhDirectoryClient.AutoRefreshEvery;
            bool floored = !Features.Global.KmhPollGate.DueAt(
                                now.AddSeconds(1), now, default(DateTime), 1, every)
                        && Features.Global.KmhPollGate.DueAt(
                                now.AddSeconds(10), now, default(DateTime), 1, every);
            r.Add(("global: a failed refresh is not retried on the next frame", floored,
                   floored ? "5s floor after one failure" : "A FAILED REFRESH RETRIED IMMEDIATELY"));

            bool escalates = Features.Global.KmhPollGate.RetryFloor(1)
                               < Features.Global.KmhPollGate.RetryFloor(4)
                          && Features.Global.KmhPollGate.RetryFloor(4)
                               <= Features.Global.KmhPollGate.MaxRetry
                          && Features.Global.KmhPollGate.RetryFloor(99)
                               == Features.Global.KmhPollGate.MaxRetry
                          && Features.Global.KmhPollGate.RetryFloor(0) == TimeSpan.Zero;
            r.Add(("global: the retry floor escalates and then stops escalating", escalates,
                   escalates ? "5s to 60s" : "THE RETRY FLOOR DOES NOT ESCALATE OR IS UNBOUNDED"));

            bool healthy = Features.Global.KmhPollGate.DueAt(now.Add(every), now, now, 0, every)
                        && !Features.Global.KmhPollGate.DueAt(now.AddSeconds(1), now, now, 0, every);
            r.Add(("global: a healthy client still refreshes on its own cadence", healthy,
                   healthy ? "20s while it is working" : "THE FLOOR BROKE THE NORMAL REFRESH"));

            // The browser's own gate: one built from a constant declared BELOW it captures TimeSpan.Zero and fires per OnGUI.
            string cacheRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                      "kmh-dirgate-" + System.Guid.NewGuid().ToString("N"));
            string priorRoot = Features.Global.KmhDirectoryCache.RootOverrideForTest;
            try
            {
                System.IO.Directory.CreateDirectory(cacheRoot);
                Features.Global.KmhDirectoryCache.RootOverrideForTest = cacheRoot;
                Features.Global.KmhDirectoryCache.Save(new List<Features.Global.ServerCardDto>
                {
                    new Features.Global.ServerCardDto { ServerId = new string('c', 32), Name = "Cached" },
                });

                Features.Global.KmhDirectoryClient.ResetForTest();
                Features.Global.KmhDirectoryClient.LoadCacheIfEmpty();

                DateTime justNow = DateTime.UtcNow;
                bool throttled = !Features.Global.KmhDirectoryClient.AutoRefreshDue(justNow.AddSeconds(1));
                bool dueLater  = Features.Global.KmhDirectoryClient.AutoRefreshDue(
                                     justNow.Add(Features.Global.KmhDirectoryClient.AutoRefreshEvery).AddSeconds(5));
                r.Add(("global: the browser's own gate honours its refresh interval", throttled && dueLater,
                       throttled ? (dueLater ? "throttled, then due" : "NEVER BECOMES DUE AGAIN")
                                 : "DUE EVERY FRAME - THE GATE CAPTURED A ZERO INTERVAL"));
            }
            catch (Exception ex) { r.Add(("global: the browser's own gate honours its refresh interval", false, ex.Message)); }
            finally
            {
                Features.Global.KmhDirectoryCache.RootOverrideForTest = priorRoot;
                Features.Global.KmhDirectoryClient.ResetForTest();
                try { System.IO.Directory.Delete(cacheRoot, true); } catch { }
            }

            // A non-positive interval is refused at construction rather than becoming an unseen per-frame loop.
            bool rejectsZero = false;
            try { _ = new Features.Global.KmhPollGate(TimeSpan.Zero); }
            catch (ArgumentOutOfRangeException) { rejectsZero = true; }
            r.Add(("global: a poll gate refuses a non-positive interval", rejectsZero,
                   rejectsZero ? "refused" : "A ZERO-INTERVAL GATE WAS ACCEPTED"));

            var slot = Features.Global.KmhPollGate.SlotOnly();
            bool slotHolds = slot.TryEnter(now) && !slot.TryEnter(now);
            slot.Exit();
            bool refusesDue = false;
            try { _ = slot.Due(now); }
            catch (InvalidOperationException) { refusesDue = true; }
            r.Add(("global: a slot-only gate holds the slot and refuses to be asked when it is due",
                   slotHolds && refusesDue,
                   !slotHolds ? "THE SLOT DID NOT HOLD"
                              : (refusesDue ? "slot held, Due refused" : "A SLOT-ONLY GATE ANSWERED Due")));

            // One shared gate, so the friends list cannot drift onto a different floor from the browser's.
            var gate = new Features.Global.KmhPollGate(TimeSpan.FromSeconds(30));
            bool shared = gate.TryEnter(now) && !gate.TryEnter(now);
            gate.Exit();
            bool reenters = gate.TryEnter(now);
            gate.Exit();
            r.Add(("global: the shared gate admits one caller at a time and releases", shared && reenters,
                   shared && reenters ? "one at a time" : "THE IN-FLIGHT SLOT DOES NOT HOLD OR DOES NOT RELEASE"));

            // The attempt must be recorded for the floor to measure from; a gate that never tried is due.
            gate.Reset();
            gate.TryEnter(now);
            gate.Failed();
            gate.Exit();
            bool backsOff = !gate.Due(now.AddSeconds(1)) && gate.Due(now.AddSeconds(10));
            r.Add(("global: a gate that has failed backs off before it is due again", backsOff,
                   backsOff ? "backed off" : "A FAILED GATE WAS IMMEDIATELY DUE"));
        }

        // The cache keys chat on a long: two messages mapping to one long lose the second, and same-second is ordinary.
        private static void GlobalChatOrdering(List<(string, bool, string)> r)
        {
            long baseTicks = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero).UtcTicks;

            Features.Global.GlobalChatEntry a = new Features.Global.GlobalChatEntry
            { MessageId = "aaaaaaaabbbbbbbbccccccccdddddddd", SentAtUnix = 1789200000, SentAtTicks = baseTicks };
            Features.Global.GlobalChatEntry b = new Features.Global.GlobalChatEntry
            { MessageId = "eeeeeeeeffffffff0000000011111111", SentAtUnix = 1789200000, SentAtTicks = baseTicks + 1200 };

            long ia = Features.Chat.GlobalChatBridge.IdFor(a);
            long ib = Features.Chat.GlobalChatBridge.IdFor(b);
            bool ordered = ia < ib;
            r.Add(("global chat: two messages in the same second keep the order they were sent in", ordered,
                   ordered ? "ordered by the exact instant" : "SAME-SECOND MESSAGES ARE ORDERED ARBITRARILY"));

            // The same message twice is the same id, or a reconnect duplicates the whole backlog.
            bool stable = Features.Chat.GlobalChatBridge.IdFor(a) == ia;
            r.Add(("global chat: the same message always maps to the same cache id", stable,
                   stable ? "stable" : "THE CACHE ID IS NOT STABLE"));

            // An older Global sends seconds only; same-second messages must still separate, or a collision drops one.
            Features.Global.GlobalChatEntry oldA = new Features.Global.GlobalChatEntry
            { MessageId = "aaaaaaaabbbbbbbbccccccccdddddddd", SentAtUnix = 1789200000 };
            Features.Global.GlobalChatEntry oldB = new Features.Global.GlobalChatEntry
            { MessageId = "eeeeeeeeffffffff0000000011111111", SentAtUnix = 1789200000 };
            bool wide = Features.Chat.GlobalChatBridge.IdFor(oldA) != Features.Chat.GlobalChatBridge.IdFor(oldB);
            r.Add(("global chat: an older Global that sends seconds only still separates messages", wide,
                   wide ? "separated" : "TWO MESSAGES COLLIDED ON ONE CACHE ID"));
        }

        // Unlinking or switching directory must drop everything that was true only because of the previous identity.
        private static void IdentityTeardown(List<(string, bool, string)> r)
        {
            Features.Global.GlobalChatClient.EnsureRunningForTest();
            bool registered = Features.Global.KmhIdentityTeardown.RegisteredCount >= 3;
            r.Add(("identity: the per-identity caches register their own teardown", registered,
                   registered ? Features.Global.KmhIdentityTeardown.RegisteredCount + " registered"
                              : "NOTHING REGISTERED A TEARDOWN"));

            int ran = 0;
            Features.Global.KmhIdentityTeardown.Register("self-test probe", () => ran++);
            // One cache throwing must not leave the rest of the previous identity loaded.
            Features.Global.KmhIdentityTeardown.Register("self-test thrower",
                () => throw new InvalidOperationException("deliberate"));
            Features.Global.KmhIdentityTeardown.Register("self-test probe 2", () => ran++);
            Features.Global.KmhIdentityTeardown.ForgetIdentity();

            bool all = ran == 2;
            r.Add(("identity: one cache failing to forget does not strand the others", all,
                   all ? "every registered cache ran" : "A TEARDOWN FAILURE STOPPED THE REST"));
        }

        // A Join offered where joining is impossible fails at RWT's door and reads to the player as KMH being broken.
        private static void DiscordActivityAndLink(List<(string, bool, string)> r)
        {
            {
                // DiscordRPC rejects the WHOLE presence when it carries a join secret with no URI scheme registered.
                Features.Global.ServerCardDto joinable = new Features.Global.ServerCardDto
                {
                    ServerId = new string('d', 32), Name = "KMH | Test", JoinEndpoint = "play.example.net:25560",
                    RequiresPassword = false, FromPublicDirectory = true, MaxPlayers = 32, Players = 3,
                    Status = "Online", Official = false, Protocol = SubProtocol.KmhProtocol.CurrentVersion,
                };

                string withoutScheme = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal,
                                                       ServerSessionForTest(joinable, joinable: false)).JoinSecret;
                r.Add(("discord: no join secret is sent when no URI scheme is registered",
                       string.IsNullOrEmpty(withoutScheme),
                       string.IsNullOrEmpty(withoutScheme) ? "" : "A SECRET DISCORDRPC WILL REFUSE THE WHOLE PRESENCE FOR"));

                string withScheme = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal,
                                                    ServerSessionForTest(joinable, joinable: true)).JoinSecret;
                r.Add(("discord: a registered scheme does advertise the join", withScheme.Length > 0,
                       withScheme.Length > 0 ? "" : "the join is never offered at all"));
            }

            KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
            bool wasActivity = s.DiscordShowActivity;
            Features.Discord.KmhPresenceActivity.ResetForTest();
            try
            {
                s.DiscordShowActivity = false;
                Features.Discord.KmhPresenceActivity.Note(Features.Discord.KmhActivity.Marketplace);
                bool offByDefault = Features.Discord.KmhPresenceActivity.Current == Features.Discord.KmhActivity.None;
                r.Add(("discord activity: nothing is published while the setting is off", offByDefault,
                       offByDefault ? "" : "ACTIVITY LEAKED WITH THE SETTING OFF"));

                // Nothing is recorded while off, so switching on cannot publish what the player did before agreeing.
                s.DiscordShowActivity = true;
                bool noBacklog = Features.Discord.KmhPresenceActivity.Current == Features.Discord.KmhActivity.None;
                r.Add(("discord activity: turning it on does not publish what happened while it was off", noBacklog,
                       noBacklog ? "" : "AN ACTIVITY NOTED WHILE OFF APPEARED WHEN IT WAS TURNED ON"));
                s.DiscordShowActivity = false;

                s.DiscordShowActivity = true;
                Features.Discord.KmhPresenceActivity.Note(Features.Discord.KmhActivity.Marketplace);
                Features.Discord.KmhActivity now = Features.Discord.KmhPresenceActivity.Current;
                bool on = now == Features.Discord.KmhActivity.Marketplace
                       && Features.Discord.KmhPresenceActivity.Line(now) == "At the marketplace";
                r.Add(("discord activity: turning it on publishes the generic line", on, now.ToString()));

                // Turning it off has to take effect at once, not when the current activity would have expired.
                s.DiscordShowActivity = false;
                bool offNow = Features.Discord.KmhPresenceActivity.Current == Features.Discord.KmhActivity.None;
                r.Add(("discord activity: turning it off hides an activity already noted", offNow,
                       offNow ? "" : "ACTIVITY SURVIVED THE SETTING BEING TURNED OFF"));
                s.DiscordShowActivity = true;

                Features.Discord.KmhPresenceActivity.SetForTest(Features.Discord.KmhActivity.Auctions,
                                                                DateTime.UtcNow.AddSeconds(-1));
                bool expired = Features.Discord.KmhPresenceActivity.Current == Features.Discord.KmhActivity.None;
                r.Add(("discord activity: a stale activity expires instead of sticking", expired,
                       expired ? "" : "AN EXPIRED ACTIVITY IS STILL BEING PUBLISHED"));

                // Re-noting an activity only moves its deadline, so a per-frame call does not push to Discord 60x a second.
                Features.Discord.KmhPresenceActivity.ResetForTest();
                Features.Discord.KmhPresenceActivity.Note(Features.Discord.KmhActivity.Quests);
                for (int i = 0; i < 100; i++) Features.Discord.KmhPresenceActivity.Note(Features.Discord.KmhActivity.Quests);
                bool coalesced = Features.Discord.KmhPresenceActivity.Current == Features.Discord.KmhActivity.Quests;
                r.Add(("discord activity: repeating the same activity changes nothing", coalesced,
                       coalesced ? "" : "A REPEATED ACTIVITY IS NOT COALESCED"));

                // Fixed text only: interpolating an amount, item or name must fail here, not on someone's Discord profile.
                bool generic = true;
                string offender = "";
                foreach (Features.Discord.KmhActivity a in Enum.GetValues(typeof(Features.Discord.KmhActivity)))
                {
                    if (a == Features.Discord.KmhActivity.None) continue;
                    string line = Features.Discord.KmhPresenceActivity.Line(a);
                    bool ok = line.Length > 0 && line.Length <= 48
                           && line.IndexOf('0') < 0 && line.IndexOf('1') < 0 && line.IndexOf('2') < 0
                           && line.IndexOf('3') < 0 && line.IndexOf('4') < 0 && line.IndexOf('5') < 0
                           && line.IndexOf('6') < 0 && line.IndexOf('7') < 0 && line.IndexOf('8') < 0
                           && line.IndexOf('9') < 0
                           && line.IndexOf('{') < 0 && line.IndexOf('<') < 0;
                    if (!ok) { generic = false; offender = a + "='" + line + "'"; break; }
                }
                r.Add(("discord activity: every line is fixed text with no amounts or markup", generic, offender));

                // Every activity a screen can declare has a line, or a new one renders as an empty status.
                bool everyKindSpoken = true;
                foreach (Features.Discord.KmhActivity a in Enum.GetValues(typeof(Features.Discord.KmhActivity)))
                    if (a != Features.Discord.KmhActivity.None
                        && string.IsNullOrEmpty(Features.Discord.KmhPresenceActivity.Line(a)))
                        everyKindSpoken = false;
                r.Add(("discord activity: every activity kind has a line", everyKindSpoken, ""));

                // The activity takes the state line, so the badge still says "official"; a community server never gets one.
                Features.Global.ServerCardDto Card(bool official) => new Features.Global.ServerCardDto
                {
                    ServerId = new string('c', 32), Name = "KMH | Civilization Reforged",
                    JoinEndpoint = "play.example.net:25560", RequiresPassword = false, FromPublicDirectory = true,
                    MaxPlayers = 32, Players = 3, Status = "Online", Official = official,
                    Protocol = SubProtocol.KmhProtocol.CurrentVersion,
                };

                string treasury = Features.Discord.KmhPresenceActivity.Line(Features.Discord.KmhActivity.Treasury);
                var off = PresenceForTest(Features.Discord.PresenceKind.ServerScreen, Features.Discord.PresenceTier.Medium,
                                          ServerSessionForTest(Card(true)), treasury);
                var com = PresenceForTest(Features.Discord.PresenceKind.ServerScreen, Features.Discord.PresenceTier.Medium,
                                          ServerSessionForTest(Card(false)), treasury);

                bool shown = off.Details == "At the treasury" && com.Details == "At the treasury";
                r.Add(("discord activity: a Colony Nexus screen becomes the headline on both server kinds", shown,
                       shown ? "" : $"official='{off.Details}' community='{com.Details}'"));

                // The badge text is translated, so it is compared with the game's own text for the key, in any language.
                string officialText = UI.KmhText.Get("KMH_Presence_OfficialKmh", "Official Colony Nexus Server");
                var offIdle = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, ServerSessionForTest(Card(true)));
                var comIdle = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, ServerSessionForTest(Card(false)));
                bool badgeHeld = offIdle.SmallImageKey == "kmh_official_server_v1" && offIdle.SmallImageText == officialText
                              && comIdle.SmallImageKey != "kmh_official_server_v1" && com.SmallImageKey != "kmh_official_server_v1"
                              && (comIdle.SmallImageText ?? "").IndexOf("Official", StringComparison.OrdinalIgnoreCase) < 0;
                r.Add(("discord activity: the official badge stays official-only", badgeHeld,
                       badgeHeld ? "" : $"official='{offIdle.SmallImageKey}' community='{comIdle.SmallImageKey}'/'{com.SmallImageKey}'"));

                bool noName = off.Details.IndexOf("Civilization", StringComparison.OrdinalIgnoreCase) < 0
                           && com.Details.IndexOf("Civilization", StringComparison.OrdinalIgnoreCase) < 0;
                r.Add(("discord activity: an activity line never carries the server name", noName, ""));

                // Leaving a server must not leave a status saying what you were doing on it.
                Features.Discord.KmhPresenceActivity.Note(Features.Discord.KmhActivity.Guild);
                Features.Discord.KmhPresenceActivity.Clear();
                bool cleared = Features.Discord.KmhPresenceActivity.Current == Features.Discord.KmhActivity.None;
                r.Add(("discord activity: disconnecting clears it", cleared,
                       cleared ? "" : "AN ACTIVITY SURVIVED THE SESSION THAT PRODUCED IT"));
            }
            finally
            {
                s.DiscordShowActivity = wasActivity;
                Features.Discord.KmhPresenceActivity.ResetForTest();
            }

            // Every reason a Join is withheld must name itself, or "nothing happened" answers a button someone else pressed.
            {
                var seen = new Dictionary<string, Features.Discord.AdvertiseBlock>(StringComparer.Ordinal);
                bool spoken = true, unique = true;
                string clash = "";
                foreach (Features.Discord.AdvertiseBlock b in
                         Enum.GetValues(typeof(Features.Discord.AdvertiseBlock)))
                {
                    string line = Features.Discord.KmhDiscordStatus.LineFor(b);
                    if (string.IsNullOrEmpty(line)) { spoken = false; clash = b.ToString() + " says nothing"; break; }
                    if (seen.TryGetValue(line, out Features.Discord.AdvertiseBlock other))
                    { unique = false; clash = b + " reads the same as " + other; break; }
                    seen[line] = b;
                }
                r.Add(("discord join: every reason a Join is withheld names itself", spoken && unique, clash));

                r.Add(("discord join: the invite hint says who sends the invite",
                       Features.Discord.KmhDiscordStatus.InviteHint().IndexOf("Discord", StringComparison.Ordinal) >= 0,
                       ""));
            }

            // The link code is bound to the game's Steam account; without an id a browser signed in elsewhere could take it.
            {
                ulong wasId = Features.Account.KmhSteamPersona.SteamIdForTest;
                bool wasNoSteam = Features.Account.KmhSteamPersona.AssumeNoSteamForTest;
                try
                {
                    Features.Account.KmhSteamPersona.SteamIdForTest = 76561198000000123UL;
                    r.Add(("account link: the client states the Steam account it is running as",
                           Features.Account.KmhSteamPersona.SteamId64() == 76561198000000123UL,
                           Features.Account.KmhSteamPersona.SteamId64().ToString()));

                    // Steam is told absent rather than assumed absent: in a real game it is running.
                    Features.Account.KmhSteamPersona.SteamIdForTest = 0;
                    Features.Account.KmhSteamPersona.AssumeNoSteamForTest = true;
                    bool noneWithoutSteam = Features.Account.KmhSteamPersona.SteamId64() == 0;
                    r.Add(("account link: with no Steam there is no id to claim", noneWithoutSteam,
                           noneWithoutSteam ? "" : "AN ID WAS INVENTED WITHOUT STEAM"));
                }
                finally
                {
                    Features.Account.KmhSteamPersona.SteamIdForTest = wasId;
                    Features.Account.KmhSteamPersona.AssumeNoSteamForTest = wasNoSteam;
                }
            }

            // Needs no running Steam: the connect string derives from the card, gated exactly as Discord's join is.
            {
                Features.Global.ServerCardDto SteamCard(bool listed, bool password) => new Features.Global.ServerCardDto
                {
                    ServerId = new string('e', 32), Name = "KMH | Test", JoinEndpoint = "play.example.net:25560",
                    RequiresPassword = password, FromPublicDirectory = listed, MaxPlayers = 16, Players = 0,
                    Status = "Online", Official = false, Protocol = SubProtocol.KmhProtocol.CurrentVersion,
                };

                KMHPatchSettings st = KMHPatchMod.SettingsOrDefault;
                bool wasJoin = st.AllowDiscordJoin;
                try
                {
                    st.AllowDiscordJoin = true;
                    string connect = Features.Discord.KmhSteamPresence.ConnectFor(SteamCard(true, false));
                    bool round = Features.Discord.KmhSteamPresence.TryReadServerId(connect, out string backId)
                              && backId == new string('e', 32);
                    r.Add(("steam join: a listed server produces a connect string that reads back", round, connect));

                    r.Add(("steam join: the connect string fits what Steam will accept",
                           connect.Length > 0 && connect.Length <= Features.Discord.KmhSteamPresence.MaxConnectLength,
                           connect.Length + " char(s)"));

                    bool withheld = Features.Discord.KmhSteamPresence.ConnectFor(SteamCard(false, false)).Length == 0
                                 && Features.Discord.KmhSteamPresence.ConnectFor(SteamCard(true, true)).Length == 0
                                 && Features.Discord.KmhSteamPresence.ConnectFor(null).Length == 0;
                    r.Add(("steam join: unlisted, password-locked and unknown servers offer nothing", withheld,
                           withheld ? "" : "A SERVER THAT CANNOT BE JOINED WAS OFFERED TO STEAM FRIENDS"));

                    st.AllowDiscordJoin = false;
                    bool offOff = Features.Discord.KmhSteamPresence.ConnectFor(SteamCard(true, false)).Length == 0;
                    r.Add(("steam join: turning joins off withdraws Steam's offer too", offOff,
                           offOff ? "" : "STEAM STILL OFFERED A JOIN WITH JOINS TURNED OFF"));
                }
                finally { st.AllowDiscordJoin = wasJoin; }

                // The short and long ones are VALID hex, or the length check could be deleted with every test still green.
                bool refuses = !Features.Discord.KmhSteamPresence.TryReadServerId("", out _)
                            && !Features.Discord.KmhSteamPresence.TryReadServerId(null, out _)
                            && !Features.Discord.KmhSteamPresence.TryReadServerId("+kmh abcdef", out _)
                            && !Features.Discord.KmhSteamPresence.TryReadServerId("+kmh " + new string('a', 64), out _)
                            && !Features.Discord.KmhSteamPresence.TryReadServerId("+kmh short", out _)
                            && !Features.Discord.KmhSteamPresence.TryReadServerId("+kmh " + new string('z', 32), out _)
                            && !Features.Discord.KmhSteamPresence.TryReadServerId("rm -rf /", out _);
                r.Add(("steam join: a connect string KMH does not issue is refused", refuses,
                       refuses ? "" : "A CRAFTED CONNECT STRING WAS ACCEPTED"));

                // The tick must not touch Steam when nothing changed, or it writes a key every frame.
                Features.Discord.KmhSteamPresence.SuppressForTest = true;
                try
                {
                    Features.Discord.KmhSteamPresence.ResetForTest();
                    Features.Discord.KmhSteamPresence.Tick();
                    string first = Features.Discord.KmhSteamPresence.LastConnectForTest;
                    for (int i = 0; i < 50; i++) Features.Discord.KmhSteamPresence.Tick();
                    r.Add(("steam join: repeating the tick changes nothing",
                           Features.Discord.KmhSteamPresence.LastConnectForTest == first, ""));
                }
                finally
                {
                    Features.Discord.KmhSteamPresence.SuppressForTest = false;
                    Features.Discord.KmhSteamPresence.ResetForTest();
                }
            }

            // A URL that arrives over the network and is handed to the shell.
            {
                bool accepts = Features.Account.KmhDiscordLinkClient.IsDiscordAuthorizeUrl(
                                   "https://discord.com/oauth2/authorize?client_id=1&state=x");
                bool refuses = !Features.Account.KmhDiscordLinkClient.IsDiscordAuthorizeUrl("")
                            && !Features.Account.KmhDiscordLinkClient.IsDiscordAuthorizeUrl(null)
                            && !Features.Account.KmhDiscordLinkClient.IsDiscordAuthorizeUrl("http://discord.com/oauth2/authorize")
                            && !Features.Account.KmhDiscordLinkClient.IsDiscordAuthorizeUrl("https://discord.com.evil.test/x")
                            && !Features.Account.KmhDiscordLinkClient.IsDiscordAuthorizeUrl("https://evil.test/discord.com")
                            && !Features.Account.KmhDiscordLinkClient.IsDiscordAuthorizeUrl("file:///c:/windows/system32/cmd.exe")
                            && !Features.Account.KmhDiscordLinkClient.IsDiscordAuthorizeUrl("javascript:alert(1)")
                            && !Features.Account.KmhDiscordLinkClient.IsDiscordAuthorizeUrl("steam://run/294100");
                r.Add(("discord link: only an https Discord URL is ever opened", accepts && refuses,
                       accepts ? (refuses ? "" : "A NON-DISCORD URL WOULD BE OPENED") : "the real authorize URL was refused"));

                string bad = Features.Account.KmhDiscordLinkClient.Sanitize(
                                 "<b>na\u0000me</b><color=red>x</color>" + new string('y', 200));
                bool inert = bad.IndexOf('<') < 0 && bad.IndexOf('>') < 0 && bad.IndexOf('\u0000') < 0
                          && bad.Length <= 48;
                r.Add(("discord link: a Discord display name cannot carry markup into the UI", inert, bad));
            }
        }

        private static Features.Discord.KmhDiscordRpc.Presence PresenceForTest(Features.Discord.PresenceKind kind,
            Features.Discord.PresenceTier tier, Features.Discord.PresenceSession session, string label = null, bool allies = false)
        {
            var choice = new Features.Discord.PresenceChoice { Kind = kind, Tier = tier, Label = label, WithAllies = allies, SinceUtc = DateTime.UtcNow };
            return Features.Discord.KmhPresenceFormat.Compose(choice, session, KMHPatchMod.SettingsOrDefault, 1234, DateTime.UtcNow);
        }

        private static Features.Discord.PresenceSession ServerSessionForTest(Features.Global.ServerCardDto card, bool joinable = false)
            => new Features.Discord.PresenceSession
            {
                Playing = true, Multiplayer = true, KmhServer = true, Card = card, JoinAdvertisable = joinable, StartedUtc = DateTime.UtcNow,
            };

        private static void DiscordDynamicPresence(List<(string, bool, string)> r)
        {
            var P = typeof(Features.Discord.PresenceKind);
            DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Features.Discord.PresenceCandidate C(Features.Discord.PresenceKind k, Features.Discord.PresenceTier tier, string label = null)
                => new Features.Discord.PresenceCandidate(k, tier, label);
            var colony = C(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal);
            var research = C(Features.Discord.PresenceKind.Research, Features.Discord.PresenceTier.Normal);
            var raid = C(Features.Discord.PresenceKind.RaidMechanoids, Features.Discord.PresenceTier.Critical);
            var world = C(Features.Discord.PresenceKind.WorldMap, Features.Discord.PresenceTier.Medium);
            var menu = C(Features.Discord.PresenceKind.MainMenu, Features.Discord.PresenceTier.Normal);
            List<Features.Discord.PresenceCandidate> L(params Features.Discord.PresenceCandidate[] c) => new List<Features.Discord.PresenceCandidate>(c);

            {
                var res = new Features.Discord.KmhPresenceResolver();
                for (int i = 0; i <= 15; i++) res.Resolve(L(research), t0.AddSeconds(2 * i));
                var c = res.Resolve(L(research, raid), t0.AddSeconds(32));
                bool took = c.Kind == Features.Discord.PresenceKind.RaidMechanoids && c.Urgent;
                var again = res.Resolve(L(research, raid), t0.AddSeconds(34));
                r.Add(("presence: a raid replaces research at once, and only the switch is urgent", took && !again.Urgent,
                       $"{c.Kind} urgent={c.Urgent} then urgent={again.Urgent}"));
            }

            {
                var res = new Features.Discord.KmhPresenceResolver();
                DateTime t = t0;
                for (int i = 0; i < 30; i++, t = t.AddSeconds(2)) res.Resolve(L(colony, raid), t);
                var held = res.Resolve(L(colony), t.AddSeconds(10));
                var after = res.Resolve(L(colony), t.AddSeconds(20));
                var later = res.Resolve(L(colony), t.AddSeconds(20) + Features.Discord.KmhPresenceResolver.RecoveryFor + TimeSpan.FromSeconds(2));
                bool ok = held.Kind == Features.Discord.PresenceKind.RaidMechanoids
                       && after.Kind == Features.Discord.PresenceKind.Recovering
                       && later.Kind == Features.Discord.PresenceKind.Colony;
                r.Add(("presence: a raid that ends shows recovering, then everyday play", ok, $"{held.Kind} -> {after.Kind} -> {later.Kind}"));
            }

            {
                var res = new Features.Discord.KmhPresenceResolver();
                res.Resolve(L(colony), t0);
                res.Resolve(L(colony, raid), t0.AddSeconds(2));
                res.Resolve(L(colony, raid), t0.AddSeconds(4));
                var after = res.Resolve(L(colony), t0.AddSeconds(40));
                r.Add(("presence: a brief threat leaves no recovery behind", after.Kind == Features.Discord.PresenceKind.Colony, after.Kind.ToString()));
            }

            {
                var res = new Features.Discord.KmhPresenceResolver();
                int changes = 0;
                Features.Discord.PresenceKind last = Features.Discord.PresenceKind.None;
                for (int i = 0; i < 40; i++)
                {
                    var c = res.Resolve(i % 2 == 0 ? L(colony, raid) : L(colony), t0.AddSeconds(2 * i));
                    if (c.Kind != last) { changes++; last = c.Kind; }
                }
                r.Add(("presence: a flickering threat does not flicker the status", changes <= 2, changes + " change(s)"));
            }

            {
                var res = new Features.Discord.KmhPresenceResolver();
                for (int i = 0; i < 5; i++) res.Resolve(L(colony), t0.AddSeconds(2 * i));
                var glance = res.Resolve(L(colony, world), t0.AddSeconds(10));
                res.Resolve(L(colony, world), t0.AddSeconds(12));
                var stay = res.Resolve(L(colony, world), t0.AddSeconds(15));
                bool ok = glance.Kind == Features.Discord.PresenceKind.Colony && stay.Kind == Features.Discord.PresenceKind.WorldMap;
                r.Add(("presence: a new everyday activity waits to settle", ok, $"{glance.Kind} then {stay.Kind}"));
            }

            {
                var res = new Features.Discord.KmhPresenceResolver();
                Features.Discord.PresenceKind first = res.Resolve(L(colony, research), t0).Kind, early = first, turn = first;
                for (int i = 1; i <= 302; i++)
                {
                    Features.Discord.PresenceKind k = res.Resolve(L(colony, research), t0.AddSeconds(2 * i)).Kind;
                    if (i == 270) early = k;
                    if (i == 302) turn = k;
                }
                bool ok = first == early && turn != first;
                r.Add(("presence: equal activities rotate, and only after the interval", ok, $"{first} {early} {turn}"));
            }

            {
                var res = new Features.Discord.KmhPresenceResolver();
                var fallout = C(Features.Discord.PresenceKind.ToxicFallout, Features.Discord.PresenceTier.High);
                var flare = C(Features.Discord.PresenceKind.SolarFlare, Features.Discord.PresenceTier.High);
                res.Resolve(L(colony, fallout), t0);
                var early = res.Resolve(L(colony, fallout, flare), t0.AddSeconds(10));
                var later = res.Resolve(L(colony, fallout, flare), t0.AddSeconds(22));
                bool ok = early.Kind == Features.Discord.PresenceKind.ToxicFallout && later.Kind == Features.Discord.PresenceKind.SolarFlare;
                r.Add(("presence: a newer event of the same weight takes over after a short hold", ok, $"{early.Kind} then {later.Kind}"));
            }

            {
                var res = new Features.Discord.KmhPresenceResolver();
                var flare = C(Features.Discord.PresenceKind.SolarFlare, Features.Discord.PresenceTier.High);
                var fallout = C(Features.Discord.PresenceKind.ToxicFallout, Features.Discord.PresenceTier.High);
                var sick = C(Features.Discord.PresenceKind.Disease, Features.Discord.PresenceTier.High);
                Features.Discord.PresenceKind start = res.Resolve(L(colony, flare, fallout), t0).Kind;
                res.Resolve(L(colony, flare, fallout), t0.AddSeconds(2));
                Features.Discord.PresenceKind next = start;
                for (int i = 2; i <= 9; i++) next = res.Resolve(L(colony, fallout, sick), t0.AddSeconds(2 * i)).Kind;
                bool ok = start == Features.Discord.PresenceKind.SolarFlare && next == Features.Discord.PresenceKind.Disease;
                r.Add(("presence: when a status ends, the newest waiting activity goes next", ok, $"{start} then {next}"));
            }

            {
                var res = new Features.Discord.KmhPresenceResolver();
                for (int i = 0; i < 30; i++) res.Resolve(L(colony, raid), t0.AddSeconds(2 * i));
                var m = res.Resolve(L(menu), t0.AddSeconds(62));
                var next = res.Resolve(L(colony), t0.AddSeconds(70));
                bool ok = m.Kind == Features.Discord.PresenceKind.MainMenu && next.Kind == Features.Discord.PresenceKind.Colony;
                r.Add(("presence: leaving a colony leaves its raid behind", ok, $"{m.Kind} then {next.Kind}"));
            }

            {
                TimeSpan min = TimeSpan.FromSeconds(15);
                DateTime last = t0;
                bool ok = !Features.Discord.KmhRichPresence.ShouldPush(false, true, DateTime.MinValue, t0, min)
                       && Features.Discord.KmhRichPresence.ShouldPush(true, false, DateTime.MinValue, t0, min)
                       && !Features.Discord.KmhRichPresence.ShouldPush(true, false, last, t0.AddSeconds(10), min)
                       && Features.Discord.KmhRichPresence.ShouldPush(true, false, last, t0.AddSeconds(15), min)
                       && !Features.Discord.KmhRichPresence.ShouldPush(true, true, last, t0.AddSeconds(3), min)
                       && Features.Discord.KmhRichPresence.ShouldPush(true, true, last, t0.AddSeconds(6), min);
                r.Add(("presence: updates wait for a change, the interval, or an escalation past the floor", ok, ""));
            }

            {
                string offender = "";
                foreach (Features.Discord.PresenceKind k in Enum.GetValues(P))
                {
                    if (k == Features.Discord.PresenceKind.None) continue;
                    string[] d = Features.Discord.KmhPresenceCatalog.Details(k, "toxic fallout");
                    string[] st = Features.Discord.KmhPresenceCatalog.States(k);
                    bool external = k == Features.Discord.PresenceKind.ServerScreen || k == Features.Discord.PresenceKind.Extension;
                    if (d.Length == 0) { offender = k + " has no headline"; break; }
                    if (!Features.Discord.PresenceKinds.IsPhase(k) && !external && st.Length == 0) { offender = k + " has no second line"; break; }
                    foreach (string line in external ? st : System.Linq.Enumerable.Concat(d, st))
                    {
                        bool fits = line.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(line) <= 128
                                 && line.IndexOf('<') < 0 && line.IndexOf('{') < 0 && !HasDigit(line);
                        if (!fits) { offender = k + "='" + line + "'"; break; }
                    }
                    if (offender.Length > 0) break;
                }
                r.Add(("presence: every activity has fixed wording that fits Discord", offender.Length == 0, offender));

                bool icons = true;
                foreach (Features.Discord.PresenceIcon icon in Enum.GetValues(typeof(Features.Discord.PresenceIcon)))
                    if (icon != Features.Discord.PresenceIcon.None
                        && (Features.Discord.KmhPresenceCatalog.AssetFor(icon).Length == 0 || Features.Discord.KmhPresenceCatalog.IconText(icon).Length == 0))
                        icons = false;
                r.Add(("presence: every activity picture has an asset key and a tooltip", icons, ""));

                string[] lines = Features.Discord.KmhPresenceCatalog.Details(Features.Discord.PresenceKind.Raid, null);
                TimeSpan every = Features.Discord.KmhPresenceCatalog.EventVariantEvery;
                string a = Features.Discord.KmhPresenceCatalog.Pick(lines, 77, t0, t0.AddSeconds(30), every);
                string b = Features.Discord.KmhPresenceCatalog.Pick(lines, 77, t0, t0.AddMinutes(4), every);
                string c = Features.Discord.KmhPresenceCatalog.Pick(lines, 77, t0, t0.AddMinutes(5).AddSeconds(1), every);
                r.Add(("presence: a wording holds for its period, then moves on", a == b && b != c, $"{a} | {b} | {c}"));
            }

            KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
            bool[] was = { s.DiscordShowGameplay, s.DiscordDetailedActivity, s.DiscordShowMultiplayer, s.DiscordShowServerName,
                           s.DiscordShowPrivateServerName, s.DiscordShowColonyInfo, s.DiscordShowElapsed, s.AllowDiscordJoin };
            try
            {
                s.DiscordShowGameplay = s.DiscordDetailedActivity = s.DiscordShowMultiplayer = s.DiscordShowServerName = true;
                s.DiscordShowPrivateServerName = s.DiscordShowColonyInfo = false;
                s.DiscordShowElapsed = s.AllowDiscordJoin = true;
                var card = new Features.Global.ServerCardDto
                {
                    ServerId = new string('9', 32), Name = "Colony Nexus | Test Server", JoinEndpoint = "play.example.net:25560",
                    RequiresPassword = false, FromPublicDirectory = true, MaxPlayers = 40, Players = 3, Status = "Online",
                    Protocol = SubProtocol.KmhProtocol.CurrentVersion,
                };
                var single = new Features.Discord.PresenceSession { Playing = true, StartedUtc = DateTime.UtcNow, Colonists = 7, Day = 34 };
                bool Mentions(Features.Discord.KmhDiscordRpc.Presence p, string text)
                    => (p.Details + "|" + p.State + "|" + p.SmallImageText + "|" + p.LargeImageText).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;

                var raidMp = PresenceForTest(Features.Discord.PresenceKind.Raid, Features.Discord.PresenceTier.Critical, ServerSessionForTest(card, true));
                bool raidShown = Array.IndexOf(Features.Discord.KmhPresenceCatalog.Details(Features.Discord.PresenceKind.Raid, null), raidMp.Details) >= 0
                              && raidMp.State == card.Name && raidMp.SmallImageKey == "cn_combat_v1" && raidMp.SmallImageText.Length > 0;
                r.Add(("presence: a raid on a server leads, with the server beneath and the combat picture", raidShown,
                       $"'{raidMp.Details}' / '{raidMp.State}' / {raidMp.SmallImageKey}"));

                var sp = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, single);
                bool spOk = !Mentions(sp, "multiplayer") && !Mentions(sp, "server") && string.IsNullOrEmpty(sp.PartyId)
                         && string.IsNullOrEmpty(sp.JoinSecret) && sp.State.Length > 0;
                r.Add(("presence: singleplayer never claims multiplayer", spOk, $"'{sp.Details}' / '{sp.State}'"));

                s.DiscordShowMultiplayer = false;
                var hidden = PresenceForTest(Features.Discord.PresenceKind.Raid, Features.Discord.PresenceTier.Critical, ServerSessionForTest(card, true));
                bool hid = !Mentions(hidden, card.Name) && !Mentions(hidden, "multiplayer") && string.IsNullOrEmpty(hidden.PartyId)
                        && string.IsNullOrEmpty(hidden.JoinSecret) && !Mentions(hidden, "Singleplayer");
                r.Add(("presence: with multiplayer hidden there is no server, party, Join or false singleplayer", hid,
                       hid ? "" : $"LEAKED: '{hidden.Details}' / '{hidden.State}' party='{hidden.PartyId}'"));
                string market = Features.Discord.KmhPresenceActivity.Line(Features.Discord.KmhActivity.Marketplace);
                var screen = PresenceForTest(Features.Discord.PresenceKind.ServerScreen, Features.Discord.PresenceTier.Medium,
                                             ServerSessionForTest(card, true), market);
                r.Add(("presence: with multiplayer hidden a Colony Nexus screen is not named either", screen.Details != market, screen.Details));
                s.DiscordShowMultiplayer = true;

                s.DiscordShowServerName = false;
                var unnamed = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, ServerSessionForTest(card));
                r.Add(("presence: with server names off a public server is not named", !Mentions(unnamed, card.Name), unnamed.State));
                s.DiscordShowServerName = true;

                var priv = ServerSessionForTest(null);
                priv.PrivateServerName = "Secret Base";
                var p1 = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, priv);
                s.DiscordShowPrivateServerName = true;
                var p2 = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, priv);
                s.DiscordShowPrivateServerName = false;
                r.Add(("presence: a private server is named only when the player opts in",
                       !Mentions(p1, "Secret Base") && Mentions(p2, "Secret Base") && string.IsNullOrEmpty(p1.PartyId),
                       $"off='{p1.State}' on='{p2.State}'"));

                s.DiscordDetailedActivity = false;
                var simple = PresenceForTest(Features.Discord.PresenceKind.Raid, Features.Discord.PresenceTier.Critical, single);
                s.DiscordDetailedActivity = true;
                s.DiscordShowGameplay = false;
                var noGame = PresenceForTest(Features.Discord.PresenceKind.Raid, Features.Discord.PresenceTier.Critical, single);
                s.DiscordShowGameplay = true;
                string[] raidLines = Features.Discord.KmhPresenceCatalog.Details(Features.Discord.PresenceKind.Raid, null);
                bool generic = Array.IndexOf(raidLines, simple.Details) < 0 && Array.IndexOf(raidLines, noGame.Details) < 0
                            && string.IsNullOrEmpty(simple.SmallImageKey) && string.IsNullOrEmpty(noGame.SmallImageKey);
                r.Add(("presence: simple mode and gameplay off never show what is happening", generic, $"'{simple.Details}' '{noGame.Details}'"));

                s.DiscordShowElapsed = false;
                var noClock = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, single);
                s.DiscordShowElapsed = true;
                var clock = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, single);
                r.Add(("presence: the elapsed clock follows its setting", !noClock.StartedUtc.HasValue && clock.StartedUtc.HasValue, ""));

                var quiet = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, single);
                s.DiscordShowColonyInfo = true;
                var counted = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, single);
                var onServer = ServerSessionForTest(card);
                onServer.Colonists = 7; onServer.Day = 34;
                var served = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, onServer);
                s.DiscordShowColonyInfo = false;
                r.Add(("presence: on a server the colony size goes on the logo's tooltip and the server keeps its line",
                       served.State == card.Name && served.LargeImageText.IndexOf("34", StringComparison.Ordinal) >= 0,
                       $"state='{served.State}' tooltip='{served.LargeImageText}'"));
                r.Add(("presence: colony size appears only when the player opts in",
                       !HasDigit(quiet.State) && counted.State.IndexOf("7", StringComparison.Ordinal) >= 0
                       && counted.State.IndexOf("34", StringComparison.Ordinal) >= 0,
                       $"off='{quiet.State}' on='{counted.State}'"));

                var connecting = new Features.Discord.PresenceSession { Playing = false, JoinServerPublic = true, JoinServerName = card.Name };
                var conn = Features.Discord.KmhPresenceFormat.Compose(new Features.Discord.PresenceChoice { Kind = Features.Discord.PresenceKind.Connecting },
                                                                      connecting, s, 1, DateTime.UtcNow);
                var menuP = Features.Discord.KmhPresenceFormat.Compose(new Features.Discord.PresenceChoice { Kind = Features.Discord.PresenceKind.MainMenu },
                                                                       new Features.Discord.PresenceSession(), s, 1, DateTime.UtcNow);
                bool phases = conn.Details == UI.KmhText.Get("KMH_RP_Connecting", "Connecting to a server") && conn.State == card.Name
                           && menuP.Details == UI.KmhText.Get("KMH_RP_Menu", "In the main menu") && string.IsNullOrEmpty(menuP.SmallImageKey);
                r.Add(("presence: connecting names a public server; the menu says only the menu", phases, $"'{conn.Details}' / '{conn.State}'"));

                var allies = PresenceForTest(Features.Discord.PresenceKind.Raid, Features.Discord.PresenceTier.Critical, single, allies: true);
                r.Add(("presence: allies arriving change the raid's second line", allies.State == Features.Discord.KmhPresenceCatalog.AlliesLine(), allies.State));

                // Every composed line goes through the real DiscordRPC types, where an over-long field is refused outright.
                if (Features.Discord.KmhDiscordRpc.Available)
                {
                    string refused = "";
                    foreach (Features.Discord.PresenceKind k in Enum.GetValues(P))
                    {
                        if (k == Features.Discord.PresenceKind.None) continue;
                        foreach (var ses in new[] { single, ServerSessionForTest(card, true) })
                        {
                            var p = PresenceForTest(k, Features.Discord.PresenceTier.High, ses, new string('x', 300));
                            object built = Features.Discord.KmhDiscordRpc.Build(p);
                            string bd = built?.GetType().GetProperty("Details")?.GetValue(built) as string ?? "";
                            string bs = built?.GetType().GetProperty("State")?.GetValue(built) as string ?? "";
                            if (built == null || bd != p.Details || bs != (p.State ?? "")) { refused = k + $" '{bd}'/'{bs}'"; break; }
                        }
                        if (refused.Length > 0) break;
                    }
                    r.Add(("presence: every status builds with the real DiscordRPC library", refused.Length == 0, refused));
                }
            }
            finally
            {
                s.DiscordShowGameplay = was[0]; s.DiscordDetailedActivity = was[1]; s.DiscordShowMultiplayer = was[2];
                s.DiscordShowServerName = was[3]; s.DiscordShowPrivateServerName = was[4]; s.DiscordShowColonyInfo = was[5];
                s.DiscordShowElapsed = was[6]; s.AllowDiscordJoin = was[7];
            }

            {
                bool medical = Features.Discord.KmhPresenceSignals.MedicalKind(0, 5) == Features.Discord.PresenceKind.None
                            && Features.Discord.KmhPresenceSignals.MedicalKind(1, 5) == Features.Discord.PresenceKind.InjuredColonists
                            && Features.Discord.KmhPresenceSignals.MedicalKind(2, 10) == Features.Discord.PresenceKind.InjuredColonists
                            && Features.Discord.KmhPresenceSignals.MedicalKind(2, 4) == Features.Discord.PresenceKind.MedicalEmergency;
                r.Add(("presence: one downed colonist is an injury, half the colony is an emergency", medical, ""));

                bool outbreak = !Features.Discord.KmhPresenceSignals.Outbreak(1, 1) && !Features.Discord.KmhPresenceSignals.Outbreak(2, 20)
                             && Features.Discord.KmhPresenceSignals.Outbreak(2, 8) && Features.Discord.KmhPresenceSignals.Outbreak(3, 30);
                r.Add(("presence: one sick colonist is not an outbreak", outbreak, ""));

                bool food = !Features.Discord.KmhPresenceSignals.FoodShort(100f, 10, false)
                         && Features.Discord.KmhPresenceSignals.FoodShort(30f, 10, false)
                         && Features.Discord.KmhPresenceSignals.FoodShort(50f, 10, true)
                         && !Features.Discord.KmhPresenceSignals.FoodShort(61f, 10, true)
                         && !Features.Discord.KmhPresenceSignals.FoodShort(0f, 0, false);
                r.Add(("presence: a food shortage uses the game's low-food line per colonist, with room to recover", food, ""));

                bool raids = Features.Discord.KmhPresenceSignals.RaidKind(1, 4, 0, 0, 0, false) == Features.Discord.PresenceKind.RaidMechanoids
                          && Features.Discord.KmhPresenceSignals.RaidKind(0, 2, 0, 0, 2, false) == Features.Discord.PresenceKind.RaidEntities
                          && Features.Discord.KmhPresenceSignals.RaidKind(5, 0, 0, 0, 0, true) == Features.Discord.PresenceKind.DropPods
                          && Features.Discord.KmhPresenceSignals.RaidKind(1, 0, 0, 6, 0, false) == Features.Discord.PresenceKind.RaidManhunters
                          && Features.Discord.KmhPresenceSignals.RaidKind(3, 0, 0, 0, 0, false) == Features.Discord.PresenceKind.Raid;
                r.Add(("presence: a raid is named for who is mostly in it", raids, ""));

                bool lone = Features.Discord.KmhPresenceSignals.LoneAnimal(0, 0, 0, 1, 0)
                         && !Features.Discord.KmhPresenceSignals.LoneAnimal(0, 0, 0, 2, 0)
                         && !Features.Discord.KmhPresenceSignals.LoneAnimal(1, 0, 0, 1, 0);
                r.Add(("presence: one animal taking revenge is not a manhunter pack", lone, ""));

                // Read from the loaded defs: counting cryptosleep sickness makes every new colony look like an outbreak.
                bool IllnessDef(string name)
                {
                    Verse.HediffDef d = Verse.DefDatabase<Verse.HediffDef>.GetNamedSilentFail(name);
                    return d != null && Features.Discord.KmhPresenceSignals.IsIllness(d.makesSickThought, d.tendable);
                }
                bool ill = IllnessDef("Flu") && IllnessDef("Plague") && !IllnessDef("CryptosleepSickness") && !IllnessDef("ToxicBuildup");
                r.Add(("presence: an outbreak counts real illnesses, not cryptosleep sickness or toxic buildup", ill,
                       $"flu={IllnessDef("Flu")} cryo={IllnessDef("CryptosleepSickness")} toxic={IllnessDef("ToxicBuildup")}"));

                var evs = new List<Features.World.Dto.WorldEventDto>
                {
                    new Features.World.Dto.WorldEventDto { Type = Features.World.Dto.WorldEventDto.WorldWeather, StartedUtcTicks = 30 },
                    new Features.World.Dto.WorldEventDto { Type = Features.World.Dto.WorldEventDto.TaxHoliday, StartedUtcTicks = 10 },
                    new Features.World.Dto.WorldEventDto { Type = Features.World.Dto.WorldEventDto.MarketBoom, StartedUtcTicks = 20 },
                };
                var weatherOnly = new List<Features.World.Dto.WorldEventDto> { evs[0] };
                string shownEvent = Features.Discord.KmhPresenceSignals.EventToShow(evs);
                r.Add(("presence: a server event is named by the newest one, and world weather is left to its condition",
                       shownEvent == Features.World.Dto.WorldEventDto.MarketBoom
                       && Features.Discord.KmhPresenceSignals.EventToShow(weatherOnly) == null
                       && Features.Discord.KmhPresenceCatalog.ServerEventLine(Features.World.Dto.WorldEventDto.TaxHoliday)
                          != Features.Discord.KmhPresenceCatalog.ServerEventLine("no_such_event"), shownEvent ?? "null"));

                // Background states must let what the player is doing through; at a higher tier they hide it all session.
                bool Surfaces(Features.Discord.PresenceCandidate background, Features.Discord.PresenceCandidate doing, int minutes)
                {
                    var res = new Features.Discord.KmhPresenceResolver();
                    var live = new List<Features.Discord.PresenceCandidate> { background, doing };
                    DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                    for (int s = 0; s <= minutes * 60; s += 2)
                        if (res.Resolve(live, t0.AddSeconds(s)).Kind == doing.Kind) return true;
                    return false;
                }
                bool eventYields = Surfaces(new Features.Discord.PresenceCandidate(Features.Discord.PresenceKind.ServerEvent,
                                                Features.Discord.KmhPresenceSignals.ServerEventTier, Features.World.Dto.WorldEventDto.TaxHoliday),
                                            new Features.Discord.PresenceCandidate(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal), 11);
                bool foodYields = Surfaces(new Features.Discord.PresenceCandidate(Features.Discord.PresenceKind.FoodShortage,
                                               Features.Discord.KmhPresenceSignals.FoodShortageTier),
                                           new Features.Discord.PresenceCandidate(Features.Discord.PresenceKind.ServerScreen, Features.Discord.PresenceTier.Medium, "At the marketplace"), 5);
                r.Add(("presence: a server event and low food give way to what the player is doing", eventYields && foodYields,
                       $"event={eventYields} food={foodYields}"));

                bool lords = Features.Discord.KmhPresenceSignals.ClassifyLord(typeof(RimWorld.LordJob_Siege)) == Features.Discord.KmhPresenceSignals.LordKind.Siege
                          && Features.Discord.KmhPresenceSignals.ClassifyLord(typeof(RimWorld.LordJob_BestowingCeremony)) == Features.Discord.KmhPresenceSignals.LordKind.Royal
                          && Features.Discord.KmhPresenceSignals.ClassifyLord(typeof(RimWorld.LordJob_Joinable_Concert)) == Features.Discord.KmhPresenceSignals.LordKind.Ritual
                          && Features.Discord.KmhPresenceSignals.ClassifyLord(typeof(RimWorld.LordJob_MechanoidsDefend)) == Features.Discord.KmhPresenceSignals.LordKind.MechCluster
                          && Features.Discord.KmhPresenceSignals.ClassifyLord(typeof(RimWorld.LordJob_AssaultColony)) == Features.Discord.KmhPresenceSignals.LordKind.None;
                r.Add(("presence: lord jobs are recognised through their base classes", lords, ""));

                Verse.GameConditionDef fallout = Verse.DefDatabase<Verse.GameConditionDef>.GetNamedSilentFail("ToxicFallout");
                Verse.GameConditionDef flare = Verse.DefDatabase<Verse.GameConditionDef>.GetNamedSilentFail("SolarFlare");
                Verse.GameConditionDef aurora = Verse.DefDatabase<Verse.GameConditionDef>.GetNamedSilentFail("Aurora");
                Verse.GameConditionDef drone = Verse.DefDatabase<Verse.GameConditionDef>.GetNamedSilentFail("PsychicDrone");
                Verse.GameConditionDef soothe = Verse.DefDatabase<Verse.GameConditionDef>.GetNamedSilentFail("PsychicSoothe");
                Verse.GameConditionDef dark = Verse.DefDatabase<Verse.GameConditionDef>.GetNamedSilentFail("UnnaturalDarkness");
                Verse.GameConditionDef skies = Verse.DefDatabase<Verse.GameConditionDef>.GetNamedSilentFail("DarkenedSkies");
                Features.Discord.PresenceKind K(Verse.GameConditionDef d) => Features.Discord.KmhPresenceSignals.HazardKind(d);
                bool conditions = fallout != null && flare != null && drone != null && soothe != null
                               && K(fallout) == Features.Discord.PresenceKind.ToxicFallout
                               && K(flare) == Features.Discord.PresenceKind.SolarFlare
                               && K(drone) == Features.Discord.PresenceKind.PsychicDrone
                               && K(soothe) == Features.Discord.PresenceKind.None
                               && (aurora == null || K(aurora) == Features.Discord.PresenceKind.None)
                               && (dark == null || K(dark) == Features.Discord.PresenceKind.Hazard)
                               && (skies == null || K(skies) == Features.Discord.PresenceKind.Hazard);
                r.Add(("presence: conditions are read from the game's own defs, and a pleasant one is no disaster", conditions,
                       $"soothe={K(soothe)} aurora={K(aurora)} dark={(dark == null ? "absent" : K(dark).ToString())} skies={(skies == null ? "absent" : K(skies).ToString())}"));

                string label = Features.Discord.KmhPresenceSignals.CleanLabel("<color=red>toxic</color>\u0007 fallout");
                string ext = Features.Discord.KmhPresenceSignals.CleanExtensionText("<b>Defending</b> " + new string('z', 200));
                bool clean = label == "toxic fallout" && ext.IndexOf('<') < 0 && System.Text.Encoding.UTF8.GetByteCount(ext) <= 64;
                r.Add(("presence: outside text loses markup and is capped", clean, $"'{label}' / {ext.Length} chars"));
            }

            {
                string disabled = null;
                Action<string> note = id => disabled = id;
                KMH.Sdk.Client.Apis.PresenceProviders.ProviderDisabled += note;
                try
                {
                    KMH.Sdk.Client.Apis.PresenceProviders.Register("kmh.selftest.ok", () => new KMH.Sdk.Client.Apis.PresenceClaim { Details = "Guarding the outpost" });
                    KMH.Sdk.Client.Apis.PresenceProviders.Register("kmh.selftest.bad", () => throw new InvalidOperationException("x"));
                    int asked = 0;
                    for (int i = 0; i < 4; i++)
                        foreach (KMH.Sdk.Client.Apis.PresenceClaim c in KMH.Sdk.Client.Apis.PresenceProviders.Collect())
                            if (c.Details == "Guarding the outpost") asked++;
                    r.Add(("presence: an extension's activity is collected, and a failing one is dropped",
                           asked == 4 && disabled == "kmh.selftest.bad", $"asked={asked} disabled={disabled ?? "none"}"));
                }
                finally
                {
                    KMH.Sdk.Client.Apis.PresenceProviders.Unregister("kmh.selftest.ok");
                    KMH.Sdk.Client.Apis.PresenceProviders.Unregister("kmh.selftest.bad");
                    KMH.Sdk.Client.Apis.PresenceProviders.ProviderDisabled -= note;
                }
            }

            // In a running game the detector always finds something, and at the menu it reports one phase only.
            {
                var found = new List<Features.Discord.PresenceCandidate>();
                Features.Discord.KmhPresenceSignals.Sample(found, DateTime.UtcNow, true);
                bool playing = Verse.Current.ProgramState == Verse.ProgramState.Playing;
                bool ok = (playing ? found.Count > 0 : found.Count == 1 && Features.Discord.PresenceKinds.IsPhase(found[0].Kind))
                       && Features.Discord.KmhPresenceSignals.FailedProbeCount == 0;
                r.Add(("presence: the detector reads the live game without failing", ok,
                       string.Join(",", found.ConvertAll(c => c.Kind + "/" + c.Tier).ToArray())));
            }
        }

        private static bool HasDigit(string s)
        {
            foreach (char c in s ?? "") if (char.IsDigit(c)) return true;
            return false;
        }

        private static void DiscordPresence(List<(string, bool, string)> r)
        {
            // Discord draws no image without an asset key and a misspelt key draws nothing, so keys and id are literal.
            {
                var built = Features.Discord.KmhRichPresence.Build();
                bool asked = built.LargeImageKey == "kmh_logo_v1";
                r.Add(("discord: the status asks for the uploaded logo by its exact key", asked,
                       asked ? built.LargeImageKey : "WRONG OR MISSING LOGO KEY: '" + built.LargeImageKey + "'"));

                r.Add(("discord: the large image names the product",
                       built.LargeImageText.StartsWith("KMH: Colony Nexus", StringComparison.Ordinal), built.LargeImageText));

                bool id = Features.Discord.KmhRichPresence.LooksLikeApplicationId(
                              KMHPatchSettings.DefaultDiscordAppId)
                       && KMHPatchSettings.DefaultDiscordAppId == "1459619253010563327";
                r.Add(("discord: the default application id is the KMH one and is a valid snowflake", id,
                       KMHPatchSettings.DefaultDiscordAppId));

                // A bot token or client secret shipped in a mod would be a credential handed to every player.
                bool noSecret = !Features.Discord.KmhRichPresence.NeedsDiscordSecret;
                r.Add(("discord: rich presence needs no bot token or client secret", noSecret,
                       noSecret ? "application id only" : "THE CLIENT IS ASKING FOR A DISCORD SECRET"));
            }

            {
                Features.Global.ServerCardDto Card(bool official) => new Features.Global.ServerCardDto
                {
                    ServerId = new string('b', 32), Name = "KMH | Civilization Reforged",
                    JoinEndpoint = "play.example.net:25560", RequiresPassword = false, FromPublicDirectory = true,
                    MaxPlayers = 32, Players = 3, Status = "Online", Official = official,
                    Protocol = SubProtocol.KmhProtocol.CurrentVersion,
                };

                var off = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, ServerSessionForTest(Card(true)));
                string officialText = UI.KmhText.Get("KMH_Presence_OfficialKmh", "Official Colony Nexus Server");
                bool badged = off.SmallImageKey == "kmh_official_server_v1"
                           && off.SmallImageText == officialText
                           && off.State == "KMH | Civilization Reforged";
                r.Add(("discord: an official server carries the official badge and says so", badged,
                       badged ? "" : $"key='{off.SmallImageKey}' state='{off.State}' details='{off.Details}'"));

                var com = PresenceForTest(Features.Discord.PresenceKind.Colony, Features.Discord.PresenceTier.Normal, ServerSessionForTest(Card(false)));
                bool clean = com.SmallImageKey != "kmh_official_server_v1"
                          && (com.SmallImageText ?? "").IndexOf("Official", StringComparison.OrdinalIgnoreCase) < 0
                          && com.State.IndexOf("Official", StringComparison.OrdinalIgnoreCase) < 0;
                r.Add(("discord: a community server is never dressed as an official one", clean,
                       clean ? "" : $"COMMUNITY SERVER LOOKS OFFICIAL: key='{com.SmallImageKey}' state='{com.State}'"));

                r.Add(("discord: every presence state carries the logo",
                       off.LargeImageKey == "kmh_logo_v1" && com.LargeImageKey == "kmh_logo_v1", ""));
            }

            KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
            bool wasOn = s.AllowDiscordJoin, wasPresence = s.DiscordPresenceEnabled, wasPrivate = s.DiscordShowPrivateServerName;
            string wasAppId = s.DiscordAppId;
            s.AllowDiscordJoin = true;
            try
            {
                Features.Global.ServerCardDto Open() => new Features.Global.ServerCardDto
                {
                    ServerId = new string('a', 32), Name = "Open", JoinEndpoint = "play.example.net:25560",
                    RequiresPassword = false, FromPublicDirectory = true, MaxPlayers = 32, Players = 3,
                    Status = "Online", Protocol = SubProtocol.KmhProtocol.CurrentVersion,
                    ModPolicy = Features.Global.ModPolicyNames.AllowAll,
                };

                r.Add(("presence: a public, open, addressable server may be joined",
                       Features.Discord.KmhJoinGate.MayAdvertise(Open()), "join offered"));

                var locked = Open(); locked.RequiresPassword = true;
                var unlisted = Open(); unlisted.FromPublicDirectory = false;
                var addressless = Open(); addressless.JoinEndpoint = "";
                var malformed = Open(); malformed.JoinEndpoint = "play.example.net";   // no port

                bool refused = !Features.Discord.KmhJoinGate.MayAdvertise(locked)
                            && !Features.Discord.KmhJoinGate.MayAdvertise(unlisted)
                            && !Features.Discord.KmhJoinGate.MayAdvertise(addressless)
                            && !Features.Discord.KmhJoinGate.MayAdvertise(malformed)
                            && !Features.Discord.KmhJoinGate.MayAdvertise(null);
                r.Add(("presence: a password, an unlisted server or no address each refuse the join on their own",
                       refused, refused ? "all four refused" : "A JOIN WAS OFFERED THAT RWT WOULD REFUSE"));

                // Capacity changes by the second and the joiner re-checks it, so advertising it keeps a Join from flickering.
                var full = Open(); full.Players = full.MaxPlayers;
                r.Add(("presence: a full server still advertises, and the joiner is the one who refuses",
                       Features.Discord.KmhJoinGate.MayAdvertise(full)
                       && Features.Discord.KmhJoinGate.MayJoin(full, true).Compat == Features.Global.CompatState.Full,
                       "advertised, refused at click time"));

                s.AllowDiscordJoin = false;
                r.Add(("presence: turning joins off refuses even a perfectly joinable server",
                       !Features.Discord.KmhJoinGate.MayAdvertise(Open())
                       && Features.Discord.KmhJoinGate.MayJoin(Open(), true).Refusal == Features.Discord.JoinRefusal.JoinsOff,
                       "the setting wins on both sides"));
                s.AllowDiscordJoin = true;

                // A Global that predates the field cannot say a server is open, so absent must read as locked.
                var older = Features.Global.DirectoryParse.Cards(new Newtonsoft.Json.Linq.JObject
                {
                    ["servers"] = new Newtonsoft.Json.Linq.JArray
                    {
                        new Newtonsoft.Json.Linq.JObject
                        {
                            ["server_id"] = new string('e', 32), ["join_endpoint"] = "play.example.net:25560",
                        },
                    },
                }, out _);
                r.Add(("presence: a directory that never reported the password field reads as locked",
                       older.Count == 1 && older[0].RequiresPassword
                       && !Features.Discord.KmhJoinGate.MayAdvertise(older[0]),
                       "absent means locked, not open"));

                // The secret carries an id and nothing else: an endpoint in it would be somewhere to send a player.
                string secret = Features.Discord.KmhPresenceJoin.Secret(Open());
                bool shape = secret == Features.Discord.KmhPresenceJoin.Prefix + new string('a', 32)
                          && secret.IndexOf("example.net", StringComparison.Ordinal) < 0;
                r.Add(("presence: the join secret names a server, never an address", shape, secret));

                bool rejects = !Features.Discord.KmhPresenceJoin.TryReadServerId("kmh1:../../etc", out _)
                            && !Features.Discord.KmhPresenceJoin.TryReadServerId("play.evil.net:25560", out _)
                            && !Features.Discord.KmhPresenceJoin.TryReadServerId("kmh1:" + new string('a', 31), out _)
                            && !Features.Discord.KmhPresenceJoin.TryReadServerId("kmh1:" + new string('Z', 32), out _)
                            && !Features.Discord.KmhPresenceJoin.TryReadServerId(null, out _)
                            && Features.Discord.KmhPresenceJoin.TryReadServerId(secret, out string id)
                            && id == new string('a', 32);
                r.Add(("presence: a crafted join secret is refused before anything looks it up", rejects,
                       rejects ? "only the id shape this client issues is accepted" : "A CRAFTED SECRET PARSED"));

                JoinGate(r, Open);
                PresenceText(r, Open);
                PresenceLifecycle(r);
            }
            finally
            {
                s.AllowDiscordJoin = wasOn; s.DiscordPresenceEnabled = wasPresence;
                s.DiscordShowPrivateServerName = wasPrivate; s.DiscordAppId = wasAppId;
            }
        }

        // The Discord secret only reaches a card in the player's OWN directory; every condition is re-asked against it here.
        private static void JoinGate(List<(string, bool, string)> r, Func<Features.Global.ServerCardDto> Open)
        {
            Features.Discord.JoinDecision Join(Features.Global.ServerCardDto c, bool fresh = true)
                => Features.Discord.KmhJoinGate.MayJoin(c, fresh);

            r.Add(("join: an open, listed, compatible server on a fresh directory is allowed",
                   Join(Open()).Allowed, "allowed"));

            r.Add(("join: a server id that is in no card refuses rather than guessing an address",
                   Join(null).Refusal == Features.Discord.JoinRefusal.NotKnown, "unknown id refused"));

            var unlisted = Open(); unlisted.FromPublicDirectory = false;
            var locked = Open(); locked.RequiresPassword = true;
            r.Add(("join: an unlisted server and a passworded one refuse with their own reason",
                   Join(unlisted).Refusal == Features.Discord.JoinRefusal.NotPublic
                   && Join(locked).Refusal == Features.Discord.JoinRefusal.PasswordRequired, "both named"));

            // A row off the disk cache, or one older than the window, is not an answer to "can I join right now".
            DateTime now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
            bool freshness = Features.Discord.KmhJoinGate.DirectoryIsFresh(now.AddSeconds(-5), false, now)
                          && !Features.Discord.KmhJoinGate.DirectoryIsFresh(now.AddHours(-2), false, now)
                          && !Features.Discord.KmhJoinGate.DirectoryIsFresh(now.AddSeconds(-5), true, now)
                          && !Features.Discord.KmhJoinGate.DirectoryIsFresh(default, false, now);
            r.Add(("join: a cached or aged directory is never treated as a current answer", freshness,
                   freshness ? "cache and age both refuse" : "A STALE DIRECTORY COUNTED AS FRESH"));
            r.Add(("join: a stale directory refuses instead of acting on what it last saw",
                   Join(Open(), fresh: false).Refusal == Features.Discord.JoinRefusal.StaleDirectory, "refreshed first"));

            var offline = Open(); offline.Status = "Offline";
            var maintenance = Open(); maintenance.Status = "Maintenance";
            var starting = Open(); starting.Status = "Restarting";
            var full = Open(); full.Players = full.MaxPlayers;
            var protocolOff = Open(); protocolOff.Protocol = SubProtocol.KmhProtocol.CurrentVersion + 1;
            var noAddress = Open(); noAddress.JoinEndpoint = "";

            bool states = Join(offline).Compat == Features.Global.CompatState.Offline
                       && Join(maintenance).Compat == Features.Global.CompatState.Maintenance
                       && Join(starting).Compat == Features.Global.CompatState.Starting
                       && Join(full).Compat == Features.Global.CompatState.Full
                       && Join(protocolOff).Compat == Features.Global.CompatState.ProtocolMismatch
                       && Join(noAddress).Compat == Features.Global.CompatState.NoAddress;
            r.Add(("join: offline, maintenance, starting, full, wrong protocol and no address each refuse", states,
                   states ? "six states refused" : "A BLOCKED SERVER WAS OFFERED A JOIN"));

            // Its own check: RWT's version is compared against this install, so an empty one cannot refuse.
            var rwtOff = Open(); rwtOff.RwtVersion = "0.0.0.0";
            bool rwt = string.IsNullOrEmpty(Features.Global.KmhRwtVersions.Mine)
                     ? Features.Global.KmhRwtVersions.Supports("26.1.1.1")
                     : Join(rwtOff).Compat == Features.Global.CompatState.RwtUnsupported;
            r.Add(("join: a server on another RimWorld Together generation refuses", rwt,
                   string.IsNullOrEmpty(Features.Global.KmhRwtVersions.Mine)
                       ? "no RWT version here: an unknown one never refuses" : "refused"));

            Features.Global.ModProfileClient.ResetForTest();
            try
            {
                var enforced = Open(); enforced.ModPolicy = Features.Global.ModPolicyNames.Enforced;
                r.Add(("join: a mod comparison that has not finished refuses rather than guessing",
                       Join(enforced).Compat == Features.Global.CompatState.Checking, "checking refuses"));

                var comparison = new Features.Global.ModComparison();
                comparison.Missing.Add("SomeMod");
                Features.Global.ModProfileClient.SeedForTest(enforced.ServerId, new Features.Global.ModProfileClient.Entry
                {
                    Profile = new Features.Global.ModProfileDto { Policy = Features.Global.ModPolicyNames.Enforced },
                    Comparison = comparison,
                });
                r.Add(("join: a mod list this player cannot satisfy refuses",
                       Join(enforced).Compat == Features.Global.CompatState.ModsAction, "mods refused"));
            }
            finally { Features.Global.ModProfileClient.ResetForTest(); }

            bool spoken = true;
            foreach (Features.Global.ServerCardDto c in new Features.Global.ServerCardDto[]
                     { null, unlisted, locked, offline, maintenance, full, protocolOff, noAddress })
            {
                Features.Discord.JoinDecision d = Join(c);
                if (d.Allowed || !string.IsNullOrEmpty(d.Explain(c))) continue;
                spoken = false;
            }
            r.Add(("join: every refusal names a reason the player can act on", spoken,
                   spoken ? "all reasons spoken" : "A JOIN FAILED SILENTLY"));

            // Discord delivers one event per click, and a double click must not open two connections.
            Features.Discord.KmhPresenceJoin.ResetForTest();
            try
            {
                string secret = Features.Discord.KmhPresenceJoin.Secret(Open());
                bool first = !Features.Discord.KmhPresenceJoin.IsDuplicate(secret, now);
                Features.Discord.KmhPresenceJoin.SeenForTest(secret, now);
                bool dup = Features.Discord.KmhPresenceJoin.IsDuplicate(secret, now.AddSeconds(2))
                        && !Features.Discord.KmhPresenceJoin.IsDuplicate(secret, now.Add(Features.Discord.KmhPresenceJoin.DuplicateWindow))
                        && !Features.Discord.KmhPresenceJoin.IsDuplicate("kmh1:" + new string('b', 32), now.AddSeconds(2));
                r.Add(("join: a repeated Discord join inside the window is ignored, a later or different one is not",
                       first && dup, first && dup ? "debounced" : "A DOUBLE CLICK WOULD CONNECT TWICE"));
            }
            finally { Features.Discord.KmhPresenceJoin.ResetForTest(); }

            // Browser and Discord invite share one gate; only the three rules that exist because someone else invited differ.
            Features.Discord.JoinDecision Browse(Features.Global.ServerCardDto c, bool fresh = true)
                => Features.Discord.KmhJoinGate.MayJoin(c, fresh, Features.Discord.JoinSource.Browser);

            var lockedB = Open(); lockedB.RequiresPassword = true;
            var unlistedB = Open(); unlistedB.FromPublicDirectory = false;
            var maintenanceB = Open(); maintenanceB.Status = "Maintenance";
            bool split = Browse(lockedB).Allowed          // RWT can prompt a player who chose the server
                      && Browse(unlistedB).Allowed        // a Recent row need not be in the public directory
                      && Browse(Open(), fresh: false).Allowed
                      && !Join(lockedB).Allowed && !Join(unlistedB).Allowed && !Join(Open(), fresh: false).Allowed
                      && Browse(maintenanceB).Compat == Features.Global.CompatState.Maintenance
                      && Join(maintenanceB).Compat == Features.Global.CompatState.Maintenance;
            r.Add(("join: the browser and a Discord invite share one gate and differ only where they must", split,
                   split ? "one policy, two sources" : "THE TWO ENTRY POINTS DISAGREE"));

            // A join delivered against a presence KMH has already replaced belongs to a session that is gone.
            bool stale = !Features.Discord.KmhPresenceJoin.IsStale(Features.Discord.KmhRichPresence.Epoch)
                       && Features.Discord.KmhPresenceJoin.IsStale(Features.Discord.KmhRichPresence.Epoch - 1)
                       && Features.Discord.KmhPresenceJoin.IsStale(Features.Discord.KmhRichPresence.Epoch + 1);
            r.Add(("join: a join delivered against a replaced presence is refused", stale,
                   stale ? "epoch checked" : "A STALE JOIN WOULD ACT ON THE CURRENT SESSION"));

            // The refresh is awaited, so everything that could revoke the invite meanwhile is re-asked afterwards.
            bool moved = Features.Discord.KmhPresenceJoin.SessionMovedOn(4, 5, onKmhServer: true)
                      && !Features.Discord.KmhPresenceJoin.SessionMovedOn(5, 5, onKmhServer: true)
                      && !Features.Discord.KmhPresenceJoin.SessionMovedOn(4, 5, onKmhServer: false);
            r.Add(("join: joining a server during the refresh drops the older invite instead of interrupting it",
                   moved, moved ? "generation bound" : "A STALE INVITE WOULD DISCONNECT A LIVE SESSION"));

            // Confirming an invite while on another server disconnects it, so the warning must appear exactly then.
            bool wasOnKmh = SubProtocol.KmhDispatcher.IsKmhServer;
            string wasServerId = SubProtocol.KmhDispatcher.ServerId;
            try
            {
                string target = new string('a', 32), other = new string('c', 32);
                SubProtocol.KmhDispatcher.IsKmhServer = false;
                bool menuQuiet = !Features.Discord.KmhPresenceJoin.LeavingAnotherServer(target);
                SubProtocol.KmhDispatcher.IsKmhServer = true;
                SubProtocol.KmhDispatcher.ServerId = target;
                bool sameQuiet = !Features.Discord.KmhPresenceJoin.LeavingAnotherServer(target);
                SubProtocol.KmhDispatcher.ServerId = other;
                bool warns = Features.Discord.KmhPresenceJoin.LeavingAnotherServer(target);
                r.Add(("join: an invite to another server warns that confirming leaves the current one",
                       menuQuiet && sameQuiet && warns,
                       menuQuiet && sameQuiet && warns ? "warned only when it is true" : "WARNING LOGIC IS WRONG"));
            }
            finally
            {
                SubProtocol.KmhDispatcher.IsKmhServer = wasOnKmh;
                SubProtocol.KmhDispatcher.ServerId = wasServerId;
            }

            Features.Discord.KmhPresenceJoin.ResetForTest();
            try
            {
                bool idle = !Features.Discord.KmhPresenceJoin.ConnectInFlight(now);
                Features.Discord.KmhPresenceJoin.ConnectingForTest(now.Add(Features.Discord.KmhPresenceJoin.ConnectGuard));
                bool guarded = Features.Discord.KmhPresenceJoin.ConnectInFlight(now.AddSeconds(1))
                            && !Features.Discord.KmhPresenceJoin.ConnectInFlight(now.Add(Features.Discord.KmhPresenceJoin.ConnectGuard));
                r.Add(("join: a second invite arriving mid-connect does not start a second connection",
                       idle && guarded, idle && guarded ? "one action at a time" : "TWO CONNECTIONS COULD START"));
            }
            finally { Features.Discord.KmhPresenceJoin.ResetForTest(); }
        }

        private static void PresenceText(List<(string, bool, string)> r, Func<Features.Global.ServerCardDto> Open)
        {
            KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
            bool wasKmh = SubProtocol.KmhDispatcher.IsKmhServer;
            string wasId = SubProtocol.KmhDispatcher.ServerId, wasName = SubProtocol.KmhDispatcher.ServerName;
            bool wasJoinable = Features.Discord.KmhRichPresence.JoinAdvertisable;
            bool wasPrivate = s.DiscordShowPrivateServerName, wasNamed = s.DiscordShowServerName, wasMp = s.DiscordShowMultiplayer;
            Features.Global.KmhDirectoryClient.ResetForTest();
            // Everyday play, so a raid in the loaded save cannot change the headline under test.
            Features.Discord.KmhDiscordRpc.Presence Shown()
            {
                var ses = Features.Discord.KmhRichPresence.Session(s, DateTime.UtcNow);
                ses.Playing = true;
                var idle = new Features.Discord.PresenceChoice { Kind = Features.Discord.PresenceKind.Colony, SinceUtc = DateTime.UtcNow };
                return Features.Discord.KmhPresenceFormat.Compose(idle, ses, s, 1, DateTime.UtcNow);
            }
            try
            {
                s.DiscordShowServerName = s.DiscordShowMultiplayer = true;
                // Joinable-server checks: the URI scheme is marked registered, as no real RPC client exists here.
                Features.Discord.KmhRichPresence.SetJoinAdvertisableForTest(true);
                // The address the fixture cards are listed at, as if the player had joined one of them.
                Features.Discord.KmhRichPresence.DialledForTest = "play.example.net:25560";

                SubProtocol.KmhDispatcher.IsKmhServer = false;
                Features.Discord.KmhDiscordRpc.Presence menu = Shown();
                r.Add(("presence: off a KMH server the status invents no server and offers no join",
                       string.IsNullOrEmpty(menu.JoinSecret) && string.IsNullOrEmpty(menu.PartyId), menu.Details));

                Features.Global.ServerCardDto card = Open();
                SubProtocol.KmhDispatcher.IsKmhServer = true;
                SubProtocol.KmhDispatcher.ServerId = card.ServerId;
                SubProtocol.KmhDispatcher.ServerName = "Private Name";
                Features.Global.KmhDirectoryClient.SeedForTest(
                    new List<Features.Global.ServerCardDto> { card }, Features.Global.GlobalLinkState.Online, false);

                Features.Discord.KmhDiscordRpc.Presence pub = Shown();
                r.Add(("presence: a listed server is named, sized and joinable",
                       pub.State == "Open" && pub.PartySize == 3 && pub.PartyMax == 32
                       && pub.JoinSecret == Features.Discord.KmhPresenceJoin.Secret(card) && pub.PartyPublic,
                       $"{pub.State} {pub.PartySize}/{pub.PartyMax}"));

                // Discord refuses a party of 0, and an empty server is still one player once you are on it.
                var empty = Open(); empty.Players = 0;
                var one = Open(); one.MaxPlayers = 1; one.Players = 1;
                var unknown = Open(); unknown.MaxPlayers = 0; unknown.Players = 0;
                Features.Global.KmhDirectoryClient.SeedForTest(
                    new List<Features.Global.ServerCardDto> { empty }, Features.Global.GlobalLinkState.Online, false);
                Features.Discord.KmhDiscordRpc.Presence pEmpty = Shown();
                Features.Global.KmhDirectoryClient.SeedForTest(
                    new List<Features.Global.ServerCardDto> { one }, Features.Global.GlobalLinkState.Online, false);
                Features.Discord.KmhDiscordRpc.Presence pOne = Shown();
                Features.Global.KmhDirectoryClient.SeedForTest(
                    new List<Features.Global.ServerCardDto> { unknown }, Features.Global.GlobalLinkState.Online, false);
                Features.Discord.KmhDiscordRpc.Presence pUnknown = Shown();
                bool counts = pEmpty.PartySize == 1 && pEmpty.PartyMax == 32
                           && pOne.PartySize == 1 && pOne.PartyMax == 1
                           && string.IsNullOrEmpty(pUnknown.PartyId) && pUnknown.PartyMax == 0;
                r.Add(("presence: 0 reported, 1/1 and an unpublished capacity each render a party Discord accepts",
                       counts, $"{pEmpty.PartySize}/{pEmpty.PartyMax}, {pOne.PartySize}/{pOne.PartyMax}, none"));

                // A server the public directory does not list is private as far as KMH can tell.
                Features.Global.KmhDirectoryClient.ResetForTest();
                s.DiscordShowPrivateServerName = false;
                Features.Discord.KmhDiscordRpc.Presence priv = Shown();
                bool withheld = (priv.Details + priv.State + priv.SmallImageText).IndexOf("Private Name", StringComparison.Ordinal) < 0
                             && string.IsNullOrEmpty(priv.JoinSecret) && string.IsNullOrEmpty(priv.PartyId);
                r.Add(("presence: a server that is not publicly listed is neither named nor advertised", withheld,
                       withheld ? priv.State : "A PRIVATE SERVER LEAKED INTO A DISCORD STATUS"));

                s.DiscordShowPrivateServerName = true;
                Features.Discord.KmhDiscordRpc.Presence opted = Shown();
                // Opting in to a name is not opting in to a party, an invite or an address.
                r.Add(("presence: opting in names a private server but still offers no invite",
                       opted.State.IndexOf("Private Name", StringComparison.Ordinal) >= 0
                       && string.IsNullOrEmpty(opted.JoinSecret)
                       && string.IsNullOrEmpty(opted.PartyId) && opted.PartyMax == 0
                       && opted.State.IndexOf("play.example.net", StringComparison.Ordinal) < 0, opted.State));
                s.DiscordShowPrivateServerName = false;

                // A hand-edited cache must not be able to pass a private server off as a listed one.
                var forged = Open(); forged.FromPublicDirectory = false;
                Features.Global.KmhDirectoryClient.SeedForTest(
                    new List<Features.Global.ServerCardDto> { forged }, Features.Global.GlobalLinkState.Online, true);
                Features.Discord.KmhDiscordRpc.Presence fake = Shown();
                r.Add(("presence: a cached row that did not come from the public directory is treated as private",
                       string.IsNullOrEmpty(fake.JoinSecret) && fake.State != forged.Name && fake.Details != forged.Name, fake.State));

                // The push guard skips an identical payload, so anything that changes has to change the payload.
                bool moves = Features.Discord.KmhRichPresence.Payload(pub) != Features.Discord.KmhRichPresence.Payload(pEmpty)
                          && Features.Discord.KmhRichPresence.Payload(pub) != Features.Discord.KmhRichPresence.Payload(menu)
                          && Features.Discord.KmhRichPresence.Payload(pub) == Features.Discord.KmhRichPresence.Payload(pub);
                r.Add(("presence: a changed server or player count changes the payload the push guard compares",
                       moves, moves ? "guard tracks the state" : "A CHANGED STATUS WOULD NEVER BE PUSHED"));

                // A Discord status is world-readable and a join secret goes to strangers: neither may carry the address.
                Features.Global.KmhDirectoryClient.SeedForTest(
                    new List<Features.Global.ServerCardDto> { card }, Features.Global.GlobalLinkState.Online, false);
                Features.Discord.KmhDiscordRpc.Presence leak = Shown();
                string all = string.Join("|", new[] { leak.Details, leak.State, leak.PartyId, leak.JoinSecret,
                                                           leak.LargeImageText, leak.SmallImageText, leak.LargeImageUrl });
                bool noAddress = all.IndexOf("play.example.net", StringComparison.OrdinalIgnoreCase) < 0
                              && all.IndexOf("25560", StringComparison.Ordinal) < 0
                              && all.IndexOf(card.JoinEndpoint, StringComparison.Ordinal) < 0
                              && leak.JoinSecret == Features.Discord.KmhPresenceJoin.Prefix + card.ServerId;
                r.Add(("presence: neither the status nor the join secret carries the server's address", noAddress,
                       noAddress ? "id only" : "AN ADDRESS REACHED A DISCORD PAYLOAD"));

                // Owner-written, delivered over the wire, and handed straight to another application's parser.
                var hostile = Open();
                // Built from char values: a raw control byte in a source file makes grep treat it as binary.
                hostile.Name = "Bad" + (char)0 + "Name" + (char)7 + (char)27 + "[31m" + (char)10 + "second line";
                Features.Global.KmhDirectoryClient.SeedForTest(
                    new List<Features.Global.ServerCardDto> { hostile }, Features.Global.GlobalLinkState.Online, false);
                Features.Discord.KmhDiscordRpc.Presence nasty = Shown();
                bool clean = true;
                foreach (char c in nasty.Details + nasty.State) if (char.IsControl(c)) clean = false;
                r.Add(("presence: control characters in a server name never reach Discord", clean,
                       clean ? nasty.Details : "A CONTROL CHARACTER REACHED A DISCORD PAYLOAD"));

                // char.IsControl misses a direction override (a format char), so a name could render as something else.
                string bidi = Features.Discord.KmhDiscordRpc.Trim("KMH‮EVIL‬ Server", 128);
                string intl = Features.Discord.KmhDiscordRpc.Trim("Hüttenwerk مرحبا 🌍‍🔥 Ünïcödé", 128);
                bool safeText = bidi.IndexOf('‮') < 0 && bidi.IndexOf('‬') < 0
                             && bidi.IndexOf("EVIL", StringComparison.Ordinal) >= 0
                             && intl.IndexOf('‍') >= 0
                             && intl.IndexOf("مرحبا", StringComparison.Ordinal) >= 0
                             && intl.IndexOf("Ünïcödé", StringComparison.Ordinal) >= 0;
                r.Add(("presence: direction overrides are stripped while real scripts and emoji survive", safeText,
                       safeText ? bidi : "SANITISATION IS WRONG IN ONE DIRECTION"));

                // Discord counts UTF-8 bytes: 100 accented letters are under 128 characters but over 128 bytes.
                string wide = new string('é', 200) + new string('\ud83d', 1);
                string shortWide = new string('é', 100);
                string trimmed = Features.Discord.KmhDiscordRpc.Trim(wide, 128);
                string trimmedShort = Features.Discord.KmhDiscordRpc.Trim(shortWide, 128);
                bool bytes = System.Text.Encoding.UTF8.GetByteCount(trimmed) <= 128
                          && System.Text.Encoding.UTF8.GetByteCount(trimmedShort) <= 128
                          && shortWide.Length <= 128
                          && !char.IsHighSurrogate(trimmed[trimmed.Length - 1])
                          && Features.Discord.KmhDiscordRpc.Trim(null, 128) == ""
                          && Features.Discord.KmhDiscordRpc.Trim("   ", 128) == ""
                          && Features.Discord.KmhDiscordRpc.Trim("ok", 128) == "ok";
                r.Add(("presence: a name is capped in the bytes Discord counts, never mid-character", bytes,
                       $"{System.Text.Encoding.UTF8.GetByteCount(trimmed)} / {System.Text.Encoding.UTF8.GetByteCount(trimmedShort)} byte(s)"));
            }
            finally
            {
                Features.Discord.KmhRichPresence.DialledForTest = null;
                Features.Discord.KmhRichPresence.SetJoinAdvertisableForTest(wasJoinable);
                SubProtocol.KmhDispatcher.IsKmhServer = wasKmh;
                SubProtocol.KmhDispatcher.ServerId = wasId;
                SubProtocol.KmhDispatcher.ServerName = wasName;
                s.DiscordShowPrivateServerName = wasPrivate; s.DiscordShowServerName = wasNamed; s.DiscordShowMultiplayer = wasMp;
                Features.Global.KmhDirectoryClient.ResetForTest();
            }
        }

        private static void PresenceLifecycle(List<(string, bool, string)> r)
        {
            KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
            Features.Discord.KmhRichPresence.ResetForTest();
            Features.Discord.KmhRichPresence.SuppressClientForTest = true;
            try
            {
                s.DiscordPresenceEnabled = true;
                s.DiscordAppId = "";
                Features.Discord.KmhRichPresence.Tick();
                r.Add(("presence: with no application id KMH sets no status and leaves RWT's alone",
                       !Features.Discord.KmhRichPresence.Active && !Features.Discord.KmhRwtPresence.StoppedByKmh,
                       Features.Discord.KmhRichPresence.LastState));

                s.DiscordAppId = "000000000000000000";
                int before = Features.Discord.KmhRichPresence.RestartsForTest;
                Features.Discord.KmhRichPresence.Tick();
                Features.Discord.KmhRichPresence.Tick();
                Features.Discord.KmhRichPresence.Tick();
                int starts = Features.Discord.KmhRichPresence.RestartsForTest - before;
                r.Add(("presence: a start that cannot connect is retried on a timer, not once a frame",
                       starts == 1 && Features.Discord.KmhRichPresence.RetryEvery > TimeSpan.Zero,
                       $"{starts} attempt(s) across three ticks"));

                s.DiscordPresenceEnabled = false;
                Features.Discord.KmhRichPresence.Tick();
                r.Add(("presence: switching it off stops KMH's status and puts RWT's own back",
                       !Features.Discord.KmhRichPresence.Active && !Features.Discord.KmhRwtPresence.StoppedByKmh,
                       Features.Discord.KmhRichPresence.LastState));

                s.DiscordPresenceEnabled = true;
                Features.Discord.KmhRichPresence.Tick();
                r.Add(("presence: switching it back on tries again rather than staying off for the session",
                       Features.Discord.KmhRichPresence.RestartsForTest > starts + 1,
                       $"{Features.Discord.KmhRichPresence.RestartsForTest} starts"));

                // Frames keep running while the game quits; a client started then would set a status on the way out.
                Features.Discord.KmhRichPresence.ResetForTest();
                Features.Discord.KmhRichPresence.SetShuttingDownForTest(true);
                try
                {
                    int quitStarts = Features.Discord.KmhRichPresence.RestartsForTest;
                    Features.Discord.KmhRichPresence.Tick();
                    Features.Discord.KmhRichPresence.Tick();
                    r.Add(("presence: once the game is quitting nothing starts the client again",
                           Features.Discord.KmhRichPresence.RestartsForTest == quitStarts,
                           $"{Features.Discord.KmhRichPresence.RestartsForTest - quitStarts} start(s) while quitting"));
                }
                finally { Features.Discord.KmhRichPresence.SetShuttingDownForTest(false); }

                // Typed by hand into a settings box, so the shapes that cannot work are named as such.
                bool ids = Features.Discord.KmhRichPresence.LooksLikeApplicationId("1505021874868981854")
                        && !Features.Discord.KmhRichPresence.LooksLikeApplicationId("not-an-id")
                        && !Features.Discord.KmhRichPresence.LooksLikeApplicationId("123")
                        && !Features.Discord.KmhRichPresence.LooksLikeApplicationId("15050218748689818540000")
                        && !Features.Discord.KmhRichPresence.LooksLikeApplicationId("15050218748689818 4")
                        && !Features.Discord.KmhRichPresence.LooksLikeApplicationId("")
                        && !Features.Discord.KmhRichPresence.LooksLikeApplicationId(null);
                r.Add(("presence: an application id that cannot work is named as such, not reported as no Discord",
                       ids, ids ? "shape checked" : "AN UNUSABLE ID WOULD LOOK LIKE A MISSING DISCORD"));

                // A missing library cannot turn up later, a Discord not yet running can: only the latter is retried.
                Features.Discord.KmhRichPresence.ResetForTest();
                Features.Discord.KmhRichPresence.SuppressClientForTest = false;
                Features.Discord.KmhDiscordRpc.ForceUnavailableForTest = true;
                s.DiscordAppId = "000000000000000000";
                Features.Discord.KmhRichPresence.Tick();
                Features.Discord.KmhRichPresence.Tick();
                bool never = !Features.Discord.KmhRichPresence.Active
                          && Features.Discord.KmhRichPresence.RestartsForTest == 1
                          && Features.Discord.KmhRichPresence.NextRetryForTest == DateTime.MaxValue;

                Features.Discord.KmhDiscordRpc.ForceUnavailableForTest = false;
                Features.Discord.KmhRichPresence.ResetForTest();
                Features.Discord.KmhRichPresence.SuppressClientForTest = true;
                s.DiscordAppId = "000000000000000001";
                Features.Discord.KmhRichPresence.Tick();
                bool later = Features.Discord.KmhRichPresence.NextRetryForTest < DateTime.MaxValue
                          && Features.Discord.KmhRichPresence.NextRetryForTest > DateTime.UtcNow;
                r.Add(("presence: a missing DiscordRPC is never retried, a Discord that is not running is",
                       never && later, never && later ? "one diagnostic, no frame spam" : "RETRY POLICY IS WRONG"));

                // By-name lookup sees only loaded assemblies; unloaded, this would pass against a method name RWT lacks.
                foreach (string asm in new[] { "RTClient", "GameClient" })
                    try { System.Reflection.Assembly.Load(asm); } catch { }

                Type manager = Patches.Patch_DiscordManager_KmhPresence.ManagerType();
                System.Reflection.MethodBase target = Patches.Patch_DiscordManager_KmhPresence.StartPresenceMethod();
                // This generation drives its whole presence from one static StartPresence; there is no SetPresence.
                bool named = target != null && target.Name == "StartPresence" && target.IsStatic;
#if RWT_OLD
                // The old RWT generation ships no DiscordManager at all, so finding nothing is correct here.
                named = manager == null && target == null;
#endif
                r.Add(("presence: the RWT method KMH suppresses is one this RWT actually has", named,
                       manager == null ? "no DiscordManager in this generation"
                                       : manager.FullName + "." + (target?.Name ?? "NOT FOUND")));
            }
            finally
            {
                Features.Discord.KmhRichPresence.SuppressClientForTest = false;
                Features.Discord.KmhDiscordRpc.ForceUnavailableForTest = false;
                Features.Discord.KmhRichPresence.ResetForTest();
            }
        }

        // A mismatch must end the session before anything is hydrated, and say so instead of going quiet.
        private static void ProtocolMismatch(List<(string, bool, string)> r)
        {
            bool wasKmh = SubProtocol.KmhDispatcher.IsKmhServer;
            SubProtocol.KmhTransportStatus wasStatus = SubProtocol.KmhTransport.Status;
            try
            {
                r.Add(("protocol: this client speaks v3", SubProtocol.KmhProtocol.CurrentVersion == 3,
                       $"v{SubProtocol.KmhProtocol.CurrentVersion}"));

                // Starts in VersionMismatch: a first refusal toasts via MessageTypeDefOf, unresolvable outside the game.
                SubProtocol.KmhDispatcher.IsKmhServer = true;
                SubProtocol.KmhTransport.Status = SubProtocol.KmhTransportStatus.VersionMismatch;
                bool activated = SubProtocol.KmhHandshakeHandler.ActivateSession(
                    2, "1.3.0", SubProtocol.KmhTransportStatus.ChatFallback);

                bool refused = !activated
                            && !SubProtocol.KmhDispatcher.IsKmhServer
                            && SubProtocol.KmhTransport.Status == SubProtocol.KmhTransportStatus.VersionMismatch;
                r.Add(("protocol: a v2 server is refused, and no v3 state is hydrated against it", refused,
                       refused ? "session torn down, status stays VersionMismatch"
                               : $"activated={activated} isKmh={SubProtocol.KmhDispatcher.IsKmhServer} " +
                                 $"status={SubProtocol.KmhTransport.Status}"));

                // Repeating it is idempotent: a re-handshake must not flip the session back on.
                bool again = SubProtocol.KmhHandshakeHandler.ActivateSession(
                    2, "1.3.0", SubProtocol.KmhTransportStatus.ChatFallback);
                r.Add(("protocol: a repeated mismatch stays refused rather than oscillating",
                       !again && !SubProtocol.KmhDispatcher.IsKmhServer
                       && SubProtocol.KmhTransport.Status == SubProtocol.KmhTransportStatus.VersionMismatch,
                       "still refused, still off"));

                // Non-vacuous: a matching version activates; started as a live session, as a first handshake needs a game.
                SubProtocol.KmhDispatcher.IsKmhServer = true;
                SubProtocol.KmhTransport.Status = SubProtocol.KmhTransportStatus.ChatFallback;
                bool matched = SubProtocol.KmhHandshakeHandler.ActivateSession(
                    SubProtocol.KmhProtocol.CurrentVersion, "1.3.1", SubProtocol.KmhTransportStatus.ChatFallback);
                r.Add(("protocol: a matching server activates, so the refusal above means something", matched,
                       matched ? "v3 to v3 activates" : "ACTIVATION IS BROKEN FOR EVERYONE"));
            }
            finally
            {
                SubProtocol.KmhDispatcher.IsKmhServer = wasKmh;
                SubProtocol.KmhTransport.Status = wasStatus;
            }
        }

        private static bool Near(float a, float b) => System.Math.Abs(a - b) < 0.0005f;

        // A preference that saves but never reloads reads as unsaved: the volume shows its default on every launch.
        private static void MediaPreferenceRehydration(List<(string, bool, string)> r)
        {
            try
            {
                var s = new Features.Chat.ChatVideoPlayer.MediaPrefs { Volume = 1f, Muted = false, Loop = false };
                Features.Chat.ChatVideoPlayer.PrefsForTest = s;
                Features.Chat.ChatVideoPlayer.SyncFromSettings();

                bool loaded = Near(Features.Chat.ChatVideoPlayer.Volume, 1f)
                           && !Features.Chat.ChatVideoPlayer.Looping;
                r.Add(("media prefs: a saved volume is what the controls show, not the shipped default", loaded,
                       loaded ? "reads back 100%" : $"READ {Features.Chat.ChatVideoPlayer.Volume:P0}, loop={Features.Chat.ChatVideoPlayer.Looping}"));

                // Settings update before the debounce fires, or Mod Options writes a stale value over the player's.
                Features.Chat.ChatVideoPlayer.SetVolume(0.25f);
                bool immediate = Near(s.Volume, 0.25f);
                r.Add(("media prefs: moving the slider updates settings at once, only the disk write waits", immediate,
                       immediate ? "settings hold 25% before any save" : $"SETTINGS STILL AT {s.Volume:P0}"));

                Features.Chat.ChatVideoPlayer.SyncFromSettings();
                bool survives = Near(Features.Chat.ChatVideoPlayer.Volume, 0.25f);
                r.Add(("media prefs: re-reading settings mid-drag keeps the player's choice", survives,
                       survives ? "25% survives a resync" : $"REVERTED TO {Features.Chat.ChatVideoPlayer.Volume:P0}"));

                Features.Chat.ChatVideoPlayer.SetVolume(0f);
                bool mutePersists = Near(s.Volume, 0f);
                Features.Chat.ChatVideoPlayer.SetVolume(5f);
                bool clamped = Near(s.Volume, 1f)
                            && Features.Chat.ChatVideoPlayer.ClampVolume(-3f) == 0f;
                r.Add(("media prefs: silence and maximum both persist, and out-of-range values clamp", mutePersists && clamped,
                       mutePersists && clamped ? "0 and 1 both stored" : $"mute={mutePersists} clamp={clamped}"));
            }
            catch (Exception ex) { r.Add(("media prefs: the preference paths run outside the game", false, ex.Message)); }
            finally
            {
                Features.Chat.ChatVideoPlayer.PrefsForTest = null;
                Features.Chat.ChatVideoPlayer.SyncFromSettings();
            }
        }

        // The client never asserts who the player is, so every one of these states is reached by what Global said.
        private static void PlayerAccountLink(List<(string, bool, string)> r)
        {
            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "kmh-acct-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string previousBase = Features.Global.GlobalHttp.BaseUrl;
            try
            {
                Features.Account.KmhAccountStore.RootOverrideForTest = scratch;
                Features.Account.KmhAccountStore.ResetForTest();
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Global.GlobalHttp.BaseUrl = "https://directory-a.example";

                DeepLinkShape(r);

                Features.Account.KmhAccountClient.SeedWaitingForTest("chal-1", Soon());
                Features.Account.KmhAccountClient.Apply(JObject.Parse(
                    "{\"state\":\"Consumed\",\"player_token\":\"tok-abc\",\"player_global_id\":\"pgid-0123456789\",\"display_name\":\"Knappe\"}"));
                bool linked = Features.Account.KmhAccountClient.State == Features.Account.AccountState.Linked
                           && Features.Account.KmhAccountStore.TokenFor("https://directory-a.example") == "tok-abc";
                r.Add(("account: the poll that consumes the challenge is what links the player", linked,
                       linked ? "token stored, state Linked" : $"STATE {Features.Account.KmhAccountClient.State}"));

                Features.Account.KmhAccountStore.ResetForTest();
                bool survives = Features.Account.KmhAccountStore.TokenFor("https://directory-a.example") == "tok-abc"
                             && Features.Account.KmhAccountStore.PlayerGlobalId == "pgid-0123456789";
                r.Add(("account: the link is read back from disk after a restart, not held in memory", survives,
                       survives ? "reloaded from account.json" : "LOST ON RELOAD"));

                // A token minted by one directory says nothing about another, and must never be presented to it.
                bool scoped = Features.Account.KmhAccountStore.TokenFor("https://directory-b.example").Length == 0
                           && !Features.Account.KmhAccountStore.HasToken("https://directory-b.example");
                r.Add(("account: a credential is only ever offered back to the directory that issued it", scoped,
                       scoped ? "directory-b sees nothing" : "LEAKED ACROSS DIRECTORIES"));

                // Consumed with no token means another copy of the game already took it.
                Features.Account.KmhAccountStore.Clear();
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhAccountClient.SeedWaitingForTest("chal-2", Soon());
                Features.Account.KmhAccountClient.Apply(JObject.Parse(
                    "{\"state\":\"Consumed\",\"player_global_id\":\"pgid-0123456789\"}"));
                bool claimed = Features.Account.KmhAccountClient.State == Features.Account.AccountState.AlreadyLinked
                            && !Features.Account.KmhAccountStore.HasToken("https://directory-a.example");
                r.Add(("account: a code already claimed elsewhere does not link this client", claimed,
                       claimed ? "AlreadyLinked, nothing stored" : $"STATE {Features.Account.KmhAccountClient.State}"));

                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhAccountClient.SeedWaitingForTest("chal-3", Soon());
                Features.Account.KmhAccountClient.Apply(JObject.Parse("{\"state\":\"Pending\"}"));
                bool stillWaiting = Features.Account.KmhAccountClient.State == Features.Account.AccountState.Waiting;
                Features.Account.KmhAccountClient.Apply(JObject.Parse("{\"state\":\"Expired\"}"));
                bool expired = Features.Account.KmhAccountClient.State == Features.Account.AccountState.Expired
                            && !Features.Account.KmhAccountStore.HasToken("https://directory-a.example");
                r.Add(("account: pending keeps waiting and expired ends it, neither one links anything",
                       stillWaiting && expired,
                       stillWaiting && expired ? "Pending -> Waiting, Expired -> Expired" : $"pending={stillWaiting} expired={expired}"));

                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhAccountClient.SeedWaitingForTest("chal-4", Soon());
                Features.Account.KmhAccountClient.Apply(JObject.Parse("{\"state\":\"Whatever\",\"player_token\":\"tok-x\"}"));
                bool refused = Features.Account.KmhAccountClient.State == Features.Account.AccountState.Failed
                            && !Features.Account.KmhAccountStore.HasToken("https://directory-a.example");
                r.Add(("account: an unrecognised state fails instead of storing whatever came with it", refused,
                       refused ? "Failed, nothing stored" : $"STATE {Features.Account.KmhAccountClient.State}"));

                // The browser half can be abandoned; the code must stop being live without the dialog doing anything.
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhAccountClient.SeedWaitingForTest("chal-5",
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1);
                Features.Account.KmhAccountClient.Tick();
                bool timedOut = Features.Account.KmhAccountClient.State == Features.Account.AccountState.Expired;
                r.Add(("account: a code past its expiry gives up locally rather than polling for ever", timedOut,
                       timedOut ? "Tick expired it with no call" : $"STATE {Features.Account.KmhAccountClient.State}"));

                // Every tick short of the interval must be free, or the dialog polls Global once a frame.
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhAccountClient.SeedWaitingForTest("chal-6", Soon());
                for (int i = 0; i < Features.Account.KmhAccountClient.PollIntervalTicks - 1; i++)
                    Features.Account.KmhAccountClient.Tick();
                bool quiet = Features.Account.KmhAccountClient.State == Features.Account.AccountState.Waiting
                          && !Features.Account.KmhAccountClient.Busy;
                r.Add(("account: drawing the dialog does not poll Global on every frame", quiet,
                       quiet ? $"{Features.Account.KmhAccountClient.PollIntervalTicks - 1} ticks, no call" : "POLLED EARLY"));

                Features.Account.KmhAccountClient.Cancel();
                Features.Account.KmhAccountClient.Tick();
                bool stopped = !Features.Account.KmhAccountClient.Busy;
                Features.Account.KmhAccountClient.CancelToIdle();
                bool idle = Features.Account.KmhAccountClient.State == Features.Account.AccountState.NotLinked
                         && Features.Account.KmhAccountClient.SecondsLeft == 0;
                r.Add(("account: cancelling leaves no poll running and returns to a state the player can act on",
                       stopped && idle, stopped && idle ? "cancelled to NotLinked" : $"busy={!stopped} state={Features.Account.KmhAccountClient.State}"));
            }
            catch (Exception ex) { r.Add(("account: the player link paths run outside the game", false, ex.Message)); }
            finally
            {
                Features.Global.GlobalHttp.BaseUrl = previousBase;
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhAccountStore.RootOverrideForTest = null;
                Features.Account.KmhAccountStore.ResetForTest();
                try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, true); } catch (Exception) { }
            }
        }

        private static long Soon() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 900;

        // The blob's Class is instantiated as-is, so a saved item must be the def it was sold as, of that def's class.
        private static void SavedItemShape(List<(string, bool, string)> r)
        {
            Type ClassOf(string d) => d == "Gun_Revolver" ? typeof(Verse.ThingWithComps) : d == "WoodLog" ? typeof(Verse.Thing) : null;
            Type Resolve(string n) => n == "ThingWithComps" ? typeof(Verse.ThingWithComps) : n == "Pawn" ? typeof(Verse.Pawn)
                                    : n == "Hediff_Injury" ? typeof(Verse.Hediff_Injury) : n == "System.IO.FileSystemWatcher" ? typeof(System.IO.FileSystemWatcher)
                                    : n == "GameComponent" ? typeof(Verse.GameComponent) : null;
            bool Ok(string xml, string def) => Items.KmhThingScribe.ShapeMatches(xml, def, ClassOf, Resolve, out _);
            string honest = "<kmhthing><t Class=\"ThingWithComps\"><def>Gun_Revolver</def></t></kmhthing>";
            r.Add(("saved item: the item it was sold as, of its own class, loads", Ok(honest, "Gun_Revolver"), ""));
            r.Add(("saved item: a different def is refused", !Ok("<kmhthing><t><def>WoodLog</def></t></kmhthing>", "Gun_Revolver"), ""));
            r.Add(("saved item: a class the def does not use is refused",
                   !Ok("<kmhthing><t Class=\"Pawn\"><def>Gun_Revolver</def></t></kmhthing>", "Gun_Revolver"), ""));
            r.Add(("saved item: a def this game does not know is refused",
                   !Ok("<kmhthing><t><def>Nope_Missing</def></t></kmhthing>", "Nope_Missing"), ""));
            r.Add(("saved item: a document type declaration is refused",
                   !Ok("<!DOCTYPE x [<!ENTITY a \"b\">]><kmhthing><t Class=\"ThingWithComps\"><def>Gun_Revolver</def></t></kmhthing>", "Gun_Revolver"), ""));
            r.Add(("saved item: a material this game has loads",
                   Ok("<kmhthing><t Class=\"ThingWithComps\"><def>Gun_Revolver</def><stuff>WoodLog</stuff></t></kmhthing>", "Gun_Revolver"), ""));
            r.Add(("saved item: a material this game does not have is refused, never swapped for another",
                   !Ok("<kmhthing><t Class=\"ThingWithComps\"><def>Gun_Revolver</def><stuff>Nope_Bioferrite</stuff></t></kmhthing>", "Gun_Revolver"), ""));
            r.Add(("saved item: a crate holding a building this game does not have is refused",
                   !Ok("<kmhthing><t Class=\"ThingWithComps\"><def>Gun_Revolver</def><innerContainer><innerList><li><def>Nope_Building</def></li></innerList></innerContainer></t></kmhthing>", "Gun_Revolver"), ""));
            // Scribe constructs a nested Class before checking it can be saved, so only the game's own saveable types pass.
            string Nested(string cls) => "<kmhthing><t Class=\"ThingWithComps\"><def>Gun_Revolver</def><x><li Class=\"" + cls + "\" /></x></t></kmhthing>";
            r.Add(("saved item: a nested game type that can be saved loads", Ok(Nested("Hediff_Injury"), "Gun_Revolver"), ""));
            r.Add(("saved item: a nested type outside the game is refused before anything is built",
                   !Ok(Nested("System.IO.FileSystemWatcher"), "Gun_Revolver"), ""));
            r.Add(("saved item: a nested abstract type is refused", !Ok(Nested("GameComponent"), "Gun_Revolver"), ""));
            // Scribe matches <t> by qualified name, so a namespaced <t> is what loads while a plain decoy beside it is checked.
            r.Add(("saved item: a namespaced item beside a plain decoy is refused",
                   !Ok("<kmhthing><t xmlns=\"urn:x\" Class=\"Pawn\"><def>Gun_Revolver</def></t>" + honest.Substring("<kmhthing>".Length), "Gun_Revolver"), ""));
            r.Add(("saved item: any namespace declaration is refused, even one nothing uses",
                   !Ok("<kmhthing xmlns:x=\"urn:x\"><t Class=\"ThingWithComps\"><def>Gun_Revolver</def></t></kmhthing>", "Gun_Revolver"), ""));
            r.Add(("saved item: a second item beside the first is refused",
                   !Ok("<kmhthing><t Class=\"ThingWithComps\"><def>Gun_Revolver</def></t><t Class=\"ThingWithComps\"><def>Gun_Revolver</def></t></kmhthing>", "Gun_Revolver"), ""));
        }

        // Notice text, item names and worker pawn names come from other players and must draw as text.
        private static void OtherPlayersTextIsInert(List<(string, bool, string)> r)
        {
            const string marked = "<size=60><color=red>Server</color></size>";
            var notice = new Features.OfflineNotices.Dto.NotificationDto { Title = marked, Body = marked + new string('x', 3000) };
            string title = Features.OfflineNotices.NotificationHandler.Title(notice), body = Features.OfflineNotices.NotificationHandler.Body(notice);
            r.Add(("inert: a notice's title and body draw as text, and a long body is capped",
                   title.IndexOf('<') < 0 && body.IndexOf('<') < 0 && body.Length <= 1001, $"title={title} bodyLength={body.Length}"));

            int cached = UI.ItemLabels.CachedCountForTest;
            string label = UI.ItemLabels.ResolveLabel(marked) + UI.ItemLabels.ResolveLabel(marked + "|" + marked + "|3");
            r.Add(("inert: an item name no def has draws as text and is not kept",
                   label.IndexOf('<') < 0 && UI.ItemLabels.CachedCountForTest == cached, $"label={label} cached {cached}->{UI.ItemLabels.CachedCountForTest}"));

            string pawn = Features.Sites.Dialog_KMHSites.PawnName(new Features.Sites.Dto.WorkerProgressDto { PawnName = marked });
            r.Add(("inert: a site worker's pawn name draws as text", pawn.IndexOf('<') < 0, pawn));
        }

        // One stack that cannot load must not leave the others placed: the server replays an unacked delivery on every join.
        private static void DeliveryAllOrNothing(List<(string, bool, string)> r)
        {
            // Extensions get the answer Buy, Bid and Withdraw use, or an extension's shop repeats the loss guarded here.
            KMH.Sdk.Client.Apis.IItemLabelResolver sdk = new Extensibility.ItemLabelResolverAdapter();
            string none = sdk.MissingContent("Steel"), noStuff = sdk.MissingContent("Steel", "Nope_SelftestStuff"),
                   noDef = sdk.MissingContent("Nope_SelftestMissing");
            const string sdkName = "extension API: content this game lacks is reported as the game's own buttons report it";
            if (Verse.DefDatabase<Verse.ThingDef>.GetNamedSilentFail("Steel") == null) r.Add(KmhSelfTestResult.Skipped(sdkName, "needs the game's defs"));
            else r.Add((sdkName, none == null && noStuff != null && noDef != null, $"{none ?? "null"} | {noStuff ?? "null"} | {noDef ?? "null"}"));

            const string name = "delivery: a stack this game cannot load blocks the whole delivery and places nothing";
            Verse.Map map = UI.ColonyGoods.DeliveryMap();
            if (map == null || UI.CaravanReader.GetSelectedCaravan() != null)
            { r.Add(KmhSelfTestResult.Skipped(name, "needs a colony map and no selected caravan")); return; }
            // A drop pod is a thing on the map the moment it is called, before it lands.
            int Steel() => map.listerThings.AllThings.Count;
            int before = Steel();
            var payloads = new List<Items.KmhThingPayload>
            {
                new Items.KmhThingPayload { DefName = "Steel", StackCount = 5 },
                new Items.KmhThingPayload { DefName = "Nope_SelftestMissing", StackCount = 1 },
            };
            UI.ColonyGoods.PayloadDelivery got = UI.ColonyGoods.DeliverPayloads(payloads, out string blocked, out _);
            r.Add((name, got == UI.ColonyGoods.PayloadDelivery.Blocked && Steel() == before,
                   $"result {got}, things on the map {before} -> {Steel()}"));
            r.Add(("delivery: a blocked item's reason names it", (blocked ?? "").Contains("Nope_SelftestMissing"), blocked ?? "(none)"));
        }

        // Row text is reused only within one snapshot, language and second: a purchase arrives as a new snapshot list.
        private static void MarketRowText(List<(string, bool, string)> r)
        {
            var cache = new Features.Marketplace.Dialog_KMHMarketplace.RowTextCache();
            long t0 = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc).Ticks;
            Features.Marketplace.Dto.MarketplaceListing Listing(int left) => new Features.Marketplace.Dto.MarketplaceListing
            {
                Id = 9001, ItemDefName = "Steel", RemainingQty = left, OriginalQty = 50, UnitPriceMilli = 2000,
                ListedUtcTicks = t0 - TimeSpan.TicksPerMinute * 10, ExpiresUtcTicks = t0 + TimeSpan.TicksPerDay,
            };
            var before = Listing(40);
            var after  = Listing(39);
            var firstSnapshot = new List<Features.Marketplace.Dto.MarketplaceListing> { before };
            var nextSnapshot  = new List<Features.Marketplace.Dto.MarketplaceListing> { after };

            var a      = cache.TextFor(before, firstSnapshot, t0);
            var same   = cache.TextFor(before, firstSnapshot, t0 + TimeSpan.TicksPerMillisecond * 300);
            var bought = cache.TextFor(after, nextSnapshot, t0 + TimeSpan.TicksPerMillisecond * 600);
            bool reused = ReferenceEquals(a, same) && ReferenceEquals(a.Item, same.Item);
            bool fresh  = !ReferenceEquals(a, bought) && bought.Qty.StartsWith("39") && a.Qty.StartsWith("40");
            string listedNow = bought.Listed;
            bool moves  = cache.TextFor(after, nextSnapshot, t0 + TimeSpan.TicksPerSecond * 90).Listed != listedNow;

            var english = cache.TextFor(after, nextSnapshot, t0 + TimeSpan.TicksPerSecond * 90);
            bool prior = KmhText.EnglishOnlyForTest;
            Features.Marketplace.Dialog_KMHMarketplace.RowText other;
            try { KmhText.EnglishOnlyForTest = !prior; other = cache.TextFor(after, nextSnapshot, t0 + TimeSpan.TicksPerSecond * 90); }
            finally { KmhText.EnglishOnlyForTest = prior; }
            bool language = !ReferenceEquals(english, other);

            r.Add(("marketplace: a row's text is reused within a snapshot and second, rebuilt after a purchase, a language change or a second",
                   reused && fresh && moves && language,
                   $"reused {reused}, after purchase '{bought.Qty}' (was '{a.Qty}'), time text moves {moves}, language rebuild {language}"));
        }

        // A game server names chat media urls, and each sink they reach would act on a file:, UNC or custom-scheme one.
        private static void ChatMediaSchemes(List<(string, bool, string)> r)
        {
            try { ChatMediaSinks(r); }
            catch (Exception ex) { r.Add(("chat media: the scheme checks run outside the game", false, ex.ToString())); }
        }

        private static void ChatMediaSinks(List<(string, bool, string)> r)
        {
            const string Url = "https://cdn.discordapp.com/attachments/1/2/pic.webp";
            const string local = "file:///C:/kmh-selftest-missing.png";
            string[] hostile =
            {
                local, "file://attacker.example/share/x.png", "\\\\attacker.example\\share\\x.png", "javascript:alert(1)",
                "steam://run/1", "ms-settings:privacy", "http://cdn.example/x.png", "https:///x.png",
                " https://cdn.example/x.png", "https://user:pw@cdn.example/x.png", "C:\\Windows\\win.ini", "/etc/passwd",
            };
            string accepted = "";
            foreach (string h in hostile) if (Features.Chat.ChatImageCache.IsWebMedia(h)) accepted += " " + h;
            bool webOk = Features.Chat.ChatImageCache.IsWebMedia(Url)
                      && Features.Chat.ChatImageCache.IsWebMedia("https://media.discordapp.net/a/b.png?ex=1&format=png");
            r.Add(("chat media: only an absolute https url with a host counts as web media", accepted.Length == 0 && webOk,
                   accepted.Length > 0 ? "ACCEPTED:" + accepted : webOk ? "" : "A REAL MEDIA URL WAS REFUSED"));

            // On the way in, so no row ever holds one.
            Features.Chat.ChatCache.Clear();
            Features.Chat.ChatCache.Append(new Features.Chat.Dto.ChatMessage
                { Id = 1, Channel = "selftest-media", Body = "a", ImageUrl = local, IsVideo = true }, notify: false);
            Features.Chat.ChatCache.Append(new Features.Chat.Dto.ChatMessage
                { Id = 2, Channel = "selftest-media", Body = "b", ImageUrl = Url }, notify: false);
            List<Features.Chat.Dto.ChatMessage> held = Features.Chat.ChatCache.Recent("selftest-media");
            Features.Chat.ChatCache.Clear();
            bool dropped = held.Count == 2 && held[0].ImageUrl.Length == 0 && !held[0].IsVideo && held[1].ImageUrl == Url;
            r.Add(("chat media: a non-https media url from the server is dropped as the message arrives", dropped,
                   held.Count == 2 ? $"kept '{held[0].ImageUrl}'" : held.Count + " message(s)"));

            // Every image fetch starts in Begin.
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            string imageWhy;
            try
            {
                typeof(Features.Chat.ChatImageCache).GetMethod("Begin", any).Invoke(null, new object[] { local });
                imageWhy = Features.Chat.ChatImageCache.FailureFor(local) ?? "(no failure - the fetch started)";
            }
            catch (Exception ex) { imageWhy = (ex.InnerException ?? ex).GetType().Name; }
            bool imageRefused = imageWhy == Features.Chat.ChatImageCache.NotWebMedia && !Features.Chat.ChatImageCache.CanRetry(local)
                             && !Features.Chat.ChatImageCache.IsLoading(local);
            (typeof(Features.Chat.ChatImageCache).GetField("_byUrl", any)?.GetValue(null) as System.Collections.IDictionary)?.Remove(local);
            r.Add(("chat media: an image fetch refuses a file: url before UnityWebRequest sees it", imageRefused, imageWhy));

            // The wire path never plays a local file; only this cache's own downloads may be played from disk.
            string cached = Features.Chat.ChatVideoCache.PlayableUrl(Features.Chat.ChatVideoCache.PathFor("selftest", 360));
            string outside = Features.Chat.ChatVideoCache.PlayableUrl(
                System.IO.Path.Combine(Features.Chat.ChatVideoCache.Folder, "..", "kmh-selftest.mp4"));
            string Played(Action play, string key)
            {
                try { play(); return Features.Chat.ChatVideoPlayer.FailureFor(key) ?? "(no failure - playback started)"; }
                catch (Exception ex) { return ex.GetType().Name; }
            }
            string refusal = Features.Chat.ChatVideoPlayer.NotPlayable;
            string wire = Played(() => Features.Chat.ChatVideoPlayer.Play(local), local);
            string wireCached = Played(() => Features.Chat.ChatVideoPlayer.Play(cached), cached);
            string keyed = Played(() => Features.Chat.ChatVideoPlayer.Play("selftest-watch", "file:///C:/Windows/win.ini", 0), "selftest-watch");
            bool cacheRule = Features.Chat.ChatVideoCache.IsCachedPlayable(cached)
                          && !Features.Chat.ChatVideoCache.IsCachedPlayable(outside)
                          && !Features.Chat.ChatVideoCache.IsCachedPlayable("file:///C:/Windows/win.ini");
            bool videoRefused = wire == refusal && wireCached == refusal && keyed == refusal && cacheRule;
            r.Add(("chat media: the video player plays https or its own cache, never another file: url", videoRefused,
                   $"wire={wire}, wire-cached={wireCached}, keyed={keyed}, cacheRule={cacheRule}"));

            var opened = new List<string>();
            Features.Chat.ChatPanel.OpenForTest = u => opened.Add(u);
            try
            {
                foreach (string h in new[] { local, "javascript:alert(1)", "steam://run/1", "http://cdn.example/x.mp4" })
                    Features.Chat.ChatPanel.OpenExternally(h);
                Features.Chat.ChatPanel.OpenExternally(Url);
            }
            finally { Features.Chat.ChatPanel.OpenForTest = null; }
            bool shell = opened.Count == 1 && opened[0] == Url;
            r.Add(("chat media: 'open in your browser' hands the shell nothing but https", shell,
                   shell ? "" : "OPENED: " + string.Join(" ", opened.ToArray())));
        }

        // Two people on one Windows login share account.json, so the second Steam account must never act as the first.
        private static void SteamAccountGate(List<(string, bool, string)> r)
        {
            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "kmh-steamgate-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string previousBase = Features.Global.GlobalHttp.BaseUrl;
            ulong wasId = Features.Account.KmhSteamPersona.SteamIdForTest;
            bool wasNoSteam = Features.Account.KmhSteamPersona.AssumeNoSteamForTest;
            const string dir = "https://directory-a.example";
            const ulong ann = 76561198000000001UL, bea = 76561198000000002UL;

            void Legacy()
            {
                Features.Account.KmhAccountStore.Clear();
                System.IO.Directory.CreateDirectory(scratch);
                System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "account.json"),
                    "{\"SchemaVersion\":1,\"Directory\":\"" + dir + "\",\"PlayerToken\":\"tok-old\","
                  + "\"PlayerGlobalId\":\"pgid-old\",\"DisplayName\":\"Old\",\"LinkedAtUnix\":1}");
                Features.Account.KmhAccountStore.ResetForTest();
            }

            bool WaitForCheck()
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (Features.Account.KmhAccountClient.CheckingForTest && clock.ElapsedMilliseconds < 5000)
                    System.Threading.Thread.Sleep(10);
                return !Features.Account.KmhAccountClient.CheckingForTest;
            }

            try
            {
                Features.Account.KmhAccountStore.RootOverrideForTest = scratch;
                Features.Account.KmhAccountStore.ResetForTest();
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhSteamPersona.AssumeNoSteamForTest = false;
                Features.Global.GlobalHttp.BaseUrl = dir;

                Features.Account.KmhSteamPersona.SteamIdForTest = ann;
                Features.Account.KmhAccountStore.Save(dir, "tok-ann", "pgid-ann", "Ann");
                bool annActs = Features.Account.KmhAccountStore.TokenFor(dir) == "tok-ann";
                Features.Account.KmhSteamPersona.SteamIdForTest = bea;
                string used = "";
                if (Features.Account.KmhAccountStore.TokenFor(dir).Length > 0) used += " token";
                if (Features.Account.KmhAccountStore.HasToken(dir)) used += " linked";
                if (Features.Global.GlobalChatClient.Linked) used += " global-chat";
                if (Features.Global.KmhGlobalDmClient.Linked) used += " dms";
                if (Features.ConnectedTreasury.ConnectedGlobalClient.HasToken) used += " treasury";
                bool kept = Features.Account.KmhAccountStore.HasStoredToken(dir);
                r.Add(("steam gate: another Steam account never acts as the stored player",
                       annActs && used.Length == 0 && kept,
                       !annActs ? "ANN COULD NOT USE HER OWN LINK" : used.Length > 0 ? "USED AS ANN:" + used
                     : kept ? "withheld, kept for its owner" : "THE CREDENTIAL WAS LOST"));

                Features.Account.KmhSteamPersona.SteamIdForTest = ann;
                Features.Account.KmhAccountStore.ResetForTest();
                bool ownerAfterRestart = Features.Account.KmhAccountStore.TokenFor(dir) == "tok-ann"
                                      && Features.Account.KmhAccountStore.BoundSteamId == ann;
                r.Add(("steam gate: the owner's Steam account keeps its link across a restart", ownerAfterRestart,
                       $"bound={Features.Account.KmhAccountStore.BoundSteamId}"));

                // A link saved before the binding existed is not used until Global says whose it is.
                Legacy();
                Features.Account.KmhSteamPersona.SteamIdForTest = ann;
                bool unconfirmed = Features.Account.KmhAccountStore.TokenFor(dir).Length == 0
                                && Features.Account.KmhAccountStore.NeedsSteamCheck(dir, ann);
                Features.Account.KmhAccountClient.ApplyMe(new Features.Global.GlobalHttp.Result
                    { Ok = true, Status = 200, Body = JObject.Parse("{\"player_global_id\":\"pgid-old\",\"display_name\":\"Old\"}") }, ann);
                Features.Account.KmhAccountStore.ResetForTest();
                bool confirmed = Features.Account.KmhAccountStore.TokenFor(dir) == "tok-old"
                              && !Features.Account.KmhAccountStore.NeedsSteamCheck(dir, ann);
                r.Add(("steam gate: an older link is withheld until Global confirms it for this Steam account",
                       unconfirmed && confirmed, $"before confirm withheld={unconfirmed}, after confirm usable={confirmed}"));

                // Global's 409: withheld for the rest of the launch, and what was loaded as that player goes.
                Legacy();
                Features.Account.KmhSteamPersona.SteamIdForTest = bea;
                int before = Features.Global.KmhIdentityGeneration.Current;
                var mismatch = new Features.Global.GlobalHttp.Result { Ok = false, Status = 409, ErrorCode = "KMH-LNK-709" };
                Features.Account.KmhAccountClient.ApplyMe(mismatch, bea);
                int once = Features.Global.KmhIdentityGeneration.Current;
                Features.Account.KmhAccountClient.ApplyMe(mismatch, bea);
                bool refused = Features.Account.KmhAccountClient.State == Features.Account.AccountState.WrongSteamAccount
                            && Features.Account.KmhAccountStore.TokenFor(dir).Length == 0
                            && Features.Account.KmhAccountStore.HasStoredToken(dir);
                bool torn = once != before && Features.Global.KmhIdentityGeneration.Current == once;
                bool quiet = !Features.Account.KmhAccountStore.NeedsSteamCheck(dir, bea);
                r.Add(("steam gate: Global naming another Steam account tears the identity down and withholds it",
                       refused && torn && quiet, $"withheld={refused} tornDownOnce={torn} stopsAsking={quiet}"));

                // No Steam id is no evidence, as on Global: a copy launched outside Steam keeps its link.
                Features.Account.KmhSteamPersona.SteamIdForTest = ann;
                Features.Account.KmhAccountStore.Save(dir, "tok-ann", "pgid-ann", "Ann");
                Features.Account.KmhSteamPersona.SteamIdForTest = 0;
                Features.Account.KmhSteamPersona.AssumeNoSteamForTest = true;
                bool noSteam = Features.Account.KmhAccountStore.TokenFor(dir) == "tok-ann";
                Features.Account.KmhSteamPersona.AssumeNoSteamForTest = false;
                bool table = Features.Account.KmhAccountStore.MayAct(ann, ann, false)
                          && !Features.Account.KmhAccountStore.MayAct(bea, ann, false)
                          && !Features.Account.KmhAccountStore.MayAct(ann, 0, false)
                          && !Features.Account.KmhAccountStore.MayAct(ann, ann, true)
                          && Features.Account.KmhAccountStore.MayAct(0, ann, false);
                r.Add(("steam gate: without a Steam id the link is read the way Global reads it", noSteam && table,
                       $"noSteam={noSteam} table={table}"));

                // Asked from the frame pump, so a background client never waits for the Account dialog to be opened.
                bool pumped = false;
                var steps = typeof(Patches.Patch_Root_Update_KmhPump).GetField("Steps",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as Action[];
                if (steps != null)
                    foreach (Action a in steps)
                        if (a != null && a.Method.Name == "Watch" && a.Method.DeclaringType == typeof(Features.Account.KmhAccountClient))
                            pumped = true;

                Legacy();
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhSteamPersona.SteamIdForTest = ann;
                int calls = Features.Global.GlobalHttp.SuppressedCallCount;
                Features.Account.KmhAccountClient.Watch();
                bool settled = WaitForCheck();
                bool asked = Features.Global.GlobalHttp.SuppressedCallCount == calls + 1;
                Features.Account.KmhAccountClient.Watch();
                bool backedOff = WaitForCheck() && Features.Global.GlobalHttp.SuppressedCallCount == calls + 1;

                Features.Account.KmhAccountStore.Save(dir, "tok-ann", "pgid-ann", "Ann");
                Features.Account.KmhAccountClient.ResetForTest();
                int boundCalls = Features.Global.GlobalHttp.SuppressedCallCount;
                Features.Account.KmhAccountClient.Watch();
                bool boundQuiet = WaitForCheck() && Features.Global.GlobalHttp.SuppressedCallCount == boundCalls;
                r.Add(("steam gate: the frame pump asks Global once about an unconfirmed link, and never about a confirmed one",
                       pumped && settled && asked && backedOff && boundQuiet,
                       $"pumped={pumped} asked={asked} backedOff={backedOff} confirmedQuiet={boundQuiet}"));
            }
            catch (Exception ex) { r.Add(("steam gate: the Steam account gate runs outside the game", false, ex.Message)); }
            finally
            {
                Features.Account.KmhSteamPersona.SteamIdForTest = wasId;
                Features.Account.KmhSteamPersona.AssumeNoSteamForTest = wasNoSteam;
                Features.Global.GlobalHttp.BaseUrl = previousBase;
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhAccountStore.RootOverrideForTest = null;
                Features.Account.KmhAccountStore.ResetForTest();
                try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, true); } catch (Exception) { }
            }
        }

        private static void IdentityChangePaths(List<(string, bool, string)> r)
        {
            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "kmh-ident-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string previousBase = Features.Global.GlobalHttp.BaseUrl;
            const string dir = "https://directory-a.example";
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

            void Set(Type t, string member, object value)
            {
                System.Reflection.PropertyInfo p = t.GetProperty(member, any);
                if (p != null) p.SetValue(null, value);
                else t.GetField(member, any).SetValue(null, value);
            }

            void SeedAccountA()
            {
                Features.Account.KmhUsernameClient.SeedForTest("ann", true, 0, "");
                Set(typeof(Features.Friends.KmhFriendsClient), "Friends",
                    new List<Features.Friends.FriendDto> { new Features.Friends.FriendDto { PlayerGlobalId = new string('a', 32) } });
                Set(typeof(Features.Account.KmhPrivacyClient), "State", Features.Account.PrivacyState.Loaded);
                Set(typeof(Features.Account.KmhDiscordLinkClient), "Linked", true);
                Set(typeof(Features.Account.KmhDiscordLinkClient), "DisplayName", "ann-discord");
                Set(typeof(Features.Account.KmhDiscordLinkClient), "State", Features.Account.DiscordLinkState.Ready);
                Set(typeof(Features.Friends.KmhPresenceReporter), "_published", true);
                Set(typeof(Features.Friends.KmhSteamDiscoveryClient), "LastMatched", 3);
                Features.ConnectedTreasury.ConnectedTreasuryCache.ApplyBalance(
                    new Features.ConnectedTreasury.ConnectedBalance { Settled = 500 }, "");
            }

            // Named, so a failure says which cache never registered its teardown.
            string LeftOfA()
            {
                var left = new List<string>();
                if (Features.Account.KmhUsernameClient.Username.Length > 0) left.Add("username");
                if (Features.Friends.KmhFriendsClient.Friends.Count > 0) left.Add("friends");
                if (Features.Account.KmhPrivacyClient.State == Features.Account.PrivacyState.Loaded) left.Add("privacy");
                if (Features.Account.KmhDiscordLinkClient.Linked
                    || Features.Account.KmhDiscordLinkClient.DisplayName.Length > 0) left.Add("discord");
                if (Features.Friends.KmhPresenceReporter.PublishedForTest) left.Add("presence");
                if (Features.Friends.KmhSteamDiscoveryClient.LastMatched > 0) left.Add("steam discovery");
                if (Features.ConnectedTreasury.ConnectedTreasuryCache.Balance != null) left.Add("connected balance");
                return string.Join(", ", left.ToArray());
            }

            int Generation() => Features.Global.KmhIdentityGeneration.Current;

            try
            {
                Features.Account.KmhAccountStore.RootOverrideForTest = scratch;
                Features.Account.KmhAccountStore.ResetForTest();
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Global.GlobalHttp.BaseUrl = dir;

                // Try again after an outage, then approving the new code: B's token lands over A's with no unlink.
                Features.Account.KmhAccountStore.Save(dir, "tok-a", "pgid-a", "Ann");
                SeedAccountA();
                int before = Generation();
                Features.Account.KmhAccountClient.SeedWaitingForTest("chal-b", Soon());
                Features.Account.KmhAccountClient.Apply(JObject.Parse(
                    "{\"state\":\"Consumed\",\"player_token\":\"tok-b\",\"player_global_id\":\"pgid-b\",\"display_name\":\"Bea\"}"));
                string left = LeftOfA();
                bool relink = Generation() != before && left.Length == 0
                           && Features.Account.KmhAccountStore.TokenFor(dir) == "tok-b";
                r.Add(("identity: linking over a live link tears the previous account down", relink,
                       relink ? "generation moved, nothing of A left"
                              : $"generation {before} -> {Generation()}, A's still loaded: {left}"));

                // Signed out everywhere from another device: /me says 401, and the next link must start empty.
                Features.Account.KmhAccountStore.Save(dir, "tok-a", "pgid-a", "Ann");
                SeedAccountA();
                before = Generation();
                Features.Account.KmhAccountClient.ApplyMe(new Features.Global.GlobalHttp.Result
                    { Ok = false, Status = 401, ErrorCode = "KMH-LNK-705" });
                left = LeftOfA();
                bool revoked = Generation() != before && left.Length == 0
                            && !Features.Account.KmhAccountStore.HasToken(dir)
                            && Features.Account.KmhAccountClient.State == Features.Account.AccountState.NotLinked
                            && Features.Account.KmhAccountClient.PlayerGlobalId.Length == 0;
                r.Add(("identity: a token revoked from the web tears the account down with the credential", revoked,
                       revoked ? "generation moved, nothing of A left"
                               : $"generation {before} -> {Generation()}, A's still loaded: {left}"));

                Features.Account.KmhAccountStore.Save(dir, "tok-a", "pgid-a", "Ann");
                SeedAccountA();
                before = Generation();
                Features.Account.KmhAccountStore.Save(dir, "tok-a", "pgid-a", "Ann Again");
                Features.Account.KmhAccountClient.ApplyMe(new Features.Global.GlobalHttp.Result
                    { Ok = true, Status = 200, Body = JObject.Parse("{\"player_global_id\":\"pgid-a\",\"display_name\":\"Ann\"}") });
                bool kept = Generation() == before && Features.Friends.KmhFriendsClient.Friends.Count == 1
                         && Features.ConnectedTreasury.ConnectedTreasuryCache.Balance != null;
                r.Add(("identity: re-reading the same account keeps what it has loaded", kept,
                       kept ? "no teardown" : $"generation {before} -> {Generation()}"));

                // A locally refused stale answer must not reach WrongSteamAccount, which offers a destructive Unlink.
                Features.Account.KmhAccountClient.ApplyMe(new Features.Global.GlobalHttp.Result
                    { Ok = false, Status = 409, ErrorCode = Features.Global.GlobalHttp.IdentityChangedCode });
                Features.Account.AccountState afterStale = Features.Account.KmhAccountClient.State;
                Features.Account.KmhAccountClient.ApplyMe(new Features.Global.GlobalHttp.Result
                    { Ok = false, Status = 409, ErrorCode = "KMH-LNK-709" });
                bool stale = afterStale != Features.Account.AccountState.WrongSteamAccount
                          && Features.Account.KmhAccountClient.State == Features.Account.AccountState.WrongSteamAccount
                          && Features.Account.KmhAccountStore.HasStoredToken(dir);
                r.Add(("identity: a stale answer refused locally is not reported as the wrong Steam account", stale,
                       stale ? "only Global's own 409 means another Steam account" : $"stale answer -> {afterStale}"));

                bool recheck = !Features.Account.KmhAccountClient.RetryIsRelink;
                Features.Account.KmhAccountStore.Clear();
                bool relinkWhenEmpty = Features.Account.KmhAccountClient.RetryIsRelink;
                r.Add(("account: try again while Global is down re-checks a held link instead of starting a new one",
                       recheck && relinkWhenEmpty, $"held={recheck} empty={relinkWhenEmpty}"));

                // A directory switch goes through the same teardown, so it reaches the same caches.
                Features.Account.KmhAccountStore.Save(dir, "tok-a", "pgid-a", "Ann");
                SeedAccountA();
                before = Generation();
                Features.Global.KmhDirectoryClient.ForgetDirectory();
                left = LeftOfA();
                bool switched = Generation() != before && left.Length == 0;
                r.Add(("identity: switching directory empties every per-account cache", switched,
                       switched ? "nothing of A left" : $"A's still loaded: {left}"));
            }
            catch (Exception ex) { r.Add(("identity: the identity-change paths run outside the game", false, ex.Message)); }
            finally
            {
                Features.Global.GlobalHttp.BaseUrl = previousBase;
                Features.Account.KmhAccountClient.ResetForTest();
                Features.Account.KmhAccountStore.RootOverrideForTest = null;
                Features.Account.KmhAccountStore.ResetForTest();
                Features.Account.KmhUsernameClient.ResetForTest();
                Features.Friends.KmhFriendsClient.Reset();
                Features.Account.KmhPrivacyClient.Reset();
                Features.Account.KmhDiscordLinkClient.Reset();
                Features.Friends.KmhPresenceReporter.Reset();
                Features.Friends.KmhSteamDiscoveryClient.Reset();
                Features.ConnectedTreasury.ConnectedTreasuryCache.ForgetBalance();
                try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, true); } catch (Exception) { }
            }
        }

        // These fixtures are the ones Global's own suite asserts, so the client's mirror of its rules cannot drift unseen.
        private static void PlayerUsername(List<(string, bool, string)> r)
        {
            string previousBase = Features.Global.GlobalHttp.BaseUrl;
            try
            {
                // A directory this machine holds no credential for, so nothing here can reach the network.
                Features.Global.GlobalHttp.BaseUrl = "https://username-selftest.example";
                Features.Account.KmhUsernameClient.ResetForTest();

                bool shapes = Features.Account.KmhUsernameRules.LooksValid("KNAPPE0")
                           && Features.Account.KmhUsernameRules.LooksValid("twisted_john")
                           && Features.Account.KmhUsernameRules.LooksValid("abc")
                           && !Features.Account.KmhUsernameRules.LooksValid("ab")
                           && !Features.Account.KmhUsernameRules.LooksValid("0leading")
                           && !Features.Account.KmhUsernameRules.LooksValid("_leading")
                           && !Features.Account.KmhUsernameRules.LooksValid("has space")
                           && !Features.Account.KmhUsernameRules.LooksValid("hyphen-ated")
                           && !Features.Account.KmhUsernameRules.LooksValid("émoji")
                           && !Features.Account.KmhUsernameRules.LooksValid(new string('a', 25));
                r.Add(("username: the local hint accepts and refuses exactly the shapes Global does", shapes, ""));

                bool suggests = Features.Account.KmhUsernameRules.SuggestFrom("Knappe 0!") == "Knappe_0"
                             && Features.Account.KmhUsernameRules.SuggestFrom("  spaced  out  ") == "spaced_out"
                             && Features.Account.KmhUsernameRules.SuggestFrom("123456") == ""
                             && Features.Account.KmhUsernameRules.SuggestFrom("a") == ""
                             && Features.Account.KmhUsernameRules.SuggestFrom(null) == "";
                r.Add(("username: a Steam persona converts to the same suggestion Global would make", suggests,
                       Features.Account.KmhUsernameRules.SuggestFrom("Knappe 0!")));

                r.Add(("username: an empty field explains nothing rather than showing a red error",
                       Features.Account.KmhUsernameRules.Explain("").Length == 0, ""));

                Features.Account.KmhUsernameClient.ApplyStatus(JObject.Parse(
                    "{\"player_global_id\":\"pgid-1\",\"username\":\"KNAPPE0\",\"can_change\":false," +
                    "\"change_allowed_at\":1790000000,\"suggestion\":\"\"}"));
                bool applied = Features.Account.KmhUsernameClient.Username == "KNAPPE0"
                            && !Features.Account.KmhUsernameClient.CanChange
                            && Features.Account.KmhUsernameClient.ChangeAllowedAtUnix == 1790000000L
                            && Features.Account.KmhUsernameClient.State == Features.Account.UsernameState.Ready;
                r.Add(("username: Global's status shape is read whole, cooldown included", applied,
                       applied ? "KNAPPE0, locked until " + Features.Account.KmhUsernameClient.ChangeAllowedText()
                               : "PARSE MISMATCH"));

                Features.Account.KmhUsernameClient.ApplyStatus(JObject.Parse(
                    "{\"player_global_id\":\"pgid-2\",\"username\":null,\"can_change\":true," +
                    "\"change_allowed_at\":0,\"suggestion\":\"Knappe_0\"}"));
                bool unnamed = !Features.Account.KmhUsernameClient.HasUsername
                            && Features.Account.KmhUsernameClient.CanChange
                            && Features.Account.KmhUsernameClient.Suggestion == "Knappe_0";
                r.Add(("username: an account that predates usernames is offered one rather than refused", unnamed, ""));

                // A lookup per keystroke would hammer Global from a text field.
                Features.Account.KmhUsernameClient.Typed("someone");
                for (int i = 0; i < Features.Account.KmhUsernameClient.CheckDelayTicks - 1; i++)
                    Features.Account.KmhUsernameClient.Tick();
                int early = Features.Account.KmhUsernameClient.ChecksStarted;
                Features.Account.KmhUsernameClient.Tick();
                int after = Features.Account.KmhUsernameClient.ChecksStarted;
                bool debounced = early == 0 && after == 1;
                r.Add(("username: typing asks Global once the field settles, not once per frame", debounced,
                       $"{Features.Account.KmhUsernameClient.CheckDelayTicks - 1} ticks={early}, next tick={after}"));

                Features.Account.KmhUsernameClient.Typed("no spaces here");
                for (int i = 0; i < Features.Account.KmhUsernameClient.CheckDelayTicks + 5; i++)
                    Features.Account.KmhUsernameClient.Tick();
                bool skipped = Features.Account.KmhUsernameClient.ChecksStarted == after;
                r.Add(("username: a locally invalid handle is never sent to Global at all", skipped,
                       $"checks={Features.Account.KmhUsernameClient.ChecksStarted}"));

                bool explains = Features.Account.KmhUsernameClient.Explain("KMH-USR-802").IndexOf("taken",
                                    StringComparison.OrdinalIgnoreCase) >= 0
                             && Features.Account.KmhUsernameClient.Explain("KMH-USR-803").IndexOf("recently",
                                    StringComparison.OrdinalIgnoreCase) >= 0
                             && Features.Account.KmhUsernameClient.Explain("").Length == 0;
                r.Add(("username: every refusal Global can send has a sentence a player can act on", explains, ""));

                // Steam is stood down, not assumed absent: assuming absence fails for every player launched from Steam.
                Features.Account.KmhSteamPersona.AssumeNoSteamForTest = true;
                Features.Account.KmhSteamPersona.ResetForTest();
                bool quietWithoutSteam = Features.Account.KmhSteamPersona.Name == ""
                                      && !Features.Account.KmhSteamPersona.Known;
                Features.Account.KmhSteamPersona.AssumeNoSteamForTest = false;
                Features.Account.KmhSteamPersona.ResetForTest();
                r.Add(("username: a copy of the game launched outside Steam reads no persona and does not throw",
                       quietWithoutSteam, ""));

                // Both answers: offline Steam is absent either way, so a one-sided check stays green with the seam gone.
                bool readsWhenPresent   = Features.Account.KmhSteamPersona.SteamAvailable(false, true);
                bool quietWhenStoodDown = !Features.Account.KmhSteamPersona.SteamAvailable(true, true);
                bool quietWhenAbsent    = !Features.Account.KmhSteamPersona.SteamAvailable(false, false);
                r.Add(("username: the no-Steam check reads the seam, not whether this machine has Steam",
                       readsWhenPresent && quietWhenStoodDown && quietWhenAbsent,
                       readsWhenPresent ? "" : "THE CHECK PASSES WHETHER OR NOT THE CODE WORKS"));

                Features.Account.KmhSteamPersona.SeedForTest("Knappe 0!");
                bool localFallback = Features.Account.KmhSteamPersona.Known
                                  && Features.Account.KmhUsernameRules.SuggestFrom(
                                         Features.Account.KmhSteamPersona.Name) == "Knappe_0";
                r.Add(("username: the local Steam persona fills the suggestion Global cannot make", localFallback, ""));

                Features.Account.KmhUsernameClient.SeedForTest("SOMEONE", true, 0, "");
                Features.Account.KmhUsernameClient.Reset();
                bool cleared = !Features.Account.KmhUsernameClient.HasUsername
                            && Features.Account.KmhUsernameClient.State == Features.Account.UsernameState.Unknown;
                r.Add(("username: unlinking clears the handle rather than leaving it on screen", cleared, ""));
            }
            catch (Exception ex) { r.Add(("username: the client username paths run outside the game", false, ex.Message)); }
            finally
            {
                Features.Global.GlobalHttp.BaseUrl = previousBase;
                Features.Account.KmhUsernameClient.ResetForTest();
                Features.Account.KmhSteamPersona.ResetForTest();
            }
        }

        // A link carrying the code as prose 404s; everything handed to a browser must survive Uri parsing.
        private static void DeepLinkShape(List<(string, bool, string)> r)
        {
            const string b = "https://global.knappe0.games/link-account";

            string plain = Features.Account.KmhAccountClient.BuildDeepLink(b, "1SC4-D4GX");
            bool ok = plain == b + "?code=1SC4-D4GX"
                   && Uri.TryCreate(plain, UriKind.Absolute, out Uri u) && u.Scheme == "https";
            r.Add(("account deep link: the code is a query parameter, not appended prose", ok, plain));

            r.Add(("account deep link: it contains no spaces or literal 'code:' text",
                   plain.IndexOf(' ') < 0 && plain.IndexOf("code: ", StringComparison.Ordinal) < 0, ""));

            string spaced = Features.Account.KmhAccountClient.BuildDeepLink(b, "  1SC4 D4GX  ");
            bool encoded = Uri.TryCreate(spaced, UriKind.Absolute, out Uri su) && su.Query.Contains("%20")
                        && spaced.IndexOf(' ') < 0;
            r.Add(("account deep link: a code containing spaces is encoded, never left raw", encoded, spaced));

            string second = Features.Account.KmhAccountClient.BuildDeepLink(b + "?next=1", "AAAA-BBBB");
            r.Add(("account deep link: a url that already has a query gets '&', not a second '?'",
                   second.EndsWith("?next=1&code=AAAA-BBBB", StringComparison.Ordinal), second));

            r.Add(("account deep link: with no code it is still the plain valid page url",
                   Features.Account.KmhAccountClient.BuildDeepLink(b, "") == b, ""));
            r.Add(("account deep link: with no url it is empty rather than a broken fragment",
                   Features.Account.KmhAccountClient.BuildDeepLink("", "ABCD") == "", ""));

            string weird = Features.Account.KmhAccountClient.BuildDeepLink(b, "A&B=C#D");
            bool safe = Uri.TryCreate(weird, UriKind.Absolute, out Uri wu)
                     && wu.Query.IndexOf('#') < 0 && wu.Query.IndexOf('&', 1) < 0;
            r.Add(("account deep link: a malformed code cannot inject extra query or fragment", safe, weird));
        }

        // Real metadata from the official repository, so the classifier is proven against names RWT actually publishes.
        private const string RwtReleasesFixture = @"[
          {""tag_name"":""26.8.31.1"",""prerelease"":false,""draft"":false,""published_at"":""2026-08-31T00:00:00Z"",
           ""assets"":[
             {""name"":""3005289691.zip"",""browser_download_url"":""https://github.com/RimWorld-Together/Rimworld-Together/releases/download/fixture/c.zip"",""size"":2483440,""digest"":""sha256:b54ba9ef9b5a136dbb54ba9ef9b5a136dbb54ba9ef9b5a136dbb54ba9ef9b5a13""},
             {""name"":""Server-linux-x64.zip"",""browser_download_url"":""https://github.com/RimWorld-Together/Rimworld-Together/releases/download/fixture/sl.zip"",""size"":29092938,""digest"":""sha256:c42e2d9e7bb87db59c42e2d9e7bb87db59c42e2d9e7bb87db59c42e2d9e7bb87d""},
             {""name"":""Server-win-x64.zip"",""browser_download_url"":""https://github.com/RimWorld-Together/Rimworld-Together/releases/download/fixture/sw.zip"",""size"":29443247,""digest"":null},
             {""name"":""linux-x64.zip"",""browser_download_url"":""https://github.com/RimWorld-Together/Rimworld-Together/releases/download/fixture/l.zip"",""size"":29092942,""digest"":null},
             {""name"":""Slim-linux-x64.zip"",""browser_download_url"":""https://github.com/RimWorld-Together/Rimworld-Together/releases/download/fixture/sll.zip"",""size"":627934,""digest"":null}]},
          {""tag_name"":""26.8.16.1_(1)"",""prerelease"":false,""draft"":false,""published_at"":""2026-08-21T00:00:00Z"",
           ""assets"":[{""name"":""3005289691.zip"",""browser_download_url"":""https://github.com/RimWorld-Together/Rimworld-Together/releases/download/fixture/c2.zip"",""size"":2480489,""digest"":null}]},
          {""tag_name"":""26.6.9.1"",""prerelease"":false,""draft"":false,""published_at"":""2026-06-09T00:00:00Z"",
           ""assets"":[{""name"":""3005289691.zip"",""browser_download_url"":""https://github.com/RimWorld-Together/Rimworld-Together/releases/download/fixture/c3.zip"",""size"":2400000,""digest"":null}]},
          {""tag_name"":""27.0.0.1-rc"",""prerelease"":true,""draft"":false,""published_at"":""2026-09-10T00:00:00Z"",
           ""assets"":[{""name"":""3005289691.zip"",""browser_download_url"":""https://github.com/RimWorld-Together/Rimworld-Together/releases/download/fixture/c4.zip"",""size"":2500000,""digest"":null}]},
          {""tag_name"":""99.9.9.9"",""prerelease"":false,""draft"":true,""published_at"":""2026-09-11T00:00:00Z"",
           ""assets"":[{""name"":""3005289691.zip"",""browser_download_url"":""https://github.com/RimWorld-Together/Rimworld-Together/releases/download/fixture/c5.zip"",""size"":1,""digest"":null}]}]";

        private static Features.Rwt.RwtVersion Rv(string raw) => Features.Rwt.RwtVersion.From(raw);

        // A display name and a version are both plain strings, so a misfilled keyed string can hand one off as the other.
        private static void RwtVersionIsNotADisplayName(List<(string, bool, string)> r)
        {
            const string DisplayName = "KMH | Vanilla | Official";

            r.Add(("rwt version: a server display name cannot become a version",
                   !Features.Rwt.RwtVersion.TryParse(DisplayName, out _)
                   && !Features.Rwt.RwtVersion.From(DisplayName).IsKnown, DisplayName));

            foreach (string notAVersion in new[] { "KMH | Legacy Test", "Vanilla", "rwt.knappe0.games",
                                                   "KMH | Test 2 | Current RWT (26.8.31.1)", "1.3.1-rel-221" })
                if (Features.Rwt.RwtVersion.From(notAVersion).IsKnown)
                    r.Add(("rwt version: a non-version string was accepted as a version", false, notAVersion));

            r.Add(("rwt version: real versions still parse, decorations and all",
                   Features.Rwt.RwtVersion.From("26.8.31.1").Value == "26.8.31.1"
                   && Features.Rwt.RwtVersion.From("Release 26.8.31.1").Value == "26.8.31.1"
                   && Features.Rwt.RwtVersion.From("26.8.16.1_(1)").Value == "26.8.16.1", ""));

            // A display name handed to the resolver resolves nothing, rather than resolving something wrong.
            Features.Rwt.RwtInstallPlan bogus =
                Features.Rwt.RwtResolver.PlanClient(Rv(DisplayName), Rv("26.6.9.1"));
            r.Add(("rwt resolve: a display name produces no download at all",
                   !bogus.CanInstall && bogus.State == Features.Rwt.RwtPlanState.NoVersionRequested,
                   bogus.State.ToString()));

            // A keyed string with fewer placeholders than the code passes renders the wrong argument at {0}.
            string stale = "RimWorld Together will download version {0} and replace your current copy.";
            string good  = "{0} requires RimWorld Together {1}. Package {2} ({3}).";
            bool guards = !UI.KmhText.Accepts(stale, 4) && UI.KmhText.Accepts(good, 4)
                       && UI.KmhText.Accepts(stale, 1) && UI.KmhText.Accepts("{0} of {1:N0}", 2)
                       && !UI.KmhText.Accepts("{0} of {10}", 2) && !UI.KmhText.Accepts("{{1}} {0", 2);
            r.Add(("text: a keyed string with too few placeholders is refused, not silently misfilled", guards, ""));

            string rendered = UI.KmhText.Get("KMH_Definitely_Not_A_Real_Key",
                "{0} requires RimWorld Together {1}.", DisplayName, "26.8.31.1");
            bool ordered = rendered.StartsWith(DisplayName, StringComparison.Ordinal)
                        && rendered.Contains("RimWorld Together 26.8.31.1");
            r.Add(("text: the confirmation names the server first and the version second", ordered, rendered));
        }

        // Global Chat talks to Global directly, so none of this may depend on a server connection.
        private static void GlobalChatTransport(List<(string, bool, string)> r)
        {
            Features.Global.GlobalChatClient.ResetForTest();
            try
            {
                Features.Global.GlobalChatEntry One(string id, long at, string body = "hi")
                    => new Features.Global.GlobalChatEntry
                    { MessageId = id, UserId = "u1", Username = "KNAPPE0", Body = body, SentAtUnix = at };

                r.Add(("global chat: a message is accepted once", Features.Global.GlobalChatClient.Accept(One("m1", 10)), ""));

                // A reconnect replays recent history, so the same id must not appear twice in the log.
                bool twice = Features.Global.GlobalChatClient.Accept(One("m1", 10));
                r.Add(("global chat: the same message id is never shown twice", !twice
                       && Features.Global.GlobalChatClient.Snapshot().Count == 1, ""));

                Features.Global.GlobalChatClient.Accept(One("m0", 5, "older"));
                var ordered = Features.Global.GlobalChatClient.Snapshot();
                r.Add(("global chat: replayed history sorts into place by time", ordered.Count == 2
                       && ordered[0].MessageId == "m0" && ordered[1].MessageId == "m1", ""));

                // A frame already off the socket when the player signed out must not land in the next account's log.
                {
                    Features.Global.GlobalChatClient.ResetForTest();
                    int inFlight = Features.Global.GlobalChatClient.SessionGeneration;
                    Features.Global.GlobalChatClient.Forget();
                    Features.Global.GlobalChatClient.AcceptHistory(
                        new List<Features.Global.GlobalChatEntry> { One("stale", 99, "from the previous account") },
                        inFlight);
                    int landed = Features.Global.GlobalChatClient.Snapshot().Count;
                    r.Add(("global chat: a frame from the signed-out session is dropped, not shown to the next account",
                           landed == 0, landed == 0 ? "dropped" : $"{landed} STALE MESSAGE(S) SURVIVED SIGN-OUT"));
                    Features.Global.GlobalChatClient.ResetForTest();
                }

                r.Add(("global chat: a malformed frame is ignored rather than accepted",
                       !Features.Global.GlobalChatClient.Accept(null)
                       && !Features.Global.GlobalChatClient.Accept(One("", 1))
                       && Features.Global.GlobalChatClient.Parse(null) == null, ""));

                for (int i = 0; i < Features.Global.GlobalChatClient.MaxHeld + 120; i++)
                    Features.Global.GlobalChatClient.Accept(One("bulk-" + i, 100 + i));
                int held = Features.Global.GlobalChatClient.Snapshot().Count;
                r.Add(("global chat: the held log is bounded", held <= Features.Global.GlobalChatClient.MaxHeld,
                       $"{held} <= {Features.Global.GlobalChatClient.MaxHeld}"));

                // Dropping the oldest must also forget its id, or the window becomes a permanent leak.
                bool reacceptable = Features.Global.GlobalChatClient.Accept(One("m0", 5, "older"));
                r.Add(("global chat: an id evicted from the window is no longer remembered for ever", reacceptable, ""));

                // Each channel kind reads its own setting: falling through to ChatNotifyServer silences Global for everyone.
                // Asserted without pumping the main-thread queue, which would run production work mid-handshake.
                {
                    var s0 = new KMHPatchSettings
                    { ChatNotifyServer = 0, ChatNotifyGuild = 1, ChatNotifyGlobal = 2, ChatNotifyDm = 1 };
                    string dm = Features.Chat.ChatChannels.Dm("a", "b");
                    var lvl = new Func<string, Features.Chat.CommsNotifyLevel>(c => Features.Chat.KmhCommsNotifications.LevelFor(c, s0));

                    bool ownLevel = lvl(Features.Chat.ChatChannels.Server) == Features.Chat.CommsNotifyLevel.Silent
                                 && lvl(Features.Chat.ChatChannels.Guild("W")) == Features.Chat.CommsNotifyLevel.Toast
                                 && lvl(Features.Chat.ChatChannels.Global) == Features.Chat.CommsNotifyLevel.ToastSound
                                 && lvl(dm) == Features.Chat.CommsNotifyLevel.Toast;
                    r.Add(("comms: every channel kind reads its own notification setting", ownLevel,
                           ownLevel ? "" : "A CHANNEL IS INHERITING ANOTHER CHANNEL'S LEVEL"));

                    // A toast must name its channel, or a network-wide line reads as one from the player's own server.
                    var lab = new Func<string, string>(Features.Chat.KmhCommsNotifications.Label);
                    bool named = lab(Features.Chat.ChatChannels.Global) != lab(Features.Chat.ChatChannels.Server)
                              && lab(Features.Chat.ChatChannels.Global).StartsWith("Global")
                              && lab(dm).StartsWith("DM")
                              && lab(Features.Chat.ChatChannels.Guild("W")).StartsWith("Guild");
                    r.Add(("comms: a toast names the channel it came from", named,
                           named ? "" : "GLOBAL AND SERVER CHAT LOOK THE SAME IN A TOAST"));

                    // Default-on: inheriting a default meant for another channel keeps Global silent.
                    var fresh = new KMHPatchSettings();
                    r.Add(("comms: Global Chat notifies by default",
                           Features.Chat.KmhCommsNotifications.LevelFor(Features.Chat.ChatChannels.Global, fresh)
                           != Features.Chat.CommsNotifyLevel.Silent, ""));
                }

                r.Add(("global chat: backlog does not notify, anything later does",
                       !Features.Chat.GlobalChatBridge.ShouldNotify(primingHistory: true)
                    &&  Features.Chat.GlobalChatBridge.ShouldNotify(primingHistory: false), ""));

                // And the cache honours the flag. One unique body, so nothing else arriving can be mistaken for it.
                {
                    string mark = "kmh-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    int seen = 0;
                    Action<Features.Chat.Dto.ChatMessage> tap = m => { if (m != null && m.Body == mark) seen++; };
                    Features.Chat.ChatCache.MessageArrived += tap;
                    try
                    {
                        var silent = new Features.Chat.Dto.ChatMessage
                        { Id = -9001, Channel = Features.Chat.ChatChannels.Global, FromUsername = "h", Body = mark };
                        var loud = new Features.Chat.Dto.ChatMessage
                        { Id = -9002, Channel = Features.Chat.ChatChannels.Global, FromUsername = "h", Body = mark };

                        Features.Chat.ChatCache.Append(silent, notify: false);
                        int afterSilent = seen;
                        Features.Chat.ChatCache.Append(loud, notify: true);

                        r.Add(("global chat: a history line lands in the log without raising a notification",
                               afterSilent == 0 && seen == 1,
                               afterSilent == 0 ? "" : "A HISTORY LINE RAISED A NOTIFICATION"));
                    }
                    finally { Features.Chat.ChatCache.MessageArrived -= tap; }
                }

                {
                    Features.Global.GlobalChatClient.ResetForTest();
                    var flags = new List<bool>();
                    Action<Features.Global.GlobalChatEntry> prevArrived = Features.Global.GlobalChatClient.Arrived;
                    Features.Global.GlobalChatClient.Arrived =
                        e => flags.Add(Features.Global.GlobalChatClient.PrimingHistory);
                    try
                    {
                        Features.Global.GlobalChatClient.AcceptHistory(new List<Features.Global.GlobalChatEntry>
                            { One("h1", 10, "days ago"), One("h2", 11, "also days ago") });
                        bool backlogPrimed = flags.Count == 2 && flags[0] && flags[1];

                        Features.Global.GlobalChatClient.AcceptHistory(new List<Features.Global.GlobalChatEntry>
                            { One("h3", 12, "missed while offline") });
                        Features.Global.GlobalChatClient.Accept(One("s1", 13, "live"));
                        bool laterIsNews = flags.Count == 4 && !flags[2] && !flags[3];

                        r.Add(("global chat: only the first history load of a launch is treated as backlog",
                               backlogPrimed && laterIsNews,
                               backlogPrimed ? "" : "THE BACKLOG WAS NOT PRIMED"));
                    }
                    finally
                    {
                        Features.Global.GlobalChatClient.Arrived = prevArrived;
                        Features.Global.GlobalChatClient.ResetForTest();
                    }
                }

                TimeSpan first = Features.Global.GlobalChatClient.BackoffFor(0);
                // Jittered, so individual samples are not ordered; what must hold is the window they fall in.
                TimeSpan capped = Features.Global.GlobalChatClient.BackoffFor(50);
                bool backoff = first >= Features.Global.KmhRealtimeBackoff.Min
                            && capped <= Features.Global.KmhRealtimeBackoff.Max
                            && Features.Global.GlobalChatClient.BackoffFor(100) <= Features.Global.KmhRealtimeBackoff.Max;
                r.Add(("global chat: reconnects back off and are capped", backoff,
                       $"{(int)first.TotalSeconds}s first, capped at {(int)Features.Global.KmhRealtimeBackoff.Max.TotalSeconds}s"));

                // Capability decides availability, never a hardcoded build number.
                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Online, true, false);
                bool offWhenUnsupported = !Features.Global.GlobalChatClient.Available;
                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Online, true, true);
                bool onWhenSupported = Features.Global.GlobalChatClient.Available;
                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Offline, true, true);
                bool offWhenDown = !Features.Global.GlobalChatClient.Available;
                r.Add(("global chat: availability comes from capability negotiation",
                       offWhenUnsupported && onWhenSupported && offWhenDown, ""));

                r.Add(("global chat: every refusal Global can send has a sentence a player can act on",
                       Features.Global.GlobalChatClient.Explain("KMH-GCH-902").IndexOf("username",
                           StringComparison.OrdinalIgnoreCase) >= 0
                       && Features.Global.GlobalChatClient.Explain("KMH-GCH-905").Length > 0
                       && Features.Global.GlobalChatClient.Explain("").Length == 0, ""));

                bool distinct = Features.Chat.ChatChannels.IsGlobal(Features.Chat.ChatChannels.Global)
                             && !Features.Chat.ChatChannels.IsGlobal(Features.Chat.ChatChannels.Server)
                             && Features.Chat.ChatChannels.IsServerLocal(Features.Chat.ChatChannels.Server)
                             && !Features.Chat.ChatChannels.IsServerLocal(Features.Chat.ChatChannels.Global)
                             && !Features.Chat.ChatChannels.IsGuild(Features.Chat.ChatChannels.Global)
                             && !Features.Chat.ChatChannels.IsDm(Features.Chat.ChatChannels.Global);
                r.Add(("global chat: Global, Server, Guild and DM stay four distinct channels", distinct, ""));

                // It lands in the normal chat log, so the whole shell works without a second window.
                var mapped = Features.Chat.GlobalChatBridge.ToMessage(One("m-map", 1700000000, "hello"));
                bool bridged = mapped.Channel == Features.Chat.ChatChannels.Global
                            && mapped.FromUsername == "KNAPPE0" && mapped.Body == "hello"
                            && mapped.SenderVerified;
                r.Add(("global chat: a message becomes an ordinary chat line on the Global channel", bridged,
                       mapped.Channel));

                long idA = Features.Chat.GlobalChatBridge.IdFor(One("aaaa", 1700000000));
                long idB = Features.Chat.GlobalChatBridge.IdFor(One("bbbb", 1700000000));
                long idLater = Features.Chat.GlobalChatBridge.IdFor(One("cccc", 1700000001));
                r.Add(("global chat: ids are ordered by time and distinct within a second",
                       idA != idB && idLater > idA && idLater > idB, $"{idA} {idB} {idLater}"));

                r.Add(("global chat: the same message always maps to the same id",
                       Features.Chat.GlobalChatBridge.IdFor(One("aaaa", 1700000000)) == idA, ""));

                // A main-menu sender has no origin server and must still render.
                var noServer = Features.Chat.GlobalChatBridge.ToMessage(One("m-menu", 1700000000));
                r.Add(("global chat: a message from the main menu carries no server tag",
                       noServer.Origin == "global", noServer.Origin));

                // A name a server relayed for a Discord member proves nothing, so it never gets the verified-account mark.
                var relayed = One("m-relay", 1700000000, "hi");
                relayed.RelaySource = "discord";
                relayed.OriginServerName = "Vanilla";
                var relayedMsg = Features.Chat.GlobalChatBridge.ToMessage(relayed);

                var inGame = One("m-ingame", 1700000000, "hi");
                inGame.RelaySource = "ingame";
                inGame.OriginServerName = "Vanilla";
                var inGameMsg = Features.Chat.GlobalChatBridge.ToMessage(inGame);

                var own = One("m-own", 1700000000, "hi");
                own.OriginServerName = "Vanilla";
                var ownMsg = Features.Chat.GlobalChatBridge.ToMessage(own);

                r.Add(("global chat: a relayed name is never shown as a verified account",
                       !relayedMsg.SenderVerified && !inGameMsg.SenderVerified && ownMsg.SenderVerified, ""));

                string relayLine = Features.Chat.ChatPanel.Line(relayedMsg);
                r.Add(("global chat: a Discord relay is marked as one and names the server that relayed it, and an unlinked player keeps their server tag",
                       relayedMsg.Origin == "discord" && relayedMsg.UnverifiedSender && relayedMsg.RelayedBy == "Vanilla" && relayLine.Contains("· Vanilla")
                    && inGameMsg.Origin == "Vanilla" && ownMsg.Origin == "Vanilla" && !ownMsg.UnverifiedSender,
                       relayedMsg.Origin + "/" + inGameMsg.Origin + " | " + relayLine));

                // A hidden tab reads as "not built yet", so it hides only once Global says it has no chat.
                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Unknown, true, false);
                bool offeredWhileProbing = Features.Global.GlobalChatClient.Offered;
                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Offline, true, false);
                bool offeredWhenDown = Features.Global.GlobalChatClient.Offered;
                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Online, true, false);
                bool hiddenWhenRefused = !Features.Global.GlobalChatClient.Offered;
                r.Add(("global chat: the tab is offered until Global actually says it has no chat",
                       offeredWhileProbing && offeredWhenDown && hiddenWhenRefused, ""));

                // The draft is only cleared on a real send, so every blocked state must name itself.
                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Offline, true, true);
                // The fixture decides linked or not; with the real store, a linked machine posts to live Global Chat.
                {
                    string seamPrev = Features.Account.KmhAccountStore.RootOverrideForTest;
                    string seamRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                        "kmh-linkseam-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    System.IO.Directory.CreateDirectory(seamRoot);
                    Features.Account.KmhAccountStore.RootOverrideForTest = seamRoot;
                    Features.Account.KmhAccountStore.ResetForTest();
                    Features.Account.KmhAccountStore.Save(Features.Global.GlobalHttp.BaseUrl, "seam-token",
                                                          new string('a', 32), "Seam");
                    bool linkedWhenStored = Features.Global.GlobalChatClient.Linked;
                    // The credential must land under the fixture's root, or "unlinked" only means this machine has none.
                    bool wroteWhereTold = System.IO.Directory.GetFiles(seamRoot, "*.json").Length > 0;

                    Features.Account.KmhAccountStore.RootOverrideForTest = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "kmh-linkseam-empty-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Features.Account.KmhAccountStore.ResetForTest();
                    bool unlinkedWhenEmpty = !Features.Global.GlobalChatClient.Linked;

                    // The REASON is a separate read of the same store, so it must sit inside the fixture too.
                    Features.Account.KmhGlobalCapabilities.SeedForTest(
                        Features.Account.KmhGlobalCapabilities.ProbeState.Online, true, true);
                    bool reasonWhenEmpty = Features.Global.GlobalChatClient.WhyCannotSend().Length > 0;
                    Features.Account.KmhAccountStore.RootOverrideForTest = seamRoot;
                    Features.Account.KmhAccountStore.ResetForTest();
                    bool noReasonWhenStored = Features.Global.GlobalChatClient.WhyCannotSend().Length == 0;

                    Features.Account.KmhAccountStore.RootOverrideForTest = seamPrev;
                    Features.Account.KmhAccountStore.ResetForTest();
                    try { System.IO.Directory.Delete(seamRoot, true); } catch { }

                    r.Add(("global chat: linked/unlinked is decided by the fixture, not by this machine",
                           linkedWhenStored && unlinkedWhenEmpty && wroteWhereTold
                        && reasonWhenEmpty && noReasonWhenStored,
                           wroteWhereTold ? "" : "THE UNLINKED CHECKS PASS WHETHER OR NOT THE CODE WORKS"));
                }

                // The empty account root spans the whole block; around the send only, the reason comes from this machine.
                string previousRoot = Features.Account.KmhAccountStore.RootOverrideForTest;
                Features.Account.KmhAccountStore.RootOverrideForTest = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "kmh-unlinked-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Features.Account.KmhAccountStore.ResetForTest();

                bool saysDown, saysUnsupported, saysUnlinked, unlinked, refuses;
                try
                {
                    // Seeded here: inherited, "says down" would read an online directory and pass on the unlinked reason.
                    Features.Account.KmhGlobalCapabilities.SeedForTest(
                        Features.Account.KmhGlobalCapabilities.ProbeState.Offline, true, true);
                    saysDown = Features.Global.GlobalChatClient.WhyCannotSend().Length > 0;
                    Features.Account.KmhGlobalCapabilities.SeedForTest(
                        Features.Account.KmhGlobalCapabilities.ProbeState.Online, true, false);
                    saysUnsupported = Features.Global.GlobalChatClient.WhyCannotSend().Length > 0;
                    Features.Account.KmhGlobalCapabilities.SeedForTest(
                        Features.Account.KmhGlobalCapabilities.ProbeState.Online, true, true);
                    saysUnlinked = Features.Global.GlobalChatClient.WhyCannotSend().Length > 0;
                    unlinked = !Features.Global.GlobalChatClient.Linked;
                    refuses = !Features.Global.GlobalChatClient.Send("hello");
                }
                finally
                {
                    Features.Account.KmhAccountStore.RootOverrideForTest = previousRoot;
                    Features.Account.KmhAccountStore.ResetForTest();
                }

                // Named, so a failure says which term failed instead of an empty detail.
                string missing = (saysDown ? "" : " down")
                               + (saysUnsupported ? "" : " unsupported")
                               + (saysUnlinked ? "" : " unlinked-reason")
                               + (unlinked ? "" : " unlinked-state")
                               + (refuses ? "" : " refusal");
                r.Add(("global chat: a send that cannot go out is refused with a reason, never silently dropped",
                       missing.Length == 0, missing.Length == 0 ? "" : "NOT REFUSED FOR:" + missing));

                // One unreachable moment must not disable Global for the rest of the launch.
                var caps = Features.Account.KmhGlobalCapabilities.ProbeState.Offline;
                DateTime t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                bool notYet = !Features.Account.KmhGlobalCapabilities.ShouldProbe(caps, true, t0, t0.AddSeconds(5));
                bool retries = Features.Account.KmhGlobalCapabilities.ShouldProbe(
                    caps, true, t0, t0 + Features.Account.KmhGlobalCapabilities.RetryAfterOffline);
                bool onlineStays = !Features.Account.KmhGlobalCapabilities.ShouldProbe(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Online, true, t0, t0.AddDays(1));
                bool newDirectory = Features.Account.KmhGlobalCapabilities.ShouldProbe(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Online, false, t0, t0.AddSeconds(1));
                r.Add(("global chat: an offline directory is re-probed instead of disabling Global for the launch",
                       notYet && retries && onlineStays && newDirectory, ""));
            }
            catch (Exception ex) { r.Add(("global chat: the transport rules run outside the game", false, ex.Message)); }
            finally
            {
                Features.Global.GlobalChatClient.ResetForTest();
                Features.Account.KmhGlobalCapabilities.ResetForTest();
            }
        }

        // An enforced server sits on "Checking..." for ever if only the details pane asks for its profile.
        private static void BrowserFreshness(List<(string, bool, string)> r)
        {
            Features.Global.ModProfileClient.ResetForTest();
            try
            {
                Features.Global.ServerCardDto enforced = new Features.Global.ServerCardDto
                { ServerId = "srv-enforced", ModPolicy = Features.Global.ModPolicyNames.Enforced, ManifestRevision = 1 };

                // Before anything asks, the row cannot state a verdict, so it reads "Checking...".
                r.Add(("browser: an enforced row with no profile yet reads as checking",
                       Features.Global.KmhCompatibility.Evaluate(new Features.Global.ServerCardDto
                       {
                           ServerId = "srv-enforced", ModPolicy = Features.Global.ModPolicyNames.Enforced,
                           Status = "Online", JoinEndpoint = "host:25555", MaxPlayers = 8,
                       }) == Features.Global.CompatState.Checking, ""));

                Features.Global.ModProfileClient.EnsureFetchedForList(enforced);
                r.Add(("browser: the list asks for the profile its own verdict needs",
                       Features.Global.ModProfileClient.Peek("srv-enforced") != null,
                       $"list fetches={Features.Global.ModProfileClient.ListFetchesForTest}"));

                // A long directory must not turn into one call per row at once.
                for (int i = 0; i < Features.Global.ModProfileClient.MaxListFetches + 20; i++)
                    Features.Global.ModProfileClient.EnsureFetchedForList(new Features.Global.ServerCardDto
                    { ServerId = "bulk-" + i, ModPolicy = Features.Global.ModPolicyNames.Enforced });
                r.Add(("browser: list profile fetches are bounded",
                       Features.Global.ModProfileClient.ListFetchesForTest <= Features.Global.ModProfileClient.MaxListFetches,
                       $"{Features.Global.ModProfileClient.ListFetchesForTest} <= {Features.Global.ModProfileClient.MaxListFetches}"));

                // A failed fetch left failed for its revision blocks Join for good; it retries once its back-off passes.
                var failedCard = new Features.Global.ServerCardDto { ServerId = "srv-failed", ModPolicy = Features.Global.ModPolicyNames.Enforced, ManifestRevision = 3 };
                var waiting = new Features.Global.ModProfileClient.Entry { Revision = 3, Failures = 1, RetryAtUtc = DateTime.UtcNow.AddMinutes(5), ErrorCode = "KMH-NET-001" };
                Features.Global.ModProfileClient.SeedForTest("srv-failed", waiting);
                Features.Global.ModProfileClient.EnsureFetched(failedCard);
                bool heldOff = ReferenceEquals(Features.Global.ModProfileClient.Peek("srv-failed"), waiting);
                var due = new Features.Global.ModProfileClient.Entry { Revision = 3, Failures = 2, RetryAtUtc = DateTime.UtcNow.AddSeconds(-1), ErrorCode = "KMH-NET-001" };
                Features.Global.ModProfileClient.SeedForTest("srv-failed", due);
                Features.Global.ModProfileClient.EnsureFetched(failedCard);
                var retried = Features.Global.ModProfileClient.Peek("srv-failed");
                bool retry = heldOff && !ReferenceEquals(retried, due) && retried.Failures >= 2
                          && Features.Global.ModProfileClient.RetryDelay(1) < Features.Global.ModProfileClient.RetryDelay(5)
                          && Features.Global.ModProfileClient.RetryDelay(99) <= TimeSpan.FromMinutes(5);
                r.Add(("browser: a failed mod-list fetch is retried after its back-off, never left failed for that revision", retry,
                       $"heldOff={heldOff} retried={!ReferenceEquals(retried, due)} failures={retried?.Failures}"));

                int before = Features.Global.ModProfileClient.ListFetchesForTest;
                Features.Global.ModProfileClient.EnsureFetchedForList(new Features.Global.ServerCardDto
                { ServerId = "srv-open", ModPolicy = Features.Global.ModPolicyNames.AllowAll });
                r.Add(("browser: an AllowAll server costs no profile call",
                       Features.Global.ModProfileClient.ListFetchesForTest == before, ""));
            }
            finally { Features.Global.ModProfileClient.ResetForTest(); }

            DateTime now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
            r.Add(("browser: the listing refreshes on its own while the window is open",
                   Features.Global.KmhDirectoryClient.AutoRefreshEvery >= TimeSpan.FromSeconds(10)
                   && Features.Global.KmhDirectoryClient.AutoRefreshEvery <= TimeSpan.FromMinutes(2),
                   $"{(int)Features.Global.KmhDirectoryClient.AutoRefreshEvery.TotalSeconds}s"));
        }

        private static void RwtVersionResolution(List<(string, bool, string)> r)
        {
            RwtVersionIsNotADisplayName(r);
            BrowserFreshness(r);
            GlobalChatTransport(r);

            try
            {
                List<Features.Rwt.RwtRelease> parsed = Features.Rwt.RwtReleaseCatalog.Parse(RwtReleasesFixture);
                Features.Rwt.RwtReleaseCatalog.SeedForTest(parsed);

                bool noDraft = !parsed.Exists(x => x.Version == "99.9.9.9");
                r.Add(("rwt catalog: a draft release is never installable", noDraft,
                       noDraft ? $"{parsed.Count} release(s) parsed" : "DRAFT OFFERED"));

                bool decorated = parsed.Exists(x => x.Version == "26.8.16.1");
                r.Add(("rwt catalog: a re-cut tag such as 26.8.16.1_(1) normalises to its version", decorated, ""));

                Features.Rwt.RwtRelease latest = Features.Rwt.RwtReleaseCatalog.LatestStable;
                bool latestOk = latest != null && latest.Version == "26.8.31.1";
                r.Add(("rwt catalog: latest stable skips prereleases and is ordered numerically", latestOk,
                       latest?.Version ?? "(none)"));

                Features.Rwt.RwtInstallPlan legacy =
                    Features.Rwt.RwtResolver.PlanClient(Rv("26.6.9.1"), Rv("26.8.31.1"));
                bool exact = legacy.CanInstall && legacy.Release.Version == "26.6.9.1"
                          && legacy.Asset.Role == Features.Rwt.RwtAssetRole.Client;
                r.Add(("rwt resolve: a legacy server resolves its own release, never the latest", exact,
                       legacy.CanInstall ? legacy.Explain() : legacy.State.ToString()));

                Features.Rwt.RwtInstallPlan current =
                    Features.Rwt.RwtResolver.PlanClient(Rv("Release 26.8.31.1"), Rv("26.6.9.1"));
                bool currentOk = current.CanInstall && current.Release.Version == "26.8.31.1"
                              && current.Asset.Name == "3005289691.zip";
                r.Add(("rwt resolve: a current server resolves 26.8.31.1 and the official client package", currentOk,
                       current.CanInstall ? current.Explain() : current.State.ToString()));

                Features.Rwt.RwtInstallPlan same = Features.Rwt.RwtResolver.PlanClient(Rv("26.8.31.1"), Rv("26.8.31.1"));
                r.Add(("rwt resolve: the version already running is not offered as a download",
                       same.State == Features.Rwt.RwtPlanState.AlreadyInstalled && !same.PendingRestart, ""));

                // Activation waits for a restart, so the loaded version stays stale; a click must not re-download it.
                try
                {
                    Features.Rwt.RwtInstaller.SeedActivatedForTest("26.8.31.1");
                    Features.Rwt.RwtInstallPlan staged =
                        Features.Rwt.RwtResolver.PlanClient(Rv("26.8.31.1"), Rv("26.6.9.1"));
                    bool idempotent = staged.State == Features.Rwt.RwtPlanState.AlreadyInstalled
                                   && !staged.CanInstall && staged.PendingRestart
                                   && staged.Explain().IndexOf("restart", StringComparison.OrdinalIgnoreCase) >= 0;
                    r.Add(("rwt resolve: a release already activated is not downloaded again before the restart",
                           idempotent, staged.Explain()));

                    Features.Rwt.RwtInstallPlan other =
                        Features.Rwt.RwtResolver.PlanClient(Rv("26.6.9.1"), Rv("26.6.9.1"));
                    r.Add(("rwt resolve: staging one version does not block resolving another",
                           other.State == Features.Rwt.RwtPlanState.AlreadyInstalled && !other.PendingRestart, ""));
                }
                finally { Features.Rwt.RwtInstaller.SeedActivatedForTest(""); }

                Features.Rwt.RwtInstallPlan missing = Features.Rwt.RwtResolver.PlanClient(Rv("1.2.3.4"), Rv("26.8.31.1"));
                r.Add(("rwt resolve: an unpublished version is refused rather than substituted",
                       missing.State == Features.Rwt.RwtPlanState.VersionUnknown
                       && missing.Explain().Contains("1.2.3.4"), missing.Explain()));

                Features.Rwt.RwtInstallPlan none = Features.Rwt.RwtResolver.PlanClient(Rv(""), Rv("26.8.31.1"));
                r.Add(("rwt resolve: a server that published no version produces no download",
                       none.State == Features.Rwt.RwtPlanState.NoVersionRequested, ""));

                Features.Rwt.RwtInstallPlan linux = Features.Rwt.RwtResolver.PlanServer(
                    "26.8.31.1", Features.Rwt.RwtOs.Linux, Features.Rwt.RwtArch.X64);
                bool linuxOk = linux.CanInstall && linux.Asset.Name == "Server-linux-x64.zip";
                r.Add(("rwt resolve: a linux-x64 server gets the linux server package, not a windows one", linuxOk,
                       linux.CanInstall ? linux.Asset.Name : linux.State.ToString()));

                Features.Rwt.RwtInstallPlan win = Features.Rwt.RwtResolver.PlanServer(
                    "26.8.31.1", Features.Rwt.RwtOs.Windows, Features.Rwt.RwtArch.X64);
                r.Add(("rwt resolve: a windows server gets the windows server package",
                       win.CanInstall && win.Asset.Name == "Server-win-x64.zip",
                       win.CanInstall ? win.Asset.Name : win.State.ToString()));

                bool clientIsNotServer = current.Asset.Role == Features.Rwt.RwtAssetRole.Client
                                      && linux.Asset.Role == Features.Rwt.RwtAssetRole.Server;
                r.Add(("rwt resolve: client and server packages are never interchanged", clientIsNotServer, ""));

                Features.Rwt.RwtReleaseCatalog.ResetForTest();
                Features.Rwt.RwtInstallPlan offline = Features.Rwt.RwtResolver.PlanClient(Rv("26.8.31.1"), Rv("26.6.9.1"));
                r.Add(("rwt resolve: with no catalog it reports unavailable rather than inventing a version",
                       offline.State == Features.Rwt.RwtPlanState.CatalogUnavailable, offline.Explain()));

                string digest = Features.Rwt.RwtReleaseCatalog.ReadDigest(
                    "sha256:B54BA9EF9B5A136DBB54BA9EF9B5A136DBB54BA9EF9B5A136DBB54BA9EF9B5A1");
                r.Add(("rwt catalog: a published sha256 digest is read and lower-cased",
                       digest.Length == 64 && digest.StartsWith("b54ba9ef"), digest.Substring(0, Math.Min(12, digest.Length))));
                r.Add(("rwt catalog: a malformed digest is discarded rather than trusted",
                       Features.Rwt.RwtReleaseCatalog.ReadDigest("sha256:nope").Length == 0, ""));

                RwtActivation(r);
                RwtCopiesRules(r);
            }
            catch (Exception ex) { r.Add(("rwt resolve: the resolver runs outside the game", false, ex.Message)); }
            finally { Features.Rwt.RwtReleaseCatalog.ResetForTest(); }
        }

        private static byte[] FakeRwtAssembly(params string[] texts)
        {
            // Odd offset on purpose: metadata strings are not aligned.
            var bytes = new List<byte> { 0x4D, 0x5A, 0x90 };
            foreach (string t in texts) { bytes.AddRange(System.Text.Encoding.Unicode.GetBytes(t)); bytes.Add(0x01); bytes.Add(0x07); bytes.Add(0x00); }
            return bytes.ToArray();
        }

        private static string FakeRwtCopy(string dir, string version, bool kmhMarker)
        {
            string asm = System.IO.Path.Combine(dir, "1.6", "Assemblies");
            System.IO.Directory.CreateDirectory(asm);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "About"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "About", "About.xml"), "<ModMetaData/>");
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(asm, "RTShared.dll"), FakeRwtAssembly("1.0.0.0", "Release " + version, "127.0.0.1"));
            if (kmhMarker) Features.Rwt.RwtCopies.WriteMarker(dir, version, true);
            return dir;
        }

        // KMH's local RWT copy outranks the Workshop one in every list; going back must reuse a copy, not re-download it.
        private static void RwtCopiesRules(List<(string, bool, string)> r)
        {
            string v = Features.Rwt.RwtCopies.VersionInAssembly(FakeRwtAssembly("1.0.0.0", "26.8.31.1", "127.0.0.1"));
            string two = Features.Rwt.RwtCopies.VersionInAssembly(FakeRwtAssembly("26.8.31.1", "26.6.9.1"));
            string none = Features.Rwt.RwtCopies.VersionInAssembly(FakeRwtAssembly("1.0.0.0", "26.8.31.10.2"));
            r.Add(("rwt copies: a copy's version is read from its shared assembly, and only when it names exactly one",
                   v == "26.8.31.1" && two == "" && none == "", $"one='{v}' two='{two}' none='{none}'"));

            var K = Features.Rwt.RwtSwitchKind.Download;
            bool table = Features.Rwt.RwtCopies.Decide("26.8.31.1", true, "26.6.9.1", "26.8.31.1", false) == Features.Rwt.RwtSwitchKind.UseWorkshop
                      && Features.Rwt.RwtCopies.Decide("26.6.9.1", true, "26.8.31.1", "26.8.31.1", true) == Features.Rwt.RwtSwitchKind.UsePrevious
                      && Features.Rwt.RwtCopies.Decide("26.6.9.1", true, "26.6.9.1", "26.8.31.1", false) == Features.Rwt.RwtSwitchKind.RestartOnly
                      && Features.Rwt.RwtCopies.Decide("26.8.31.1", false, "", "26.8.31.1", false) == Features.Rwt.RwtSwitchKind.RestartOnly
                      && Features.Rwt.RwtCopies.Decide("26.6.9.1", false, "", "26.8.31.1", false) == K
                      && Features.Rwt.RwtCopies.Decide("26.6.9.1", true, "", "", false) == K
                      && Features.Rwt.RwtCopies.Decide("", true, "", "", true) == K;
            r.Add(("rwt copies: a version already on this PC is switched to, and only a missing one is downloaded", table, table ? "" : "WRONG CHOICE"));

            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmh-rwtc-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string game = System.IO.Path.Combine(root, "RimWorld");
                string mods = System.IO.Path.Combine(game, "Mods");
                string kmh = System.IO.Path.Combine(root, Persistence.KmhDataFolder.Name);
                string workshop = System.IO.Path.Combine(root, "workshop", Features.Rwt.RwtAssetClassifier.ClientWorkshopId);
                System.IO.Directory.CreateDirectory(mods);
                System.IO.Directory.CreateDirectory(kmh);
                Features.Rwt.RwtInstaller.ModsRootOverrideForTest = mods;
                Features.Rwt.RwtPaths.RootOverrideForTest = kmh;
                Features.Rwt.RwtCopies.GameVersionFolderForTest = "1.6";
                Features.Rwt.RwtCopies.WorkshopCopyOverrideForTest = FakeRwtCopy(workshop, "26.8.31.1", false);
                string local = Features.Rwt.RwtCopies.LocalCopy;
                bool Marked(string version) => Features.Rwt.RwtCopies.MarkedVersion(local) == version;

                Features.Rwt.RwtInstaller.ForceCopyForTest = true;
                string payload = FakeRwtCopy(System.IO.Path.Combine(root, "elsewhere", "RimworldTogether"), "26.6.9.1", false);
                bool installed = Features.Rwt.RwtInstaller.Activate(payload, "26.6.9.1", true, out string why1);
                bool noLeftovers = System.IO.Directory.GetDirectories(game, ".kmh-rwt-incoming-*").Length == 0;
                // A copy leaves its source behind; a rename would have taken it.
                bool copied = System.IO.Directory.Exists(payload);
                r.Add(("rwt copies: a package unpacked on another drive is copied in, marked with its version, and leaves nothing half-done",
                       installed && copied && Marked("26.6.9.1") && noLeftovers, $"{why1} copied={copied} marked='{Features.Rwt.RwtCopies.MarkedVersion(local)}' leftovers={!noLeftovers}"));
                Features.Rwt.RwtInstaller.ForceCopyForTest = false;

                var backToWorkshop = Features.Rwt.RwtCopies.DecideOnDisk("26.8.31.1", out _);
                bool parked = backToWorkshop == Features.Rwt.RwtSwitchKind.UseWorkshop && Features.Rwt.RwtCopies.UseWorkshop(out string why2)
                           && !System.IO.Directory.Exists(local);
                r.Add(("rwt copies: a server on the Workshop version sets KMH's copy aside instead of downloading it again", parked,
                       $"{backToWorkshop} {(System.IO.Directory.Exists(local) ? "copy still in Mods" : "")}"));

                var again = Features.Rwt.RwtCopies.DecideOnDisk("26.6.9.1", out string previous);
                bool reused = again == Features.Rwt.RwtSwitchKind.UsePrevious && Features.Rwt.RwtCopies.UsePrevious(previous, out string why3) && Marked("26.6.9.1");
                r.Add(("rwt copies: going back to the other server reuses the copy set aside, with no download", reused, $"{again}"));

                bool restored = Features.Rwt.RwtCopies.RestoreOriginal(out bool changed, out string why4) && changed
                             && !System.IO.Directory.Exists(local) && !Features.Rwt.RwtCopies.HasOriginal;
                r.Add(("rwt copies: restoring takes KMH's copy out of Mods, so the Workshop copy loads in the player's own list", restored, why4));

                FakeRwtCopy(local, "26.8.16.1", false);
                System.IO.File.WriteAllText(System.IO.Path.Combine(local, "players-own.txt"), "mine");
                bool all = true;
                foreach (string ver in new[] { "26.6.9.1", "26.6.23.1", "26.7.25.1", "26.5.24.1" })
                    all &= Features.Rwt.RwtInstaller.Activate(FakeRwtCopy(System.IO.Path.Combine(root, "dl-" + ver, "Rwt"), ver, false), ver, true, out _);
                string why5 = "";
                bool back = all && Features.Rwt.RwtCopies.RestoreOriginal(out bool changed2, out why5) && changed2
                         && System.IO.File.Exists(System.IO.Path.Combine(local, "players-own.txt")) && !Features.Rwt.RwtCopies.IsKmhCopy(local);
                r.Add(("rwt copies: the player's own copy survives four installs and comes back on restore", back,
                       back ? "" : $"installs={all} own={System.IO.File.Exists(System.IO.Path.Combine(local, "players-own.txt"))} {why5}"));
            }
            catch (Exception ex) { r.Add(("rwt copies: the switching paths run outside the game", false, ex.Message)); }
            finally
            {
                Features.Rwt.RwtInstaller.ModsRootOverrideForTest = null;
                Features.Rwt.RwtPaths.RootOverrideForTest = null;
                Features.Rwt.RwtCopies.WorkshopCopyOverrideForTest = null;
                Features.Rwt.RwtCopies.GameVersionFolderForTest = null;
                Features.Rwt.RwtInstaller.ResetForTest();
                try { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        private static void RwtActivation(List<(string, bool, string)> r)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "kmh-rwt-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string mods = System.IO.Path.Combine(root, "Mods");
                string kmh  = System.IO.Path.Combine(root, Persistence.KmhDataFolder.Name);
                System.IO.Directory.CreateDirectory(mods);
                System.IO.Directory.CreateDirectory(kmh);
                Features.Rwt.RwtInstaller.ModsRootOverrideForTest = mods;
                Features.Rwt.RwtPaths.RootOverrideForTest = kmh;
                Features.Rwt.RwtReleaseCatalog.RootOverrideForTest = kmh;

                string target = System.IO.Path.Combine(mods, Features.Rwt.RwtAssetClassifier.ClientWorkshopId);
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(target, "About"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(target, "About", "About.xml"), "<ModMetaData/>");
                System.IO.File.WriteAllText(System.IO.Path.Combine(target, "marker-old.txt"), "old");

                string staging = System.IO.Path.Combine(root, "staging");
                string payload = System.IO.Path.Combine(staging, "RimworldTogether");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(payload, "About"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(payload, "About", "About.xml"), "<ModMetaData/>");
                System.IO.File.WriteAllText(System.IO.Path.Combine(payload, "marker-new.txt"), "new");

                string found = Features.Rwt.RwtInstaller.FindModRoot(staging);
                r.Add(("rwt install: the mod folder is found by its About, not by a guessed name",
                       found == payload, found ?? "(not found)"));

                bool activated = Features.Rwt.RwtInstaller.Activate(found, "26.8.16.1", true, out string why);
                bool nowNew = System.IO.File.Exists(System.IO.Path.Combine(target, "marker-new.txt"));
                r.Add(("rwt install: activation replaces the managed copy", activated && nowNew, why));

                string[] kept = System.IO.Directory.Exists(Features.Rwt.RwtInstaller.PreviousRoot)
                    ? System.IO.Directory.GetDirectories(Features.Rwt.RwtInstaller.PreviousRoot) : new string[0];
                bool preserved = kept.Length == 1
                              && System.IO.File.Exists(System.IO.Path.Combine(kept[0], "marker-old.txt"));
                r.Add(("rwt install: the previous copy is kept aside, never deleted", preserved,
                       preserved ? System.IO.Path.GetFileName(kept[0]) : "PREVIOUS COPY LOST"));

                // RimWorld keeps the first-listed of two copies sharing a packageId; a parked copy can sort first and load.
                int withAbout = 0;
                foreach (string d in System.IO.Directory.GetDirectories(mods))
                    if (System.IO.File.Exists(System.IO.Path.Combine(d, "About", "About.xml"))) withAbout++;
                string previousFull = System.IO.Path.GetFullPath(Features.Rwt.RwtInstaller.PreviousRoot);
                bool outside = withAbout == 1
                            && !previousFull.StartsWith(System.IO.Path.GetFullPath(mods), StringComparison.OrdinalIgnoreCase);
                r.Add(("rwt install: the previous copy is kept outside the Mods folder RimWorld loads from", outside,
                       outside ? previousFull : $"{withAbout} mod folder(s) in Mods"));

                for (int i = 0; i < 3; i++)
                {
                    string legacy = System.IO.Path.Combine(mods,
                        Features.Rwt.RwtAssetClassifier.ClientWorkshopId + ".kmh-previous-2026010100000" + i);
                    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(legacy, "About"));
                    System.IO.File.WriteAllText(System.IO.Path.Combine(legacy, "About", "About.xml"), "<ModMetaData/>");
                }
                string payload2 = System.IO.Path.Combine(root, "staging2", "RimworldTogether");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(payload2, "About"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(payload2, "About", "About.xml"), "<ModMetaData/>");
                System.IO.File.WriteAllText(System.IO.Path.Combine(payload2, "marker-newer.txt"), "newer");
                bool again = Features.Rwt.RwtInstaller.Activate(payload2, "26.8.31.1", true, out string why2);
                int left = 0;
                foreach (string d in System.IO.Directory.GetDirectories(mods))
                    if (System.IO.File.Exists(System.IO.Path.Combine(d, "About", "About.xml"))) left++;
                string[] held = System.IO.Directory.GetDirectories(Features.Rwt.RwtInstaller.PreviousRoot);
                bool newestKept = false, ownKept = false;
                int parked = 0;
                foreach (string h in held)
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(h, "marker-new.txt"))) newestKept = true;
                    if (System.IO.File.Exists(System.IO.Path.Combine(h, "marker-old.txt"))) ownKept = true;
                    if (System.IO.Path.GetFileName(h).IndexOf(".kmh-previous-", StringComparison.Ordinal) >= 0) parked++;
                }
                // The player's original and any parked copy without Colony Nexus's marker may be the player's own: kept.
                bool tidied = again && left == 1 && parked == 3 && held.Length == 5 && newestKept && ownKept
                           && System.IO.File.Exists(System.IO.Path.Combine(target, "marker-newer.txt"));
                r.Add(("rwt install: copies parked in Mods by older builds move out of Mods and, unmarked, are never deleted",
                       tidied, tidied ? $"{held.Length} held" : $"{why2} mods={left} held={held.Length} parked={parked} newest={newestKept} own={ownKept}"));

                // RWT's own updater leaves an unmarked copy in Mods: later installs set it aside but never delete it.
                System.IO.Directory.Delete(target, true);
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(target, "About"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(target, "About", "About.xml"), "<ModMetaData/>");
                System.IO.File.WriteAllText(System.IO.Path.Combine(target, "marker-rwt-updater.txt"), "rwt");
                // Its date is whatever the download carried; the oldest makes it the first copy a tidy-up would pick.
                System.IO.Directory.SetCreationTimeUtc(target, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                bool installed = true;
                for (int i = 0; i < 3; i++)
                {
                    string next = System.IO.Path.Combine(root, "staging-u" + i, "RimworldTogether");
                    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(next, "About"));
                    System.IO.File.WriteAllText(System.IO.Path.Combine(next, "About", "About.xml"), "<ModMetaData/>");
                    installed &= Features.Rwt.RwtInstaller.Activate(next, "26.10.4." + (i + 1), true, out _);
                }
                bool updaterKept = false, ownStill = false;
                foreach (string h in System.IO.Directory.GetDirectories(Features.Rwt.RwtInstaller.PreviousRoot))
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(h, "marker-rwt-updater.txt"))) updaterKept = true;
                    if (System.IO.File.Exists(System.IO.Path.Combine(h, "marker-old.txt"))) ownStill = true;
                }
                r.Add(("rwt install: a copy KMH did not install is never deleted, however many installs follow",
                       installed && updaterKept && ownStill, $"installed={installed} updater copy kept={updaterKept} own kept={ownStill}"));

                // Steam owns the Workshop tree, so nothing here may reach outside the managed Mods folder.
                bool scoped = target.StartsWith(mods, StringComparison.Ordinal)
                           && !target.Contains("workshop", StringComparison.OrdinalIgnoreCase);
                r.Add(("rwt install: only the KMH-managed Mods copy is touched", scoped, ""));

                string zip = System.IO.Path.Combine(root, "probe.bin");
                System.IO.File.WriteAllText(zip, "hello");
                r.Add(("rwt install: a wrong checksum refuses the package",
                       !Features.Rwt.RwtInstaller.DigestMatches(zip, new string('a', 64)), ""));
                // Older official releases carry no digest: installable only on the player's yes, and never as verified.
                var unpublished = new Features.Rwt.RwtInstallPlan
                {
                    State = Features.Rwt.RwtPlanState.Ready,
                    Release = new Features.Rwt.RwtRelease { Version = "26.6.9.1" },
                    Asset = new Features.Rwt.RwtAsset { Name = "3005289691.zip", Sha256 = "",
                        Url = "https://github.com/RimWorld-Together/Rimworld-Together/releases/download/26.6.9.1/3005289691.zip" },
                };
                r.Add(("rwt install: a package published without a checksum needs the player's yes and is never verified",
                       !Features.Rwt.RwtInstaller.DigestMatches(zip, "")
                       && Features.Rwt.RwtInstaller.Refusal(unpublished, false) != null
                       && Features.Rwt.RwtInstaller.Refusal(unpublished, true) == null, ""));
            }
            catch (Exception ex) { r.Add(("rwt install: the activation paths run outside the game", false, ex.Message)); }
            finally
            {
                Features.Rwt.RwtInstaller.ModsRootOverrideForTest = null;
                Features.Rwt.RwtPaths.RootOverrideForTest = null;
                Features.Rwt.RwtReleaseCatalog.RootOverrideForTest = null;
                Features.Rwt.RwtInstaller.ResetForTest();
                try { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        // The prompt is the one piece of KMH that interrupts the main menu, so every reason it stays quiet matters.
        private static void AccountPromptRules(List<(string, bool, string)> r)
        {
            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "kmh-prompt-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string previousBase = Features.Global.GlobalHttp.BaseUrl;
            try
            {
                Features.Account.KmhAccountStore.RootOverrideForTest = scratch;
                Features.Account.KmhAccountStore.ResetForTest();
                Features.Global.GlobalHttp.BaseUrl = "https://directory.example";

                Features.Account.KmhAccountPrompt.ResetForTest();
                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Offline, playerLinking: false);
                r.Add(("account prompt: never offered while the directory is unreachable",
                       !Features.Account.KmhAccountPrompt.ShouldOffer(), ""));
                // That ask starts a real probe whose answer lands when it lands; under load it can land on a later check.
                for (int i = 0; i < 300 && Features.Account.KmhGlobalCapabilities.State
                                           == Features.Account.KmhGlobalCapabilities.ProbeState.Probing; i++)
                    System.Threading.Thread.Sleep(10);

                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Online, playerLinking: false);
                r.Add(("account prompt: never offered by a directory without player linking",
                       !Features.Account.KmhAccountPrompt.ShouldOffer(), ""));

                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Online, playerLinking: true);
                r.Add(("account prompt: offered when the directory supports linking and nobody is linked",
                       Features.Account.KmhAccountPrompt.ShouldOffer(), ""));

                Features.Account.KmhAccountPrompt.SuppressForSession();
                r.Add(("account prompt: 'Not now' silences it for the rest of the session",
                       !Features.Account.KmhAccountPrompt.ShouldOffer(), ""));

                Features.Account.KmhAccountPrompt.ResetForTest();
                Features.Account.KmhAccountStore.Save("https://directory.example", "tok", "pgid", "Someone");
                r.Add(("account prompt: never offered once the player is linked",
                       !Features.Account.KmhAccountPrompt.ShouldOffer(), ""));

                // A credential for another directory is not this one's, so the prompt must still appear.
                Features.Global.GlobalHttp.BaseUrl = "https://other.example";
                Features.Account.KmhGlobalCapabilities.SeedForTest(
                    Features.Account.KmhGlobalCapabilities.ProbeState.Online, playerLinking: true);
                r.Add(("account prompt: a link to a different directory does not count as linked here",
                       Features.Account.KmhAccountPrompt.ShouldOffer(), ""));
            }
            catch (Exception ex) { r.Add(("account prompt: the prompt rules run outside the game", false, ex.Message)); }
            finally
            {
                Features.Global.GlobalHttp.BaseUrl = previousBase;
                Features.Account.KmhAccountPrompt.ResetForTest();
                Features.Account.KmhGlobalCapabilities.ResetForTest();
                Features.Account.KmhAccountStore.RootOverrideForTest = null;
                Features.Account.KmhAccountStore.ResetForTest();
                try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, true); } catch (Exception) { }
            }
        }

        // Without pruning, dead servers stay for ever under the name they had when last joined.
        private static void ServerHistoryPruning(List<(string, bool, string)> r, string scratch)
        {
            Features.Servers.SeenServersStore.ResetForTest();
            DateTime now = DateTime.UtcNow;

            Features.Servers.SeenServersStore.Record("srv-old", "Long Gone", "1.1.1.1:25555", "1.3.0", "26.6.9.1");
            Features.Servers.SeenServersStore.Record("srv-new", "Still Here", "2.2.2.2:25555", "1.3.1", "26.8.31.1");
            Features.Servers.SeenServersStore.SetFavorite("srv-old", "Long Gone", true);
            Features.Servers.SeenServersStore.Record("srv-stale", "Also Gone", "3.3.3.3:25555", "1.3.0", "26.6.9.1");

            foreach (Features.Servers.SeenServersStore.Entry e in Features.Servers.SeenServersStore.AllForTest())
                if (e.ServerId != "srv-new")
                    e.LastSeenTicks = (now - Features.Servers.SeenServersStore.ForgetAfter
                                           - TimeSpan.FromDays(1)).Ticks;

            int dropped = Features.Servers.SeenServersStore.PruneForTest(now);
            var left = Features.Servers.SeenServersStore.All();
            bool keptFavourite = left.Exists(x => x.ServerId == "srv-old");
            bool keptRecent    = left.Exists(x => x.ServerId == "srv-new");
            bool droppedStale  = !left.Exists(x => x.ServerId == "srv-stale");

            r.Add(("servers: a server not seen in months is forgotten, unless it is a favourite",
                   keptFavourite && keptRecent && droppedStale && dropped == 1,
                   $"dropped={dropped}, favourite kept={keptFavourite}, recent kept={keptRecent}"));

            // A rename on Global must reach local history without the player rejoining.
            Features.Servers.SeenServersStore.ResetForTest();
            Features.Servers.SeenServersStore.Record("srv-ren", "Old Name", "1.1.1.1:25559", "1.3.1", "26.6.9.1");
            int changed = Features.Servers.SeenServersStore.RefreshFromDirectory(
                new List<Features.Servers.SeenServersStore.Entry>
                {
                    new Features.Servers.SeenServersStore.Entry
                    {
                        ServerId = "srv-ren", Name = "KMH | Test 0 | Legacy Upgrade",
                        Endpoint = "rwt.knappe0.games:25559", KmhVersion = "1.3.1", RwtVersion = "26.6.9.1",
                    },
                });
            var renamed = Features.Servers.SeenServersStore.All();
            bool adopted = changed >= 2 && renamed.Count == 1
                        && renamed[0].Name == "KMH | Test 0 | Legacy Upgrade"
                        && renamed[0].Endpoint == "rwt.knappe0.games:25559";
            r.Add(("servers: a renamed server stops showing the name it had when it was last joined", adopted,
                   adopted ? renamed[0].Name : "STALE NAME KEPT"));

            Features.Servers.SeenServersStore.ResetForTest();
            int none = Features.Servers.SeenServersStore.RefreshFromDirectory(
                new List<Features.Servers.SeenServersStore.Entry>
                {
                    new Features.Servers.SeenServersStore.Entry { ServerId = "srv-unknown", Name = "Someone Else" },
                });
            r.Add(("servers: the browser updates history it has, and never creates history it has not",
                   none == 0 && Features.Servers.SeenServersStore.All().Count == 0, $"changed={none}"));
        }

        private static void ServerHistoryKeying(List<(string, bool, string)> r)
        {
            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "kmh-seen-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Features.Servers.SeenServersStore.RootOverrideForTest = scratch;
                Features.Servers.SeenServersStore.ResetForTest();

                // Keyed on the endpoint, a moved server becomes two entries; an older server sends no id, only its address.
                Features.Servers.SeenServersStore.Record("", "", "1.2.3.4:5555", "1.3.0", "26.8.31.1");
                var afterLegacy = Features.Servers.SeenServersStore.All();
                bool legacyKept = afterLegacy.Count == 1 && !afterLegacy[0].IdentityKnown;

                Features.Servers.SeenServersStore.Record("srv-abc", "Medieval Fantasy", "1.2.3.4:5555", "1.3.1", "26.8.31.1");
                var afterId = Features.Servers.SeenServersStore.All();
                bool upgraded = afterId.Count == 1 && afterId[0].IdentityKnown
                             && afterId[0].ServerId == "srv-abc" && afterId[0].Name == "Medieval Fantasy";

                Features.Servers.SeenServersStore.Record("srv-abc", "Medieval Fantasy", "9.9.9.9:5555", "1.3.1", "26.8.31.1");
                var afterMove = Features.Servers.SeenServersStore.All();
                bool followedMove = afterMove.Count == 1 && afterMove[0].Endpoint == "9.9.9.9:5555";

                Features.Servers.SeenServersStore.Record("srv-xyz", "Outbreak", "1.2.3.4:5555", "1.3.1", "26.8.31.1");
                bool distinct = Features.Servers.SeenServersStore.All().Count == 2;

                Features.Servers.SeenServersStore.Forget("srv-abc");
                bool forgotten = Features.Servers.SeenServersStore.All().Count == 1;

                r.Add(("servers: history from before the permanent id is kept, then upgraded in place",
                       legacyKept && upgraded, $"legacyKept={legacyKept}, upgraded={upgraded}"));
                r.Add(("servers: a server that changes address stays one entry, and a new one there is its own",
                       followedMove && distinct, $"followedMove={followedMove}, distinct={distinct}"));
                r.Add(("servers: forgetting by permanent id removes exactly that server", forgotten, $"remaining={Features.Servers.SeenServersStore.All().Count}"));

                var listed = new List<Features.Global.ServerCardDto>
                {
                    new Features.Global.ServerCardDto { ServerId = "srv-official", JoinEndpoint = "9.9.9.9:5555", FromPublicDirectory = true },
                    new Features.Global.ServerCardDto { ServerId = "srv-cached", JoinEndpoint = "6.6.6.6:5555", FromPublicDirectory = false },
                };
                string elsewhere = Features.Servers.SeenServersStore.TrustedId("srv-official", "6.6.6.6:5555", listed);
                string there = Features.Servers.SeenServersStore.TrustedId("srv-official", "9.9.9.9:5555", listed);
                string cachedOnly = Features.Servers.SeenServersStore.TrustedId("srv-cached", "6.6.6.6:5555", listed);
                string noDirectory = Features.Servers.SeenServersStore.TrustedId("srv-official", "9.9.9.9:5555", null);
                Features.Servers.SeenServersStore.ResetForTest();
                Features.Servers.SeenServersStore.Record("srv-official", "Listed Name", "9.9.9.9:5555", "1.3.1", "26.10.20.1");
                Features.Servers.SeenServersStore.Record(noDirectory, "Listed Name", "9.9.9.9:5555", "1.3.1", "26.10.20.1");
                int entries = Features.Servers.SeenServersStore.All().Count;
                r.Add(("servers: a hello's id counts only when a listed card at the dialled address backs it",
                       elsewhere == "" && there == "srv-official" && cachedOnly == "" && noDirectory == "" && entries == 1,
                       $"elsewhere '{elsewhere}', there '{there}', cached only '{cachedOnly}', no directory '{noDirectory}', entries {entries}"));

                ServerHistoryPruning(r, scratch);
            }
            catch (Exception ex)
            {
                r.Add(("servers: history from before the permanent id is kept, then upgraded in place", false, "threw: " + ex.Message));
            }
            finally
            {
                try { Features.Servers.SeenServersStore.ResetForTest(); } catch { }
                Features.Servers.SeenServersStore.RootOverrideForTest = null;
                try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, true); } catch { }
            }
        }

        // The server drops a notice once acked, so content is stored first and a failed write means no ack.
        private static void DurableNoticeBounds(List<(string, bool, string)> r)
        {
            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "kmh-notices-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                // The runner has no Unity, so the real save-data path cannot be asked for here.
                Features.OfflineNotices.PendingNoticeStore.RootOverrideForTest = scratch;
                Features.OfflineNotices.PendingNoticeStore.ResetForTest();
                var batch = new List<Features.OfflineNotices.Dto.NotificationDto>
                {
                    new Features.OfflineNotices.Dto.NotificationDto { Id = "n-1", Title = "a", Body = "one" },
                    new Features.OfflineNotices.Dto.NotificationDto { Id = "n-2", Title = "b", Body = "two" },
                };

                bool stored = Features.OfflineNotices.PendingNoticeStore.Accept(batch, out var fresh);
                bool bothNew = stored && fresh.Count == 2;
                // Content, not just the id: a crash before the letter must still leave something to show.
                var pending = Features.OfflineNotices.PendingNoticeStore.TakePending();
                bool keptContent = pending.Count == 2 && pending.Exists(p => p.Id == "n-1" && p.Body == "one");

                // The redelivery a lost ack causes: recognised, and not shown a second time.
                bool again = Features.OfflineNotices.PendingNoticeStore.Accept(batch, out var repeat);
                bool noDuplicate = again && repeat.Count == 0;

                Features.OfflineNotices.PendingNoticeStore.MarkShown(batch);
                bool clearedAfterShown = Features.OfflineNotices.PendingNoticeStore.TakePending().Count == 0;
                // Still remembered after display, or a later redelivery would show it all over again.
                bool stillDeduped = Features.OfflineNotices.PendingNoticeStore.Accept(batch, out var afterShown)
                                    && afterShown.Count == 0;

                r.Add(("notices: a durable notice is written down with its content before it is acknowledged",
                       bothNew && keptContent, $"accepted={bothNew}, contentKept={keptContent}"));
                r.Add(("notices: a redelivered notice is recognised instead of shown twice",
                       noDuplicate && stillDeduped, $"beforeShow={noDuplicate}, afterShow={stillDeduped}"));
                r.Add(("notices: a displayed notice stops being pending",
                       clearedAfterShown, $"pendingAfterShown={!clearedAfterShown}"));
            }
            catch (Exception ex)
            {
                r.Add(("notices: a durable notice is written down with its content before it is acknowledged", false, "threw: " + ex.Message));
            }
            finally
            {
                try { Features.OfflineNotices.PendingNoticeStore.ResetForTest(); } catch { }
                Features.OfflineNotices.PendingNoticeStore.RootOverrideForTest = null;
                try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, true); } catch { }
            }
        }

        // Bounding paths (zip-slip) is not enough: a tiny archive can expand to fill a player's disk.
        private static void ZipExpansionBounds(List<(string, bool, string)> r)
        {
            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "kmh-zipbomb-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                // Highly compressible, so a small archive decompresses far past the per-entry cap.
                byte[] zeros = new byte[(int)Features.Enforcement.ConfigProfileUtility.MaxZipEntryBytes + (1 << 20)];
                byte[] bomb;
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                    using (var s = zip.CreateEntry("Mod_bomb.xml").Open())
                        s.Write(zeros, 0, zeros.Length);
                    bomb = ms.ToArray();
                }
                bool refused = false;
                try { Features.Enforcement.ConfigProfileUtility.ExtractZipBytesToFolder(bomb, scratch); }
                catch (System.IO.InvalidDataException) { refused = true; }
                catch { }
                bool ratio = bomb.Length < zeros.Length / 10;   // it really is a bomb, not just a big file
                r.Add(("enforcement: a config profile that expands past the limit is refused, not written",
                       refused && ratio, $"refused={refused}, archive={bomb.Length}B expands to {zeros.Length}B"));
            }
            catch (Exception ex) { r.Add(("enforcement: a config profile that expands past the limit is refused, not written", false, "threw: " + ex.Message)); }
            finally { try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, true); } catch { } }
        }

        // The declared size is allocated before the hash can prove anything, so a server's numbers are bounded first.
        private static void ProfileReceiveBounds(List<(string, bool, string)> r)
        {
            var A = typeof(Features.Enforcement.EnforcementProfileApplier);
            void Begin(string h, int c, int t) => A.GetMethod("OnProfileBegin").Invoke(null, new object[] { h, c, t });
            void Chunk(string h, int i, string b) => A.GetMethod("OnProfileChunk").Invoke(null, new object[] { h, i, b });
            int Held() => ((System.Collections.IDictionary)A
                .GetField("_rxChunks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null)).Count;

            // int.MaxValue bytes is a 2 GB allocation on the word of whatever server the player joined.
            Begin("deadbeef", 1, int.MaxValue);
            bool absurdTotal = Held() == 0;
            Chunk("deadbeef", 0, Convert.ToBase64String(new byte[8]));
            bool refusedStaysRefused = Held() == 0;

            Begin("deadbeef", int.MaxValue, 64);
            bool absurdCount = Held() == 0;

            // A legitimate push (one the player agreed to), then the three ways a hostile one tries to grow past it.
            const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var flow = typeof(Features.Enforcement.EnforcementFlow);
            flow.GetField("<PendingHash>k__BackingField", hidden).SetValue(null, "deadbeef");
            flow.GetField("_awaitingApply", hidden).SetValue(null, true);
            Begin("deadbeef", 2, 64);
            Chunk("deadbeef", 9999, Convert.ToBase64String(new byte[32]));
            bool indexRejected = Held() == 0;
            Chunk("deadbeef", 0, Convert.ToBase64String(new byte[Features.Enforcement.EnforcementProfileApplier.MaxRxChunkBytes + 1]));
            bool oversizeChunkRejected = Held() == 0;
            Chunk("deadbeef", 0, Convert.ToBase64String(new byte[32]));
            Chunk("deadbeef", 1, Convert.ToBase64String(new byte[64]));   // 32+64 > the declared 64
            bool overrunRejected = Held() == 1;

            Begin(null, 0, 0);   // leave nothing armed for the rest of the run
            Features.Enforcement.EnforcementFlow.Clear();

            r.Add(("enforcement: a profile declaring an absurd size is refused before anything is allocated",
                   absurdTotal && absurdCount, $"absurdTotal={absurdTotal}, absurdCount={absurdCount}"));
            r.Add(("enforcement: chunks for a refused profile are ignored rather than buffered",
                   refusedStaysRefused, $"buffered={!refusedStaysRefused}"));
            r.Add(("enforcement: an out-of-range or oversized chunk is dropped, not stored",
                   indexRejected && oversizeChunkRejected, $"index={indexRejected}, oversize={oversizeChunkRejected}"));
            r.Add(("enforcement: chunks cannot accumulate past the size the server declared",
                   overrunRejected, $"heldChunks={Held()}"));
        }

        // A snapshot the client renders and consults per mod must cost a bounded amount even when it is malformed.
        private static void EnforcementBounds(List<(string, bool, string)> r)
        {
            // KMH must recognise itself under any install name (_steam, a renamed test copy), or it locks the restore page.
            var kmhId = typeof(Features.Enforcement.EnforcementCache)
                .GetMethod("IsKmhId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            bool Ident(string id) => kmhId != null && (bool)kmhId.Invoke(null, new object[] { id });

            bool ownIds = Ident(Constants.PackageId) && Ident(Constants.PackageId + "_steam")
                       && Ident(Constants.PackageId + ".test") && Ident(" " + Constants.PackageId.ToUpperInvariant() + " ");
            r.Add(("enforcement: KMH recognises itself as plain, _steam and a renamed test install", ownIds,
                   kmhId == null ? "IsKmhId not found" : "4 form(s) matched"));

            bool notOthers = !Ident(Constants.PackageId + "er") && !Ident("knappe.kmh") && !Ident("") && !Ident(null);
            r.Add(("enforcement: a different mod whose id merely starts the same is not mistaken for KMH", notOthers,
                   notOthers ? "4 near-miss(es) refused" : "a near-miss matched"));

            int maxMods = Features.Enforcement.EnforcementCache.MaxSafeMods;
            var huge = new List<string>(maxMods + 500);
            for (int i = 0; i < maxMods + 500; i++) huge.Add("mod.id." + i);
            huge.Add(new string('x', 100_000));

            bool prevEnabled = Features.Enforcement.EnforcementCache.Enabled;
            Features.Enforcement.EnforcementCache.Apply(false, true, false, false, false, huge, new string('h', 100_000));
            int stored = Features.Enforcement.EnforcementCache.SafeCount;
            bool capped = stored <= maxMods;
            int longest = 0;
            foreach (string m in Features.Enforcement.EnforcementCache.SafeMods)
                if (m != null && m.Length > longest) longest = m.Length;
            bool trimmed = longest <= Features.Enforcement.EnforcementCache.MaxModIdChars;
            int hashLen = Features.Enforcement.EnforcementCache.ServerProfileHash.Length;
            bool hashBounded = hashLen <= Features.Enforcement.EnforcementCache.MaxHashChars;

            Features.Enforcement.EnforcementCache.Apply(prevEnabled, true, false, false, false, null, "");

            ProfileReceiveBounds(r);
            ZipExpansionBounds(r);
            ConfigApplyIsTransactional(r);
            DurableNoticeBounds(r);
            ServerHistoryKeying(r);
            GlobalBrowserState(r);
            MediaPreferenceRehydration(r);
            PlayerAccountLink(r);
            IdentityChangePaths(r);
            SteamAccountGate(r);
            SavedItemShape(r);
            OtherPlayersTextIsInert(r);
            DeliveryAllOrNothing(r);
            MarketRowText(r);
            AccountPromptRules(r);
            PlayerUsername(r);
            RwtVersionResolution(r);

            r.Add(("enforcement: an oversized safe-mods list is capped, not stored whole",
                   capped, $"stored={stored}, cap={maxMods}"));
            r.Add(("enforcement: an absurdly long mod id is trimmed rather than kept",
                   trimmed, $"longest={longest}"));
            r.Add(("enforcement: an absurdly long profile hash is trimmed rather than kept",
                   hashBounded, $"hashChars={hashLen}"));
        }

        // The reference envelope serialization (payload -> JObject -> serialize) for the byte-identity check.
        private sealed class EnvRef
        {
            [JsonProperty("kind")] public string  Kind    { get; set; }
            [JsonProperty("v")]    public int     Version { get; set; }
            [JsonProperty("data")] public JObject Data    { get; set; }
        }

        private sealed class EnvSample
        {
            [JsonProperty("a_str")] public string    A { get; set; }
            [JsonProperty("b_num")] public long      B { get; set; }
            [JsonProperty("c_opt", NullValueHandling = NullValueHandling.Ignore)] public string C { get; set; }
            [JsonProperty("d_list")] public List<int> D { get; set; }
        }

        private static string EnvOld(string kind, object data, int version)
            => JsonConvert.SerializeObject(new EnvRef
            {
                Kind = kind, Version = version, Data = data == null ? new JObject() : JObject.FromObject(data)
            });

        private sealed class FakeSource : IKmhWealthSource
        {
            private readonly float? _v;
            private readonly string _name;
            public FakeSource(float? v) { _v = v; _name = "fake"; }
            public FakeSource(string name, float? v) { _v = v; _name = name; }
            public string Name => _name;
            public float SilverValue() => _v ?? throw new InvalidOperationException("boom");
        }

        private static byte[] WebpBytes(bool animated)
        {
            var b = new System.Collections.Generic.List<byte>();
            b.AddRange(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            b.AddRange(new byte[] { 0, 0, 0, 0 });
            b.AddRange(System.Text.Encoding.ASCII.GetBytes("WEBP"));
            b.AddRange(System.Text.Encoding.ASCII.GetBytes("VP8X"));
            b.AddRange(new byte[] { 10, 0, 0, 0, 0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            if (animated)
            {
                b.AddRange(System.Text.Encoding.ASCII.GetBytes("ANIM"));
                b.AddRange(new byte[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
                b.AddRange(System.Text.Encoding.ASCII.GetBytes("ANMF"));
                b.AddRange(new byte[] { 16, 0, 0, 0 });
                b.AddRange(new byte[16]);
            }
            else
            {
                b.AddRange(System.Text.Encoding.ASCII.GetBytes("VP8L"));
                b.AddRange(new byte[] { 8, 0, 0, 0 });
                b.AddRange(new byte[8]);
            }
            return b.ToArray();
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] h = sha.ComputeHash(bytes);
            var sb = new System.Text.StringBuilder(64);
            foreach (byte b in h) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static byte[] Slice(byte[] src, int at, int len)
        {
            byte[] outp = new byte[len];
            System.Buffer.BlockCopy(src, at, outp, 0, len);
            return outp;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Verbatim encoder output, not hand-computed: it also carries a NETSCAPE loop and a local colour table.
        private static byte[] BuildTestGif() => new byte[]
        {
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x02, 0x00, 0x02, 0x00, 0x81, 0x00,
            0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x21, 0xFF, 0x0B, 0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45,
            0x32, 0x2E, 0x30, 0x03, 0x01, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00,
            0x00, 0x02, 0x00, 0x02, 0x00, 0x00, 0x08, 0x07, 0x00, 0x01, 0x04, 0x08,
            0x00, 0x20, 0x20, 0x00, 0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x02, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00, 0x81, 0xFF, 0x00,
            0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x07,
            0x00, 0x03, 0x00, 0x00, 0x10, 0x20, 0x20, 0x00, 0x3B,
        };

        // Deflate output differs across runtimes, so what is pinned is that SERVER-produced bytes inflate on Mono exactly.
        private const string ServerDeflateVector =
            "8DCAB10A80201006E077F96709B2A67B8896C66810EE4231ADF09610DFBDA13DFAE6AF2286CC20C4E4BB92DD59FCA13060A70" +
            "E54115452012D158141BD01CB36B92420CC2AB2C3E0D21B34DA66DE637F9CE1EBACAD3D";

        private const string ServerDeflateText =
            "{\"kind\":\"kmh.snapshot\",\"data\":{\"items\":[{\"id\":1,\"defName\":\"Steel\",\"qty\":42}," +
            "{\"id\":2,\"defName\":\"Steel\",\"qty\":42},{\"id\":3,\"defName\":\"Steel\",\"qty\":42}]}}";

        private static void FrameCodec(List<(string, bool, string)> r)
        {
            byte[] fromServer = HexBytes(ServerDeflateVector);
            byte[] inflated = SubProtocol.KmhFrameCodec.Inflate(fromServer, 64 * 1024);
            string text = inflated == null ? "" : System.Text.Encoding.UTF8.GetString(inflated);
            r.Add(("frame codec: a frame the server compressed inflates here byte for byte",
                   text == ServerDeflateText,
                   inflated == null ? "did not inflate at all" : $"{fromServer.Length} -> {inflated.Length} bytes"));

            byte[] raw = System.Text.Encoding.UTF8.GetBytes(ServerDeflateText + new string(' ', 4000));
            byte[] packed = SubProtocol.KmhFrameCodec.Deflate(raw);
            byte[] back = packed == null ? null : SubProtocol.KmhFrameCodec.Inflate(packed, 64 * 1024);
            r.Add(("frame codec: this client's own round trip returns the original",
                   back != null && System.Text.Encoding.UTF8.GetString(back) == System.Text.Encoding.UTF8.GetString(raw),
                   packed == null ? "did not compress" : $"{raw.Length} -> {packed.Length} bytes"));

            byte[] body = System.Text.Encoding.UTF8.GetBytes("hi");
            SubProtocol.KmhFrameCodec.ReadHeader(SubProtocol.KmhFrameCodec.Frame(body, true), out int zn, out bool zc);
            SubProtocol.KmhFrameCodec.ReadHeader(SubProtocol.KmhFrameCodec.Frame(body, false), out int pn, out bool pc);
            r.Add(("frame codec: the header flag and length agree with the server's",
                   zc && !pc && zn == body.Length && pn == body.Length, $"{zn}/{zc} vs {pn}/{pc}"));

            // Without this cap a server - or anything that can reach the port - turns a few KB into gigabytes here.
            byte[] bomb = SubProtocol.KmhFrameCodec.Deflate(new byte[4 * 1024 * 1024]);
            r.Add(("frame codec: a decompression bomb is refused at the frame cap",
                   bomb != null && SubProtocol.KmhFrameCodec.Inflate(bomb, 64 * 1024) == null
                                && SubProtocol.KmhFrameCodec.Inflate(bomb, 8 * 1024 * 1024) != null,
                   bomb == null ? "fixture did not compress" : $"{bomb.Length} packed bytes"));

            byte[] noise = new byte[8192];
            new Random(1234).NextBytes(noise);
            r.Add(("frame codec: incompressible data is sent raw, never bigger",
                   SubProtocol.KmhFrameCodec.Deflate(noise) == null, ""));
        }

        private static byte[] HexBytes(string hex)
        {
            byte[] outp = new byte[hex.Length / 2];
            for (int i = 0; i < outp.Length; i++)
                outp[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return outp;
        }

        private static bool Approx(float a, float b) => Math.Abs(a - b) < 0.01f;

        private static KmhThingPayload Payload(long marketValue, int stack)
            => new KmhThingPayload { MarketValue = marketValue, StackCount = stack };

        private static TreasurySnapshot Snap(int silver, Dictionary<string, int> items, List<KmhThingPayload> payloads)
            => new TreasurySnapshot { SilverBalance = silver, Items = items, ItemPayloads = payloads };
    }
}
