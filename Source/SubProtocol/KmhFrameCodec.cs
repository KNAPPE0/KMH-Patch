using System;
using System.IO;
using System.IO.Compression;

namespace KMHPatch.SubProtocol
{
    // The length prefix is bounded by the frame budget, so its top bit carries the compression flag for free.
    internal static class KmhFrameCodec
    {
        internal const int CompressedFlag = unchecked((int)0x80000000);
        internal const int LengthMask     = 0x7FFFFFFF;

        // Below this the saving is noise and the CPU is not free; above it every measured payload wins.
        internal const int CompressAboveBytes = 2048;

        public static byte[] Frame(byte[] body, bool compressed)
        {
            int header = body.Length | (compressed ? CompressedFlag : 0);
            byte[] frame = new byte[4 + body.Length];
            frame[0] = (byte)(header >> 24); frame[1] = (byte)(header >> 16);
            frame[2] = (byte)(header >> 8);  frame[3] = (byte)header;
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);
            return frame;
        }

        public static void ReadHeader(byte[] len4, out int bytes, out bool compressed)
        {
            int header = (len4[0] << 24) | (len4[1] << 16) | (len4[2] << 8) | len4[3];
            compressed = (header & CompressedFlag) != 0;
            bytes      = header & LengthMask;
        }

        // null means compressing was not worth it, so the caller sends the original rather than a bigger copy.
        public static byte[] Deflate(byte[] raw)
        {
            if (raw == null || raw.Length < CompressAboveBytes) return null;
            try
            {
                using (var ms = new MemoryStream())
                {
                    using (var z = new DeflateStream(ms, CompressionLevel.Fastest, true)) z.Write(raw, 0, raw.Length);
                    byte[] packed = ms.ToArray();
                    return packed.Length < raw.Length ? packed : null;
                }
            }
            catch (Exception) { return null; }
        }

        // maxBytes is the receiver's own advertised cap, never the sender's: without it a few KB expand into gigabytes.
        public static byte[] Inflate(byte[] packed, int maxBytes)
        {
            if (packed == null || maxBytes <= 0) return null;
            try
            {
                using (var src = new MemoryStream(packed, false))
                using (var z = new DeflateStream(src, CompressionMode.Decompress))
                using (var outp = new MemoryStream())
                {
                    byte[] buf = new byte[8192];
                    int total = 0, n;
                    while ((n = z.Read(buf, 0, buf.Length)) > 0)
                    {
                        total += n;
                        if (total > maxBytes) return null;
                        outp.Write(buf, 0, n);
                    }
                    return outp.ToArray();
                }
            }
            catch (Exception) { return null; }
        }
    }
}
