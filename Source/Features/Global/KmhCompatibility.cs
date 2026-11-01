using System;
using System.Collections.Generic;
using KMHPatch.UI;

namespace KMHPatch.Features.Global
{
    // Deliberately no "pending": a check finishes or names why it cannot, so no card sits indefinite.
    internal enum CompatState
    {
        Checking,
        Offline,
        Starting,
        Maintenance,
        // No button helps: the RimWorld version is the player's own Steam branch.
        GameVersionMismatch,
        ProtocolMismatch,
        RwtUnsupported,
        NoAddress,
        Full,
        ModsUnavailable,
        ModsAction,
        ModsNeedExpansion,
        ModsOptionalDiffer,
        Ready,
    }

    internal static class KmhCompatibility
    {
        // ModsUnavailable is not a refusal: RWT checks mods at the door, and blocking left Join dead while fetches failed.
        public static bool IsBlocking(CompatState s)
            => s != CompatState.Ready && s != CompatState.ModsOptionalDiffer && s != CompatState.ModsUnavailable;

        // The mod verdict on its own, so a version or address problem shown first cannot hide the mod-list fix.
        public static CompatState ModStateOf(ServerCardDto card) => card == null ? CompatState.Checking : ModState(card);

        public static CompatState Evaluate(ServerCardDto card)
        {
            if (card == null) return CompatState.Checking;

            if (card.Status == "Offline") return CompatState.Offline;
            if (card.Status == "Restarting" || card.Status == "Stale") return CompatState.Starting;
            // A server in maintenance (mid-wipe, shutting down, failed write) answers the browser but refuses work.
            if (card.Status == "Maintenance") return CompatState.Maintenance;

            // Two RimWorld versions are not known to share a world safely; a server naming no version is not refused.
            if (Compat.KmhGameVersion.CompareWithMine(card.RimWorldVersion) == Compat.GameMatch.Differs)
                return CompatState.GameVersionMismatch;

            if (card.Protocol > 0 && card.Protocol != SubProtocol.KmhProtocol.CurrentVersion)
                return CompatState.ProtocolMismatch;

            if (!KmhRwtVersions.Supports(card.RwtVersion)) return CompatState.RwtUnsupported;
            if (card.MaxPlayers > 0 && card.Players >= card.MaxPlayers) return CompatState.Full;
            if (string.IsNullOrEmpty(card.JoinEndpoint)) return CompatState.NoAddress;

            return ModState(card);
        }

        private static CompatState ModState(ServerCardDto card)
        {
            // Enforcing nothing is a real answer, not a missing one: no mod list is refused.
            if (ModPolicyNames.EnforcesNothing(card.ModPolicy)) return CompatState.Ready;

            ModProfileClient.Entry entry = ModProfileClient.Peek(card.ServerId);
            if (entry == null || entry.Pending) return CompatState.Checking;
            if (entry.Profile == null) return CompatState.ModsUnavailable;
            if (ModPolicyNames.EnforcesNothing(entry.Profile.Policy)) return CompatState.Ready;

            ModComparison c = entry.Comparison;
            if (c == null) return CompatState.Checking;
            if (c.Blocking) return c.BlockedOnlyByExpansions ? CompatState.ModsNeedExpansion : CompatState.ModsAction;
            return c.OptionalNotInstalled.Count > 0 ? CompatState.ModsOptionalDiffer : CompatState.Ready;
        }

        public static string Describe(ServerCardDto card)
        {
            switch (Evaluate(card))
            {
                case CompatState.Offline:            return KmhText.Get("KMH_Browser_Compat_Offline2", "Offline");
                case CompatState.Starting:           return KmhText.Get("KMH_Browser_Compat_Starting", "Not ready");
                case CompatState.Maintenance:        return KmhText.Get("KMH_Browser_Compat_Maintenance", "In maintenance");
                case CompatState.GameVersionMismatch:
                    return KmhText.Get("KMH_Browser_Compat_GameVersion", "Needs RimWorld {0}", Show(Compat.KmhGameVersion.MajorMinorOf(card.RimWorldVersion)));
                case CompatState.ProtocolMismatch:   return KmhText.Get("KMH_Browser_Compat_Protocol", "Colony Nexus version differs");
                case CompatState.RwtUnsupported:     return KmhText.Get("KMH_Browser_Compat_Rwt", "RimWorld Together differs");
                case CompatState.NoAddress:          return KmhText.Get("KMH_Browser_Compat_NoAddress", "No address published");
                case CompatState.Full:               return KmhText.Get("KMH_Browser_Compat_Full", "Server full");
                case CompatState.ModsUnavailable:    return KmhText.Get("KMH_Browser_Compat_ModsUnavailable", "Mod list unavailable");
                case CompatState.ModsAction:         return KmhText.Get("KMH_Browser_Compat_ModsAction", "Mods need changing");
                case CompatState.ModsNeedExpansion:  return KmhText.Get("KMH_Browser_Compat_ModsExpansion", "Needs a RimWorld expansion");
                case CompatState.ModsOptionalDiffer: return KmhText.Get("KMH_Browser_Compat_ModsOptional", "Ready - optional mods differ");
                case CompatState.Ready:              return KmhText.Get("KMH_Browser_Compat_Ready", "Ready to join");
                default:                             return KmhText.Get("KMH_Browser_Compat_Checking", "Checking…");
            }
        }

        public static string Explain(ServerCardDto card) => Explain(card, Evaluate(card));

        // Takes the caller's decided state so a mod list landing mid-click cannot make the shown reason disagree.
        public static string Explain(ServerCardDto card, CompatState state)
        {
            switch (state)
            {
                case CompatState.Offline:
                    return KmhText.Get("KMH_Browser_Why_Offline", "This server has stopped answering Colony Nexus Global.");
                case CompatState.Starting:
                    return KmhText.Get("KMH_Browser_Why_Starting", "This server is starting up or has not reported in recently. Try again shortly.");
                case CompatState.Maintenance:
                    return KmhText.Get("KMH_Browser_Why_Maintenance", "This server is in maintenance and is refusing play for now. Try again shortly.");
                case CompatState.GameVersionMismatch:
                    return KmhText.Get("KMH_Browser_Why_GameVersion",
                        "This server runs RimWorld {0} and you are running {1}. Players on different RimWorld versions cannot share a server yet, so it cannot be joined from this game. To play here, run RimWorld {0}: Steam chooses the version under RimWorld > Properties > Betas. Colony Nexus never changes that for you.",
                        Show(Compat.KmhGameVersion.MajorMinorOf(card.RimWorldVersion)), Show(Compat.KmhGameVersion.Full));
                case CompatState.ProtocolMismatch:
                    return KmhText.Get("KMH_Browser_Why_Protocol",
                        "This server runs a different version of Colony Nexus. Update Colony Nexus on whichever side is older: a server and a client must speak the same protocol.");
                case CompatState.RwtUnsupported:
                    string rwt = string.Format(KmhText.Get("KMH_Browser_Why_Rwt",
                        "This server runs RimWorld Together {0}; you are running {1}. Switch to the matching version to join."),
                        Show(card.RwtVersion), Show(KmhRwtVersions.Mine));
                    // Every blocker at once, so fixing the version does not just reveal the next one.
                    return ModState(card) == CompatState.ModsAction
                        ? rwt + " " + KmhText.Get("KMH_Browser_Why_RwtAndMods", "Your mod list also needs changing for this server; \"Prepare and join\" in the server browser does both.")
                        : rwt;
                case CompatState.NoAddress:
                    return KmhText.Get("KMH_Browser_Why_NoAddress",
                        "The owner has not published a connection address for this server, so it cannot be joined from the browser. Ask them for the address and use RimWorld Together's own connect screen.");
                case CompatState.Full:
                    return KmhText.Get("KMH_Browser_Why_Full", "Every player slot on this server is taken right now.");
                case CompatState.ModsUnavailable:
                    return KmhText.Get("KMH_Browser_Why_ModsUnavailable",
                        "This server has not published its mod list, so the comparison could not run. You can still join, and RimWorld Together will do its own check.");
                case CompatState.ModsAction:
                    return KmhText.Get("KMH_Browser_Why_ModsAction",
                        "Your mod list does not match what this server requires. \"Prepare and join\" in the server browser downloads what is missing, sets your mod list, restarts RimWorld and joins.");
                case CompatState.ModsNeedExpansion:
                    return KmhText.Get("KMH_Browser_Why_ModsExpansion",
                        "This server requires a RimWorld expansion you do not have. Expansions are bought from Steam and cannot be downloaded here. Ask the owner to make it optional if the server does not really need it.");
                case CompatState.ModsOptionalDiffer:
                    return KmhText.Get("KMH_Browser_Why_ModsOptional",
                        "You can join. Some mods this server allows are not in your list; that is optional, not a blocker.");
                case CompatState.Ready:
                    return KmhText.Get("KMH_Browser_Why_Ready", "Everything matches. Join is ready.");
                default:
                    return KmhText.Get("KMH_Browser_Why_Checking", "Checking this server's requirements…");
            }
        }

        public static List<string> Details(ServerCardDto card)
        {
            List<string> lines = new List<string>();
            if (card == null) return lines;

            ModProfileClient.Entry entry = ModProfileClient.Peek(card.ServerId);
            ModComparison c = entry?.Comparison;
            if (c == null) return lines;

            // Inert here, not at parse (names match local mod ids); keys stay literal for the source-scanning check.
            if (c.MissingWorkshop.Count + c.MissingExpansions.Count + c.MissingLocal.Count + c.MissingDisabled.Count == c.Missing.Count
                && c.Missing.Count > 0)
            {
                AddEntries(lines, c.MissingDisabled, KmhText.Get("KMH_Browser_Mod_Disabled", "Installed, switched off: {0}"));
                AddEntries(lines, c.MissingWorkshop, KmhText.Get("KMH_Browser_Mod_Subscribe", "Missing, can subscribe: {0}"));
                AddEntries(lines, c.MissingExpansions, KmhText.Get("KMH_Browser_Mod_Expansion", "Missing expansion: {0}"));
                AddEntries(lines, c.MissingLocal, KmhText.Get("KMH_Browser_Mod_Manual", "Missing, install by hand: {0}"));
            }
            else Add(lines, c.Missing, KmhText.Get("KMH_Browser_Mod_Missing", "Missing: {0}"));

            // The author tells apart two mods that share a display name.
            List<string> banned = new List<string>();
            foreach (string m in c.Disallowed)
            {
                string author = KmhLocalMods.AuthorOf(m);
                banned.Add(string.IsNullOrEmpty(author) ? m : m + " (" + author + ")");
            }
            Add(lines, banned, KmhText.Get("KMH_Browser_Mod_Disallowed", "Not allowed here: {0}"));
            Add(lines, c.OptionalNotInstalled, KmhText.Get("KMH_Browser_Mod_Optional", "Optional, not installed: {0}"));
            // Without this, an order-only mismatch says "Mods need changing" with nothing beneath it.
            if (c.OrderMismatch)
                lines.Add(KmhText.Get("KMH_Browser_Mod_Order", "Your load order differs from this server's."));
            foreach (ModShadow s in c.Shadowing)
                lines.Add(KmhText.Get("KMH_Browser_Mod_Shadow", "Two mods share one id: {0} (Workshop {1}) can load instead of {2}, which this server uses. \"Prepare and join\" unsubscribes it.",
                    UI.KmhDisplayText.Inert(s.Copy.Name, MaxModName), UI.KmhDisplayText.Inert(s.Copy.WorkshopId, 20), UI.KmhDisplayText.Inert(s.Server.Name, MaxModName)));
            return lines;
        }

        private const int MaxModLines = 25, MaxModName = 64;

        private static void AddEntries(List<string> lines, List<ModEntryDto> mods, string format)
        {
            if (mods == null || mods.Count == 0) return;
            List<string> names = new List<string>();
            foreach (ModEntryDto e in mods) names.Add(Label(e));
            Add(lines, names, format);
        }

        private static string Label(ModEntryDto e)
            => string.IsNullOrEmpty(e.Author) ? e.Name : e.Name + " (" + e.Author + ")";

        private static void Add(List<string> lines, List<string> mods, string format)
        {
            if (mods == null) return;
            int shown = 0;
            foreach (string m in mods)
            {
                if (shown++ == MaxModLines)
                {
                    lines.Add(string.Format(KmhText.Get("KMH_Browser_Mod_More", "…and {0} more"),
                                            mods.Count - MaxModLines));
                    return;
                }
                lines.Add(string.Format(format, UI.KmhDisplayText.Inert(m, MaxModName)));
            }
        }

        public static string ModsLabel(ServerCardDto card)
        {
            if (card == null) return "";
            if (card.ModPolicy == ModPolicyNames.AllowAll)
                return KmhText.Get("KMH_Browser_Mods_Optional", "Any mods allowed");
            if (card.ModPolicy == ModPolicyNames.NotSet)
                return KmhText.Get("KMH_Browser_Mods_NotSet", "No mod list set yet");

            ModProfileClient.Entry entry = ModProfileClient.Peek(card.ServerId);
            if (entry != null && entry.Pending) return KmhText.Get("KMH_Browser_Mods_Checking", "Comparing…");
            if (entry != null && entry.Profile == null)
                return KmhText.Get("KMH_Browser_Mods_Unpublished", "Not published by this server");

            if (card.ModPolicy != ModPolicyNames.Enforced)
                return KmhText.Get("KMH_Browser_Mods_Unpublished", "Not published by this server");

            ModComparison c = entry?.Comparison;
            if (c == null) return KmhText.Get("KMH_Browser_Mods_Checking", "Comparing…");
            if (c.Blocking && c.Missing.Count + c.Disallowed.Count == 0)
                return KmhText.Get("KMH_Browser_Mods_OrderOnly", "Load order differs");
            if (c.Blocking)
                return string.Format(KmhText.Get("KMH_Browser_Mods_Mismatch", "{0} to fix, of {1} enforced"),
                    c.Missing.Count + c.Disallowed.Count, card.ModCount);
            return string.Format(KmhText.Get("KMH_Browser_Mods_Match", "Matches ({0} enforced)"), card.ModCount);
        }

        public static string StatusLabel(string status)
        {
            switch (status)
            {
                case "Online":      return KmhText.Get("KMH_Browser_Status_Online", "Online");
                case "Degraded":    return KmhText.Get("KMH_Browser_Status_Degraded", "Degraded");
                case "Maintenance": return KmhText.Get("KMH_Browser_Status_Maintenance", "Maintenance");
                case "Restarting":  return KmhText.Get("KMH_Browser_Status_Restarting", "Restarting");
                case "Stale":       return KmhText.Get("KMH_Browser_Status_Stale", "Not responding");
                case "Offline":     return KmhText.Get("KMH_Browser_Status_Offline", "Offline");
                default:            return status ?? "";
            }
        }

        private static string Show(string s) => string.IsNullOrEmpty(s) ? "?" : s;
    }
}
