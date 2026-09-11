using System;
using System.Collections.Generic;
using UnityEngine;

namespace KMHPatch.Features.Chat
{
    // A GIF decoder, because Unity has none: frames composite onto a persistent canvas, since a frame is usually a patch.
    internal static class KmhGif
    {
        // A 500-frame 800x600 gif is 960MB of Texture2D. Shared with the APNG decoder: two copies of a bound drift apart.
        internal const int MaxFrames = 120;
        internal const int MaxPixels = 1920 * 1080;
        internal const int MaxTotalDecodedPixels = 40 * 1024 * 1024;

        internal sealed class Frame
        {
            public Texture2D Texture;
            public float     DelaySeconds;
        }

        // Split from the texture because building one needs a graphics device: everything hard stays checkable headless.
        internal sealed class RawFrame
        {
            public Color32[] Pixels;         // top-down, logical screen size
            public float     DelaySeconds;
        }

        internal sealed class RawAnimation
        {
            public readonly List<RawFrame> Frames = new List<RawFrame>();
            public int Width, Height;
        }

        public sealed class Animation
        {
            public readonly List<Frame> Frames = new List<Frame>();
            public int   Width, Height;
            public float TotalSeconds;

            public bool IsAnimated => Frames.Count > 1;

            // Constant-time enough for a per-frame call: the list is short and the scan stops at the first frame past t.
            public Texture2D FrameAt(float t)
            {
                if (Frames.Count == 0) return null;
                if (Frames.Count == 1 || TotalSeconds <= 0f) return Frames[0].Texture;
                float at = t % TotalSeconds;
                for (int i = 0; i < Frames.Count; i++)
                {
                    at -= Frames[i].DelaySeconds;
                    if (at <= 0f) return Frames[i].Texture;
                }
                return Frames[Frames.Count - 1].Texture;
            }

            public void Dispose()
            {
                foreach (Frame f in Frames)
                {
                    if (f?.Texture == null) continue;
                    try { UnityEngine.Object.Destroy(f.Texture); } catch { }
                    f.Texture = null;
                }
                Frames.Clear();
            }
        }

        public static bool LooksLikeGif(byte[] data)
            => data != null && data.Length > 6
            && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8'
            && (data[4] == '7' || data[4] == '9') && data[5] == 'a';

        // Returns null on anything malformed. Never throws - this is fed by the internet.
        public static Animation Decode(byte[] d)
        {
            try
            {
                RawAnimation raw = DecodeFrames(d);
                if (raw == null || raw.Frames.Count == 0) return null;

                var anim = new Animation { Width = raw.Width, Height = raw.Height };
                foreach (RawFrame f in raw.Frames)
                    anim.Frames.Add(new Frame
                    {
                        Texture = ToTexture(f.Pixels, raw.Width, raw.Height),
                        DelaySeconds = f.DelaySeconds,
                    });
                anim.TotalSeconds = 0f;
                foreach (Frame f in anim.Frames) anim.TotalSeconds += f.DelaySeconds;
                return anim;
            }
            catch (Exception ex)
            {
                Diagnostics.KmhLog.Debug($"GIF decode failed: {ex.Message}");
                return null;
            }
        }

        // The whole format, with no Unity object in sight. Never throws.
        internal static RawAnimation DecodeFrames(byte[] d)
        {
            try { return DecodeCore(d); }
            catch (Exception ex)
            {
                Diagnostics.KmhLog.Debug($"GIF decode failed: {ex.Message}");
                return null;
            }
        }

        private static RawAnimation DecodeCore(byte[] d)
        {
            if (!LooksLikeGif(d)) return null;
            int p = 6;

            int width  = Read16(d, ref p);
            int height = Read16(d, ref p);
            if (width <= 0 || height <= 0 || (long)width * height > MaxPixels) return null;

            byte packed = d[p++];
            p++;   // background colour index - unused: KMH composites onto transparency, not onto a background
            p++;   // pixel aspect ratio

            Color32[] globalTable = null;
            if ((packed & 0x80) != 0)
            {
                int size = 2 << (packed & 0x07);
                globalTable = ReadTable(d, ref p, size);
            }

            var anim = new RawAnimation { Width = width, Height = height };

            // The canvas a frame is composited onto, and the copy kept for disposal method 3 ("restore to previous").
            var canvas = new Color32[width * height];
            var previous = new Color32[width * height];

            int  delayCs = 10;          // 100ms, the browser-compatible default for a gif that specifies nothing
            int  transparentIndex = -1;
            int  disposal = 0;
            long decodedPixels = 0;

            while (p < d.Length)
            {
                byte block = d[p++];

                if (block == 0x3B) break;             // trailer

                if (block == 0x21)                    // extension
                {
                    byte label = d[p++];
                    if (label == 0xF9)                // graphic control
                    {
                        int len = d[p++];             // always 4, but trust the file's own length
                        int end = p + len;
                        byte gcPacked = d[p];
                        disposal = (gcPacked >> 2) & 0x07;
                        transparentIndex = (gcPacked & 0x01) != 0 ? d[p + 3] : -1;
                        delayCs = d[p + 1] | (d[p + 2] << 8);
                        p = end;
                        SkipSubBlocks(d, ref p);
                    }
                    else SkipSubBlocks(d, ref p);     // application / comment / plain text - nothing KMH needs
                    continue;
                }

                if (block != 0x2C) break;             // not an image descriptor: the file is malformed, stop cleanly

                int fx = Read16(d, ref p), fy = Read16(d, ref p);
                int fw = Read16(d, ref p), fh = Read16(d, ref p);
                byte imgPacked = d[p++];

                Color32[] local = null;
                if ((imgPacked & 0x80) != 0)
                {
                    int size = 2 << (imgPacked & 0x07);
                    local = ReadTable(d, ref p, size);
                }
                bool interlaced = (imgPacked & 0x40) != 0;
                Color32[] table = local ?? globalTable;

                byte[] indices = LzwDecode(d, ref p, fw * fh);
                if (indices == null || table == null) return anim.Frames.Count > 0 ? anim : null;

                // Disposal 3 keeps a copy to restore AFTER this frame is shown.
                if (disposal == 3) Array.Copy(canvas, previous, canvas.Length);

                Compose(canvas, width, height, indices, fx, fy, fw, fh, table, transparentIndex, interlaced);

                decodedPixels += (long)width * height;
                if (anim.Frames.Count >= MaxFrames || decodedPixels > MaxTotalDecodedPixels)
                {
                    Diagnostics.KmhLog.Debug("GIF: hit the frame/pixel cap - showing what decoded so far.");
                    break;
                }

                // Copied, not referenced: the canvas keeps mutating, so sharing it would leave every frame showing the last.
                var snapshot = new Color32[canvas.Length];
                Array.Copy(canvas, snapshot, canvas.Length);
                anim.Frames.Add(new RawFrame
                {
                    Pixels = snapshot,
                    // 0 and 1 hundredths mean 'as fast as possible'; browsers clamp to 100ms and so do we.
                    DelaySeconds = (delayCs <= 1 ? 10 : delayCs) / 100f,
                });

                if (disposal == 2)                    // restore to background = clear this frame's rect
                    ClearRect(canvas, width, height, fx, fy, fw, fh);
                else if (disposal == 3)
                    Array.Copy(previous, canvas, canvas.Length);

                // Per-frame control applies to the NEXT image block only; reset so it cannot leak forward.
                transparentIndex = -1; disposal = 0; delayCs = 10;
            }

            return anim.Frames.Count > 0 ? anim : null;
        }

        private static void Compose(Color32[] canvas, int cw, int ch, byte[] idx,
                                    int fx, int fy, int fw, int fh, Color32[] table, int transparent, bool interlaced)
        {
            // Interlaced gifs store rows in four passes rather than top to bottom.
            int[] rowMap = interlaced ? InterlaceRows(fh) : null;

            for (int row = 0; row < fh; row++)
            {
                int destRow = fy + (rowMap != null ? rowMap[row] : row);
                if (destRow < 0 || destRow >= ch) continue;

                for (int col = 0; col < fw; col++)
                {
                    int destCol = fx + col;
                    if (destCol < 0 || destCol >= cw) continue;

                    int i = row * fw + col;
                    if (i >= idx.Length) return;
                    int ci = idx[i];
                    if (ci == transparent) continue;          // transparent pixels leave the canvas as it was
                    if (ci < 0 || ci >= table.Length) continue;

                    canvas[destRow * cw + destCol] = table[ci];
                }
            }
        }

        private static int[] InterlaceRows(int h)
        {
            var map = new int[h];
            int n = 0;
            for (int y = 0; y < h; y += 8) map[n++] = y;
            for (int y = 4; y < h; y += 8) map[n++] = y;
            for (int y = 2; y < h; y += 4) map[n++] = y;
            for (int y = 1; y < h; y += 2) map[n++] = y;
            return map;
        }

        private static void ClearRect(Color32[] canvas, int cw, int ch, int x, int y, int w, int h)
        {
            var clear = new Color32(0, 0, 0, 0);
            for (int r = y; r < y + h && r < ch; r++)
                for (int c = x; c < x + w && c < cw; c++)
                    if (r >= 0 && c >= 0) canvas[r * cw + c] = clear;
        }

        // GIF rows run top-down and SetPixels32 expects bottom-up; flipping here is why frames are not upside down.
        private static Texture2D ToTexture(Color32[] canvas, int w, int h)
        {
            var flipped = new Color32[canvas.Length];
            for (int y = 0; y < h; y++)
                Array.Copy(canvas, y * w, flipped, (h - 1 - y) * w, w);

            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(flipped);
            t.Apply(false, false);
            return t;
        }

        private static Color32[] ReadTable(byte[] d, ref int p, int entries)
        {
            var table = new Color32[entries];
            for (int i = 0; i < entries; i++)
            {
                table[i] = new Color32(d[p], d[p + 1], d[p + 2], 255);
                p += 3;
            }
            return table;
        }

        private static void SkipSubBlocks(byte[] d, ref int p)
        {
            while (p < d.Length)
            {
                int len = d[p++];
                if (len == 0) return;
                p += len;
            }
        }

        private static int Read16(byte[] d, ref int p)
        {
            int v = d[p] | (d[p + 1] << 8);
            p += 2;
            return v;
        }

        // CONTRACT: p returns just past this image's sub-block chain - leaving the 0x00 makes every gif decode as one frame.
        private static byte[] LzwDecode(byte[] d, ref int p, int expected)
        {
            if (p >= d.Length || expected <= 0) return null;
            int minCodeSize = d[p++];
            if (minCodeSize < 1 || minCodeSize > 11) return null;

            int clearCode = 1 << minCodeSize;
            int endCode   = clearCode + 1;

            // 4096 is the format's hard ceiling on dictionary size.
            var prefix = new int[4096];
            var suffix = new byte[4096];
            var stack  = new byte[4096];

            var outBytes = new byte[expected];
            int outAt = 0;

            int codeSize = minCodeSize + 1;
            int next     = endCode + 1;
            int mask     = (1 << codeSize) - 1;
            int prev     = -1;

            int bitBuf = 0, bitCount = 0, subLeft = 0;

            while (true)
            {
                while (bitCount < codeSize)
                {
                    if (subLeft == 0)
                    {
                        if (p >= d.Length) { return Drain(d, ref p, ref subLeft, outAt > 0 ? outBytes : null); }
                        subLeft = d[p++];
                        if (subLeft == 0) return Drain(d, ref p, ref subLeft, outAt > 0 ? outBytes : null);   // block terminator
                    }
                    if (p >= d.Length) return Drain(d, ref p, ref subLeft, outAt > 0 ? outBytes : null);
                    bitBuf |= d[p++] << bitCount;
                    bitCount += 8;
                    subLeft--;
                }

                int code = bitBuf & mask;
                bitBuf >>= codeSize;
                bitCount -= codeSize;

                if (code == endCode) return Drain(d, ref p, ref subLeft, outBytes);
                if (code == clearCode)
                {
                    codeSize = minCodeSize + 1;
                    mask = (1 << codeSize) - 1;
                    next = endCode + 1;
                    prev = -1;
                    continue;
                }

                int cur = code;
                int sp  = 0;

                // LZW's one special case: a code not yet in the dictionary can only mean the previous sequence plus its first byte.
                if (code >= next)
                {
                    if (prev < 0) return Drain(d, ref p, ref subLeft, outAt > 0 ? outBytes : null);
                    stack[sp++] = FirstByte(prefix, suffix, prev, clearCode);
                    cur = prev;
                }

                int guard = 0;
                while (cur >= clearCode)
                {
                    if (++guard > 4096) return Drain(d, ref p, ref subLeft, outAt > 0 ? outBytes : null);   // corrupt prefix chain
                    stack[sp++] = suffix[cur];
                    cur = prefix[cur];
                }
                stack[sp++] = (byte)cur;

                while (sp > 0)
                {
                    if (outAt >= expected) return Drain(d, ref p, ref subLeft, outBytes);   // frame full
                    outBytes[outAt++] = stack[--sp];
                }

                if (prev >= 0 && next < 4096)
                {
                    prefix[next] = prev;
                    suffix[next] = (byte)cur;
                    next++;
                    if ((next & mask) == 0 && codeSize < 12)
                    {
                        codeSize++;
                        mask = (1 << codeSize) - 1;
                    }
                }
                prev = code;
            }
            // Every way out of the loop is a Drain(...) return, which is what leaves p on a real block boundary.
        }

        // Walk to the end of the current sub-block chain so the caller resumes on a real block boundary.
        private static byte[] Drain(byte[] d, ref int p, ref int subLeft, byte[] result)
        {
            p += subLeft;                       // rest of the sub-block we were mid-way through
            subLeft = 0;
            while (p < d.Length)
            {
                int len = d[p++];
                if (len == 0) break;            // terminator - consumed, which is the whole point
                p += len;
            }
            return result;
        }

        private static byte FirstByte(int[] prefix, byte[] suffix, int code, int clearCode)
        {
            int guard = 0;
            while (code >= clearCode)
            {
                if (++guard > 4096) return 0;
                code = prefix[code];
            }
            return (byte)code;
        }
    }
}
