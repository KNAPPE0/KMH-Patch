using System;
using System.Collections.Generic;
using UnityEngine;

namespace KMHPatch.Features.Chat
{
    // Animated PNG - what Discord serves for stickers and many emoji, and what Unity decodes as its FIRST frame only.
    internal static class KmhApng
    {
        // The gif decoder's bounds, not a second copy of them: an animation is an animation whatever it arrived as.
        private const int MaxFrames = KmhGif.MaxFrames;
        private const int MaxPixels = KmhGif.MaxPixels;
        private const int MaxTotalDecodedPixels = KmhGif.MaxTotalDecodedPixels;

        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        // One frame, still in png form. Kept out of Unity so the hard part is checkable headless.
        internal sealed class Part
        {
            public byte[] Png;
            public int    X, Y, Width, Height;
            public float  DelaySeconds;
            public byte   Dispose, Blend;
        }

        internal sealed class Sheet
        {
            public readonly List<Part> Parts = new List<Part>();
            public int Width, Height;
        }

        // A png carrying an acTL chunk before its first IDAT is animated; anything else is an ordinary still.
        internal static bool LooksLikeApng(byte[] d)
        {
            if (d == null || d.Length < 8 + 12) return false;
            for (int i = 0; i < Signature.Length; i++) if (d[i] != Signature[i]) return false;

            int p = 8;
            while (p + 8 <= d.Length)
            {
                int len = ReadInt(d, p);
                if (len < 0 || p + 12L + len > d.Length) return false;
                string type = TypeAt(d, p + 4);
                if (type == "acTL") return true;
                if (type == "IDAT" || type == "IEND") return false;
                p += 12 + len;
            }
            return false;
        }

        // Every frame as its own png, in order. Never throws; a truncated file yields what was readable.
        internal static Sheet Split(byte[] d)
        {
            try { return SplitCore(d); }
            catch (Exception ex)
            {
                Diagnostics.KmhLog.Debug($"APNG split failed: {ex.Message}");
                return null;
            }
        }

        private static Sheet SplitCore(byte[] d)
        {
            if (!LooksLikeApng(d)) return null;

            var sheet = new Sheet();
            var shared = new List<byte[]>();       // IHDR aside, the chunks every frame needs (palette, transparency…)
            byte[] header = null;
            var chunks = new List<byte[]>();       // data chunks of the frame being read
            Part current = null;
            bool defaultIsFrame = false, sawIdat = false;
            long pixelBudget = 0;

            int p = 8;
            while (p + 8 <= d.Length)
            {
                int len = ReadInt(d, p);
                if (len < 0 || p + 12L + len > d.Length) break;
                string type = TypeAt(d, p + 4);
                int dataAt = p + 8;

                switch (type)
                {
                    case "IHDR":
                        header = Slice(d, dataAt, len);
                        sheet.Width  = ReadInt(d, dataAt);
                        sheet.Height = ReadInt(d, dataAt + 4);
                        if (sheet.Width <= 0 || sheet.Height <= 0
                            || (long)sheet.Width * sheet.Height > MaxPixels) return null;
                        break;

                    case "acTL":
                        break;   // frame count and loop count; the frames themselves say everything needed

                    case "fcTL":
                        Close(sheet, current, chunks, header, shared, ref pixelBudget);
                        chunks.Clear();
                        current = ReadControl(d, dataAt, len, sheet);
                        if (current == null) return sheet;
                        if (!sawIdat) defaultIsFrame = true;
                        break;

                    case "IDAT":
                        sawIdat = true;
                        if (current != null && defaultIsFrame) chunks.Add(Slice(d, dataAt, len));
                        break;

                    case "fdAT":
                        if (current != null && len >= 4) chunks.Add(Slice(d, dataAt + 4, len - 4));
                        break;

                    case "IEND":
                        Close(sheet, current, chunks, header, shared, ref pixelBudget);
                        return sheet;

                    default:
                        if (!sawIdat && type != "acTL") shared.Add(Chunk(type, Slice(d, dataAt, len)));
                        break;
                }

                if (sheet.Parts.Count >= MaxFrames) return sheet;
                p += 12 + len;
            }

            Close(sheet, current, chunks, header, shared, ref pixelBudget);
            return sheet;
        }

        private static Part ReadControl(byte[] d, int at, int len, Sheet sheet)
        {
            if (len < 26) return null;
            int w = ReadInt(d, at + 4), h = ReadInt(d, at + 8);
            int x = ReadInt(d, at + 12), y = ReadInt(d, at + 16);
            if (w <= 0 || h <= 0 || x < 0 || y < 0) return null;
            if (x + (long)w > sheet.Width || y + (long)h > sheet.Height) return null;

            int num = (d[at + 20] << 8) | d[at + 21];
            int den = (d[at + 22] << 8) | d[at + 23];
            if (den == 0) den = 100;

            return new Part
            {
                X = x, Y = y, Width = w, Height = h,
                DelaySeconds = Delay(num / (float)den),
                Dispose = d[at + 24],
                Blend   = d[at + 25],
            };
        }

        // A zero or near-zero delay means "as fast as possible", which every browser reads as a tenth of a second.
        internal static float Delay(float seconds) => seconds < 0.02f ? 0.1f : seconds;

        private static void Close(Sheet sheet, Part current, List<byte[]> chunks, byte[] header,
                                  List<byte[]> shared, ref long pixelBudget)
        {
            if (current == null || header == null || chunks.Count == 0) return;
            long cost = (long)current.Width * current.Height;
            if (pixelBudget + cost > MaxTotalDecodedPixels) return;
            pixelBudget += cost;

            current.Png = BuildPng(header, shared, chunks, current.Width, current.Height);
            if (current.Png != null) sheet.Parts.Add(current);
        }

        // A frame's pixels are a plain png once they carry a header of their own size and the file's shared chunks.
        private static byte[] BuildPng(byte[] header, List<byte[]> shared, List<byte[]> data, int w, int h)
        {
            if (header.Length < 13) return null;
            var ihdr = (byte[])header.Clone();
            WriteInt(ihdr, 0, w);
            WriteInt(ihdr, 4, h);

            var parts = new List<byte[]> { Signature, Chunk("IHDR", ihdr) };
            foreach (byte[] c in shared) parts.Add(c);
            foreach (byte[] c in data) parts.Add(Chunk("IDAT", c));
            parts.Add(Chunk("IEND", new byte[0]));

            int total = 0;
            foreach (byte[] part in parts) total += part.Length;
            var png = new byte[total];
            int at = 0;
            foreach (byte[] part in parts) { Buffer.BlockCopy(part, 0, png, at, part.Length); at += part.Length; }
            return png;
        }

        internal static byte[] Chunk(string type, byte[] data)
        {
            var c = new byte[12 + data.Length];
            WriteInt(c, 0, data.Length);
            for (int i = 0; i < 4; i++) c[4 + i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, c, 8, data.Length);
            WriteInt(c, 8 + data.Length, unchecked((int)Crc32(c, 4, 4 + data.Length)));
            return c;
        }

        // PNG's own CRC. A synthesized chunk with a wrong one is refused by every decoder, including Unity's.
        internal static uint Crc32(byte[] d, int at, int len)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = at; i < at + len; i++)
            {
                crc ^= d[i];
                for (int b = 0; b < 8; b++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }

        private static string TypeAt(byte[] d, int at)
            => "" + (char)d[at] + (char)d[at + 1] + (char)d[at + 2] + (char)d[at + 3];

        private static int ReadInt(byte[] d, int at)
            => (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];

        private static void WriteInt(byte[] d, int at, int v)
        {
            d[at] = (byte)(v >> 24); d[at + 1] = (byte)(v >> 16);
            d[at + 2] = (byte)(v >> 8); d[at + 3] = (byte)v;
        }

        private static byte[] Slice(byte[] d, int at, int len)
        {
            var s = new byte[len];
            Buffer.BlockCopy(d, at, s, 0, len);
            return s;
        }

        // Frames composite onto a persistent canvas, exactly as a gif's do: an APNG frame is usually a patch.
        public static KmhGif.Animation Decode(byte[] d)
        {
            try
            {
                Sheet sheet = Split(d);
                if (sheet == null || sheet.Parts.Count == 0) return null;

                int w = sheet.Width, h = sheet.Height;
                var canvas = new Color32[w * h];
                var scratch = new Color32[w * h];
                var anim = new KmhGif.Animation { Width = w, Height = h };

                foreach (Part part in sheet.Parts)
                {
                    Color32[] before = part.Dispose == 2 ? (Color32[])canvas.Clone() : null;

                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    bool ok = tex.LoadImage(part.Png);
                    if (ok) Paint(canvas, w, h, tex, part);
                    try { UnityEngine.Object.Destroy(tex); } catch { }
                    if (!ok) continue;

                    anim.Frames.Add(new KmhGif.Frame
                    {
                        Texture = ToTexture(canvas, w, h, scratch),
                        DelaySeconds = part.DelaySeconds,
                    });

                    if (part.Dispose == 1) ClearRect(canvas, w, h, part);
                    else if (part.Dispose == 2 && before != null) canvas = before;
                }

                if (anim.Frames.Count == 0) return null;
                anim.TotalSeconds = 0f;
                foreach (KmhGif.Frame f in anim.Frames) anim.TotalSeconds += f.DelaySeconds;
                return anim;
            }
            catch (Exception ex)
            {
                Diagnostics.KmhLog.Debug($"APNG decode failed: {ex.Message}");
                return null;
            }
        }

        private static void Paint(Color32[] canvas, int cw, int ch, Texture2D tex, Part part)
        {
            Color32[] src = tex.GetPixels32();
            int fw = tex.width, fh = tex.height;

            for (int row = 0; row < fh; row++)
            {
                int cy = part.Y + row;
                if (cy < 0 || cy >= ch) continue;
                // Unity hands pixels back bottom-up; the canvas runs top-down, like the gif decoder's.
                int srcRow = (fh - 1 - row) * fw;
                for (int col = 0; col < fw; col++)
                {
                    int cx = part.X + col;
                    if (cx < 0 || cx >= cw) continue;
                    Color32 s = src[srcRow + col];
                    canvas[cy * cw + cx] = part.Blend == 1 ? Over(s, canvas[cy * cw + cx]) : s;
                }
            }
        }

        internal static Color32 Over(Color32 src, Color32 dst)
        {
            if (src.a == 255 || dst.a == 0) return src;
            if (src.a == 0) return dst;
            int a = src.a + dst.a * (255 - src.a) / 255;
            if (a <= 0) return new Color32(0, 0, 0, 0);
            return new Color32(
                (byte)((src.r * src.a + dst.r * dst.a * (255 - src.a) / 255) / a),
                (byte)((src.g * src.a + dst.g * dst.a * (255 - src.a) / 255) / a),
                (byte)((src.b * src.a + dst.b * dst.a * (255 - src.a) / 255) / a),
                (byte)a);
        }

        internal static void ClearRect(Color32[] canvas, int cw, int ch, Part part)
        {
            var clear = new Color32(0, 0, 0, 0);
            for (int row = part.Y; row < part.Y + part.Height && row < ch; row++)
                for (int col = part.X; col < part.X + part.Width && col < cw; col++)
                    if (row >= 0 && col >= 0) canvas[row * cw + col] = clear;
        }

        // One scratch buffer for the whole animation: a 25-frame sticker was allocating and dropping 400KB a frame.
        private static Texture2D ToTexture(Color32[] canvas, int w, int h, Color32[] scratch)
        {
            for (int y = 0; y < h; y++)
                Array.Copy(canvas, y * w, scratch, (h - 1 - y) * w, w);

            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(scratch);
            t.Apply(false, false);
            return t;
        }
    }
}
