using KMHPatch.Features.Seasons.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Seasons
{
    // Receives kmh.archive.season into SeasonArchiveCache + the request the Season Archive board sends on open.
    internal static class SeasonHandler
    {
        public static void Register() => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.SeasonArchive, OnSnapshot);

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.SeasonArchiveRequest, null);

        private static void OnSnapshot(KmhEnvelope env)
        {
            SeasonArchiveSnapshot snap = env?.DataAs<SeasonArchiveSnapshot>();
            if (snap != null) SeasonArchiveCache.Apply(snap);
        }
    }
}
