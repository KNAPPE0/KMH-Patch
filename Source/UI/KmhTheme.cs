using System.Globalization;
using UnityEngine;

namespace KMHPatch.UI
{
    // Presentation only: nothing here affects permission or identity, and colour is never the sole signal for a state.
    internal static class KmhTheme
    {
        // Built-in fallbacks, picked to stay legible on RimWorld's dark panels.
        private static readonly Color FallbackAccent  = new Color(0.886f, 0.757f, 0.420f);   // KMH amber
        private static readonly Color FallbackServer  = new Color(0.62f,  0.69f,  0.79f);    // cool slate
        private static readonly Color FallbackGuild   = new Color(0.49f,  0.83f,  0.49f);    // green
        private static readonly Color FallbackDm      = new Color(0.80f,  0.70f,  0.95f);    // violet
        private static readonly Color FallbackDiscord = new Color(0.549f, 0.620f, 1.000f);   // Discord blurple-ish

        // The glyph and tag carry a relayed line's meaning; the tint only reinforces it.
        private static readonly Color FallbackNameNormal  = new Color(0.94f, 0.94f, 0.94f);
        private static readonly Color FallbackTextNormal  = new Color(0.82f, 0.82f, 0.82f);
        private static readonly Color FallbackNameDiscord = new Color(0.62f, 0.69f, 1.00f);
        private static readonly Color FallbackTextDiscord = new Color(0.78f, 0.80f, 0.92f);

        // State never follows a palette: a warning an owner could recolour into ordinary text is worse than none.
        public static readonly Color System  = new Color(0.72f, 0.72f, 0.78f);
        public static readonly Color Info    = new Color(0.64f, 0.80f, 0.95f);
        public static readonly Color Warning = new Color(0.96f, 0.80f, 0.35f);
        public static readonly Color Error   = new Color(1.00f, 0.50f, 0.50f);

        // Server-provided defaults, set from the hello. Blank = owner expressed no preference.
        public static string ServerAccent  { get; private set; } = "";
        public static string ServerServer  { get; private set; } = "";
        public static string ServerGuild   { get; private set; } = "";
        public static string ServerDm      { get; private set; } = "";
        public static string ServerDiscord { get; private set; } = "";

        // Re-clamped even though the server is trusted, because this string is concatenated into every chat line.
        public const  string DefaultDiscordMarker = "◈";
        public static string DiscordMarker { get; private set; } = DefaultDiscordMarker;

        public static string ClampMarker(string m)
        {
            if (string.IsNullOrWhiteSpace(m)) return DefaultDiscordMarker;
            m = m.Trim();
            // Refused whole, not stripped: mangling markup would leave the owner's mistake on screen looking deliberate.
            if (m.IndexOf('<') >= 0 || m.IndexOf('>') >= 0) return DefaultDiscordMarker;
            return m.Length > 4 ? m.Substring(0, 4) : m;
        }

        public static string ServerNameNormal  { get; private set; } = "";
        public static string ServerTextNormal  { get; private set; } = "";
        public static string ServerNameDiscord { get; private set; } = "";
        public static string ServerTextDiscord { get; private set; } = "";

        public static void ApplyServerTheme(string accent, string server, string guild, string dm, string discord)
        {
            ServerAccent = accent ?? ""; ServerServer = server ?? ""; ServerGuild = guild ?? "";
            ServerDm = dm ?? ""; ServerDiscord = discord ?? "";
        }

        // A missing colour is never a compatibility failure; the built-in palette simply stands.
        public static void ApplyServerChatTheme(string nameNormal, string textNormal, string nameDiscord, string textDiscord,
                                                string discordMarker = null)
        {
            ServerNameNormal  = nameNormal  ?? ""; ServerTextNormal  = textNormal  ?? "";
            ServerNameDiscord = nameDiscord ?? ""; ServerTextDiscord = textDiscord ?? "";
            DiscordMarker     = ClampMarker(discordMarker);
        }

        // Server switch: the next server's own defaults must not inherit the previous one's.
        public static void ClearServerTheme()
        {
            ApplyServerTheme("", "", "", "", "");
            ApplyServerChatTheme("", "", "", "");
        }

        public static Color Accent       => Resolve(Settings?.ThemeAccent,       ServerAccent,  FallbackAccent);
        public static Color ServerChat   => Resolve(Settings?.ThemeServerChat,   ServerServer,  FallbackServer);
        public static Color GuildChat    => Resolve(Settings?.ThemeGuildChat,    ServerGuild,   FallbackGuild);
        public static Color DirectMsg    => Resolve(Settings?.ThemeDirectMsg,    ServerDm,      FallbackDm);
        public static Color DiscordSrc   => Resolve(Settings?.ThemeDiscord,      ServerDiscord, FallbackDiscord);

        public static Color NameNormal   => Resolve(Settings?.ThemeNameNormal,   ServerNameNormal,  FallbackNameNormal);
        public static Color TextNormal   => Resolve(Settings?.ThemeTextNormal,   ServerTextNormal,  FallbackTextNormal);
        public static Color NameDiscord  => Resolve(Settings?.ThemeNameDiscord,  ServerNameDiscord, FallbackNameDiscord);
        public static Color TextDiscord  => Resolve(Settings?.ThemeTextDiscord,  ServerTextDiscord, FallbackTextDiscord);

        private static KMHPatchSettings Settings => KMHPatchMod.Settings;

        // A server can never quietly override a deliberate local choice, nor the reverse.
        private static Color Resolve(string playerHex, string serverHex, Color fallback)
        {
            if (Settings != null && !Settings.ThemeUseServer && TryHex(playerHex, out Color p)) return p;
            if (TryHex(serverHex, out Color s)) return s;
            return fallback;
        }

        // Malformed input falls through to the next layer rather than throwing or drawing an invisible colour.
        public static bool TryHex(string hex, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string h = hex.Trim().TrimStart('#');
            if (h.Length != 6) return false;
            if (!int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;

            float r = ((v >> 16) & 0xFF) / 255f, g = ((v >> 8) & 0xFF) / 255f, b = (v & 0xFF) / 255f;
            c = EnsureReadable(new Color(r, g, b));
            return true;
        }

        // Lifted rather than rejected, so the owner keeps their hue and the player keeps readable text.
        private const float MinLuminance = 0.35f;

        private static Color EnsureReadable(Color c)
        {
            float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            if (lum >= MinLuminance || lum <= 0f) return lum <= 0f ? new Color(0.6f, 0.6f, 0.6f) : c;
            float k = MinLuminance / lum;
            return new Color(Mathf.Min(1f, c.r * k), Mathf.Min(1f, c.g * k), Mathf.Min(1f, c.b * k));
        }

        // "#RRGGBB" for rich-text tags. KMH builds these from trusted config, never from message text.
        public static string Hex(Color c)
            => "#" + Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f).ToString("X2")
                   + Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f).ToString("X2")
                   + Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f).ToString("X2");

        public static string Accented(string text) => "<color=" + Hex(Accent) + ">" + text + "</color>";

        public static Color ForChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return ServerChat;
            if (Features.Chat.ChatChannels.IsDm(channel))    return DirectMsg;
            if (Features.Chat.ChatChannels.IsGuild(channel)) return GuildChat;
            return ServerChat;
        }
    }
}
