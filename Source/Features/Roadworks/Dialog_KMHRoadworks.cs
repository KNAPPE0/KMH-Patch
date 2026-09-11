using System.Collections.Generic;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Roadworks.Dto;
using KMHPatch.Features.Sites;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Roadworks
{
    // Plan and track road projects; the route is quoted here before anything is sent, so the price is seen rather than discovered after paying.
    public class Dialog_KMHRoadworks : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(760f, 560f);

        private Vector2 _scroll;
        private string  _tier = KmhRoadDefs.TierTrail;

        public Dialog_KMHRoadworks()
        {
            doCloseX = true; absorbInputAroundWindow = true; draggable = true; resizeable = true;
            EnableAutoRefresh(() => RoadworksHandler.RequestSnapshot());
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Roadworks");

            if (Widgets.ButtonText(new Rect(rect.width - 150f, y, 146f, 28f), "Refresh"))
                RoadworksHandler.RequestSnapshot();
            y += 34f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (!RoadworksHandler.Available)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f),
                    "<color=#ffce4d>This server does not support Roadworks.</color>");
                return;
            }

            RoadworksSnapshot snap = RoadworksCache.Snapshot;
            if (snap == null) { DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f), "Loading road network…"); return; }

            if (!snap.AllowRoadworks)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                    "<color=#ffce4d>Roadworks is turned off on this server.</color>");
                y += 24f;
            }
            else
            {
                Help(rect, ref y, $"<color=grey>{Plural(snap.Segments.Count, "road segment")} built across the world.</color>");
                // Nothing else on screen says roads are laid by a site's workers rather than by clicking.
                Help(rect, ref y,
                    "<color=grey>Roads are built by a Roadworks site's colonists: pick a route below, then assign workers "
                    + "to that site. Each production cycle they lay road.</color>");
                Help(rect, ref y,
                    "<color=grey>Finished roads speed up caravans for everyone. Silver is reserved when you start and "
                    + "spent per segment; cancelling refunds the rest.</color>");
                y += 6f;
            }

            y = DrawPlanner(rect, y, snap);
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (snap.Projects.Count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 8f, rect.width, 24f), "You have no road projects.");
                return;
            }

            // The total belongs to the list, not to the header above the planner, where it read as a second per-project figure.
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 22f),
                $"<b>Your projects</b> <color=grey>· {snap.EscrowSilver} silver reserved in total</color>");
            y += 26f;

            // Measured, not guessed: at a fixed 62 the last line ran past the bottom of its own row.
            float rowH = DialogLayout.TextRowH * 3f + BarH + 24f;
            // Floored - this window is resizeable and BeginScrollView throws on a negative rect.
            Rect view = new Rect(0f, y, rect.width, Mathf.Max(DialogLayout.MinBodyHeight, rect.height - y - 6f));
            Rect content = new Rect(0f, 0f, Mathf.Max(1f, view.width - DialogLayout.ScrollbarReserveWidth),
                                    snap.Projects.Count * rowH);
            Widgets.BeginScrollView(view, ref _scroll, content);
            DialogLayout.VisibleRange(_scroll, view.height, rowH, snap.Projects.Count, out int first, out int last);
            for (int i = first; i < last; i++)
                DrawProject(new Rect(0f, i * rowH, content.width, rowH - 4f), snap.Projects[i]);
            Widgets.EndScrollView();
        }

        private float DrawPlanner(Rect rect, float y, RoadworksSnapshot snap)
        {
            List<SiteEntry> manageable = ManageableRoadworksSites();
            SiteEntry site = SelectedRoadworksSite(manageable);
            if (site == null)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                    "<color=grey>Build a Roadworks site to start a road project.</color>");
                return y + 26f;
            }

            // Only worth a chooser when there is a choice; one site selects itself.
            if (manageable.Count > 1)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, 220f, DialogLayout.TextRowH), "<b>Roadworks site</b>");
                if (Widgets.ButtonText(new Rect(226f, y, Mathf.Min(360f, Mathf.Max(160f, rect.width - 232f)), 24f),
                                       SiteChoiceLabel(site)))
                {
                    var opts = new List<FloatMenuOption>();
                    foreach (SiteEntry s in manageable)
                    {
                        SiteEntry pick = s;
                        opts.Add(new FloatMenuOption(SiteChoiceLabel(pick), () => _fromTile = pick.Tile));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
                y += 28f;
            }

            string[] tiers = { KmhRoadDefs.TierTrail, KmhRoadDefs.TierRoad, KmhRoadDefs.TierHighway };
            float tierW = MinTierW;
            foreach (string t in tiers)
            {
                snap.SilverPerSegment.TryGetValue(t, out int seg);
                float need = 0f;
                try { need = Text.CalcSize(TierChoice(t, seg)).x + 34f; } catch { }
                tierW = Mathf.Max(tierW, need);
            }
            tierW = Mathf.Min(tierW, MaxTierW);
            bool sameRow = TiersFitBeside(rect.width, tierW);

            DialogLayout.LabelTrunc(new Rect(0f, y, sameRow ? 220f : rect.width, DialogLayout.TextRowH),
                $"<b>From</b> {SiteWithTile(site)}");

            float tierY = sameRow ? y : y + 26f;
            float cellW = TierCellW(rect.width, tierW);

            for (int i = 0; i < tiers.Length; i++)
            {
                string tier = tiers[i];
                snap.SilverPerSegment.TryGetValue(tier, out int per);
                snap.WorkPerSegment.TryGetValue(tier, out double workSeg);
                bool allowed = KmhRoadDefs.AllowedAtSiteTier(tier, site.OutputTier);
                string label = TierChoice(tier, per);
                if (!allowed) label += " 🔒";

                Rect tr = new Rect(TierCellX(rect.width, tierW, i), tierY, cellW, TierCellH);
                if (DialogLayout.DrawSegment(tr, label, _tier == tier, allowed)) _tier = tier;
                TooltipHandler.TipRegion(tr, allowed
                    ? $"{per} silver per segment. Takes {(workSeg <= 0 ? 1 : workSeg):0.##} work per segment, "
                    + "so a higher tier is slower to lay as well as dearer."
                    : $"Locked: this needs a higher site tier than T{site.OutputTier}.");
            }
            y = tierY + 28f;

            if (!KmhRoadDefs.AllowedAtSiteTier(_tier, site.OutputTier)) _tier = KmhRoadDefs.TierTrail;
            bool routePlannerOpen = Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active;
            bool canPlan = snap.AllowRoadworks && !routePlannerOpen;

            Rect pickRect = new Rect(0f, y, 200f, 28f);
            if (Widgets.ButtonText(pickRect, "Pick destination on map", active: canPlan))
                BeginPick(site, snap, _tier);

            // A greyed button with no reason reads as broken, and the caravan route planner is the usual cause.
            if (!canPlan)
                TooltipHandler.TipRegion(pickRect, routePlannerOpen
                    ? "Close the caravan route planner first - it owns the world map while it is open."
                    : "Roadworks is turned off on this server.");

            DialogLayout.LabelTrunc(new Rect(206f, y + 3f, rect.width - 206f, 20f),
                "<color=grey>Already-built stretches are not charged again.</color>");
            return y + 32f;
        }

        // World targeting, then plan + quote + confirm. Nothing is sent until the player accepts the price.
        private void BeginPick(SiteEntry site, RoadworksSnapshot snap, string tier)
        {
            Close();
            // Or the targeter starts behind a colony map: live, invisible, and unclickable.
            CameraJumper.TryShowWorld();
            Messages.Message("Click a tile to route the road to. Right-click or Escape to cancel.",
                             MessageTypeDefOf.NeutralEvent, false);
            Find.WorldTargeter.BeginTargeting(target =>
            {
                if (!target.IsValid) return false;
                PlanAndConfirm(site, snap, target.Tile, tier);
                return true;
            }, canTargetTiles: true);
        }

        private static void PlanAndConfirm(SiteEntry site, RoadworksSnapshot snap, PlanetTile dest, string tier)
        {
            var from = new PlanetTile(site.Tile, dest.LayerId());
            if (!KmhRoadRoutePlanner.TryPlan(from, dest, snap.MaxRouteSegments, out List<PlanetTile> route, out string why))
            { Messages.Message(why, MessageTypeDefOf.RejectInput, false); return; }

            List<(int layer, int tile)> flat = KmhRoadRoutePlanner.Flatten(route);
            List<string> keys = KmhRoadQuote.RouteKeys(flat);

            string refusal = KmhRoadQuote.RefusalFor(snap, tier, keys);
            if (refusal != null) { Messages.Message(refusal, MessageTypeDefOf.RejectInput, false); return; }

            int cost = KmhRoadQuote.SilverFor(snap, tier, keys);
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                $"Build {Plural(keys.Count, "segment")} of {TierLabel(tier)} from {SiteWithTile(site)} to tile {dest.tileId}?\n\n" +
                $"Cost: {cost} silver from your treasury. Cancelling later refunds whatever is still unbuilt.",
                () =>
                {
                    var tiles  = new List<int>();
                    var layers = new List<int>();
                    foreach ((int layer, int tile) in flat) { tiles.Add(tile); layers.Add(layer); }
                    RoadworksHandler.StartProject(site.Tile, tier, tiles, layers);
                }));
        }

        // Progress-bar height, shared by the row measurement and the row draw so the two cannot drift.
        private const float BarH = 14f;

        private void DrawProject(Rect r, RoadProjectDto p)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(8f);

            float textW = Mathf.Max(0f, inner.width - 120f);

            // Identity first, then progress, then why it is moving at the speed it is - one thing per line.
            SiteEntry from = SiteAt(p.SiteTile);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, textW, DialogLayout.TextRowH),
                $"<b>{TierLabel(p.Tier)}</b> from {(from != null ? SiteWithTile(from) : $"tile {p.SiteTile}")} "
                + $"<color=grey>· #{p.Id}</color> · {StateLabel(p.State)}");

            float pct = p.SegmentsTotal <= 0 ? 0f : Mathf.Clamp01((p.SegmentsDone + (float)p.CurrentProgress) / p.SegmentsTotal);
            Widgets.FillableBar(new Rect(inner.x, inner.y + DialogLayout.TextRowH + 2f, textW, BarH), pct);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH + BarH + 6f, textW, DialogLayout.TextRowH),
                $"<color=#cccccc>{p.SegmentsDone} of {Plural(p.SegmentsTotal, "segment")} laid</color>"
                + $" <color=grey>· {p.EscrowUnspent} silver still reserved</color>");
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 2f + BarH + 6f, textW, DialogLayout.TextRowH),
                $"<color=grey>{RateLine(p)}</color>");

            if (p.State != RoadProjectDto.StateBuilding) return;
            if (Widgets.ButtonText(new Rect(inner.xMax - 110f, inner.y + 8f, 110f, 24f), "Cancel"))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Cancel project #{p.Id}? {p.EscrowUnspent} silver returns to your treasury. Finished road stays.",
                    () => RoadworksHandler.CancelProject(p.Id)));
        }


        // Answers "why is this bar not moving" on the row itself: rate from the crew, ETA from the site's cycle.
        private static string RateLine(RoadProjectDto p)
        {
            RoadworksSnapshot snap = RoadworksCache.Snapshot;
            SiteEntry site = SiteAt(p.SiteTile);
            if (snap == null || site == null) return "no site";

            double perSeg = 1.0;
            if (snap.WorkPerSegment != null && snap.WorkPerSegment.TryGetValue(KmhRoadDefs.Normalize(p.Tier), out double w) && w > 0)
                perSeg = w;

            double crew = CrewWork(site, snap);
            if (crew <= 0) return "<color=#ffcf59>no workers - assign a colonist to the site</color>";

            double segsPerCycle = crew / perSeg;
            int remaining = Mathf.Max(0, p.SegmentsTotal - p.SegmentsDone);
            double cycleMin = site.EffectiveCycleMinutes > 0 ? site.EffectiveCycleMinutes : 0;
            string eta = cycleMin > 0 && segsPerCycle > 0
                ? $" · ~{FormatMinutes(remaining / segsPerCycle * cycleMin)} remaining"
                : "";
            string rate = segsPerCycle.ToString("0.##");
            return $"{rate} {(rate == "1" ? "segment" : "segments")} per site cycle{eta}";
        }

        private static double CrewWork(SiteEntry s, RoadworksSnapshot snap)
        {
            if (s?.Workers == null || s.WorkerProgress == null) return 0;
            double crew = 0;
            foreach (string w in s.Workers)
            {
                if (!s.WorkerProgress.TryGetValue(w, out WorkerProgressDto wp) || wp == null) continue;
                if (wp.Legacy || wp.PawnLoadId <= 0 || !string.IsNullOrEmpty(wp.BlockedReason)) continue;
                crew += snap.WorkPerWorker + wp.EarnedLevel * snap.WorkPerSkillLevel;
            }
            return crew;
        }

        private static string FormatMinutes(double minutes)
        {
            if (minutes < 90) return $"{Mathf.Max(1, Mathf.RoundToInt((float)minutes))}min";
            double hours = minutes / 60.0;
            return hours < 48 ? $"{hours:0.#}h" : $"{hours / 24.0:0.#}d";
        }

        private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

        // Wrapped, not truncated: these lines are the only explanation of how roads actually get built.
        private static void Help(Rect rect, ref float y, string text)
        {
            float h = Text.CalcHeight(text, rect.width);
            Widgets.Label(new Rect(0f, y, rect.width, h), text);
            y += h + 2f;
        }

        // SiteLabels.Name already falls back to "Tile 72540", so an unconditional suffix repeats the tile twice.
        private static string SiteWithTile(SiteEntry s)
        {
            if (s == null) return "";
            string name = SiteLabels.Name(s);
            return string.IsNullOrEmpty(s.SiteName) ? name : $"{name} (tile {s.Tile})";
        }

        private static string TierChoice(string tier, int silverPerSegment)
            => $"{TierLabel(tier)} · {silverPerSegment} silver/seg";

        internal const float TierGap    = 4f;
        internal const float TierCellH  = 24f;
        internal const float TierRowX   = 226f;
        internal const int   TierCount  = 3;
        private  const float MinTierW   = 130f;
        private  const float MaxTierW   = 250f;

        // Pure so the three cells can be proven not to overlap or leave the dialog without a running game.
        internal static bool TiersFitBeside(float rectWidth, float tierW)
            => rectWidth >= TierRowX + TierCount * (tierW + TierGap);

        internal static float TierCellW(float rectWidth, float tierW)
            => TiersFitBeside(rectWidth, tierW) ? tierW : Mathf.Max(60f, rectWidth / TierCount - TierGap);

        internal static float TierCellX(float rectWidth, float tierW, int index)
            => (TiersFitBeside(rectWidth, tierW) ? TierRowX : 0f) + index * (TierCellW(rectWidth, tierW) + TierGap);

        private static SiteEntry SiteAt(int tile)
        {
            List<SiteEntry> sites = SiteCache.Snapshot?.Sites;
            if (sites == null) return null;
            foreach (SiteEntry s in sites) if (s.Tile == tile) return s;
            return null;
        }

        private List<SiteEntry> _manageable = new List<SiteEntry>();
        private string _manageableKey = "";

        // Uses the site's own ownership rule (so guild sites count), cached because the planner draws every frame and this scans every visible site.
        private List<SiteEntry> ManageableRoadworksSites()
        {
            string me = KmhSession.Me;
            string key = $"{SiteCache.LastUpdatedUtc.Ticks}:{GuildCache.Guild?.Name ?? ""}:{me}";
            if (_manageableKey == key) return _manageable;

            var outp = new List<SiteEntry>();
            List<SiteEntry> sites = SiteCache.Snapshot?.Sites;
            if (sites != null && !string.IsNullOrEmpty(me))
                foreach (SiteEntry s in sites)
                    if (s != null && s.Archetype == SiteEntry.ArchetypeRoadworks
                        && SiteOwnershipClient.CanManage(s, me)) outp.Add(s);
            outp.Sort((a, b) => a.Tile.CompareTo(b.Tile));
            _manageable = outp;
            _manageableKey = key;
            return outp;
        }

        // Remembered by tile, not index: a site leaving the snapshot would otherwise move the selection silently.
        private int _fromTile = -1;

        private SiteEntry SelectedRoadworksSite(List<SiteEntry> manageable)
        {
            if (manageable.Count == 0) return null;
            foreach (SiteEntry s in manageable) if (s.Tile == _fromTile) return s;
            _fromTile = manageable[0].Tile;
            return manageable[0];
        }

        private static string SiteChoiceLabel(SiteEntry s)
            => $"{SiteWithTile(s)} · "
               + (SiteOwnershipClient.KindOf(s) == SiteEntry.OwnerGuildKind
                    ? (string.IsNullOrEmpty(s.ControllingGuild) ? "guild" : s.ControllingGuild)
                    : "personal");

        private static string TierLabel(string tier)
        {
            switch (KmhRoadDefs.Normalize(tier))
            {
                case KmhRoadDefs.TierRoad:    return "Road";
                case KmhRoadDefs.TierHighway: return "Highway";
                default:                      return "Trail";
            }
        }

        private static string StateLabel(string state)
        {
            switch (state)
            {
                case RoadProjectDto.StateComplete:  return "<color=#80ff80>complete</color>";
                case RoadProjectDto.StateCancelled: return "<color=grey>cancelled</color>";
                default:                            return "<color=#79b8ff>building</color>";
            }
        }

    }
}
