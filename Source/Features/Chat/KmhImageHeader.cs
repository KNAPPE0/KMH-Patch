namespace KMHPatch.Features.Chat
{
    // LoadImage allocates whatever the header declares, and a small PNG can claim 16384x16384, so read it first.
    internal static class KmhImageHeader
    {
        internal static bool TryReadSize(byte[] b, out int width, out int height)
        {
            width = height = 0;
            if (b == null || b.Length < 24) return false;

            // PNG: the signature, then IHDR first by rule - length, "IHDR", width, height, big-endian.
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            {
                if (b[12] != (byte)'I' || b[13] != (byte)'H' || b[14] != (byte)'D' || b[15] != (byte)'R') return false;
                return Accept(BigEndian32(b, 16), BigEndian32(b, 20), out width, out height);
            }

            if (b[0] == 0xFF && b[1] == 0xD8) return TryJpeg(b, out width, out height);
            return false;
        }

        // Walked segment by segment, so an EXIF thumbnail's own frame header inside APP1 is skipped, never read.
        private static bool TryJpeg(byte[] b, out int width, out int height)
        {
            width = height = 0;
            int p = 2;
            while (p + 3 < b.Length)
            {
                if (b[p] != 0xFF) return false;
                byte marker = b[p + 1];
                if (marker == 0xFF) { p++; continue; }
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8)) { p += 2; continue; }
                if (marker == 0xD9 || marker == 0xDA) return false;

                int len = (b[p + 2] << 8) | b[p + 3];
                if (len < 2) return false;
                bool frame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (frame)
                {
                    if (p + 8 >= b.Length) return false;
                    return Accept((b[p + 7] << 8) | b[p + 8], (b[p + 5] << 8) | b[p + 6], out width, out height);
                }
                p += 2 + len;
            }
            return false;
        }

        private static long BigEndian32(byte[] b, int at)
            => ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];

        private static bool Accept(long w, long h, out int width, out int height)
        {
            width = height = 0;
            if (w <= 0 || h <= 0 || w > int.MaxValue || h > int.MaxValue) return false;
            width = (int)w; height = (int)h;
            return true;
        }
    }
}
