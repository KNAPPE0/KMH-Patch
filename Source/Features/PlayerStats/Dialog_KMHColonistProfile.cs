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

        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(620f, 640f);

        protected override void DrawContents(Rect rect)
        {
            List<PlayerLeaderboardEntry> all = PlayerStatsCache.Entries;
            PlayerLeaderboardEntry e = all?.Find(x => string.Equals(x.Username, _username, StringComparison.OrdinalIgnoreCase));

            float y = DialogLayout.DrawTitle(rect, "Colonist Profile");
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (e == null)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f), "<color=#A9AFB9>No stats for this player yet.</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            string guild  = string.IsNullOrEmpty(e.GuildName) ? "<color=#A9AFB9>no guild</color>" : KmhDisplayText.GuildName(e.GuildName);
            string colony = string.IsNullOrEmpty(e.ColonyName) ? "<color=#A9AFB9>no colony reported yet</color>" : $"<b>{I(e.ColonyName)}</b>";
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                $"{LinkedAccountsCache.Format(_username)}  <color=#A9AFB9>·</color>  {colony}  <color=#A9AFB9>·</color>  {guild}");
            y += 24f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            ColonistProfile d = ColonistProfileCache.Get(_username);

            string topName = !string.IsNullOrEmpty(e.TopColonistName) ? e.TopColonistName : (d?.Name ?? "");
            if (string.IsNullOrEmpty(topName))
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), "<color=#A9AFB9>No colonist reported for this colony yet.</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }
            Color old = GUI.color;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                $"<b>{I(topName)}</b>  <color=#A9AFB9>·</color>  {I(string.IsNullOrEmpty(e.TopColonistTitle) ? (d?.Title ?? "") : e.TopColonistTitle)}  <color=#A9AFB9>·</color>  {e.TopColonistKills} kills");
            y += 22f;
            if (d != null && !string.IsNullOrEmpty(d.GenderAge))
            {
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f), $"{I(d.GenderAge)} · {I(d.Descriptor)}");
                GUI.color = old;
                y += 20f;
            }

            int cur = 0;
            for (int i = 0; i < TabOrder.Length; i++) if (TabOrder[i] == _tab) { cur = i; break; }
            y = DialogLayout.TabRow(y, rect.width, TabLabels, cur, i => _tab = TabOrder[i]);

            Rect body = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(body);

            if (!ColonistProfileCache.Has(_username))
                DialogLayout.LabelTrunc(new Rect(body.x + 8f, body.y + 8f, body.width - 16f, 22f), "<color=#A9AFB9>Loading colonist…</color>");
            else if (d == null)
                DialogLayout.LabelTrunc(new Rect(body.x + 8f, body.y + 8f, body.width - 16f, 22f), "<color=#A9AFB9>No colonist detail available.</color>");
            else
            {
                // Rebuilt only for a new profile or tab: each arriving profile is a fresh object.
                if (!ReferenceEquals(_linesFor, d) || _linesTab != _tab) { _lines = BuildLines(d); _linesFor = d; _linesTab = _tab; }
                DrawLines(body, _lines);
            }

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private List<(string text, bool header)> _lines;
        private ColonistProfile _linesFor;
        private Tab _linesTab;

        private static readonly Tab[]    TabOrder  = { Tab.Bio, Tab.Health, Tab.Combat, Tab.Skills, Tab.History };
        private static readonly string[] TabLabels = { "Bio", "Health", "Combat", "Skills", "History" };

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
                    foreach (ColonistCapacity c in d.Capacities) R($"{I(c.Name)}: {c.Pct}%");
                    L.Add(("", false));
                    H("Conditions");
                    if (d.Conditions.Count == 0) R("<color=#A9AFB9>No notable long-term conditions.</color>");
                    else foreach (string c in d.Conditions) R($"• {I(c)}");
                    break;

                case Tab.Combat:
                    H("Combat Summary");
                    R($"Total kills: <b>{d.TotalKills}</b>");
                    R($"Humanlike: {d.HumanlikeKills}   Mechanoid: {d.MechanoidKills}   Animal: {d.AnimalKills}");
                    R($"Damage taken: {d.DamageTaken}");
                    R($"Weapon: {I(d.Weapon)}{(string.IsNullOrEmpty(d.WeaponQuality) || d.WeaponQuality == "None" ? "" : $" ({I(d.WeaponQuality)})")}");
                    if (d.RecentCombat.Count > 0)
                    {
                        L.Add(("", false));
                        H("Recent combat");
                        foreach (string c in d.RecentCombat) R($"• {I(c)}");
                    }
                    break;

                case Tab.Skills:
                    H("Skills");
                    if (d.Skills.Count == 0) R("<color=#A9AFB9>No skills reported.</color>");
                    else foreach (ColonistSkill sk in d.Skills) R($"{I(sk.Name)}   <b>{sk.Level}</b> {Passion(sk.Passion)}");
                    break;

                case Tab.History:
                {
                    H("Service");
                    R($"Days in colony: <b>{d.DaysInColony}</b>"
                      + (d.DaysInColony > 0 ? $"  <color=#A9AFB9>· about {Mathf.Max(1, d.DaysInColony / 60)} year(s) of colony time</color>" : ""));
                    R($"Origin: <color=#c4cad6>{(string.IsNullOrEmpty(d.Childhood) ? "-" : I(d.Childhood))}</color>");
                    if (!string.IsNullOrEmpty(d.Adulthood))
                        R($"Became: <color=#c4cad6>{I(d.Adulthood)}</color>");

                    L.Add(("", false));
                    H("What they have been through");
                    R($"Kills: <b>{d.TotalKills}</b>  <color=#A9AFB9>· {d.HumanlikeKills} humanlike, {d.MechanoidKills} mechanoid, {d.AnimalKills} animal</color>");
                    R($"Damage taken: {d.DamageTaken}");
                    if (d.RecentCombat != null && d.RecentCombat.Count > 0)
                        foreach (string c in d.RecentCombat) R($"• {I(c)}");
                    else R("<color=#A9AFB9>No recent combat recorded.</color>");

                    L.Add(("", false));
                    H("Lasting marks");
                    if (d.Conditions != null && d.Conditions.Count > 0)
                        foreach (string c in d.Conditions) R($"• {I(c)}");
                    else R("<color=#A9AFB9>None - unscarred so far.</color>");
                    break;
                }

                default:
                    H("Backstory");
                    R($"Childhood: <color=#c4cad6>{(string.IsNullOrEmpty(d.Childhood) ? "-" : I(d.Childhood))}</color>");
                    R($"Adulthood: <color=#c4cad6>{(string.IsNullOrEmpty(d.Adulthood) ? "-" : I(d.Adulthood))}</color>");
                    R($"Days in colony: {d.DaysInColony}");
                    L.Add(("", false));
                    H("Traits");
                    if (d.Traits.Count == 0) R("<color=#A9AFB9>None.</color>");
                    else foreach (string t in d.Traits) R($"• {I(t)}");
                    L.Add(("", false));
                    H("Incapable Of");
                    if (d.Incapable.Count == 0) R("<color=#A9AFB9>No incapabilities recorded.</color>");
                    else R(I(string.Join(", ", d.Incapable)));
                    break;
            }
            return L;
        }

        private void DrawLines(Rect body, List<(string text, bool header)> lines)
        {
            Rect inner = body.ContractedBy(8f);
            float lineH = DialogLayout.TextRowH;
            float viewH = Mathf.Max(inner.height, lines.Count * lineH + 6f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float ly = 0f;
            foreach ((string text, bool header) in lines)
            {
                if (header) DialogLayout.LabelTrunc(new Rect(0f, ly, viewRect.width, lineH), $"<color=#f0c04f><b>{text}</b></color>");
                else if (!string.IsNullOrEmpty(text)) DialogLayout.LabelTrunc(new Rect(8f, ly, viewRect.width - 8f, lineH), text);
                ly += lineH;
            }
            Widgets.EndScrollView();
        }

        // Every string here was typed or generated in another player's game.
        private static string I(string s) => KmhDisplayText.Inert(s ?? "");

        private static string Passion(int passion) => passion >= 2 ? "<color=#ff9d4d>++</color>" : passion == 1 ? "<color=#f0c04f>+</color>" : "";
    }
}
