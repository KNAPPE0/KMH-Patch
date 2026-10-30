using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Features.Rwt;

namespace KMHPatch.Diagnostics
{
    // The RWT installer takes only bounded, verified bytes from GitHub's release hosts and unpacks only into staging.
    internal static class KmhRwtPackageSelfTest
    {
        public static List<(string, bool, string)> Run()
        {
            var r = new List<(string, bool, string)>();
            void Check(string name, Func<(bool ok, string detail)> probe)
            {
                try { (bool ok, string detail) = probe(); r.Add((name, ok, detail)); }
                catch (Exception ex) { r.Add((name, false, $"threw {ex.GetType().Name}: {ex.Message}")); }
            }
            string scratch = Path.Combine(Path.GetTempPath(), "kmh-rwt-pkg-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(scratch);
            try
            {
                Checks(Check, scratch);
            }
            finally { try { Directory.Delete(scratch, true); } catch (Exception) { } }
            return r;
        }

        private static void Checks(Action<string, Func<(bool, string)>> Check, string scratch)
        {
            Check("RWT package: only GitHub's release hosts over https are download addresses", () =>
            {
                string[] good = { "https://github.com/RimWorld-Together/Rimworld-Together/releases/download/1/x.zip",
                                  "https://objects.githubusercontent.com/a", "https://release-assets.githubusercontent.com/a" };
                string[] bad = { "http://github.com/x.zip", "https://github.com.evil.example/x.zip", "https://evil.example/x.zip",
                                 "https://user:pw@github.com/x.zip", "https://github.com:8443/x.zip", "file:///C:/x.zip", "not a url", "" };
                var wrong = new List<string>();
                foreach (string u in good) if (!RwtPackage.IsOfficialUrl(u)) wrong.Add("refused " + u);
                foreach (string u in bad) if (RwtPackage.IsOfficialUrl(u)) wrong.Add("allowed " + u);
                return (wrong.Count == 0, wrong.Count == 0 ? $"{good.Length} allowed, {bad.Length} refused" : string.Join("; ", wrong));
            });

            Check("RWT package: a release asset pointing anywhere else is never offered", () =>
            {
                string json = "[{\"tag_name\":\"26.9.1.1\",\"draft\":false,\"prerelease\":false,\"assets\":["
                            + "{\"name\":\"3005289691.zip\",\"browser_download_url\":\"https://evil.example/3005289691.zip\",\"size\":10},"
                            + "{\"name\":\"Server-linux-x64.zip\",\"browser_download_url\":\"https://github.com/someone/fork/releases/download/26.9.1.1/Server-linux-x64.zip\",\"size\":10},"
                            + "{\"name\":\"Server-win-x64.zip\",\"browser_download_url\":\"" + Official + "26.9.1.1/Server-win-x64.zip\",\"size\":10}]}]";
                List<RwtRelease> parsed = RwtReleaseCatalog.Parse(json);
                bool ok = parsed.Count == 1 && parsed[0].Assets.Count == 1 && parsed[0].Assets[0].Name == "Server-win-x64.zip";
                return (ok, $"{parsed.Count} release(s), {(parsed.Count > 0 ? parsed[0].Assets.Count : 0)} asset(s) kept");
            });

            Check("RWT package: a missing checksum is never a match", () =>
            {
                string file = Path.Combine(scratch, "digest.bin");
                File.WriteAllBytes(file, Encoding.ASCII.GetBytes("hello"));
                string real = Sha256(file);
                bool ok = !RwtInstaller.DigestMatches(file, "") && !RwtInstaller.DigestMatches(file, "abc")
                       && RwtInstaller.DigestMatches(file, real) && !RwtInstaller.DigestMatches(file, new string('0', 64));
                return (ok, ok ? "missing and malformed digests fail; the right one passes" : "a missing digest still verified");
            });

            Check("RWT package: an unverifiable package needs the player's explicit yes", () =>
            {
                RwtReleaseCatalog.SeedForTest(new List<RwtRelease>());
                try
                {
                    RwtInstallPlan plan = Plan(Official + "1/3005289691.zip", "");
                    RwtInstallPlan offsite = Plan("https://evil.example/3005289691.zip", new string('a', 64));
                    bool ok = RwtInstaller.Refusal(plan, false) != null && RwtInstaller.Refusal(plan, true) == null
                           && RwtInstaller.Refusal(Plan(plan.Asset.Url, new string('a', 64)), false) == null
                           && RwtInstaller.Refusal(offsite, true) != null;
                    return (ok, RwtInstaller.Refusal(plan, false) ?? "no refusal without consent");
                }
                finally { RwtReleaseCatalog.ResetForTest(); }
            });

            // The first hop is a release asset of RWT's own repository; any other github.com page is someone else's file.
            Check("RWT package: only RWT's own release assets are a first download address", () =>
            {
                string[] good = { Official + "26.8.31.1/3005289691.zip", "https://GitHub.com/rimworld-together/rimworld-together/releases/download/x/y.zip" };
                string[] bad = { "https://github.com/someone/fork/releases/download/26.8.31.1/3005289691.zip",
                                 "https://objects.githubusercontent.com/github-production-release-asset/x",
                                 Official + "../../../../someone/fork/releases/download/1/x.zip",
                                 Official + "1/x.zip?redirect=https://evil.example", "http://github.com/RimWorld-Together/Rimworld-Together/releases/download/1/x.zip" };
                var wrong = new List<string>();
                foreach (string u in good) if (!RwtPackage.IsReleaseDownloadUrl(u)) wrong.Add("refused " + u);
                foreach (string u in bad) if (RwtPackage.IsReleaseDownloadUrl(u)) wrong.Add("allowed " + u);
                bool refused = RwtInstaller.Refusal(Plan("https://github.com/someone/fork/releases/download/1/3005289691.zip", new string('a', 64)), true) != null;
                return (wrong.Count == 0 && refused, wrong.Count == 0 ? (refused ? "" : "the installer took a fork's asset") : string.Join("; ", wrong));
            });

            // The server names the version, so a pre-release or a release past the recent ones needs the player's own yes.
            Check("RWT package: a pre-release or an old release needs the player's explicit yes", () =>
            {
                var stable = new List<RwtRelease>();
                for (int i = 1; i <= 12; i++) stable.Add(new RwtRelease { Version = $"26.{i}.1.1", Tag = $"26.{i}.1.1" });
                stable.Reverse();
                RwtReleaseCatalog.SeedForTest(stable);
                try
                {
                    string sha = new string('a', 64);
                    RwtInstallPlan pre = Plan(Official + "1/3005289691.zip", sha);
                    pre.Release.Prerelease = true;
                    RwtInstallPlan old = Plan(Official + "1/3005289691.zip", sha);
                    old.Release = stable[11];
                    RwtInstallPlan recent = Plan(Official + "1/3005289691.zip", sha);
                    recent.Release = stable[0];
                    bool ok = RwtInstaller.Refusal(pre, false) != null && RwtInstaller.Refusal(pre, true) == null
                           && RwtInstaller.Refusal(old, false) != null && RwtInstaller.Refusal(old, true) == null
                           && RwtInstaller.Refusal(recent, false) == null && pre.UnusualText().Length > 0;
                    return (ok, $"pre={RwtInstaller.Refusal(pre, false) ?? "allowed"}, old={RwtInstaller.Refusal(old, false) ?? "allowed"}, recent={RwtInstaller.Refusal(recent, false) ?? "allowed"}");
                }
                finally { RwtReleaseCatalog.ResetForTest(); }
            });

            // A parked copy without the marker may be the player's own, moved aside by an older release.
            Check("RWT package: pruning old copies never deletes one Colony Nexus did not install", () =>
            {
                string prior = RwtInstaller.ModsRootOverrideForTest;
                string game = Path.Combine(scratch, "prune-game");
                Directory.CreateDirectory(Path.Combine(game, "Mods"));
                RwtInstaller.ModsRootOverrideForTest = Path.Combine(game, "Mods");
                try
                {
                    string previous = RwtCopies.PreviousRoot;
                    Directory.CreateDirectory(previous);
                    string own = Path.Combine(previous, "3005289691.kmh-previous-20250101");
                    Directory.CreateDirectory(own);
                    Directory.SetCreationTimeUtc(own, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                    var marked = new List<string>();
                    for (int i = 0; i < 4; i++)
                    {
                        string d = Path.Combine(previous, "3005289691-2026010" + i);
                        Directory.CreateDirectory(d);
                        RwtCopies.WriteMarker(d, "26." + i + ".1.1", true);
                        Directory.SetCreationTimeUtc(d, new DateTime(2026, 1, 1 + i, 0, 0, 0, DateTimeKind.Utc));
                        marked.Add(d);
                    }
                    typeof(RwtInstaller).GetMethod("PrunePrevious", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        .Invoke(null, new object[] { marked[3] });
                    int left = marked.FindAll(Directory.Exists).Count;
                    bool ok = Directory.Exists(own) && Directory.Exists(marked[3]) && left == RwtInstaller.KeepPrevious;
                    return (ok, $"player's copy kept={Directory.Exists(own)}, marked copies left={left}");
                }
                finally { RwtInstaller.ModsRootOverrideForTest = prior; }
            });

            Check("RWT package: a refused request while a download runs leaves that download's progress alone", () =>
            {
                try
                {
                    RwtInstaller.SetRunningForTest(true, RwtInstallStage.Downloading, "26.10.20.1");
                    RwtInstaller.Begin(null);
                    bool kept = RwtInstaller.Stage == RwtInstallStage.Downloading && RwtInstaller.Detail == "26.10.20.1";
                    return (kept, $"stage after the refused request: {RwtInstaller.Stage} ({RwtInstaller.Detail})");
                }
                finally { RwtInstaller.SetRunningForTest(false, RwtInstallStage.Idle, ""); }
            });

            Check("RWT package: a normal package unpacks", () =>
            {
                string zip = Zip(scratch, ("3005289691/About/About.xml", "<ModMetaData/>"), ("3005289691/Assemblies/a.dll", "x"));
                string staging = Fresh(scratch);
                string failed = RwtPackage.Extract(zip, staging);
                bool ok = failed == null && File.Exists(Path.Combine(staging, "3005289691", "About", "About.xml"))
                       && RwtInstaller.FindModRoot(staging) != null;
                return (ok, failed ?? "About.xml unpacked under staging");
            });

            foreach (string hostile in new[] { "../evil.txt", "a/../../evil.txt", "/etc/evil.txt", "C:/evil.txt", "a.txt:stream",
                                               "CON.txt", "a/nul", "a//b.txt", "a/b./c.txt" })
            {
                string name = hostile;
                Check($"RWT package: an entry named '{name}' is refused and nothing escapes", () =>
                {
                    string zip = Zip(scratch, ("ok.txt", "fine"), (name, "pwned"));
                    string staging = Fresh(scratch);
                    string failed = RwtPackage.Extract(zip, staging);
                    bool escaped = File.Exists(Path.Combine(scratch, "evil.txt")) || File.Exists(Path.Combine(Path.GetDirectoryName(scratch), "evil.txt"));
                    return (failed != null && !escaped, failed ?? "accepted");
                });
            }

            Check("RWT package: too many entries is refused", () =>
            {
                var many = new List<(string, string)>();
                for (int i = 0; i < 12; i++) many.Add(($"f{i}.txt", "x"));
                string failed = RwtPackage.Extract(Zip(scratch, many.ToArray()), Fresh(scratch), maxEntries: 10);
                return (failed != null && failed.Contains("too many"), failed ?? "accepted");
            });

            Check("RWT package: a package that unpacks past the cap is refused, however small it is packed", () =>
            {
                string zip = Zip(scratch, ("big.txt", new string('z', 400_000)));
                bool small = new FileInfo(zip).Length < 10_000;
                string failed = RwtPackage.Extract(zip, Fresh(scratch), maxUnpacked: 100_000);
                return (small && failed != null, $"packed {new FileInfo(zip).Length} bytes; {failed ?? "accepted"}");
            });

            Func<Uri, bool> local = u => u.Scheme == "http" && u.Host == "127.0.0.1";
            Func<string, Task<string>> Download(string dest, long expected, long cap, int stallSeconds = 5)
                => url => RwtPackage.DownloadAsync(url, dest, expected, cap, local, TimeSpan.FromSeconds(stallSeconds),
                                                   TimeSpan.FromSeconds(20), CancellationToken.None);

            Check("RWT package: a normal download is stored whole", () =>
            {
                byte[] body = Encoding.ASCII.GetBytes(new string('p', 50_000));
                string dest = Path.Combine(scratch, "normal.zip");
                string failed = Serve(Ok(body), Download(dest, body.Length, 1_000_000));
                bool ok = failed == null && File.Exists(dest) && new FileInfo(dest).Length == body.Length && !File.Exists(dest + ".partial");
                return (ok, failed ?? $"{new FileInfo(dest).Length} bytes");
            });

            Check("RWT package: a declared length over the cap is refused before the body is read", () =>
            {
                string dest = Path.Combine(scratch, "declared.zip");
                string failed = Serve(Headers("HTTP/1.1 200 OK\r\nContent-Length: 999999999\r\n\r\n"), Download(dest, 0, 1_000_000));
                // Named by its declared size: refused on the header, not after reading into a body that never comes.
                bool onHeader = failed != null && failed.StartsWith("the download is larger than the release says (999999999", StringComparison.Ordinal);
                return (onHeader && !File.Exists(dest), failed ?? "accepted");
            });

            Check("RWT package: a body with no length stops at the cap and leaves nothing behind", () =>
            {
                string dest = Path.Combine(scratch, "chunked.zip");
                string failed = Serve(Unbounded(), Download(dest, 0, 200_000));
                return (failed != null && !File.Exists(dest) && !File.Exists(dest + ".partial"), failed ?? "accepted");
            });

            Check("RWT package: a stalled download is abandoned", () =>
            {
                string dest = Path.Combine(scratch, "stall.zip");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string failed = Serve(Stall(), Download(dest, 0, 1_000_000, stallSeconds: 1));
                bool ok = failed != null && failed.Contains("stalled") && sw.Elapsed < TimeSpan.FromSeconds(6) && !File.Exists(dest);
                return (ok, $"{failed ?? "accepted"} after {sw.Elapsed.TotalSeconds:0.0}s");
            });

            Check("RWT package: a redirect away from the allowed hosts is refused", () =>
            {
                string dest = Path.Combine(scratch, "redirect.zip");
                string failed = Serve(Headers("HTTP/1.1 302 Found\r\nLocation: https://evil.example/x.zip\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),
                    Download(dest, 0, 1_000_000));
                return (failed != null && failed.Contains("redirected away"), failed ?? "accepted");
            });

            Check("RWT package: a redirect loop ends", () =>
            {
                string dest = Path.Combine(scratch, "loop.zip");
                string failed = ServeMany(port => Headers($"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{port}/again\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),
                    Download(dest, 0, 1_000_000));
                return (failed != null && failed.Contains("too many"), failed ?? "accepted");
            });

            Check("RWT package: a download that is not the size the release lists is refused", () =>
            {
                byte[] body = Encoding.ASCII.GetBytes("short");
                string dest = Path.Combine(scratch, "size.zip");
                string failed = Serve(Ok(body), Download(dest, 1000, 1_000_000));
                return (failed != null && failed.Contains("not the size the release lists") && !File.Exists(dest), failed ?? "accepted");
            });
        }

        private const string Official = "https://github.com/RimWorld-Together/Rimworld-Together/releases/download/";

        private static RwtInstallPlan Plan(string url, string sha) => new RwtInstallPlan
        {
            State = RwtPlanState.Ready,
            Release = new RwtRelease { Version = "26.9.1.1", Tag = "26.9.1.1" },
            Asset = new RwtAsset { Name = "3005289691.zip", Url = url, Size = 10, Sha256 = sha, Role = RwtAssetRole.Client },
        };

        private static string Sha256(string file)
        {
            using (FileStream fs = File.OpenRead(file))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        private static string Fresh(string scratch)
        {
            string dir = Path.Combine(scratch, "stage-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Zip(string scratch, params (string Name, string Text)[] entries)
        {
            string path = Path.Combine(scratch, Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");
            using (FileStream fs = File.Create(path))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                foreach ((string name, string text) in entries)
                {
                    ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using (StreamWriter w = new StreamWriter(e.Open())) w.Write(text);
                }
            return path;
        }

        private static string Serve(Action<NetworkStream> respond, Func<string, Task<string>> download)
            => ServeMany(_ => respond, download);

        private static string ServeMany(Func<int, Action<NetworkStream>> respondFor, Func<string, Task<string>> download)
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (CancellationTokenSource stop = new CancellationTokenSource())
            {
                Task server = Task.Run(async () =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        TcpClient c;
                        try { c = await listener.AcceptTcpClientAsync().ConfigureAwait(false); } catch (Exception) { return; }
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                using (c)
                                {
                                    NetworkStream s = c.GetStream();
                                    ReadRequest(s);
                                    respondFor(port)(s);
                                }
                            }
                            catch (Exception) { }
                        });
                    }
                });
                try
                {
                    Task<string> run = download($"http://127.0.0.1:{port}/pkg.zip");
                    if (!run.Wait(TimeSpan.FromSeconds(30))) return "hung for 30 s";
                    return run.Result;
                }
                finally { stop.Cancel(); listener.Stop(); }
            }
        }

        private static void ReadRequest(NetworkStream s)
        {
            var seen = new StringBuilder();
            byte[] one = new byte[1];
            while (!seen.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && s.Read(one, 0, 1) == 1) seen.Append((char)one[0]);
        }

        private static Action<NetworkStream> Ok(byte[] body) => s =>
        {
            byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            s.Write(head, 0, head.Length);
            s.Write(body, 0, body.Length);
        };

        private static Action<NetworkStream> Headers(string raw) => s =>
        {
            byte[] head = Encoding.ASCII.GetBytes(raw);
            s.Write(head, 0, head.Length);
            Thread.Sleep(200);
        };

        private static Action<NetworkStream> Unbounded() => s =>
        {
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n");
            s.Write(head, 0, head.Length);
            byte[] chunk = new byte[16384];
            for (int i = 0; i < 200; i++) s.Write(chunk, 0, chunk.Length);
        };

        private static Action<NetworkStream> Stall() => s =>
        {
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 100000\r\nConnection: close\r\n\r\nabc");
            s.Write(head, 0, head.Length);
            Thread.Sleep(15000);
        };
    }
}
