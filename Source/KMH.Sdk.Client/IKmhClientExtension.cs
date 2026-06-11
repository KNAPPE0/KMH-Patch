namespace KMH.Sdk.Client
{
    /// <summary>
    /// Implement this interface in your RimWorld mod's assembly and
    /// KMH-Patch will discover + instantiate it at game load.
    /// </summary>
    /// <remarks>
    /// Discovery rules:
    ///   - Your mod's About/About.xml should list KMHPatch as a
    ///     <c>&lt;modDependencies&gt;</c> entry and use
    ///     <c>&lt;loadAfter&gt;</c> to ensure the patch loads first.
    ///     Your assembly should reference KMH.Sdk.Client.dll, not
    ///     KMHPatch.dll directly.
    ///   - At KMH-Patch's mod-init time, every loaded RimWorld mod
    ///     is scanned for concrete public classes implementing this
    ///     interface with a parameterless constructor.
    ///   - Multiple extensions per mod assembly are allowed; each is
    ///     instantiated + registered independently.
    ///
    /// Lifecycle:
    ///   1. <see cref="Register"/> called once during KMH-Patch's
    ///      <c>Mod</c> constructor - after Harmony patches are
    ///      installed and KMH handlers are registered, but before any
    ///      RimWorld scene loads. Wire your event subscriptions,
    ///      register custom wire kinds, install your own Harmony
    ///      patches (using your own Harmony instance with an id
    ///      distinct from KMH's).
    ///   2. RimWorld runs normally; KMH fires events as snapshots
    ///      arrive from the server, the player links / unlinks their
    ///      Discord, etc.
    ///   3. <see cref="Shutdown"/> called when RimWorld quits cleanly
    ///      (best-effort - process kills don't call it).
    ///
    /// Failure isolation:
    ///   - Throwing from your ctor or from <see cref="Register"/> is
    ///     caught by the loader and logged; KMH continues without
    ///     your extension active.
    ///   - Throwing from a subscribed event handler is logged but does
    ///     not abort the event dispatch chain.
    /// </remarks>
    public interface IKmhClientExtension
    {
        /// <summary>
        /// Display name shown in logs and on the KMH tab's loaded-
        /// extensions list. Keep short + human-readable
        /// (e.g. "Bank Loans", "Auction House UI").
        /// </summary>
        string Name { get; }

        /// <summary>SemVer string. Surfaced in logs + diagnostic UI.</summary>
        string Version { get; }

        /// <summary>
        /// Called once at mod init. Subscribe to events, register
        /// custom wire kinds, install your own Harmony patches.
        /// </summary>
        void Register(IKmhClientHost host);

        /// <summary>
        /// Called on graceful game shutdown. Best-effort. Use for
        /// flushing local-state files etc.
        /// </summary>
        void Shutdown();
    }
}
