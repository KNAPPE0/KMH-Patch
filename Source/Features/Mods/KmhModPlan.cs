using System;
using System.Collections.Generic;

namespace KMHPatch.Features.Mods
{
    // Decoupled from ModMetaData so the planning rules run without a game.
    internal sealed class InstalledMod
    {
        public string Name = "";
        public string PackageId = "";
        // RimWorld Together's ModID for this mod: its folder name, which for a Workshop mod is the Workshop id.
        public string WorkshopId = "";
        public bool Active;
        public bool Official;
    }

    // Client-side: only the client knows installed packageIds. Refused wherever it would be a guess, because a
    // wrong ModsConfig.xml leaves a game that will not start.
    internal sealed class KmhModPlan
    {
        public List<string> Order = new List<string>();
        public List<string> WillEnable = new List<string>();
        // RimWorld Together refuses a client carrying any of these.
        public List<string> WillDisable = new List<string>();

        public bool Ok;
        public string Blocked = "";
        public List<string> NotInstalled = new List<string>();

        public bool Changes => WillEnable.Count > 0 || WillDisable.Count > 0 || Reordered;
        public bool Reordered;

        // activeOrder is the live load order: RimWorld lists installed mods in its own order, which always differs.
        public static KmhModPlan Build(Global.ModProfileDto profile, IList<InstalledMod> installed,
                                        IList<string> activeOrder, ICollection<string> optIn = null)
        {
            KmhModPlan plan = new KmhModPlan();
            if (profile == null || installed == null)
            {
                plan.Blocked = "no mod list to apply";
                return plan;
            }
            if (profile.Entries.Count == 0)
            {
                // Names alone cannot give a load order; guessing a packageId from a display name enables the wrong mod.
                plan.Blocked = "this server has not published enough detail about its mods to build a list";
                return plan;
            }

            Dictionary<string, List<InstalledMod>> byName = ByName(installed);
            Dictionary<string, List<InstalledMod>> byWorkshop = ByWorkshopId(installed);

            HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> order = new List<string>();

            // The plan is the server's allowed set; anything else stays out of the written list, which disables it.
            foreach (Global.ModEntryDto e in profile.Entries)
            {
                InstalledMod match = Resolve(e, byName, byWorkshop, out string ambiguity);
                if (ambiguity != null) { plan.Blocked = ambiguity; return plan; }

                if (match == null)
                {
                    if (e.Role == Global.ModRoleNames.Required) plan.NotInstalled.Add(e.Name);
                    continue;
                }
                // An optional mod is switched on only by the player, never by applying a server's list.
                if (e.Role != Global.ModRoleNames.Required && !match.Active && !OptedIn(e, optIn)) continue;

                if (wanted.Add(match.PackageId))
                {
                    order.Add(match.PackageId);
                    if (!match.Active) plan.WillEnable.Add(match.Name);
                }
            }

            if (plan.NotInstalled.Count > 0)
            {
                plan.Blocked = "some mods this server requires are not installed yet";
                return plan;
            }
            if (order.Count == 0)
            {
                plan.Blocked = "none of this server's mods are installed here";
                return plan;
            }

            // RWT's entry order is not a load order (a patch before its mod breaks the game): sequence comes from the
            // server's published order, else this player's existing working order.
            order = Sequence(order, profile.Order, activeOrder);

            string refused = Refusal(order);
            if (refused != null)
            {
                plan.Blocked = refused;
                return plan;
            }
            // Core leads whatever order the server sent.
            PromoteCore(order);

            foreach (InstalledMod m in installed)
                if (m != null && m.Active && !string.IsNullOrEmpty(m.PackageId) && !wanted.Contains(m.PackageId))
                    plan.WillDisable.Add(m.Name);

            plan.Reordered = !SameOrder(activeOrder ?? new List<string>(), order);
            plan.Order = order;
            plan.Ok = true;
            return plan;
        }

        // The key the player ticked, exactly: a server-supplied name or packageId must not stand in for another item.
        internal static string OptInKey(Global.ModEntryDto e) => string.IsNullOrEmpty(e?.Id) ? e?.Name ?? "" : e.Id;

        private static bool OptedIn(Global.ModEntryDto e, ICollection<string> optIn)
        {
            if (optIn == null || optIn.Count == 0) return false;
            string key = OptInKey(e);
            if (key.Length == 0) return false;
            foreach (string o in optIn)
                if (string.Equals(o, key, string.IsNullOrEmpty(e.Id) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
            return false;
        }

        // Enforcement's published packageIds, held to Build's rules; its sequence is kept because that server verifies it.
        public static KmhModPlan ForOrder(IList<string> published, IList<InstalledMod> installed)
        {
            KmhModPlan plan = new KmhModPlan();
            if (published == null || published.Count == 0 || installed == null)
            {
                plan.Blocked = "no mod list to apply";
                return plan;
            }

            Dictionary<string, List<InstalledMod>> byName = ByName(installed);
            List<string> order = new List<string>();
            foreach (string id in published)
            {
                // Absent is the optional case, as in Build: something the host has and this player does not.
                InstalledMod match = ByPackageId(id, byName);
                if (match == null || Has(order, match.PackageId)) continue;
                order.Add(match.PackageId);
                if (!match.Active) plan.WillEnable.Add(match.Name);
            }

            string refused = Refusal(order);
            if (refused != null)
            {
                plan.Blocked = refused;
                return plan;
            }

            foreach (InstalledMod m in installed)
                if (m != null && m.Active && !string.IsNullOrEmpty(m.PackageId) && !Has(order, m.PackageId))
                    plan.WillDisable.Add(m.Name);

            plan.Order = order;
            plan.Ok = true;
            return plan;
        }

        // Without any of these the result cannot be undone from inside the game.
        private static string Refusal(List<string> order)
        {
            if (!Has(order, CoreId)) return "this server's mod list does not include RimWorld's Core";
            if (!Has(order, RwtId))
                return "this server's mod list does not include RimWorld Together, "
                     + "so applying it would leave you unable to connect to anything";
            if (!Has(order, KmhId))
                return "this server's mod list does not include Colony Nexus, "
                     + "so applying it would turn off the browser you are reading this in";
            // What those three need to load at all, read from their own About.xml rather than kept by hand here.
            foreach (string dep in DependenciesOfProtected())
                if (!Has(order, dep))
                    return $"this server's mod list leaves out {dep}, which Colony Nexus or RimWorld Together needs to load, "
                         + "so applying it would leave you unable to undo it from inside the game";
            return null;
        }

        internal static Func<IEnumerable<string>> DependenciesForTest;

        private static IEnumerable<string> DependenciesOfProtected()
            => DependenciesForTest != null ? DependenciesForTest() : Global.KmhLocalMods.DependenciesOf(CoreId, RwtId, KmhId);

        private static Dictionary<string, List<InstalledMod>> ByName(IList<InstalledMod> installed)
        {
            Dictionary<string, List<InstalledMod>> byName =
                new Dictionary<string, List<InstalledMod>>(StringComparer.OrdinalIgnoreCase);
            foreach (InstalledMod m in installed)
            {
                if (m == null || string.IsNullOrEmpty(m.Name) || string.IsNullOrEmpty(m.PackageId)) continue;
                if (!byName.TryGetValue(m.Name, out List<InstalledMod> list))
                    byName[m.Name] = list = new List<InstalledMod>();
                list.Add(m);
            }
            return byName;
        }

        internal static Dictionary<string, List<InstalledMod>> ByWorkshopId(IList<InstalledMod> installed)
        {
            var byId = new Dictionary<string, List<InstalledMod>>(StringComparer.Ordinal);
            foreach (InstalledMod m in installed ?? new List<InstalledMod>())
            {
                if (m == null || string.IsNullOrEmpty(m.PackageId) || !KmhWorkshop.IsWorkshopId(m.WorkshopId)) continue;
                if (!byId.TryGetValue(m.WorkshopId, out List<InstalledMod> list)) byId[m.WorkshopId] = list = new List<InstalledMod>();
                list.Add(m);
            }
            return byId;
        }

        // With both a local and a Workshop copy, the one running now wins, as in ByPackageId.
        internal static InstalledMod ByWorkshopId(string id, Dictionary<string, List<InstalledMod>> byWorkshop)
        {
            string key = (id ?? "").Trim();
            if (!KmhWorkshop.IsWorkshopId(key) || byWorkshop == null || !byWorkshop.TryGetValue(key, out List<InstalledMod> same)) return null;
            if (same.Count == 1) return same[0];
            List<InstalledMod> running = same.FindAll(m => m.Active);
            return running.Count == 1 ? running[0] : null;
        }

        // RimWorld renames the Workshop copy of a mod it also finds locally to "<id>_steam"; both are this id.
        internal const string SteamDuplicateSuffix = "_steam";

        // Returns only a mod on this machine, so its packageId (never the wire's text) is written. The running copy
        // beats its local/Workshop twin: switching to an old local snapshot breaks a working game.
        private static InstalledMod ByPackageId(string id, Dictionary<string, List<InstalledMod>> byName)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string bare = Bare(id);
            InstalledMod exact = null, active = null;
            foreach (List<InstalledMod> list in byName.Values)
                foreach (InstalledMod m in list)
                {
                    if (!IsCopyOf(m.PackageId, bare)) continue;
                    if (m.Active && active == null) active = m;
                    if (string.Equals(m.PackageId, id, StringComparison.OrdinalIgnoreCase) && exact == null) exact = m;
                }
            return active ?? exact ?? FirstOf(bare, byName);
        }

        private static InstalledMod FirstOf(string bare, Dictionary<string, List<InstalledMod>> byName)
        {
            foreach (List<InstalledMod> list in byName.Values)
                foreach (InstalledMod m in list)
                    if (IsCopyOf(m.PackageId, bare)) return m;
            return null;
        }

        private static bool IsCopyOf(string packageId, string bare)
            => string.Equals(packageId, bare, StringComparison.OrdinalIgnoreCase)
            || string.Equals(packageId, bare + SteamDuplicateSuffix, StringComparison.OrdinalIgnoreCase);

        // Exact packageId (a name-only match is then another mod), else a unique Workshop id, else a unique name.
        private static InstalledMod Resolve(Global.ModEntryDto e, Dictionary<string, List<InstalledMod>> byName,
                                             Dictionary<string, List<InstalledMod>> byWorkshop, out string ambiguity)
        {
            ambiguity = null;
            InstalledMod exact = ByPackageId(e.PackageId, byName);
            if (exact != null) return exact;
            if (!string.IsNullOrEmpty(e.PackageId)) return null;

            // RWT's own mod id is matched before names, so a mod renamed in an update still matches.
            InstalledMod sameId = ByWorkshopId(e.Id, byWorkshop);
            if (sameId != null) return sameId;

            if (!byName.TryGetValue(e.Name, out List<InstalledMod> named)) return null;
            if (named.Count == 1) return named[0];

            ambiguity = "you have " + named.Count + " mods called \"" + e.Name
                      + "\", so Colony Nexus cannot tell which one this server means";
            return null;
        }

        // Newly enabled mods go last, where RimWorld itself puts a mod just switched on.
        private static List<string> Sequence(List<string> wanted, IList<string> published, IList<string> current)
        {
            // Keyed by the bare id, so the server's "nova.rimworldtogether" places this player's "..._steam" copy.
            Dictionary<string, string> want = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string w in wanted) if (!want.ContainsKey(Bare(w))) want[Bare(w)] = w;
            List<string> ordered = new List<string>();

            IList<string> lead = published != null && published.Count > 0 ? published : current;
            if (lead != null)
                foreach (string id in lead)
                    if (!string.IsNullOrEmpty(id) && want.TryGetValue(Bare(id), out string mine) && !Has(ordered, mine)) ordered.Add(mine);

            foreach (string id in wanted)
                if (!Has(ordered, id)) ordered.Add(id);

            return ordered;
        }

        // "<id>_steam" is the same mod as "<id>", so either copy satisfies a check for it.
        private static bool Has(List<string> list, string id)
        {
            string want = Bare(id);
            foreach (string s in list) if (string.Equals(Bare(s), want, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string Bare(string id)
            => id != null && id.EndsWith(SteamDuplicateSuffix, StringComparison.OrdinalIgnoreCase)
                ? id.Substring(0, id.Length - SteamDuplicateSuffix.Length) : id ?? "";

        private const string CoreId = "ludeon.rimworld";
        internal const string RwtId = "nova.rimworldtogether";
        private const string KmhId  = Constants.PackageId;

        private static void PromoteCore(List<string> order)
        {
            int at = order.FindIndex(id => string.Equals(id, CoreId, StringComparison.OrdinalIgnoreCase));
            if (at <= 0) return;
            order.RemoveAt(at);
            order.Insert(0, CoreId);
        }

        private static bool SameOrder(IList<string> a, IList<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}
