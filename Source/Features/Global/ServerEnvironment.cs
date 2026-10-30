using System;

namespace KMHPatch.Features.Global
{
    // Global has no environment column, so test servers self-tag; untagged is Community, never hidden as Test.
    internal static class ServerEnvironment
    {
        public const string TestTag = "test";

        public static bool IsTest(ServerCardDto card)
        {
            if (card?.Tags == null) return false;
            foreach (string t in card.Tags)
                if (string.Equals(t, TestTag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string Label(ServerCardDto card)
        {
            if (card == null) return "";
            if (card.Official) return UI.KmhText.Get("KMH_Browser_Env_Official", "Official");
            return IsTest(card)
                ? UI.KmhText.Get("KMH_Browser_Env_Test", "Test")
                : UI.KmhText.Get("KMH_Browser_Env_Community", "Community");
        }
    }
}
