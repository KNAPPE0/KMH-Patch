using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KMH.Sdk.Client;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Extensibility
{
    // Client extensions are ordinary RimWorld mods, so they are already in the AppDomain by the Mod ctor.
    internal static class ExtensionLoader
    {
        private static readonly List<LoadedExtension> _loaded = new List<LoadedExtension>();
        public static IReadOnlyList<LoadedExtension> Loaded => _loaded;

        public static void DiscoverAndLoad()
        {
            try
            {
                Assembly self = typeof(ExtensionLoader).Assembly;
                Assembly sdk  = typeof(IKmhClientExtension).Assembly;

                int candidateCount = 0;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == self || asm == sdk)                       continue;
                    if (asm.IsDynamic || asm.GlobalAssemblyCache)        continue;

                    // Skips GetTypes() on the ~150 assemblies a typical load carries.
                    bool refsSdk = false;
                    try
                    {
                        foreach (AssemblyName n in asm.GetReferencedAssemblies())
                        {
                            if (n.Name == sdk.GetName().Name) { refsSdk = true; break; }
                        }
                    }
                    catch { continue; }
                    if (!refsSdk) continue;
                    candidateCount++;

                    TryLoadFromAssembly(asm);
                }

                KmhLog.Info($"Extensions: scanned {candidateCount} candidate assembly/ies, loaded {_loaded.Count}.");
            }
            catch (Exception ex)
            {
                KmhLog.Error("Extension discovery threw", ex);
            }
        }

        public static void ShutdownAll()
        {
            foreach (LoadedExtension ext in _loaded)
            {
                try { ext.Instance.Shutdown(); }
                catch (Exception ex)
                {
                    KmhLog.Warn($"Extension '{ext.Name}' Shutdown threw: {ex.Message}");
                }
            }
        }

        private static void TryLoadFromAssembly(Assembly asm)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                KmhLog.Warn($"Extensions: type load failure in '{asm.GetName().Name}':");
                foreach (Exception inner in ex.LoaderExceptions.Take(3))
                    KmhLog.Warn($"  - {inner.Message}");
                types = ex.Types.Where(t => t != null).ToArray();
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Extensions: GetTypes failed for '{asm.GetName().Name}': {ex.Message}");
                return;
            }

            foreach (Type t in types)
            {
                if (t == null || t.IsAbstract || t.IsInterface) continue;
                if (!typeof(IKmhClientExtension).IsAssignableFrom(t)) continue;
                TryInstantiate(t, asm);
            }
        }

        private static void TryInstantiate(Type type, Assembly asm)
        {
            IKmhClientExtension instance;
            try { instance = (IKmhClientExtension)Activator.CreateInstance(type); }
            catch (Exception ex)
            {
                KmhLog.Warn($"Extensions: could not instantiate '{type.FullName}' from '{asm.GetName().Name}': {ex.Message}");
                return;
            }

            string name    = string.IsNullOrWhiteSpace(instance.Name)    ? type.FullName : instance.Name;
            string version = string.IsNullOrWhiteSpace(instance.Version) ? "0.0.0"       : instance.Version;

            KmhClientHost host = new KmhClientHost(name);
            try { instance.Register(host); }
            catch (Exception ex)
            {
                KmhLog.Error($"Extensions: '{name}' v{version} Register() threw - disabling", ex);
                return;
            }

            _loaded.Add(new LoadedExtension
            {
                Name        = name,
                Version     = version,
                SourceAsm   = asm.GetName().Name,
                Instance    = instance,
                Host        = host,
            });
            KmhLog.Info($"Extensions: loaded '{name}' v{version} (from {asm.GetName().Name})");
        }
    }

    internal sealed class LoadedExtension
    {
        public string                  Name      { get; set; }
        public string                  Version   { get; set; }
        public string                  SourceAsm { get; set; }
        public IKmhClientExtension     Instance  { get; set; }
        public KmhClientHost           Host      { get; set; }
    }
}
