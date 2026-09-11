using System;
using KMHPatch.Diagnostics;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch
{
    // A draw that throws logs once and shows a fallback, rather than taking the window down with it.
    public abstract class Window_KMHBase : Window
    {
        private bool _loggedThisOpen;
        private bool _claimedAccept;
        private readonly int _openedInSession = SubProtocol.KmhDispatcher.SessionGeneration;

        // A window holding another session's data or approval must not act against the next connection.
        protected virtual bool ClosesOnSessionEnd => true;

        protected abstract void DrawContents(Rect inRect);

        private Action   _autoRefresh;
        private float    _refreshTimer;
        // Marked on RECEIPT, never on send, or a screen reads "live" while no snapshot has ever arrived.
        private DateTime _lastRefreshUtc = DateTime.MinValue;
        private DateTime _firstRequestUtc = DateTime.MinValue;

        // How long a screen waits for its first snapshot before saying so instead of claiming to be loading.
        private const int NoResponseSeconds = 15;

        protected void EnableAutoRefresh(Action requestSnapshot)
        {
            _autoRefresh     = requestSnapshot;
            _refreshTimer    = DialogLayout.AutoRefreshSeconds;
            _firstRequestUtc = DateTime.UtcNow;
            try { requestSnapshot?.Invoke(); } catch (Exception ex) { KmhLog.Warn($"{GetType().Name} auto-refresh initial request threw: {ex.Message}"); }
        }

        // Call ONLY from a feature cache's Updated event - never after merely sending a request.
        protected void MarkRefreshed() => _lastRefreshUtc = DateTime.UtcNow;

        protected bool HasReceivedData => _lastRefreshUtc != DateTime.MinValue;

        // True once a first snapshot is overdue, so a screen can say what happened instead of loading forever.
        protected bool WaitedTooLong => !HasReceivedData && _firstRequestUtc != DateTime.MinValue
                                        && (DateTime.UtcNow - _firstRequestUtc).TotalSeconds >= NoResponseSeconds;

        protected int SecondsSinceRefresh
            => HasReceivedData ? Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds) : 0;

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            HoldCameraWhileHovered();
            ClaimAcceptKeyWhileTyping();
            HoldMinimumSize();
            if (CloseIfSessionEnded()) return;
            if (_autoRefresh == null) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                try { _autoRefresh(); } catch (Exception ex) { KmhLog.Warn($"{GetType().Name} auto-refresh threw: {ex.Message}"); }
            }
        }

        private bool CloseIfSessionEnded()
        {
            if (!ClosesOnSessionEnd || SubProtocol.KmhDispatcher.SessionGeneration == _openedInSession) return false;
            Close(false);
            return true;
        }

        // RimWorld's camera eats the scroll wheel first, so a non-absorbing window's scroll views never see one.
        private void HoldCameraWhileHovered()
        {
            if (absorbInputAroundWindow) return;
            try { preventCameraMotion = Find.WindowStack?.GetWindowAt(Verse.UI.MousePositionOnUIInverted) == this; }
            catch { }
        }

        // True while a text field in this window holds the keyboard. Windows with one override it.
        protected virtual bool TypingInThisWindow => false;

        // RimWorld gives Enter to the topmost window that closes on it, skipping any that do not - so it must stop here.
        private void ClaimAcceptKeyWhileTyping()
        {
            bool typing = TypingInThisWindow;
            if (typing == _claimedAccept) return;   // windows without a text field never reach the assignment
            _claimedAccept = typing;
            closeOnAccept  = typing;
        }

        public override void OnAcceptKeyPressed()
        {
            if (_claimedAccept) return;
            base.OnAcceptKeyPressed();
        }

        // Clearing the SAVED placement is the caller's job; only it knows which settings are its own.
        protected void CentreAt(float w, float h)
        {
            float sw = Verse.UI.screenWidth, sh = Verse.UI.screenHeight;
            w = Mathf.Min(w, Mathf.Max(100f, sw - 40f));
            h = Mathf.Min(h, Mathf.Max(100f, sh - 40f));
            windowRect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
        }

        // RimWorld's resizer does not know a layout's minimum, so a window can be dragged down to a sliver.
        protected Vector2 minSize;

        private void HoldMinimumSize()
        {
            if (minSize.x <= 0f && minSize.y <= 0f) return;
            if (windowRect.width >= minSize.x && windowRect.height >= minSize.y) return;
            windowRect.width  = Mathf.Max(windowRect.width,  minSize.x);
            windowRect.height = Mathf.Max(windowRect.height, minSize.y);
        }

        // A remembered size, never smaller than the window can draw itself at nor bigger than the screen.
        protected static Vector2 SizeWithin(float savedW, float savedH, float minW, float minH)
            => new Vector2(Mathf.Clamp(savedW, minW, Mathf.Max(minW, Verse.UI.screenWidth  - 40f)),
                           Mathf.Clamp(savedH, minH, Mathf.Max(minH, Verse.UI.screenHeight - 40f)));

        // Negative means never placed; a window saved on a monitor since detached still comes back clickable.
        protected void PlaceAt(float savedX, float savedY)
        {
            if (savedX < 0f && savedY < 0f) return;
            windowRect.x = Mathf.Clamp(savedX, 0f, Mathf.Max(0f, Verse.UI.screenWidth  - windowRect.width));
            windowRect.y = Mathf.Clamp(savedY, 0f, Mathf.Max(0f, Verse.UI.screenHeight - windowRect.height));
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
