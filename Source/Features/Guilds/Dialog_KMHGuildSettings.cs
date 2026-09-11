using KMHPatch.Features.Guilds.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Guilds
{
    // Per-rank caps: -1 unlimited, 0 disabled, positive a literal silver cap.
    public class Dialog_KMHGuildSettings : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(520f, 540f);

        // Working copy - we mutate this freely; only on Save do we send.
        private GuildSettingsDto _draft;

        // Held as strings so partial typing like "12-" or an empty box cannot crash the parse.
        private string _siteTax    = "";
        private string _marketTax  = "";
        private string _memberCap  = "";
        private string _officerCap = "";
        private string _modCap     = "";
        private string _adminCap   = "";

        public Dialog_KMHGuildSettings(GuildSettingsDto initial)
        {
            _draft = initial ?? new GuildSettingsDto();
            _siteTax    = _draft.SiteRewardSilverTaxPercent.ToString();
            _marketTax  = _draft.MarketplaceSaleTaxPercent.ToString();
            _memberCap  = _draft.MemberDailyWithdrawCap.ToString();
            _officerCap = _draft.OfficerDailyWithdrawCap.ToString();
            _modCap     = _draft.ModeratorDailyWithdrawCap.ToString();
            _adminCap   = _draft.AdminDailyWithdrawCap.ToString();

            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Guild settings");
            DialogLayout.DrawSectionDivider(rect, ref y);

            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f),
                "Tax percents apply to every member's earnings. " +
                "Per-rank caps: -1 unlimited, 0 disabled.");
            GUI.color = oldCol;
            y += 22f;

            Text.Font = GameFont.Medium;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), "Tax rates");
            Text.Font = GameFont.Small;
            y += 26f;
            y = DrawNumberRow(rect, y, "Site reward silver tax %",        ref _siteTax);
            y = DrawNumberRow(rect, y, "Marketplace sale tax %",          ref _marketTax);

            y += 8f;

            Text.Font = GameFont.Medium;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), "Daily withdraw caps (silver)");
            Text.Font = GameFont.Small;
            y += 26f;
            y = DrawNumberRow(rect, y, "Member cap",    ref _memberCap);
            y = DrawNumberRow(rect, y, "Officer cap",   ref _officerCap);
            y = DrawNumberRow(rect, y, "Moderator cap", ref _modCap);
            y = DrawNumberRow(rect, y, "Admin cap",     ref _adminCap);

            y += 8f;

            bool defaultGuildOnly = _draft.DefaultListingsGuildOnly;
            Widgets.CheckboxLabeled(new Rect(0f, y, rect.width, 24f),
                "Default new listings to guild-only",
                ref defaultGuildOnly);
            _draft.DefaultListingsGuildOnly = defaultGuildOnly;
            y += 28f;

            const float btnW = 120f;
            const float btnH = 32f;
            float btnY = rect.height - btnH - 4f;

            if (Widgets.ButtonText(new Rect(0f, btnY, btnW, btnH), "Cancel"))
            {
                Close();
            }
            if (Widgets.ButtonText(new Rect(rect.width - btnW, btnY, btnW, btnH), "Save"))
            {
                if (TryCommitDraft())
                {
                    GuildHandler.TrySaveSettings(_draft);
                    Close();
                }
            }
        }

        private static float DrawNumberRow(Rect rect, float y, string label, ref string value)
        {
            const float labelW = 240f;
            const float fieldW = 120f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), label);
            value = Widgets.TextField(new Rect(labelW, y, fieldW, 26f), value ?? "");
            return y + 30f;
        }

        // The whole save is rejected if any field fails, so nothing is ever partially applied.
        private bool TryCommitDraft()
        {
            if (!TryParseInt(_siteTax,   out int siteTax,   nonNegative: true,  fieldName: "Site reward tax")) return false;
            if (!TryParseInt(_marketTax, out int marketTax, nonNegative: true,  fieldName: "Marketplace sale tax")) return false;
            if (siteTax > 50 || marketTax > 50)
            {
                Notifications.KmhNotifications.Rejected("Tax percents must be 0..50");
                return false;
            }
            // Caps: allow -1 (unlimited) or >= 0.
            if (!TryParseInt(_memberCap,  out int memberCap,  nonNegative: false, fieldName: "Member cap"))    return false;
            if (!TryParseInt(_officerCap, out int officerCap, nonNegative: false, fieldName: "Officer cap"))   return false;
            if (!TryParseInt(_modCap,     out int modCap,     nonNegative: false, fieldName: "Moderator cap")) return false;
            if (!TryParseInt(_adminCap,   out int adminCap,   nonNegative: false, fieldName: "Admin cap"))     return false;

            // -1 is the only allowed negative.
            if (memberCap  < -1 || officerCap < -1 || modCap < -1 || adminCap < -1)
            {
                Notifications.KmhNotifications.Rejected("Caps must be -1, 0, or positive");
                return false;
            }

            _draft.SiteRewardSilverTaxPercent = siteTax;
            _draft.MarketplaceSaleTaxPercent  = marketTax;
            _draft.MemberDailyWithdrawCap     = memberCap;
            _draft.OfficerDailyWithdrawCap    = officerCap;
            _draft.ModeratorDailyWithdrawCap  = modCap;
            _draft.AdminDailyWithdrawCap      = adminCap;
            return true;
        }

        private static bool TryParseInt(string s, out int value, bool nonNegative, string fieldName)
        {
            if (!int.TryParse((s ?? "").Trim(), out value))
            {
                Notifications.KmhNotifications.Rejected($"{fieldName}: enter a whole number");
                return false;
            }
            if (nonNegative && value < 0)
            {
                Notifications.KmhNotifications.Rejected($"{fieldName}: cannot be negative");
                return false;
            }
            return true;
        }
    }
}
