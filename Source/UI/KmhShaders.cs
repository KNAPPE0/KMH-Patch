using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // The GLSL is proven to compile before the first draw: a failed Waylith compile only logs and draws magenta.
    internal static class KmhShaders
    {
        public const string SweepDef = "ColonyNexus_MaskSweep";
        public const string SweepFile = "ColonyNexusMaskSweep.frag.glsl";
        private static readonly string[] SweepUniforms = { "_Color", "_MainTex_ST", "_SweepPos", "_SweepAspect", "_SweepWidth" };
        private static readonly Rect FullUv = new Rect(0f, 0f, 1f, 1f);

        private enum State { NotStarted, Waiting, Ready, Failed }
        private static State _state;
        private static string _status = "not checked yet";
        private static Material _sweep;
        private static int _idPos, _idAspect, _idColor;
        private static float _nextTry;
        public static int Draws { get; private set; }

        public static bool Enabled => KMHPatchMod.Settings?.ShaderEffects == true;

        public static string Status => _state.ToString().ToLowerInvariant() + ": " + _status;

        // False means the caller draws its fallback.
        public static bool DrawMaskedSweep(Rect r, Texture tex, float pos, float alpha, Color color)
        {
            if (!Enabled || tex == null || !Ready()) return false;
            if (Event.current == null || Event.current.type != EventType.Repaint) return true;
            if (pos <= 0f || pos >= 1f || alpha <= 0.001f || r.width < 1f || r.height < 1f) return true;
            try
            {
                _sweep.SetFloat(_idPos, pos);
                _sweep.SetFloat(_idAspect, r.width / r.height);
                _sweep.SetColor(_idColor, color);
                Graphics.DrawTexture(r, tex, FullUv, 0, 0, 0, 0, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha) * GUI.color.a), _sweep);
                Draws++;
                return true;
            }
            catch (Exception ex) { Fail("draw threw " + ex.GetType().Name + ": " + ex.Message); return false; }
        }

        // Whether the shipped shader is usable here, checked whatever the setting says (the self-test asks this).
        public static bool Ready()
        {
            if (_state == State.Ready) return true;
            if (_state == State.Failed) return false;
            if (_state == State.Waiting && Time.realtimeSinceStartup < _nextTry) return false;
            try { Check(); }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
                Fail("check threw " + inner.GetType().Name + ": " + inner.Message);
            }
            return _state == State.Ready;
        }

        private static void Check()
        {
            if (!Compat.KmhGameVersion.IsCoreClr) { Fail("not RimWorld 1.7; native shaders are 1.7 only", quiet: true); return; }
            if (!UnityData.IsInMainThread) { Wait("waits for the main thread"); return; }
            Assembly waylith = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Waylith");
            Type registry = waylith?.GetType("Waylith.Rendering.ShaderRegistry"), shaderT = waylith?.GetType("Waylith.Rendering.Shader");
            Type loader = waylith?.GetType("Waylith.Rendering.ShaderSourceLoader"), compiler = waylith?.GetType("Waylith.Rendering.RuntimeShaderCompiler");
            Type lang = waylith?.GetType("Waylith.Rendering.RuntimeShaderLanguage"), stage = waylith?.GetType("Waylith.Rendering.RuntimeShaderStage");
            if (registry == null || shaderT == null || loader == null || compiler == null || lang == null || stage == null)
            { Fail("the Waylith shader API is not where 1.7 had it"); return; }

            object[] resolve = { SweepDef, null };
            if (!(bool)M(registry, "TryResolve", typeof(string), shaderT.MakeByRefType()).Invoke(null, resolve) || resolve[1] == null)
            {
                if (!PlayDataLoader.Loaded) Wait("defs still loading");
                else Fail("ShaderDef " + SweepDef + " is not registered (1.7/Defs/ShaderDefs)");
                return;
            }
            object engine = resolve[1];
            if ((bool)shaderT.GetProperty("HasErrors").GetValue(engine, null)) { Fail(SweepFile + " has layout errors (it would draw magenta)"); return; }

            object layout = shaderT.GetProperty("MaterialLayout").GetValue(engine, null);
            var slots = layout?.GetType().GetProperty("UniformSlots")?.GetValue(layout, null) as Array;
            string[] names = slots == null ? new string[0]
                : slots.Cast<object>().Select(s => s.GetType().GetProperty("PropertyName").GetValue(s, null) as string).ToArray();
            string[] missing = SweepUniforms.Where(u => !names.Contains(u)).ToArray();
            if (missing.Length > 0) { Fail("uniforms missing: " + string.Join(", ", missing)); return; }

            // Compiling the source here surfaces Slang's message instead of a silent magenta quad, and warms the cache.
            string source = (string)M(loader, "ReadShaderSource", typeof(string)).Invoke(null, new object[] { SweepFile });
            object fragment = Enum.Parse(stage, "Fragment");
            object[] args = { source, "ColonyNexus.MaskSweep", "main", Enum.Parse(lang, "Glsl"), fragment, false };
            var sw = Stopwatch.StartNew();
            byte[] spirv;
            try { spirv = (byte[])M(compiler, "Compile", typeof(string), typeof(string), typeof(string), lang, stage, typeof(bool).MakeByRefType()).Invoke(null, args); }
            catch (TargetInvocationException t) when (t.InnerException != null) { Fail("compile failed: " + t.InnerException.Message); return; }
            if (spirv == null || spirv.Length < 20) { Fail("the compiler returned no SPIR-V"); return; }

            Shader shader = ShaderDatabase.LoadShader(SweepDef);
            if (shader == null) { Fail("ShaderDatabase.LoadShader returned nothing"); return; }
            _idPos = Shader.PropertyToID("_SweepPos");
            _idAspect = Shader.PropertyToID("_SweepAspect");
            _idColor = Shader.PropertyToID("_Color");
            _sweep = new Material(shader) { name = "ColonyNexus_MaskSweep" };
            _status = $"{SweepDef}, {spirv.Length} B SPIR-V ({((bool)args[5] ? "cached" : sw.ElapsedMilliseconds + " ms")})";
            _state = State.Ready;
            KmhLog.Info("Native shader ready: " + _status);
        }

        private static MethodInfo M(Type t, string name, params Type[] args)
            => t.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, args, null) ?? throw new MissingMethodException(t.FullName, name);

        private static void Wait(string why) { _state = State.Waiting; _status = why; _nextTry = Time.realtimeSinceStartup + 1f; }

        private static void Fail(string why, bool quiet = false)
        {
            if (_state == State.Failed) return;
            _state = State.Failed; _status = why; _sweep = null;
            if (!quiet) KmhLog.Warn("Native shader unavailable, drawing without it: " + why);
        }
    }
}
