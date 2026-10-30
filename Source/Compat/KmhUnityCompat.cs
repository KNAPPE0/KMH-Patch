using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Video;

namespace KMHPatch.Compat
{
    // A member missing from RimWorld 1.7's UnityStubs fails its method at JIT time, before any try runs, so each sits
    // alone in a NoInlining helper called from a try; the first miss is remembered and the feature goes on without it.
    internal static class KmhUnityCompat
    {
        private static bool _noReverbZoneMix, _noCanSetTime, _noAudioTrackCount, _noGpuVendor;

        // Opt-out of reverb zones; absent on 1.7, which has no reverb zones to opt out of.
        public static void SetReverbZoneMix(AudioSource a, float value)
        {
            if (a == null || _noReverbZoneMix) return;
            try { ReverbZoneMix(a, value); }
            catch (MissingMemberException) { _noReverbZoneMix = true; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReverbZoneMix(AudioSource a, float value) => a.reverbZoneMix = value;

        // False where the player cannot say: a seek bar is then hidden rather than drawn dead.
        public static bool CanSetTime(VideoPlayer p)
        {
            if (p == null || _noCanSetTime) return false;
            try { return CanSetTimeOf(p); }
            catch (MissingMemberException) { _noCanSetTime = true; return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool CanSetTimeOf(VideoPlayer p) => p.canSetTime;

        // Unity needs the track count set before Prepare for url audio; a player without the member routes on its own.
        public static void SetControlledAudioTrackCount(VideoPlayer p, ushort count)
        {
            if (p == null || _noAudioTrackCount) return;
            try { AudioTrackCount(p, count); }
            catch (MissingMemberException) { _noAudioTrackCount = true; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AudioTrackCount(VideoPlayer p, ushort count) => p.controlledAudioTrackCount = count;

        public static string GraphicsDeviceVendor()
        {
            if (_noGpuVendor) return "";
            try { return GpuVendor() ?? ""; }
            catch (MissingMemberException) { _noGpuVendor = true; return ""; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GpuVendor() => SystemInfo.graphicsDeviceVendor;

        // GetAccessControl is Mono/.NET Framework only; CoreCLR has it as an extension, found by name (no net472 reference).
        public static System.Security.AccessControl.FileSecurity GetFileAccessControl(FileInfo info)
        {
            if (!KmhGameVersion.IsCoreClr) return MonoGetAcl(info);
            return (System.Security.AccessControl.FileSecurity)AclExtension("GetAccessControl", 1)?.Invoke(null, new object[] { info });
        }

        public static bool SetFileAccessControl(FileInfo info, System.Security.AccessControl.FileSecurity acl)
        {
            if (!KmhGameVersion.IsCoreClr) { MonoSetAcl(info, acl); return true; }
            var set = AclExtension("SetAccessControl", 2);
            if (set == null) return false;
            set.Invoke(null, new object[] { info, acl });
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static System.Security.AccessControl.FileSecurity MonoGetAcl(FileInfo info) => info.GetAccessControl();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void MonoSetAcl(FileInfo info, System.Security.AccessControl.FileSecurity acl) => info.SetAccessControl(acl);

        private static System.Reflection.MethodInfo AclExtension(string name, int args)
        {
            Type ext = Type.GetType("System.IO.FileSystemAclExtensions, System.IO.FileSystem.AccessControl", false);
            if (ext == null) return null;
            foreach (var m in ext.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                var ps = m.GetParameters();
                if (m.Name == name && ps.Length == args && ps[0].ParameterType == typeof(FileInfo)) return m;
            }
            return null;
        }
    }
}
