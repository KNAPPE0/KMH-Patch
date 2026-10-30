using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Admin
{
    internal sealed class ConfigField
    {
        public string Path = "";
        public string Type = "";
        public string Value = "";
        public string Default = "";
        public bool   ReadOnly;
        public bool   Secret;

        public bool Editable => !ReadOnly && !Secret;
    }

    // The server is the authority on every value; this only remembers its last answer so the dialog can draw.
    internal static class ServerConfigCache
    {
        private static readonly object _lock = new object();
        private static readonly List<string> _areas = new List<string>();
        private static readonly Dictionary<string, List<ConfigField>> _fields =
            new Dictionary<string, List<ConfigField>>(StringComparer.OrdinalIgnoreCase);

        public static event Action Updated;
        public static string LastResult = "";
        public static bool   LastResultOk;
        // Bumped on every answer; without it a fresh refusal is indistinguishable from an earlier success.
        public static int    ResultSerial;

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ConfigSchema,  OnSchema);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ConfigApplied, OnApplied);
        }

        public static List<string> Areas()
        {
            lock (_lock) return new List<string>(_areas);
        }

        public static List<ConfigField> Fields(string area)
        {
            lock (_lock)
                return _fields.TryGetValue(area ?? "", out List<ConfigField> f)
                     ? new List<ConfigField>(f) : null;
        }

        public static void Clear()
        {
            lock (_lock) { _areas.Clear(); _fields.Clear(); }
            LastResult = ""; LastResultOk = false; ResultSerial++;
        }

        public static void RequestAreas() => KmhDispatcher.Send(KmhProtocol.Kind.ConfigRequest, new { area = "" });
        public static void RequestArea(string area) => KmhDispatcher.Send(KmhProtocol.Kind.ConfigRequest, new { area });

        public static bool Apply(string area, Dictionary<string, string> edits)
            => KmhDispatcher.Send(KmhProtocol.Kind.ConfigApply, new { area, edits });

        public static bool RequestRestart()
            => KmhDispatcher.Send(KmhProtocol.Kind.ConfigRestart, new { });

        private static void OnSchema(KmhEnvelope env)
        {
            if (env == null) return;
            string area = env.GetString("area") ?? "";
            if (string.IsNullOrEmpty(area))
            {
                string[] areas = env.GetStringArray("areas") ?? new string[0];
                lock (_lock) { _areas.Clear(); _areas.AddRange(areas); }
                Raise();
                return;
            }

            var parsed = new List<ConfigField>();
            try
            {
                Newtonsoft.Json.Linq.JArray rows = env.Data?["fields"] as Newtonsoft.Json.Linq.JArray;
                if (rows != null)
                    foreach (Newtonsoft.Json.Linq.JToken t in rows)
                    {
                        if (!(t is Newtonsoft.Json.Linq.JObject o)) continue;
                        parsed.Add(new ConfigField
                        {
                            Path     = (string)o["path"] ?? "",
                            Type     = (string)o["type"] ?? "text",
                            Value    = (string)o["value"] ?? "",
                            Default  = (string)o["def"] ?? "",
                            ReadOnly = (bool?)o["ro"] ?? false,
                            Secret   = (bool?)o["secret"] ?? false,
                        });
                    }
            }
            catch (Exception ex) { KmhLog.Warn($"Server settings: could not read the schema ({ex.GetType().Name})."); }

            lock (_lock) _fields[area] = parsed;
            Raise();
        }

        private static void OnApplied(KmhEnvelope env)
        {
            bool ok = env?.GetBool("ok") ?? false;
            // The server says live or restart-needed; a blanket "Saved." would make a restart-only change read as applied.
            string sent = env?.GetString("reason") ?? "";
            string reason = sent.Length > 0 ? sent : (ok ? "Saved." : "Refused.");
            string area = env?.GetString("area") ?? "";

            // Re-read, not trusted: the server clamps on reload, so what it holds may differ from what was typed.
            if (ok && area.Length > 0) RequestArea(area);

            // Handlers run on the network thread while the dialog draws, so only the main thread ever touches these.
            KmhMainThread.Post(() =>
            {
                LastResultOk = ok;
                LastResult   = reason;
                ResultSerial++;
                try { Updated?.Invoke(); } catch (Exception) { }
            });
        }

        private static void Raise()
        {
            KmhMainThread.Post(() => { try { Updated?.Invoke(); } catch (Exception) { } });
        }
    }
}
