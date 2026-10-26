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

    /// <summary>
    /// The leaves of a user struct in the order the IR flattens it: depth-first over the public instance fields in
    /// declaration order, a view (<see cref="IArrayView"/>) is ONE leaf (its pointer and length fields in the IR), a
    /// primitive or enum is one leaf, and a nested struct is replaced by its own leaves.
    /// </summary>
    /// <remarks>
    /// The WebGL body-struct manifest numbers the views and scalars of the IR's flattened fields; reading them back
    /// from the struct's DIRECT fields broke at the first nested struct: SpawnDev.Codecs' SilkPulsesInputs
    /// (`{ ArrayView&lt;byte&gt; Bytes; int ...; SilkShellCoderTableOffsets ShellTables; ArrayView&lt;int&gt; Scratch; ... }`)
    /// handed the nested struct to the scalar encoder (InvalidCastException in Convert.ToSingle) and every later field
    /// shifted by one (2026-10-26, 13 SILK tests).
    /// </remarks>
    public static System.Collections.Generic.List<ClrStructLeaf> FlattenUserStruct(Type type)
    {
        var leaves = new System.Collections.Generic.List<ClrStructLeaf>();
        Flatten(type, System.Array.Empty<System.Reflection.FieldInfo>(), leaves);
        return leaves;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = global::ILGPU.Util.TrimmingAnnotations.StructureLayout)]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072", Justification = global::ILGPU.Util.TrimmingAnnotations.StructureLayout)]
    private static void Flatten(Type type, System.Reflection.FieldInfo[] path,
        System.Collections.Generic.List<ClrStructLeaf> leaves)
    {
        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var fieldPath = new System.Reflection.FieldInfo[path.Length + 1];
            path.CopyTo(fieldPath, 0);
            fieldPath[path.Length] = field;
            var ft = field.FieldType;
            bool isView = typeof(IArrayView).IsAssignableFrom(ft);
            if (IsNestedStructToFlatten(ft))
                Flatten(ft, fieldPath, leaves);
            else
                leaves.Add(new ClrStructLeaf(fieldPath, isView));
        }
    }

    /// <summary>
    /// Whether a field of type <paramref name="type"/> inside a user struct is replaced by its own fields, as the IR
    /// flattens it: a value type that is not a primitive, an enum or a view, and not one of ILGPU's own scalar types
    /// (Half, BFloat16, the FP8/FP4 types, the index types: one IR primitive each, which the encoders handle whole) -
    /// an ILGPU type that holds views or pointers is still flattened.
    /// </summary>
    public static bool IsNestedStructToFlatten(Type type)
    {
        if (!type.IsValueType || type.IsPrimitive || type.IsEnum) return false;
        if (typeof(IArrayView).IsAssignableFrom(type)) return false;
        if (type.Assembly == typeof(IArrayView).Assembly && !HoldsViewOrPointer(type)) return false;
        return true;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = global::ILGPU.Util.TrimmingAnnotations.StructureLayout)]
    private static bool HoldsViewOrPointer(Type type)
    {
        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
        {
            var ft = field.FieldType;
            if (ft.IsPointer || ft == typeof(IntPtr) || ft == typeof(UIntPtr) || !ft.IsValueType) return true;
            if (typeof(IArrayView).IsAssignableFrom(ft)) return true;
            if (!ft.IsPrimitive && !ft.IsEnum && HoldsViewOrPointer(ft)) return true;
        }
        return false;
    }
}

/// <summary>One leaf of a flattened user struct (<see cref="KernelParameterKinds.FlattenUserStruct"/>).</summary>
/// <param name="Path">The fields from the struct down to the leaf.</param>
/// <param name="IsView">Whether the leaf is a view.</param>
public sealed record ClrStructLeaf(System.Reflection.FieldInfo[] Path, bool IsView)
{
    /// <summary>The leaf field.</summary>
    public System.Reflection.FieldInfo Field => Path[Path.Length - 1];

    /// <summary>The leaf's dotted name (<c>ShellTables.PulsesAt</c>).</summary>
    public string Name => string.Join(".", System.Linq.Enumerable.Select(Path, f => f.Name));

    /// <summary>The leaf's value in <paramref name="structValue"/> (a boxed struct of the flattened type).</summary>
    public object? GetValue(object structValue)
    {
        object? current = structValue;
        foreach (var f in Path)
        {
            if (current == null) return null;
            current = f.GetValue(current);
        }
        return current;
    }
}
