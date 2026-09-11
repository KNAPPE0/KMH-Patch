using UnityEngine;

namespace KMHPatch.UI
{
    internal static class KmhScroll
    {
        // A scroll view's viewRect must be IDENTICAL on every event pass, or IMGUI desyncs and the panel stays blank.
        public static bool NewFrame(ref int lastFrame) => NewFrame(ref lastFrame, Time.frameCount);

        // Pure overload so the once-per-frame rule can be proven without a running game.
        public static bool NewFrame(ref int lastFrame, int currentFrame)
        {
            if (lastFrame == currentFrame) return false;
            lastFrame = currentFrame;
            return true;
        }

        // Never negative: content shorter than its viewport cannot scroll at all.
        public static float MaxOffset(float contentH, float viewportH) => Mathf.Max(0f, contentH - viewportH);

        // Collapsing a section shrinks content under a live offset, leaving the view scrolled past the end.
        public static float Clamp(float scrollY, float contentH, float viewportH)
        {
            float max = MaxOffset(contentH, viewportH);
            return scrollY < 0f ? 0f : (scrollY > max ? max : scrollY);
        }
    }
}
