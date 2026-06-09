using System;
using System.Collections.Generic;
using System.Linq;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU;

/// <summary>
/// A serializable, device-INDEPENDENT description of the capabilities a backend's code
/// generator branches on. It is the single source of truth for every capability decision
/// the WGSL / GLSL / Wasm transpilers make, so that shader/binary generation can run with
/// NO real device on any host OS (build servers, CI, a dev box without WebGPU).
///
/// This is the foundation of the precompiled-shaders feature (see
/// <c>Plans/precompiled-shaders.md</c>). Layer 1 (<see cref="ShaderCompiler"/>) feeds a
/// <see cref="CapabilityProfile"/> into the existing generators instead of a live adapter;
/// the runtime path builds a profile from its real device and feeds the SAME generators, so
/// the offline and runtime code paths are unified through this one type.
///
/// DESIGN RULE (structural byte-identical guard): the code generators MUST read every
/// capability they branch on from a <see cref="CapabilityProfile"/> and NEVER directly from
/// a live <c>Accelerator</c>/adapter. Then the profile is the only cap source by
/// construction, and an artifact can never silently depend on an un-profiled capability. A
/// guard test enforces "no direct device-capability access on the generator code path".
///
/// VERSIONING: <see cref="SchemaVersion"/> is part of any cache key. Bump
/// <see cref="CurrentSchemaVersion"/> whenever the generators gain a NEW capability branch,
/// so artifacts produced before that branch existed are treated as a cache miss (never
/// trusted) rather than silently reused.
/// </summary>
public sealed record CapabilityProfile
{
    /// <summary>
    /// The current profile schema version. Bump when the generators add a capability
    /// branch (a new field here). Old artifacts keyed at a lower version are ignored.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Which backend's generator this profile targets. Reuses ILGPU's
    /// <see cref="AcceleratorType"/> (extended by SpawnDev with WebGPU / WebGL / Wasm) so we
    /// do not introduce a parallel backend enum that could drift.
    /// </summary>
    public required AcceleratorType Backend { get; init; }

    /// <summary>
    /// Human-readable profile name, used in artifact paths and diagnostics
    /// (e.g. "Chrome-WebGPU-f16", "WebGL2-Baseline", "WasmDefault"). Not semantically
    /// significant - the generators branch on the capability fields, not the name.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Schema version this profile was authored against. Defaults to
    /// <see cref="CurrentSchemaVersion"/>. Part of the artifact cache key.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    // ---- Float16 ----

    /// <summary>
    /// True when the device exposes NATIVE 16-bit float (WebGPU adapter feature
    /// <c>shader-f16</c>; OpenCL <c>cl_khr_fp16</c>). This is the GATE: when true the
    /// generator emits the native f16 path; when false it emits the emulated path
    /// (<c>_f16_to_f32</c>/<c>_f32_to_f16</c> helpers + packed storage). Always false on
    /// WebGL and Wasm (both always emulate f16).
    /// </summary>
    public bool Float16Native { get; init; }

    // ---- Float64 ----

    /// <summary>
    /// True when the device has NATIVE 64-bit float (desktop CUDA/OpenCL/CPU). The GATE for
    /// f64: when true the generator emits native f64 and <see cref="Float64Mode"/> is
    /// IGNORED; when false it emits the emulation selected by <see cref="Float64Mode"/>.
    /// Always false on WebGPU and WebGL (both emulate f64).
    /// </summary>
    public bool Float64Native { get; init; }

    /// <summary>
    /// Which f64 emulation the generator emits. ONLY MEANINGFUL when
    /// <see cref="Float64Native"/> is false. Reuses the existing
    /// <see cref="F64EmulationMode"/> unchanged (no <c>Native</c> value is added to that
    /// enum - native is the absence of emulation, expressed by
    /// <see cref="Float64Native"/>=true).
    /// </summary>
    public F64EmulationMode Float64Mode { get; init; } = F64EmulationMode.Dekker;

    // ---- Int64 ----

    /// <summary>
    /// True when the device has native 64-bit integers (desktop). False on WebGPU/WebGL
    /// (both emulate i64 as <c>vec2&lt;u32&gt;</c>). Wasm is native i64.
    /// </summary>
    public bool Int64Native { get; init; }

    // ---- Subgroups / warp ----

    /// <summary>
    /// True when the backend exposes subgroup / warp operations (WebGPU subgroups, Wasm
    /// emulated warps, CUDA/OpenCL hardware warps). Drives whether the generator emits
    /// subgroup intrinsics vs the shared-memory fallback.
    /// </summary>
    public bool SubGroups { get; init; }

    /// <summary>
    /// Logical warp/subgroup width the generator assumes (Wasm = 8, WebGPU adapter-reported,
    /// CUDA = 32). 1 means "no real warp" (scalar). Affects warp-shuffle codegen.
    /// </summary>
    public int WarpSize { get; init; } = 1;

    // ---- Limits ----

    /// <summary>
    /// Maximum threads per group the generator may bake into a workgroup-size declaration
    /// (Wasm 256, WebGPU device-reported). 0 = unspecified (use backend default).
    /// </summary>
    public int MaxNumThreadsPerGroup { get; init; }

    /// <summary>
    /// Maximum storage-buffer bindings per shader stage (WebGPU; Chrome = 10). The generator
    /// throws if a kernel needs more bindings than this. 0 = unspecified (use backend
    /// default). NOTE: an artifact built against a value N is valid on a device with a
    /// HIGHER limit, but runtime profile-matching is EXACT-equality in v1 (threshold
    /// relaxation is a later, separately-tested optimization).
    /// </summary>
    public int MaxStorageBufferBindings { get; init; }

    // ---- Raw feature set (forward-compat) ----

    /// <summary>
    /// The raw set of device/adapter feature strings (e.g. "shader-f16", "subgroups",
    /// "cl_khr_fp16"). The high-level bool fields above are the canonical gates the
    /// generators branch on; this set is retained for forward-compatibility and for
    /// features not yet promoted to a typed field. Codegen that consults a named feature
    /// must do so through <see cref="HasFeature"/> (NOT a live adapter).
    /// </summary>
    public IReadOnlySet<string> EnabledFeatures { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>True if the named device/adapter feature is present in this profile.</summary>
    public bool HasFeature(string feature) => EnabledFeatures.Contains(feature);

    /// <summary>
    /// A stable, deterministic identity for this profile, suitable for inclusion in an
    /// artifact cache key. Order-independent over <see cref="EnabledFeatures"/> (sorted) so
    /// two equal profiles always hash identically. Deterministic by construction - no
    /// <see cref="object.GetHashCode"/>, no dictionary iteration order.
    /// </summary>
    public string ToCacheKeyString()
    {
        var features = EnabledFeatures.OrderBy(f => f, StringComparer.Ordinal);
        return string.Join('|',
            $"v{SchemaVersion}",
            Backend.ToString(),
            $"f16n={(Float16Native ? 1 : 0)}",
            $"f64n={(Float64Native ? 1 : 0)}",
            $"f64m={Float64Mode}",
            $"i64n={(Int64Native ? 1 : 0)}",
            $"sg={(SubGroups ? 1 : 0)}",
            $"warp={WarpSize}",
            $"maxtg={MaxNumThreadsPerGroup}",
            $"maxsb={MaxStorageBufferBindings}",
            $"feat={string.Join(',', features)}");
    }
}
