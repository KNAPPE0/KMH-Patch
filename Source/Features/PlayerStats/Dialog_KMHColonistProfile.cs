using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.Features.Reputation;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.PlayerStats
{
    // Colonist Profile shows a player's colony header and strongest pawn, fetching full detail on open.
    public class Dialog_KMHColonistProfile : Window_KMHBase
    {
        private readonly string _username;
        private Tab     _tab = Tab.Bio;
        private Vector2 _scroll;

        private enum Tab { Bio, Health, Combat, Skills, History }

        public Dialog_KMHColonistProfile(string username)
        {
            _username = username ?? "";
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
            PlayerStatsHandler.RequestColonist(_username);
        }

        public override Vector2 InitialSize => new Vector2(620f, 640f);

        protected override void DrawContents(Rect rect)
        {
            List<PlayerLeaderboardEntry> all = PlayerStatsCache.Entries;
            PlayerLeaderboardEntry e = all?.Find(x => string.Equals(x.Username, _username, StringComparison.OrdinalIgnoreCase));

            float y = DialogLayout.DrawTitle(rect, "Colonist Profile");
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (e == null)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f), "<color=grey>No stats for this player yet.</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            string guild  = string.IsNullOrEmpty(e.GuildName) ? "<color=grey>no guild</color>" : e.GuildName;
            string colony = string.IsNullOrEmpty(e.ColonyName) ? "<color=grey>unknown colony</color>" : $"<b>{e.ColonyName}</b>";
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                $"{LinkedAccountsCache.Format(_username)}  <color=grey>·</color>  {colony}  <color=grey>·</color>  {guild}");
            y += 24f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            ColonistProfile d = ColonistProfileCache.Get(_username);

            string topName = !string.IsNullOrEmpty(e.TopColonistName) ? e.TopColonistName : (d?.Name ?? "");
            if (string.IsNullOrEmpty(topName))
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), "<color=grey>No colonist reported for this colony yet.</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }
            Color old = GUI.color;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                $"<b>{topName}</b>  <color=grey>·</color>  {(string.IsNullOrEmpty(e.TopColonistTitle) ? (d?.Title ?? "") : e.TopColonistTitle)}  <color=grey>·</color>  {e.TopColonistKills} kills");
            y += 22f;
            if (d != null && !string.IsNullOrEmpty(d.GenderAge))
            {
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f), $"{d.GenderAge} · {d.Descriptor}");
                GUI.color = old;
                y += 20f;
            }

            float tx = 0f;
            tx = Tabber(tx, y, "Bio",     Tab.Bio);
            tx = Tabber(tx, y, "Health",  Tab.Health);
            tx = Tabber(tx, y, "Combat",  Tab.Combat);
            tx = Tabber(tx, y, "Skills",  Tab.Skills);
            _  = Tabber(tx, y, "History", Tab.History);
            y += 32f;

            Rect body = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(body);

            if (_tab == Tab.History)
                DialogLayout.LabelTrunc(new Rect(body.x + 8f, body.y + 8f, body.width - 16f, 22f),
                    "<color=grey>Per-colonist history (notable events, milestones) arrives in a later update.</color>");
            else if (!ColonistProfileCache.Has(_username))
                DialogLayout.LabelTrunc(new Rect(body.x + 8f, body.y + 8f, body.width - 16f, 22f), "<color=grey>Loading colonist…</color>");
            else if (d == null)
                DialogLayout.LabelTrunc(new Rect(body.x + 8f, body.y + 8f, body.width - 16f, 22f), "<color=grey>No colonist detail available.</color>");
            else
                DrawLines(body, BuildLines(d));

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private float Tabber(float x, float y, string label, Tab tab)
        {
            float w = Mathf.Max(66f, Text.CalcSize(label).x + 20f);
            Color old = GUI.color;
            if (_tab == tab) GUI.color = new Color(0.45f, 0.75f, 1f);
            if (Widgets.ButtonText(new Rect(x, y, w, 28f), label)) _tab = tab;
            GUI.color = old;
            return x + w + 4f;
        }

        private List<(string text, bool header)> BuildLines(ColonistProfile d)
        {
            List<(string, bool)> L = new List<(string, bool)>();
            void H(string s) => L.Add((s, true));
            void R(string s) => L.Add((s, false));

            switch (_tab)
            {
                case Tab.Health:
                    H("Overall");
                    R($"Health: <b>{d.HealthPct}%</b>");
                    R($"Pain: {d.PainPct}%");
                    foreach (ColonistCapacity c in d.Capacities) R($"{c.Name}: {c.Pct}%");
                    L.Add(("", false));
                    H("Conditions");
                    if (d.Conditions.Count == 0) R("<color=grey>No notable long-term conditions.</color>");
                    else foreach (string c in d.Conditions) R($"• {c}");
                    break;

                case Tab.Combat:
                    H("Combat Summary");
                    R($"Total kills: <b>{d.TotalKills}</b>");
                    R($"Humanlike: {d.HumanlikeKills}   Mechanoid: {d.MechanoidKills}   Animal: {d.AnimalKills}");
                    R($"Damage taken: {d.DamageTaken}");
                    R($"Weapon: {d.Weapon}{(string.IsNullOrEmpty(d.WeaponQuality) || d.WeaponQuality == "None" ? "" : $" ({d.WeaponQuality})")}");
                    if (d.RecentCombat.Count > 0)
                    {
                        L.Add(("", false));
                        H("Recent combat");
                        foreach (string c in d.RecentCombat) R($"• {c}");
                    }
                    break;

                case Tab.Skills:
                    H("Skills");
                    if (d.Skills.Count == 0) R("<color=grey>No skills reported.</color>");
                    else foreach (ColonistSkill sk in d.Skills) R($"{sk.Name}   <b>{sk.Level}</b> {Passion(sk.Passion)}");
                    break;

                default: // Bio
                    H("Backstory");
                    R($"Childhood: <color=#cccccc>{(string.IsNullOrEmpty(d.Childhood) ? "-" : d.Childhood)}</color>");
                    R($"Adulthood: <color=#cccccc>{(string.IsNullOrEmpty(d.Adulthood) ? "-" : d.Adulthood)}</color>");
                    R($"Days in colony: {d.DaysInColony}");
                    L.Add(("", false));
                    H("Traits");
                    if (d.Traits.Count == 0) R("<color=grey>None.</color>");
                    else foreach (string t in d.Traits) R($"• {t}");
                    L.Add(("", false));
                    H("Incapable Of");
                    if (d.Incapable.Count == 0) R("<color=grey>No incapabilities recorded.</color>");
                    else R(string.Join(", ", d.Incapable));
                    break;
            }
            return L;
        }

        private void DrawLines(Rect body, List<(string text, bool header)> lines)
        {
            Rect inner = body.ContractedBy(8f);
            const float lineH = 22f;
            float viewH = Mathf.Max(inner.height, lines.Count * lineH + 6f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float ly = 0f;
            foreach ((string text, bool header) in lines)
            {
                if (header) DialogLayout.LabelTrunc(new Rect(0f, ly, viewRect.width, lineH), $"<color=#e0b94a><b>{text}</b></color>");
                else if (!string.IsNullOrEmpty(text)) DialogLayout.LabelTrunc(new Rect(8f, ly, viewRect.width - 8f, lineH), text);
                ly += lineH;
            }
            Widgets.EndScrollView();
        }

        private static string Passion(int passion) => passion >= 2 ? "<color=#ff9d4d>++</color>" : passion == 1 ? "<color=#ffce4d>+</color>" : "";
    }
}
