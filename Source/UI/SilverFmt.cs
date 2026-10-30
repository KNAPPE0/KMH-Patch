namespace KMHPatch.UI
{
    internal static class SilverFmt
    {
        public static string Format(int amount)  => Format((long)amount);
        public static string Format(long amount) => amount < 0
            ? "-$" + (-amount).ToString("N0")
            :  "$" +   amount.ToString("N0");

        // A milli-silver price: whole silver as Format shows it, a fraction to at most three places.
        public static string FormatMilli(long milli) => milli % 1000 == 0 ? Format(milli / 1000)
            : (milli < 0 ? "-$" : "$") + (System.Math.Abs(milli) / 1000m).ToString("#,0.###");
    }
}
