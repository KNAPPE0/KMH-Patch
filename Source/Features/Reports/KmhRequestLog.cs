using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using KMHPatch.SubProtocol;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Reports
{
    // The last KMH requests and what answered them: kinds, timings and result codes only, never content.
    internal sealed class KmhRequestLog
    {
        internal const int Kept = 20;
        internal const double QuietSeconds = 30;

        internal static readonly KmhRequestLog Live = new KmhRequestLog();

        private sealed class Row
        {
            public string Kind = "", Family = "", OpId = "", AnsweredBy = "", Code = "";
            public DateTime SentLocal;
            public long SentTicks;
            public long Ms = -1;
            public bool Delivered;
            public bool? Ok;
        }

        private readonly object _lock = new object();
        private readonly List<Row> _rows = new List<Row>(Kept + 1);
        // Every received envelope passes through here, so with nothing awaited it costs one read.
        private int _awaited;

        // Heartbeats and pushes that never get an answer would crowd out the requests worth reading.
        internal static bool Tracked(string kind)
            => !string.IsNullOrEmpty(kind) && kind != KmhProtocol.Kind.Ping && kind != KmhProtocol.Kind.Pong
            && kind != KmhProtocol.Kind.Hello && kind != KmhProtocol.Kind.HelloAck && kind != KmhProtocol.Kind.DebugLog
            && kind != KmhProtocol.Kind.Notice && kind != KmhProtocol.Kind.OpResult && !KmhFragments.IsFragment(kind);

        // "kmh.marketplace.buy" is answered by "kmh.marketplace.snapshot": the first two parts name the feature.
        internal static string Family(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "";
            int first = kind.IndexOf('.');
            if (first < 0) return kind;
            int second = kind.IndexOf('.', first + 1);
            return second < 0 ? kind : kind.Substring(0, second);
        }

        public void Sent(string kind, string opId, bool delivered) => Sent(kind, opId, delivered, DateTime.Now, Stopwatch.GetTimestamp());

        internal void Sent(string kind, string opId, bool delivered, DateTime local, long ticks)
        {
            if (!Tracked(kind)) return;
            lock (_lock)
            {
                _rows.Add(new Row { Kind = kind, Family = Family(kind), OpId = opId ?? "", SentLocal = local, SentTicks = ticks, Delivered = delivered });
                if (_rows.Count > Kept) _rows.RemoveAt(0);
                int awaited = 0;
                foreach (Row r in _rows) if (r.Ms < 0 && r.Delivered) awaited++;
                _awaited = awaited;
            }
        }

        public void Received(KmhEnvelope env) => Received(env, Stopwatch.GetTimestamp());

        internal void Received(KmhEnvelope env, long ticks)
        {
            if (env == null || string.IsNullOrEmpty(env.Kind) || System.Threading.Volatile.Read(ref _awaited) == 0) return;
            lock (_lock)
            {
                Row hit = null;
                if (env.Kind == KmhProtocol.Kind.OpResult)
                {
                    string op = env.GetString("op") ?? "";
                    if (op.Length == 0) return;
                    foreach (Row r in _rows) if (r.Ms < 0 && r.Delivered && r.OpId == op) { hit = r; break; }
                }
                else if (Tracked(env.Kind))
                {
                    string family = Family(env.Kind);
                    // The oldest waiting one: a server answers its requests in the order they came.
                    foreach (Row r in _rows) if (r.Ms < 0 && r.Delivered && r.Family == family) { hit = r; break; }
                }
                if (hit == null) return;
                _awaited--;
                hit.Ms = Math.Max(0L, (ticks - hit.SentTicks) * 1000L / Stopwatch.Frequency);
                hit.AnsweredBy = env.Kind;
                if (env.Data?["ok"] is JValue ok && ok.Type == JTokenType.Boolean) hit.Ok = (bool)ok;
                hit.Code = CodeOf(env.GetString("reason")) ?? CodeOf(env.GetString("code")) ?? "";
            }
        }

        private static readonly Regex CodeShape = new Regex(@"^[A-Za-z0-9_.\-]{1,40}$");

        // A machine word only: a free-text reason is somebody's sentence, and stays out.
        internal static string CodeOf(string s) => !string.IsNullOrEmpty(s) && CodeShape.IsMatch(s) ? s : null;

        public JArray Snapshot() => Snapshot(Stopwatch.GetTimestamp());

        internal JArray Snapshot(long nowTicks)
        {
            var arr = new JArray();
            lock (_lock)
            {
                foreach (Row r in _rows)
                {
                    var o = new JObject
                    {
                        ["kind"] = r.Kind,
                        ["sent"] = r.SentLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    };
                    if (!r.Delivered) o["result"] = "not sent";
                    else if (r.Ms >= 0)
                    {
                        o["result"] = "answered";
                        o["answered_by"] = r.AnsweredBy;
                        o["ms"] = r.Ms;
                        if (r.Ok.HasValue) o["ok"] = r.Ok.Value;
                        if (r.Code.Length > 0) o["code"] = r.Code;
                    }
                    else
                    {
                        double waited = (nowTicks - r.SentTicks) / (double)Stopwatch.Frequency;
                        o["result"] = waited < QuietSeconds ? "waiting" : "no reply seen";
                        o["seconds_since"] = (long)waited;
                    }
                    arr.Add(o);
                }
            }
            return arr;
        }

        internal int Count { get { lock (_lock) return _rows.Count; } }
    }
}
