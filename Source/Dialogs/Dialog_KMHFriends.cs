using System;
using System.Collections.Generic;
using KMHPatch.Features.Account;
using KMHPatch.Features.Friends;
using KMHPatch.Features.Global;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    internal sealed class Dialog_KMHFriends : Window_KMHBase
    {
        public override Vector2 InitialSize => DialogLayout.FitToScreen(Mathf.Clamp(Verse.UI.screenWidth * 0.5f, 660f, 820f),
                                                                         Mathf.Clamp(Verse.UI.screenHeight * 0.9f, 600f, 800f));

        // The friends list belongs to the Global account, not to the server it was opened on.
        protected override bool ClosesOnSessionEnd => false;

        private const string AddField = "kmhFriendsAddId";
        private const int MaxTyped = 64;

        private enum Tab { Requests, Friends, Blocked }
        private enum Kind { Incoming, Outgoing, Friend, Blocked }

        private Tab _tab;
        private bool _tabChosen;
        private Vector2 _scroll;
        private string _addId = "", _sentFor;
        private bool _typing;
        private string _copied = "";
        private float _copiedUntil;

        public Dialog_KMHFriends()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        public static void Open()
        {
            Dialog_KMHFriends open = Find.WindowStack.WindowOfType<Dialog_KMHFriends>();
            if (open != null) { Find.WindowStack.Notify_ClickedInsideWindow(open); return; }
            KmhFriendsClient.ClearAdd();
            KmhFriendsClient.Refresh();
            if (KmhFriendsClient.MyCode == null) KmhFriendsClient.FetchCode();
            Find.WindowStack.Add(new Dialog_KMHFriends());
        }

        // Enter in the box sends the request rather than closing the window.
        protected override bool TypingInThisWindow => _typing;

        protected override void DrawContents(Rect inRect)
        {
            KmhFriendsClient.RefreshIfDue();

            float y = DialogLayout.DrawTitle(inRect, KmhText.Get("KMH_Friends_Title", "Friends"));
            if (DialogLayout.DrawCloseButton(inRect)) Close();
            Rect body = new Rect(inRect.x, y, inRect.width, Mathf.Max(0f, inRect.height - y - DialogLayout.FooterReserve));
            _typing = false;

            switch (KmhFriendsClient.State)
            {
                case FriendsState.NotLinked: DrawNotLinked(body); return;
                case FriendsState.Unknown:
                case FriendsState.Loading:
                    DialogLayout.DrawWaiting(new Rect(body.x, body.y + 4f, body.width, 40f),
                        KmhText.Get("KMH_Friends_Loading", "Asking Colony Nexus Global for your friends…"));
                    return;
            }

            bool down = KmhFriendsClient.State == FriendsState.Unavailable;
            if (down || KmhFriendsClient.Offline) y = DrawOfflineBanner(body, y, down);

            y = DrawTopCards(body, y, down);
            y += 8f;
            y = DrawTabs(body, y);
            DrawList(new Rect(body.x, y, body.width, Mathf.Max(0f, body.yMax - y)), down);
        }

        private void DrawNotLinked(Rect body)
        {
            string why = KmhText.Get("KMH_Friends_NotLinked",
                "Friends needs your Colony Nexus account. Link it once under Account & links; it takes about a minute and uses your Steam sign-in.");
            float h = DialogLayout.LabelWrapped(body.x, body.y, body.width, why);
            string open = KmhText.Get("KMH_Friends_OpenAccount", "Open Account & links");
            if (KmhBrand.Button(new Rect(body.x, body.y + h + 12f, DialogLayout.ButtonW(open, 180f, body.width), 32f), open, true))
            {
                Close();
                Dialog_KMHAccount.Open();
            }
        }

        private float DrawOfflineBanner(Rect body, float y, bool neverLoaded)
        {
            string text = neverLoaded
                ? KmhText.Get("KMH_Friends_Offline", "Colony Nexus Global cannot be reached, so friends cannot be loaded right now. Everything else keeps working.")
                : KmhText.Get("KMH_Friends_Stale", "Colony Nexus Global stopped answering; this list may be out of date.");
            string retry = KmhText.Get("KMH_Common_Retry", "Try again");
            float bw = DialogLayout.ButtonW(retry, 90f, 160f);
            float h = Mathf.Max(30f, Text.CalcHeight(text, body.width - bw - 24f) + 8f);
            Rect r = new Rect(body.x, y, body.width, h);
            Widgets.DrawBoxSolid(r, KmhBrand.WithAlpha(KmhBrand.Warn, 0.12f));
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), KmhBrand.Warn);
            Widgets.Label(new Rect(r.x + 10f, r.y + 4f, r.width - bw - 24f, r.height - 8f), text);
            if (KmhBrand.Button(new Rect(r.xMax - bw - 6f, r.y + (h - 26f) / 2f, bw, 26f), retry, false) && !KmhFriendsClient.Busy)
                KmhFriendsClient.Refresh();
            return y + h + 8f;
        }

        private float DrawTopCards(Rect body, float y, bool down)
        {
            // Side by side only where both cards keep their values whole; at 820 the code and ID get cut off.
            bool side = body.width >= 900f;
            float shareW = side ? Mathf.Clamp(body.width * 0.42f, 380f, 460f) : body.width;
            float addW = side ? body.width - shareW - 10f : body.width;
            float addH = QuickAddHeight(addW), shareH = ShareHeight(shareW);
            float h = side ? Mathf.Max(addH, shareH) : addH;

            DrawQuickAdd(new Rect(body.x, y, addW, h), down);
            if (side) { DrawShare(new Rect(body.x + addW + 10f, y, shareW, h)); return y + h; }
            DrawShare(new Rect(body.x, y + addH + 8f, body.width, shareH));
            return y + addH + 8f + shareH;
        }

        private static void Card(Rect r, bool primary)
        {
            Widgets.DrawBoxSolid(r, KmhBrand.WithAlpha(KmhBrand.Surface2, 0.85f));
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), primary ? KmhBrand.Accent : KmhBrand.LineStrong);
        }

        private static string Heading(string s) => $"<b>{s}</b>";

        private float QuickAddHeight(float w)
        {
            float row = DialogLayout.TextRowH;
            return 10f + row + Text.CalcHeight(QuickAddHelp(), w - 24f) + 6f + 30f + 6f + StatusH(w - 24f) + 10f;
        }

        // Grows to two lines, or the advice after "not confirmed..." is cut off.
        private float StatusH(float w)
        {
            string s = Status().text;
            return Mathf.Max(DialogLayout.TextRowH, string.IsNullOrEmpty(s) ? 0f : Text.CalcHeight(s, w));
        }

        private static string QuickAddHelp() => KmhText.Get("KMH_Friends_Add_Help",
            "Paste the friend code, player ID or Colony Nexus username a friend gave you. They do not need to be your Steam friend.");

        private void DrawQuickAdd(Rect r, bool down)
        {
            Card(r, true);
            float x = r.x + 12f, w = r.width - 24f, y = r.y + 10f, row = DialogLayout.TextRowH;
            DialogLayout.LabelTrunc(new Rect(x, y, w, row), Heading(KmhText.Get("KMH_Friends_Add_Title", "Quick Add")));
            y += row;
            GUI.color = DialogLayout.MutedColor;
            float helpH = Text.CalcHeight(QuickAddHelp(), w);
            Widgets.Label(new Rect(x, y, w, helpH), QuickAddHelp());
            GUI.color = Color.white;
            y += helpH + 6f;

            string paste = KmhText.Get("KMH_Friends_Add_Paste", "Paste");
            string send = KmhFriendsClient.AddState == KmhFriendsClient.AddStage.Sending
                ? KmhText.Get("KMH_Friends_Add_SendingShort", "Sending…") : KmhText.Get("KMH_Friends_Add", "Send request");
            float pasteW = DialogLayout.ButtonW(paste, 64f, 110f), sendW = DialogLayout.ButtonW(send, 120f, 180f);
            Rect field = new Rect(x, y, Mathf.Max(60f, w - pasteW - sendW - 12f), 30f);

            // Read before the field is laid out, or the field sees the key first.
            Event e = Event.current;
            bool enter = e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                      && GUI.GetNameOfFocusedControl() == AddField;

            if (KmhFriendsClient.AddState == KmhFriendsClient.AddStage.Sent && _sentFor != null && _addId == _sentFor) { _addId = ""; _sentFor = null; }

            GUI.SetNextControlName(AddField);
            string typed = Widgets.TextField(field, _addId ?? "");
            if (typed.Length > MaxTyped) typed = typed.Substring(0, MaxTyped);
            if (typed != _addId) { _addId = typed; KmhFriendsClient.ClearAdd(); KmhFriendsClient.ClearActionError(); }
            _typing = GUI.GetNameOfFocusedControl() == AddField;
            if (_addId.Length == 0 && !_typing)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.38f);
                DialogLayout.LabelTrunc(new Rect(field.x + 7f, field.y, field.width - 10f, field.height),
                    KmhText.Get("KMH_Friends_Add_Placeholder", "e.g. KX7P-2M9Q, a player ID or a username"), TextAnchor.MiddleLeft);
                GUI.color = Color.white;
            }

            Rect pasteR = new Rect(field.xMax + 6f, y, pasteW, 30f);
            if (KmhBrand.Button(pasteR, paste, false, KmhText.Get("KMH_Friends_Add_PasteTip", "Put what you copied into the box.")))
            {
                string clip = "";
                try { clip = (GUIUtility.systemCopyBuffer ?? "").Trim(); } catch { }
                if (clip.Length > MaxTyped) clip = clip.Substring(0, MaxTyped);
                if (clip.Length > 0) { _addId = clip; KmhFriendsClient.ClearAdd(); KmhFriendsClient.ClearActionError(); }
                GUI.FocusControl(AddField);
            }

            string why = SendBlocker(down);
            bool can = why == null;
            Rect sendR = new Rect(pasteR.xMax + 6f, y, sendW, 30f);
            bool clicked = can ? KmhBrand.Button(sendR, send, true) : DrawDisabled(sendR, send, why);
            if (enter) e.Use();
            if ((clicked || (enter && can)) && can && KmhFriendsClient.QuickAdd(_addId))
            {
                _sentFor = _addId;
                // Unfocused, or the field keeps showing its own copy of the text after a confirmed send clears it.
                GUI.FocusControl(null);
            }
            y += 36f;

            DrawAddStatus(new Rect(x, y, w, StatusH(w)));
        }

        // Null when Send may go; else the reason, shown on hover so the button is never silently dead.
        private string SendBlocker(bool down)
        {
            if (down) return KmhText.Get("KMH_Friends_Add_Down", "Colony Nexus Global is not answering.");
            if ((_addId ?? "").Trim().Length == 0) return KmhText.Get("KMH_Friends_Add_Empty", "Type or paste a friend code, player ID or username first.");
            if (KmhFriendsClient.AddState == KmhFriendsClient.AddStage.Sending) return KmhText.Get("KMH_Friends_Add_Wait", "Waiting for Colony Nexus Global to answer.");
            if (KmhFriendsClient.Busy) return KmhText.Get("KMH_Friends_Add_Busy", "Updating your friends list; one moment.");
            string own = KmhAccountStore.PlayerGlobalId;
            if (KmhFriendsClient.NormalizeId(_addId) is string id && id.Length > 0 && id == (own ?? ""))
                return KmhFriendsClient.RefusalText("KMH-FRD-753");
            return null;
        }

        private static bool DrawDisabled(Rect r, string label, string why)
        {
            Widgets.DrawBoxSolid(r, KmhBrand.WithAlpha(KmhBrand.Surface2, 0.5f));
            GUI.color = KmhBrand.Line; Widgets.DrawBox(r, 1); GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(r, label, TextAnchor.MiddleCenter);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(r, why);
            return false;
        }

        private void DrawAddStatus(Rect r)
        {
            (string text, Color c) = Status();
            if (string.IsNullOrEmpty(text)) return;
            GUI.color = c;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(r, text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private (string text, Color color) Status()
        {
            switch (KmhFriendsClient.AddState)
            {
                case KmhFriendsClient.AddStage.Sending: return (KmhFriendsClient.AddMessage, DialogLayout.MutedColor);
                case KmhFriendsClient.AddStage.Sent:    return ("✓ " + KmhFriendsClient.AddMessage, KmhBrand.Ok);
                case KmhFriendsClient.AddStage.Refused:
                case KmhFriendsClient.AddStage.Failed:  return (KmhFriendsClient.AddMessage, KmhBrand.Bad);
                default:                                 return (Hint(_addId), DialogLayout.MutedColor);
            }
        }

        // A typed value read back so a typo shows before sending; Global decides what it really is.
        internal static string Hint(string typed)
        {
            string t = (typed ?? "").Trim();
            if (t.Length == 0) return "";
            if (KmhFriendsClient.NormalizeId(t).Length > 0) return KmhText.Get("KMH_Friends_Hint_Id", "A player ID. Press Send request.");
            string bare = t.Replace("-", "").Replace(" ", "");
            bool hexish = bare.Length > 12;
            foreach (char ch in bare) if (!Uri.IsHexDigit(ch)) { hexish = false; break; }
            if (hexish) return KmhText.Get("KMH_Friends_Hint_Partial", "A player ID has 32 letters and digits; this one has {0}.", bare.Length);
            if (bare.Length == 8 && t.Length <= 9) return KmhText.Get("KMH_Friends_Hint_Code", "Looks like a friend code. Press Send request.");
            return KmhText.Get("KMH_Friends_Hint_Name", "Will look for a player with this Colony Nexus username.");
        }

        private float ShareHeight(float w)
        {
            float row = DialogLayout.TextRowH;
            return 10f + row + 30f + 4f + 30f + 6f + Text.CalcHeight(ShareHelp(), w - 24f) + 10f;
        }

        private static string ShareHelp() => KmhText.Get("KMH_Friends_Share_Help",
            "Give a friend either one. Your code can be replaced if it ends up somewhere you did not want.");

        private void DrawShare(Rect r)
        {
            Card(r, false);
            float x = r.x + 12f, w = r.width - 24f, y = r.y + 10f, row = DialogLayout.TextRowH;
            DialogLayout.LabelTrunc(new Rect(x, y, w, row), Heading(KmhText.Get("KMH_Friends_Share_Title", "Share with a friend")));
            y += row;

            string code = KmhFriendsClient.MyCode;
            string codeShown = code == null ? KmhText.Get("KMH_Friends_Share_CodeLoading", "loading…")
                             : code.Length == 0 ? KmhText.Get("KMH_Friends_Share_NoCode", "not offered by this Global")
                             : KmhFriendsClient.FormatCode(code);
            ShareRow(new Rect(x, y, w, 30f), KmhText.Get("KMH_Friends_Share_Code", "Friend code"), codeShown,
                     string.IsNullOrEmpty(code) ? null : KmhFriendsClient.FormatCode(code), "code");
            y += 34f;

            string id = KmhAccountStore.PlayerGlobalId ?? "";
            string idShown = id.Length > 12 ? id.Substring(0, 6) + "…" + id.Substring(id.Length - 6) : id;
            Rect idRow = new Rect(x, y, w, 30f);
            ShareRow(idRow, KmhText.Get("KMH_Friends_Share_Id", "Player ID"), idShown, id.Length > 0 ? id : null, "id");
            if (id.Length > 0) TooltipHandler.TipRegion(new Rect(idRow.x, idRow.y, idRow.width * 0.7f, idRow.height), id);
            y += 36f;

            GUI.color = DialogLayout.MutedColor;
            Widgets.Label(new Rect(x, y, w, Text.CalcHeight(ShareHelp(), w)), ShareHelp());
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(code))
            {
                string renew = KmhText.Get("KMH_Friends_Share_Renew", "New code");
                float rw = DialogLayout.ButtonW(renew, 80f, 120f);
                if (KmhBrand.Button(new Rect(r.xMax - rw - 10f, r.y + 8f, rw, 22f), renew, false))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KmhText.Get("KMH_Friends_Share_RenewConfirm", "Replace your friend code? The old one stops working at once; people you already gave it to will need the new one."),
                        () => KmhFriendsClient.FetchCode(regenerate: true), destructive: false));
            }
        }

        private void ShareRow(Rect r, string label, string shown, string copyValue, string key)
        {
            float labelW = Mathf.Min(110f, r.width * 0.34f);
            string copy = Time.realtimeSinceStartup < _copiedUntil && _copied == key
                ? KmhText.Get("KMH_Friends_Copied", "Copied ✓") : KmhText.Get("KMH_Friends_Copy", "Copy");
            float copyW = DialogLayout.ButtonW(copy, 72f, 110f);
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(r.x, r.y, labelW, r.height), label, TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            Rect valueR = new Rect(r.x + labelW, r.y, Mathf.Max(0f, r.width - labelW - copyW - 6f), r.height);
            if (copyValue == null)
            {
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(valueR, KmhDisplayText.Inert(shown ?? "", 60), TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                return;
            }
            Text.Font = GameFont.Medium;
            DialogLayout.LabelTrunc(valueR, KmhDisplayText.Inert(shown ?? "", 40), TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            if (KmhBrand.Button(new Rect(r.xMax - copyW, r.y + 2f, copyW, r.height - 4f), copy, false))
            {
                try { GUIUtility.systemCopyBuffer = copyValue; } catch { }
                _copied = key; _copiedUntil = Time.realtimeSinceStartup + 2.5f;
            }
        }

        private float DrawTabs(Rect body, float y)
        {
            int incoming = KmhFriendsClient.Incoming.Count, outgoing = KmhFriendsClient.Outgoing.Count;
            if (!_tabChosen) _tab = incoming > 0 ? Tab.Requests : Tab.Friends;
            string[] labels =
            {
                incoming > 0 ? KmhText.Get("KMH_Friends_Tab_RequestsNew", "Requests ({0} new)", incoming)
                             : KmhText.Get("KMH_Friends_Tab_Requests", "Requests ({0})", incoming + outgoing),
                KmhText.Get("KMH_Friends_Tab_Friends", "Friends ({0})", KmhFriendsClient.Friends.Count),
                KmhText.Get("KMH_Friends_Tab_Blocked", "Blocked ({0})", KmhFriendsClient.Blocked.Count),
            };
            float x = body.x;
            for (int i = 0; i < labels.Length; i++)
            {
                float w = DialogLayout.ButtonW(labels[i], 110f, 220f);
                if (DialogLayout.DrawSegment(new Rect(x, y, w, 28f), labels[i], (int)_tab == i))
                {
                    _tab = (Tab)i; _tabChosen = true; _scroll = Vector2.zero;
                }
                if (i == 0 && incoming > 0) KmhBrand.DrawDisc(new Rect(x + w - 10f, y - 3f, 9f, 9f), KmhBrand.Accent);
                x += w + 4f;
            }
            DrawSteam(new Rect(x + 8f, y, body.xMax - x - 8f, 28f));
            return y + 34f;
        }

        // Steam stays optional: never a step in adding someone.
        private static void DrawSteam(Rect r)
        {
            if (!KmhSteamDiscoveryClient.Offered || r.width < 120f) return;
            KmhSteamDiscoveryClient.RunOnceIfOffered();
            string find = KmhText.Get("KMH_SteamFind_Button", "Find Steam friends");
            float bw = DialogLayout.ButtonW(find, 130f, r.width);
            Rect b = new Rect(r.xMax - bw, r.y, bw, r.height);
            if (!KmhSteamDiscoveryClient.Busy && KmhBrand.Button(b, find, false)) KmhSteamDiscoveryClient.Run();
            string said = KmhSteamDiscoveryClient.Summary();
            // Shown beside the button only where it fits whole; a cut-off "Couldn't reach Col..." read as an error.
            bool fits = said.Length > 0 && Text.CalcSize(said).x <= r.width - bw - 8f;
            TooltipHandler.TipRegion(b, said.Length > 0 && !fits ? said + "\n\n" + KmhSteamDiscoveryClient.Explain() : KmhSteamDiscoveryClient.Explain());
            if (fits) DialogLayout.LabelTrunc(new Rect(r.x, r.y, r.width - bw - 6f, r.height), $"<color=#9AA4B2>{said}</color>", TextAnchor.MiddleRight);
        }

        private void DrawList(Rect rect, bool down)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);
            var sections = _sections;
            sections.Clear();
            switch (_tab)
            {
                case Tab.Requests:
                    sections.Add((KmhText.Get("KMH_Friends_Incoming", "Asking to be your friend"), KmhFriendsClient.Incoming, Kind.Incoming));
                    sections.Add((KmhText.Get("KMH_Friends_Outgoing", "Sent by you, waiting for an answer"), KmhFriendsClient.Outgoing, Kind.Outgoing));
                    break;
                case Tab.Friends: sections.Add((null, Sorted(KmhFriendsClient.Friends), Kind.Friend)); break;
                default:          sections.Add((null, KmhFriendsClient.Blocked, Kind.Blocked)); break;
            }

            float rowH = Mathf.Max(36f, DialogLayout.TextRowH + 14f), headH = DialogLayout.TextRowH + 6f;
            float height = 0f; int total = 0;
            foreach (var s in sections) { if (s.rows.Count == 0) continue; total += s.rows.Count; height += (s.title != null ? headH : 0f) + s.rows.Count * (rowH + 2f) + 6f; }

            if (total == 0) { DrawEmpty(inner); return; }

            Rect view = new Rect(0f, 0f, Mathf.Max(1f, inner.width - DialogLayout.ScrollbarReserveWidth), Mathf.Max(height, inner.height));
            _scroll.y = KmhScroll.Clamp(_scroll.y, view.height, inner.height);
            Widgets.BeginScrollView(inner, ref _scroll, view);
            float y = 0f;
            HashSet<string> dupes = DuplicateNames();
            foreach (var s in sections)
            {
                if (s.rows.Count == 0) continue;
                if (s.title != null)
                {
                    GUI.color = s.kind == Kind.Incoming ? KmhBrand.Accent : DialogLayout.MutedColor;
                    DialogLayout.LabelTrunc(new Rect(2f, y, view.width, headH), $"{s.title} ({s.rows.Count})", TextAnchor.MiddleLeft);
                    GUI.color = Color.white;
                    y += headH;
                }
                foreach (FriendDto f in s.rows)
                {
                    DrawRow(new Rect(0f, y, view.width, rowH), f, s.kind, down, dupes);
                    y += rowH + 2f;
                }
                y += 6f;
            }
            Widgets.EndScrollView();
        }

        private void DrawEmpty(Rect r)
        {
            string text;
            switch (_tab)
            {
                case Tab.Requests: text = KmhText.Get("KMH_Friends_EmptyRequests", "No requests waiting. Requests you send or receive show up here."); break;
                case Tab.Blocked:  text = KmhText.Get("KMH_Friends_EmptyBlocked", "You have not blocked anyone."); break;
                default:           text = KmhText.Get("KMH_Friends_Empty", "No friends yet. Share your friend code, or paste a friend's into Quick Add above."); break;
            }
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelWrapped(r.x + 6f, r.y + 6f, r.width - 12f, text);
            GUI.color = Color.white;
        }

        private readonly List<(string title, List<FriendDto> rows, Kind kind)> _sections = new List<(string title, List<FriendDto> rows, Kind kind)>();
        private readonly List<FriendDto> _sorted = new List<FriendDto>();
        private readonly HashSet<string> _seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dupeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private List<FriendDto> Sorted(List<FriendDto> rows)
        {
            List<FriendDto> list = _sorted;
            list.Clear();
            list.AddRange(rows);
            list.Sort((a, b) => a.Online != b.Online ? (a.Online ? -1 : 1) : string.Compare(a.Shown, b.Shown, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // Two people may carry one name; the end of their id tells them apart.
        private HashSet<string> DuplicateNames()
        {
            HashSet<string> seen = _seenNames, dupes = _dupeNames;
            seen.Clear();
            dupes.Clear();
            foreach (List<FriendDto> rows in new[] { KmhFriendsClient.Friends, KmhFriendsClient.Incoming, KmhFriendsClient.Outgoing, KmhFriendsClient.Blocked })
                foreach (FriendDto f in rows) if (!seen.Add(f.Shown)) dupes.Add(f.Shown);
            return dupes;
        }

        private void DrawRow(Rect rect, FriendDto f, Kind kind, bool down, HashSet<string> dupes)
        {
            Widgets.DrawBoxSolid(rect, KmhBrand.WithAlpha(KmhBrand.Surface2, 0.55f));
            Widgets.DrawHighlightIfMouseover(rect);
            bool busy = KmhFriendsClient.Busy || down;
            string busyWhy = down ? KmhText.Get("KMH_Friends_Add_Down", "Colony Nexus Global is not answering.")
                                  : KmhText.Get("KMH_Friends_Add_Busy", "Updating your friends list; one moment.");
            float right = rect.xMax - 6f;
            float bh = rect.height - 10f;

            if (kind != Kind.Blocked && RowButton(ref right, rect, "⋯", false, true, 34f, KmhText.Get("KMH_Friends_MoreTip", "More: remove, block and other actions")))
                KmhPlayerMenu.Open(null, f.PlayerGlobalId, f.Shown);

            switch (kind)
            {
                case Kind.Incoming:
                    if (RowButton(ref right, rect, KmhText.Get("KMH_Friends_Decline", "Decline"), false, !busy, 0f, busy ? busyWhy : null)) KmhFriendsClient.Remove(f.PlayerGlobalId);
                    if (RowButton(ref right, rect, KmhText.Get("KMH_Friends_Accept", "Accept"), true, !busy, 0f, busy ? busyWhy : null)) KmhFriendsClient.Accept(f.PlayerGlobalId, f.Shown);
                    break;
                case Kind.Outgoing:
                    if (RowButton(ref right, rect, KmhText.Get("KMH_Friends_CancelRequest", "Cancel request"), false, !busy, 0f, busy ? busyWhy : null)) KmhFriendsClient.Remove(f.PlayerGlobalId);
                    break;
                case Kind.Friend:
                    if (RowButton(ref right, rect, KmhText.Get("KMH_PlayerMenu_Message", "Message"), false, !down, 0f, down ? busyWhy : null))
                        KmhPlayerMenu.Message(null, f.PlayerGlobalId);
                    DrawJoin(ref right, rect, f);
                    break;
                case Kind.Blocked:
                    if (RowButton(ref right, rect, KmhText.Get("KMH_Friends_Unblock", "Unblock"), false, !busy, 0f, busy ? busyWhy : null)) KmhFriendsClient.Unblock(f.PlayerGlobalId);
                    break;
            }

            float x = rect.x + 10f;
            if (kind == Kind.Friend)
            {
                KmhBrand.DrawDisc(new Rect(x, rect.center.y - 5f, 10f, 10f), f.Online ? KmhBrand.Ok : KmhBrand.WithAlpha(KmhBrand.Ring, 0.35f));
                x += 18f;
            }
            string name = f.Shown + (dupes.Contains(f.Shown) ? $"  <color=#9AA4B2>#{Tail(f.PlayerGlobalId)}</color>" : "");
            float cellsW = Mathf.Max(0f, right - 8f - x);
            float nameW = kind == Kind.Friend ? cellsW * 0.5f : cellsW;
            DialogLayout.LabelTrunc(new Rect(x, rect.y, nameW, rect.height), $"<b>{name}</b>", TextAnchor.MiddleLeft);
            if (kind != Kind.Friend) return;
            GUI.color = f.Online ? KmhBrand.Ok : DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(x + nameW + 6f, rect.y, Mathf.Max(0f, cellsW - nameW - 6f), rect.height), StateLine(f), TextAnchor.MiddleLeft);
            GUI.color = Color.white;
        }

        private static string Tail(string id) => string.IsNullOrEmpty(id) || id.Length < 4 ? id ?? "" : id.Substring(id.Length - 4);

        private static bool RowButton(ref float right, Rect row, string label, bool primary, bool active, float width, string tip)
        {
            float w = width > 0f ? width : DialogLayout.ButtonW(label, 72f, 160f);
            Rect r = new Rect(right - w, row.y + 5f, w, row.height - 10f);
            right = r.x - 6f;
            return active ? KmhBrand.Button(r, label, primary, tip) : DrawDisabled(r, label, tip ?? "");
        }

        // Only what the friend's privacy let Global send: online and at most a server id, never an address.
        private static string StateLine(FriendDto f)
        {
            if (f.State == "Blocked") return KmhText.Get("KMH_Friends_State_Blocked", "blocked");
            if (!f.Online) return KmhText.Get("KMH_Friends_State_Offline", "offline");
            ServerCardDto card = KmhFriendsClient.CardFor(f);
            if (card != null) return KmhText.Get("KMH_Friends_State_Playing", "playing on {0}", KmhDisplayText.Inert(card.Name, 40));
            if (!string.IsNullOrEmpty(f.ServerId)) return KmhText.Get("KMH_Friends_State_OnServer", "on a Colony Nexus server");
            return KmhText.Get("KMH_Friends_State_Online", "online");
        }

        // The browser's own join gate, so a full, passworded or incompatible server refuses here too.
        private static void DrawJoin(ref float right, Rect rect, FriendDto f)
        {
            ServerCardDto card = KmhFriendsClient.CardFor(f);
            if (card == null) return;
            Features.Discord.JoinDecision d = Features.Discord.KmhJoinGate.MayJoin(
                card, directoryFresh: true, source: Features.Discord.JoinSource.Browser);
            string join = KmhText.Get("KMH_Browser_Join", "Join");
            if (RowButton(ref right, rect, join, true, d.Allowed, 0f, d.Allowed ? null : d.Explain(card))) JoinServer(card);
        }

        private static void JoinServer(ServerCardDto card)
        {
            if (!card.TrySplitEndpoint(out string host, out int port)) return;
            Features.Servers.SeenServersStore.Record(card.ServerId, card.Name, card.JoinEndpoint,
                                                     card.KmhVersion, card.RwtVersion);
            if (!RwtCompat.ConnectTo(host, port, card.ServerId))
                Messages.Message(KmhText.Get("KMH_Browser_Join_Failed",
                    "RimWorld Together would not start the connection."), MessageTypeDefOf.RejectInput, false);
        }
    }
}
