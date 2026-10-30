using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Placed from the rect the game hands its menu each frame, so it never covers the title or logo on 1.6 or 1.7.
    internal static class KmhMainMenuSurface
    {
        private const float CardW = 300f, CardH = 182f, Gap = 22f, Pad = 12f;
        // RimWorld Together draws a 45 px "▶" button just left of the menu's first row.
        internal const float SideButtons = 50f;

        // The rectangle MainMenuDrawer.DoMainMenuControls was given this frame; empty until it first draws.
        internal static Rect VanillaPane;

        public static void Draw()
        {
            float w = Verse.UI.screenWidth, h = Verse.UI.screenHeight;
            Rect pane = VanillaPane.width > 0f ? VanillaPane : DefaultPane(w, h);
            Rect card = Place(w, h, pane);
            DrawCard(card);
            Patches.Patch_MainMenuDrawer_EnforcementRestore.DrawRecoveryPanel(w, h, card, pane);
            Features.Account.KmhAccountPrompt.OfferOnce();
        }

        // Where both versions put their menu, for the frames before the game has drawn it.
        internal static Rect DefaultPane(float w, float h) => new Rect(w - 480f, h / 2f - 158f, 450f, 433f);

        internal static Rect Place(float w, float h, Rect pane)
        {
            float x = Mathf.Max(8f, pane.x - SideButtons - Gap - CardW);
            float y = Mathf.Clamp(pane.y, 8f, Mathf.Max(8f, h - CardH - 8f));
            return new Rect(x, y, CardW, CardH);
        }

        internal static Rect Claim(Vector2 size, float w, float h, Rect card, Rect pane)
        {
            Rect top = new Rect((w - size.x) / 2f, 16f, size.x, size.y);
            if (!top.Overlaps(card) && !top.Overlaps(pane)) return top;
            float x = Mathf.Clamp(card.xMax - size.x, 8f, Mathf.Max(8f, w - size.x - 8f));
            return new Rect(x, card.yMax + 10f, size.x, size.y);
        }

        private static void DrawCard(Rect card)
        {
            KmhServersMenuTile.Kick();
            Widgets.DrawShadowAround(card);
            Widgets.DrawBoxSolid(card, KmhBrand.WithAlpha(KmhBrand.Surface, 0.96f));
            Color old = GUI.color;
            GUI.color = KmhBrand.LineStrong;
            Widgets.DrawBox(card, 1);
            GUI.color = old;
            Widgets.DrawBoxSolid(new Rect(card.x, card.y, card.width, 2f), KmhBrand.Accent);

            float x = card.x + Pad, w = card.width - Pad * 2f, y = card.y + Pad;

            // The header: the mark, the name and the build, so a stale install shows on the first screen.
            Rect markRect = new Rect(x, y, 34f, 34f);
            KmhBrand.DrawMark(markRect);
            KmhBrand.DrawWordmark(new Rect(markRect.xMax + 8f, y + 6f, 150f, 22f), KmhBrand.Ink, TextAnchor.MiddleLeft);
            Text.Font = GameFont.Tiny;
            // Tiny's own line height, not 16px: the fixed boxes clipped the descenders on this card's small lines.
            float tinyH = DialogLayout.TextRowH;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = KmhBrand.Muted;
            Widgets.Label(new Rect(x, y + 2f, w, tinyH), BuildTag);
            y += 38f;

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = KmhServersMenuTile.StatusColorPublic();
            Rect statusRect = new Rect(x, y, w, tinyH);
            string statusLine = KmhServersMenuTile.CardLine(w, out string fullStatus);
            Widgets.Label(statusRect, statusLine);
            if (statusLine != fullStatus) TooltipHandler.TipRegion(statusRect, fullStatus);
            y += 22f;

            GUI.color = old;
            // Global Chat needs an account, not a game, so it sits beside Browse rather than inside a server.
            const float chatW = 104f;
            Rect browse = new Rect(x, y, w - chatW - 6f, 36f);
            if (KmhBrand.Button(browse, KmhText.Get("KMH_Menu_Browse", "Browse servers"), true, KmhServersMenuTile.Tooltip()))
                Find.WindowStack.Add(new Features.Global.Dialog_KMHServerBrowser());
            Rect chat = new Rect(browse.xMax + 6f, y, chatW, 36f);
            if (KmhBrand.Button(chat, KmhText.Get("KMH_Menu_GlobalChat", "Global Chat"), false,
                                KmhText.Get("KMH_Menu_GlobalChatTip", "Talk with players on every Colony Nexus server without joining one. Needs a linked Colony Nexus account.")))
                Features.Comms.Dialog_KMHComms.OpenGlobal();
            if (Features.Chat.ChatCache.Unread(Features.Chat.ChatChannels.Global) > 0)
                KmhBrand.DrawDisc(new Rect(chat.xMax - 9f, chat.y - 3f, 9f, 9f), KmhBrand.Accent);
            y += 44f;

            // Friends belong to the Colony Nexus account, not to a game, so they are reachable before joining one.
            float bw = (w - 18f) / 4f;
            if (KmhBrand.Button(new Rect(x, y, bw, 28f), KmhText.Get("KMH_Menu_Account", "Account"), false))
                Dialogs.Dialog_KMHAccount.Open();
            Rect friends = new Rect(x + bw + 6f, y, bw, 28f);
            if (KmhBrand.Button(friends, KmhText.Get("KMH_Menu_Friends", "Friends"), false, KmhText.Get("KMH_Menu_FriendsTip", "Add friends with a code, see who is online and answer requests.")))
                Dialogs.Dialog_KMHFriends.Open();
            if (Features.KmhUnread.FriendRequests() > 0) KmhBrand.DrawDisc(new Rect(friends.xMax - 9f, friends.y - 3f, 9f, 9f), KmhBrand.Accent);
            if (KmhBrand.Button(new Rect(x + (bw + 6f) * 2f, y, bw, 28f), KmhText.Get("KMH_Menu_About", "About"), false))
                Find.WindowStack.Add(new Dialogs.Dialog_KMHAbout());
            if (KmhBrand.Button(new Rect(x + (bw + 6f) * 3f, y, bw, 28f), KmhText.Get("KMH_Menu_ReportShort", "Report"), false,
                                KmhText.Get("KMH_Report_ButtonTip", "Tell the server owner and the Colony Nexus team what went wrong or what you would like. A screenshot and diagnostics can go with it - you see both before anything is sent.")))
                Features.Reports.KmhReportClient.Open();
            y += 34f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = KmhBrand.Muted;
            Widgets.Label(new Rect(x, y, w, DialogLayout.TextRowH), AccountLine().Truncate(w));

            GUI.color = old;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static readonly string BuildTag = BuildBuildTag();

        private static string BuildBuildTag()
        {
            System.Version v = typeof(KmhMainMenuSurface).Assembly.GetName().Version;
            return $"{v.Major}.{v.Minor}.{v.Build} · {KmhProtocol.UiBuildTag}";
        }

        private static string AccountLine()
        {
            Features.Account.KmhGlobalCapabilities.EnsureProbed();
            if (!Features.Account.KmhGlobalCapabilities.Online)
                return KmhText.Get("KMH_Menu_AccountOffline", "Account: Global unreachable, linking waits");

            bool linked = Features.Account.KmhAccountStore.HasToken(Features.Global.GlobalHttp.BaseUrl);
            bool held = Features.Account.KmhAccountStore.HasStoredToken(Features.Global.GlobalHttp.BaseUrl);
            if (linked) Features.Account.KmhUsernameClient.EnsureLoaded();

            // Global Chat needs no RWT server, so it connects here rather than waiting for a join.
            Features.Global.GlobalChatClient.EnsureRunning();

            if (!linked && held) return KmhText.Get("KMH_Menu_LinkNotConfirmed", "Link not confirmed for this Steam account");
            if (!linked) return KmhText.Get("KMH_Menu_NotLinked", "Steam not linked: open Account to link");
            return Features.Account.KmhUsernameClient.HasUsername
                ? KmhText.Get("KMH_Menu_SignedInAs", "Signed in as") + " " + Features.Account.KmhUsernameClient.Username
                : KmhText.Get("KMH_Menu_SteamLinked", "Steam linked");
        }
    }
}
