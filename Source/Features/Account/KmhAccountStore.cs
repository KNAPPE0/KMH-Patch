using System;
using System.IO;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;
using Verse;

namespace KMHPatch.Features.Account
{
    // Not in mod settings: those get copied between installs, and this credential stands for an identity.
    internal static class KmhAccountStore
    {
        internal static string RootOverrideForTest;

        private static string Root => RootOverrideForTest
            ?? Persistence.KmhDataFolder.Root;

        private static string Path_ => System.IO.Path.Combine(Root, "account.json");

        private sealed class Record
        {
            public int    SchemaVersion = 1;
            public string Directory = "";
            public string PlayerToken = "";
            public string PlayerGlobalId = "";
            public string DisplayName = "";
            public long   LinkedAtUnix;
            // The Steam account this credential was linked or confirmed as; 0 until known.
            public ulong  SteamId64;
        }

        private static Record _cached;
        private static bool _loaded;
        // Global said this launch's Steam account is not the credential's owner.
        private static bool _refused;

        private static Record Current
        {
            get
            {
                if (_loaded) return _cached;
                _loaded = true;
                try
                {
                    if (File.Exists(Path_)) _cached = JsonConvert.DeserializeObject<Record>(File.ReadAllText(Path_));
                }
                catch (Exception ex) { KmhLog.Debug($"Account: could not read the stored link: {ex.Message}"); }
                return _cached ??= new Record();
            }
        }

        // A credential issued by one directory means nothing to another, so it is only offered back to its own.
        public static string StoredTokenFor(string directory)
        {
            Record r = Current;
            return string.Equals(r.Directory, directory, StringComparison.OrdinalIgnoreCase) ? r.PlayerToken ?? "" : "";
        }

        public static bool HasStoredToken(string directory) => StoredTokenFor(directory).Length > 0;

        // What anything acting as the player reads: empty while the running Steam account is not the credential's.
        public static string TokenFor(string directory)
            => MayActAs(KmhSteamPersona.SteamId64()) ? StoredTokenFor(directory) : "";

        public static string PlayerGlobalId => Current.PlayerGlobalId ?? "";
        public static string DisplayName => Current.DisplayName ?? "";
        public static bool HasToken(string directory) => TokenFor(directory).Length > 0;

        public static ulong BoundSteamId => Current.SteamId64;

        internal static bool MayActAs(ulong running) => MayAct(running, Current.SteamId64, _refused);

        // No Steam id is no evidence either way, which is also how Global reads it.
        internal static bool MayAct(ulong running, ulong bound, bool refused)
            => !refused && (running == 0 || bound == running);

        // Held, and only Global can say whose it is.
        internal static bool NeedsSteamCheck(string directory, ulong running)
            => running != 0 && !_refused && Current.SteamId64 == 0 && HasStoredToken(directory);

        public static void ConfirmSteam(ulong steamId64)
        {
            if (steamId64 == 0) return;
            _refused = false;
            Record r = Current;
            if (r.SteamId64 == steamId64 || (r.PlayerToken ?? "").Length == 0) return;
            r.SteamId64 = steamId64;
            Write();
        }

        // True the first time, so the caller tears down only once.
        public static bool RefuseSteam(ulong steamId64)
        {
            bool first = !_refused;
            _refused = true;
            Record r = Current;
            if (steamId64 != 0 && r.SteamId64 == steamId64)
            {
                r.SteamId64 = 0;
                Write();
            }
            return first;
        }

        public static void Save(string directory, string token, string playerGlobalId, string displayName)
        {
            Record was = Current;
            _cached = new Record
            {
                Directory      = directory ?? "",
                PlayerToken    = token ?? "",
                PlayerGlobalId = playerGlobalId ?? "",
                DisplayName    = displayName ?? "",
                LinkedAtUnix   = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                // Linked from this copy, so Global bound it to the Steam account running now.
                SteamId64      = KmhSteamPersona.SteamId64(),
            };
            _loaded = true;
            _refused = false;
            Write();
            ForgetIfChanged(was);
        }

        public static void Clear()
        {
            Record was = Current;
            _cached = new Record();
            _loaded = true;
            _refused = false;
            try { if (File.Exists(Path_)) File.Delete(Path_); }
            catch (Exception ex) { KmhLog.Warn($"Account: could not clear the stored link: {ex.Message}"); }
            ForgetIfChanged(was);
        }

        // Called after the write, so a request started in between carries the new token and is only refused.
        private static void ForgetIfChanged(Record was)
        {
            Record now = _cached;
            bool same = string.Equals(was.Directory ?? "", now.Directory ?? "", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(was.PlayerToken ?? "", now.PlayerToken ?? "", StringComparison.Ordinal)
                     && string.Equals(was.PlayerGlobalId ?? "", now.PlayerGlobalId ?? "", StringComparison.Ordinal);
            if (!same) Global.KmhIdentityTeardown.ForgetIdentity();
        }

        public static void UpdateDisplay(string displayName)
        {
            Record r = Current;
            if (string.Equals(r.DisplayName, displayName, StringComparison.Ordinal)) return;
            r.DisplayName = displayName ?? "";
            Write();
        }

        // Fires while the new bytes are going down, when only an atomic write still has the old file intact.
        internal static Action DuringWriteForTest;

        // Replaced whole or not at all: a truncated file read as no link while its token stayed valid on Global,
        // with nothing left here to revoke it.
        private static void Write()
        {
            try
            {
                Directory.CreateDirectory(Root);
                Persistence.KmhAtomicFile.Write(Path_, JsonConvert.SerializeObject(_cached, Formatting.Indented),
                    new Persistence.KmhAtomicFile.Options
                    {
                        DuringWrite   = DuringWriteForTest,
                        BeforeReplace = Restrict,
                        AfterReplace  = Restrict,
                    });
            }
            catch (Exception ex) { KmhLog.Warn($"Account: could not save the link: {ex.Message}"); }
        }

        // A bearer credential, so kept from other accounts on the machine. Best effort: RimWorld's Mono may lack ACLs,
        // and it is no defence against code running as the player - 'kmh global unlink' is what revokes it.
        private static void Restrict(string path)
        {
            // A runtime without the ACL API fails when the method is compiled, so the call has to be inside the try.
            try { RestrictAcl(path); }
            catch (Exception ex) { KmhLog.Debug($"Account: could not restrict account.json ({ex.GetType().Name})."); }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RestrictAcl(string path)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            var info = new FileInfo(path);
            System.Security.AccessControl.FileSecurity acl = Compat.KmhUnityCompat.GetFileAccessControl(info);
            if (acl == null) { KmhLog.Debug("Account: this runtime offers no file ACLs; account.json keeps its inherited permissions."); return; }
            using (var me = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                foreach (System.Security.AccessControl.FileSystemAccessRule existing in
                         acl.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)))
                    acl.RemoveAccessRuleAll(existing);
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    me.User, System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
            }
            Compat.KmhUnityCompat.SetFileAccessControl(info, acl);
        }

        internal static void ResetForTest() { _cached = null; _loaded = false; _refused = false; }
    }
}
