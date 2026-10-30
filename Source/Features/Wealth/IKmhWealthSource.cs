namespace KMHPatch.Features.Wealth
{
    // Off-map wealth counts so parked value cannot dodge raids. SilverValue() is polled: keep it cheap and side-effect-free.
    internal interface IKmhWealthSource
    {
        string Name { get; }
        float SilverValue();
    }
}
