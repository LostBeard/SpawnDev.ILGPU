// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                 Trim roots for the browser backends' by-name member lookups
//
// File: BrowserTrimRoots.cs
//
// The WebGPU / WebGL / Wasm accelerators marshal kernel arguments by looking up ILGPU's
// OWN view, index and stride members by name ("BaseView", "IntLength", "Extent",
// "XStride", "X", "Value", ...). Nothing in an application has to call those members
// statically, so a trimmed app could lose one: the lookup returns null and the argument
// is marshalled wrong, silently. Every method that does such a lookup depends on
// ViewMembers below, which roots the properties and fields of those types.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Runtime;
using global::ILGPU.Util;
using System.Diagnostics.CodeAnalysis;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// Trim roots for the browser backends. Methods that look members up by name carry
    /// <c>[DynamicDependency(nameof(BrowserTrimRoots.ViewMembers), typeof(BrowserTrimRoots))]</c>.
    /// </summary>
    internal static class BrowserTrimRoots
    {
        /// <summary>
        /// Never called. Its DynamicDependency set is kept whenever a method that depends on it
        /// is kept, which roots the properties and fields of every ILGPU view, index and stride type
        /// (all instantiations of the generic ones).
        /// </summary>
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(ArrayView<>))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(ArrayView1D<,>))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(ArrayView2D<,>))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(ArrayView3D<,>))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(VariableView<>))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Index1D))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Index2D))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Index3D))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(LongIndex1D))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(LongIndex2D))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(LongIndex3D))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride1D.Infinite))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride1D.Dense))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride1D.General))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride2D.Infinite))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride2D.DenseX))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride2D.DenseY))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride2D.General))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride3D.Infinite))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride3D.DenseXY))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride3D.DenseZY))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(Stride3D.General))]
        [DynamicDependency(TrimmingAnnotations.ViewMembers, typeof(SpecializedValue<>))]
        internal static void ViewMembers() { }
    }
}
