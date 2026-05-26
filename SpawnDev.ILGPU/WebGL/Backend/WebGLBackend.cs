// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGL
//                        Copyright (c) 2024 SpawnDev Project
//
// File: WebGLBackend.cs
//
// WebGL2/GLSL backend for ILGPU.
// Compiles ILGPU IR to GLSL ES 3.0 vertex shader code using Transform Feedback
// to emulate compute shaders via the CodeGeneratorBackend pattern.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Backends.EntryPoints;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Intrinsics;
using global::ILGPU.IR.Types;
using global::ILGPU.Runtime;
using System.Reflection;
using System.Text;

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// WebGL2/GLSL backend for ILGPU.
    /// Compiles ILGPU IR to GLSL ES 3.0 vertex shader code using the CodeGeneratorBackend pattern.
    /// Transform Feedback captures output from vertex shader to emulate compute shader buffers.
    /// </summary>
    public class WebGLBackend : CodeGeneratorBackend<
        GLSLIntrinsic.Handler,
        GLSLCodeGenerator.GeneratorArgs,
        GLSLCodeGenerator,
        StringBuilder>
    {
        /// <summary>
        /// The backend type constant for WebGL.
        /// </summary>
        public const BackendType BackendTypeWebGL = BackendType.WebGL;

        /// <summary>
        /// Controls whether verbose logging is enabled.
        /// </summary>
        public static bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// Enables shader caching.
        /// </summary>
        public static bool EnableShaderCaching { get; set; } = true;

        /// <summary>
        /// Stores the last generated GLSL source for debugging.
        /// </summary>
        public static string? LastGeneratedGLSL { get; set; }

        /// <summary>
        /// Writes a message to the console.
        /// Caller MUST check <see cref="VerboseLogging"/> BEFORE constructing the message string
        /// to avoid allocating interpolated strings when logging is disabled.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void Log(string message) => Console.WriteLine(message);

        #region Instance

        /// <summary>
        /// The configuration options for this backend.
        /// </summary>
        public WebGLBackendOptions Options { get; }

        /// <summary>
        /// Effective F64 emulation mode for this backend. Defaults to <c>Options.F64Emulation</c>
        /// at construction time and can be promoted at runtime via <see cref="WebGLAccelerator.F64Mode"/>
        /// for callers that need to switch between Dekker and Ozaki without recreating the
        /// backend (e.g. <c>AcceleratorRequirements.RequiresFloat64Strict</c> applied after
        /// the accelerator already exists). Codegen reads this property, so changing it
        /// will affect kernels compiled AFTER the change. Cached compiled kernels still
        /// reflect the mode in effect when they were generated; callers that depend on
        /// re-compilation after a mode flip should clear the kernel cache.
        /// </summary>
        public F64EmulationMode F64Mode { get; set; }

        /// <summary>True if f64 emulation is enabled (Dekker or Ozaki).</summary>
        internal bool EnableF64Emulation => F64Mode != F64EmulationMode.Disabled;

        /// <summary>True if using Ozaki quad-float f64 emulation.</summary>
        internal bool UseOzakiF64Emulation => F64Mode == F64EmulationMode.Ozaki;

        /// <summary>Always true — i64 emulation is required by ILGPU IR.</summary>
        internal bool EnableI64Emulation => true;

        /// <summary>
        /// Creates a new WebGL backend with default options.
        /// </summary>
        public WebGLBackend(Context context)
            : this(context, WebGLBackendOptions.Default)
        {
        }

        /// <summary>
        /// Creates a new WebGL backend with the specified options.
        /// </summary>
        public WebGLBackend(Context context, WebGLBackendOptions options)
            : base(
                  context,
                  new WebGLCapabilityContext(),
                  BackendTypeWebGL,
                  new WebGLArgumentMapper(context))
        {
            Options = options ?? WebGLBackendOptions.Default;
            F64Mode = Options.F64Emulation;

            InitIntrinsicProvider();
            RegisterMathIntrinsics();

            InitializeKernelTransformers(builder =>
            {
                // WebGL-specific transformers (none for now)
            });

            // XMath hard reference removed — loaded dynamically via reflection
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated argument mapper.
        /// </summary>
        public new WebGLArgumentMapper ArgumentMapper =>
            base.ArgumentMapper as WebGLArgumentMapper;

        #endregion

        #region Methods

        private static IntrinsicImplementationManager GetIntrinsicManager(Context context)
        {
            var prop = typeof(Context).GetProperty("IntrinsicManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (prop == null) throw new InvalidOperationException("Could not find IntrinsicManager property on Context.");
            var manager = (IntrinsicImplementationManager)prop.GetValue(context);
            FixIntrinsicManager(manager);
            return manager;
        }

        private static void FixIntrinsicManager(IntrinsicImplementationManager manager)
        {
            try
            {
                var mgrType = typeof(IntrinsicImplementationManager);
                var containersField = mgrType.GetField("containers", BindingFlags.Instance | BindingFlags.NonPublic);
                if (containersField == null) return;

                var containers = (Array)containersField.GetValue(manager);
                int webGlIndex = (int)BackendTypeWebGL;

                if (webGlIndex >= containers.Length || containers.GetValue(webGlIndex) == null)
                {
                    var containerType = mgrType.GetNestedType("BackendContainer", BindingFlags.NonPublic);
                    var createMethod = containerType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public);

                    if (webGlIndex >= containers.Length)
                    {
                        if (VerboseLogging) Log($"WebGL: Resizing IntrinsicManager containers from {containers.Length} to {webGlIndex + 1}");
                        var newContainers = Array.CreateInstance(containerType, webGlIndex + 1);
                        Array.Copy(containers, newContainers, containers.Length);
                        containers = newContainers;
                        containersField.SetValue(manager, containers);
                    }

                    var newContainer = createMethod.Invoke(null, null);
                    containers.SetValue(newContainer, webGlIndex);
                    if (VerboseLogging) Log("WebGL: Initialized BackendContainer for WebGL.");
                }
                else
                {
                    var containerType = mgrType.GetNestedType("BackendContainer", BindingFlags.NonPublic);
                    var container = containers.GetValue(webGlIndex);
                    var matchersField = containerType.GetField("matchers", BindingFlags.Instance | BindingFlags.NonPublic);
                    var matchers = matchersField.GetValue(container);
                    if (matchers == null)
                    {
                        if (VerboseLogging) Log("WebGL: BackendContainer found but uninitialized. Re-initializing.");
                        var createMethod = containerType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public);
                        var newContainer = createMethod.Invoke(null, null);
                        containers.SetValue(newContainer, webGlIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                if (VerboseLogging) Log($"WebGL: Error fixing IntrinsicManager: {ex}");
            }
        }

        private void RegisterIntrinsic(MethodInfo method, GLSLIntrinsic.Handler handler)
        {
            if (method == null)
            {
                if (VerboseLogging) Log("WebGL: Skipping invalid intrinsic method (null)");
                return;
            }
            if (VerboseLogging) Log($"WebGL: Registering Intrinsic: {method.DeclaringType.Name}.{method.Name}");
            GetIntrinsicManager(Context).RegisterMethod(
                method,
                new WebGLIntrinsic(
                    handler.Method,
                    IntrinsicImplementationMode.GenerateCode));
        }

        private void RegisterIntrinsic(Type type, string methodName, GLSLIntrinsic.Handler handler)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == methodName);

            bool found = false;
            foreach (var method in methods)
            {
                found = true;
                RegisterIntrinsic(method, handler);
            }
            if (!found)
                if (VerboseLogging) Log($"WebGL: Intrinsic not found: {type.Name}.{methodName}");
        }

        private void RegisterRedirect(MethodInfo original, MethodInfo target)
        {
            if (original == null || target == null) return;
            if (VerboseLogging) Log($"WebGL: Redirecting {original.DeclaringType.Name}.{original.Name} -> {target.DeclaringType.Name}.{target.Name}");
            GetIntrinsicManager(Context).RegisterMethod(
                original,
                new WebGLIntrinsic(
                    target,
                    IntrinsicImplementationMode.Redirect));
        }

        private void RegisterMathIntrinsics()
        {
            var t = typeof(WebGLIntrinsics);

            void Reg(MethodInfo original, MethodInfo wrapper, GLSLIntrinsic.Handler handler)
            {
                if (original == null) return;
                RegisterRedirect(original, wrapper);
                RegisterIntrinsic(wrapper, handler);
            }

            void RegAll(Type type, string name, GLSLIntrinsic.Handler handler)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == name);

                foreach (var m in methods)
                {
                    MethodInfo target = m;
                    if (m.IsGenericMethod)
                    {
                        var gArgs = m.GetGenericArguments();
                        if (gArgs.Length == 1)
                        {
                            try { target = m.MakeGenericMethod(typeof(float)); } catch { continue; }
                        }
                        else continue;
                    }

                    var pTypes = target.GetParameters().Select(p => p.ParameterType).ToArray();
                    var wrapper = t.GetMethod(
                        name,
                        BindingFlags.Public | BindingFlags.Static,
                        null, pTypes, null);

                    if (wrapper != null)
                    {
                        if (VerboseLogging) Log($"WebGL: Mapping {type.Name}.{name}({string.Join(",", pTypes.Select(pt => pt.Name))}) to {t.Name}.{name}");
                        Reg(target, wrapper, handler);
                    }
                    else
                    {
                        var pTypeNames = string.Join(", ", pTypes.Select(pt => pt.Name));
                        if (VerboseLogging) Log($"WebGL: Missing wrapper for {type.Name}.{name}({pTypeNames})");
                    }
                }
            }

            // Unary
            RegAll(typeof(Math), "Abs", GLSLCodeGenerator.GenerateAbs);
            RegAll(typeof(MathF), "Abs", GLSLCodeGenerator.GenerateAbs);
            RegAll(typeof(Math), "Sign", GLSLCodeGenerator.GenerateSign);
            RegAll(typeof(MathF), "Sign", GLSLCodeGenerator.GenerateSign);
            RegAll(typeof(Math), "Round", GLSLCodeGenerator.GenerateRound);
            RegAll(typeof(MathF), "Round", GLSLCodeGenerator.GenerateRound);
            RegAll(typeof(Math), "Truncate", GLSLCodeGenerator.GenerateTruncate);
            RegAll(typeof(MathF), "Truncate", GLSLCodeGenerator.GenerateTruncate);

            // Binary
            RegAll(typeof(Math), "Atan2", GLSLCodeGenerator.GenerateAtan2);
            RegAll(typeof(MathF), "Atan2", GLSLCodeGenerator.GenerateAtan2);
            RegAll(typeof(Math), "Max", GLSLCodeGenerator.GenerateMax);
            RegAll(typeof(MathF), "Max", GLSLCodeGenerator.GenerateMax);
            RegAll(typeof(Math), "Min", GLSLCodeGenerator.GenerateMin);
            RegAll(typeof(MathF), "Min", GLSLCodeGenerator.GenerateMin);
            RegAll(typeof(Math), "Pow", GLSLCodeGenerator.GeneratePow);
            RegAll(typeof(MathF), "Pow", GLSLCodeGenerator.GeneratePow);

            // Ternary
            RegAll(typeof(Math), "Clamp", GLSLCodeGenerator.GenerateClamp);
            RegAll(typeof(MathF), "Clamp", GLSLCodeGenerator.GenerateClamp);
            RegAll(typeof(Math), "FusedMultiplyAdd", GLSLCodeGenerator.GenerateFusedMultiplyAdd);
            RegAll(typeof(MathF), "FusedMultiplyAdd", GLSLCodeGenerator.GenerateFusedMultiplyAdd);

            // IntrinsicMath
            RegAll(typeof(IntrinsicMath), "Abs", GLSLCodeGenerator.GenerateAbs);
            RegAll(typeof(IntrinsicMath), "Min", GLSLCodeGenerator.GenerateMin);
            RegAll(typeof(IntrinsicMath), "Max", GLSLCodeGenerator.GenerateMax);

            // XMath
            try
            {
                var xmathType = Type.GetType("ILGPU.Algorithms.XMath, ILGPU.Algorithms");
                if (xmathType != null)
                {
                    RegAll(xmathType, "Rsqrt", GLSLCodeGenerator.GenerateRsqrt);
                    RegAll(xmathType, "Rcp", GLSLCodeGenerator.GenerateRcp);
                    if (VerboseLogging) Log("WebGL: Registered XMath intrinsics (Rsqrt, Rcp)");
                }
            }
            catch (Exception ex)
            {
                if (VerboseLogging) Log($"WebGL: Error registering XMath intrinsics: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a new entry point.
        /// </summary>
        protected override EntryPoint CreateEntryPoint(
            in EntryPointDescription entry,
            in BackendContext backendContext,
            in KernelSpecialization specialization) =>
            new EntryPoint(
                entry,
                backendContext.SharedMemorySpecification,
                specialization);

        /// <summary>
        /// Creates the main kernel builder and initializes generator args.
        /// </summary>
        protected override StringBuilder CreateKernelBuilder(
            EntryPoint entryPoint,
            in BackendContext backendContext,
            in KernelSpecialization specialization,
            out GLSLCodeGenerator.GeneratorArgs data)
        {
            var builder = new StringBuilder();

            builder.AppendLine("#version 300 es");
            builder.AppendLine("//");
            builder.Append("// Generated by SpawnDev.ILGPU.WebGL v");
            builder.AppendLine(Context.Version);
            builder.AppendLine("//");
            builder.AppendLine("precision highp float;");
            builder.AppendLine("precision highp int;");
            builder.AppendLine();

            // Struct definitions land here so helper functions (emitted next, with
            // struct-typed params) can see the type. The original placeholder in
            // GLSLKernelFunctionGenerator.GenerateHeader still emits, but lands
            // AFTER helpers - removed (replaced with empty) by CreateKernel.
            // (rc.16 fn-def-codegen Bug D, WebGL ordering fix, 2026-05-05.)
            builder.AppendLine("// __STRUCT_DEFS_TOP_PLACEHOLDER__");
            builder.AppendLine();

            // Grid dimension uniforms (needed for 2D/3D index decomposition)
            if (entryPoint.IndexType == IndexType.Index2D || entryPoint.IndexType == IndexType.Index3D)
            {
                builder.AppendLine("uniform int u_grid_width;");
                if (entryPoint.IndexType == IndexType.Index3D)
                    builder.AppendLine("uniform int u_grid_height;");
                builder.AppendLine();
            }

            var typeGenerator = new GLSLTypeGenerator(this, Context.TypeContext);

            data = new GLSLCodeGenerator.GeneratorArgs(
                this,
                typeGenerator,
                entryPoint,
                backendContext.SharedAllocations,
                backendContext.DynamicSharedAllocations);

            return builder;
        }

        /// <summary>
        /// Creates a function-code generator for helper methods.
        /// </summary>
        protected override GLSLCodeGenerator CreateFunctionCodeGenerator(
            Method method,
            Allocas allocas,
            GLSLCodeGenerator.GeneratorArgs data) =>
            new GLSLFunctionGenerator(data, method, allocas);

        /// <summary>
        /// Creates a kernel-code generator for the main entry point.
        /// </summary>
        protected override GLSLCodeGenerator CreateKernelCodeGenerator(
            in AllocaKindInformation sharedAllocations,
            Method method,
            Allocas allocas,
            GLSLCodeGenerator.GeneratorArgs data) =>
            new GLSLKernelFunctionGenerator(data, method, allocas);

        /// <summary>
        /// Creates the final compiled kernel.
        /// </summary>
        protected override CompiledKernel CreateKernel(
            EntryPoint entryPoint,
            CompiledKernel.KernelInfo? kernelInfo,
            StringBuilder builder,
            GLSLCodeGenerator.GeneratorArgs data)
        {
            // All code generation is complete — now emit struct type definitions.
            // Types are discovered lazily during code generation, so we collect them
            // here and replace the top-of-file placeholder (set up in
            // CreateKernelBuilder) BEFORE helper fn definitions, which need the
            // struct types in scope. The kernel-header placeholder in
            // GLSLKernelFunctionGenerator.GenerateHeader is no longer needed for
            // ordering and gets stripped to empty here.
            var structDefs = new StringBuilder();
            data.TypeGenerator.GenerateTypeDefinitions(structDefs, data.BodyStructTypeIdsToSkip);
            var glslSource = builder.ToString()
                .Replace("// __STRUCT_DEFS_TOP_PLACEHOLDER__\r\n", structDefs.ToString())
                .Replace("// __STRUCT_DEFS_TOP_PLACEHOLDER__\n", structDefs.ToString())
                // The original kernel-header placeholder. Helpers physically appear
                // before this line in the file (helpers Merge before the kernel's
                // GenerateHeader runs), so replacing this one with struct defs would
                // place them AFTER the helpers that use them - GLSL parse error.
                // Strip it to empty; the top-of-file placeholder above carries the
                // real struct defs.
                .Replace("// __STRUCT_DEFS_PLACEHOLDER__\r\n", "")
                .Replace("// __STRUCT_DEFS_PLACEHOLDER__\n", "")
                // GLSL ES 3.0: ANGLE crashes on INT_MIN (-2147483648) regardless
                // of representation (even constant-folded expressions). Replace with
                // -2147483647 which is semantically equivalent for bounds checks.
                .Replace("int(-2147483648)", "int(-2147483647)");
            if (VerboseLogging)
            {
                Log("--- GENERATED GLSL ---");
                Log(glslSource);
                Log("-----------------------");
            }
            LastGeneratedGLSL = glslSource;
            return new WebGLCompiledKernel(
                Context,
                entryPoint,
                glslSource,
                parameterBindings: data.ParameterBindings,
                outputVaryings: data.OutputVaryings,
                bodyStructManifest: data.BodyStructManifest);
        }

        #endregion
    }

    /// <summary>
    /// WebGL capability context — defines the capabilities of the WebGL2 backend.
    /// </summary>
    public class WebGLCapabilityContext : CapabilityContext
    {
        // WebGL2 has limited capabilities compared to WebGPU
    }

    /// <summary>
    /// Represents a compiled WebGL2/GLSL kernel (vertex shader).
    /// </summary>
    public class WebGLCompiledKernel : CompiledKernel
    {
        /// <summary>
        /// Monotonic source for the glWorker program-cache id (<see cref="ProgramId"/>). MUST be
        /// unique-and-stable, NOT <c>GLSLSource.GetHashCode()</c>: a string hash is non-unique (two
        /// distinct shaders CAN collide on one value) and <c>string.GetHashCode()</c> is volatile under
        /// .NET Wasm Hybrid Globalization. A collision would make the glWorker reuse the WRONG cached GL
        /// program (<c>programCache[programId]</c>, glWorker.js) → the wrong shader runs → silent wrong
        /// output. See Wasm/CLAUDE.md "kernelId MUST be a monotonic unique id".
        /// </summary>
        private static int _nextProgramId = 0;

        /// <summary>
        /// Stable, process-unique id for this kernel's glWorker program-cache slot
        /// (<c>programCache[programId]</c>). Assigned once at compile time; stable across all dispatches
        /// of this compiled kernel and unique across distinct kernels. Replaces the old
        /// <c>GLSLSource.GetHashCode()</c> id, which could collide and run the wrong cached program.
        /// </summary>
        public int ProgramId { get; } = System.Threading.Interlocked.Increment(ref _nextProgramId);

        /// <summary>
        /// The generated GLSL ES 3.0 vertex shader source.
        /// </summary>
        public string GLSLSource { get; }

        /// <summary>
        /// Parameter binding metadata for runtime dispatch.
        /// </summary>
        public IReadOnlyList<KernelParameterBinding> ParameterBindings { get; }

        /// <summary>
        /// Output varying metadata for Transform Feedback.
        /// </summary>
        public IReadOnlyList<OutputVaryingInfo> OutputVaryings { get; }

        /// <summary>
        /// Per-field metadata for body-struct kernel parameters. Empty when no body
        /// struct is present. Consumed by WebGLAccelerator.MarshalArguments to
        /// decompose each body-struct arg into per-field buffer_ref dispatch entries.
        /// </summary>
        public IReadOnlyList<BodyStructBindingEntry> BodyStructManifest { get; }

        public WebGLCompiledKernel(
            Context context,
            EntryPoint entryPoint,
            string glslSource,
            IReadOnlyList<KernelParameterBinding>? parameterBindings = null,
            IReadOnlyList<OutputVaryingInfo>? outputVaryings = null,
            IReadOnlyList<BodyStructBindingEntry>? bodyStructManifest = null)
            : base(context, entryPoint, null)
        {
            GLSLSource = glslSource;
            ParameterBindings = parameterBindings ?? Array.Empty<KernelParameterBinding>();
            OutputVaryings = outputVaryings ?? Array.Empty<OutputVaryingInfo>();
            BodyStructManifest = bodyStructManifest ?? Array.Empty<BodyStructBindingEntry>();
        }
    }

    /// <summary>
    /// Argument mapper for WebGL kernel parameters.
    /// </summary>
    public class WebGLArgumentMapper : ArgumentMapper
    {
        public WebGLArgumentMapper(Context context) : base(context) { }

        /// <summary>
        /// Maps a view type to its WebGL equivalent.
        /// </summary>
        protected override System.Type MapViewType(System.Type viewType, System.Type elementType)
        {
            return viewType;
        }

        /// <summary>
        /// Maps a view instance for kernel invocation.
        /// </summary>
        protected override void MapViewInstance<TILEmitter, TSource, TTarget>(
            in TILEmitter emitter,
            System.Type viewType,
            in TSource source,
            in TTarget target)
        {
            // View mapping is handled separately for WebGL
        }
    }
}
