using System;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU;

/// <summary>
/// Thrown at kernel compile time when a kernel uses a feature the target backend cannot
/// implement bit-exactly (e.g. sub-word atomic write on WebGL, 64-bit CAS on WebGPU).
///
/// Replaces the prior silent-wrong-output mode where an unsupported intrinsic produced
/// a working kernel that returned garbage on the affected backend. Consumers catching
/// this can either switch backends via <see cref="AcceleratorRequirements"/> or restructure
/// the kernel to avoid the unsupported feature.
///
/// Carries the intrinsic name + target backend + remediation hint in the message so
/// the failure mode is self-documenting.
/// </summary>
public class UnsupportedKernelFeatureException : NotSupportedException
{
    /// <summary>The kernel feature that's unsupported on the target backend.</summary>
    public string Feature { get; }

    /// <summary>The target backend that rejected the feature.</summary>
    public AcceleratorType Backend { get; }

    /// <summary>Suggested remediation (which backend to switch to, or how to restructure).</summary>
    public string Remediation { get; }

    /// <summary>
    /// Creates the exception. The message is
    /// "Kernel feature '<paramref name="feature"/>' is not supported on the <paramref name="backend"/> backend."
    /// followed by <paramref name="remediation"/>.
    /// </summary>
    /// <param name="feature">The unsupported kernel feature (for example the intrinsic name).</param>
    /// <param name="backend">The backend that rejected the feature.</param>
    /// <param name="remediation">Suggested fix, appended to the message.</param>
    public UnsupportedKernelFeatureException(string feature, AcceleratorType backend, string remediation)
        : base($"Kernel feature '{feature}' is not supported on the {backend} backend. {remediation}")
    {
        Feature = feature;
        Backend = backend;
        Remediation = remediation;
    }
}
