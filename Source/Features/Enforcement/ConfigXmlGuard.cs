using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // RimWorld constructs whatever type a settings file's Class names, at every launch, so a profile may only name settings types.
    internal static class ConfigXmlGuard
    {
        internal static Func<string, Type> ResolveForTest;
        internal static Func<Assembly, bool> AllowedAssemblyForTest;

        // Null when every enforceable file in the folder is safe to hand to RimWorld, else the first reason not to.
        internal static string FolderRefusal(string folder, Func<string, bool> enforceable)
        {
            if (!Directory.Exists(folder)) return null;
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (!enforceable(name)) continue;
                string why = FileRefusal(file);
                if (why != null) return why;
            }
            return null;
        }

        internal static string FileRefusal(string path)
        {
            string name = Path.GetFileName(path);
            try
            {
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return JsonRefusal(name, File.ReadAllText(path));
                if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return null;
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
                using (XmlReader r = XmlReader.Create(path, settings))
                    while (r.Read())
                    {
                        if (r.NodeType != XmlNodeType.Element) continue;
                        string cls = r.GetAttribute("Class");
                        if (cls == null) continue;
                        string why = ClassRefusal(cls.Trim(), r.Depth == 1 && r.LocalName == "ModSettings");
                        if (why != null) return $"'{name}' {why}";
                    }
                return null;
            }
            catch (XmlException ex) { return $"'{name}' is not plain settings XML ({ex.Message})"; }
            catch (IOException ex) { return $"'{name}' could not be read ({ex.Message})"; }
        }

        // Read as Newtonsoft reads it: a name written with JSON escapes is decoded first, so the raw text proves nothing.
        internal static string JsonRefusal(string name, string text)
        {
            if ((text ?? "").IndexOf('\0') >= 0) return $"'{name}' holds a NUL character, which a settings file never does";
            try
            {
                int open = 0;
                bool any = false;
                using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(text ?? "")))
                    while (reader.Read())
                    {
                        Newtonsoft.Json.JsonToken k = reader.TokenType;
                        if (k == Newtonsoft.Json.JsonToken.Comment) continue;
                        any = true;
                        if (k == Newtonsoft.Json.JsonToken.StartObject || k == Newtonsoft.Json.JsonToken.StartArray || k == Newtonsoft.Json.JsonToken.StartConstructor) open++;
                        else if (k == Newtonsoft.Json.JsonToken.EndObject || k == Newtonsoft.Json.JsonToken.EndArray || k == Newtonsoft.Json.JsonToken.EndConstructor) open--;
                        // Only $type builds a type; $id and $ref are reference markers some mods write into their own settings.
                        else if (k == Newtonsoft.Json.JsonToken.PropertyName && string.Equals(((reader.Value as string) ?? "").Trim(), "$type", StringComparison.OrdinalIgnoreCase))
                            return $"'{name}' names a .NET type (\"{Clip((string)reader.Value)}\"), which a settings file never needs";
                    }
                // The reader stops quietly at the end of a cut-off file, so an unclosed one is caught here.
                if (!any || open != 0) return $"'{name}' is not plain settings JSON (it ends before it is complete)";
                return null;
            }
            catch (Exception ex) { return $"'{name}' is not plain settings JSON ({Clip(ex.Message)})"; }
        }

        // A type nothing resolves is never built; one that resolves must be a settings type from the game or a loaded mod.
        internal static string ClassRefusal(string cls, bool settingsRoot)
        {
            if (cls.Length == 0 || cls.Length > 256 || cls.IndexOfAny(new[] { ',', '[', ']', '`', '&', '*', '/', '\\' }) >= 0)
                return $"names the type \"{Clip(cls)}\" in a form only code outside the game's mods would use";
            Type t = ResolveForTest != null ? ResolveForTest(cls) : GenTypes.GetTypeInAnyAssembly(cls);
            if (t == null) return null;
            if (!(AllowedAssemblyForTest ?? FromGameOrMod)(t.Assembly))
                return $"names \"{Clip(cls)}\", which is not part of RimWorld or a loaded mod";
            if (t.IsAbstract || t.IsInterface || t.ContainsGenericParameters)
                return $"names \"{Clip(cls)}\", which cannot be a settings object";
            if (settingsRoot ? !typeof(ModSettings).IsAssignableFrom(t) : !typeof(IExposable).IsAssignableFrom(t))
                return $"names \"{Clip(cls)}\", which is not a {(settingsRoot ? "mod settings" : "saveable")} type";
            return null;
        }

        internal static bool IsGameOrModType(Type t) => t != null && FromGameOrMod(t.Assembly);

        private static readonly Dictionary<Assembly, bool> _allowed = new Dictionary<Assembly, bool>();

        private static bool FromGameOrMod(Assembly a)
        {
            if (a == null) return false;
            lock (_allowed)
            {
                if (_allowed.TryGetValue(a, out bool known)) return known;
                bool ok = a == typeof(Game).Assembly;
                if (!ok)
                    foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                        if (m?.assemblies?.loadedAssemblies != null && m.assemblies.loadedAssemblies.Contains(a)) { ok = true; break; }
                _allowed[a] = ok;
                return ok;
            }
        }

        private static string Clip(string s) => UI.KmhDisplayText.Inert(s, 80);
    }
}
