using System;
using System.Collections;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Reports
{
    // Taken at the end of the click's frame, before the report window covers the game.
    internal static class KmhScreenshot
    {
        internal const int MaxWidth = 1600;
        private static readonly int[] Qualities = { 80, 65, 50, 35 };

        public static void Capture(Action<byte[], string> done)
        {
            Root root = Find.Root;
            if (root == null) { done?.Invoke(null, "the game is not ready to take one"); return; }
            root.StartCoroutine(AtEndOfFrame(done));
        }

        private static IEnumerator AtEndOfFrame(Action<byte[], string> done)
        {
            yield return new WaitForEndOfFrame();
            byte[] jpg = null;
            string why = null;
            Texture2D full = null, small = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                (int w, int h) = ScaledSize(full.width, full.height, MaxWidth);
                Texture2D source = full;
                if (w != full.width)
                {
                    RenderTexture rt = RenderTexture.GetTemporary(w, h, 0);
                    RenderTexture previous = RenderTexture.active;
                    try
                    {
                        Graphics.Blit(full, rt);
                        RenderTexture.active = rt;
                        small = new Texture2D(w, h, TextureFormat.RGB24, false);
                        small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        small.Apply();
                        source = small;
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        RenderTexture.ReleaseTemporary(rt);
                    }
                }
                foreach (int quality in Qualities)
                {
                    byte[] encoded = ImageConversion.EncodeToJPG(source, quality);
                    if (encoded != null && encoded.Length <= ReportLimits.MaxScreenshotBytes) { jpg = encoded; break; }
                }
                if (jpg == null) why = "it was too large to attach even at low quality";
            }
            catch (Exception ex)
            {
                why = ex.Message;
                KmhLog.Warn($"Report: the screenshot could not be taken: {ex.Message}");
            }
            finally
            {
                if (full != null) UnityEngine.Object.Destroy(full);
                if (small != null) UnityEngine.Object.Destroy(small);
            }
            try { done?.Invoke(jpg, why); }
            catch (Exception ex) { KmhLog.Warn($"Report: opening the report window failed: {ex.Message}"); }
        }

        internal static (int Width, int Height) ScaledSize(int width, int height, int maxWidth)
        {
            if (width <= 0 || height <= 0) return (Math.Max(1, width), Math.Max(1, height));
            if (width <= maxWidth) return (width, height);
            int h = (int)Math.Round(height * (maxWidth / (double)width));
            return (maxWidth, Math.Max(1, h));
        }
    }
}
