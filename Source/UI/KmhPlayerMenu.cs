using System;
using System.Collections.Generic;
using KMHPatch.Features.Account;
using KMHPatch.Features.Chat;
using KMHPatch.Features.Chat.Dto;
using KMHPatch.Features.Friends;
using KMHPatch.Features.Global;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // One player menu everywhere; a server name and a Global account are never guessed from each other.
    internal static class KmhPlayerMenu
    {
        internal enum Act
        {
            Profile, AddFriend, AcceptFriend, CancelRequest, RemoveFriend, Message, Mail,
            BlockHere, UnblockHere, BlockEverywhere, UnblockEverywhere, Self, NotAPlayer,
            DeclineFriend, CopyId,
        }

        // How Message reaches them, best first.
        internal enum Route { None, GlobalDm, ServerDm, Mail }

        internal enum Why { None, NotLinked, NoGlobalId, NotOnServer, MailOff, ChatAndMailOff, BlockingOff, YouBlocked }

        internal struct Choice
        {
            public Act Act;
            public bool Enabled;
            public Why Why;
            public Route Route;

            public override string ToString()
                => Act + (Enabled ? "+" : "-") + (Why != Why.None ? Why.ToString() : "") + (Route != Route.None ? Route.ToString() : "");
        }

        // Who the menu is about. LocalName is a name on THIS server only; a Global username goes in Shown.
        internal struct Target
        {
            public string LocalName, GlobalId, Shown;
            public bool NameIsPlayer;
        }

        // Everything a plan depends on, so the rules can be checked without a game.
        internal struct Facts
        {
            public string LocalName;
            public bool NameIsPlayer;
            public string GlobalId;
            public bool IsSelf, Linked, OnServer, ChatOn, MailOn, BlockingOn, BlockedHere, BlockedEverywhere;
            public string Friendship;   // "", "friend", "incoming" or "outgoing"
        }

        // username: a player on this server, or null when only a Global account is known. Never show a Steam id here.
        public static void Open(string username, string globalId = null, string shownName = null)
            => Open(new Target { LocalName = username ?? "", GlobalId = globalId ?? "", Shown = shownName, NameIsPlayer = true });

        public static List<FloatMenuOption> Options(string username, string globalId = null, string shownName = null)
            => Options(new Target { LocalName = username ?? "", GlobalId = globalId ?? "", Shown = shownName, NameIsPlayer = true });

        internal static void NameCell(Rect r, string username, string shown = null)
        {
            bool named = !string.IsNullOrEmpty(username);
            if (named && Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            DialogLayout.LabelTrunc(r, named ? shown ?? Features.LinkedAccounts.LinkedAccountsCache.Format(username) : "<color=#A9AFB9>-</color>");
            if (named) LinkRegion(r, username, highlight: false);
        }

        internal static void LinkRegion(Rect r, string username, bool highlight = true)
        {
            if (string.IsNullOrEmpty(username)) return;
            // Built only under the mouse: every row draws every frame, and the tip text is not free.
            if (Mouse.IsOver(r))
            {
                if (highlight) Widgets.DrawHighlight(r);
                TooltipHandler.TipRegion(r, KmhText.Get("KMH_PlayerMenu_NameTip", "Click for their profile, messages, friend and block options.") + "\n\n" + Title(new Target { LocalName = username }));
            }
            if (Widgets.ButtonInvisible(r)) Open(username);
        }

        internal static void Open(Target t)
        {
            List<FloatMenuOption> options = Options(t);
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options, Title(t)));
        }

        internal static List<FloatMenuOption> Options(Target t) => Options(t, true);

        internal static List<FloatMenuOption> Options(Target t, bool withProfile)
        {
            Facts f = Gather(t);
            string shown = Title(t);
            var list = new List<FloatMenuOption>();
            foreach (Choice c in Plan(f)) if (withProfile || c.Act != Act.Profile) list.Add(Option(c, f, shown));
            return list;
        }

        internal static string Title(Target t)
        {
            string name = !string.IsNullOrEmpty(t.Shown) ? t.Shown : t.LocalName;
            return KmhDisplayText.Inert(string.IsNullOrEmpty(name) ? "?" : name, 48);
        }

        // A chat line names a server player, except in Global Chat, where only the sender's Global account counts.
        internal static Target TargetFor(ChatMessage m, string channel)
        {
            if (m == null) return default(Target);
            if (ChatChannels.IsGlobal(channel))
                return new Target { GlobalId = m.SenderGlobalId ?? "", Shown = m.FromUsername ?? "" };
            return new Target { LocalName = m.FromUsername ?? "", Shown = m.FromUsername ?? "", NameIsPlayer = !m.UnverifiedSender };
        }

        internal static List<Choice> Plan(Facts f)
        {
            var c = new List<Choice>();
            bool named = !string.IsNullOrEmpty(f.LocalName);
            bool local = named && f.NameIsPlayer;
            bool global = !string.IsNullOrEmpty(f.GlobalId);

            if (local) c.Add(Pick(Act.Profile, f.OnServer, Why.NotOnServer));
            if (f.IsSelf) { c.Add(Pick(Act.Self, false, Why.None)); return c; }

            if (local || global)
            {
                Act friend = f.Friendship == "friend"   ? Act.RemoveFriend
                           : f.Friendship == "incoming" ? Act.AcceptFriend
                           : f.Friendship == "outgoing" ? Act.CancelRequest
                                                        : Act.AddFriend;
                Why fw = !f.Linked ? Why.NotLinked : !global ? Why.NoGlobalId : f.BlockedEverywhere ? Why.YouBlocked : Why.None;
                c.Add(Pick(friend, fw == Why.None, fw));
                if (friend == Act.AcceptFriend) c.Add(Pick(Act.DeclineFriend, fw == Why.None, fw));

                Route route = f.Linked && global && !f.BlockedEverywhere                      ? Route.GlobalDm
                            : local && f.OnServer && f.ChatOn && ChatChannels.CanDm(f.LocalName) ? Route.ServerDm
                            : local && f.OnServer && f.MailOn                                   ? Route.Mail
                                                                                                : Route.None;
                Why mw = !local ? (f.Linked ? Why.YouBlocked : Why.NotLinked)
                       : f.OnServer ? Why.ChatAndMailOff : Why.NotOnServer;
                Choice message = Pick(Act.Message, route != Route.None, mw);
                message.Route = route;
                c.Add(message);

                // Message already fell back to mail; a second entry doing the same would read as something else.
                if (local && route != Route.Mail)
                    c.Add(Pick(Act.Mail, f.OnServer && f.MailOn, f.OnServer ? Why.MailOff : Why.NotOnServer));
            }
            else c.Add(Pick(Act.NotAPlayer, false, Why.None));

            if (named)
                c.Add(Pick(f.BlockedHere ? Act.UnblockHere : Act.BlockHere, f.OnServer && f.BlockingOn,
                           f.OnServer ? Why.BlockingOff : Why.NotOnServer));
            if (global)
                c.Add(Pick(f.BlockedEverywhere ? Act.UnblockEverywhere : Act.BlockEverywhere, f.Linked, Why.NotLinked));
            // Their account's ID, to paste into Quick Add elsewhere; it names a Colony Nexus account, never a Steam one.
            if (global) c.Add(Pick(Act.CopyId, true, Why.None));
            return c;
        }

        private static Choice Pick(Act a, bool enabled, Why why)
            => new Choice { Act = a, Enabled = enabled, Why = enabled ? Why.None : why };

        internal static Facts Gather(Target t)
        {
            string local = t.LocalName ?? "";
            string id = KmhFriendsClient.NormalizeId(t.GlobalId);
            string mine = KmhAccountStore.PlayerGlobalId ?? "";
            return new Facts
            {
                LocalName = local,
                NameIsPlayer = t.NameIsPlayer,
                GlobalId = id,
                IsSelf = (t.NameIsPlayer && KmhSession.IsMe(local))
                      || (id.Length > 0 && string.Equals(id, mine, StringComparison.OrdinalIgnoreCase)),
                Linked = KmhAccountStore.HasToken(GlobalHttp.BaseUrl),
                OnServer = SubProtocol.KmhDispatcher.IsKmhServer,
                ChatOn = Features.KmhFeatures.IsEnabled("chat"),
                MailOn = Features.KmhFeatures.IsEnabled("mail"),
                BlockingOn = ChatModerationCache.BlockingEnabled,
                BlockedHere = local.Length > 0 && ChatModerationCache.IsBlocked(local),
                BlockedEverywhere = id.Length > 0 && KmhFriendsClient.IsBlocked(id),
                Friendship = KmhFriendsClient.FriendshipWith(id),
            };
        }

        private static FloatMenuOption Option(Choice c, Facts f, string shown)
        {
            string label = Label(c);
            if (c.Enabled) return new FloatMenuOption(label, ActionFor(c, f, shown));

            string why = Reason(c.Why);
            var off = new FloatMenuOption(why.Length > 0 ? $"{label} ({why})" : label, null);
            string tip = Tip(c.Why);
            if (tip.Length > 0) off.tooltip = new TipSignal(tip);
            return off;
        }

        private static Action ActionFor(Choice c, Facts f, string shown)
        {
            string name = f.LocalName, id = f.GlobalId;
            Route route = c.Route;
            switch (c.Act)
            {
                case Act.Profile:      return () => Find.WindowStack.Add(new Features.PlayerStats.Dialog_KMHPlayerProfile(name));
                case Act.AddFriend:    return () => Sent(KmhFriendsClient.Request(id, shown));
                case Act.AcceptFriend: return () => Sent(KmhFriendsClient.Accept(id, shown));
                case Act.DeclineFriend:
                case Act.CancelRequest:
                case Act.RemoveFriend: return () => Sent(KmhFriendsClient.Remove(id));
                case Act.Message:      return () => Reach(route, name, id);
                case Act.Mail:         return () => Features.Mail.Dialog_KMHCompose.Open(name);
                case Act.BlockHere:    return () => ChatHandler.TryBlock(name, true);
                case Act.UnblockHere:  return () => ChatHandler.TryBlock(name, false);
                case Act.BlockEverywhere:
                    return () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KmhText.Get("KMH_Friends_BlockConfirm",
                            "Block {0}?\n\nThey stop being your friend, cannot ask again, and cannot see you.", shown),
                        () => Sent(KmhFriendsClient.Block(id, shown)), destructive: true));
                case Act.UnblockEverywhere: return () => Sent(KmhFriendsClient.Unblock(id));
                case Act.CopyId:
                    return () =>
                    {
                        UnityEngine.GUIUtility.systemCopyBuffer = id;
                        Notifications.KmhNotifications.Positive(KmhText.Get("KMH_PlayerMenu_IdCopied", "Copied {0}'s player ID.", shown));
                    };
                default: return null;
            }
        }

        public static void Message(string username, string globalId = null)
        {
            Facts f = Gather(new Target { LocalName = username ?? "", GlobalId = globalId ?? "", NameIsPlayer = true });
            foreach (Choice c in Plan(f))
            {
                if (c.Act != Act.Message) continue;
                if (c.Enabled) Reach(c.Route, f.LocalName, f.GlobalId);
                else Notifications.KmhNotifications.Rejected(Reason(c.Why));
                return;
            }
        }

        private static void Reach(Route route, string name, string id)
        {
            switch (route)
            {
                case Route.GlobalDm: Features.Comms.Dialog_KMHComms.OpenOn(ChatChannels.GlobalDm(id)); break;
                case Route.ServerDm: Features.Comms.Dialog_KMHComms.OpenOn(ChatChannels.Dm(KmhSession.Me, name)); break;
                case Route.Mail:     Features.Mail.Dialog_KMHCompose.Open(name); break;
            }
        }

        // One friends call at a time: a click that found the slot taken says so rather than vanishing.
        private static void Sent(bool sent)
        {
            if (!sent)
                Notifications.KmhNotifications.Rejected(KmhText.Get("KMH_PlayerMenu_Busy",
                    "Friends is still busy with your last request. Try again in a moment."));
        }

        private static string Label(Choice c)
        {
            switch (c.Act)
            {
                case Act.Profile:           return KmhText.Get("KMH_PlayerMenu_Profile", "View profile");
                case Act.AddFriend:         return KmhText.Get("KMH_PlayerMenu_AddFriend", "Add friend");
                case Act.AcceptFriend:      return KmhText.Get("KMH_PlayerMenu_AcceptFriend", "Accept friend request");
                case Act.DeclineFriend:     return KmhText.Get("KMH_PlayerMenu_DeclineFriend", "Decline friend request");
                case Act.CopyId:            return KmhText.Get("KMH_PlayerMenu_CopyId", "Copy player ID");
                case Act.CancelRequest:     return KmhText.Get("KMH_PlayerMenu_CancelRequest", "Cancel friend request");
                case Act.RemoveFriend:      return KmhText.Get("KMH_Friends_Remove", "Remove friend");
                case Act.Message:
                    return c.Route == Route.Mail ? KmhText.Get("KMH_PlayerMenu_MessageByMail", "Message by mail")
                                                 : KmhText.Get("KMH_PlayerMenu_Message", "Message");
                case Act.Mail:              return KmhText.Get("KMH_PlayerMenu_Mail", "Send mail");
                case Act.BlockHere:         return KmhText.Get("KMH_PlayerMenu_BlockHere", "Block on this server");
                case Act.UnblockHere:       return KmhText.Get("KMH_PlayerMenu_UnblockHere", "Unblock on this server");
                case Act.BlockEverywhere:   return KmhText.Get("KMH_PlayerMenu_BlockEverywhere", "Block everywhere");
                case Act.UnblockEverywhere: return KmhText.Get("KMH_PlayerMenu_UnblockEverywhere", "Unblock everywhere");
                case Act.Self:              return KmhText.Get("KMH_PlayerMenu_Self", "This is you");
                default:                    return KmhText.Get("KMH_PlayerMenu_NotAPlayer", "Not a Colony Nexus player");
            }
        }

        private static string Reason(Why why)
        {
            switch (why)
            {
                case Why.NotLinked:      return KmhText.Get("KMH_PlayerMenu_WhyNotLinked", "link your account first");
                case Why.NoGlobalId:     return KmhText.Get("KMH_PlayerMenu_WhyNoId", "needs their player ID");
                case Why.NotOnServer:    return KmhText.Get("KMH_PlayerMenu_WhyNotOnServer", "not on their server");
                case Why.MailOff:        return KmhText.Get("KMH_PlayerMenu_WhyMailOff", "mail is off here");
                case Why.ChatAndMailOff: return KmhText.Get("KMH_PlayerMenu_WhyNoRoute", "chat and mail are off here");
                case Why.BlockingOff:    return KmhText.Get("KMH_PlayerMenu_WhyBlockingOff", "blocking is off here");
                case Why.YouBlocked:     return KmhText.Get("KMH_PlayerMenu_WhyBlocked", "you blocked them");
                default:                 return "";
            }
        }

        private static string Tip(Why why)
        {
            switch (why)
            {
                case Why.NoGlobalId:
                    return KmhText.Get("KMH_PlayerMenu_NoIdTip",
                        "A name on a server does not say which Colony Nexus account it is. Ask them for the friend code in "
                      + "their Friends window, then type it into Quick Add in yours.");
                case Why.NotLinked:
                    return KmhText.Get("KMH_PlayerMenu_NotLinkedTip",
                        "Friends, messages across servers and blocking everywhere need a Colony Nexus account. Link one "
                      + "under Account & links.");
                default:
                    return "";
            }
        }
    }
}
