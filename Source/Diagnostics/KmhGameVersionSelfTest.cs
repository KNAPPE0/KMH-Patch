using System;
using System.Collections.Generic;
using System.IO;
using KMHPatch.Compat;
using KMHPatch.Features.Global;
using KMHPatch.Features.Mods;

namespace KMHPatch.Diagnostics
{
    // Public like RWT's packets, so MessagePack treats it as one.
    public sealed class MessagePackProbe { public string Name = "kmh"; public int Value = 7; }

    // The game's version is pinned in every check, so the suite answers the same on 1.6, 1.7 and outside the game.
    internal static class KmhGameVersionSelfTest
    {
        private const string Mine17 = "1.7.4976 rev1043", Mine16 = "1.6.4871 rev591";

        public static List<(string, bool, string)> Run()
        {
            var r = new List<(string, bool, string)>();
            string was = KmhGameVersion.FullForTest;
            void Check(string name, Func<(bool ok, string detail)> probe)
            {
                try { (bool ok, string detail) = probe(); r.Add((name, ok, detail)); }
                catch (Exception ex)
                {
                    // A reflected call wraps the real failure; the outer TargetInvocationException says nothing.
                    Exception inner = ex;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    r.Add((name, false, inner.GetType().Name + ": " + inner.Message));
                }
                finally { KmhGameVersion.FullForTest = was; }
            }

            Check("game version: major.minor is read from every form RimWorld and servers write, and nothing else", () =>
            {
                bool ok = KmhGameVersion.MajorMinorOf(Mine17) == "1.7" && KmhGameVersion.MajorMinorOf("1.6") == "1.6"
                       && KmhGameVersion.MajorMinorOf("1.10.2 rev3") == "1.10" && KmhGameVersion.MajorMinorOf("<b>1.7</b>") == ""
                       && KmhGameVersion.MajorMinorOf("") == "" && KmhGameVersion.MajorMinorOf(null) == ""
                       && KmhGameVersion.BuildOf(Mine16) == "1.6.4871" && KmhGameVersion.BuildOf("1.6") == "1.6";
                return (ok, $"{KmhGameVersion.MajorMinorOf(Mine17)} {KmhGameVersion.MajorMinorOf("1.10.2 rev3")} {KmhGameVersion.BuildOf(Mine16)}");
            });

            Check("game version: another major.minor differs, a build inside one version is only a build difference", () =>
            {
                bool ok = KmhGameVersion.Compare("1.6", Mine17) == GameMatch.Differs
                       && KmhGameVersion.Compare("1.7", Mine16) == GameMatch.Differs
                       && KmhGameVersion.Compare("1.7", Mine17) == GameMatch.Same
                       && KmhGameVersion.Compare("1.6", Mine16) == GameMatch.Same
                       && KmhGameVersion.Compare("1.6.4633", Mine16) == GameMatch.BuildDiffers
                       && KmhGameVersion.Compare("1.6.4871 rev1", "1.6.4871 rev2") == GameMatch.Same
                       && KmhGameVersion.Compare("", Mine17) == GameMatch.Unknown
                       && KmhGameVersion.Compare("<size=40>x", Mine17) == GameMatch.Unknown
                       && KmhGameVersion.Compare("1.6", "") == GameMatch.Unknown;
                return (ok, "");
            });

            Check("game version: the browser's note keeps its meaning - any difference, as far as the server says", () =>
            {
                bool ok = !Dialog_KMHServerBrowser.RimWorldDiffers("1.6.4633 rev1261", "1.6.4633 rev590")
                       && Dialog_KMHServerBrowser.RimWorldDiffers("1.6.4633", "1.6.4871")
                       && !Dialog_KMHServerBrowser.RimWorldDiffers("1.6", "1.6.4871 rev590")
                       && Dialog_KMHServerBrowser.RimWorldDiffers("1.6", Mine17)
                       && !Dialog_KMHServerBrowser.RimWorldDiffers("", Mine17);
                return (ok, "");
            });

            Check("join: a server on another RimWorld version is refused, named, and offered no fix", () =>
            {
                KmhGameVersion.FullForTest = Mine17;
                var card = Card("1.6", SubProtocol.KmhProtocol.CurrentVersion);
                CompatState s = KmhCompatibility.Evaluate(card);
                string label = KmhCompatibility.Describe(card), why = KmhCompatibility.Explain(card, s);
                bool ok = s == CompatState.GameVersionMismatch && KmhCompatibility.IsBlocking(s)
                       && !Dialog_KMHServerBrowser.Fixable(card, s)
                       && label.Contains("1.6") && why.Contains("1.6") && why.Contains("1.7.4976") && why.Contains("Betas")
                       && why.IndexOf("Prepare", StringComparison.Ordinal) < 0;
                return (ok, $"state={s} label='{label}'");
            });

            Check("join: the same version, or a server that names none, is never refused on the game version", () =>
            {
                KmhGameVersion.FullForTest = Mine17;
                var same = Card("1.7", SubProtocol.KmhProtocol.CurrentVersion + 1);
                var silent = Card("", SubProtocol.KmhProtocol.CurrentVersion + 1);
                // A protocol mismatch is used to stop the evaluation at the next rule without the running RWT.
                CompatState a = KmhCompatibility.Evaluate(same), b = KmhCompatibility.Evaluate(silent);
                KmhGameVersion.FullForTest = Mine16;
                var old = Card("1.6", SubProtocol.KmhProtocol.CurrentVersion + 1);
                CompatState c = KmhCompatibility.Evaluate(old);
                bool ok = a == CompatState.ProtocolMismatch && b == CompatState.ProtocolMismatch && c == CompatState.ProtocolMismatch;
                return (ok, $"1.7/1.7 -> {a}, unnamed -> {b}, 1.6/1.6 -> {c}");
            });

            Check("join: a mismatch is reported before anything a Prepare could act on", () =>
            {
                KmhGameVersion.FullForTest = Mine17;
                var card = new ServerCardDto { Status = "Online", RimWorldVersion = "1.6", JoinEndpoint = "host:25555", MaxPlayers = 8,
                                               Protocol = SubProtocol.KmhProtocol.CurrentVersion + 1, RwtVersion = "0.0.0.1", ModPolicy = "Enforced" };
                CompatState s = KmhCompatibility.Evaluate(card);
                return (s == CompatState.GameVersionMismatch, $"state={s}");
            });

            Check("session: the in-game refusal names both versions and the Steam branch, and promises nothing", () =>
            {
                string t = SubProtocol.KmhHandshakeHandler.GameVersionText("1.6", Mine17);
                bool ok = t.Contains("RimWorld 1.6") && t.Contains(Mine17) && t.Contains("Betas") && t.Contains("Colony Nexus stays off")
                       && t.IndexOf("switch", StringComparison.OrdinalIgnoreCase) < 0;
                return (ok, t);
            });

            Check("session: the game version is judged before Config Enforcement can act on a hello", () =>
            {
                List<System.Reflection.MethodBase> calls = KmhIlReader.MethodsCalledBy("KMHPatch.SubProtocol.KmhHandshakeHandler", "OnHello");
                int refuse = calls.FindIndex(m => m.Name == "RefuseGameVersion");
                int begin = calls.FindIndex(m => m.Name == "Begin" && m.DeclaringType?.Name == "EnforcementAdmission");
                return (refuse >= 0 && begin > refuse, $"game check at call {refuse}, enforcement at call {begin}");
            });

            Check("prepare: a job made on another RimWorld version is dropped; a job from before the stamp is kept", () =>
            {
                var j16 = new Features.Mods.KmhPrepareJob { GameVersion = "1.6" };
                var j17 = new Features.Mods.KmhPrepareJob { GameVersion = "1.7" };
                var jOld = new Features.Mods.KmhPrepareJob();
                bool ok = Features.Mods.KmhPrepareJobStore.FromOtherGame(j16, "1.7")
                       && !Features.Mods.KmhPrepareJobStore.FromOtherGame(j17, "1.7")
                       && !Features.Mods.KmhPrepareJobStore.FromOtherGame(jOld, "1.7")
                       && !Features.Mods.KmhPrepareJobStore.FromOtherGame(j16, "")
                       && !Features.Mods.KmhPrepareJobStore.FromOtherGame(null, "1.7");
                return (ok, "");
            });

            Check("rwt copies: a copy is read from this version's folder, then the newest older one, never a newer one", () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "kmh-rwtfolders-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    foreach (string d in new[] { "1.5", "1.6", "1.8", "Assemblies", "1.6.old", "About" })
                        Directory.CreateDirectory(Path.Combine(root, d));
                    string on17 = string.Join(",", Features.Rwt.RwtCopies.VersionFolders(root, "1.7"));
                    string on16 = string.Join(",", Features.Rwt.RwtCopies.VersionFolders(root, "1.6"));
                    bool ok = on17 == "1.7,1.6,1.5" && on16 == "1.6,1.5";
                    return (ok, $"1.7 -> {on17}; 1.6 -> {on16}");
                }
                finally { try { Directory.Delete(root, true); } catch { } }
            });

            // RWT ships an older build in 1.6 and the current one in Current; reading 1.6 makes a copy look old on 1.7.
            Check("rwt copies: a copy with LoadFolders.xml is read where RimWorld loads it, for this game version", () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "kmh-rwtload-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string prevGame = Features.Rwt.RwtCopies.GameVersionFolderForTest;
                try
                {
                    foreach (string d in new[] { "1.6", "Current" }) Directory.CreateDirectory(Path.Combine(root, d, "Assemblies"));
                    File.WriteAllBytes(Path.Combine(root, "1.6", "Assemblies", "RTShared.dll"), System.Text.Encoding.Unicode.GetBytes("x26.10.15.1x"));
                    File.WriteAllBytes(Path.Combine(root, "Current", "Assemblies", "RTShared.dll"), System.Text.Encoding.Unicode.GetBytes("x26.10.20.1x"));
                    string lf = Path.Combine(root, "LoadFolders.xml");
                    File.WriteAllText(lf, "<loadFolders><v1.6><li>1.6</li></v1.6><v1.7><li>Current</li></v1.7></loadFolders>");
                    string Read(string game) { Features.Rwt.RwtCopies.GameVersionFolderForTest = game; return Features.Rwt.RwtCopies.VersionOfCopy(root); }
                    string on17 = Read("1.7"), on16 = Read("1.6"), on18 = Read("1.8");
                    // An older RWT's own file names no 1.7 entry, so 1.7 takes the newest older one.
                    File.WriteAllText(lf, "<loadFolders><v1.6><li>/</li><li>Current</li></v1.6></loadFolders>");
                    string older = Read("1.7");
                    bool ok = on17 == "26.10.20.1" && on16 == "26.10.15.1" && on18 == "26.10.20.1" && older == "26.10.20.1";
                    return (ok, $"1.7 -> {on17}; 1.6 -> {on16}; 1.8 -> {on18}; 1.6-only file on 1.7 -> {older}");
                }
                finally
                {
                    Features.Rwt.RwtCopies.GameVersionFolderForTest = prevGame;
                    try { Directory.Delete(root, true); } catch { }
                }
            });

            const string LiveCopy = "rwt copies: the copy RimWorld loaded RimWorld Together from reads as the version running now";
            string loadedRoot = null;
            try
            {
                foreach (Verse.ModContentPack m in Verse.LoadedModManager.RunningModsListForReading)
                    if (m != null && string.Equals(KmhModPlan.Bare(m.PackageId), KmhModPlan.RwtId, StringComparison.OrdinalIgnoreCase))
                        loadedRoot = m.ModMetaData?.RootDir?.FullName;
            }
            catch (Exception) { }
            if (string.IsNullOrEmpty(loadedRoot) || string.IsNullOrEmpty(KmhRwtVersions.Mine))
                r.Add(KmhSelfTestResult.Skipped(LiveCopy, "no RimWorld Together loaded"));
            else
                Check(LiveCopy, () =>
                {
                    string read = Features.Rwt.RwtCopies.VersionOfCopy(loadedRoot);
                    return (Features.Rwt.RwtVersionText.Same(read, KmhRwtVersions.Mine), $"{loadedRoot}: read {read}, running {KmhRwtVersions.Mine}");
                });

            Check("runtime: Mono and CoreCLR are told apart by capability, consistently", () =>
            {
                bool mono = Type.GetType("Mono.Runtime") != null;
                return (KmhGameVersion.IsCoreClr == !mono, KmhGameVersion.IsCoreClr ? "CoreCLR" : "Mono");
            });

            Check("unity compat: a missing member is absorbed by its helper, and null is never touched", () =>
            {
                KmhUnityCompat.SetReverbZoneMix(null, 0f);
                KmhUnityCompat.SetControlledAudioTrackCount(null, 1);
                bool ok = !KmhUnityCompat.CanSetTime(null);
                return (ok, "");
            });

            Check("messagepack: RunAndSave becomes Run, and any other access keeps its meaning", () =>
            {
                bool ok = (int)KmhMessagePackCoreClr.Runnable((System.Reflection.Emit.AssemblyBuilderAccess)3) == 1
                       && (int)KmhMessagePackCoreClr.Runnable((System.Reflection.Emit.AssemblyBuilderAccess)1) == 1
                       && (int)KmhMessagePackCoreClr.Runnable((System.Reflection.Emit.AssemblyBuilderAccess)9) == 9;
                return (ok, "");
            });

            Check("loading screen: RimWorld's loading-window state is readable on this game version", () =>
                (UI.KmhLoadingScreen.Available, ""));

            Check("brand mark: the moon starts where the website draws it, then passes in front of the world and behind it", () =>
            {
                var world = new UnityEngine.Vector2(16f, 17f);
                UI.KmhBrand.Orbit(0f, out UnityEngine.Vector2 at, out float r0, out _);
                UI.KmhBrand.Orbit(UnityEngine.Mathf.PI / 2f, out UnityEngine.Vector2 near, out _, out bool front);
                UI.KmhBrand.Orbit(-UnityEngine.Mathf.PI / 2f, out UnityEngine.Vector2 far, out _, out bool farFront);
                bool ok = (at - new UnityEngine.Vector2(27f, 6.5f)).magnitude < 0.01f && UnityEngine.Mathf.Approximately(r0, 2f)
                       && front && !farFront && (near - world).magnitude < 8f && (far - world).magnitude < 8f && near.y > far.y;
                return (ok, $"start {at}, near {near}, far {far}");
            });

            Check("loading motion: Animated off stills everything, Reduce motion keeps only the progress bar moving", () =>
            {
                var off = UI.KmhLoadingPanel.Motion.For(false, false, 12f);
                var calm = UI.KmhLoadingPanel.Motion.For(true, true, 12f);
                var full = UI.KmhLoadingPanel.Motion.For(true, false, 12f);
                bool ok = !off.Functional && !off.Decorative && off.T == 0f && float.IsNaN(off.Moon())
                       && calm.Functional && !calm.Decorative && float.IsNaN(calm.Moon()) && calm.Breath(0.2f, 0.05f, 1f) == 0.2f
                       && full.Functional && full.Decorative && !float.IsNaN(full.Moon());
                return (ok, "");
            });

            Check("loading panel: progress is steps done plus the running step's own share, never more", () =>
            {
                PrepLine Line(PrepLineState s, float p = -1f) => new PrepLine { State = s, Progress = p };
                float a = Features.Global.Dialog_KMHPrepareJoin.Overall(new[] { Line(PrepLineState.Done), Line(PrepLineState.Skipped),
                    Line(PrepLineState.Working, 0.5f), Line(PrepLineState.Pending) }, out PrepLine cur, out int index, out int total);
                float b = Features.Global.Dialog_KMHPrepareJoin.Overall(new[] { Line(PrepLineState.Done), Line(PrepLineState.Waiting),
                    Line(PrepLineState.Pending) }, out PrepLine waiting, out int wIndex, out _);
                float c = Features.Global.Dialog_KMHPrepareJoin.Overall(new[] { Line(PrepLineState.Working, 7f) }, out _, out _, out _);
                float none = Features.Global.Dialog_KMHPrepareJoin.Overall(new PrepLine[0], out PrepLine nothing, out _, out _);
                bool ok = UnityEngine.Mathf.Approximately(a, 2.5f / 4f) && index == 3 && total == 4 && cur?.Progress == 0.5f
                       && UnityEngine.Mathf.Approximately(b, 1f / 3f) && wIndex == 2 && waiting != null
                       && UnityEngine.Mathf.Approximately(c, 1f) && none < 0f && nothing == null;
                return (ok, $"{a:0.###} {b:0.###} {c:0.###} {none}");
            });

            Check("main menu: the card and the recovery warning stay clear of each other, RimWorld's menu and RWT's play button", () =>
            {
                bool Inside(UnityEngine.Rect a, float sw, float sh) => a.xMin >= 0f && a.yMin >= 0f && a.xMax <= sw && a.yMax <= sh;
                var warnSize = new UnityEngine.Vector2(420f, 112f);
                var bad = new List<string>();
                int moved = 0;
                foreach ((float sw, float sh) in new[] { (1024f, 768f), (1280f, 720f), (1280f, 800f), (1366f, 768f), (1600f, 900f),
                                                         (1920f, 1080f), (2560f, 1440f), (960f, 540f) })
                {
                    UnityEngine.Rect pane = UI.KmhMainMenuSurface.DefaultPane(sw, sh);
                    UnityEngine.Rect card = UI.KmhMainMenuSurface.Place(sw, sh, pane);
                    UnityEngine.Rect warn = UI.KmhMainMenuSurface.Claim(warnSize, sw, sh, card, pane);
                    // RimWorld Together's play button, drawn beside the first row.
                    var rwtPlay = new UnityEngine.Rect(pane.x - 50f, pane.y, 45f, 45f);
                    if (warn.y > 16f) moved++;
                    if (card.Overlaps(pane) || card.Overlaps(rwtPlay) || warn.Overlaps(card) || warn.Overlaps(pane) || warn.Overlaps(rwtPlay)
                        || !Inside(card, sw, sh) || !Inside(warn, sw, sh))
                        bad.Add($"{sw}x{sh}");
                }
                // A size where the top spot is taken proves the claim was exercised, not just never needed.
                return (bad.Count == 0 && moved > 0, bad.Count > 0 ? "overlap at " + string.Join(", ", bad) : $"8 sizes, moved under the card at {moved}");
            });

            const string shaderCheck = "native shader: the shipped light sweep is registered, compiles and loads on RimWorld 1.7";
            if (!KmhGameVersion.IsCoreClr) r.Add(KmhSelfTestResult.Skipped(shaderCheck, "RimWorld 1.6 has no native shaders"));
            else Check(shaderCheck, () => (UI.KmhShaders.Ready(), UI.KmhShaders.Status));

            // Older RWT serializes via a dynamic formatter that 1.7 breaks; newer RWT takes only its annotated packets.
            const string packet = "messagepack: RWT serializes a packet on this runtime";
            Type serializer = HarmonyLib.AccessTools.TypeByName("RTShared.Misc.Serializer");
            if (serializer == null) r.Add(KmhSelfTestResult.Skipped(packet, "RimWorld Together is not loaded"));
            else Check(packet, () =>
            {
                bool generated = HarmonyLib.AccessTools.TypeByName("MessagePack.GeneratedMessagePackResolver") != null;
                Type pkt = generated ? HarmonyLib.AccessTools.TypeByName("RTNetwork.Packets.PKT_Version") : typeof(MessagePackProbe);
                if (pkt == null) return (false, "RTNetwork.Packets.PKT_Version is gone");
                System.Reflection.MethodInfo convert = HarmonyLib.AccessTools.Method(serializer, "ConvertObjectToBytes")?.MakeGenericMethod(pkt);
                if (convert == null) return (false, "RTShared.Misc.Serializer.ConvertObjectToBytes is gone");
                byte[] bytes = (byte[])convert.Invoke(null, new object[] { Activator.CreateInstance(pkt), false });
                bool patched = KmhGameVersion.IsCoreClr ? KmhMessagePackCoreClr.Status == "applied" : true;
                return (patched && bytes != null && bytes.Length > 0,
                        $"{(KmhGameVersion.IsCoreClr ? "CoreCLR" : "Mono")}, {pkt.Name}, fix {KmhMessagePackCoreClr.Status}, {bytes?.Length ?? 0} bytes");
            });

            return r;
        }

        private static ServerCardDto Card(string rimWorld, int protocol)
            => new ServerCardDto { Status = "Online", RimWorldVersion = rimWorld, JoinEndpoint = "host:25555", MaxPlayers = 8,
                                   Protocol = protocol, ModPolicy = "None" };
    }
}
