// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: WebGPUBackendOptions.cs
//
// Configuration options for the WebGPU backend.
// ---------------------------------------------------------------------------------------

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// <see cref="WebGPUBackendOptions.F64ErrorTermGuard"/>: whether the WGSL Dekker / Ozaki doubles guard their error-free
    /// transforms against a reassociating shader compiler.
    /// </summary>
    public enum F64ErrorTermGuard
    {
        /// <summary>Ask the device when the accelerator is created: guard only where the unguarded transforms come out wrong.</summary>
        Auto,
        /// <summary>Always guard (correct on every compiler WGSL allows; slower where the guard is not needed).</summary>
        On,
        /// <summary>Never guard (fastest; wrong on a compiler that reassociates, e.g. Mesa).</summary>
        Off,
    }

    /// <summary>
    /// Configuration options for the WebGPU backend.
    /// </summary>
    public record WebGPUBackendOptions
    {
        /// <summary>
        /// Controls how 64-bit float (double) values are emulated.
        /// GPU hardware lacks native f64 support, so doubles are emulated using f32 pairs.
        /// Default: <see cref="F64EmulationMode.Dekker"/> (fast, good precision).
        /// </summary>
        public F64EmulationMode F64Emulation { get; init; } = F64EmulationMode.Dekker;

        /// <summary>
        /// Whether the Dekker and Ozaki doubles guard their error-free transforms against a reassociating shader compiler.
        /// WGSL lets an implementation reassociate (spec 15.7.5) and Mesa's compiler does: every error term came out 0 and
        /// a double kept only its f32 part. The guard (each rounded intermediate through an opaque zero) costs time where
        /// it is not needed (an f64 multiply-add chain on an RTX 4070 through D3D12: 0.054 ns unguarded, 0.085 guarded,
        /// 2026-11-28), so the default <see cref="F64ErrorTermGuard.Auto"/> asks the device: creating the accelerator runs
        /// the unguarded transforms once on known values, and only a device whose compiler breaks them gets the guard.
        /// No effect on <see cref="F64EmulationMode.Ieee"/> (integer arithmetic). The result is
        /// <see cref="WebGPUBackend.GuardF64ErrorTerms"/>, which can also be set later.
        /// </summary>
        public F64ErrorTermGuard F64ErrorTermGuard { get; init; } = F64ErrorTermGuard.Auto;

        /// <summary>
        /// When true, disables native subgroup operations (subgroupShuffle, subgroupBroadcastFirst, etc.)
        /// even if the GPU device supports them, and falls back to workgroup shared-memory emulation instead.
        /// Useful for verifying kernel correctness on both the native-subgroup and emulation paths.
        /// Default: false (use native subgroups when available).
        /// </summary>
        public bool ForceDisableSubgroups { get; init; } = false;

        /// <summary>
        /// Default options.
        /// </summary>
        public static WebGPUBackendOptions Default { get; } = new();
    }
}
