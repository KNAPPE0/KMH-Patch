namespace KMHPatch.Dialogs
{
    internal static class ConnectedAmountCheck
    {
        internal static bool TryRead(string text, long max, long held, bool deposit, string notEnough, out long amount)
        {
            if (!long.TryParse((text ?? "").Trim(), out amount) || amount <= 0)
            {
                Notifications.KmhNotifications.Negative("Enter a whole number of silver above zero.");
                return false;
            }
            if (max > 0 && amount > max)
            {
                Notifications.KmhNotifications.Negative($"That is more than this server moves at once ({max:N0}).");
                return false;
            }
            // Checked here only to save a round trip; the server decides, and its answer is what is shown.
            if (deposit && amount > held)
            {
                Notifications.KmhNotifications.Negative(notEnough);
                return false;
            }
            return true;
        }
    }
}
