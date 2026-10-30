using System;
using System.Collections.Generic;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Leaderboards;
using KMHPatch.Features.Leaderboards.Dto;
using KMHPatch.Features.PlayerStats;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Standings
{
    // The server's own ranking as it arrived; nothing here re-sorts or re-scores, and a search keeps real ranks.
    internal sealed class LeaderboardsView
    {
        private static readonly Color Gold = new Color(0.96f, 0.72f, 0.24f), Silver = new Color(0.74f, 0.78f, 0.84f), Bronze = new Color(0.79f, 0.49f, 0.25f);
        private static readonly Color Muted = new Color(0.66f, 0.69f, 0.73f), Verified = new Color(0.36f, 0.80f, 0.55f), Reported = new Color(0.36f, 0.72f, 0.78f);

        private string _key = "";
        private string _search = "";
        private Vector2 _catScroll, _rowScroll;
        private DateTime _askedUtc = DateTime.MinValue;

        // The rows a search leaves, rebuilt only when the board, the search or the snapshot changes.
        private List<LeaderboardRow> _view; private string _viewKey = "";

        public void Asked() => _askedUtc = DateTime.UtcNow;

        internal Action<string> OpenProfile;
        internal string Selected;

        private void Open(string name)
        {
            if (OpenProfile != null) OpenProfile(name);
            else Find.WindowStack.Add(new Dialog_KMHPlayerProfile(name));
        }

        public void Draw(Rect c)
        {
            LeaderboardsSnapshot snap = LeaderboardCache.Snapshot;
            if (snap == null || snap.Boards == null || snap.Boards.Count == 0)
            {
                bool waited = _askedUtc != DateTime.MinValue && (DateTime.UtcNow - _askedUtc).TotalSeconds > 8;
                if (waited)
                    DialogLayout.LabelWrapped(c.x + 6f, c.y + 6f, c.width - 12f,
                        "<color=#A9AFB9>" + KmhText.Get("KMH_Lb_NotSent", "This server does not send leaderboards yet (its Colony Nexus server addon needs an update). The other sections on the left still work.") + "</color>");
                else DialogLayout.DrawWaiting(new Rect(c.x + 6f, c.y + 6f, c.width - 12f, 32f), KmhText.Get("KMH_Lb_Asking", "Asking the server for its leaderboards…"));
                return;
            }

            LeaderboardBoard board = LeaderboardCache.Board(_key) ?? Lead(snap);
            _key = board?.Key ?? "";

            float catW = Mathf.Clamp(c.width * 0.24f, 150f, 210f);
            Rect cats = new Rect(c.x, c.y, catW, c.height);
            Widgets.DrawMenuSection(cats);
            DrawCategories(cats.ContractedBy(6f), snap);
            if (board != null) DrawBoard(new Rect(c.x + catW + 10f, c.y, c.width - catW - 10f, c.height), board, snap);
        }

        private static LeaderboardBoard Lead(LeaderboardsSnapshot snap)
        {
            foreach (string k in new[] { "economy", "trade", "contracts", "guild-members" })
                foreach (LeaderboardBoard b in snap.Boards) if (b.Key == k && b.Rows.Count > 0) return b;
            foreach (LeaderboardBoard b in snap.Boards) if (b.Rows.Count > 0) return b;
            return snap.Boards[0];
        }

        private void DrawCategories(Rect inner, LeaderboardsSnapshot snap)
        {
            const float rowH = 26f, groupH = 22f;
            float contentH = 0f; string last = null;
            foreach (LeaderboardBoard b in snap.Boards) { if (b.Group != last) { contentH += groupH; last = b.Group; } contentH += rowH; }
            Rect view = new Rect(0f, 0f, inner.width - (contentH > inner.height ? DialogLayout.ScrollbarReserveWidth : 0f), Mathf.Max(contentH, inner.height));

            Widgets.BeginScrollView(inner, ref _catScroll, view);
            float y = 0f; last = null;
            foreach (LeaderboardBoard b in snap.Boards)
            {
                if (b.Group != last)
                {
                    last = b.Group;
                    Text.Font = GameFont.Tiny;
                    GUI.color = Muted;
                    Widgets.Label(new Rect(4f, y + 5f, view.width - 8f, groupH), Leaderboards.LeaderboardText.Group(b.Group).ToUpperInvariant());
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    y += groupH;
                }
                Rect r = new Rect(0f, y, view.width, rowH - 2f);
                bool active = b.Key == _key;
                if (active)
                {
                    Widgets.DrawBoxSolid(r, KmhBrand.WithAlpha(KmhBrand.Accent, 0.16f));
                    Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), KmhBrand.Accent);
                }
                Widgets.DrawHighlightIfMouseover(r);
                if (b.Rows.Count == 0) GUI.color = Muted;
                // One line: a long translated name would wrap into the row above.
                DialogLayout.LabelTrunc(new Rect(r.x + 8f, r.y, r.width - 44f, r.height), Leaderboards.LeaderboardText.Title(b), TextAnchor.MiddleLeft);
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Muted;
                Widgets.Label(new Rect(r.x, r.y, r.width - 6f, r.height), b.Total.ToString("N0"));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(r) && !active) { _key = b.Key; _rowScroll = Vector2.zero; _search = ""; }
                y += rowH;
            }
            Widgets.EndScrollView();
        }

        private void DrawBoard(Rect r, LeaderboardBoard b, LeaderboardsSnapshot snap)
        {
            float y = r.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, y, r.width - 230f, 32f), Leaderboards.LeaderboardText.Title(b));
            Text.Font = GameFont.Small;

            // Where the number comes from, said once: a colony's own report reads differently from a server measurement.
            string badge = b.Reported ? KmhText.Get("KMH_Lb_BadgeReported", "Reported by players' games") : KmhText.Get("KMH_Lb_BadgeVerified", "Verified by the server");
            float bw = Text.CalcSize(badge).x + 24f;
            Rect br = new Rect(r.xMax - bw, y + 4f, bw, 24f);
            Widgets.DrawBoxSolid(br, (b.Reported ? Reported : Verified) * new Color(1f, 1f, 1f, 0.18f));
            GUI.color = b.Reported ? Reported : Verified;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(br, badge);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (Mouse.IsOver(br))
                TooltipHandler.TipRegion(br, b.Reported ? KmhText.Get("KMH_Lb_BadgeReportedTip", "Each player's game sends this number; the server only checks it is plausible.")
                                                       : KmhText.Get("KMH_Lb_BadgeVerifiedTip", "The server measured this itself; no game can change it."));
            y += 34f;

            string description = Leaderboards.LeaderboardText.Description(b);
            if (!string.IsNullOrEmpty(description))
            {
                DialogLayout.LabelTrunc(new Rect(r.x, y, r.width, 20f), $"<color=#A9AFB9>{KmhDisplayText.Inert(description, 200)}</color>");
                y += 22f;
            }

            // Always three places, so the podium keeps its shape before anyone is ranked.
            float podiumH = Mathf.Clamp(r.height * 0.36f, 150f, 184f);
            DrawPodium(new Rect(r.x, y, r.width, podiumH), b);
            y += podiumH + 8f;

            // The search narrows the list below; the podium above is always the board's own top three.
            Rect sr = new Rect(r.x, y, Mathf.Min(320f, r.width * 0.5f), 26f);
            GUI.SetNextControlName("lbSearch");
            _search = Widgets.TextField(sr, _search ?? "");
            if (string.IsNullOrEmpty(_search) && GUI.GetNameOfFocusedControl() != "lbSearch")
            {
                GUI.color = Muted;
                Widgets.Label(new Rect(sr.x + 6f, sr.y + 3f, sr.width, sr.height),
                              b.IsGuildBoard ? KmhText.Get("KMH_Lb_FindGuild", "Find a guild…") : KmhText.Get("KMH_Lb_FindPlayer", "Find a player or guild…"));
                GUI.color = Color.white;
            }
            string site = Leaderboards.LeaderboardCache.WebsitePage(snap.Page, Global.GlobalHttp.BaseUrl, b.Key);
            string open = KmhText.Get("KMH_Lb_OpenWebsite", "Open on the website");
            float siteW = site.Length > 0 ? DialogLayout.ButtonW(open, 160f, 240f) : 0f;
            string meta = "<color=#A9AFB9>" + KmhText.Get("KMH_Lb_Meta", "{0} ranked · Season {1}", b.Total.ToString("N0"), snap.Season) + "</color>";
            DialogLayout.LabelTrunc(new Rect(sr.xMax + 8f, y, Mathf.Max(0f, r.xMax - sr.xMax - 16f - siteW), 26f), meta, TextAnchor.MiddleRight);
            if (site.Length > 0)
            {
                Rect wb = new Rect(r.xMax - siteW, y, siteW, 26f);
                if (KmhBrand.Button(wb, open, false)) Application.OpenURL(site);
                TooltipHandler.TipRegion(wb, site);
            }
            y += 32f;

            Rect list = new Rect(r.x, y, r.width, r.yMax - y);
            Widgets.DrawMenuSection(list);
            DrawRows(list, b);
        }

        private static Color MedalColor(int rank) => rank <= 1 ? Gold : rank == 2 ? Silver : Bronze;

        // The website's and Discord's podium: second, first, third on steps, each with its player's own colour.
        private void DrawPodium(Rect r, LeaderboardBoard b)
        {
            const float gap = 10f;
            float w = (r.width - gap * 2f) / 3f;
            int[] order = { 1, 0, 2 };
            // Floors so every step still holds its numeral at small heights.
            float[] step = { Mathf.Max(44f, r.height * 0.27f), Mathf.Max(36f, r.height * 0.20f), Mathf.Max(30f, r.height * 0.15f) };
            Text.Font = GameFont.Tiny;
            float tinyH = DialogLayout.TextRowH;
            Text.Font = GameFont.Small;
            float smallH = DialogLayout.TextRowH;
            for (int slot = 0; slot < 3; slot++)
            {
                int i = order[slot];
                LeaderboardRow row = i < b.Rows.Count ? b.Rows[i] : null;
                Color medal = MedalColor(row?.Rank ?? i + 1);
                float x = r.x + slot * (w + gap);
                Rect plinth = new Rect(x, r.yMax - step[i], w, step[i]);
                Widgets.DrawBoxSolid(plinth, new Color(medal.r, medal.g, medal.b, row == null ? 0.05f : 0.20f));
                Widgets.DrawBoxSolid(new Rect(plinth.x, plinth.y, plinth.width, 3f), row == null ? new Color(1f, 1f, 1f, 0.10f) : medal);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = row == null ? new Color(medal.r, medal.g, medal.b, 0.35f) : medal;
                Widgets.Label(plinth, (row?.Rank ?? i + 1).ToString());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                float yb = plinth.y - 4f;
                Rect detail = new Rect(x + 4f, yb - tinyH, w - 8f, tinyH); yb = detail.y;
                Rect value = new Rect(x + 4f, yb - smallH, w - 8f, smallH); yb = value.y;
                string second = row == null ? "" : Second(b, row);
                Rect sub = new Rect(x + 4f, yb - (second.Length > 0 ? tinyH : 0f), w - 8f, tinyH); yb = second.Length > 0 ? sub.y : yb;
                Rect name = new Rect(x + 4f, yb - smallH, w - 8f, smallH); yb = name.y;
                float d = Mathf.Min(i == 0 ? 34f : 30f, Mathf.Max(18f, yb - r.y - 2f));
                Rect disc = new Rect(x + w / 2f - d / 2f, yb - d - 2f, d, d);

                if (row == null)
                {
                    KmhBrand.DrawDisc(disc, new Color(1f, 1f, 1f, 0.06f));
                    DialogLayout.LabelTrunc(name, "<color=#A9AFB9>" + (b.Rows.Count == 0 && i == 0 ? KmhText.Get("KMH_Lb_NoRankings", "No rankings yet")
                                                                                              : KmhText.Get("KMH_Lb_OpenSpot", "Open spot")) + "</color>", TextAnchor.MiddleCenter);
                    DialogLayout.LabelTrunc(value, "<color=#A9AFB9>—</color>", TextAnchor.MiddleCenter);
                    continue;
                }
                Monogram(disc, row.Name, medal, d >= 30f ? GameFont.Small : GameFont.Tiny);
                Text.Font = GameFont.Small;
                if (row.Online) KmhBrand.DrawDisc(new Rect(disc.xMax - 9f, disc.yMax - 9f, 9f, 9f), KmhBrand.Ok);
                DialogLayout.LabelTrunc(name, $"<b>{KmhDisplayText.Inert(row.Name, 40)}</b>", TextAnchor.MiddleCenter);
                if (second.Length > 0)
                {
                    Text.Font = GameFont.Tiny;
                    DialogLayout.LabelTrunc(sub, $"<color=#A9AFB9>{second}</color>", TextAnchor.MiddleCenter);
                    Text.Font = GameFont.Small;
                }
                GUI.color = i == 0 ? Gold : Color.white;
                DialogLayout.LabelTrunc(value, KmhDisplayText.Inert(row.Display, 40), TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                DialogLayout.LabelTrunc(detail, $"<color=#A9AFB9>{PodiumDetail(b, i)}</color>", TextAnchor.MiddleCenter);
                Text.Font = GameFont.Small;
                Rect card = new Rect(x, disc.y, w, r.yMax - disc.y);
                Widgets.DrawHighlightIfMouseover(card);
                if (!b.IsGuildBoard && Widgets.ButtonInvisible(card)) Open(row.Name);
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // Same wording as the website's board image.
        internal static string PodiumDetail(LeaderboardBoard b, int i)
        {
            long top = b.Rows.Count > 0 ? b.Rows[0].Value : 0;
            if (i == 0)
            {
                if (b.Rows.Count < 2 || b.Rows[1].Value <= 0) return KmhText.Get("KMH_Lb_TopOfBoard", "Top of the board");
                long second = b.Rows[1].Value;
                if (top <= second) return KmhText.Get("KMH_Lb_Tied", "Tied for first");
                double ahead = (top - second) / (double)second;
                return KmhText.Get("KMH_Lb_Leads", "Leads #2 by {0}", ahead < 1 ? $"{Math.Max(1, (int)Math.Round(ahead * 100))}%" : $"{(top / (double)second).ToString("0.#")}x");
            }
            if (i >= b.Rows.Count || top <= 0 || b.Rows[i].Value < 0) return "";
            return KmhText.Get("KMH_Lb_OfFirst", "{0}% of #1", (int)Math.Round(b.Rows[i].Value * 100.0 / top));
        }

        // Two letters on the player's own colour: the same face on every board, here, on the website and in Discord.
        internal static void Monogram(Rect disc, string name, Color ring, GameFont font)
        {
            KmhBrand.DrawDisc(disc, ring);
            KmhBrand.DrawDisc(disc.ContractedBy(Mathf.Max(1.5f, disc.width * 0.07f)), NameColour(name));
            Text.Font = font;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(disc, Initials(name));
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // LeaderboardSvg.AvatarColour's hue from the same FNV-1a hash, so a player keeps one colour everywhere.
        internal static Color NameColour(string name)
        {
            uint hash = 2166136261;
            foreach (char ch in name ?? "") { hash ^= ch; hash *= 16777619; }
            return Color.HSVToRGB((hash % 360) / 360f, 0.55f, 0.62f);
        }

        internal static string Initials(string name)
        {
            var words = new List<string>();
            foreach (string word in (name ?? "").Split(new[] { ' ', '_', '-', '.', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string letters = "";
                foreach (char ch in word) if (char.IsLetterOrDigit(ch) && letters.Length < 2) letters += ch;
                if (letters.Length > 0) words.Add(letters);
            }
            string s = words.Count == 0 ? "?" : words.Count == 1 ? CamelPair(name) ?? words[0] : words[0].Substring(0, 1) + words[1].Substring(0, 1);
            return s.ToUpperInvariant();
        }

        // LeaderboardSvg.CamelPair: "SimEzra" is S and E, as the website draws it.
        private static string CamelPair(string name)
        {
            string s = (name ?? "").Trim();
            for (int i = 1; i < s.Length; i++)
                if (char.IsUpper(s[i]) && char.IsLower(s[i - 1]) && char.IsLetter(s[0])) return s[0].ToString() + s[i];
            return null;
        }

        private static string Second(LeaderboardBoard b, LeaderboardRow row)
        {
            if (b.IsGuildBoard) return "";
            if (!string.IsNullOrEmpty(row.Guild)) return KmhDisplayText.GuildName(row.Guild);
            return string.IsNullOrEmpty(row.Colony) ? "" : KmhDisplayText.Inert(row.Colony, 40);
        }

        private void DrawRows(Rect box, LeaderboardBoard b)
        {
            string viewKey = b.Key + "\n" + _search + "\n" + LeaderboardCache.LastUpdatedUtc.Ticks;
            if (_view == null || _viewKey != viewKey)
            {
                _view = new List<LeaderboardRow>();
                string q = (_search ?? "").Trim();
                foreach (LeaderboardRow row in b.Rows)
                    if (q.Length == 0 || Has(row.Name, q) || Has(row.Guild, q) || Has(row.Colony, q)) _view.Add(row);
                _viewKey = viewKey;
            }

            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            // Measured per font, or the guild/colony line falls out of a fixed-height row.
            float nameH = DialogLayout.TextRowH;
            Text.Font = GameFont.Tiny;
            float subH = DialogLayout.TextRowH;
            Text.Font = GameFont.Small;
            float rowH = Mathf.Max(34f, nameH + subH + 4f);
            float viewH = Mathf.Max(inner.height, _view.Count * rowH + 8f);
            Rect view = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);
            long top = b.Rows.Count > 0 ? Math.Max(1L, b.Rows[0].Value) : 1L;
            string me = KmhSession.Me, myGuild = GuildCache.Guild?.Name ?? "";
            float barX = view.width * 0.58f, barW = view.width * 0.22f;

            Widgets.BeginScrollView(inner, ref _rowScroll, view);
            DialogLayout.VisibleRange(_rowScroll, inner.height, rowH, _view.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                LeaderboardRow row = _view[i];
                Rect rr = new Rect(0f, i * rowH, view.width, rowH - 2f);
                bool mine = b.IsGuildBoard ? !string.IsNullOrEmpty(myGuild) && string.Equals(row.Name, myGuild, StringComparison.OrdinalIgnoreCase)
                                           : KmhSession.Same(row.Name, me);
                if (mine) Widgets.DrawBoxSolid(rr, new Color(0.30f, 0.65f, 0.40f, 0.20f));
                else if (i % 2 == 0) Widgets.DrawAltRect(rr);
                if (!b.IsGuildBoard && Selected != null && KmhSession.Same(row.Name, Selected))
                {
                    Widgets.DrawBoxSolid(rr, KmhBrand.WithAlpha(KmhBrand.Accent, 0.12f));
                    Widgets.DrawBoxSolid(new Rect(rr.x, rr.y, 3f, rr.height), KmhBrand.Accent);
                }
                Widgets.DrawHighlightIfMouseover(rr);

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = row.Rank <= 3 ? MedalColor(row.Rank) : Muted;
                Widgets.Label(new Rect(rr.x, rr.y, 44f, rr.height), "#" + row.Rank);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                float disc = Mathf.Min(24f, rr.height - 6f);
                Monogram(new Rect(rr.x + 48f, rr.y + (rr.height - disc) / 2f, disc, disc), row.Name,
                         row.Rank <= 3 ? MedalColor(row.Rank) : new Color(1f, 1f, 1f, 0.18f), GameFont.Tiny);
                Text.Font = GameFont.Small;
                float nx = rr.x + 56f + disc;
                string name = KmhDisplayText.Inert(row.Name, 48) + (row.Online ? "  <color=#4cd38a>●</color>" : "") + (mine ? "  <color=#4cc38a>★</color>" : "");
                string second = Second(b, row);
                if (second.Length > 0)
                {
                    DialogLayout.LabelTrunc(new Rect(nx, rr.y + 1f, barX - nx - 10f, nameH), $"<b>{name}</b>");
                    Text.Font = GameFont.Tiny;
                    DialogLayout.LabelTrunc(new Rect(nx, rr.y + 1f + nameH, barX - nx - 10f, subH), $"<color=#A9AFB9>{second}</color>");
                    Text.Font = GameFont.Small;
                }
                else DialogLayout.LabelTrunc(new Rect(nx, rr.y + (rr.height - nameH) / 2f, barX - nx - 10f, nameH), $"<b>{name}</b>");

                float frac = Mathf.Clamp(row.Value / (float)top, 0.02f, 1f);
                Rect bar = new Rect(barX, rr.y + rr.height / 2f - 3f, barW, 6f);
                Widgets.DrawBoxSolid(bar, new Color(1f, 1f, 1f, 0.08f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * frac, bar.height), b.Reported ? Reported : Gold);

                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(barX + barW, rr.y, view.width - barX - barW - 8f, rr.height), KmhDisplayText.Inert(row.Display, 40));
                Text.Anchor = TextAnchor.UpperLeft;
                if (!b.IsGuildBoard && Widgets.ButtonInvisible(rr)) Open(row.Name);
            }
            if (_view.Count == 0)
                DialogLayout.LabelTrunc(new Rect(6f, 6f, view.width, 20f), string.IsNullOrEmpty(_search)
                    ? "<color=#A9AFB9>" + KmhText.Get("KMH_Lb_NobodyRanked", "Nobody is ranked on this board yet.") + "</color>"
                    : "<color=#A9AFB9>" + KmhText.Get("KMH_Lb_NobodyMatches", "Nobody on this board matches.") + "</color>");
            Widgets.EndScrollView();
        }

        private static bool Has(string s, string q) => !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
