using System;
using System.Collections.Generic;
using KMHPatch.Features.Quests.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Quests
{
    // Quest Post composer for DeliverItem + Bounty kinds (Bounty hides delivery rows - it's manual poster sign-off).
    public class Dialog_KMHPostQuest : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(620f, 700f);

        private string _kind              = QuestEntry.KindDeliverItem;
        private string _visibility        = QuestHandler.QuestVisibilityPublic;
        private string _title             = "";
        private string _description       = "";
        private string _bountySilver      = "0";
        // Auto-cancel after this many hours; 0 / blank = never expires.
        private string _expiresHours      = "0";
        private string _targetItemDefName = "";
        private string _targetItemQty     = "1";
        private int    _targetQualityIdx  = 0; // 0 = any, 1..7 = Awful..Legendary (or better)

        private string _escortPickupTile  = "";
        private string _escortDropoffTile = "";
        private string _escortTargetDesc  = "";
        private string _defendColonyTile  = "";
        private string _defendDurationDays = "1";       // converted to game ticks on submit
        private string _huntTargetKind     = QuestEntry.HuntAnimalSpecies;
        private string _huntTargetDefName  = "";
        private string _huntTargetLabel    = "";   // friendly display for the picked target
        private string _huntTargetCount    = "1";
        private string _buildAtTile        = "";
        private string _buildStructureDef  = "";
        private string _buildStructureLabel = ""; // friendly display for the picked structure
        private string _buildCount         = "1";

        public Dialog_KMHPostQuest()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Post a new quest");
            DialogLayout.DrawSectionDivider(rect, ref y);

            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f),
                "Bounty silver is escrowed from your treasury / personal vault at post time.");
            GUI.color = oldCol;
            y += 22f;

            const float labelW = 180f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Kind");
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), KindLabel(_kind)))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(KindLabel(QuestEntry.KindDeliverItem), () => _kind = QuestEntry.KindDeliverItem),
                    new FloatMenuOption(KindLabel(QuestEntry.KindBounty),      () => _kind = QuestEntry.KindBounty),
                    new FloatMenuOption(KindLabel(QuestEntry.KindEscort),      () => _kind = QuestEntry.KindEscort),
                    new FloatMenuOption(KindLabel(QuestEntry.KindDefend),      () => _kind = QuestEntry.KindDefend),
                    new FloatMenuOption(KindLabel(QuestEntry.KindHunt),        () => _kind = QuestEntry.KindHunt),
                    new FloatMenuOption(KindLabel(QuestEntry.KindBuild),       () => _kind = QuestEntry.KindBuild),
                    new FloatMenuOption(KindLabel(QuestEntry.KindCustom),      () => _kind = QuestEntry.KindCustom),
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += 30f;

            // The snapshot never says whether an owner routed auto-verified kinds to review, so nothing may promise settlement.
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f),
                "<color=grey>Auto-verified kinds settle without you acting - unless this server asks the poster to sign off first.</color>");
            y += 20f;

            // The server forces a personal poster to public: there is no guild key to scope the quest against.
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Visibility");
            string visLabel = _visibility == QuestHandler.QuestVisibilityGuildOnly
                ? "Guild + allies only"
                : "Public (all servers)";
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), visLabel))
            {
                List<FloatMenuOption> visOpts = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Public (all servers)",
                        () => _visibility = QuestHandler.QuestVisibilityPublic),
                    new FloatMenuOption("Guild + allies only",
                        () => _visibility = QuestHandler.QuestVisibilityGuildOnly),
                };
                Find.WindowStack.Add(new FloatMenu(visOpts));
            }
            y += 30f;

            y = DrawTextRow(rect, y, "Title", ref _title);

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Description");
            string descPreview = string.IsNullOrEmpty(_description)
                ? "<color=grey>(click to edit)</color>"
                : (_description.Length > 60 ? _description.Substring(0, 60) + "…" : _description);
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), descPreview))
            {
                Find.WindowStack.Add(new Dialog_KMHMultilineTextInput(
                    title:        "Quest description",
                    confirmLabel: "Save",
                    initial:      _description ?? "",
                    maxChars:     1024,
                    rejectEmpty:  false,
                    onConfirm:    s => _description = s));
            }
            y += 30f;

            y += 8f;

            Text.Font = GameFont.Medium;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), "Bounty + delivery");
            Text.Font = GameFont.Small;
            y += 26f;

            y = DrawTextRow(rect, y, "Bounty silver", ref _bountySilver);
            y = DrawTextRow(rect, y, "Expires in (hours, 0=never)", ref _expiresHours);
            y = DrawPerKindFields(rect, y, labelW);

            const float btnW = 120f;
            const float btnH = 32f;
            float btnY = rect.height - btnH - 4f;

            if (IconButton.Draw(new Rect(0f, btnY, btnW, btnH), KMHTextures.Cancel, "Cancel"))
            {
                Close();
            }
            if (IconButton.Draw(new Rect(rect.width - btnW, btnY, btnW, btnH), KMHTextures.Post, "Post"))
            {
                Submit();
            }
        }

        private static float DrawTextRow(Rect rect, float y, string label, ref string value)
        {
            const float labelW = 180f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), label);
            value = Widgets.TextField(new Rect(labelW, y, rect.width - labelW, 26f), value ?? "");
            return y + 30f;
        }

        private static float DrawPickRow(Rect rect, float y, string label, string currentLabel,
            string placeholder, List<Dialog_KMHDefPicker.Entry> source, Action<string, string> onPick)
        {
            const float labelW = 180f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), label);
            string display = string.IsNullOrEmpty(currentLabel) ? $"<color=grey>{placeholder}</color>" : currentLabel;
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), display))
                Find.WindowStack.Add(new Dialog_KMHDefPicker($"Pick {label.ToLower()}", source, onPick));
            return y + 30f;
        }

        // Number field for a world-tile id with a "Current" button that fills the player's current map tile
        private static float DrawTileRow(Rect rect, float y, string label, ref string value)
        {
            const float labelW = 180f;
            const float btnW   = 76f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), label);
            float fieldW = rect.width - labelW - btnW - 6f;
            value = Widgets.TextField(new Rect(labelW, y, fieldW, 26f), value ?? "");
            if (Widgets.ButtonText(new Rect(labelW + fieldW + 6f, y, btnW, 26f), "Current"))
            {
                int tile = Find.CurrentMap?.Tile ?? -1;
                if (tile >= 0) value = tile.ToString();
                else Notifications.KmhNotifications.Neutral("No current map tile - enter one manually.");
            }
            return y + 30f;
        }

        private static string KindLabel(string kind)
        {
            switch (kind)
            {
                case QuestEntry.KindBounty: return "Bounty (manual sign-off)";
                case QuestEntry.KindEscort: return "Escort (auto-verified)";
                case QuestEntry.KindDefend: return "Defend (auto-verified)";
                case QuestEntry.KindHunt:   return "Hunt (auto-verified)";
                case QuestEntry.KindBuild:  return "Build (auto-verified)";
                case QuestEntry.KindCustom: return "Custom (proof + review)";
                default:                    return "Deliver item (auto-verified)";
            }
        }

        // Draws the input rows specific to the selected kind.
        private float DrawPerKindFields(Rect rect, float y, float labelW)
        {
            switch (_kind)
            {
                case QuestEntry.KindDeliverItem:
                    DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Target item");
                    string targetDisplay = string.IsNullOrEmpty(_targetItemDefName)
                        ? "<color=grey>(pick an item)</color>"
                        : ItemLabels.ResolveLabel(_targetItemDefName);
                    if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), targetDisplay))
                    {
                        UI.KmhItemPickerService.Open(
                            title:           "Pick target item",
                            pickActionLabel: "Select",
                            source:          UI.KmhItemPickerService.AllPickableItems(),
                            onPick:          (defName, qty) => { _targetItemDefName = defName; _targetItemQty = qty.ToString(); },
                            closeOnPick:     true);
                    }
                    y += 30f;
                    y = DrawTextRow(rect, y, "Target item qty", ref _targetItemQty);

                    // quality requirement - delivered items must match or beat it, Any skips the check
                    DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Required quality");
                    string qLabel = _targetQualityIdx > 0 ? $"{ItemKeys.QualityName(_targetQualityIdx)} or better" : "Any";
                    if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), qLabel))
                    {
                        List<FloatMenuOption> qOpts = new List<FloatMenuOption>
                        {
                            new FloatMenuOption("Any", () => _targetQualityIdx = 0),
                        };
                        for (int qi = 1; qi <= 7; qi++)
                        {
                            int captured = qi;
                            qOpts.Add(new FloatMenuOption($"{ItemKeys.QualityName(captured)} or better",
                                () => _targetQualityIdx = captured));
                        }
                        Find.WindowStack.Add(new FloatMenu(qOpts));
                    }
                    return y + 30f;

                case QuestEntry.KindEscort:
                    y = DrawTileRow(rect, y, "Pickup tile",  ref _escortPickupTile);
                    y = DrawTileRow(rect, y, "Dropoff tile", ref _escortDropoffTile);
                    return DrawTextRow(rect, y, "Who/what to escort", ref _escortTargetDesc);

                case QuestEntry.KindDefend:
                    y = DrawTileRow(rect, y, "Colony tile to defend", ref _defendColonyTile);
                    return DrawTextRow(rect, y, "Defense window (days)", ref _defendDurationDays);

                case QuestEntry.KindHunt:
                    DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Hunt target type");
                    if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), HuntKindLabel(_huntTargetKind)))
                    {
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                        {
                            new FloatMenuOption(HuntKindLabel(QuestEntry.HuntAnimalSpecies), () => SetHuntKind(QuestEntry.HuntAnimalSpecies)),
                            new FloatMenuOption(HuntKindLabel(QuestEntry.HuntPawnKind),      () => SetHuntKind(QuestEntry.HuntPawnKind)),
                            new FloatMenuOption(HuntKindLabel(QuestEntry.HuntNamedRaider),   () => SetHuntKind(QuestEntry.HuntNamedRaider)),
                        }));
                    }
                    y += 30f;
                    // Named raider is a free-text name; species / pawn-kind pick from a list.
                    if (_huntTargetKind == QuestEntry.HuntNamedRaider)
                        y = DrawTextRow(rect, y, "Raider name", ref _huntTargetDefName);
                    else
                        y = DrawPickRow(rect, y, "Target", _huntTargetLabel, "(pick a target)",
                            _huntTargetKind == QuestEntry.HuntPawnKind ? DefBrowsers.PawnKinds() : DefBrowsers.Animals(),
                            (d, l) => { _huntTargetDefName = d; _huntTargetLabel = l; });
                    return DrawTextRow(rect, y, "How many", ref _huntTargetCount);

                case QuestEntry.KindBuild:
                    y = DrawTileRow(rect, y, "Build at tile", ref _buildAtTile);
                    y = DrawPickRow(rect, y, "Structure", _buildStructureLabel, "(pick a structure)",
                        DefBrowsers.Buildings(),
                        (d, l) => { _buildStructureDef = d; _buildStructureLabel = l; });
                    return DrawTextRow(rect, y, "How many", ref _buildCount);

                case QuestEntry.KindCustom:
                {
                    Color old = GUI.color;
                    GUI.color = DialogLayout.MutedColor;
                    Widgets.Label(new Rect(0f, y + 4f, rect.width, 36f),
                        "Custom quests are judged by you: the claimer submits proof and you approve or reject. Put clear instructions (20+ chars) in the description.");
                    GUI.color = old;
                    return y + 40f;
                }

                default: // Bounty
                {
                    Color old = GUI.color;
                    GUI.color = DialogLayout.MutedColor;
                    DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 22f),
                        "Bounty quests have no automatic target - the claimer marks for your sign-off.");
                    GUI.color = old;
                    return y + 30f;
                }
            }
        }

        // An animal defName is not a valid pawn-kind, so switching sub-kind must clear the picked target.
        private void SetHuntKind(string k)
        {
            if (_huntTargetKind == k) return;
            _huntTargetKind    = k;
            _huntTargetDefName = "";
            _huntTargetLabel   = "";
        }

        private static string HuntKindLabel(string k)
        {
            switch (k)
            {
                case QuestEntry.HuntPawnKind:    return "Pawn kind";
                case QuestEntry.HuntNamedRaider: return "Named raider";
                default:                         return "Animal species";
            }
        }

        private void Submit()
        {
            if (!int.TryParse((_bountySilver ?? "").Trim(), out int bounty) || bounty < 0)
            {
                Notifications.KmhNotifications.Rejected("Bounty silver: enter a whole number (0 allowed)");
                return;
            }

            // Expiry: empty + "0" + omitted all mean "never expires".
            string expRaw = (_expiresHours ?? "").Trim();
            int expHours = 0;
            if (expRaw.Length > 0 && (!int.TryParse(expRaw, out expHours) || expHours < 0))
            {
                Notifications.KmhNotifications.Rejected("Expires in: enter 0 (never) or a positive whole number of hours");
                return;
            }

            QuestEntry draft = new QuestEntry
            {
                Kind         = _kind,
                Visibility   = _visibility,
                Title        = (_title ?? "").Trim(),
                Description  = (_description ?? "").Trim(),
                BountySilver = bounty,
            };

            // Light client-side checks only; the server re-validates and replies with a chat reason on rejection.
            switch (_kind)
            {
                case QuestEntry.KindDeliverItem:
                    if (!int.TryParse((_targetItemQty ?? "").Trim(), out int dq) || dq <= 0)
                    { Notifications.KmhNotifications.Rejected("Target item qty: enter a positive whole number"); return; }
                    draft.TargetItemDefName  = (_targetItemDefName ?? "").Trim();
                    draft.TargetItemQty      = dq;
                    draft.TargetQualityIndex = _targetQualityIdx;
                    break;

                case QuestEntry.KindEscort:
                    if (!TryTile(_escortPickupTile, out int ep) || !TryTile(_escortDropoffTile, out int ed))
                    { Notifications.KmhNotifications.Rejected("Pickup/dropoff tiles must be whole numbers"); return; }
                    draft.EscortPickupTile        = ep;
                    draft.EscortDropoffTile       = ed;
                    draft.EscortTargetDescription = (_escortTargetDesc ?? "").Trim();
                    break;

                case QuestEntry.KindDefend:
                    if (!TryTile(_defendColonyTile, out int dc))
                    { Notifications.KmhNotifications.Rejected("Colony tile must be a whole number"); return; }
                    if (!double.TryParse((_defendDurationDays ?? "").Trim(), out double days) || days <= 0)
                    { Notifications.KmhNotifications.Rejected("Defense window: enter days > 0"); return; }
                    draft.DefendColonyTile        = dc;
                    draft.DefendDurationGameTicks = (long)(days * 60000L); // 60k ticks per in-game day
                    break;

                case QuestEntry.KindHunt:
                    if (string.IsNullOrWhiteSpace(_huntTargetDefName))
                    { Notifications.KmhNotifications.Rejected(_huntTargetKind == QuestEntry.HuntNamedRaider ? "Enter a raider name" : "Pick a hunt target"); return; }
                    if (!int.TryParse((_huntTargetCount ?? "").Trim(), out int hc) || hc <= 0)
                    { Notifications.KmhNotifications.Rejected("Hunt count: enter a positive whole number"); return; }
                    draft.HuntTargetKind    = _huntTargetKind;
                    draft.HuntTargetDefName = (_huntTargetDefName ?? "").Trim();
                    draft.HuntTargetCount   = hc;
                    break;

                case QuestEntry.KindBuild:
                    if (string.IsNullOrWhiteSpace(_buildStructureDef))
                    { Notifications.KmhNotifications.Rejected("Pick a structure to build"); return; }
                    if (!TryTile(_buildAtTile, out int bt))
                    { Notifications.KmhNotifications.Rejected("Build tile must be a whole number"); return; }
                    if (!int.TryParse((_buildCount ?? "").Trim(), out int bc) || bc <= 0)
                    { Notifications.KmhNotifications.Rejected("Build count: enter a positive whole number"); return; }
                    draft.BuildAtTile           = bt;
                    draft.BuildStructureDefName = (_buildStructureDef ?? "").Trim();
                    draft.BuildCount            = bc;
                    break;

                case QuestEntry.KindCustom:
                    if (draft.Description.Length < 20)
                    { Notifications.KmhNotifications.Rejected("Custom quests need a 20+ char description"); return; }
                    break;
            }

            if (QuestHandler.TryPostDraft(draft, expHours))
                Close();
        }

        private static bool TryTile(string s, out int tile)
            => int.TryParse((s ?? "").Trim(), out tile) && tile >= 0;
    }
}
