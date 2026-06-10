using System;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU;

/// <summary>
/// Declares that a kernel method should be PRECOMPILED at build time for a given backend +
/// capability profile (precompiled-shaders Layer 2). The MSBuild precompile task scans for this
/// attribute, runs <see cref="ShaderCompiler.Generate"/> per (kernel x profile), and emits the
/// artifact + a manifest that registers it into <see cref="ShaderArtifactCache"/> so the runtime
/// load path can use it instead of transpiling (Layer 3).
///
/// Apply multiple times for multiple (backend, profile) targets. <see cref="Profile"/> names a
/// preset registered in <see cref="CapabilityProfiles"/> (e.g. "WebGPU-f16-subgroups",
/// "WasmDefault") or a project-defined profile.
///
/// Purely declarative - it has NO runtime effect on its own; precompilation is opt-in via the
/// <c>&lt;SpawnDevPrecompileShaders&gt;</c> csproj flag, and a cache miss always falls back to
/// runtime generation, so the attribute can never change results.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class PrecompiledKernelAttribute : Attribute
{
    /// <summary>The backend this precompiled target is for (WebGPU / WebGL / Wasm).</summary>
    public AcceleratorType Backend { get; }

    /// <summary>The name of the <see cref="CapabilityProfile"/> preset to precompile against.</summary>
    public string Profile { get; }

    /// <summary>Declares a precompile target for this kernel.</summary>
    public PrecompiledKernelAttribute(AcceleratorType backend, string profile)
    {
        Backend = backend;
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }
}
