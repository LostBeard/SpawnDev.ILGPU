using System;
using System.Collections.Generic;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;

namespace SpawnDev.ILGPU;

/// <summary>
/// Curated named <see cref="CapabilityProfile"/> presets for the three browser backends,
/// plus a registry (name -> profile) used to resolve the <c>Profile = "..."</c> name on a
/// <c>[PrecompiledKernel]</c> attribute, and a best-effort snapshot of a live device.
///
/// Preset values are derived from the 6-backend feature matrix (see
/// <c>SpawnDev.ILGPU/CLAUDE.md</c> and the WebGPU/Wasm/WebGL <c>CLAUDE.md</c> files):
/// WebGPU emulates f64/i64 and may have native f16 (adapter <c>shader-f16</c>); WebGL
/// emulates f16/f64/i64 and has no shared memory/atomics/barriers; Wasm has native f64/i64
/// and emulated f16, with 8-wide emulated warps and 256 threads/group.
/// </summary>
public static class CapabilityProfiles
{
    // WebGPU presets are named by CAPABILITY, not browser - the profile keys on what the
    // adapter exposes, never on Chrome vs Firefox. WebGPU/WGSL is a W3C standard, so the
    // emitted shader is the same target across browsers; what differs is the FEATURE/LIMIT
    // set (notably `subgroups` - Chrome shipped it ahead of Firefox - and `shader-f16`
    // availability, plus limits like maxStorageBuffersPerShaderStage = 10 on Chrome vs the
    // spec floor of 8). Two browsers exposing the same caps share a profile (and an
    // artifact); a device lacking a cap is simply a different point in this space and the
    // exact-match cache falls back to runtime generation. Use FromAccelerator() to snapshot
    // a real device (Chrome, Firefox, or anything else) rather than guessing.

    /// <summary>
    /// WebGPU with BOTH native <c>shader-f16</c> AND <c>subgroups</c> (a modern Chrome-class
    /// adapter, and any Firefox/other device that exposes the same two features). 10 storage
    /// buffer bindings (Chrome's reported limit).
    /// </summary>
    public static readonly CapabilityProfile WebGPUFull = new()
    {
        Backend = AcceleratorType.WebGPU,
        Name = "WebGPU-f16-subgroups",
        Float16Native = true,
        Float64Native = false,
        Float64Mode = F64EmulationMode.Dekker,
        Int64Native = false,
        SubGroups = true,
        WarpSize = 0, // adapter-reported; 0 = let backend default when not pinned
        MaxNumThreadsPerGroup = 256,
        MaxStorageBufferBindings = 10,
        EnabledFeatures = new HashSet<string>(StringComparer.Ordinal) { "shader-f16", "subgroups" },
    };

    /// <summary>
    /// WebGPU with native <c>shader-f16</c> but NO <c>subgroups</c> - the typical Firefox-class
    /// point today (Firefox shipped WebGPU but subgroup support lags Chrome). Subgroup ops
    /// lower to the shared-memory fallback. Conservative 8-binding spec floor.
    /// </summary>
    public static readonly CapabilityProfile WebGPUNoSubgroups = new()
    {
        Backend = AcceleratorType.WebGPU,
        Name = "WebGPU-f16-noSubgroups",
        Float16Native = true,
        Float64Native = false,
        Float64Mode = F64EmulationMode.Dekker,
        Int64Native = false,
        SubGroups = false,
        WarpSize = 1,
        MaxNumThreadsPerGroup = 256,
        MaxStorageBufferBindings = 8,
        EnabledFeatures = new HashSet<string>(StringComparer.Ordinal) { "shader-f16" },
    };

    /// <summary>
    /// WebGPU lowest-common-denominator: NO <c>shader-f16</c>, NO <c>subgroups</c>, spec-floor
    /// 8 bindings. The safest artifact - emulated f16 + shared-memory subgroup fallback work
    /// everywhere WebGPU runs. Matches the broadest set of devices/browsers.
    /// </summary>
    public static readonly CapabilityProfile WebGPUBaseline = new()
    {
        Backend = AcceleratorType.WebGPU,
        Name = "WebGPU-baseline",
        Float16Native = false,
        Float64Native = false,
        Float64Mode = F64EmulationMode.Dekker,
        Int64Native = false,
        SubGroups = false,
        WarpSize = 1,
        MaxNumThreadsPerGroup = 256,
        MaxStorageBufferBindings = 8,
        EnabledFeatures = new HashSet<string>(StringComparer.Ordinal),
    };

    /// <summary>WebGL2 baseline: no shared memory/atomics/barriers, all of f16/f64/i64 emulated.</summary>
    public static readonly CapabilityProfile WebGL2Baseline = new()
    {
        Backend = AcceleratorType.WebGL,
        Name = "WebGL2-Baseline",
        Float16Native = false,
        Float64Native = false,
        Float64Mode = F64EmulationMode.Dekker,
        Int64Native = false,
        SubGroups = false,
        WarpSize = 1,
        MaxNumThreadsPerGroup = 0,
        MaxStorageBufferBindings = 0,
        EnabledFeatures = new HashSet<string>(StringComparer.Ordinal),
    };

    /// <summary>
    /// Wasm default: native f64 + native i64, emulated f16, 8-wide emulated warps,
    /// 256 threads/group (multi-worker SharedArrayBuffer dispatch).
    /// </summary>
    public static readonly CapabilityProfile WasmDefault = new()
    {
        Backend = AcceleratorType.Wasm,
        Name = "WasmDefault",
        Float16Native = false,
        Float64Native = true,
        Float64Mode = F64EmulationMode.Disabled, // ignored (native), set explicit for clarity
        Int64Native = true,
        SubGroups = true,
        WarpSize = 8,
        MaxNumThreadsPerGroup = 256,
        MaxStorageBufferBindings = 0,
        EnabledFeatures = new HashSet<string>(StringComparer.Ordinal),
    };

    private static readonly Dictionary<string, CapabilityProfile> Registry =
        new(StringComparer.Ordinal)
        {
            [WebGPUFull.Name] = WebGPUFull,
            [WebGPUNoSubgroups.Name] = WebGPUNoSubgroups,
            [WebGPUBaseline.Name] = WebGPUBaseline,
            [WebGL2Baseline.Name] = WebGL2Baseline,
            [WasmDefault.Name] = WasmDefault,
        };

    /// <summary>All built-in presets.</summary>
    public static IReadOnlyCollection<CapabilityProfile> All => Registry.Values;

    /// <summary>
    /// Resolve a profile by name (the <c>Profile = "..."</c> on a <c>[PrecompiledKernel]</c>
    /// attribute, or a <c>.csproj</c> profile list). Returns null if the name is unknown.
    /// </summary>
    public static CapabilityProfile? Resolve(string name) =>
        Registry.TryGetValue(name, out var p) ? p : null;

    /// <summary>
    /// Register a custom profile by name (e.g. a project-defined profile). Overwrites an
    /// existing entry with the same name.
    /// </summary>
    public static void Register(CapabilityProfile profile) => Registry[profile.Name] = profile;

    /// <summary>
    /// Best-effort snapshot of a LIVE accelerator's codegen-relevant capabilities - the
    /// boundary where a developer ON the target hardware captures an exact profile for
    /// build-time precompile. This is the ONLY sanctioned place to read capabilities off a
    /// real device into a profile; the code generators themselves must read ONLY from the
    /// profile (the structural byte-identical guard).
    ///
    /// For WebGPU it reads the backend's EFFECTIVE capabilities (`HasShaderF16`/`HasSubgroups`/
    /// `F64Mode`), so a force-disabled subgroups or force-emulated f16 configuration is captured
    /// faithfully (the offline artifact then matches that backend byte-for-byte). For other
    /// backends f64/i64-native are per-backend (Wasm native; WebGL emulated) and f16-native comes
    /// from <paramref name="enabledFeatures"/> (OpenCL <c>cl_khr_fp16</c>).
    /// </summary>
    public static CapabilityProfile FromAccelerator(
        Accelerator accelerator,
        IReadOnlySet<string>? enabledFeatures = null)
    {
        if (accelerator is null) throw new ArgumentNullException(nameof(accelerator));
        var type = accelerator.AcceleratorType;
        var features = enabledFeatures ?? new HashSet<string>(StringComparer.Ordinal);

        bool f64Native = type switch
        {
            AcceleratorType.Wasm or AcceleratorType.Cuda or
            AcceleratorType.OpenCL or AcceleratorType.CPU => true,
            _ => false, // WebGPU / WebGL emulate f64
        };
        bool i64Native = type switch
        {
            AcceleratorType.Wasm or AcceleratorType.Cuda or
            AcceleratorType.OpenCL or AcceleratorType.CPU => true,
            _ => false, // WebGPU / WebGL emulate i64
        };

        bool f16Native;
        bool subGroups;
        var f64Mode = F64EmulationMode.Dekker;
        var effectiveFeatures = new HashSet<string>(StringComparer.Ordinal);

        // WebGPU: snapshot the backend's EFFECTIVE capabilities, NOT the raw adapter feature set.
        // The backend can force-disable subgroups (ForceDisableSubgroups) or force-emulate f16
        // (ForceEmulatedF16) even when the adapter exposes them - and the generators branch on the
        // EFFECTIVE values (HasSubgroups / HasShaderF16). Reading the raw features would make an
        // offline artifact diverge from a backend that disabled a feature (verified by the
        // PrecompiledShaders_OfflineWGSL_MatchesRuntime guard on the no-subgroups lane).
        if (accelerator is WebGPUAccelerator wgpu)
        {
            f16Native = wgpu.Backend.HasShaderF16;
            subGroups = wgpu.Backend.HasSubgroups;
            f64Mode = wgpu.Backend.F64Mode;
            if (f16Native) effectiveFeatures.Add("shader-f16");
            if (subGroups) effectiveFeatures.Add("subgroups");
        }
        else
        {
            f16Native = type switch
            {
                AcceleratorType.OpenCL => features.Contains("cl_khr_fp16"),
                AcceleratorType.Cuda or AcceleratorType.CPU => true,
                _ => false, // WebGL / Wasm emulate f16
            };
            subGroups = accelerator.WarpSize > 1;
            foreach (var f in features) effectiveFeatures.Add(f);
        }

        return new CapabilityProfile
        {
            Backend = type,
            Name = $"{type}-snapshot",
            Float16Native = f16Native,
            Float64Native = f64Native,
            Float64Mode = f64Mode,
            Int64Native = i64Native,
            SubGroups = subGroups,
            WarpSize = subGroups ? accelerator.WarpSize : 1,
            MaxNumThreadsPerGroup = accelerator.MaxNumThreadsPerGroup,
            MaxStorageBufferBindings = 0, // a dispatch-time limit; not a codegen input
            EnabledFeatures = effectiveFeatures,
        };
    }
}
