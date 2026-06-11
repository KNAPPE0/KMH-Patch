namespace KMHPatch.UI
{
    // Mirror of the server addon's KMHServerAddon.Util.SilverFmt. Keeps client-rendered silver values formatted
    // identically to anything the server prints - so chat lines / notifications / dialog labels all read the same
    // shape
    //
    // Format: leading "$" + comma-grouped digits. Negatives keep the sign inside the dollar marker ("-$50") for
    // legibility in transaction logs
    //
    // Bug-tracker driver: the legacy "100s" suffix was used in some dialogs and "$100" in others. Centralising the
    // format here means a global style change (currency symbol, separator, etc.) is a one-file edit instead of a
    // search-and-replace across every dialog
    internal static class SilverFmt
    {
        public static string Format(int amount)  => Format((long)amount);
        public static string Format(long amount) => amount < 0
            ? "-$" + (-amount).ToString("N0")
            :  "$" +   amount.ToString("N0");
    }
}
