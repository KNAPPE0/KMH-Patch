using System;
using System.Runtime.InteropServices;

namespace KMHPatch.Features.Rwt
{
    internal enum RwtPlanState
    {
        Ready,
        AlreadyInstalled,
        VersionUnknown,
        NoAssetForPlatform,
        CatalogUnavailable,
        NoVersionRequested,
    }

    internal sealed class RwtInstallPlan
    {
        public RwtPlanState State = RwtPlanState.CatalogUnavailable;
        public string RequestedVersion = "";
        public RwtRelease Release;
        public RwtAsset Asset;
        public bool PendingRestart;

        public bool CanInstall => State == RwtPlanState.Ready;

        // GitHub publishes a SHA-256 digest for newer release assets; an older one may have none.
        public bool Verifiable => Asset != null && Asset.Sha256.Length == 64;

        // A server names the version, so a pre-release or a release past the recent ones needs the player's own yes.
        public bool Unusual => Release != null && (!Release.Stable || RwtReleaseCatalog.IsOld(Release));
        public bool NeedsOptIn => !Verifiable || Unusual;

        public string UnusualText()
        {
            if (Release == null) return "";
            string when = Release.PublishedAt == default ? "?" : Release.PublishedAt.UtcDateTime.ToString("yyyy-MM-dd");
            return !Release.Stable
                ? KMHPatch.UI.KmhText.Get("KMH_Prep_RwtPrerelease", "RimWorld Together {0} is marked as a pre-release (published {1}).", Release.Version, when)
                : KMHPatch.UI.KmhText.Get("KMH_Prep_RwtOld", "RimWorld Together {0} is not one of its {2} newest releases (published {1}).", Release.Version, when, RwtReleaseCatalog.RecentStable);
        }

        public string Explain()
        {
            switch (State)
            {
                case RwtPlanState.Ready:
                    return $"RimWorld Together {Release.Version} ({Asset.Name})";
                case RwtPlanState.AlreadyInstalled:
                    return PendingRestart
                        ? $"RimWorld Together {RequestedVersion} is already downloaded - restart RimWorld to load it."
                        : $"RimWorld Together {RequestedVersion} is already the version you are running.";
                case RwtPlanState.VersionUnknown:
                    return $"No official RimWorld Together release is published as {RequestedVersion}.";
                case RwtPlanState.NoAssetForPlatform:
                    return $"Release {RequestedVersion} publishes no client package for this platform.";
                case RwtPlanState.NoVersionRequested:
                    return "This server has not published which RimWorld Together version it runs.";
                default:
                    return "The official release list could not be reached, so no version can be resolved right now.";
            }
        }
    }

    // The requested version is never widened to the latest release: the point is to match the server exactly.
    internal static class RwtResolver
    {
        public static RwtOs CurrentOs
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return RwtOs.Windows;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return RwtOs.Linux;
                return RwtOs.Any;
            }
        }

        public static RwtArch CurrentArch
        {
            get
            {
                switch (RuntimeInformation.OSArchitecture)
                {
                    case Architecture.X86:   return RwtArch.X86;
                    case Architecture.X64:   return RwtArch.X64;
                    case Architecture.Arm:   return RwtArch.Arm;
                    case Architecture.Arm64: return RwtArch.Arm64;
                    default: return RwtArch.Any;
                }
            }
        }

        // Takes RwtVersion, not string: a display name cannot be parsed into one, so it cannot reach the resolver.
        public static RwtInstallPlan PlanClient(RwtVersion requestedVersion, RwtVersion installedVersion)
            => Plan(requestedVersion.Value, installedVersion.Value, RwtAssetRole.Client, RwtOs.Any, RwtArch.Any);

        public static RwtInstallPlan PlanServer(string requestedVersion, RwtOs os, RwtArch arch)
            => Plan(requestedVersion, "", RwtAssetRole.Server, os, arch);

        public static RwtInstallPlan Plan(string requestedVersion, string installedVersion,
                                          RwtAssetRole role, RwtOs os, RwtArch arch)
        {
            RwtInstallPlan plan = new RwtInstallPlan
            {
                RequestedVersion = RwtVersionText.Normalize(requestedVersion),
            };

            if (plan.RequestedVersion.Length == 0)
            {
                plan.State = RwtPlanState.NoVersionRequested;
                return plan;
            }

            // Activation applies on restart, so the staged version counts too, or every click re-downloads the same release.
            if (RwtVersionText.Same(plan.RequestedVersion, installedVersion)
                || RwtVersionText.Same(plan.RequestedVersion, RwtInstaller.ActivatedVersion))
            {
                plan.State = RwtPlanState.AlreadyInstalled;
                plan.PendingRestart = !RwtVersionText.Same(plan.RequestedVersion, installedVersion);
                return plan;
            }

            RwtRelease release = RwtReleaseCatalog.Find(plan.RequestedVersion);
            if (release == null)
            {
                plan.State = RwtReleaseCatalog.State == RwtReleaseCatalog.CatalogState.Ready
                    ? RwtPlanState.VersionUnknown
                    : RwtPlanState.CatalogUnavailable;
                return plan;
            }

            plan.Release = release;
            plan.Asset = release.Find(role, os, arch);
            plan.State = plan.Asset == null ? RwtPlanState.NoAssetForPlatform : RwtPlanState.Ready;
            return plan;
        }
    }
}
