using System;
using ILGPU;
using ILGPU.Backends.EntryPoints;

namespace SpawnDev.ILGPU.CodeGen;

/// <summary>
/// What a kernel parameter IS, decided by its .NET type, for the WGSL, GLSL and Wasm generators.
/// </summary>
/// <remarks>
/// In the IR a user struct with one view and some ints has the same shape as an ArrayView1D/2D/3D wrapper (a view
/// pointer followed by primitive fields), so the generators' structural rules (field count, "the only pointer is the
/// first field") read `{ ArrayView&lt;byte&gt; Bytes; int A, B; }` as an ArrayView1D and `{ view; int A..E }` as an
/// ArrayView3D. The dispatchers decide by the argument's .NET type: every value type that is not an
/// <see cref="IArrayView"/> and holds views is flattened into its fields. Generators must decide the same way or the
/// layout and the bound arguments disagree (WebGPU: "layout expects 5 binding(s), but only 4 entries were built";
/// WebGL and Wasm read the wrong values; 2026-10-25, SpawnDev.Codecs SilkShellCoderTestKernel).
/// </remarks>
public static class KernelParameterKinds
{
    /// <summary>
    /// True when IR parameter <paramref name="irParameterIndex"/> of <paramref name="entryPoint"/> is a user struct:
    /// a .NET value type that is not an <see cref="IArrayView"/>. False for views, scalars, the index parameter and
    /// a capturing lambda's display class (a class), and when the type is not known.
    /// </summary>
    public static bool IsUserStruct(EntryPoint entryPoint, int irParameterIndex)
    {
        if (entryPoint == null) return false;
        int clrIndex = irParameterIndex - entryPoint.KernelIndexParameterOffset;
        if (clrIndex < 0 || clrIndex >= entryPoint.Parameters.Count) return false;
        Type type = entryPoint.Parameters[clrIndex];
        if (type.IsByRef) type = type.GetElementType()!;
        return type.IsValueType && !type.IsPrimitive && !type.IsEnum && !typeof(IArrayView).IsAssignableFrom(type);
    }
}
