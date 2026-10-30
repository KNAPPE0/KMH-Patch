namespace KMH.Sdk.Client
{
    /// <summary>A client extension, found in any loaded mod and created through a public parameterless constructor.</summary>
    /// <remarks>Reference KMH.Sdk.Client.dll; list Colony Nexus (knappe.kmh.patch) in modDependencies and loadAfter.
    /// A throwing constructor or Register disables only that extension; a throwing handler is logged, the rest still run.</remarks>
    public interface IKmhClientExtension
    {
        /// <summary>Short human-readable name used in log lines, e.g. "Bank Loans".</summary>
        string Name { get; }

        /// <summary>SemVer string, shown in the log.</summary>
        string Version { get; }

        /// <summary>Called once from Colony Nexus's Mod constructor; subscribe, register wire kinds, patch here.</summary>
        void Register(IKmhClientHost host);

        /// <summary>Called on a graceful quit only; a killed process never calls it.</summary>
        void Shutdown();
    }
}
