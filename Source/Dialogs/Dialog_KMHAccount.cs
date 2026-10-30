using System;
using System.Collections.Generic;
using KMHPatch.Features.Account;
using KMHPatch.Features.Global;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Every link lives here rather than scattered across the screens that happen to use it.
    public class Dialog_KMHAccount : Window_KMHBase
    {
        // The Steam link belongs to the player, not to whatever server they are on, so it outlives the session.
        protected override bool ClosesOnSessionEnd => false;

        // Fixed and clamped to the screen, never measured from the content: content-sized windows broke the KMH tab.
        public override Vector2 InitialSize => DialogLayout.FitToScreen(560f, 680f);

        public Dialog_KMHAccount()
        {
            doCloseButton = true;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
        }

        // startLink: the player already chose "Link Steam" elsewhere, so ask for the code now, not via a second button.
        public static void Open(bool startLink = false)
        {
            if (Find.WindowStack.IsOpen<Dialog_KMHAccount>()) return;
            var w = new Dialog_KMHAccount();
            Find.WindowStack.Add(w);
            if (!startLink || KmhAccountStore.HasToken(Features.Global.GlobalHttp.BaseUrl)) return;
            KmhAccountClient.StartLink();
            w._openBrowserWhenReady = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            KmhAccountClient.Refresh();
            KmhUsernameClient.Refresh();
            _wasLinked = KmhAccountClient.State == AccountState.Linked;
        }

        // The poll runs off the dialog, so closing the window is what ends it.
        public override void PostClose()
        {
            base.PostClose();
            KmhAccountClient.Cancel();
        }

        private bool _wasLinked;
        private bool _offerUsername;

        private Vector2 _scroll;
        // BeginScrollView reads _viewHeight and the draw writes _measuredHeight, so the viewRect cannot change mid-frame.
        private float _viewHeight     = 640f;
        private float _measuredHeight = 640f;
        private int _lastScrollFrame = -1;

        protected override void DrawContents(Rect inRect)
        {
            KmhAccountClient.Tick();
            FollowLink();

            if (KmhScroll.NewFrame(ref _lastScrollFrame)) _viewHeight = _measuredHeight;

            Rect content = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - DialogLayout.FooterReserve);
            if (content.height <= 0f) return;

            // Always reserved: reserving only on overflow oscillates, as the bar narrows text and wrapping grows height.
            float viewW = Mathf.Max(1f, content.width - DialogLayout.ScrollbarReserveWidth);
            float viewH = Mathf.Max(_viewHeight, content.height);
            Rect viewRect = new Rect(0f, 0f, viewW, viewH);

            _scroll.y = KmhScroll.Clamp(_scroll.y, viewH, content.height);
            Widgets.BeginScrollView(content, ref _scroll, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            float measured = viewH;
            try
            {
                Text.Font = GameFont.Medium;
                listing.Label(KmhText.Get("KMH_Account_AccountLinks", "Account & links"));
                Text.Font = GameFont.Small;

                listing.Gap(4f);
                listing.Label($"<color=#9AA4B2>Directory: {DirectoryLabel()}</color>");
                listing.GapLine(10f);

                // All links before the settings: under the privacy rows Discord fell off the bottom of the window.
                DrawUsername(listing);
                DrawSteam(listing);
                DrawServerVerification(listing);
                listing.GapLine(12f);
                DrawDiscord(listing);
                DrawPrivacy(listing);
            }
            finally
            {
                measured = listing.CurHeight;
                listing.End();
                Widgets.EndScrollView();
            }

            _measuredHeight = Mathf.Max(measured, content.height);
        }

        private static string DirectoryLabel()
            => GlobalHttp.IsOfficial ? "Official Colony Nexus Global" : GlobalHttp.BaseUrl;

        // The same answer the Federation window gives, so a signed-in player is never told to sign in.
        private static void DrawServerVerification(Listing_Standard listing)
        {
            if (!KmhDispatcher.IsKmhServer || string.IsNullOrEmpty(KmhDispatcher.ServerId)) return;
            listing.Gap(6f);
            listing.Label(KmhText.Get("KMH_Account_ThisServer", "<b>This server</b>"));
            string text = ServerVerificationText(KmhJoinTicketClient.Verified,
                ProblemInThisWindow(KmhJoinTicketClient.Problem, KmhJoinTicketClient.CurrentProblemText()));
            listing.Label(KmhJoinTicketClient.Verified ? $"<color=#4cc38a>{text}</color>" : $"<color=#f0c04f>{text}</color>");
        }

        // Signed out, the general advice sends the player to this very window, so point at its Steam section instead.
        internal static string ProblemInThisWindow(string problem, string text)
            => problem == KmhJoinTicketClient.SignedOut
                ? KmhText.Get("KMH_Account_ServerSignedOut",
                    "You are not signed in to Colony Nexus Global yet. Link Steam in the Steam section of this window, and this server confirms you without a reconnect.")
                : text;

        internal static string ServerVerificationText(bool verified, string problem)
            => verified ? KmhText.Get("KMH_Account_ServerVerified", "This server accepted your Colony Nexus Global sign-in.")
             : !string.IsNullOrEmpty(problem) ? problem
             : KmhText.Get("KMH_Account_ServerChecking", "Waiting for Colony Nexus Global to confirm who you are on this server.");

        // A link that just completed is the one moment a player expects to be asked for a name, so ask then.
        private void FollowLink()
        {
            bool linked = KmhAccountClient.State == AccountState.Linked;
            if (linked && !_wasLinked)
            {
                KmhUsernameClient.Refresh();
                _offerUsername = true;
            }
            if (!linked) _offerUsername = false;
            _wasLinked = linked;

            if (!_offerUsername || KmhUsernameClient.Busy) return;
            if (KmhUsernameClient.State != UsernameState.Ready) return;
            _offerUsername = false;
            if (!KmhUsernameClient.HasUsername) Dialog_KMHUsername.Open();
        }

        // Global owns these; while it is down say so, or a blank page reads as "you are visible to nobody".
        private void DrawPrivacy(Listing_Standard listing)
        {
            if (KmhAccountClient.State != AccountState.Linked) return;

            KmhPrivacyClient.RefreshIfNeeded();
            listing.GapLine(12f);
            listing.Label(KmhText.Get("KMH_Account_WhoCanSeeYou", "Who can see you"));

            if (KmhPrivacyClient.State == PrivacyState.Unavailable)
            {
                listing.Label("<color=#9AA4B2>Colony Nexus Global cannot be reached, so your privacy settings cannot be "
                            + "shown or changed right now. They are unchanged.</color>");
                if (!KmhPrivacyClient.Busy && listing.ButtonText(KmhText.Get("KMH_Account_TryAgain", "Try again"))) KmhPrivacyClient.Load();
                return;
            }
            if (KmhPrivacyClient.State != PrivacyState.Loaded)
            {
                listing.Label(KmhText.Get("KMH_Account_CheckingWithGlobal", "<color=#9AA4B2>Checking with Global...</color>"));
                return;
            }

            AudienceRow(listing, "Show me online to", KmhPrivacyClient.PresenceVisibility, false,
                KmhPrivacyClient.SetPresenceVisibility);

            bool showServer = KmhPrivacyClient.ShowServer;
            listing.CheckboxLabeled(KmhText.Get("KMH_Account_AlsoShowWhichServer", "Also show which server I am on"), ref showServer,
                "Separate from being shown as online. Turn this off to appear online without saying where.");
            if (showServer != KmhPrivacyClient.ShowServer) KmhPrivacyClient.SetShowServer(showServer);

            AudienceRow(listing, "Friend requests from", KmhPrivacyClient.FriendRequests, true,
                KmhPrivacyClient.SetFriendRequests);

            AudienceRow(listing, "Direct messages from", KmhPrivacyClient.DirectMessages, false,
                KmhPrivacyClient.SetDirectMessages);

            bool steam = KmhPrivacyClient.SteamDiscovery;
            listing.CheckboxLabeled(
                KmhText.Get("KMH_Account_SteamDiscovery", "Let my Steam friends find me on Colony Nexus"), ref steam,
                "Matches you with Steam friends who also have this on. Only your Steam friend ids are sent, never "
                + "names. Turning it off removes the friends it added and forgets the list.");
            if (steam != KmhPrivacyClient.SteamDiscovery) KmhPrivacyClient.SetSteamDiscovery(steam);
        }

        private void AudienceRow(Listing_Standard listing, string label, string current, bool openOrClosed,
                                 Action<string> pick)
        {
            Rect row = listing.GetRect(28f);
            Widgets.Label(row.LeftPart(0.55f), label);
            if (!Widgets.ButtonText(row.RightPart(0.43f), AudienceLabel(current)) || KmhPrivacyClient.Busy) return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (string v in openOrClosed
                     ? new[] { KmhPrivacyClient.Everyone, KmhPrivacyClient.Nobody }
                     : new[] { KmhPrivacyClient.Everyone, KmhPrivacyClient.Friends, KmhPrivacyClient.Nobody })
            {
                string chosen = v;
                options.Add(new FloatMenuOption(AudienceLabel(chosen), () => pick(chosen)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string AudienceLabel(string audience)
        {
            if (audience == KmhPrivacyClient.Everyone) return "Everyone";
            if (audience == KmhPrivacyClient.Friends) return "Friends only";
            return "No one";
        }

        private void DrawUsername(Listing_Standard listing)
        {
            if (KmhAccountClient.State != AccountState.Linked) return;

            listing.Label(KmhText.Get("KMH_Account_KmhUsername", "<b>Colony Nexus username</b>"));
            switch (KmhUsernameClient.State)
            {
                case UsernameState.Loading:
                    listing.Label(KmhText.Get("KMH_Account_CheckingWithGlobal", "<color=#9AA4B2>Checking with Global...</color>"));
                    break;

                case UsernameState.GlobalUnavailable:
                    listing.Label(KmhText.Get("KMH_Account_NotAvailableWhileGlobal", "<color=#9AA4B2>Not available while Global is unreachable.</color>"));
                    break;

                default:
                    if (KmhUsernameClient.HasUsername)
                    {
                        listing.Label($"<b>{KmhUsernameClient.Username}</b>");
                        if (!KmhUsernameClient.CanChange)
                            listing.Label($"<color=#9AA4B2>Changeable again on {KmhUsernameClient.ChangeAllowedText()}.</color>");
                    }
                    else
                    {
                        listing.Label(KmhText.Get("KMH_Account_NotChosenYetYour", "<color=#9AA4B2>Not chosen yet - your Steam name stands in until you pick one.</color>"));
                    }

                    listing.Gap(6f);
                    if (listing.ButtonText(KmhUsernameClient.HasUsername ? "Change username" : "Choose a username"))
                        Dialog_KMHUsername.Open();

                    // Offered even on rename cooldown: releasing takes nothing from anyone and frees an abandoned handle.
                    if (KmhUsernameClient.CanRelease && !KmhUsernameClient.Busy
                        && listing.ButtonText(KmhText.Get("KMH_Account_ReleaseUsername", "Give up this username")))
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            KmhText.Get("KMH_Account_ReleaseConfirm", "Give up the username {0}?")
                                .Replace("{0}", UI.KmhDisplayText.Inert(KmhUsernameClient.Username, 32))
                            + "\n\n" + KmhText.Get("KMH_Account_ReleaseConfirmBody",
                                "It becomes free for anyone else straight away, and you will have none until you "
                              + "choose one. Choosing a new one still counts as a rename, so the usual wait applies."),
                            KmhUsernameClient.Release, destructive: true));
                    break;
            }

            listing.GapLine(12f);
        }

        private void DrawSteam(Listing_Standard listing)
        {
            listing.Label(KmhText.Get("KMH_Account_Steam", "<b>Steam</b>"));

            switch (KmhAccountClient.State)
            {
                case AccountState.Loading:
                    listing.Label(KmhText.Get("KMH_Account_CheckingWithGlobal", "<color=#9AA4B2>Checking with Global...</color>"));
                    break;

                case AccountState.Linked:
                    listing.Label($"Linked as <b>{Name()}</b>");
                    DrawPlayerId(listing.GetRect(28f), KmhAccountClient.PlayerGlobalId);
                    if (Features.Friends.KmhFriendsClient.MyCode == null) Features.Friends.KmhFriendsClient.FetchCode();
                    else if (Features.Friends.KmhFriendsClient.MyCode.Length > 0) DrawFriendCode(listing.GetRect(28f), Features.Friends.KmhFriendsClient.MyCode);
                    listing.Gap(6f);
                    // Friends lives here too, so a player who came to find their code can add someone without a second hunt.
                    if (listing.ButtonText(KmhText.Get("KMH_Account_OpenFriends", "Friends and Quick Add")))
                        Dialog_KMHFriends.Open();
                    if (!KmhAccountClient.Busy && listing.ButtonText(KmhText.Get("KMH_Account_UnlinkSteam", "Unlink Steam")))
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "Unlink this Steam account from Colony Nexus?\n\nYour progress on each server is kept. You will need to link again to be recognised as the same player.",
                            KmhAccountClient.Unlink, destructive: true));
                    // Unlink revokes only this machine's credential; this also kills copies on PCs the player no longer has.
                    if (!KmhAccountClient.Busy && listing.ButtonText(KmhText.Get("KMH_Account_SignOutEverywhere",
                                                                                 "Sign out everywhere")))
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            KmhText.Get("KMH_Account_SignOutEverywhereConfirm", "Sign out of Colony Nexus on every device?")
                            + "\n\n" + KmhText.Get("KMH_Account_SignOutEverywhereBody",
                                "Every copy of RimWorld linked to this Colony Nexus account is signed out, including this "
                              + "one. Nothing you own is lost - each copy links again as the same player."),
                            KmhAccountClient.UnlinkEverywhere, destructive: true));
                    break;

                case AccountState.Opening:
                    listing.Label(KmhText.Get("KMH_Account_AskingGlobalForA", "<color=#9AA4B2>Asking Global for a link code...</color>"));
                    break;

                case AccountState.Waiting:
                    DrawWaiting(listing);
                    break;

                case AccountState.Expired:
                    listing.Label(KmhText.Get("KMH_Account_ThatCodeExpiredBefore", "<color=#f0c04f>That code expired before it was used.</color>"));
                    DrawLinkButton(listing, "Get a new code");
                    break;

                // Also reached when this copy's own reply was lost in transit, so it must not blame another copy.
                case AccountState.AlreadyLinked:
                    listing.Label(KmhText.Get("KMH_Account_CodeAlreadyUsed",
                        "<color=#f0c04f>That code has already been used.</color>"));
                    listing.Label(KmhText.Get("KMH_Account_CodeAlreadyUsedWhy",
                        "<color=#9AA4B2>Either another copy of the game claimed it, or the reply to this one was "
                      + "lost. Nothing is linked yet - start over and approve a new code.</color>"));
                    DrawLinkButton(listing, "Start over");
                    break;

                // The credential is kept for the Steam account that owns it, but this copy must not act as that player.
                case AccountState.WrongSteamAccount:
                    listing.Label(KmhText.Get("KMH_Account_WrongSteam",
                        "<color=#f0c04f>This Colony Nexus account belongs to a different Steam account.</color>"));
                    listing.Label(KmhText.Get("KMH_Account_WrongSteamWhy",
                        "<color=#9AA4B2>RimWorld is signed in to Steam as someone else, so Colony Nexus will not act as that "
                      + "player here. Unlink to use this Steam account instead - the other account keeps everything "
                      + "it has.</color>"));
                    listing.Gap(6f);
                    if (!KmhAccountClient.Busy && listing.ButtonText(KmhText.Get("KMH_Account_UnlinkSteam", "Unlink Steam")))
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            KmhText.Get("KMH_Account_UnlinkOtherConfirm",
                                "Unlink the other Steam account's Colony Nexus login from this copy of RimWorld?")
                            + "\n\n" + KmhText.Get("KMH_Account_UnlinkOtherConfirmBody",
                                "That account keeps its username and everything it owns. This copy can then link "
                              + "as the Steam account you are actually signed in as."),
                            KmhAccountClient.Unlink, destructive: true));
                    break;

                case AccountState.GlobalUnavailable:
                    listing.Label(KmhText.Get("KMH_Account_KmhGlobalCouldNot", "<color=#f0c04f>Colony Nexus Global could not be reached.</color>"));
                    listing.Label(KmhText.Get("KMH_Account_YourLinkIsUnaffected", "<color=#9AA4B2>Your link is unaffected - try again when you are back online.</color>"));
                    if (KmhAccountClient.RetryIsRelink) DrawLinkButton(listing, "Try again");
                    else if (!KmhAccountClient.Busy)
                    {
                        listing.Gap(6f);
                        if (listing.ButtonText(KmhText.Get("KMH_Account_TryAgain", "Try again"))) KmhAccountClient.Refresh();
                    }
                    break;

                case AccountState.Failed:
                    listing.Label($"<color=#f0c04f>Linking failed{Code()}.</color>");
                    string why = WhyLinkFailed(KmhAccountClient.ErrorCode);
                    if (why.Length > 0) listing.Label($"<color=#9AA4B2>{why}</color>");
                    DrawLinkButton(listing, "Try again");
                    break;

                default:
                    listing.Label(KmhText.Get("KMH_Account_NotLinked", "Not linked"));
                    listing.Label(KmhText.Get("KMH_Account_LinkingProvesYouAre", "<color=#9AA4B2>Linking proves you are the same player on every Colony Nexus server, so your name and history follow you.</color>"));
                    DrawLinkButton(listing, "Link Steam");
                    break;
            }
        }

        private void DrawWaiting(Listing_Standard listing)
        {
            listing.Label(KmhText.Get("KMH_Account_EnterThisCodeIn", "Enter this code in your browser:"));

            Rect code = listing.GetRect(38f);
            Widgets.DrawMenuSection(code);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(code, KmhAccountClient.Code);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            listing.Gap(6f);
            long left = KmhAccountClient.SecondsLeft;
            listing.Label($"<color=#9AA4B2>Sign in with Steam at {KmhAccountClient.LinkUrl}\nWaiting... expires in {left / 60}:{left % 60:00}</color>");

            listing.Gap(6f);
            if (listing.ButtonText(KmhText.Get("KMH_Account_OpenSteamLink", "Open Steam link"))) OpenLinkPage();
            listing.Gap(4f);
            if (listing.ButtonText(KmhText.Get("KMH_Account_CopyLink", "Copy link"))) CopyToClipboard(KmhAccountClient.DeepLink, "Link copied.");
            listing.Gap(4f);
            if (listing.ButtonText(KmhText.Get("KMH_Account_CopyCode", "Copy code"))) CopyToClipboard(KmhAccountClient.Code, "Code copied.");
            listing.Gap(4f);
            if (listing.ButtonText(KmhText.Get("KMH_Account_Cancel", "Cancel"))) KmhAccountClient.CancelToIdle();
        }

        private void DrawLinkButton(Listing_Standard listing, string label)
        {
            if (KmhAccountClient.Busy) return;
            listing.Gap(6f);
            if (!listing.ButtonText(label)) return;
            KmhAccountClient.StartLink();
            _openBrowserWhenReady = true;
        }

        private bool _openBrowserWhenReady;

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            // Opened from the main thread once the code exists, so the browser lands on a page that can be completed.
            if (!_openBrowserWhenReady || KmhAccountClient.State != AccountState.Waiting) return;
            _openBrowserWhenReady = false;
            OpenLinkPage();
        }

        internal static void CopyToClipboard(string text, string confirmation)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { GUIUtility.systemCopyBuffer = text; }
            catch (Exception) { }
            Messages.Message(confirmation, MessageTypeDefOf.TaskCompletion, false);
        }

        private static void OpenLinkPage()
        {
            string url = KmhAccountClient.DeepLink;
            if (string.IsNullOrEmpty(url)) return;
            try { Application.OpenURL(url); }
            catch (Exception) { Messages.Message("Could not open a browser - use Copy link.", MessageTypeDefOf.RejectInput, false); }
        }

        // Global only ever proved a SteamID64, so the readable name comes from Steam on this machine.
        private static string Name()
        {
            if (KmhAccountClient.DisplayName.Length > 0) return KmhAccountClient.DisplayName;
            return KmhSteamPersona.Known ? KmhSteamPersona.Name : "your Steam account";
        }

        // All 32 characters: a friend request needs the whole id, and eight of them could not be typed back in.
        internal static string PlayerIdText(string id)
            => KmhText.Get("KMH_Account_YourId", "Your player ID: {0}", KmhDisplayText.Inert(id ?? "", 40));

        internal static void DrawPlayerId(Rect row, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            string copy = KmhText.Get("KMH_Account_CopyId", "Copy ID");
            if (LabelWithCopyButton(row, $"<color=#9AA4B2>{PlayerIdText(id)}</color>", copy))
                CopyToClipboard(id, KmhText.Get("KMH_Account_IdCopied", "Player ID copied. Send it to a friend to be added."));
        }

        internal static void DrawFriendCode(Rect row, string code)
        {
            string copy = KmhText.Get("KMH_Friends_Copy", "Copy");
            string shown = Features.Friends.KmhFriendsClient.FormatCode(code);
            if (LabelWithCopyButton(row, $"<color=#9AA4B2>{KmhText.Get("KMH_Account_FriendCode", "Your friend code: {0}", KmhDisplayText.Inert(shown, 12))}</color>", copy))
                CopyToClipboard(shown, KmhText.Get("KMH_Account_CodeCopied", "Friend code copied. A friend pastes it into Friends, Quick Add."));
        }

        private static bool LabelWithCopyButton(Rect row, string label, string copy)
        {
            float copyW = DialogLayout.ButtonW(copy, 80f, row.width * 0.35f);
            DialogLayout.LabelTrunc(new Rect(row.x, row.y, Mathf.Max(0f, row.width - copyW - 8f), row.height), label, TextAnchor.MiddleLeft);
            return Widgets.ButtonText(new Rect(row.xMax - copyW, row.y + 1f, copyW, row.height - 2f), copy);
        }

        private static string Code()
            => KmhAccountClient.ErrorCode.Length > 0 ? $" ({KmhAccountClient.ErrorCode})" : "";

        // A bare code is not something a player can act on, and both of these are things they can fix.
        private static string WhyLinkFailed(string code)
        {
            switch (code)
            {
                case "KMH-LNK-708":
                    return KmhText.Get("KMH_Account_LinkNeedsSteam",
                        "Colony Nexus links your account through Steam, so RimWorld has to be running from Steam with "
                      + "Steam signed in. Start the game through Steam and try again.");
                case "KMH-LNK-707":
                    return KmhText.Get("KMH_Account_LinkSteamMismatch",
                        "The browser approved that code while signed in as a different Steam account than the "
                      + "one running the game. Sign in to that page as the same account and try again.");
                default:
                    return "";
            }
        }

        private void DrawDiscord(Listing_Standard listing)
        {
            listing.Label(KmhText.Get("KMH_Account_Discord", "<b>Discord</b>"));

            // With neither precondition met, say what is missing rather than leave a bare header.
            bool account = KmhAccountClient.State == AccountState.Linked;
            if (!account && !KmhDispatcher.IsKmhServer)
            {
                listing.Label(KmhText.Get("KMH_Account_DiscordNeedsAccount",
                    "<color=#9AA4B2>Link your Colony Nexus account above first - Discord attaches to it.</color>"));
                return;
            }

            DrawDiscordAccount(listing);
            DrawDiscordServer(listing);
        }

        // Made through Discord rather than a bot, so it holds everywhere rather than on one server.
        private void DrawDiscordAccount(Listing_Standard listing)
        {
            if (KmhAccountClient.State != AccountState.Linked) return;

            KmhDiscordLinkClient.RefreshIfNeeded();

            if (KmhDiscordLinkClient.State == DiscordLinkState.Unavailable)
            {
                listing.Label(KmhText.Get("KMH_Account_DiscordUnavailable",
                    "<color=#9AA4B2>Colony Nexus Global cannot be reached, so your Discord link cannot be shown or changed "
                  + "right now. It is unchanged.</color>"));
                if (!KmhDiscordLinkClient.Busy &&
                    listing.ButtonText(KmhText.Get("KMH_Account_TryAgain", "Try again"))) KmhDiscordLinkClient.Refresh();
                listing.Gap(6f);
                return;
            }

            if (KmhDiscordLinkClient.State == DiscordLinkState.Linking)
            {
                listing.Label(KmhText.Get("KMH_Account_DiscordLinking",
                    "<color=#9AA4B2>Linking... finish in your browser. This updates on its own.</color>"));
                listing.Gap(6f);
                return;
            }

            if (KmhDiscordLinkClient.State != DiscordLinkState.Ready)
            {
                listing.Label(KmhText.Get("KMH_Account_CheckingWithGlobal", "<color=#9AA4B2>Checking with Global...</color>"));
                listing.Gap(6f);
                return;
            }

            if (KmhDiscordLinkClient.Linked)
            {
                string name = KmhDisplayText.Inert(KmhDiscordLinkClient.DisplayName ?? "", 40);
                listing.Label(name.Length > 0
                    ? "Linked as <b>" + name + "</b>"
                    : KmhText.Get("KMH_Account_DiscordLinked", "Linked to Discord"));
                if (!KmhDiscordLinkClient.Busy &&
                    listing.ButtonText(KmhText.Get("KMH_Account_UnlinkDiscord", "Unlink Discord")))
                    KmhDiscordLinkClient.Unlink();
                listing.Gap(6f);
                return;
            }

            if (!KmhDiscordLinkClient.Available)
            {
                listing.Label(KmhText.Get("KMH_Account_DiscordNotOffered",
                    "<color=#9AA4B2>This Colony Nexus Global does not offer Discord linking.</color>"));
                listing.Gap(6f);
                return;
            }

            listing.Label(KmhText.Get("KMH_Account_NotLinked", "Not linked"));
            listing.Label(KmhText.Get("KMH_Account_DiscordConnectBlurb",
                "<color=#9AA4B2>Links your Colony Nexus account to Discord on every Colony Nexus server at once. Colony Nexus is shown your "
              + "Discord name and nothing else.</color>"));
            listing.Gap(6f);
            if (!KmhDiscordLinkClient.Busy &&
                listing.ButtonText(KmhText.Get("KMH_Account_ConnectDiscord", "Connect Discord")))
                KmhDiscordLinkClient.BeginLink();
            listing.Gap(6f);
        }

        // Separate from the account link: this is what the server's own bot relays chat as.
        private void DrawDiscordServer(Listing_Standard listing)
        {
            if (!KmhDispatcher.IsKmhServer) return;

            string me = KmhSession.Me;
            if (Features.LinkedAccounts.LinkedAccountsCache.IsLinked(me))
            {
                listing.Label($"<color=#9AA4B2>This server: linked as "
                            + $"{Features.LinkedAccounts.LinkedAccountsCache.DiscordNameFor(me)}. "
                            + "Unlink with !kmh-unlink.</color>");
                return;
            }

            listing.Label(KmhText.Get("KMH_Account_LinksThisServerS", "<color=#9AA4B2>This server can also link separately for chat relay and notifications.</color>"));
            listing.Gap(6f);
            if (listing.ButtonText(KmhText.Get("KMH_Account_GetADiscordLink", "Get a Discord link code")))
                Features.LinkedAccounts.LinkedAccountsHandler.RequestCode();
        }
    }
}
