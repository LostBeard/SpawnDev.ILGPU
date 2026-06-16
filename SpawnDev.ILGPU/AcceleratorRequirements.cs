using System;
using System.Collections.Generic;
using System.Linq;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.OpenCL;

namespace SpawnDev.ILGPU;

/// <summary>
/// Declarative set of backend capabilities a consumer requires from an accelerator.
///
/// Wire up per-property flags based on what your kernels actually need (atomics for
/// sub-word writes, shared memory for tiled reductions, native f64 for numerics where
/// Dekker emulation is too slow, etc.). Pass an instance into
/// <see cref="AcceleratorRequirementsExtensions.EnumerateCompatibleDevices"/> or
/// <see cref="AcceleratorRequirementsExtensions.CreatePreferredAccelerator"/> and
/// backends that don't satisfy the full required set are filtered out deterministically.
///
/// Motivation: the 6-backend matrix (see <c>SpawnDev.ILGPU/CLAUDE.md</c>) is non-uniform.
/// WebGL has no atomics / shared memory / barriers; WebGPU + WebGL have no native f64/i64.
/// Consumers that silently land on an unsupported backend get subtly-wrong output (e.g.
/// SpawnDev.Codecs VP9 iDCT 4x4 kernel on WebGL: compiles, runs, returns garbage bytes
/// because sub-word writes go through atomic RMW). Making requirements explicit catches
/// this at selection time instead of after a debugging session.
///
/// This class is the SELECTION gate. The complementary kernel-compile guard - an
/// <c>UnsupportedKernelFeatureException</c> thrown when a user pins directly to an incapable
/// backend despite this mechanism - is shipped for the WebGL atomics
/// (<c>GenericAtomic</c>/<c>AtomicCAS</c>) and in-kernel group/warp Scan/Reduce classes. A
/// compile guard for <see cref="RequiresScatterStores"/> (multi-store-per-thread / scatter
/// output) is NOT yet shipped - a blunt first attempt false-positived on legitimate positional
/// multi-store + grid-stride-loop kernels and was backed out; the correct criterion is an open
/// problem, so for now that flag is selection-gate-only. For requirements without a compile-time
/// guard, a consumer that pins to a backend not meeting its own requirements surfaces the mismatch
/// downstream via silent-wrong-output or <c>CapabilityNotSupportedException</c> depending on the path.
/// </summary>
public sealed class AcceleratorRequirements
{
    /// <summary>
    /// Kernel uses <c>Atomic.*</c> operations (Add, CompareExchange, Min, Max, etc.) OR
    /// writes to sub-word views (<c>ArrayView&lt;byte&gt;</c>, <c>ArrayView&lt;short&gt;</c>,
    /// etc.) where the codegen synthesises atomic RMW. True rules out WebGL.
    /// </summary>
    public bool RequiresAtomics { get; init; }

    /// <summary>
    /// Kernel uses <c>SharedMemory.Allocate</c> / <c>SharedMemory.GetDynamic</c>.
    /// True rules out WebGL.
    /// </summary>
    public bool RequiresSharedMemory { get; init; }

    /// <summary>
    /// Kernel uses <c>Group.Barrier</c>. True rules out WebGL.
    /// </summary>
    public bool RequiresBarriers { get; init; }

    /// <summary>
    /// Kernel uses Float16 (<c>ILGPU.Half</c>). Every backend supports this (native or
    /// emulated) per the f16-emulation-plan; set to true as documentation rather than
    /// a filter. Use <see cref="RequiresFloat16Native"/> to rule out emulated paths.
    /// </summary>
    public bool RequiresFloat16 { get; init; }

    /// <summary>
    /// Kernel must run on NATIVE Float16 hardware. Rules out backends where Float16 is
    /// emulated: WebGL (always emulated), Wasm (always emulated), WebGPU without the
    /// <c>shader-f16</c> feature, OpenCL devices without <c>cl_khr_fp16</c>. Leaves
    /// CPU / CUDA and native-capable WebGPU / OpenCL.
    /// </summary>
    public bool RequiresFloat16Native { get; init; }

    /// <summary>
    /// Kernel uses BFloat16 (<c>ILGPU.BFloat16</c>, "brain float"). Every backend supports it -
    /// always emulated (top-16-bits-of-fp32 + round-to-nearest-even), with native <c>cvt.*.bf16</c>
    /// on CUDA sm_80+ used only at the load/store boundary. There is no native-vs-emulated split
    /// (no hardware has native bf16 arithmetic), so this is a no-op documentation filter like
    /// <see cref="RequiresFloat16"/> - it never rules out a backend.
    /// </summary>
    public bool RequiresBFloat16 { get; init; }

    /// <summary>
    /// Kernel uses Float64 (<c>double</c>). True is compatible with every backend - WebGPU
    /// and WebGL run Float64 through Dekker emulation (see <c>CLAUDE.md</c>). Use
    /// <see cref="RequiresFloat64Native"/> to rule out emulated paths when the performance
    /// hit is unacceptable or exact IEEE 754 semantics matter.
    /// </summary>
    public bool RequiresFloat64 { get; init; }

    /// <summary>
    /// Kernel must run on NATIVE Float64 hardware. Rules out WebGPU and WebGL (emulated
    /// via Dekker). Leaves CPU / CUDA / OpenCL / Wasm.
    /// </summary>
    public bool RequiresFloat64Native { get; init; }

    /// <summary>
    /// Kernel requires strict IEEE 754 double-precision arithmetic.
    ///
    /// Distinct from <see cref="RequiresFloat64Native"/>: every backend can satisfy strict
    /// f64, just not all at the same speed.
    ///   - CPU / CUDA / OpenCL / Wasm: strict f64 is native; accepted unchanged.
    ///   - WebGPU / WebGL: strict f64 requires <see cref="F64EmulationMode.Ozaki"/>
    ///     (full IEEE 754, vec4&lt;f32&gt;). The default <see cref="F64EmulationMode.Dekker"/>
    ///     (~48-53 bit mantissa, vec2&lt;f32&gt;) is REJECTED because it diverges from
    ///     IEEE 754 53-bit semantics.
    ///
    /// <para>v1 shipping behavior (4.9.2-rc.23): this flag filters at backend-selection
    /// time. WebGPU / WebGL are accepted only when the backend was registered with
    /// <c>F64Emulation = Ozaki</c> (via <c>WebGPUBackendOptions</c> /
    /// <c>WebGLBackendOptions</c> at <c>Context.Create</c> time). If the backend is in
    /// Dekker or Disabled mode, the device is filtered out under this flag.</para>
    ///
    /// <para>v2 (planned): a settable <c>F64Mode</c> property on
    /// <c>WebGPUAccelerator</c> / <c>WebGLAccelerator</c> will let
    /// <see cref="AcceleratorRequirementsExtensions.CreatePreferredAccelerator"/> promote
    /// a Dekker-registered backend to Ozaki on demand. v3 extends the same shape across
    /// the P2P wire (peer reports supported modes, dispatch carries requested mode). See
    /// <c>Plans/accelerator-requirements-mode-flags.md</c>.</para>
    ///
    /// <para>This is a CORRECTNESS CONTRACT, not a preference. A consumer that sets this
    /// flag and dispatches to a Dekker backend gets silently-wrong numbers. Setting the
    /// flag tells the runtime to refuse such backends rather than ship wrong precision.</para>
    /// </summary>
    public bool RequiresFloat64Strict { get; init; }

    /// <summary>
    /// Kernel uses Int64 (<c>long</c>). True is compatible with every backend (emulated
    /// on WebGPU/WebGL). Use <see cref="RequiresInt64Native"/> for no-emulation.
    /// </summary>
    public bool RequiresInt64 { get; init; }

    /// <summary>
    /// Kernel must run on NATIVE Int64 hardware. Rules out WebGPU and WebGL (emulated).
    /// </summary>
    public bool RequiresInt64Native { get; init; }

    /// <summary>
    /// Kernel uses 64-bit atomic operations (<c>Atomic.Add(ref long, long)</c> etc.).
    /// Rules out WebGL (no atomics at all) and OpenCL devices without the
    /// <c>cl_khr_int64_base_atomics</c> + <c>cl_khr_int64_extended_atomics</c> extensions.
    /// </summary>
    public bool RequiresInt64Atomics { get; init; }

    /// <summary>
    /// Kernel uses <c>Warp.Shuffle</c> / <c>Group.Broadcast</c> or other subgroup primitives.
    /// Rules out WebGL, Wasm, CPU. WebGPU and OpenCL are device-dependent (need the adapter
    /// feature / Khronos subgroup extension); CUDA always supports it.
    /// </summary>
    public bool RequiresSubGroups { get; init; }

    /// <summary>
    /// Kernel writes more than one element of the same output buffer per thread, or writes to a
    /// computed/scattered output index (<c>out[someComputedIndex] = ...</c>, e.g. a per-thread
    /// loop filling several output elements, an in-place butterfly writing two slots, or any
    /// <c>dst[perm[i]] = src[i]</c> reorder). True rules out WebGL.
    ///
    /// <para>WebGL's Transform-Feedback model captures exactly ONE output record per vertex
    /// (thread) and the readback places it at the thread's own slot, so in-kernel scatter /
    /// multi-element-per-thread writes are structurally impossible - they silently drop every
    /// store but the thread's own (this is the failure class the WebGL codegen now throws
    /// <c>UnsupportedKernelFeatureException</c> for at compile time). Every other backend
    /// (WebGPU, Wasm, CUDA, OpenCL, CPU) supports arbitrary in-kernel stores.</para>
    ///
    /// <para>Note: WebGL CAN still scatter at the HOST/algorithm layer via the render-to-texture
    /// path (<c>WebGLAccelerator.Scatter</c> / <c>IScatterProvider</c>, used by RadixSort) - this
    /// flag is specifically about IN-KERNEL stores a consumer writes in their own kernel body.</para>
    /// </summary>
    public bool RequiresScatterStores { get; init; }

    /// <summary>
    /// No requirements - every backend passes. Equivalent to <c>new AcceleratorRequirements()</c>.
    /// </summary>
    public static AcceleratorRequirements None { get; } = new();
}

/// <summary>
/// Extension methods that bridge <see cref="AcceleratorRequirements"/> into the ILGPU
/// device enumeration + accelerator creation paths.
/// </summary>
public static class AcceleratorRequirementsExtensions
{
    /// <summary>
    /// Returns every device on the context whose backend satisfies the given requirements,
    /// in the context's native device order (unchanged from <see cref="Context.Devices"/>).
    /// Never throws - callers that need a single pick call
    /// <see cref="CreatePreferredAccelerator"/> instead.
    /// </summary>
    public static IReadOnlyList<Device> EnumerateCompatibleDevices(
        this Context context, AcceleratorRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirements);
        return context.Devices.Where(d => d.Satisfies(requirements)).ToList();
    }

    /// <summary>
    /// Create an accelerator on the first compatible device (preferring non-CPU when
    /// multiple match, mirroring <see cref="Context.GetPreferredDevice"/> behavior).
    /// Throws <see cref="NotSupportedException"/> when no backend satisfies the
    /// requirements - the caller's kernel genuinely can't run on this host.
    /// </summary>
    public static Accelerator CreatePreferredAccelerator(
        this Context context, AcceleratorRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirements);
        var compatible = context.EnumerateCompatibleDevices(requirements);
        if (compatible.Count == 0)
        {
            throw new NotSupportedException(
                $"No compatible accelerator found for requirements: {requirements.Describe()}. " +
                $"Available devices: {string.Join(", ", context.Devices.Select(d => d.AcceleratorType))}.");
        }
        // Prefer non-CPU when a GPU backend is compatible, else fall back to CPU.
        var preferred = compatible.FirstOrDefault(d => d.AcceleratorType != AcceleratorType.CPU)
                        ?? compatible[0];
        return preferred.CreateAccelerator(context);
    }

    /// <summary>
    /// True when the device's backend supports every capability flag set on the
    /// requirements instance. Lookup is constant-time per flag.
    /// </summary>
    public static bool Satisfies(this Device device, AcceleratorRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(requirements);
        var backend = device.AcceleratorType;

        if (requirements.RequiresAtomics && !HasAtomics(backend)) return false;
        if (requirements.RequiresSharedMemory && !HasSharedMemory(backend)) return false;
        if (requirements.RequiresBarriers && !HasBarriers(backend)) return false;
        if (requirements.RequiresFloat16 && !HasFloat16(device)) return false;
        if (requirements.RequiresFloat16Native && !HasFloat16Native(device)) return false;
        if (requirements.RequiresBFloat16 && !HasBFloat16(device)) return false;
        if (requirements.RequiresFloat64 && !HasFloat64(device)) return false;
        if (requirements.RequiresFloat64Native && !HasFloat64Native(device)) return false;
        if (requirements.RequiresFloat64Strict && !HasFloat64Strict(device)) return false;
        if (requirements.RequiresInt64 && !HasInt64(backend)) return false;
        if (requirements.RequiresInt64Native && !HasInt64Native(backend)) return false;
        if (requirements.RequiresInt64Atomics && !HasInt64Atomics(device)) return false;
        if (requirements.RequiresSubGroups && !HasSubGroups(device)) return false;
        if (requirements.RequiresScatterStores && !HasScatterStores(backend)) return false;
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Backend capability matrix. Per-feature logic lives here so the CLAUDE.md
    // table and this file stay aligned in one place. All methods are pure + cheap.

    private static bool HasAtomics(AcceleratorType backend) => backend switch
    {
        AcceleratorType.WebGL => false,
        _ => true,
    };

    private static bool HasSharedMemory(AcceleratorType backend) => backend switch
    {
        AcceleratorType.WebGL => false,
        _ => true,
    };

    private static bool HasBarriers(AcceleratorType backend) => backend switch
    {
        AcceleratorType.WebGL => false,
        _ => true,
    };

    // In-kernel scatter / multi-element-per-thread output stores. WebGL Transform-Feedback
    // captures one output record per vertex at the thread's own slot, so a kernel that writes
    // a computed output index or more than one element per thread is structurally unsupported
    // (the WebGL codegen throws UnsupportedKernelFeatureException for it). Every other backend
    // supports arbitrary in-kernel stores.
    private static bool HasScatterStores(AcceleratorType backend) => backend switch
    {
        AcceleratorType.WebGL => false,
        _ => true,
    };

    private static bool HasFloat16(Device device)
        => device.Capabilities?.Float16 ?? true; // every shipped backend supports Float16 via emulation

    // Every backend supports BFloat16 (always emulated; native cvt only at the load/store
    // boundary on CUDA). No native-vs-emulated split, so this is always true.
    private static bool HasBFloat16(Device device) => true;

    private static bool HasFloat16Native(Device device)
    {
        // Native-or-nothing: CUDA (SM_53+), OpenCL w/ cl_khr_fp16. WebGPU with shader-f16
        // is harder to detect pre-accelerator here; treat as non-native for the selection
        // gate (worst case, caller gets a CPU/CUDA fallback; pinning to WebGPU directly
        // still works when the adapter has shader-f16).
        if (device.Capabilities is CLCapabilityContext cl)
            return cl.Float16Native;
        return device.AcceleratorType == AcceleratorType.CPU
            || device.AcceleratorType == AcceleratorType.Cuda;
    }

    private static bool HasFloat64(Device device) => device.AcceleratorType switch
    {
        // Dekker emulation covers WebGPU + WebGL (per CLAUDE.md feature matrix).
        AcceleratorType.WebGPU => true,
        AcceleratorType.WebGL => true,
        AcceleratorType.OpenCL => device.Capabilities is CLCapabilityContext cl ? cl.Float64 : true,
        _ => true,
    };

    private static bool HasFloat64Native(Device device) => device.AcceleratorType switch
    {
        AcceleratorType.WebGPU => false,
        AcceleratorType.WebGL => false,
        AcceleratorType.OpenCL => device.Capabilities is CLCapabilityContext cl && cl.Float64,
        _ => true,
    };

    // Strict IEEE 754 f64. Native-f64 backends always satisfy. WebGPU/WebGL devices
    // are accepted at the device level - the actual switch to Ozaki happens at
    // accelerator-create time inside CreatePreferredAcceleratorAsync, which passes
    // F64Emulation = Ozaki when RequiresFloat64Strict is set. The user can also
    // pre-configure Ozaki by passing options to CreateAcceleratorAsync directly.
    private static bool HasFloat64Strict(Device device) => device.AcceleratorType switch
    {
        AcceleratorType.OpenCL => device.Capabilities is CLCapabilityContext cl && cl.Float64,
        // Every other backend can satisfy strict f64: native on CPU/CUDA/Wasm,
        // via Ozaki on WebGPU/WebGL.
        _ => true,
    };

    private static bool HasInt64(AcceleratorType backend) => true; // emulated everywhere needed

    private static bool HasInt64Native(AcceleratorType backend) => backend switch
    {
        AcceleratorType.WebGPU => false,
        AcceleratorType.WebGL => false,
        _ => true,
    };

    private static bool HasInt64Atomics(Device device) => device.AcceleratorType switch
    {
        AcceleratorType.WebGL => false,
        AcceleratorType.OpenCL => device.Capabilities is CLCapabilityContext cl && cl.Int64_Atomics,
        _ => true,
    };

    private static bool HasSubGroups(Device device) => device.AcceleratorType switch
    {
        AcceleratorType.WebGL => false,
        AcceleratorType.Wasm => false,
        AcceleratorType.CPU => false,
        AcceleratorType.OpenCL => device.Capabilities is CLCapabilityContext cl && cl.SubGroups,
        // WebGPU adapter feature detection would need the adapter instance; treat as
        // compatible here and let the actual dispatch fail if the adapter lacks it.
        _ => true,
    };

    /// <summary>Short human-readable summary for diagnostics.</summary>
    public static string Describe(this AcceleratorRequirements r)
    {
        var flags = new List<string>();
        if (r.RequiresAtomics) flags.Add("Atomics");
        if (r.RequiresSharedMemory) flags.Add("SharedMemory");
        if (r.RequiresBarriers) flags.Add("Barriers");
        if (r.RequiresFloat16) flags.Add("Float16");
        if (r.RequiresFloat16Native) flags.Add("Float16Native");
        if (r.RequiresBFloat16) flags.Add("BFloat16");
        if (r.RequiresFloat64) flags.Add("Float64");
        if (r.RequiresFloat64Native) flags.Add("Float64Native");
        if (r.RequiresFloat64Strict) flags.Add("Float64Strict");
        if (r.RequiresInt64) flags.Add("Int64");
        if (r.RequiresInt64Native) flags.Add("Int64Native");
        if (r.RequiresInt64Atomics) flags.Add("Int64Atomics");
        if (r.RequiresSubGroups) flags.Add("SubGroups");
        if (r.RequiresScatterStores) flags.Add("ScatterStores");
        return flags.Count == 0 ? "(none)" : string.Join(", ", flags);
    }
}
