using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Global
{
    internal static class ModPolicyNames
    {
        public const string Unknown  = "Unknown";
        public const string AllowAll = "AllowAll";
        // Nothing published yet: RWT enforces no mod list until a world exists and one has been sent to it.
        public const string NotSet   = "NotSet";
        public const string Enforced = "Enforced";

        public static bool EnforcesNothing(string policy) => policy == AllowAll || policy == NotSet;
    }

    internal sealed class ModProfileDto
    {
        public string ServerId = "";
        public string Policy = ModPolicyNames.Unknown;
        public bool   EnforceSettings;
        public bool   EnforceOrder;
        // Anything run that is on neither list is refused, as RWT does, so "not allowed" is derived, never stored.
        public List<string> Required = new List<string>();
        public List<string> Optional = new List<string>();
        // Package ids from RWT's FL_ModOrder (ModsConfig.xml); often empty, since many RWT servers publish no order.
        public List<string> Order = new List<string>();
        // RWT's SHA-256 of the document the order came from, so an applied ModsConfig.xml can be proved correct.
        public string OrderHash = "";
        public string Hash = "";
        public long   Revision;
        // Empty on older servers, which is why the name lists above remain what is compared.
        public List<ModEntryDto> Entries = new List<ModEntryDto>();

        public ModEntryDto EntryFor(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (ModEntryDto e in Entries)
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        // Global publishes at most 512; this only bounds what a broken or hostile answer can make the client compare.
        internal const int MaxListed = 2048;

        public static ModProfileDto Parse(JObject body)
        {
            if (body == null) return null;
            string id = (string)body["server_id"];
            if (string.IsNullOrEmpty(id)) return null;
            foreach (string field in new[] { "order", "required", "optional", "entries" })
                if (body[field] is JArray a && a.Count > MaxListed) return null;

            return new ModProfileDto
            {
                ServerId        = id,
                Policy          = (string)body["policy"] ?? ModPolicyNames.Unknown,
                EnforceSettings = (bool?)body["enforce_settings"] ?? false,
                EnforceOrder    = (bool?)body["enforce_order"] ?? false,
                Order           = Strings(body["order"]),
                OrderHash       = (string)body["order_hash"] ?? "",
                Required        = Strings(body["required"]),
                Optional        = Strings(body["optional"]),
                Hash            = (string)body["hash"] ?? "",
                Revision        = (long?)body["revision"] ?? 0,
                Entries         = ModEntryDto.ParseAll(body["entries"]),
            };
        }

        private static List<string> Strings(JToken token)
        {
            List<string> list = new List<string>();
            if (token is JArray arr)
                foreach (JToken t in arr)
                {
                    string s = (string)t;
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
            return list;
        }
    }

    internal sealed class ModShadow
    {
        public Mods.InstalledMod Copy;
        public ModEntryDto Server;
    }

    // Missing and Disallowed mirror the only two things RWT itself refuses a join over.
    internal sealed class ModComparison
    {
        public List<string> Missing = new List<string>();
        public List<string> Disallowed = new List<string>();
        public List<string> OptionalNotInstalled = new List<string>();
        public bool OrderMismatch;

        // Empty when the server publishes no entries; Missing is then all that is known.
        public List<ModEntryDto> MissingWorkshop = new List<ModEntryDto>();
        public List<ModEntryDto> MissingExpansions = new List<ModEntryDto>();
        public List<ModEntryDto> MissingLocal = new List<ModEntryDto>();
        public List<ModEntryDto> MissingDisabled = new List<ModEntryDto>();
        public List<ModEntryDto> OptionalWorkshop = new List<ModEntryDto>();
        // Another Workshop item with a server mod's packageId: RimWorld loads either, usually the older, so it has to go.
        public List<ModShadow> Shadowing = new List<ModShadow>();

        public bool Blocking => Missing.Count > 0 || Disallowed.Count > 0 || OrderMismatch;

        public bool BlockedOnlyByExpansions
            => Missing.Count > 0 && Disallowed.Count == 0 && !OrderMismatch
               && MissingExpansions.Count == Missing.Count;

        // Blockers only: optional mods get their own button so a player never downloads content they did not choose.
        public List<ModEntryDto> Subscribable() => SubscribableIn(MissingWorkshop);

        public List<ModEntryDto> OptionalSubscribable() => SubscribableIn(OptionalWorkshop);

        private static List<ModEntryDto> SubscribableIn(List<ModEntryDto> entries)
        {
            List<ModEntryDto> list = new List<ModEntryDto>();
            foreach (ModEntryDto e in entries) if (e.Subscribable) list.Add(e);
            return list;
        }

        public static ModComparison Compare(ModProfileDto profile, IList<string> activeModNames,
                                            IList<Mods.InstalledMod> installed = null)
        {
            ModComparison c = new ModComparison();
            if (profile == null || ModPolicyNames.EnforcesNothing(profile.Policy)) return c;
            installed = installed ?? KmhLocalMods.InstalledCached();
            activeModNames = AsServerNames(profile, activeModNames, installed);

            HashSet<string> mine = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (activeModNames != null) foreach (string m in activeModNames) if (!string.IsNullOrEmpty(m)) mine.Add(m);

            HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string m in profile.Required) { allowed.Add(m); if (!mine.Contains(m)) c.Missing.Add(m); }
            foreach (string m in profile.Optional) { allowed.Add(m); if (!mine.Contains(m)) c.OptionalNotInstalled.Add(m); }

            // RWT's only rule: anything run that is neither required nor optional blocks the join.
            if (activeModNames != null)
                foreach (string m in activeModNames)
                    if (!string.IsNullOrEmpty(m) && !allowed.Contains(m) && !c.Disallowed.Contains(m))
                        c.Disallowed.Add(m);

            c.OrderMismatch = OrderDiffers(profile, KmhLocalMods.ActivePackageIds());
            Classify(c, profile, installed);
            return c;
        }

        // An unlisted mod whose Workshop id matches exactly one entry is that entry, so a renamed mod is not flagged.
        internal static IList<string> AsServerNames(ModProfileDto profile, IList<string> active, IList<Mods.InstalledMod> installed)
        {
            if (active == null || installed == null || profile.Entries.Count == 0) return active;
            HashSet<string> listed = new HashSet<string>(profile.Required, StringComparer.OrdinalIgnoreCase);
            listed.UnionWith(profile.Optional);
            List<string> names = new List<string>(active);
            for (int i = 0; i < names.Count; i++)
            {
                if (string.IsNullOrEmpty(names[i]) || listed.Contains(names[i])) continue;
                foreach (Mods.InstalledMod m in installed)
                {
                    if (m == null || !Mods.KmhWorkshop.IsWorkshopId(m.WorkshopId) || !string.Equals(m.Name, names[i], StringComparison.OrdinalIgnoreCase)) continue;
                    ModEntryDto entry = OnlyEntryWithId(profile, m.WorkshopId);
                    // Also stops a second copy of one id taking a name the first already took.
                    if (entry == null || names.Exists(n => string.Equals(n, entry.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    names[i] = entry.Name;
                    break;
                }
            }
            return names;
        }

        private static ModEntryDto OnlyEntryWithId(ModProfileDto profile, string workshopId)
        {
            ModEntryDto found = null;
            foreach (ModEntryDto e in profile.Entries)
            {
                if (e == null || !string.Equals((e.Id ?? "").Trim(), workshopId, StringComparison.Ordinal)) continue;
                if (found != null) return null;
                found = e;
            }
            return found != null && !string.IsNullOrEmpty(found.Name) ? found : null;
        }

        // Names with no entry fall to MissingLocal: a Subscribe button for them would download nothing.
        private static void Classify(ModComparison c, ModProfileDto profile, IList<Mods.InstalledMod> installed)
        {
            if (profile.Entries.Count == 0) return;
            FindShadowing(c, profile, installed);

            foreach (string name in c.Missing)
            {
                ModEntryDto e = profile.EntryFor(name) ?? new ModEntryDto { Name = name };
                // An owned expansion that is switched off is a mod-list fix, not a purchase.
                if (IsInstalled(e, installed)) c.MissingDisabled.Add(e);
                else if (e.Kind == ModKindNames.Expansion) c.MissingExpansions.Add(e);
                else if (e.Subscribable) c.MissingWorkshop.Add(e);
                else c.MissingLocal.Add(e);
            }

            foreach (string name in c.OptionalNotInstalled)
            {
                ModEntryDto e = profile.EntryFor(name);
                if (e != null && e.Subscribable && !IsInstalled(e, installed)) c.OptionalWorkshop.Add(e);
            }
        }

        // An optional mod's other copy only matters while it runs; a required mod's matters as soon as it is installed.
        private static void FindShadowing(ModComparison c, ModProfileDto profile, IList<Mods.InstalledMod> installed)
        {
            if (installed == null) return;
            HashSet<string> required = new HashSet<string>(profile.Required, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<Mods.InstalledMod>> byWorkshop = Mods.KmhModPlan.ByWorkshopId(installed);
            foreach (ModEntryDto e in profile.Entries)
            {
                if (e == null || !e.Subscribable) continue;
                // RWT servers publish no packageIds, so the server's own copy names it once it is on disk, listed or not.
                string package = !string.IsNullOrEmpty(e.PackageId) ? e.PackageId
                    : Mods.KmhModPlan.ByWorkshopId(e.Id, byWorkshop)?.PackageId ?? KmhLocalMods.PackageIdOnDisk(e.Id);
                if (string.IsNullOrEmpty(package)) continue;
                List<Mods.InstalledMod> copies = new List<Mods.InstalledMod>();
                foreach (Mods.InstalledMod m in installed) if (m != null && SamePackage(m, package)) copies.Add(m);
                // A hand-installed copy is the one RimWorld loads, whatever Steam also holds.
                if (copies.Exists(m => !Mods.KmhWorkshop.IsWorkshopId(m.WorkshopId))) continue;
                foreach (Mods.InstalledMod m in copies)
                    if (!SameItem(m, e) && (required.Contains(e.Name) || m.Active))
                        c.Shadowing.Add(new ModShadow { Copy = m, Server = e });
            }
        }

        // Must resolve as the mod plan does: packageId, else a unique Workshop id, then name.
        internal static bool IsInstalled(ModEntryDto e, IList<Mods.InstalledMod> installed)
        {
            if (e == null || installed == null) return false;
            if (string.IsNullOrEmpty(e.PackageId) && Mods.KmhModPlan.ByWorkshopId(e.Id, Mods.KmhModPlan.ByWorkshopId(installed)) != null) return true;
            foreach (Mods.InstalledMod m in installed)
            {
                if (m == null) continue;
                if (!string.IsNullOrEmpty(e.PackageId))
                {
                    // Another Workshop item sharing the packageId is a different mod (RoM's 1.6 original beside its continuation).
                    if (SamePackage(m, e.PackageId) && (!e.Subscribable || !Mods.KmhWorkshop.IsWorkshopId(m.WorkshopId) || SameItem(m, e))) return true;
                }
                else if (!string.IsNullOrEmpty(e.Name) && string.Equals(m.Name, e.Name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool SamePackage(Mods.InstalledMod m, string packageId)
            => string.Equals(Mods.KmhModPlan.Bare(m.PackageId), Mods.KmhModPlan.Bare(packageId), StringComparison.OrdinalIgnoreCase);

        private static bool SameItem(Mods.InstalledMod m, ModEntryDto e)
            => string.Equals((m.WorkshopId ?? "").Trim(), (e.Id ?? "").Trim(), StringComparison.Ordinal);

        // KMH's check, not RWT's (stock RWT has no load-order field); only mods the server names are compared.
        internal static bool OrderDiffers(ModProfileDto profile, IList<string> activePackageIds)
        {
            if (profile == null || profile.Order.Count == 0 || activePackageIds == null) return false;

            HashSet<string> named = new HashSet<string>(profile.Order, StringComparer.OrdinalIgnoreCase);
            List<string> mineInOrder = new List<string>();
            foreach (string m in activePackageIds)
                if (!string.IsNullOrEmpty(m) && named.Contains(m)) mineInOrder.Add(m);

            // Absent mods are Missing/Optional findings, so only mods the player has are ordered against the server.
            List<string> wanted = new List<string>();
            HashSet<string> have = new HashSet<string>(mineInOrder, StringComparer.OrdinalIgnoreCase);
            foreach (string m in profile.Order)
                if (have.Contains(m) && !wanted.Contains(m)) wanted.Add(m);

            if (wanted.Count != mineInOrder.Count) return true;
            for (int i = 0; i < wanted.Count; i++)
                if (!string.Equals(wanted[i], mineInOrder[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
