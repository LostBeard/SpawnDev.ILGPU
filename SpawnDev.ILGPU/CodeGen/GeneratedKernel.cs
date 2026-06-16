using System;
using System.Collections.Generic;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU;

/// <summary>
/// Severity of a code-generation diagnostic.
/// </summary>
public enum GeneratedKernelDiagnosticSeverity
{
    /// <summary>Informational note; not a problem.</summary>
    Info,
    /// <summary>A potential problem that does not prevent compilation.</summary>
    Warning,
    /// <summary>A fatal problem; the artifact would fail to compile on a real device.</summary>
    Error,
}

/// <summary>
/// A single diagnostic produced while generating a kernel (pre-validation result, an
/// unsupported-feature note, etc.). When a backend pre-validator (e.g. Tint for WGSL)
/// is wired in, its messages surface here - which is how the offline path can report a
/// shader-validation error (e.g. a WGSL <c>cannot assign 'f32' to 'i32'</c>) WITHOUT a
/// device or a browser.
/// </summary>
public sealed record GeneratedKernelDiagnostic(
    GeneratedKernelDiagnosticSeverity Severity,
    string Message,
    int? Line = null,
    int? Column = null);

/// <summary>
/// Structural metadata about a generated kernel - everything a consumer needs to dispatch
/// it (or to inspect/diff it) without re-parsing the source. Deterministic by construction.
/// </summary>
public sealed record GeneratedKernelMetadata
{
    /// <summary>Compiled workgroup / group size baked into the artifact.</summary>
    public (int X, int Y, int Z) GroupSize { get; init; } = (1, 1, 1);

    /// <summary>Number of storage-buffer bindings the kernel uses (WebGPU).</summary>
    public int BindingCount { get; init; }

    /// <summary>Static shared-memory bytes the kernel allocates.</summary>
    public int SharedMemoryBytes { get; init; }

    /// <summary>Number of barriers in the kernel (0 = non-barrier kernel).</summary>
    public int BarrierCount { get; init; }

    /// <summary>Whether the kernel uses Float16 (native or emulated per profile).</summary>
    public bool UsesFloat16 { get; init; }
    /// <summary>Whether the kernel uses Float64 (native or emulated per profile).</summary>
    public bool UsesFloat64 { get; init; }
    /// <summary>Whether the kernel uses Int64 (native or emulated per profile).</summary>
    public bool UsesInt64 { get; init; }

    /// <summary>The fully-qualified name of the source kernel method.</summary>
    public string KernelMethodName { get; init; } = "";
}

/// <summary>
/// The result of <see cref="ShaderCompiler.Generate"/> - a kernel's generated shader/binary
/// plus metadata and diagnostics, produced WITHOUT a device on any host OS.
///
/// Exactly one of <see cref="Source"/> (text backends: WGSL/GLSL) or <see cref="Binary"/>
/// (Wasm) is populated per <see cref="Backend"/>.
/// </summary>
public sealed record GeneratedKernel
{
    /// <summary>Which backend produced this artifact.</summary>
    public required AcceleratorType Backend { get; init; }

    /// <summary>The profile this artifact was generated for (its cache key feeds the manifest).</summary>
    public required CapabilityProfile Profile { get; init; }

    /// <summary>
    /// Generated shader SOURCE for text backends (WGSL for WebGPU, GLSL for WebGL).
    /// Null for Wasm (see <see cref="Binary"/>).
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Generated module BYTES for binary backends (Wasm). Null for WebGPU/WebGL
    /// (see <see cref="Source"/>).
    /// </summary>
    public byte[]? Binary { get; init; }

    /// <summary>Structural metadata about the generated kernel.</summary>
    public GeneratedKernelMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Opaque, backend-specific codegen metadata needed to RECONSTRUCT a compiled kernel from
    /// this artifact without re-running the transpiler (e.g. for WebGPU the scalar-packing
    /// manifest, binding count, i64-spinlock indices, coalesce manifest, dynamic-shared
    /// overrides). Carried into <see cref="ShaderArtifact.CodegenMetadata"/> when registered, and
    /// serialized to a sidecar file by the build-time precompile step. See
    /// <see cref="ShaderArtifactCache"/>.
    /// </summary>
    public object? CodegenMetadata { get; init; }

    /// <summary>
    /// Diagnostics produced during generation (and pre-validation, when wired). An
    /// <see cref="GeneratedKernelDiagnosticSeverity.Error"/> here means the artifact would
    /// fail to compile on a real device of this profile.
    /// </summary>
    public IReadOnlyList<GeneratedKernelDiagnostic> Diagnostics { get; init; } =
        Array.Empty<GeneratedKernelDiagnostic>();

    /// <summary>The artifact file extension for this backend ("wgsl", "glsl", "wasm").</summary>
    public string FileExtension => Backend switch
    {
        AcceleratorType.WebGPU => "wgsl",
        AcceleratorType.WebGL => "glsl",
        AcceleratorType.Wasm => "wasm",
        _ => "txt",
    };

    /// <summary>True if any diagnostic is an error.</summary>
    public bool HasErrors
    {
        get
        {
            foreach (var d in Diagnostics)
                if (d.Severity == GeneratedKernelDiagnosticSeverity.Error)
                    return true;
            return false;
        }
    }
}
