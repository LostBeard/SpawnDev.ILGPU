using System;
using System.Collections.Generic;
using System.Reflection;
using ILGPU;
using ILGPU.Backends;
using ILGPU.Backends.EntryPoints;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.ILGPU.Wasm.Backend;
using SpawnDev.ILGPU.WebGL.Backend;

namespace SpawnDev.ILGPU;

/// <summary>
/// Layer 1 of the precompiled-shaders feature (see <c>Plans/precompiled-shaders.md</c>):
/// generate a kernel's shader/binary for a target backend from a
/// <see cref="CapabilityProfile"/> WITHOUT a real device, on any host OS.
///
/// This is the canonical, device-INDEPENDENT entry point (static by design - the build-time
/// MSBuild task and the offline "dump any kernel" tool have no <c>Context</c>/accelerator).
/// It drives the SAME backend code generators the runtime uses, fed by the profile instead
/// of a live adapter, and stops before any GPU resource / pipeline creation.
///
/// Determinism + byte-identical-to-runtime are correctness requirements (see the doc §11);
/// the timestamp-normalization and structural cap-read routing land with tasks #3/#4.
/// </summary>
public static class ShaderCompiler
{
    /// <summary>
    /// Generate the shader/binary for a kernel delegate against a capability profile.
    /// </summary>
    /// <param name="kernel">The kernel method (a delegate; its <see cref="Delegate.Method"/> is used).</param>
    /// <param name="profile">The target device capability profile.</param>
    /// <param name="specialization">Optional kernel specialization (workgroup size, etc.).</param>
    public static GeneratedKernel Generate(
        Delegate kernel,
        CapabilityProfile profile,
        KernelSpecialization? specialization = null)
    {
        if (kernel is null) throw new ArgumentNullException(nameof(kernel));
        return Generate(kernel.Method, profile, specialization);
    }

    /// <summary>
    /// Generate the shader/binary for a kernel method against a capability profile.
    /// </summary>
    public static GeneratedKernel Generate(
        MethodInfo kernelMethod,
        CapabilityProfile profile,
        KernelSpecialization? specialization = null)
    {
        if (kernelMethod is null) throw new ArgumentNullException(nameof(kernelMethod));
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        var spec = specialization ?? KernelSpecialization.Empty;
        return profile.Backend switch
        {
            AcceleratorType.WebGPU => GenerateWebGPU(kernelMethod, profile, spec),
            AcceleratorType.Wasm => GenerateWasm(kernelMethod, profile, spec),
            AcceleratorType.WebGL => GenerateWebGL(kernelMethod, profile, spec),
            _ => throw new NotSupportedException(
                $"Offline shader generation is not supported for backend {profile.Backend}. " +
                "This feature targets the three browser transpilers (WebGPU/WebGL/Wasm)."),
        };
    }

    /// <summary>
    /// Builds an <see cref="EntryPointDescription"/> for a kernel method, detecting whether
    /// it is implicitly grouped (first parameter is an index type) or explicitly grouped.
    /// </summary>
    private static EntryPointDescription DescribeEntryPoint(MethodInfo method)
    {
        // Implicitly grouped kernels take an Index1D/2D/3D first parameter. The factory
        // throws NotSupportedException when the first parameter is not an index type - in
        // that case the kernel is explicitly grouped (uses Grid/Group intrinsics).
        try
        {
            return EntryPointDescription.FromImplicitlyGroupedKernel(method);
        }
        catch (NotSupportedException)
        {
            return EntryPointDescription.FromExplicitlyGroupedKernel(method);
        }
    }

    private static GeneratedKernel GenerateWebGPU(
        MethodInfo method,
        CapabilityProfile profile,
        KernelSpecialization spec)
    {
        // Feature set the WGSL generator branches on, derived from the profile (NOT a live
        // adapter): shader-f16 gates native vs emulated f16; subgroups gates subgroup ops.
        var features = new HashSet<string>(profile.EnabledFeatures, StringComparer.Ordinal);
        if (profile.Float16Native) features.Add("shader-f16");
        else features.Remove("shader-f16");
        if (profile.SubGroups) features.Add("subgroups");
        else features.Remove("subgroups");

        // WebGPU always emulates f64 (no native path on the backend) -> the profile's
        // Float64Mode selects the emulation. ForceDisableSubgroups mirrors !SubGroups.
        var options = WebGPUBackendOptions.Default with
        {
            F64Emulation = profile.Float64Mode,
            ForceDisableSubgroups = !profile.SubGroups,
        };

        var diagnostics = new List<GeneratedKernelDiagnostic>();

        // A minimal, device-free ILGPU context is all the transpiler needs (IR + intrinsics).
        using var context = Context.Create(b => b.Default());
        using var backend = new WebGPUBackend(context, options, features);
        if (profile.MaxNumThreadsPerGroup > 0)
            backend.DefaultMaxWorkgroupSize = profile.MaxNumThreadsPerGroup;

        var entry = DescribeEntryPoint(method);
        var compiled = (WebGPUCompiledKernel)backend.Compile(entry, spec);

        var wgsl = compiled.WGSLSource;
        var (gx, gy, gz) = compiled.CompiledWorkgroupSize;

        var metadata = new GeneratedKernelMetadata
        {
            KernelMethodName = method.DeclaringType is { } dt
                ? $"{dt.FullName}.{method.Name}"
                : method.Name,
            GroupSize = (gx, gy, gz),
        };

        return new GeneratedKernel
        {
            Backend = AcceleratorType.WebGPU,
            Profile = profile,
            Source = wgsl,
            Metadata = metadata,
            Diagnostics = diagnostics,
        };
    }

    private static GeneratedKernel GenerateWasm(
        MethodInfo method,
        CapabilityProfile profile,
        KernelSpecialization spec)
    {
        // Wasm always emulates f16 and is native f64/i64 - its codegen does not branch on a
        // negotiated adapter feature the way WebGPU does, so the binary depends only on the
        // kernel IL (worker-count and group dispatch are runtime params, not baked). No JS at
        // generate time (the JS lives in WasmAccelerator/dispatch, not WasmBackend.Compile).
        using var context = Context.Create(b => b.Default());
        using var backend = new WasmBackend(context, new WasmBackendOptions());

        var entry = DescribeEntryPoint(method);
        // WasmBackend stashes the emitted module bytes on a static after Compile (the same
        // path WasmCompileDump reads). Clear-then-read brackets this single call. (A per-kernel
        // binary accessor is a cleanliness follow-up; see task #4 determinism notes.)
        WasmBackend.LastWasmBinary = null;
        _ = backend.Compile(entry, spec);
        var binary = WasmBackend.LastWasmBinary ?? Array.Empty<byte>();

        var metadata = new GeneratedKernelMetadata
        {
            KernelMethodName = method.DeclaringType is { } dt
                ? $"{dt.FullName}.{method.Name}"
                : method.Name,
        };

        return new GeneratedKernel
        {
            Backend = AcceleratorType.Wasm,
            Profile = profile,
            Binary = binary,
            Metadata = metadata,
            Diagnostics = new List<GeneratedKernelDiagnostic>(),
        };
    }

    private static GeneratedKernel GenerateWebGL(
        MethodInfo method,
        CapabilityProfile profile,
        KernelSpecialization spec)
    {
        // WebGL emulates f16/f64/i64 and has no shared memory/atomics/barriers - the GLSL is a
        // pure function of the kernel IL. No JS at generate time.
        using var context = Context.Create(b => b.Default());
        using var backend = new WebGLBackend(context, WebGLBackendOptions.Default);

        var entry = DescribeEntryPoint(method);
        var compiled = (WebGLCompiledKernel)backend.Compile(entry, spec);

        var metadata = new GeneratedKernelMetadata
        {
            KernelMethodName = method.DeclaringType is { } dt
                ? $"{dt.FullName}.{method.Name}"
                : method.Name,
        };

        return new GeneratedKernel
        {
            Backend = AcceleratorType.WebGL,
            Profile = profile,
            Source = compiled.GLSLSource,
            Metadata = metadata,
            Diagnostics = new List<GeneratedKernelDiagnostic>(),
        };
    }
}
