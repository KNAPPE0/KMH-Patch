using System;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch
{
    // Safe base for KMH dialogs. If drawing breaks, it logs once and shows a fallback instead of killing the window.
    public abstract class Window_KMHBase : Window
    {
        private bool _loggedThisOpen;

        protected abstract void DrawContents(Rect inRect);

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
