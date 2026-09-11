using System;
using System.Collections.Generic;
using System.Linq;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Browse and manage custom sites; every production stat comes straight from the server snapshot.
    public class Dialog_KMHSites : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(820f, 600f);

        private Vector2 _scroll;

        public Dialog_KMHSites()
        {
            doCloseX = true; absorbInputAroundWindow = true; draggable = true; resizeable = true;
            // The base ticks this once per frame; a timer counted down from the draw ran twice as fast and polled the server every 6s.
            EnableAutoRefresh(() => SiteHandler.RequestSnapshot());
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Custom sites");

            float tbW = Mathf.Max(IconButton.WidthFor("Refresh", false), IconButton.WidthFor("Build site here", true));
            if (IconButton.Draw(new Rect(rect.width - tbW, y, tbW - 4f, 28f), KMHTextures.Post, "Build site here"))
                Find.WindowStack.Add(new Dialog_KMHBuildSite());
            if (Widgets.ButtonText(new Rect(rect.width - tbW * 2f, y, tbW - 4f, 28f), "Refresh"))
                SiteHandler.RequestSnapshot();
            y += 34f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            SiteSnapshot snap = SiteCache.Snapshot;
            if (snap == null) { DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f), "Loading sites…"); return; }
            if (!snap.AllowCustomSites)
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), "<color=#ffce4d>Custom sites are disabled on this server.</color>");
            else
            {
                // Two lines: one sentence is wider than the window and loses its ending.
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                    "<color=grey>Send a caravan to the site tile, then Assign a colonist: they work inside the site "
                    + "(away from home, not usable) and earn real skill XP.</color>");
                y += 20f;
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                    "<color=grey>Recall brings them back to a caravan at the tile. Buildings adds production, housing "
                    + "or storage to a site you own.</color>");
            }
            y += 22f;

            if (snap.Sites.Count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 24f, rect.width, 24f), "No sites visible to you yet. Build one with the button above.");
                return;
            }

            string mine = KmhSession.Me;
            // Five text lines, floored at the button stack so a small font cannot shrink the row under its buttons.
            float rowH = Mathf.Max(DialogLayout.TextRowsH(5, 10f), ButtonStackH);
            Rect view = new Rect(0f, y, rect.width, Mathf.Max(DialogLayout.MinBodyHeight, rect.height - y - 6f));
            Rect content = new Rect(0f, 0f, Mathf.Max(1f, view.width - DialogLayout.ScrollbarReserveWidth), snap.Sites.Count * rowH);
            Widgets.BeginScrollView(view, ref _scroll, content);
            DialogLayout.VisibleRange(_scroll, view.height, rowH, snap.Sites.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                DrawRow(new Rect(0f, i * rowH, content.width, rowH - 4f), snap.Sites[i], mine);
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect r, SiteEntry s, string mine)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(8f);

            bool isOwner  = SiteOwnershipClient.CanManage(s, mine);
            // Priority order, held-pawn first: tile-authoritative, so it survives a reload or a username mismatch.
            bool heldHere      = WorkerHolding?.IsHoldingAtTile(s.Tile) == true;
            WorkerProgressDto myWp = null;
            s.WorkerProgress?.TryGetValue(mine, out myWp);
            bool wpHasPawn     = myWp != null && myWp.PawnLoadId > 0 && !myWp.Legacy;
            bool serverListsMe = s.Workers != null && s.Workers.Contains(mine, StringComparer.OrdinalIgnoreCase);
            bool hasPawnLink   = heldHere || wpHasPawn;                 // a real pawn is (or should be) inside
            bool isWorker      = hasPawnLink || serverListsMe;          // any claim of mine → show Recall/Clear

            if (SiteLabels.IsOutpost(s) && !SiteOwnershipClient.IsPlayerControlled(s))
            { DrawOutpostRow(inner, s); return; }

            // A just-captured outpost produces nothing yet, so the ordinary row would read as an empty broken site.
            if (SiteLabels.IsOutpost(s) && s.OutpostState == SiteEntry.OutpostCaptured && string.IsNullOrEmpty(s.ItemDefName))
            { DrawCapturedSetupRow(inner, s, isOwner); return; }

            // Measured from the button column, so text can never run under it.
            float textW = Mathf.Max(60f, inner.width - BtnW - 12f);

            string owner = SiteLabels.Controller(s);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, textW, DialogLayout.TextRowH),
                $"<b>{ItemLabels.ResolveLabel(s.ItemDefName)} site</b> <color=grey>· T{s.OutputTier} · tile {s.Tile} · by</color> {owner}  {AccessTag(s.AccessMode)}");
            string heritage = SiteLabels.HeritageLine(s);
            if (!string.IsNullOrEmpty(heritage))
                DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 4f, textW, DialogLayout.TextRowH), heritage);

            // Paused sites (no workers) never show a bogus cycle time - clear "Paused" text + the caravan->assign hint.
            string prodLine = !s.IsProducing
                ? $"<color=#ffcf59>Paused, no workers</color> <color=grey>· {s.BaseAmountPerCycle}/cycle · {s.Workers.Count}/{s.MaxWorkers} workers · send a caravan to tile {s.Tile} and assign a colonist</color>"
                : $"<color=#cccccc>{s.BaseAmountPerCycle}/cycle × {s.ProductionMultiplier:0.00}{OutputCapTag(s)} · ~{s.EffectiveCycleMinutes:0} min/cycle · {s.Workers.Count}/{s.MaxWorkers} workers</color>";
            Rect prodRect = new Rect(inner.x, inner.y + DialogLayout.TextRowH, textW, DialogLayout.TextRowH);
            DialogLayout.LabelTrunc(prodRect, prodLine);
            if (s.IsProducing && Mouse.IsOver(prodRect)) TooltipHandler.TipRegion(prodRect, OutputTip(s));   // build on hover only

            string myPawn = WorkerPawn(s, mine);
            if (string.IsNullOrEmpty(myPawn) && heldHere) myPawn = WorkerHolding.HeldPawnAtTile(s.Tile)?.Name?.ToStringShort ?? "";
            string who = string.IsNullOrEmpty(myPawn) ? "Your colonist" : myPawn;
            bool paused = myWp != null && !string.IsNullOrEmpty(myWp.BlockedReason);
            string roleLine;
            if (hasPawnLink)
                roleLine = paused
                    ? $"<color=#ffcf59>{who} is away at this site (paused) · {DestLabel(WorkerDest(s, mine))}</color>"
                    : $"<color=#79b8ff>{who} is working at this site (away from home) · lvl {WorkerLevel(s, mine)} · {DestLabel(WorkerDest(s, mine))}</color>";
            else if (serverListsMe)
                roleLine = "<color=#ffcf59>Worker claim needs cleanup - no pawn is here. Use Clear claim.</color>";
            else
                roleLine = isOwner ? "<color=#80ff80>You own this</color>" : "";
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 2f, textW, DialogLayout.TextRowH), roleLine);

            // Worker roster by pawn name (falls back to the account name for legacy workers).
            string roster = WorkerRoster(s);
            if (!string.IsNullOrEmpty(roster))
                DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 3f, textW, DialogLayout.TextRowH),
                    $"<color=grey>Workers: {roster}</color>");

            // Four slots: an owner working their own site needs Recall, Reward, Buildings and Remove at once.
            float bx = inner.xMax - BtnW;
            Rect r0 = new Rect(bx, inner.y, BtnW, BtnH);
            Rect r1 = new Rect(bx, inner.y + BtnPitch, BtnW, BtnH);
            Rect r2 = new Rect(bx, inner.y + BtnPitch * 2f, BtnW, BtnH);
            Rect r3 = new Rect(bx, inner.y + BtnPitch * 3f, BtnW, BtnH);

            if (isWorker)
            {
                // Both labels drop the server claim; only the Recall path also returns a held pawn.
                string label = hasPawnLink ? "Recall pawn" : "Clear claim";
                if (IconButton.Draw(r0, KMHTextures.Cancel, label)) RecallWorker(s);
                if (IconButton.Draw(r1, KMHTextures.Treasury, "Reward to")) ShowDestMenu(s.Tile, s, mine);
                if (isOwner)
                {
                    if (IconButton.Draw(r2, KMHTextures.Post, "Buildings")) OpenBuildings(s.Tile);
                    if (IconButton.Draw(r3, KMHTextures.Cancel, "Remove site")) ConfirmRemoveSite(s.Tile);
                }
            }
            else if (isOwner)
            {
                if (IconButton.Draw(r0, KMHTextures.Claim, "Assign pawn")) PickWorkerPawn(s);
                if (IconButton.Draw(r1, KMHTextures.Post, "Buildings")) OpenBuildings(s.Tile);
                if (IconButton.Draw(r2, KMHTextures.Cancel, "Remove site")) ConfirmRemoveSite(s.Tile);
            }
            else if (s.AccessMode != SiteEntry.AccessPrivate && s.Workers.Count < s.MaxWorkers)
            {
                if (IconButton.Draw(r0, KMHTextures.Claim, "Assign pawn")) PickWorkerPawn(s);
            }
        }

        private static void OpenBuildings(int tile) => Find.WindowStack.Add(new Dialog_KMHSiteBuildings(tile));

        // Shared by the row measurement and the row draw so the two cannot drift apart.
        private const float BtnH = 22f, BtnPitch = 26f;
        private const float ButtonStackH = BtnPitch * 3f + BtnH + 20f;

        private static readonly string[] RowButtonLabels =
            { "Recall pawn", "Clear claim", "Reward to", "Buildings", "Remove site", "Assign pawn" };

        // Sized from the widest label, once: Text.CalcSize is OnGUI-only and this runs per row per frame.
        private static float _btnW;
        private static float BtnW
        {
            get
            {
                if (_btnW <= 0f)
                {
                    float w = 0f;
                    foreach (string s in RowButtonLabels) w = Mathf.Max(w, IconButton.WidthFor(s));
                    _btnW = Mathf.Clamp(Mathf.Ceil(w), 100f, 190f);
                }
                return _btnW;
            }
        }

        private static WorldComponent_KMHSiteWorkers WorkerHolding => WorldComponent_KMHSiteWorkers.Instance;

        private static void ConfirmRemoveSite(int tile)
            => Find.WindowStack.Add(Verse.Dialog_MessageBox.CreateConfirmation(
                "Remove this site? Any colonist working inside is recalled to a caravan at the site tile first.",
                () => { RecallMineAt(tile); SiteHandler.TryCancel(tile); }));

        // Tile-based, not username-based, so it still works when a reconnect changed the session name.
        private static void RecallMineAt(int tile)
        {
            if (WorkerHolding?.IsHoldingAtTile(tile) == true && WorkerHolding.RecallAtTile(tile, out string where))
                Notifications.KmhNotifications.Positive($"Your colonist returned to {where}.");
        }

        private static void RecallWorker(SiteEntry s)
        {
            RecallMineAt(s.Tile);
            SiteHandler.TryLeave(s.Tile);
        }

        // No account-level fallback: a site needs a real pawn that arrived by caravan.
        private static void PickWorkerPawn(SiteEntry s)
        {
            List<Pawn> pawns = PawnsAtSiteTile(s.Tile);
            if (pawns.Count == 0)
            {
                Notifications.KmhNotifications.Rejected($"Send a caravan to this site's tile (world tile {s.Tile}), then assign a colonist.");
                return;
            }
            SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(s.RelevantSkillDef);
            List<FloatMenuOption> opts = new List<FloatMenuOption>();
            foreach (Pawn p in pawns)
            {
                Pawn pawn = p;
                int lvl = skill != null ? (pawn.skills?.GetSkill(skill)?.Level ?? 0) : 0;
                string name = pawn.Name?.ToStringShort ?? pawn.LabelShortCap;
                opts.Add(new FloatMenuOption($"{name}  ({s.RelevantSkillDef} {lvl})", () => AssignWorker(s, pawn, lvl, name)));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // If holding the pawn fails, the tag-only assignment still stands so the worker keeps counting.
        private static void AssignWorker(SiteEntry s, Pawn pawn, int level, string name)
        {
            string mine = KmhSession.Me;
            // One worker per account per site: a second colonist would overwrite the first, who stays trapped inside.
            Pawn already = WorkerHolding?.HeldPawnFor(s.Tile, mine);
            if (already != null && already != pawn)
            {
                Notifications.KmhNotifications.Rejected(
                    $"{already.Name?.ToStringShort ?? "A colonist"} is already working this site for you - recall them first.");
                return;
            }

            bool held = WorkerHolding?.Hold(pawn, s.Tile, mine) == true;
            SiteHandler.TryJoin(s.Tile, level, name, pawn.thingIDNumber, present: true);
            Notifications.KmhNotifications.Positive(held
                ? $"{name} entered the site and is now working there (recall from this menu to bring them home)."
                : $"{name} assigned - keep a caravan on tile {s.Tile} so they keep working (couldn't move them inside).");
        }

        // Colonists in a player caravan sitting on the site's world tile (physical caravan-arrival requirement).
        private static List<Pawn> PawnsAtSiteTile(int tile)
        {
            List<Pawn> outList = new List<Pawn>();
            try
            {
                foreach (RimWorld.Planet.Caravan car in Find.WorldObjects.Caravans)
                {
                    if (car == null || !car.IsPlayerControlled || car.Tile != tile) continue;
                    foreach (Pawn p in car.PawnsListForReading)
                        if (p?.IsColonist == true && !p.Dead) outList.Add(p);
                }
            }
            catch { }
            return outList;
        }

        // Pawn name for a given worker username (empty if legacy/none).
        private static string WorkerPawn(SiteEntry s, string user)
        {
            if (s?.WorkerProgress == null || string.IsNullOrEmpty(user)) return "";
            return s.WorkerProgress.TryGetValue(user, out WorkerProgressDto wp) && wp != null ? (wp.PawnName ?? "") : "";
        }

        // Worker names by pawn; legacy/blocked account-workers show disabled + reason (server-reported).
        private static string WorkerRoster(SiteEntry s)
        {
            if (s?.Workers == null || s.Workers.Count == 0) return "";
            List<string> names = new List<string>();
            foreach (string u in s.Workers)
            {
                s.WorkerProgress.TryGetValue(u, out WorkerProgressDto wp);
                if (wp != null && (wp.Legacy || !string.IsNullOrEmpty(wp.BlockedReason)))
                {
                    string why = wp.Legacy ? "legacy - reassign a pawn" : wp.BlockedReason;
                    names.Add($"<color=#ffcf59>{LinkedAccountsCache.Format(u)} (disabled: {why})</color>");
                    continue;
                }
                string pawn = WorkerPawn(s, u);
                names.Add(string.IsNullOrEmpty(pawn) ? LinkedAccountsCache.Format(u) : $"{pawn} <color=grey>({LinkedAccountsCache.Format(u)})</color>");
            }
            return string.Join(", ", names);
        }

        // An owner who also works the site needs both scopes, or the worker branch wins and site output can never be redirected.
        private static void ShowDestMenu(int tile, SiteEntry s, string mine)
        {
            bool isWorker = s?.WorkerProgress != null && s.WorkerProgress.ContainsKey(mine);
            bool canDirectSite = SiteOwnershipClient.CanManage(s, mine);
            bool hasStorage = StorageSlots(s) > 0;

            var opts = new List<FloatMenuOption>();
            if (isWorker && canDirectSite)
            {
                AddDestOptions(opts, tile, "My share", SiteStore_ScopeMine, hasStorage);
                AddDestOptions(opts, tile, "Site output", SiteStore_ScopeSite, hasStorage);
            }
            else
            {
                AddDestOptions(opts, tile, null, isWorker ? SiteStore_ScopeMine : SiteStore_ScopeSite, hasStorage);
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private const string SiteStore_ScopeMine = "mine";
        private const string SiteStore_ScopeSite = "site";

        private static void AddDestOptions(List<FloatMenuOption> opts, int tile, string prefix, string scope, bool hasStorage)
        {
            string p = string.IsNullOrEmpty(prefix) ? "" : prefix + " → ";
            opts.Add(new FloatMenuOption($"{p}Treasury",
                () => SiteHandler.TrySetDestination(tile, SiteEntry.DestTreasury, scope)));
            opts.Add(new FloatMenuOption($"{p}Colony (delivered in-game)",
                () => SiteHandler.TrySetDestination(tile, SiteEntry.DestCaravan, scope)));
            opts.Add(new FloatMenuOption($"{p}Marketplace (auto-list)",
                () => SiteHandler.TrySetDestination(tile, SiteEntry.DestMarketplace, scope)));
            opts.Add(new FloatMenuOption(
                hasStorage ? $"{p}Site storage (collect it there later)"
                           : $"{p}Site storage - build a storage building first",
                hasStorage ? (Action)(() => SiteHandler.TrySetDestination(tile, SiteEntry.DestStorage, scope)) : null));
        }

        private static int StorageSlots(SiteEntry s)
        {
            int n = 0;
            if (s?.Buildings == null) return 0;
            foreach (SiteBuilding b in s.Buildings)
                if (b != null && b.Kind == SiteBuilding.KindStorage && b.State == SiteBuilding.StateOperational) n++;
            return n;
        }

        private static int WorkerLevel(SiteEntry s, string user)
            => s?.WorkerProgress != null && s.WorkerProgress.TryGetValue(user, out WorkerProgressDto wp) ? wp.CurrentLevel : 0;
        private static string WorkerDest(SiteEntry s, string user)
            => s?.WorkerProgress != null && s.WorkerProgress.TryGetValue(user, out WorkerProgressDto wp) ? wp.Destination : SiteEntry.DestTreasury;

        // 0 means an older addon did not report a cap (a real one is always >= 1.0), so the tier rule is inferred.
        private static double OutputCap(SiteEntry s)
            => s.TierMaxOutputMultiplier > 0 ? s.TierMaxOutputMultiplier
             : s.OutputTier >= 3 ? 1.0 : s.OutputTier == 2 ? 1.25 : 1.5;

        private static double SpeedCap(SiteEntry s)
            => s.TierMaxSpeedMultiplier > 0 ? s.TierMaxSpeedMultiplier
             : s.OutputTier >= 4 ? 1.25 : s.OutputTier == 3 ? 1.5 : s.OutputTier == 2 ? 1.75 : 2.0;

        private static bool OutputCapped(SiteEntry s)
        {
            double cap = OutputCap(s);
            return cap > 0 && s.ProductionMultiplier >= cap - 0.001;
        }

        private static string OutputCapTag(SiteEntry s)
            => OutputCapped(s) ? " <color=grey>(T" + s.OutputTier + " cap)</color>" : "";

        private static string OutputTip(SiteEntry s)
        {
            double outCap = OutputCap(s), spdCap = SpeedCap(s);
            string speed = $"Workers set speed (T{s.OutputTier} cap ×{spdCap:0.00}); skill sets output (cap ×{outCap:0.00}).";
            return outCap <= 1.0
                ? speed + $"\n\nThis tier's output cap is ×1.00, so colonist skill does not raise yield here at any level. Add workers to shorten the cycle instead."
                : speed + (OutputCapped(s) ? "\n\nOutput is at this tier's cap - more skill will not raise it further." : "");
        }

        // Reading "Derelict" under a location someone already captured is worse than saying nothing at all.
        internal static string OutpostLine(SiteEntry s, int claimMinutesLeft)
        {
            switch (s?.OutpostState)
            {
                case SiteEntry.OutpostClaimable:
                {
                    if (claimMinutesLeft <= 0) return "<color=#ffcf59>The claim window has closed.</color>";
                    // Naming the winner up front beats letting someone find out by clicking Claim and being refused.
                    string earned = string.IsNullOrEmpty(s.ClaimEligibleUsername)
                        ? "" : $" <color=grey>· earned by {LinkedAccountsCache.Format(s.ClaimEligibleUsername)}</color>";
                    return $"<color=#80ff80>Restored and unclaimed</color> <color=grey>· closes in ~{claimMinutesLeft} min</color>{earned}";
                }
                case SiteEntry.OutpostHostile:
                    return "<color=#ff9c6b>Held against you. It must be defeated before it can be restored.</color>";
                case SiteEntry.OutpostDefeated:
                    return "<color=grey>Cleared. A Frontier Operation must restore it before it can be claimed.</color>";
                case SiteEntry.OutpostCaptured:
                    return string.IsNullOrEmpty(s.ItemDefName)
                        ? "<color=#ffcf59>Captured and not yet set up</color> <color=grey>· choose what it produces</color>"
                        : "<color=grey>Captured and in production.</color>";
                case SiteEntry.OutpostDormant:
                    return "<color=grey>Dormant. Nobody claimed it in time - it may be reactivated later.</color>";
                default:
                    return "<color=grey>Derelict. A Frontier Operation must restore it before it can be claimed.</color>";
            }
        }

        // Won, but not yet anything. The one action that matters here is choosing what it makes.
        private void DrawCapturedSetupRow(Rect inner, SiteEntry s, bool isOwner)
        {
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width - 120f, DialogLayout.TextRowH),
                $"<b>{SiteLabels.Name(s)}</b> <color=grey>· tile {s.Tile} · held by {SiteLabels.Controller(s)}</color>");
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH, inner.width - 120f, DialogLayout.TextRowH),
                OutpostLine(s, 0));
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 2f, inner.width - 120f, DialogLayout.TextRowH),
                $"<color=#cccccc>Condition {s.Stability}%</color>");

            if (!isOwner) return;
            if (Widgets.ButtonText(new Rect(inner.xMax - 110f, inner.y, 110f, 22f), "Set up site"))
                Find.WindowStack.Add(new Dialog_KMHBuildSite(s.Tile));
        }

        // A contested location: what it is, where it stands, and - only for whoever earned it - a way to take it.
        private void DrawOutpostRow(Rect inner, SiteEntry s)
        {
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width - 220f, DialogLayout.TextRowH),
                $"<b>{SiteLabels.Name(s)}</b> <color=grey>· tile {s.Tile} · {s.OutpostTemplate} · held by {SiteLabels.Controller(s)} · </color>{SiteLabels.StateLabel(s)}");

            int mins = SiteLabels.ClaimMinutesLeft(s);
            string line = OutpostLine(s, mins);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH, inner.width - 220f, DialogLayout.TextRowH), line);

            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 2f, inner.width - 220f, DialogLayout.TextRowH),
                $"<color=#cccccc>Condition {s.Stability}%</color>");

            // The server decides who may claim and says so in the snapshot; this only draws what it was told.
            if (!s.CanClaim) return;
            if (Widgets.ButtonText(new Rect(inner.xMax - 100f, inner.y, 100f, 22f), "Claim"))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Claim for myself", () => SiteHandler.ClaimOutpost(s.Tile, false)),
                };
                // Cosmetic gate only; the server checks membership and rank regardless.
                if (GuildCache.InGuild && !string.IsNullOrEmpty(GuildCache.Guild?.Name))
                    opts.Add(new FloatMenuOption($"Claim for {GuildCache.Guild.Name}", () => SiteHandler.ClaimOutpost(s.Tile, true)));
                else
                    opts.Add(new FloatMenuOption("Claim for my guild (you're not in a guild)", null));
                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        private static string AccessTag(string a)
            => a == SiteEntry.AccessPublic ? "<color=#80ff80>[public]</color>"
             : a == SiteEntry.AccessPrivate ? "<color=grey>[private]</color>"
             : "<color=#ffce4d>[guild]</color>";
        private static string DestLabel(string d)
            => d == SiteEntry.DestMarketplace ? "→ marketplace"
             : d == SiteEntry.DestCaravan ? "→ colony"
             : "→ treasury";
    }
}
