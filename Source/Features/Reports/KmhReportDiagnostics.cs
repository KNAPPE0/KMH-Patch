using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Reports
{
    // Game state is read on the game's thread; the log file and trimming happen off it.
    internal static class KmhReportDiagnostics
    {
        internal const int KmhLogLines    = 300;
        internal const int GameLogEntries = 40;
        internal const int StackLines     = 10;
        internal const int MaxEntryChars  = 1500;
        internal const int TailBytes      = 512 * 1024;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // The redactor is built on the game's thread: the secrets it removes live in its stores.
        internal sealed class Captured
        {
            public JObject Root;
            public Redactor Redact;
            public string LogPath = "";
            public string TopError = "";
            // Sent even when diagnostics are not: they say which build the report is about, nothing about the player.
            public JObject Versions = new JObject();
        }

        // Each section is caught alone, so one broken accessor costs only that section.
        internal static Captured CaptureOnMainThread()
        {
            var root = new JObject();
            Section(root, "summary",         Summary);
            Section(root, "performance",     PerformanceSection);
            Section(root, "frame_steps",     FrameStepsSection);
            Section(root, "connection",      ConnectionSection);
            Section(root, "network",         NetworkSection);
            Section(root, "recent_requests", () => KmhRequestLog.Live.Snapshot());
            Section(root, "game",            GameSection);
            Section(root, "system",          SystemSection);
            Section(root, "settings",        SettingsSection);
            Section(root, "account",         AccountSection);
            Section(root, "mods",            ModsSection);
            Section(root, "open_windows",    WindowsSection);
            Section(root, "kmh_problems",    () => new JArray(KmhLog.RecentProblems().ToArray()));
            Section(root, "error_groups",    () => KmhGameLogWatch.Live.Section(KmhGameLogWatch.ReportedGroups));
            Section(root, "game_log",        GameLogSection);

            string path = "";
            try { path = KmhLog.LogFilePath; } catch { }
            string top = "";
            try { top = KmhGameLogWatch.Live.TopErrorSignature(); } catch { }
            var versions = new JObject();
            if (root["summary"] is JObject s)
                foreach (string k in VersionKeys) if (s[k] != null) versions[k] = s[k].DeepClone();
            return new Captured { Root = root, Redact = Redactor.ForThisMachine(), LogPath = path, TopError = top, Versions = versions };
        }

        internal static readonly string[] VersionKeys = { "kmh_version", "build", "protocol", "rimworld", "rimworld_together" };

        // Unticked diagnostics still carry the player's own choices and the versions, so a report can be sorted.
        internal static string Complete(Captured c, ReportFields fields, bool attach, string delivery, int maxChars)
        {
            JObject root;
            if (!attach || c?.Root == null)
                root = new JObject { ["summary"] = c?.Versions?.DeepClone() ?? new JObject() };
            else
            {
                root = c.Root;
                try { root["kmh_log"] = new JArray(TailLines(c.LogPath, KmhLogLines, TailBytes).ToArray()); }
                catch (Exception ex) { root["kmh_log"] = new JArray("could not read the Colony Nexus log: " + ex.Message); }
            }
            Stamp(root, fields, c?.TopError, delivery);
            Scrub(root, c?.Redact);
            return Fit(root, maxChars);
        }

        // Global's inbox reads these names; the version keys beside them are unchanged.
        internal static void Stamp(JObject root, ReportFields fields, string topError, string delivery)
        {
            if (root == null) return;
            ReportFields f = (fields ?? new ReportFields()).Normalized();
            if (!(root["summary"] is JObject summary))
            {
                summary = new JObject();
                root.AddFirst(new JProperty("summary", summary));
            }
            summary["report_kind"]  = f.Kind;
            summary["area"]         = f.Area;
            summary["severity"]     = f.Severity;
            summary["frequency"]    = f.Frequency;
            summary["destinations"] = f.DestinationsCsv;
            summary["fingerprint"]  = KmhGameLogWatch.Fingerprint(KmhProtocol.DisplayVersion, topError, f.Title);
            summary["destination"]  = delivery ?? "";
        }

        private static void Section(JObject root, string name, Func<JToken> build)
        {
            try { root[name] = build() ?? new JObject(); }
            catch (Exception ex) { root[name] = new JObject { ["error"] = $"{ex.GetType().Name}: {ex.Message}" }; }
        }

        private static JToken Summary()
        {
            DateTime now = DateTime.Now;
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(now);
            return new JObject
            {
                ["kmh_version"]     = KmhProtocol.DisplayVersion,
                ["build"]           = KmhProtocol.UiBuildTag,
                ["protocol"]        = KmhProtocol.CurrentVersion,
                ["client_assembly"] = typeof(KmhReportDiagnostics).Assembly.GetName().Name,
                ["rimworld"]        = Safe(() => VersionControl.CurrentVersionStringWithRev),
                ["rimworld_together"] = Safe(() => Features.Global.KmhRwtVersions.Mine),
                ["language"]        = Safe(() => LanguageDatabase.activeLanguage?.folderName),
                ["session_id"]      = KmhLog.SessionId,
                ["captured_utc"]    = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", Inv) + "Z",
                ["utc_offset"]      = (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm", Inv),
                ["minutes_running"] = Math.Round(Time.realtimeSinceStartup / 60.0, 1),
                ["screen"]          = Safe(() => Current.ProgramState.ToString()),
            };
        }

        private static JToken PerformanceSection()
        {
            KmhPerfSampler.Snapshot p = KmhPerfSampler.Build();
            var o = new JObject
            {
                ["frames_sampled"]      = p.FramesSampled,
                ["fps_average"]         = Round(p.FpsAvg),
                ["fps_slowest_1pct"]    = Round(p.FpsLow1),
                ["frame_ms_typical"]    = Round(p.FrameMsP50),
                ["frame_ms_95pct"]      = Round(p.FrameMsP95),
                ["frame_ms_99pct"]      = Round(p.FrameMsP99),
                ["frame_ms_worst"]      = Round(p.FrameMsWorst),
                ["ticks_per_second"]    = p.Tps < 0f ? "no game" : Round(p.Tps).ToString(Inv),
                ["ticks_target"]        = p.TpsTarget < 0f ? "no game" : Round(p.TpsTarget).ToString(Inv),
                ["kmh_ms_per_frame"]    = Math.Round(p.KmhMsPerFrame, 3),
                ["main_thread_backlog"] = KmhMainThread.Backlog,
                ["managed_heap_mb"]     = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 1),
                ["gc_collections"]      = GC.CollectionCount(0),
            };
            try
            {
                o["mono_heap_mb"]   = Math.Round(UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / (1024.0 * 1024.0), 1);
                o["mono_used_mb"]   = Math.Round(UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0), 1);
                o["unity_alloc_mb"] = Math.Round(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0), 1);
            }
            catch { /* not every player build answers these */ }
            return o;
        }

        private static JToken FrameStepsSection()
        {
            var arr = new JArray();
            foreach (var s in KmhPerfSampler.Build().Steps)
                arr.Add(new JObject { ["step"] = s.Step, ["average_ms"] = Math.Round(s.AvgMs, 3), ["worst_ms"] = Math.Round(s.MaxMs, 3) });
            return arr;
        }

        private static JToken ConnectionSection()
        {
            var o = new JObject
            {
                ["kmh_server"]       = KmhDispatcher.IsKmhServer,
                ["server_name"]      = KmhDispatcher.ServerName,
                ["server_id"]        = KmhDispatcher.ServerId,
                ["server_build"]     = KmhDispatcher.ServerBuild,
                ["server_protocol"]  = KmhDispatcher.ServerProtocolVersion,
                ["transport"]        = KmhTransport.Status.ToString(),
                ["degraded_reason"]  = KmhTransport.DegradedReason ?? "",
                ["api_endpoint"]     = KmhApiClient.EffectiveEndpoint,
                ["api_link"]         = KmhApiClient.LinkSummary,
                ["chat_fallback"]    = KmhApiClient.ChatFallbackAllowed,
                ["features_off"]     = UI.KmhDashboardState.DisabledList(),
                ["reports_mode"]     = KmhReportClient.ServerMode,
                ["reports_version"]  = KmhReportClient.ServerReportsVersion,
                ["packets_received"] = KmhDispatcher.ReceivedCount,
                ["last_packet"]      = KmhDispatcher.LastReceivedKind ?? "",
            };
            if (KmhDispatcher.LastReceivedAt != DateTime.MinValue)
                o["last_packet_seconds_ago"] = (int)(DateTime.UtcNow - KmhDispatcher.LastReceivedAt).TotalSeconds;
            return o;
        }

        private static JToken NetworkSection()
        {
            var o = new JObject();
            foreach (KeyValuePair<string, long> kv in KmhClientTransportStats.Snapshot()) o[kv.Key] = kv.Value;
            return o;
        }

        private static JToken GameSection()
        {
            Game game = Current.Game;
            if (game == null) return new JObject { ["loaded"] = false };
            var o = new JObject { ["loaded"] = true };
            TickManager ticks = game.tickManager;
            if (ticks != null)
            {
                o["ticks"] = ticks.TicksGame;
                o["speed"] = ticks.CurTimeSpeed.ToString();
                o["days_passed"] = GenDate.DaysPassed;
            }
            o["storyteller"] = Safe(() => Find.Storyteller?.def?.label);
            o["difficulty"]  = Safe(() => Find.Storyteller?.difficultyDef?.label);
            o["maps"]        = Safe(() => Find.Maps?.Count.ToString(Inv));
            o["map_sizes"]   = Safe(() =>
            {
                var sizes = new List<string>();
                foreach (Map m in Find.Maps) sizes.Add($"{m.Size.x}x{m.Size.z}");
                return string.Join(", ", sizes.ToArray());
            });
            o["colonists"]     = Safe(() => PawnsFinder.AllMaps_FreeColonists.Count.ToString(Inv));
            o["current_map_wealth"] = Safe(() => Find.CurrentMap?.wealthWatcher?.WealthTotal.ToString("0", Inv));
            o["world_tiles"]   = Safe(() => Find.WorldGrid?.TilesCount.ToString(Inv));
            o["dlc"] = Safe(() =>
            {
                var dlc = new List<string>();
                if (ModsConfig.RoyaltyActive)  dlc.Add("Royalty");
                if (ModsConfig.IdeologyActive) dlc.Add("Ideology");
                if (ModsConfig.BiotechActive)  dlc.Add("Biotech");
                if (ModsConfig.AnomalyActive)  dlc.Add("Anomaly");
                if (ModsConfig.OdysseyActive)  dlc.Add("Odyssey");
                return dlc.Count == 0 ? "none" : string.Join(", ", dlc.ToArray());
            });
            return o;
        }

        private static JToken SystemSection()
        {
            Resolution res = Screen.currentResolution;
            return new JObject
            {
                ["os"]            = SystemInfo.operatingSystem,
                ["cpu"]           = SystemInfo.processorType,
                ["cpu_threads"]   = SystemInfo.processorCount,
                ["cpu_mhz"]       = SystemInfo.processorFrequency,
                ["ram_mb"]        = SystemInfo.systemMemorySize,
                ["gpu"]           = SystemInfo.graphicsDeviceName,
                ["gpu_vendor"]    = Compat.KmhUnityCompat.GraphicsDeviceVendor(),
                ["graphics_api"]  = SystemInfo.graphicsDeviceVersion,
                ["vram_mb"]       = SystemInfo.graphicsMemorySize,
                ["window"]        = $"{Screen.width}x{Screen.height}" + (Screen.fullScreen ? " fullscreen" : " windowed"),
                ["display"]       = res.ToString(),
                ["ui_scale"]      = Safe(() => Prefs.UIScale.ToString("0.##", Inv)),
                ["unity"]         = Application.unityVersion,
                ["rimworld"]      = Compat.KmhGameVersion.Full,
                ["runtime"]       = RuntimeName(),
                ["process_64bit"] = Environment.Is64BitProcess,
            };
        }

        private static JToken SettingsSection()
        {
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s == null) return new JObject { ["loaded"] = false };
            return new JObject
            {
                ["debug_logging"]        = s.DebugLogging,
                ["protocol_logging"]     = s.ProtocolLogging,
                ["log_sharing_mode"]     = s.LogSharingMode,
                ["use_api_transport"]    = s.UseKmhApiTransport,
                ["allow_chat_fallback"]  = s.AllowChatTransportFallback,
                ["api_fallback_port"]    = s.KmhApiPort,
                ["api_host_override"]    = string.IsNullOrWhiteSpace(s.KmhApiHostOverride) ? "not set" : "set",
                ["global_directory"]     = Global.GlobalHttp.IsOfficial ? "official" : "custom",
                ["server_colours"]       = s.ThemeUseServer,
                ["auto_load_chat_images"] = s.AutoLoadChatImages,
                ["discord_presence"]     = s.DiscordPresenceEnabled,
                ["discord_join"]         = s.AllowDiscordJoin,
                ["discord_activity"]     = s.DiscordShowActivity,
                ["global_presence"]      = s.GlobalPresenceEnabled,
                ["video_quality"]        = s.VideoQuality,
                ["chat_notifications_muted"] = s.ChatNotificationsMuted,
                ["perf_overlay"]         = s.ShowPerfOverlay,
            };
        }

        // Never the credential itself.
        private static JToken AccountSection()
        {
            string dir = Global.GlobalHttp.BaseUrl;
            return new JObject
            {
                ["global_linked"]   = Safe(() => Features.Account.KmhAccountStore.HasToken(dir) ? "yes" : "no"),
                ["global_username"] = Safe(() => Features.Account.KmhUsernameClient.HasUsername ? Features.Account.KmhUsernameClient.Username : ""),
            };
        }

        private static JToken ModsSection()
        {
            var arr = new JArray();
            int order = 0;
            foreach (ModMetaData mod in ModsConfig.ActiveModsInLoadOrder)
            {
                order++;
                arr.Add(new JObject
                {
                    ["order"]      = order,
                    ["name"]       = mod.Name ?? "",
                    ["package_id"] = mod.PackageIdPlayerFacing ?? "",
                    ["version"]    = Safe(() => mod.ModVersion),
                    ["source"]     = mod.Official ? "official" : mod.Source == ContentSource.SteamWorkshop ? "workshop" : "local",
                });
            }
            return arr;
        }

        private static JToken WindowsSection()
        {
            var arr = new JArray();
            if (Find.WindowStack == null) return arr;
            foreach (Window w in Find.WindowStack.Windows) if (w != null) arr.Add(w.GetType().FullName);
            return arr;
        }

        // Every mod's errors, not only KMH's.
        private static JToken GameLogSection()
        {
            List<LogMessage> all = KmhGameLogWatch.CopyGameLog();
            var arr = new JArray();
            if (all == null) return arr;
            var picked = new List<LogMessage>();
            foreach (LogMessage m in all) if (m != null && m.type != LogMessageType.Message) picked.Add(m);
            // The game keeps no time per message; the watch's first sighting is the closest there is.
            for (int i = Math.Max(0, picked.Count - GameLogEntries); i < picked.Count; i++)
            {
                LogMessage m = picked[i];
                arr.Add(LogEntry(m.type == LogMessageType.Error, m.repeats, KmhGameLogWatch.SeenAt(m), m.text, Safe(() => m.StackTrace)));
            }
            return arr;
        }

        internal static string LogEntry(bool error, int repeats, string timestamp, string text, string stack)
        {
            string head = "[" + (error ? "error" : "warning") + (repeats > 1 ? " x" + repeats : "")
                        + (string.IsNullOrEmpty(timestamp) ? "" : " " + timestamp) + "] " + Cap((text ?? "").Trim(), MaxEntryChars);
            string tail = FirstLines(stack, StackLines);
            return tail.Length > 0 ? head + "\n" + tail : head;
        }

        internal static string FirstLines(string text, int lines)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] parts = text.Replace("\r\n", "\n").Split('\n');
            var kept = new List<string>();
            foreach (string p in parts)
            {
                if (p.Trim().Length == 0) continue;
                if (kept.Count == lines) { kept.Add("  ..."); break; }
                kept.Add("  " + Cap(p.Trim(), 300));
            }
            return string.Join("\n", kept.ToArray());
        }

        // Opened shared: the logger may still be appending.
        internal static List<string> TailLines(string path, int lines, int maxBytes)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0L, fs.Length - maxBytes);
                fs.Seek(start, SeekOrigin.Begin);
                using (var reader = new StreamReader(fs, new UTF8Encoding(false), false))
                {
                    if (start > 0) reader.ReadLine();   // starts mid-line
                    var all = new List<string>();
                    string line;
                    while ((line = reader.ReadLine()) != null) all.Add(line);
                    for (int i = Math.Max(0, all.Count - lines); i < all.Count; i++) result.Add(Cap(all[i], MaxEntryChars));
                }
            }
            return result;
        }

        // The server truncates past maxChars, which would leave unreadable half-JSON.
        internal static string Fit(JObject root, int maxChars)
        {
            string json = root.ToString(Formatting.None);
            if (json.Length <= maxChars) return json;

            var trimmed = new List<string>();
            foreach (var (name, fromStart) in new[] { ("kmh_log", true), ("game_log", true), ("kmh_problems", true),
                                                      ("open_windows", false), ("mods", false), ("frame_steps", false),
                                                      ("error_groups", false), ("recent_requests", true) })
            {
                if (!(root[name] is JArray arr) || arr.Count == 0) continue;
                int before = arr.Count;
                while (arr.Count > 0 && json.Length > maxChars)
                {
                    int drop = Math.Max(1, (arr.Count + 3) / 4);
                    for (int i = 0; i < drop && arr.Count > 0; i++) arr.RemoveAt(fromStart ? 0 : arr.Count - 1);
                    json = root.ToString(Formatting.None);
                }
                trimmed.Add($"{name} {before} -> {arr.Count}");
                if (json.Length <= maxChars) break;
            }

            while (json.Length > maxChars)
            {
                JProperty largest = null;
                int size = 0;
                foreach (JProperty p in root.Properties())
                {
                    if (p.Name == "summary") continue;
                    int n = p.Value.ToString(Formatting.None).Length;
                    if (n > size) { size = n; largest = p; }
                }
                if (largest == null) break;
                trimmed.Add(largest.Name + " removed");
                largest.Remove();
                json = root.ToString(Formatting.None);
            }

            if (root["summary"] is JObject summary)
            {
                summary["trimmed_to_fit"] = string.Join("; ", trimmed.ToArray());
                json = root.ToString(Formatting.None);
                if (json.Length > maxChars) { summary.Remove("trimmed_to_fit"); json = root.ToString(Formatting.None); }
            }
            return json.Length <= maxChars ? json : "{}";
        }

        internal static void Scrub(JToken token, Redactor r)
        {
            if (token == null || r == null) return;
            if (token is JValue v)
            {
                if (v.Type == JTokenType.String) v.Value = r.Apply((string)v.Value);
                return;
            }
            foreach (JToken child in token.Children()) Scrub(child, r);
        }

        // Every logged path carries the user's folder name, and a logged credential must never ship.
        internal sealed class Redactor
        {
            private readonly List<KeyValuePair<string, string>> _rules = new List<KeyValuePair<string, string>>();

            public Redactor(string profilePath, IEnumerable<string> secrets)
            {
                if (secrets != null)
                    foreach (string s in secrets)
                        if (!string.IsNullOrEmpty(s) && s.Length >= 12) _rules.Add(new KeyValuePair<string, string>(s, "[redacted]"));
                string p = (profilePath ?? "").TrimEnd('\\', '/');
                if (p.Length >= 4)
                {
                    _rules.Add(new KeyValuePair<string, string>(p.Replace("\\", "\\\\"), "~"));
                    _rules.Add(new KeyValuePair<string, string>(p, "~"));
                    _rules.Add(new KeyValuePair<string, string>(p.Replace('\\', '/'), "~"));
                }
            }

            internal static Redactor ForThisMachine()
            {
                var secrets = new List<string>();
                try
                {
                    string dir = Global.GlobalHttp.BaseUrl;
                    secrets.Add(Features.Account.KmhAccountStore.TokenFor(dir));
                    secrets.Add(Features.Account.KmhAccountStore.StoredTokenFor(dir));
                }
                catch { }
                string profile = "";
                try { profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
                return new Redactor(profile, secrets);
            }

            public string Apply(string s)
            {
                if (string.IsNullOrEmpty(s)) return s;
                foreach (KeyValuePair<string, string> rule in _rules) s = ReplaceIgnoreCase(s, rule.Key, rule.Value);
                return s;
            }

            private static string ReplaceIgnoreCase(string s, string find, string with)
            {
                int at = s.IndexOf(find, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return s;
                var sb = new StringBuilder(s.Length);
                int from = 0;
                while (at >= 0)
                {
                    sb.Append(s, from, at - from).Append(with);
                    from = at + find.Length;
                    at = s.IndexOf(find, from, StringComparison.OrdinalIgnoreCase);
                }
                return sb.Append(s, from, s.Length - from).ToString();
            }
        }

        private static string RuntimeName()
        {
            try
            {
                Type mono = Type.GetType("Mono.Runtime");
                var name = mono?.GetMethod("GetDisplayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (name != null) return "Mono " + name.Invoke(null, null);
            }
            catch { }
            return ".NET " + Environment.Version;
        }

        private static string Safe(Func<string> read)
        {
            try { return read() ?? ""; }
            catch (Exception ex) { return "unavailable (" + ex.GetType().Name + ")"; }
        }

        private static double Round(float v) => Math.Round(v, 1);

        internal static string Cap(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s.Substring(0, max) + "…";
    }
}
