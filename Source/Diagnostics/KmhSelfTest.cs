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
        private static bool _ran;

        // One-time, debug-gated. Called from the first KMH activation; a bool check is the only cost when off.
        public static void RunOnceIfDebug()
        {
            if (_ran) return;
            _ran = true;
            if (KMHPatchMod.Settings?.DebugLogging != true) return;

            List<(string name, bool pass, string detail)> results;
            KmhLog.Muted = true;
            try { results = Run(); }
            catch (Exception ex) { KmhLog.Muted = false; KmhLog.Warn($"KMH self-test threw before completing: {ex.Message}"); return; }
            finally { KmhLog.Muted = false; }

            // Only failures name themselves: a server with debug on had every client ship 392 PASS lines per connect.
            int passed = 0;
            foreach (var r in results)
            {
                if (r.pass) passed++;
                else KmhLog.Warn($"  FAIL  {r.name} - {r.detail}");
            }
            string line = $"KMH client self-test: {passed}/{results.Count} passed";
            if (passed == results.Count) KmhLog.Info(line); else KmhLog.Warn($"{line} (see FAILs above)");
        }

        public static List<(string, bool, string)> Run()
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
            // The regression itself: a 13-row status table above the buttons is what broke it.
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

            // The mode was written by two screens and read by none, so Never behaved exactly like Ask.
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

            KMHPatch.Features.Identity.KmhStaff.Clear();
            r.Add(("staff: a server switch takes its badges with it",
                   !KMHPatch.Features.Identity.KmhStaff.TryBadgeForRole("admin", out _, out _), ""));

            // A real hand-built GIF89a: 2x2, a two-colour table, two frames.
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

            // An animated png is not a gif, and the badge said GIF over every Discord sticker.
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

            // Root.Update runs KMH's frame work; one step throwing used to take the rest of the frame with it.
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

            // One dead link answered 404 eleven times in seven seconds, because every failure was treated as retryable.
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

            // The cap is the SERVER's number, so an owner who raises their limit is not overridden by an invisible smaller one.
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

            bool Drift(bool had, bool has, string name, int kbd)
                => KMHPatch.Features.Chat.ChatPanel.DriftedOffField(had, has, name, kbd);
            r.Add(("chat focus: a shifted control id is recovered from",
                   Drift(true, false, "", 42), ""));
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
                   && KMHPatch.Features.Chat.KmhAgo.Since(DateTime.UtcNow.Ticks) == "just now"
                   && KMHPatch.Features.Chat.KmhAgo.Since(DateTime.UtcNow.AddHours(-3).Ticks) == "3h ago",
                   KMHPatch.Features.Chat.KmhAgo.Since(DateTime.UtcNow.AddHours(-3).Ticks)));

            // Crediting every member the whole vault multiplies server wealth by guild size; crediting nobody makes it a shelter.
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

            // The two sets look alike in a screenshot, so a marker wearing the wrong one is only visible by the path it loads.
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

            // Caching the null a pre-content lookup answers left the marker on its def texture for the whole session.
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

            // A socket error carrying its FormatMessage tail, CRLF then NULs. Built char by char and length-checked first: Mono's new string('\0', n) hands back recycled heap.
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


            // Vanilla starts a world quad's art along an east-west tangent, which laid KMH's directional art on its side.
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

            // "I sent it" and "the server applied it" were once one latch, so a refused push left the client trusting a catalog the server never held.
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

            // @name in a chat line. Getting this wrong is either a missed message or a ping for nobody.
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

            // What the box offers while you type. A finished mention must stop offering, or it completes twice.
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

            // Chunks may arrive in any order; they must be joined by INDEX, not by arrival.
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

            // A stream of the right shape but the wrong content must not reach a decoder.
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

            const string signedUrl = "https://media.discordapp.net/attachments/125676983546937344/918551905582600212/"
                                   + "Shiroket-3.gif?ex=6a8736a0&is=6a85e520&hm=a92f2801d3249016a61e1114641a519e";
            string shownA = KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(signedUrl, signedUrl, out bool didA);
            r.Add(("media: a body that is only a long signed media url becomes a short label",
                   didA && shownA == "Shiroket-3.gif · media.discordapp.net", shownA));

            // The vetted url a message carries can differ from the body's by a ?format= hint the server appended.
            string shownB = KMHPatch.Features.Chat.ChatMediaLabel.DisplayBody(
                signedUrl, signedUrl + "&format=png", out bool didB);
            r.Add(("media: a ?format= hint does not stop the body being recognised as the same media",
                   didB, shownB));

            // Everything a person might actually want to read stays exactly as it was.
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

            // A server switch must not leave the previous owner's palette - or their marker - behind.
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

            // Expanded content is taller than collapsed - if it were not, expanding would show nothing.
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
            return r;
        }

        private static List<(string, bool, string)> RoadReconcileChecks()
        {
            var r = new List<(string, bool, string)>();
            const string trail = "trail", highway = "highway";
            const string dirt = "DirtPath", stone = "StoneRoad";
            A Mine(string applied, string prev) => new A { AppliedDefName = applied, PreviousDefName = prev };

            // Empty pair, server wants a road.
            V v = KmhRoadReconcile.Decide(trail, dirt, 10, null, null, 0);
            r.Add(("road: an empty pair gets our road and a record of it",
                   v.Action == Act.Add && v.Record?.AppliedDefName == dirt && v.Record.PreviousDefName == null, ""));

            // A road already on the planet, at least as good. The player was charged for this segment, so it gets laid.
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

            // Someone else's road, worse than ours: step over it but remember it.
            v = KmhRoadReconcile.Decide(highway, stone, 30, null, dirt, 10);
            r.Add(("road: upgrading over another road remembers what was there",
                   v.Action == Act.Upgrade && v.Record?.PreviousDefName == dirt, ""));

            // The server dropped a segment we had upgraded over something.
            v = KmhRoadReconcile.Decide(null, null, 0, Mine(stone, dirt), stone, 30);
            r.Add(("road: dropping an upgraded segment restores the road it replaced",
                   v.Action == Act.Restore && v.WriteDefName == dirt && v.Record == null, "a hole would be worse than the original"));

            // The server dropped a segment we added to bare ground.
            v = KmhRoadReconcile.Decide(null, null, 0, Mine(dirt, null), dirt, 10);
            r.Add(("road: dropping a segment we added removes it",
                   v.Action == Act.Remove && v.Record == null, ""));

            // We never touched this pair and the server does not want it.
            v = KmhRoadReconcile.Decide(null, null, 0, null, stone, 30);
            r.Add(("road: a road we never touched is never removed",
                   v.Action == Act.None && v.Record == null, "the whole point of tracking what we wrote"));

            // Something else changed the road under us.
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

            // No usable RoadDef in this game: wanting a road we cannot name must not delete an existing one.
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

        // A request's snapshot and a mutation's newer one can cross in flight: never step backwards, but still accept a new server's first.
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

                // ...and an unknown or already-settled id must not disturb whatever is currently outstanding.
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

        // Both are decisions about value: a retried mutation can submit twice, and a tunnelled one ignores the owner's policy.
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
            KmhMainThread.PostForTest(genBefore, () => ran++);          // same session: runs
            KmhMainThread.PumpForTest();
            int afterSame = ran;
            KmhMainThread.PostForTest(genBefore - 1, () => ran++);      // an older session: dropped
            KmhMainThread.PumpForTest();
            r.Add(("transport: a queued packet from an ended session is dropped at the pump",
                   afterSame == 1 && ran == 1, $"same-session ran {afterSame}, stale added {ran - afterSame}"));

            AddRevisionChecks(r);
            AddOpIdChecks(r);
            AddDeliveryReceiptChecks(r);
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
                bool nullSafe = true;
                try
                {
                    Features.Delivery.GameComponent_KMHDeliveryReceipts.RecordDelivered("srv-a:2");
                    nullSafe = !Features.Delivery.GameComponent_KMHDeliveryReceipts.HeldAlready("srv-a:2");
                }
                catch { nullSafe = false; }
                r.Add(("deliveries: recording without a loaded game is survivable, and claims nothing",
                       nullSafe, nullSafe ? "" : "A MISSING GAME MUST NOT THROW NOR REPORT A DELIVERY AS HELD"));

                // The held copy carries the id, or a grant that lands late is never acknowledged and replays for ever.
                bool holdCarriesId = false;
                foreach (System.Reflection.ParameterInfo pi in typeof(Features.Delivery.KmhPendingDelivery)
                             .GetMethod("HoldSilver")?.GetParameters() ?? new System.Reflection.ParameterInfo[0])
                    if (pi.Name == "deliveryId") holdCarriesId = true;
                r.Add(("deliveries: a held grant carries the id it will be acknowledged with",
                       holdCarriesId, holdCarriesId ? "" : "A HELD GRANT CANNOT BE ACKED WHEN IT FINALLY LANDS"));
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

            // Identity still separates: different material, quality or taint are different vault rows.
            var ident = new List<KmhThingPayload> { Stack(10, 0, 100, "<x/>") };
            var otherStuff = Stack(10, 0, 100, "<y/>"); otherStuff.StuffDefName = "Steel";
            var otherQual  = Stack(10, 0, 100, "<z/>"); otherQual.Quality = 4;
            var otherTaint = Stack(10, 0, 100, "<w/>"); otherTaint.Tainted = true;
            r.Add(("Capture: material, quality and taint never fold together",
                   !KmhThingCapture.TryFoldFungible(ident, otherStuff)
                   && !KmhThingCapture.TryFoldFungible(ident, otherQual)
                   && !KmhThingCapture.TryFoldFungible(ident, otherTaint), ""));

            // A non-fungible item keeps its own blob: that is the whole point of the full-state path.
            var unique = new List<KmhThingPayload> { Stack(1, -1, 90, "<gear/>", mergeable: false) };
            r.Add(("Capture: a non-fungible item is never folded",
                   !KmhThingCapture.TryFoldFungible(unique, Stack(1, -1, 90, "<gear2/>", mergeable: false)), ""));

            // Unknown wear stays unknown rather than becoming a real-looking zero.
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
            foreach (string setter in required)
            {
                System.Reflection.MethodInfo m = src.GetMethod(setter,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (m == null || !MethodBodyCalls(cfg, m)) missing.Add(setter.Substring(4));
            }
            r.Add(("video audio: the configurator opts out of every positional and listener effect",
                   missing.Count == 0, missing.Count == 0 ? "" : "unset: " + string.Join(", ", missing)));

            r.Add(("video audio: the player's host actually applies that configuration",
                   ReferencesMethod(vp, cfg), ""));
        }

        // True when `caller`'s own IL carries a token for `target`.
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

            // Rows must stack strictly downwards and the measured total must cover every one of them.
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

            // The A/B premise: 10,000 silver deposited must arrive as exactly 10,000, not a rounded or scaled figure.
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

            // Switching servers must not carry the previous server's consent or its queued lines.
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

        // Server A -> B -> A: a revision means nothing across servers, so carrying A's to B would make B's first snapshot look stale.
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

        // A snapshot the client renders and consults per mod must cost a bounded amount even when it is malformed.
        private static void EnforcementBounds(List<(string, bool, string)> r)
        {
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
            r.Add(("enforcement: an oversized safe-mods list is capped, not stored whole",
                   capped, $"stored={stored}, cap={maxMods}"));
            r.Add(("enforcement: an absurdly long mod id is trimmed rather than kept",
                   trimmed, $"longest={longest}"));
            r.Add(("enforcement: an absurdly long profile hash is trimmed rather than kept",
                   hashBounded, $"hashChars={hashLen}"));
        }

        // Reproduces the pre-optimization envelope serialization (payload -> JObject -> serialize) for the byte-identity check.
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

        // A wealth source with a fixed value, or one that throws when value is null - to prove the fold's invariants.
        private sealed class FakeSource : IKmhWealthSource
        {
            private readonly float? _v;
            private readonly string _name;
            public FakeSource(float? v) { _v = v; _name = "fake"; }
            public FakeSource(string name, float? v) { _v = v; _name = name; }
            public string Name => _name;
            public float SilverValue() => _v ?? throw new InvalidOperationException("boom");
        }

        // Verbatim encoder output, not hand-computed: it also carries a NETSCAPE loop and a local colour table.
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
        private static bool Approx(float a, float b) => Math.Abs(a - b) < 0.01f;

        private static KmhThingPayload Payload(long marketValue, int stack)
            => new KmhThingPayload { MarketValue = marketValue, StackCount = stack };

        private static TreasurySnapshot Snap(int silver, Dictionary<string, int> items, List<KmhThingPayload> payloads)
            => new TreasurySnapshot { SilverBalance = silver, Items = items, ItemPayloads = payloads };
    }
}
