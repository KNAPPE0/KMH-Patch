using KMHPatch.Features.Account;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    public class Dialog_KMHLinkSteam : Window_KMHBase
    {
        protected override bool ClosesOnSessionEnd => false;

        public override Vector2 InitialSize => DialogLayout.FitToScreen(460f, 300f);

        public Dialog_KMHLinkSteam()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        protected override void DrawContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label(KmhText.Get("KMH_Steam_KmhAccount", "Colony Nexus Account"));
            Text.Font = GameFont.Small;

            listing.Gap(8f);
            listing.Label(KmhText.Get("KMH_Steam_LinkYourSteamAccount", "Link your Steam account to Colony Nexus to keep one verified identity across supported Colony Nexus servers."));
            listing.Gap(4f);
            listing.Label(KmhText.Get("KMH_Steam_YouCanDoThis", "<color=#9AA4B2>You can do this later from Account & links.</color>"));

            listing.Gap(14f);
            if (listing.ButtonText(KmhText.Get("KMH_Steam_LinkSteam", "Link Steam")))
            {
                KmhAccountPrompt.SuppressForSession();
                Close();
                Dialog_KMHAccount.Open(startLink: true);
            }

            listing.Gap(6f);
            if (listing.ButtonText(KmhText.Get("KMH_Steam_NotNow", "Not now")))
            {
                KmhAccountPrompt.SuppressForSession();
                Close();
            }

            listing.Gap(6f);
            if (listing.ButtonText(KmhText.Get("KMH_Steam_DonTAskAgain", "Don't ask again")))
            {
                KmhAccountPrompt.SuppressForever();
                Close();
            }

            listing.End();
        }

        public override void PostClose()
        {
            base.PostClose();
            KmhAccountPrompt.SuppressForSession();
        }
    }
}
