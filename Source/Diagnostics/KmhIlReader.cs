using System;
using System.Collections.Generic;
using System.Reflection;

namespace KMHPatch.Diagnostics
{
    // Twin of the server's reader: a disposal deleted from a socket's Start leaves every other check green.
    internal static class KmhIlReader
    {
        // An operand byte can look like a call opcode: use this for "does it call X", never "these are all the calls".
        public static List<string> CallsMadeBy(string typeName, string methodName, string namePrefix = null)
        {
            var names = new List<string>();
            foreach (MethodBase m in MethodsCalledBy(typeName, methodName))
                if (namePrefix == null || m.Name.StartsWith(namePrefix, StringComparison.Ordinal)) names.Add(m.Name);
            return names;
        }

        // The same walk, resolved to the methods themselves, for when two overloads share a name.
        public static List<MethodBase> MethodsCalledBy(string typeName, string methodName)
        {
            var found = new List<MethodBase>();
            Type t = typeof(KmhIlReader).Assembly.GetType(typeName);
            MethodInfo m = t?.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic
                                                  | BindingFlags.Static | BindingFlags.Instance);
            found.AddRange(From(m));

            // An async lambda compiles to a closure type plus a nested state machine: its calls sit two levels down.
            if (t != null) Walk(t, methodName, found, 0);
            return found;
        }

        private static void Walk(Type t, string methodName, List<MethodBase> found, int depth)
        {
            if (depth > 4) return;
            foreach (Type nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                bool mine = nested.Name.IndexOf(methodName, StringComparison.Ordinal) >= 0;
                foreach (MethodInfo nm in nested.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                          | BindingFlags.Static | BindingFlags.Instance
                                                          | BindingFlags.DeclaredOnly))
                {
                    if (!mine && nm.Name.IndexOf(methodName, StringComparison.Ordinal) < 0) continue;
                    found.AddRange(From(nm));
                }
                Walk(nested, methodName, found, depth + 1);
            }
        }

        private static IEnumerable<MethodBase> From(MethodBase m)
        {
            byte[] il = null;
            try { il = m?.GetMethodBody()?.GetILAsByteArray(); } catch { }
            if (il == null) yield break;
            Module mod = m.Module;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue;
                int tok = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                MethodBase called = null;
                try { called = mod.ResolveMethod(tok); } catch { }
                if (called != null) yield return called;
            }
        }
    }
}
