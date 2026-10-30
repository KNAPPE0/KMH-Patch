using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace KMHPatch.Compat
{
    // RWT ships MessagePack's .NET Framework build, which asks for RunAndSave; CoreCLR (RimWorld 1.7) refuses that, so no
    // packet serializes. Only the unused Save() needs the Save bit, so it is dropped in memory; no file is touched.
    internal static class KmhMessagePackCoreClr
    {
        // RWT 26.10.20.1+ pre-generates formatters, so this is a standby for a type it missed; RunnableCounted logs first use.
        internal const string TypeName = "MessagePack.Internal.DynamicAssembly";
        internal static string Status { get; private set; } = "not run";
        internal static int Rewritten { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            if (!KmhGameVersion.IsCoreClr) { Status = "not needed (Mono)"; return; }
            Type t = AccessTools.TypeByName(TypeName);
            if (t == null) { Status = "not needed (no MessagePack loaded)"; return; }
            ConstructorInfo ctor = Array.Find(t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), c => !c.IsStatic);
            if (ctor == null) { Status = "skipped: no constructor on " + TypeName; return; }
            try
            {
                harmony.Patch(ctor, transpiler: new HarmonyMethod(typeof(KmhMessagePackCoreClr), nameof(Transpile)));
                Status = Rewritten == 1 ? "applied" : $"skipped: expected one DefineDynamicAssembly call, found {Rewritten}";
            }
            catch (Exception ex) { Status = "failed: " + ex.Message.Split('\n')[0]; }
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> code)
        {
            Rewritten = 0;
            foreach (CodeInstruction ci in code)
            {
                if (IsDefineDynamicAssembly(ci))
                {
                    Rewritten++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(KmhMessagePackCoreClr), nameof(RunnableCounted)));
                }
                yield return ci;
            }
        }

        private static bool IsDefineDynamicAssembly(CodeInstruction ci)
            => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo m
            && m.Name == "DefineDynamicAssembly" && m.GetParameters().Length >= 2
            && m.GetParameters()[1].ParameterType == typeof(AssemblyBuilderAccess);

        // Real MessagePack builds only; with pre-generated formatters this may stay 0.
        internal static int Invocations => _invocations;
        private static int _invocations;

        // What the patched constructor calls; tests call Runnable, so they never count as MessagePack using the fix.
        internal static AssemblyBuilderAccess RunnableCounted(AssemblyBuilderAccess access)
        {
            if (System.Threading.Interlocked.Increment(ref _invocations) == 1)
                Diagnostics.KmhLog.Info($"MessagePack on CoreCLR: built a dynamic assembly ({access}); the fix let it run.");
            return Runnable(access);
        }

        // The access argument is on top of the stack. Dropping the Save bit leaves Run, or RunAndCollect as it was.
        internal static AssemblyBuilderAccess Runnable(AssemblyBuilderAccess access)
        {
            int a = (int)access & ~2;
            return (AssemblyBuilderAccess)(a == 0 ? 1 : a);
        }
    }
}
