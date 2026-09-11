namespace KMHPatch.Features.Wealth
{
    // One system's share of the LOCAL player's off-map wealth, so parked value cannot dodge raids. SilverValue() is polled - keep it cheap and side-effect-free.
    internal interface IKmhWealthSource
    {
        string Name { get; }        // short label for the wealth breakdown / logs, e.g. "Treasury"
        float SilverValue();        // current silver-equivalent wealth this system holds for the local player
    }
}
