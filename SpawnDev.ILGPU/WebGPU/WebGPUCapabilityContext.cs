using ILGPU.Runtime;

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// Represents the capabilities of the WebGPU backend.
    /// Capabilities are initialized conservatively and updated when the accelerator
    /// is created with the actual enabled device features.
    /// </summary>
    public sealed class WebGPUCapabilityContext : CapabilityContext
    {
        /// <summary>
        /// Creates a new WebGPU capability context.
        /// Float16 is always supported (native when the `shader-f16` device feature is
        /// enabled, otherwise emulated via inline IEEE 754 bit conversion in the WGSL
        /// codegen — see `WGSLEmulationLibrary.F16Functions`). Float64 and Int64 are
        /// conservative defaults until UpdateFromEnabledFeatures is called.
        /// </summary>
        public WebGPUCapabilityContext()
        {
            // Float16 is always available: native when shader-f16 enabled, emulated otherwise.
            Float16 = true;
            // Conservative defaults until UpdateFromEnabledFeatures is called
            Float16Native = false;
            Float64 = false;
            Int64 = false;
        }

        /// <summary>
        /// Updates capabilities based on the WebGPU device's actually enabled features.
        /// Called by WebGPUAccelerator after device creation.
        /// </summary>
        /// <param name="enabledFeatures">The set of features successfully enabled on the device (e.g. from adapter.requestDevice).</param>
        internal void UpdateFromEnabledFeatures(HashSet<string> enabledFeatures)
        {
            if (enabledFeatures == null) return;
            // Float16 stays true regardless — emulation covers the !shader-f16 case.
            // Float16Native tracks whether the device exposes native `shader-f16`.
            Float16Native = enabledFeatures.Contains("shader-f16");
            Float64 = enabledFeatures.Contains("shader-float64");
            Int64 = enabledFeatures.Contains("shader-int64");
            PackedInt8DotProduct = !Backend.WebGPUBackend.ForceEmulatedPackedInt8Dot
                && enabledFeatures.Contains(Backend.WebGPUBackend.PackedInt8DotLanguageFeature);
        }

        /// <summary>
        /// True when the WebGPU device has the native `shader-f16` feature enabled
        /// (Float16 arithmetic and storage are hardware-accelerated). False means
        /// Float16 is emulated in WGSL via f32 arithmetic plus inline IEEE 754 bit
        /// conversion at buffer load/store boundaries — still exact (f16 is a strict
        /// subset of f32) but with additional ALU cost per load/store.
        /// </summary>
        public bool Float16Native { get; internal set; }

        /// <summary>
        /// Supports Float64 (double) data type.
        /// </summary>
        public bool Float64 { get; internal set; }

        /// <summary>
        /// Supports Int64 (long) data type.
        /// </summary>
        public bool Int64 { get; internal set; }
    }
}
