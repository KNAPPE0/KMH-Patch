using System.Collections.Generic;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Build a site on an off-map tile; the server re-validates every field and rejects with a chat reason.
    public class Dialog_KMHBuildSite : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(600f, 600f);

        private string _itemDef    = "";
        private string _skill      = "";   // server-classified, carried in with the picked catalog entry
        private float  _marketValue;
        private string _amount     = "10";
        private string _access     = SiteEntry.AccessGuildOnly;
        private string _archetype  = SiteEntry.ArchetypeCustom;
        private string _tax        = "10";
        private string _dest       = SiteEntry.DestTreasury;
        private string _mktPrice   = "1";
        private int    _tile       = -1;   // chosen world tile; -1 = none picked yet (we never default to home)

        // Setup mode: the same fields against an outpost that already exists, so no tile to pick and no build cost.
        private readonly bool _setup;

        public Dialog_KMHBuildSite(int capturedTile) : this()
        {
            _setup = true;
            _tile  = capturedTile;
        }

        public Dialog_KMHBuildSite()
        {
            doCloseX = true; absorbInputAroundWindow = true; draggable = true;
            // Seeded from a selected caravan only. Never the home tile - defaulting to it dropped every site on the colony.
            Caravan car = CaravanReader.GetSelectedCaravan();
            if (car != null) _tile = car.Tile.tileId;
        }

        private Vector2 _scroll;
        // Published once per frame: writing the measurement straight back would change this frame's viewRect mid-pass.
        private float   _contentH = 600f;
        private float   _measuredH = 600f;
        private int     _contentFrame = -1;

        protected override void DrawContents(Rect rect)
        {
            // "Custom" is one of the site types, so calling the whole screen "custom" read as if it only built that one.
            float top = DialogLayout.DrawTitle(rect, _setup ? "Set up the outpost" : "Build a site");
            DialogLayout.DrawSectionDivider(rect, ref top);

            if (KmhScroll.NewFrame(ref _contentFrame)) _contentH = _measuredH;

            float footerH = 44f;
            Rect  outRect = new Rect(0f, top, rect.width, Mathf.Max(DialogLayout.MinBodyHeight, rect.height - top - footerH));
            float viewW   = Mathf.Max(1f, outRect.width - DialogLayout.ScrollbarReserveWidth);
            Rect  viewRect = new Rect(0f, 0f, viewW, Mathf.Max(_contentH, outRect.height));

            Widgets.BeginScrollView(outRect, ref _scroll, viewRect);
            float measured = DrawFields(new Rect(0f, 0f, viewW, viewRect.height));
            Widgets.EndScrollView();
            _measuredH = measured + 8f;

            const float btnH = 32f;
            float btnW = Mathf.Clamp((rect.width - 16f) / 3f, 70f, 120f);
            float btnY = rect.height - btnH - 4f;
            if (IconButton.Draw(new Rect(0f, btnY, btnW, btnH), KMHTextures.Cancel, "Cancel")) Close();
            DialogLayout.LabelTrunc(new Rect(btnW + 8f, btnY + 6f, Mathf.Max(0f, rect.width - btnW * 2f - 16f), 22f),
                                    QuoteLine(), TextAnchor.MiddleCenter);
            if (IconButton.Draw(new Rect(rect.width - btnW, btnY, btnW, btnH), KMHTextures.Post, _setup ? "Set up" : "Build")) Submit();
            PumpQuote();
        }

        // Setting up a captured outpost is free, so there is nothing to price there.
        private string QuoteLine()
        {
            if (_setup) return "<color=grey>No build cost - this outpost already exists.</color>";
            if (string.IsNullOrEmpty(_itemDef)) return "";
            if (!int.TryParse((_amount ?? "").Trim(), out int amount) || amount <= 0) return "";
            if (!SiteQuoteCache.Matches(_itemDef, amount, _archetype)) return "<color=grey>Pricing…</color>";

            SiteBuildQuote q = SiteQuoteCache.Latest;
            if (!q.Ok) return $"<color=#ff8080>{q.Reason}</color>";
            string cost = $"<b>{SilverFmt.Format(q.Cost)}</b> silver · ~{q.CycleMinutes} min/cycle";
            return q.Affordable
                ? $"<color=#cccccc>{cost}</color>"
                : $"<color=#ffcf59>{cost} - your treasury has {SilverFmt.Format(q.Balance)}</color>";
        }

        // Debounced: amount is a text field, so quoting per keystroke or per frame would storm the server.
        private string _quoteKey = "";
        private string _quoteSent = "";
        // A deadline, not a countdown: this is pumped from the draw, which runs more than once a frame.
        private float  _quoteReadyAt;
        private float  _quoteSentAt = -999f;
        // How long a sent quote is treated as still coming, and how long to wait after a send that never left.
        private const float QuoteReplySeconds = 6f;
        private const float QuoteRetrySeconds = 2f;

        private void PumpQuote()
        {
            if (_setup) return;
            int.TryParse((_amount ?? "").Trim(), out int amount);
            string key = $"{_itemDef}|{amount}|{_archetype}";
            if (key != _quoteKey) { _quoteKey = key; _quoteReadyAt = Time.realtimeSinceStartup + 0.35f; }

            // Latched only while an answer is still plausible; latching unconditionally stuck this on "Pricing…" forever.
            if (_quoteKey == _quoteSent && Time.realtimeSinceStartup - _quoteSentAt < QuoteReplySeconds) return;
            if (string.IsNullOrEmpty(_itemDef) || amount <= 0) return;
            if (Time.realtimeSinceStartup < _quoteReadyAt) return;

            if (!SiteHandler.RequestQuote(_tile, _itemDef, amount, Mathf.RoundToInt(_marketValue), _archetype))
            {
                _quoteSent = "";              // nothing was asked, so nothing is pending
                _quoteReadyAt = Time.realtimeSinceStartup + QuoteRetrySeconds;   // and no retry storm while offline
                return;
            }
            _quoteSent   = _quoteKey;
            _quoteSentAt = Time.realtimeSinceStartup;
        }

        private float DrawFields(Rect rect)
        {
            float y = 0f;
            const float labelW = 170f;

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Location");
            // TileOk would refuse a captured outpost's own tile, because a site is already standing on it.
            string locText = _setup
                ? $"<color=grey>{TileLabel(_tile)} - the outpost you captured</color>"
                : _tile >= 0
                    ? (TileOk(_tile, out _) ? $"<color=grey>{TileLabel(_tile)}</color>"
                                            : $"<color=#ff8080>{TileLabel(_tile)} - not buildable here</color>")
                    : "<color=#ff8080>(no tile chosen - use Select on map)</color>";
            DialogLayout.LabelTrunc(new Rect(labelW, y + 4f, rect.width - labelW, 22f), locText);
            y += 28f;

            if (!_setup)
            {
                if (Widgets.ButtonText(new Rect(labelW, y, 150f, 26f), "Select on map"))
                    OpenTileSelector();
                Caravan car = CaravanReader.GetSelectedCaravan();
                if (car != null && Widgets.ButtonText(new Rect(labelW + 158f, y, rect.width - labelW - 158f, 26f),
                        $"Use caravan ({car.LabelCap})"))
                    _tile = car.Tile.tileId;
                y += 34f;
            }

            // Archetype is drawn first: below "Produces" it read as if the two were alternatives.
            SiteCatalogSnapshot catalog = SiteCatalogCache.Catalog;

            // Applied here, between frames: a mid-frame archetype change hands BeginScrollView two viewRects for one frame.
            if (_pendingArchetype != null) { _archetype = _pendingArchetype; _pendingArchetype = null; }

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "<b>1. Type of site</b>");
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), ArchetypeButtonLabel(catalog, _archetype)))
                Find.WindowStack.Add(new FloatMenu(ArchetypeOptions(catalog)));
            y += 30f;
            y = Hint(rect, y, ArchetypeHint(catalog, _archetype));

            // Cleared rather than carried: the server would refuse the build without saying which field was wrong.
            if (!string.IsNullOrEmpty(_itemDef) && !OutputFitsArchetype(catalog, _itemDef, _archetype))
            {
                _itemDef = ""; _skill = ""; _marketValue = 0f;
            }

            // The curated server catalog, not the all-def browser: a site can only produce what this list offers.
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "<b>2. Produces</b>");
            string itemDisplay = string.IsNullOrEmpty(_itemDef)
                ? "<color=#ff8080>(required - choose a site output)</color>"
                : ItemLabels.ResolveLabel(_itemDef);
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), itemDisplay))
            {
                Find.WindowStack.Add(new Dialog_KMHSiteOutputPicker(entry =>
                {
                    _itemDef = entry.DefName;
                    _amount  = Mathf.Max(1, entry.MaxAmount).ToString();
                    // The server's own classification - the same one the built site will use.
                    _skill   = entry.RelevantSkill ?? "";
                    ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(entry.DefName);
                    _marketValue = td?.BaseMarketValue ?? 0f;
                }, _archetype));
            }
            y += 30f;
            y = Hint(rect, y, OutputHint(catalog, _archetype));

            // The server's own multiplier, so this preview cannot drift from what the treasury is charged.
            SiteArchetypeInfo picked = FindArchetype(catalog, _archetype);
            if (picked != null && picked.CostMultiplier > 1.0001)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, DialogLayout.TextRowH),
                    $"<color=#E2C16B>{picked.DisplayName} costs {picked.CostMultiplier:0.00}x to build - the price of picking from the whole pool.</color>");
                y += DialogLayout.TextRowH + 2f;
            }
            else if (picked != null && !string.IsNullOrEmpty(picked.PerkText))
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, DialogLayout.TextRowH),
                    $"<color=#8FD98F>{picked.PerkText}</color>");
                y += DialogLayout.TextRowH + 2f;
            }

            if (!string.IsNullOrEmpty(_itemDef))
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, DialogLayout.TextRowH),
                    $"<color=grey>Market value ~{_marketValue:0} silver/unit{(string.IsNullOrEmpty(_skill) ? "" : $" · skill: {_skill}")}</color>");
                y += DialogLayout.TextRowH + 2f;
            }

            y = Row(rect, y, "Amount per cycle", ref _amount);

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Access");
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), AccessLabel(_access)))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(AccessLabel(SiteEntry.AccessGuildOnly), () => _access = SiteEntry.AccessGuildOnly),
                    new FloatMenuOption(AccessLabel(SiteEntry.AccessPublic),    () => _access = SiteEntry.AccessPublic),
                    new FloatMenuOption(AccessLabel(SiteEntry.AccessPrivate),   () => _access = SiteEntry.AccessPrivate),
                }));
            y += 30f;

            if (_access == SiteEntry.AccessPublic)
                y = Row(rect, y, "Owner tax % (0-50)", ref _tax);

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Reward to");
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), DestLabel(_dest)))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(DestLabel(SiteEntry.DestTreasury),    () => _dest = SiteEntry.DestTreasury),
                    new FloatMenuOption(DestLabel(SiteEntry.DestCaravan),     () => _dest = SiteEntry.DestCaravan),
                    new FloatMenuOption(DestLabel(SiteEntry.DestMarketplace), () => _dest = SiteEntry.DestMarketplace),
                }));
            y += 30f;

            if (_dest == SiteEntry.DestMarketplace)
                y = Row(rect, y, "Marketplace price/unit", ref _mktPrice);

            return y;
        }

        private void Submit()
        {
            if (!_setup && !TileOk(_tile, out string tileReason))
            { Notifications.KmhNotifications.Rejected(_tile < 0 ? "Pick a tile first (Select on map)" : $"Can't build there - {tileReason}"); return; }
            if (string.IsNullOrEmpty(_itemDef)) { Notifications.KmhNotifications.Rejected("Pick an item to produce"); return; }
            if (!int.TryParse((_amount ?? "").Trim(), out int amount) || amount <= 0) { Notifications.KmhNotifications.Rejected("Amount per cycle must be > 0"); return; }
            int tax = 0;
            if (_access == SiteEntry.AccessPublic && (!int.TryParse((_tax ?? "").Trim(), out tax) || tax < 0 || tax > 50)) { Notifications.KmhNotifications.Rejected("Owner tax must be 0-50"); return; }
            int mkt = 1;
            if (_dest == SiteEntry.DestMarketplace && (!int.TryParse((_mktPrice ?? "").Trim(), out mkt) || mkt < 1)) { Notifications.KmhNotifications.Rejected("Marketplace price must be >= 1"); return; }

            bool sent = _setup
                ? SiteHandler.TrySetup(_tile, _itemDef, amount, Mathf.RoundToInt(_marketValue), _access, tax, _dest, mkt, _archetype)
                : SiteHandler.TryBuild(_tile, _itemDef, amount, Mathf.RoundToInt(_marketValue), _access, tax, _dest, mkt, _archetype);
            if (sent) Close();
        }

        private void OpenTileSelector()
        {
            // Both this dialog and the parent Sites window absorb input and would block world clicks while targeting.
            Find.WindowStack.TryRemove(typeof(Dialog_KMHSites), false);
            Close(false);
            CameraJumper.TryShowWorld();
            Find.WorldTargeter.BeginTargeting(OnTilePicked, true);
        }

        private bool OnTilePicked(GlobalTargetInfo target)
        {
            int tile = target.Tile.tileId;
            if (!TileOk(tile, out string reason))
            {
                Messages.Message($"KMH: can't build a site here - {reason}.", MessageTypeDefOf.RejectInput, false);
                return false; // invalid - keep targeting
            }
            _tile = tile;
            Find.WindowStack.Add(this); // reopen the build dialog (same instance keeps every field)
            return true;                // done targeting
        }

        // A caravan on the tile deliberately does NOT block, so "visit then build" works.
        private static bool TileOk(int tile, out string reason)
        {
            reason = "";
            if (tile < 0) { reason = "no tile selected"; return false; }
            try
            {
                if (Find.World.Impassable(tile)) { reason = "impassable terrain (ocean / mountain)"; return false; }
                foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tile))
                    if (wo is MapParent || wo is KMHSiteWorldObject) { reason = "the tile already has a base or site"; return false; }
            }
            catch (System.Exception ex) { reason = ex.Message; return false; }
            return true;
        }

        private static string TileLabel(int tile) => tile < 0 ? "(none)" : $"tile {tile}";

        private static float Row(Rect rect, float y, string label, ref string val)
        {
            const float labelW = 170f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), label);
            val = Widgets.TextField(new Rect(labelW, y, rect.width - labelW, 26f), val ?? "");
            return y + 30f;
        }

        private static string AccessLabel(string a)
            => a == SiteEntry.AccessPublic ? "Public (owner takes a cut)"
             : a == SiteEntry.AccessPrivate ? "Private (no workers)"
             : "Guild + allies";

        private static string DestLabel(string d)
            => d == SiteEntry.DestMarketplace ? "Marketplace (auto-list)"
             : d == SiteEntry.DestCaravan ? "Colony (delivered in-game)"
             : "Treasury";

        // Server-supplied; the hardcoded fallback is only for a server too old to send the list, which would show an empty menu.
        private static List<FloatMenuOption> ArchetypeOptions(SiteCatalogSnapshot cat)
        {
            var opts = new List<FloatMenuOption>();
            if (cat?.Archetypes != null && cat.Archetypes.Count > 0)
            {
                foreach (SiteArchetypeInfo a in cat.Archetypes)
                {
                    if (a == null || string.IsNullOrEmpty(a.Id)) continue;
                    SiteArchetypeInfo captured = a;
                    opts.Add(new FloatMenuOption(ArchetypeMenuLabel(captured), () => _pendingArchetype = captured.Id));
                }
                return opts;
            }

            foreach (string id in new[]
            {
                SiteEntry.ArchetypeCustom, SiteEntry.ArchetypeFarmland, SiteEntry.ArchetypeQuarry,
                SiteEntry.ArchetypeWoodland, SiteEntry.ArchetypeRanch, SiteEntry.ArchetypeRoadworks,
            })
            {
                string captured = id;
                opts.Add(new FloatMenuOption(LegacyArchetypeLabel(captured), () => _pendingArchetype = captured));
            }
            return opts;
        }

        // FloatMenu callbacks fire mid-frame; parked here and applied at the top of the next draw.
        private static string _pendingArchetype;

        private static string ArchetypeMenuLabel(SiteArchetypeInfo a)
        {
            string skill = string.IsNullOrEmpty(a.WorkerSkill) ? "uses the output's own skill" : a.WorkerSkill;
            string cost  = a.CostMultiplier > 1.0001 ? $" · {a.CostMultiplier:0.00}x cost" : "";
            return $"{a.DisplayName} - {skill}{cost}";
        }

        private static string ArchetypeButtonLabel(SiteCatalogSnapshot cat, string archetype)
        {
            SiteArchetypeInfo a = FindArchetype(cat, archetype);
            return a != null ? ArchetypeMenuLabel(a) : LegacyArchetypeLabel(archetype);
        }

        private static string LegacyArchetypeLabel(string archetype)
        {
            switch (archetype)
            {
                case SiteEntry.ArchetypeFarmland:  return "Farmland - crops (Plants)";
                case SiteEntry.ArchetypeQuarry:    return "Quarry - stone and ore (Mining)";
                case SiteEntry.ArchetypeWoodland:  return "Woodland - timber and forage (Plants)";
                case SiteEntry.ArchetypeRanch:     return "Ranch - animal products (Animals)";
                case SiteEntry.ArchetypeRoadworks: return "Roadworks - also unlocks road building (Construction)";
                default:                           return "Custom - uses the output's own skill";
            }
        }

        internal static SiteArchetypeInfo FindArchetype(SiteCatalogSnapshot cat, string id)
        {
            if (cat?.Archetypes == null || string.IsNullOrEmpty(id)) return null;
            foreach (SiteArchetypeInfo a in cat.Archetypes)
                if (a != null && string.Equals(a.Id, id, System.StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        // Reads the server's own per-item answer, never re-derives one; true when the server has classified nothing yet.
        private static bool OutputFitsArchetype(SiteCatalogSnapshot cat, string defName, string archetype)
        {
            if (cat == null || !cat.ArchetypesReady || string.IsNullOrEmpty(defName)) return true;
            foreach (SiteCatalogEntry e in cat.Entries)
            {
                if (e == null || !string.Equals(e.DefName, defName, System.StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string id in (e.AllowedArchetypes ?? "").Split(','))
                    if (string.Equals(id.Trim(), archetype, System.StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            return true;   // not in the catalog at all - let the server be the one to refuse it
        }

        // Prefers the server's own description, so an owner-added archetype explains itself without a client build.
        private static string ArchetypeHint(SiteCatalogSnapshot cat, string archetype)
        {
            SiteArchetypeInfo a = FindArchetype(cat, archetype);
            if (a != null && !string.IsNullOrEmpty(a.Description))
            {
                string extra = a.UnlocksRoadworks
                    ? " It also unlocks the Roadworks window, where you plan roads on the world map."
                    : "";
                string perk = string.IsNullOrEmpty(a.PerkText) ? "" : " " + a.PerkText;
                return a.Description + extra + perk;
            }

            switch (archetype)
            {
                case SiteEntry.ArchetypeFarmland:
                    return "Names the site and makes its workers use <b>Plants</b>. It still produces the output you pick below.";
                case SiteEntry.ArchetypeQuarry:
                    return "Names the site and makes its workers use <b>Mining</b>. It still produces the output you pick below.";
                case SiteEntry.ArchetypeWoodland:
                    return "Names the site and makes its workers use <b>Plants</b>. It still produces the output you pick below.";
                case SiteEntry.ArchetypeRanch:
                    return "Names the site and makes its workers use <b>Animals</b>. It still produces the output you pick below.";
                case SiteEntry.ArchetypeRoadworks:
                    return "Works like any other site AND unlocks the Roadworks window, where you plan roads on the world map. "
                         + "Its workers use <b>Construction</b>. It still produces the output you pick below.";
                default:
                    return "Keeps whatever skill the output implies, and names the site after it. Pick a specific type above "
                         + "if you would rather choose the skill yourself.";
            }
        }

        // Roadworks is the one archetype players assume needs no output, so it spells out what its output is for.
        private static string OutputHint(SiteCatalogSnapshot cat, string archetype)
        {
            SiteArchetypeInfo a = FindArchetype(cat, archetype);
            string scope = a == null || a.IsCustom
                ? ""
                : $" Only what a {a.DisplayName.ToLowerInvariant()} can produce is listed.";

            return archetype == SiteEntry.ArchetypeRoadworks
                ? "Required. A Roadworks site still produces goods like any other - and its output's tier is what decides "
                + "how good a road it can build (Trail / Road / Highway)." + scope
                : "Required. What this site makes each cycle. Its market value sets the build cost and the cycle time." + scope;
        }

        // Muted explanation under a control. Wraps rather than truncates: a half sentence explains nothing.
        private static float Hint(Rect rect, float y, string text)
        {
            if (string.IsNullOrEmpty(text)) return y;
            Color old = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            float w = Mathf.Max(1f, rect.width - 8f);
            float h = Text.CalcHeight(text, w);
            Widgets.Label(new Rect(4f, y, w, h), text);
            GUI.color = old;
            return y + h + 6f;
        }

    }
}
