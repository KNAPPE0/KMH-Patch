using System;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch
{
    // Base for every KMH dialog. Wraps content drawing so a single exception can never blank the window, crash the
    // game, or flood the log every frame: it logs once per open and draws a readable fallback instead. Subclasses
    // override DrawContents rather than DoWindowContents
    //
    // Lives in the root KMHPatch namespace so every dialog (KMHPatch.UI, KMHPatch.Features.*, KMHPatch.Dialogs,
    // ...) sees it without a using
    public abstract class Window_KMHBase : Window
    {
        private bool _loggedThisOpen;

        protected abstract void DrawContents(Rect inRect);

        public sealed override void DoWindowContents(Rect inRect)
        {
            // Snapshot the text state so a half-finished draw can't leak an anchor/font into the rest of the
            // frame's UI
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
