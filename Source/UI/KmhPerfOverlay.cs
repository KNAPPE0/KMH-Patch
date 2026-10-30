using System.Collections.Generic;
using System.Globalization;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    internal static class KmhPerfOverlay
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static long _lastIn = -1, _lastOut, _lastStamp;
        private static double _inPerSec, _outPerSec;

        private static void SampleRates()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long bytesIn = KmhClientTransportStats.BytesIn, bytesOut = KmhClientTransportStats.BytesOut;
            if (_lastIn < 0) { _lastIn = bytesIn; _lastOut = bytesOut; _lastStamp = now; return; }
            double seconds = (now - _lastStamp) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (seconds < 1.0) return;
            _inPerSec  = (bytesIn - _lastIn) / seconds;
            _outPerSec = (bytesOut - _lastOut) / seconds;
            _lastIn = bytesIn; _lastOut = bytesOut; _lastStamp = now;
        }

        internal static string Line(KmhPerfSampler.Snapshot p, double inPerSec, double outPerSec)
        {
            if (p == null || p.FramesSampled == 0) return "Colony Nexus · measuring…";
            string s = $"{p.FpsAvg.ToString("0", Inv)} fps · 1% low {p.FpsLow1.ToString("0", Inv)}";
            if (p.Tps >= 0f)
                s += p.TpsTarget <= 0f ? " · paused" : $" · {p.Tps.ToString("0", Inv)}/{p.TpsTarget.ToString("0", Inv)} TPS";
            s += $" · Colony Nexus {p.KmhMsPerFrame.ToString("0.00", Inv)} ms";
            if (KmhDispatcher.IsKmhServer) s += $" · ↓{Rate(inPerSec)} ↑{Rate(outPerSec)}";
            return s;
        }

        internal static List<string> PanelLines(KmhPerfSampler.Snapshot p, IList<KeyValuePair<string, long>> net, int backlog)
        {
            var lines = new List<string>();
            if (p == null || p.FramesSampled == 0) { lines.Add("Measuring - numbers appear after a few frames."); return lines; }

            lines.Add($"Frame rate: {p.FpsAvg.ToString("0", Inv)} fps on average, {p.FpsLow1.ToString("0", Inv)} fps in the slowest 1% "
                    + $"of frames (frame time: typical {p.FrameMsP50.ToString("0.0", Inv)} ms, 95% under {p.FrameMsP95.ToString("0.0", Inv)} ms, "
                    + $"worst {p.FrameMsWorst.ToString("0.0", Inv)} ms - over the last {p.FramesSampled} frames).");

            if (p.Tps < 0f) lines.Add("Game speed: no game is loaded.");
            else if (p.TpsTarget <= 0f) lines.Add("Game speed: paused.");
            else
            {
                float share = p.TpsTarget > 0f ? p.Tps / p.TpsTarget * 100f : 0f;
                lines.Add($"Game speed: {p.Tps.ToString("0", Inv)} ticks per second of a {p.TpsTarget.ToString("0", Inv)} target "
                        + $"({share.ToString("0", Inv)}%)." + (share < 90f ? " The game is running slower than the speed you picked." : ""));
            }

            double frameMs = p.FpsAvg > 0f ? 1000.0 / p.FpsAvg : 0.0;
            string busiest = p.Steps.Count > 0 && p.Steps[0].AvgMs > 0.005
                ? $" - busiest: {p.Steps[0].Step} at {p.Steps[0].AvgMs.ToString("0.00", Inv)} ms" : "";
            lines.Add($"Colony Nexus's own work each frame: {p.KmhMsPerFrame.ToString("0.00", Inv)} ms"
                    + (frameMs > 0 ? $" ({(p.KmhMsPerFrame / frameMs * 100.0).ToString("0.0", Inv)}% of a frame)" : "") + busiest + ".");

            long Get(string k) { foreach (var kv in net) if (kv.Key == k) return kv.Value; return 0; }
            long bytesOut = Get("bytes_out"), rawOut = Get("bytes_out_uncompressed");
            string saved = rawOut > 0 && bytesOut < rawOut ? $"; compression saved {(100 - bytesOut * 100 / rawOut)}% of what was sent" : "";
            lines.Add($"Network this session: received {Size(Get("bytes_in"))} in {Get("frames_in"):N0} frames, sent "
                    + $"{Size(bytesOut)} in {Get("frames_out"):N0} frames{saved}.");
            lines.Add($"Waiting for the game thread: {backlog} message(s) now, {Get("main_thread_backlog_peak"):N0} at most this session.");
            lines.Add($"Game-thread queue: oldest waiting {Get("main_thread_oldest_ms"):N0} ms, longest wait {Get("main_thread_wait_peak_ms"):N0} ms; "
                    + $"{Get("main_thread_coalesced"):N0} outdated snapshot(s) skipped, {Get("main_thread_frames_capped"):N0} frame(s) hit the per-frame cap, "
                    + $"{Get("main_thread_overloads"):N0} overload reset(s).");
            long failures = Get("main_thread_failures");
            if (failures > 0)
                lines.Add($"Game-thread failures: {failures:N0}; last: {KmhDisplayText.Inert(KmhMainThread.LastFailure ?? "", 200)}.");
            return lines;
        }

        public static void Draw()
        {
            SampleRates();
            string line = Line(KmhPerfSampler.Read(), _inPerSec, _outPerSec);

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Vector2 size = Text.CalcSize(line);
                Rect box = new Rect(6f, 6f, size.x + 12f, size.y + 6f);
                Widgets.DrawBoxSolid(box, new Color(0.08f, 0.09f, 0.10f, 0.78f));
                GUI.color = new Color(0.90f, 0.92f, 0.88f);
                Widgets.Label(new Rect(box.x + 6f, box.y, box.width - 6f, box.height), line);
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }

        internal static string Size(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", Inv) + " GB";
            if (bytes >= 1024L * 1024) return (bytes / (1024.0 * 1024)).ToString("0.0", Inv) + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0", Inv) + " KB";
            return bytes + " B";
        }

        private static string Rate(double bytesPerSecond) => Size((long)bytesPerSecond) + "/s";
    }
}
