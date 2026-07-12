using System;
using KMHPatch.Diagnostics;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch
{
    // Safe base for KMH dialogs. If drawing breaks, it logs once and shows a fallback instead of killing the window.
    public abstract class Window_KMHBase : Window
    {
        private bool _loggedThisOpen;

        protected abstract void DrawContents(Rect inRect);

        // --- optional auto-refresh + live badge (opt-in) ---
        // Every list dialog used to hand-roll the same _refreshTimer / _lastRefreshUtc / WindowUpdate / OnUpdated
        // scaffold. Opt in from the ctor with EnableAutoRefresh(feature.RequestSnapshot), subscribe the cache's
        // Updated event to MarkRefreshed, and draw DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh). Dialogs that
        // don't call EnableAutoRefresh are completely unaffected.
        private Action   _autoRefresh;
        private float    _refreshTimer;
        private DateTime _lastRefreshUtc = DateTime.UtcNow;

        // Wire a periodic snapshot request (fired once now, then every DialogLayout.AutoRefreshSeconds).
        protected void EnableAutoRefresh(Action requestSnapshot)
        {
            _autoRefresh    = requestSnapshot;
            _refreshTimer   = DialogLayout.AutoRefreshSeconds;
            _lastRefreshUtc = DateTime.UtcNow;
            try { requestSnapshot?.Invoke(); } catch (Exception ex) { KmhLog.Warn($"{GetType().Name} auto-refresh initial request threw: {ex.Message}"); }
        }

        // Reset the "live · Ns ago" badge - subscribe the feature cache's Updated event to this, and call it after a
        // manual Refresh button.
        protected void MarkRefreshed() => _lastRefreshUtc = DateTime.UtcNow;

        // Seconds since the last snapshot, for DialogLayout.DrawLiveBadge.
        protected int SecondsSinceRefresh => Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds);

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (_autoRefresh == null) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                try { _autoRefresh(); } catch (Exception ex) { KmhLog.Warn($"{GetType().Name} auto-refresh threw: {ex.Message}"); }
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        public sealed override void DoWindowContents(Rect inRect)
        {
            // Snapshot text state so a broken draw can't leak anchor/font settings into the rest of the UI.
            TextAnchor anchor = Text.Anchor;
            GameFont   font   = Text.Font;
            try
            {
                DrawContents(inRect);
            }
            catch (Exception ex)
            {
                Text.Anchor = anchor;
                Text.Font   = font;
                if (!_loggedThisOpen)
                {
                    _loggedThisOpen = true; // once per open - don't flood the log at 60fps
                    KmhLog.Error($"{GetType().Name}.DrawContents threw: {ex}");
                }
                DrawError(inRect);
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            _loggedThisOpen = false;
        }

        private static void DrawError(Rect inRect)
        {
            GameFont   font   = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Text.Font   = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(inRect,
                "<color=#ff8080>This KMH panel hit an error and couldn't finish drawing.</color>\n\n" +
                "It's safe to close and reopen it. Details are in the KMH log " +
                "(Mods → KMH Patch settings → Open KMH log folder).");
            Text.Anchor = anchor;
            Text.Font   = font;
        }
    }
}
