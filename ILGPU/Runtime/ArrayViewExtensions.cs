// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2021-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: ArrayViewExtensions.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Frontend.Intrinsic;
using ILGPU.IR.Types;
using ILGPU.Resources;
using ILGPU.Runtime.CPU;
using ILGPU.Util;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Array view extension methods
    /// </summary>
    public static partial class ArrayViewExtensions
    {
        #region ArrayView

        /// <summary>
        /// Loads the effective address of the current view.
        /// </summary>
        /// <returns>The effective address.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [NotInsideKernel]
        public static ref byte LoadEffectiveAddress<T>(this ArrayView<T> view)
            where T : unmanaged =>
            ref view.LoadEffectiveAddress();

        /// <summary>
        /// Loads the effective address of the current view.
        /// </summary>
        /// <returns>The effective address.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [NotInsideKernel]
        public static IntPtr LoadEffectiveAddressAsPtr<T>(this ArrayView<T> view)
            where T : unmanaged =>
            view.LoadEffectiveAddressAsPtr();

        /// <summary>
        /// Verifies the given alignment in bytes.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="alignmentInBytes">The alignment in bytes.</param>
        private static void VerifyAlignmentInBytes<T>(int alignmentInBytes)
            where T : unmanaged =>
            Trace.Assert(
                alignmentInBytes > 0 &
                (alignmentInBytes % 2 == 0 | alignmentInBytes == 1),
                "Invalid alignment in bytes");

        /// <summary>
        /// Aligns the given array view to the specified alignment in bytes and returns a
        /// view spanning the initial unaligned parts of the given view and another
        /// view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="alignmentInBytes">The basic alignment in bytes.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo<T>(
            this ArrayView<T> view,
            int alignmentInBytes)
            where T : unmanaged
        {
            VerifyAlignmentInBytes<T>(alignmentInBytes);
            return view.AlignToInternal(alignmentInBytes);
        }

        /// <summary>
        /// Aligns the given array view to the specified alignment in bytes and returns a
        /// view spanning the initial unaligned parts of the given view and another
        /// view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="alignmentInBytes">The basic alignment in bytes.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo<T>(
            this ArrayView1D<T, Stride1D.Dense> view,
            int alignmentInBytes)
            where T : unmanaged =>
            view.BaseView.AlignTo(alignmentInBytes);

        /// <summary>
        /// Ensures that the array view is aligned to the specified alignment in bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="alignmentInBytes">The basic alignment in bytes.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<T> AsAligned<T>(
            this ArrayView<T> view,
            int alignmentInBytes)
            where T : unmanaged
        {
            VerifyAlignmentInBytes<T>(alignmentInBytes);
            return view.AsAlignedInternal(alignmentInBytes);
        }

        /// <summary>
        /// Ensures that the array view is aligned to the specified alignment in bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="alignmentInBytes">The basic alignment in bytes.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.Dense> AsAligned<T>(
            this ArrayView1D<T, Stride1D.Dense> view,
            int alignmentInBytes)
            where T : unmanaged =>
            view.BaseView.AsAligned(alignmentInBytes);

        /// <summary>
        /// Ensures the array view is aligned to the specified byte boundary and casts
        /// it to another element type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<TOther> CastAligned<T, TOther>(
            this ArrayView<T> view,
            int alignmentInBytes)
            where T : unmanaged
            where TOther : unmanaged =>
            view.AsAligned(alignmentInBytes).Cast<TOther>();

        /// <summary>
        /// Ensures the array view is aligned to the target element's natural size and
        /// casts it to that element type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<TOther> CastAligned<T, TOther>(
            this ArrayView<T> view)
            where T : unmanaged
            where TOther : unmanaged =>
            view.CastAligned<T, TOther>(ArrayView<TOther>.ElementSize);

        /// <summary>
        /// Ensures the dense 1D view is aligned to the specified byte boundary and
        /// casts it to another element type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<TOther, Stride1D.Dense> CastAligned<T, TOther>(
            this ArrayView1D<T, Stride1D.Dense> view,
            int alignmentInBytes)
            where T : unmanaged
            where TOther : unmanaged =>
            view.BaseView.CastAligned<T, TOther>(alignmentInBytes);

        /// <summary>
        /// Ensures the dense 1D view is aligned to the target element's natural size
        /// and casts it to that element type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<TOther, Stride1D.Dense> CastAligned<T, TOther>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged
            where TOther : unmanaged =>
            view.BaseView.CastAligned<T, TOther>(ArrayView<TOther>.ElementSize);

        /// <summary>
        /// Loads a vectorized value at the given source-element index, asserting the
        /// requested alignment so the backend can emit vector memory instructions.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector LoadVectorized<T, TVector>(
            this ArrayView<T> view,
            long elementIndex,
            int alignmentInBytes)
            where T : unmanaged
            where TVector : unmanaged =>
            view.SubView(elementIndex)
                .CastAligned<T, TVector>(alignmentInBytes)[0];

        /// <summary>
        /// Loads a vectorized value at the given source-element index using the vector
        /// type's natural size as the required alignment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector LoadVectorized<T, TVector>(
            this ArrayView<T> view,
            long elementIndex)
            where T : unmanaged
            where TVector : unmanaged =>
            view.LoadVectorized<T, TVector>(elementIndex, ArrayView<TVector>.ElementSize);

        /// <summary>
        /// Stores a vectorized value at the given target-element index, asserting the
        /// requested alignment so the backend can emit vector memory instructions.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreVectorized<T, TVector>(
            this ArrayView<T> view,
            long elementIndex,
            TVector value,
            int alignmentInBytes)
            where T : unmanaged
            where TVector : unmanaged =>
            view.SubView(elementIndex)
                .CastAligned<T, TVector>(alignmentInBytes)[0] = value;

        /// <summary>
        /// Stores a vectorized value at the given target-element index using the vector
        /// type's natural size as the required alignment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreVectorized<T, TVector>(
            this ArrayView<T> view,
            long elementIndex,
            TVector value)
            where T : unmanaged
            where TVector : unmanaged =>
            view.StoreVectorized<T, TVector>(
                elementIndex, value, ArrayView<TVector>.ElementSize);

        /// <summary>
        /// Loads a vectorized value from a dense 1D view at the given source-element
        /// index, asserting the requested alignment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector LoadVectorized<T, TVector>(
            this ArrayView1D<T, Stride1D.Dense> view,
            long elementIndex,
            int alignmentInBytes)
            where T : unmanaged
            where TVector : unmanaged =>
            view.BaseView.LoadVectorized<T, TVector>(elementIndex, alignmentInBytes);

        /// <summary>
        /// Loads a vectorized value from a dense 1D view using the vector type's
        /// natural size as the required alignment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TVector LoadVectorized<T, TVector>(
            this ArrayView1D<T, Stride1D.Dense> view,
            long elementIndex)
            where T : unmanaged
            where TVector : unmanaged =>
            view.BaseView.LoadVectorized<T, TVector>(
                elementIndex, ArrayView<TVector>.ElementSize);

        /// <summary>
        /// Stores a vectorized value into a dense 1D view at the given target-element
        /// index, asserting the requested alignment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreVectorized<T, TVector>(
            this ArrayView1D<T, Stride1D.Dense> view,
            long elementIndex,
            TVector value,
            int alignmentInBytes)
            where T : unmanaged
            where TVector : unmanaged =>
            view.BaseView.StoreVectorized<T, TVector>(
                elementIndex, value, alignmentInBytes);

        /// <summary>
        /// Stores a vectorized value into a dense 1D view using the vector type's
        /// natural size as the required alignment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreVectorized<T, TVector>(
            this ArrayView1D<T, Stride1D.Dense> view,
            long elementIndex,
            TVector value)
            where T : unmanaged
            where TVector : unmanaged =>
            view.BaseView.StoreVectorized<T, TVector>(
                elementIndex, value, ArrayView<TVector>.ElementSize);

        /// <summary>
        /// Returns a variable view to the given element.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="view">The view.</param>
        /// <param name="element">The element index.</param>
        /// <returns>The resolved variable view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VariableView<T> VariableView<T>(
            this ArrayView<T> view,
            Index1D element)
            where T : unmanaged =>
            new VariableView<T>(view.SubView(element, 1));

        /// <summary>
        /// Returns a variable view to the given element.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="view">The view.</param>
        /// <param name="element">The element index.</param>
        /// <returns>The resolved variable view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VariableView<T> VariableView<T>(
            this ArrayView<T> view,
            LongIndex1D element)
            where T : unmanaged =>
            new VariableView<T>(view.SubView(element, 1L));

        /// <summary>
        /// Converts this array view into a dense version.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="view">The view.</param>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.Dense> AsDense<T>(
            this ArrayView<T> view)
            where T : unmanaged =>
            view;

        /// <summary>
        /// Converts this array view into a general version.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="view">The view.</param>
        /// <param name="stride">The generic stride information to use.</param>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.General> AsGeneral<T>(
            this ArrayView<T> view,
            Stride1D.General stride)
            where T : unmanaged =>
            view.AsDense().AsGeneral(stride);

        /// <summary>
        /// Converts this array view into a general version.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="view">The view.</param>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.General> AsGeneral<T>(
            this ArrayView<T> view)
            where T : unmanaged =>
            view.AsDense().AsGeneral();

        #endregion

        #region Base Methods

        /// <summary>
        /// Returns true if the current view is not valid or does not span over a single
        /// element (length &lt; 1).
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>True, this view has no data.</returns>
        public static bool HasNoData<TView>(this TView view)
            where TView : IArrayView =>
            !view.IsValid || view.Length < 1;

        /// <summary>
        /// Returns true if the current view is valid and includes at least a single
        /// element (length &gt; 0).
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>True, this view has a least one valid data element.</returns>
        public static bool HasData<TView>(this TView view)
            where TView : IArrayView =>
            Bitwise.And(view.IsValid, view.Length > 0);

        /// <summary>
        /// Returns the associated accelerator of the current view.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The associated parent accelerator.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Accelerator GetAccelerator<TView>(this TView view)
            where TView : IArrayView
        {
            var parentBuffer = view.Buffer;
            return parentBuffer is null
                ? throw new InvalidOperationException(
                    RuntimeErrorMessages.UnknownParentAccelerator)
                : parentBuffer.Accelerator;
        }

        /// <summary>
        /// Returns the associated parent context of the current view.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The associated parent context.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Context GetContext<TView>(this TView view)
            where TView : IArrayView =>
            view.GetAccelerator().Context;

        /// <summary>
        /// Returns the associated accelerator of the current view.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The associated parent accelerator.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ContextProperties GetContextProperties<TView>(this TView view)
            where TView : IArrayView =>
            view.GetContext().Properties;

        /// <summary>
        /// Returns the associated accelerator type of the current view.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The associated parent accelerator type.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AcceleratorType GetAcceleratorType<TView>(this TView view)
            where TView : IArrayView =>
            view.Buffer.AcceleratorType;

        /// <summary>
        /// Returns the associated default stream of the parent accelerator.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The default stream of the parent accelerator.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static AcceleratorStream GetDefaultStream<TView>(this TView view)
            where TView : IArrayView =>
            view.GetAccelerator().DefaultStream;

        /// <summary>
        /// Returns the current page locking mode.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The current page locking mode.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static PageLockingMode GetPageLockingMode<TView>(this TView view)
            where TView : IArrayView =>
            view.GetContextProperties().PageLockingMode;

        /// <summary>
        /// Returns true if the view is attached to a context using
        /// <see cref="PageLockingMode.Auto"/>.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>True, if the parent context uses automatic page locking.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool UsesAutoPageLocking<TView>(this TView view)
            where TView : IArrayView =>
            view.GetPageLockingMode() >= PageLockingMode.Auto;

        #endregion

        #region Transpose

        /// <summary>
        /// Reinterpets the given view as a transposed dense view.
        /// </summary>
        /// <typeparam name="T">The view element type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The transposed array view.</returns>
        public static ArrayView2D<T, Stride2D.DenseY> AsTransposed<T>(
            this ArrayView2D<T, Stride2D.DenseX> view)
            where T : unmanaged =>
            new ArrayView2D<T, Stride2D.DenseY>(
                view.BaseView,
                new LongIndex2D(view.Extent.Y, view.Extent.X),
                new Stride2D.DenseY(view.Stride.YStride));

        /// <summary>
        /// Reinterpets the given view as a transposed dense view.
        /// </summary>
        /// <typeparam name="T">The view element type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The transposed array view.</returns>
        public static ArrayView2D<T, Stride2D.DenseX> AsTransposed<T>(
            this ArrayView2D<T, Stride2D.DenseY> view)
            where T : unmanaged =>
            new ArrayView2D<T, Stride2D.DenseX>(
                view.BaseView,
                new LongIndex2D(view.Extent.Y, view.Extent.X),
                new Stride2D.DenseX(view.Stride.XStride));

        /// <summary>
        /// Reinterpets the given view as a transposed view.
        /// </summary>
        /// <typeparam name="T">The view element type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The transposed array view.</returns>
        public static ArrayView2D<T, Stride2D.General> AsTransposed<T>(
            this ArrayView2D<T, Stride2D.General> view)
            where T : unmanaged =>
            new ArrayView2D<T, Stride2D.General>(
                view.BaseView,
                new LongIndex2D(view.Extent.Y, view.Extent.X),
                new Stride2D.General((view.Stride.YStride, view.Stride.XStride)));

        /// <summary>
        /// Reinterpets the given view as a transposed dense view.
        /// </summary>
        /// <typeparam name="T">The view element type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The transposed array view.</returns>
        public static ArrayView3D<T, Stride3D.DenseZY> AsTransposed<T>(
            this ArrayView3D<T, Stride3D.DenseXY> view)
            where T : unmanaged =>
            new ArrayView3D<T, Stride3D.DenseZY>(
                view.BaseView,
                new LongIndex3D(view.Extent.Z, view.Extent.Y, view.Extent.X),
                new Stride3D.DenseZY(view.Stride.ZStride, view.Stride.YStride));

        /// <summary>
        /// Reinterpets the given view as a transposed dense view.
        /// </summary>
        /// <typeparam name="T">The view element type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The transposed array view.</returns>
        public static ArrayView3D<T, Stride3D.DenseXY> AsTransposed<T>(
            this ArrayView3D<T, Stride3D.DenseZY> view)
            where T : unmanaged =>
            new ArrayView3D<T, Stride3D.DenseXY>(
                view.BaseView,
                new LongIndex3D(view.Extent.Z, view.Extent.Y, view.Extent.X),
                new Stride3D.DenseXY(view.Stride.YStride, view.Stride.XStride));

        /// <summary>
        /// Reinterpets the given view as a transposed view.
        /// </summary>
        /// <typeparam name="T">The view element type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <returns>The transposed array view.</returns>
        public static ArrayView3D<T, Stride3D.General> AsTransposed<T>(
            this ArrayView3D<T, Stride3D.General> view)
            where T : unmanaged =>
            new ArrayView3D<T, Stride3D.General>(
                view.BaseView,
                new LongIndex3D(view.Extent.Z, view.Extent.Y, view.Extent.X),
                new Stride3D.General(
                    (view.Stride.ZStride,
                    view.Stride.YStride,
                    view.Stride.XStride)));

        #endregion

        #region MemSet

        /// <summary>
        /// Sets the contents of the given buffer to zero using the default accelerator
        /// stream.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static void MemSetToZero<TView>(this TView view)
            where TView : IContiguousArrayView =>
            view.MemSetToZero(view.GetDefaultStream());

        /// <summary>
        /// Sets the contents of the current buffer to zero.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static void MemSetToZero<TView>(
            this TView view,
            AcceleratorStream stream)
            where TView : IContiguousArrayView =>
            view.MemSet(stream, 0);

        /// <summary>
        /// Sets the contents of the given buffer to the given byte value using the
        /// default accelerator stream.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <param name="value">The value to write into the memory buffer.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static void MemSet<TView>(
            this TView view,
            byte value)
            where TView : IContiguousArrayView =>
            view.MemSet(view.GetDefaultStream(), value);

        /// <summary>
        /// Sets the contents of the current buffer to the given byte value.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="view">The view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="value">The value to write into the memory buffer.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static void MemSet<TView>(
            this TView view,
            AcceleratorStream stream,
            byte value)
            where TView : IContiguousArrayView
        {
            if (!view.IsValid)
                throw new ArgumentNullException(nameof(view));
            if (view.HasNoData())
                return;

            var rawView = view.AsRawArrayView();
            view.Buffer.MemSet(
                stream,
                value,
                rawView.Index,
                rawView.Length);
        }

        #endregion

        #region Copy from/to Views

        /// <summary>
        /// Copies from the source view into the target view.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="target">The target view instance.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyTo<TView>(
            this TView source,
            in TView target)
            where TView : IContiguousArrayView =>
            source.CopyTo(source.GetDefaultStream(), target);

        /// <summary>
        /// Copies from the source view into the target view.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="target">The target view instance.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyTo<TView>(
            this TView source,
            AcceleratorStream stream,
            in TView target)
            where TView : IContiguousArrayView =>
            source.Buffer.CopyTo(
                stream,
                source.IndexInBytes,
                target.AsRawArrayView());

        /// <summary>
        /// Copies from the source view into the target view.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="target">The target view instance.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyFrom<TView>(
            this TView target,
            in TView source)
            where TView : IContiguousArrayView =>
            target.CopyFrom(target.GetDefaultStream(), source);

        /// <summary>
        /// Copies from the source view into the target view.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="source">The source view instance.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyFrom<TView>(
            this TView target,
            AcceleratorStream stream,
            in TView source)
            where TView : IContiguousArrayView =>
            target.Buffer.CopyFromBuffer(
                stream,
                source.Buffer,
                source.IndexInBytes,
                target.IndexInBytes,
                source.AsRawArrayView().LengthInBytes);

        /// <summary>
        /// UNGUARDED device-to-device copy that bypasses the browser sync-copy guard on
        /// <see cref="MemoryBuffer.CopyFromBuffer"/>. For LIBRARY/internal code that already orders
        /// the copy by other means on the browser backends (queue order, or an explicit
        /// drain/flush), where the public
        /// <see cref="CopyFrom{TView}(TView, AcceleratorStream, in TView)"/> throws so consumer
        /// misuse is loud. Calling this from synchronous consumer code without such ordering
        /// re-introduces the unordered race the guard exists to stop - use the async
        /// <c>CopyFromAsync</c> path there instead.
        /// </summary>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyFromUnchecked<TView>(
            this TView target,
            AcceleratorStream stream,
            in TView source)
            where TView : IContiguousArrayView =>
            target.Buffer.CopyFromBufferAfterDrain(
                stream,
                source.Buffer,
                source.IndexInBytes,
                target.IndexInBytes,
                source.AsRawArrayView().LengthInBytes);

        #endregion

        #region Copy elements to/from CPU async

        /// <summary>
        /// Copies from the source view into the given CPU target address without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="cpuData">The base address of the pinned CPU buffer.</param>
        /// <param name="length">The number of elements to copy.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyToCPUUnsafeAsync<T, TView>(
            this TView source,
            ref T cpuData,
            long length)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            source.CopyToCPUUnsafeAsync(
                source.GetDefaultStream(),
                ref cpuData,
                length);

        /// <summary>
        /// Copies from the source view into the given CPU target address without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="cpuData">The base address of the pinned CPU buffer.</param>
        /// <param name="length">The number of elements to copy.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static unsafe void CopyToCPUUnsafeAsync<T, TView>(
            this TView source,
            AcceleratorStream stream,
            ref T cpuData,
            long length)
            where TView : IContiguousArrayView<T>
            where T : unmanaged
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (length < 1)
                return;

            // Check for an aggressive page-locking mode.
            if (source.GetPageLockingMode() == PageLockingMode.Aggressive)
            {
                var accelerator = source.GetAccelerator();
                using var pageLockScope = accelerator.CreatePageLockFromPinned<T>(
                    new IntPtr(Unsafe.AsPointer(ref cpuData)),
                    length);
                source.Buffer.CopyTo(
                    stream,
                    source.IndexInBytes,
                    pageLockScope.ArrayView.Cast<byte>());
            }
            else
            {
                using var buffer = CPUMemoryBuffer.Create(
                    source.GetAccelerator(),
                    ref cpuData,
                    length);
                source.Buffer.CopyTo(
                    stream,
                    source.IndexInBytes,
                    buffer.AsRawArrayView());
            }
        }

        /// <summary>
        /// Copies from the CPU source address into the given target view without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="cpuData">The base address of the pinned CPU buffer.</param>
        /// <param name="length">The number of elements to copy.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyFromCPUUnsafeAsync<T, TView>(
            this TView target,
            ref T cpuData,
            long length)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            target.CopyFromCPUUnsafeAsync(
                target.GetDefaultStream(),
                ref cpuData,
                length);

        /// <summary>
        /// Copies from the CPU source address into the given target view without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="target">The target view instance.</param>
        /// <param name="cpuData">The base address of the pinned CPU buffer.</param>
        /// <param name="length">The number of elements to copy.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static unsafe void CopyFromCPUUnsafeAsync<T, TView>(
            this TView target,
            AcceleratorStream stream,
            ref T cpuData,
            long length)
            where TView : IContiguousArrayView<T>
            where T : unmanaged
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (length < 1)
                return;

            // Check for an aggressive page-locking mode.
            if (target.GetPageLockingMode() == PageLockingMode.Aggressive)
            {
                var accelerator = target.GetAccelerator();
                using var pageLockScope = accelerator.CreatePageLockFromPinned<T>(
                    new IntPtr(Unsafe.AsPointer(ref cpuData)),
                    length);
                target.Buffer.CopyFrom(
                    stream,
                    pageLockScope.ArrayView.Cast<byte>(),
                    target.IndexInBytes);
            }
            else
            {
                using var buffer = CPUMemoryBuffer.Create(
                    target.GetAccelerator(),
                    ref cpuData,
                    length);
                target.Buffer.CopyFrom(
                    stream,
                    buffer.AsRawArrayView(),
                    target.IndexInBytes);
            }
        }

        #endregion

        #region Copy elements to/from CPU

        /// <summary>
        /// Copies from the source view into the given CPU target address while
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="cpuData">The base address of the CPU buffer.</param>
        /// <param name="length">The number of elements to copy.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyToCPU<T, TView>(
            this TView source,
            ref T cpuData,
            long length)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            source.CopyToCPU(
                source.GetDefaultStream(),
                ref cpuData,
                length);

        /// <summary>
        /// Copies from the source view into the given CPU target address while
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="cpuData">The base address of the CPU buffer.</param>
        /// <param name="length">The number of elements to copy.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyToCPU<T, TView>(
            this TView source,
            AcceleratorStream stream,
            ref T cpuData,
            long length)
            where TView : IContiguousArrayView<T>
            where T : unmanaged
        {
            // Copy async into memory
            source.CopyToCPUUnsafeAsync(stream, ref cpuData, length);
            stream.Synchronize();
        }

        /// <summary>
        /// Asynchronously copies the contents of the given view back to the host as a
        /// managed array. This is the backend-agnostic, browser-safe readback: it routes
        /// through <see cref="MemoryBuffer.CopyToRawAsync"/>, which awaits the accelerator's
        /// real async drain before reading. Unlike the synchronous <c>CopyToCPU</c>, this
        /// returns correct data on Wasm (drains in-flight worker kernels first) and does not
        /// throw on WebGPU / WebGL (which have no synchronous GPU-&gt;CPU readback).
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The source view to read back.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <returns>A task producing the view's <c>Length</c> elements.</returns>
        [NotInsideKernel]
        public static async Task<T[]> CopyToCPUAsync<T>(
            this ArrayView<T> source,
            AcceleratorStream stream)
            where T : unmanaged
        {
            var contig = (IContiguousArrayView)source;
            var buffer = contig.Buffer
                ?? throw new InvalidOperationException(
                    "ArrayView has no backing buffer.");
            long countElems = source.Length;
            if (countElems == 0)
                return Array.Empty<T>();
            int elementSize = ((IArrayView)source).ElementSize;
            long byteOffset = contig.IndexInBytes;
            long byteCount = countElems * elementSize;

            var bytes = await buffer
                .CopyToRawAsync(stream, byteOffset, byteCount)
                .ConfigureAwait(false);

            var result = new T[countElems];
            MemoryMarshal.Cast<byte, T>(bytes).CopyTo(new Span<T>(result));
            return result;
        }

        /// <summary>
        /// <see cref="ArrayView1D{T, TStride}"/> overload of
        /// <see cref="CopyToCPUAsync{T}(ArrayView{T}, AcceleratorStream)"/>.
        /// </summary>
        [NotInsideKernel]
        public static Task<T[]> CopyToCPUAsync<T, TStride>(
            this ArrayView1D<T, TStride> source,
            AcceleratorStream stream)
            where T : unmanaged
            where TStride : struct, IStride1D =>
            source.BaseView.CopyToCPUAsync(stream);

        /// <summary>
        /// Asynchronously streams the bytes of <paramref name="source"/> into
        /// <paramref name="target"/> (exactly <c>target.Length * sizeof(T)</c> bytes), in
        /// chunks, awaiting the stream's async reads. This is the backend-agnostic, browser-safe
        /// upload: it routes through <see cref="MemoryBuffer.CopyFromStreamRawAsync"/>. On
        /// CUDA / OpenCL / CPU it awaits <c>Stream.ReadExactlyAsync</c> (so a model off disk or
        /// network does not block a thread); on the browser backends, if <paramref name="source"/>
        /// is a <c>SpawnDev.BlazorJS.Toolbox.IJSReadStream</c> the data goes JS -&gt; GPU without
        /// entering the managed heap (the override checks). A stream that ends before
        /// <c>target.Length * sizeof(T)</c> bytes throws <see cref="EndOfStreamException"/> rather
        /// than silently zero-padding the tail.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="target">The destination view (filled exactly).</param>
        /// <param name="source">The byte source, read from its current position.</param>
        /// <param name="stream">The accelerator stream the chunk copies are issued on.</param>
        /// <param name="chunkSizeInBytes">Per-chunk transfer size; defaults to
        /// <see cref="MemoryBuffer.DefaultStreamChunkSizeInBytes"/> (16 MiB).</param>
        /// <param name="cancellationToken">Cancels the in-flight reads.</param>
        [NotInsideKernel]
        public static Task CopyFromStreamAsync<T>(
            this ArrayView<T> target,
            Stream source,
            AcceleratorStream stream,
            int chunkSizeInBytes = MemoryBuffer.DefaultStreamChunkSizeInBytes,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            var contig = (IContiguousArrayView)target;
            var buffer = contig.Buffer
                ?? throw new InvalidOperationException(
                    "ArrayView has no backing buffer.");
            long countElems = target.Length;
            if (countElems == 0)
                return Task.CompletedTask;
            int elementSize = ((IArrayView)target).ElementSize;
            long byteOffset = contig.IndexInBytes;
            long byteCount = countElems * elementSize;
            return buffer.CopyFromStreamRawAsync(
                stream,
                source,
                byteOffset,
                byteCount,
                chunkSizeInBytes,
                cancellationToken);
        }

        /// <summary>
        /// <see cref="ArrayView1D{T, TStride}"/> overload of
        /// <see cref="CopyFromStreamAsync{T}(ArrayView{T}, Stream, AcceleratorStream, int, CancellationToken)"/>.
        /// </summary>
        [NotInsideKernel]
        public static Task CopyFromStreamAsync<T, TStride>(
            this ArrayView1D<T, TStride> target,
            Stream source,
            AcceleratorStream stream,
            int chunkSizeInBytes = MemoryBuffer.DefaultStreamChunkSizeInBytes,
            CancellationToken cancellationToken = default)
            where T : unmanaged
            where TStride : struct, IStride1D =>
            target.BaseView.CopyFromStreamAsync(
                source, stream, chunkSizeInBytes, cancellationToken);

        /// <summary>
        /// Convenience overload of
        /// <see cref="CopyFromStreamAsync{T}(ArrayView{T}, Stream, AcceleratorStream, int, CancellationToken)"/>
        /// that issues the copies on the backing accelerator's default stream.
        /// </summary>
        [NotInsideKernel]
        public static Task CopyFromStreamAsync<T>(
            this ArrayView<T> target,
            Stream source,
            int chunkSizeInBytes = MemoryBuffer.DefaultStreamChunkSizeInBytes,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            var contig = (IContiguousArrayView)target;
            var buffer = contig.Buffer
                ?? throw new InvalidOperationException(
                    "ArrayView has no backing buffer.");
            return target.CopyFromStreamAsync(
                source,
                buffer.Accelerator.DefaultStream,
                chunkSizeInBytes,
                cancellationToken);
        }

        /// <summary>
        /// <see cref="ArrayView1D{T, TStride}"/> default-stream convenience overload of
        /// <see cref="CopyFromStreamAsync{T}(ArrayView{T}, Stream, int, CancellationToken)"/>.
        /// </summary>
        [NotInsideKernel]
        public static Task CopyFromStreamAsync<T, TStride>(
            this ArrayView1D<T, TStride> target,
            Stream source,
            int chunkSizeInBytes = MemoryBuffer.DefaultStreamChunkSizeInBytes,
            CancellationToken cancellationToken = default)
            where T : unmanaged
            where TStride : struct, IStride1D =>
            target.BaseView.CopyFromStreamAsync(
                source, chunkSizeInBytes, cancellationToken);

        /// <summary>
        /// Async, browser-safe equivalent of
        /// <see cref="GetAsArray1D{T}(ArrayView1D{T, Stride1D.Dense})"/>: awaits the
        /// accelerator's real async drain, then reads the dense 1D view back to a managed
        /// array. Use this instead of <c>GetAsArray1D</c> on Wasm/WebGL/WebGPU, where the
        /// synchronous readback returns stale data (Wasm) or throws (WebGPU/WebGL).
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="view">The source view to read back.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <returns>A task producing the view's <c>Length</c> elements.</returns>
        [NotInsideKernel]
        public static Task<T[]> GetAsArray1DAsync<T>(
            this ArrayView1D<T, Stride1D.Dense> view,
            AcceleratorStream stream)
            where T : unmanaged =>
            view.BaseView.CopyToCPUAsync(stream);

        /// <summary>
        /// <see cref="GetAsArray1DAsync{T}(ArrayView1D{T, Stride1D.Dense},
        /// AcceleratorStream)"/> using the view's default stream.
        /// </summary>
        [NotInsideKernel]
        public static Task<T[]> GetAsArray1DAsync<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            view.GetAsArray1DAsync(view.GetDefaultStream());

        /// <summary>
        /// Async, browser-safe equivalent of
        /// <see cref="GetAsArray2D{T}(ArrayView2D{T, Stride2D.General}, AcceleratorStream)"/>:
        /// reads the underlying buffer back via <see cref="CopyToCPUAsync{T}(ArrayView{T},
        /// AcceleratorStream)"/> (the real async drain + readback) and reshapes it into a 2D
        /// array using the view's stride, so it is correct for any stride/transposition and
        /// works on Wasm/WebGL/WebGPU where the synchronous <c>GetAsArray2D</c> reads stale
        /// data or throws.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStride">The 2D stride type.</typeparam>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <returns>A task producing the reshaped 2D array.</returns>
        [NotInsideKernel]
        public static async Task<T[,]> GetAsArray2DAsync<T, TStride>(
            this ArrayView2D<T, TStride> view,
            AcceleratorStream stream)
            where T : unmanaged
            where TStride : struct, IStride2D
        {
            var ext = view.IntExtent;
            if (!view.IsValid || ext.X == 0 || ext.Y == 0)
                return new T[ext.X < 0 ? 0 : ext.X, ext.Y < 0 ? 0 : ext.Y];

            var flat = await view.BaseView.CopyToCPUAsync(stream).ConfigureAwait(false);
            var result = new T[ext.X, ext.Y];
            for (int x = 0; x < ext.X; ++x)
                for (int y = 0; y < ext.Y; ++y)
                    result[x, y] = flat[
                        (int)view.Stride.ComputeElementIndexChecked(
                            new Index2D(x, y), ext)];
            return result;
        }

        /// <summary>
        /// Async, browser-safe equivalent of
        /// <see cref="GetAsArray3D{T}(ArrayView3D{T, Stride3D.General}, AcceleratorStream)"/>;
        /// see <see cref="GetAsArray2DAsync{T, TStride}"/> for the approach.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStride">The 3D stride type.</typeparam>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <returns>A task producing the reshaped 3D array.</returns>
        [NotInsideKernel]
        public static async Task<T[,,]> GetAsArray3DAsync<T, TStride>(
            this ArrayView3D<T, TStride> view,
            AcceleratorStream stream)
            where T : unmanaged
            where TStride : struct, IStride3D
        {
            var ext = view.IntExtent;
            if (!view.IsValid || ext.X == 0 || ext.Y == 0 || ext.Z == 0)
                return new T[
                    ext.X < 0 ? 0 : ext.X,
                    ext.Y < 0 ? 0 : ext.Y,
                    ext.Z < 0 ? 0 : ext.Z];

            var flat = await view.BaseView.CopyToCPUAsync(stream).ConfigureAwait(false);
            var result = new T[ext.X, ext.Y, ext.Z];
            for (int x = 0; x < ext.X; ++x)
                for (int y = 0; y < ext.Y; ++y)
                    for (int z = 0; z < ext.Z; ++z)
                        result[x, y, z] = flat[
                            (int)view.Stride.ComputeElementIndexChecked(
                                new Index3D(x, y, z), ext)];
            return result;
        }

        /// <summary>
        /// Guards a synchronous GPU-&gt;CPU readback against the browser backends, which have
        /// no usable synchronous readback: WebGPU/WebGL throw on a synchronous GPU-&gt;CPU
        /// copy, and Wasm would read stale data before in-flight worker kernels finish (a
        /// silent wrong result). Call this at the top of any synchronous-readback convenience
        /// API so it fails loud with an actionable message instead of returning corrupt data;
        /// the caller should switch to the matching <c>...Async</c> method on those backends.
        /// </summary>
        /// <param name="accelerator">The accelerator to check.</param>
        /// <param name="asyncAlternative">
        /// The name of the async method to recommend in the exception message.
        /// </param>
        public static void EnsureSyncReadbackSupported(
            this Accelerator accelerator,
            string asyncAlternative)
        {
            switch (accelerator.AcceleratorType)
            {
                case AcceleratorType.Wasm:
                case AcceleratorType.WebGL:
                case AcceleratorType.WebGPU:
                    throw new NotSupportedException(
                        "Synchronous GPU->CPU readback is not supported on the " +
                        $"{accelerator.AcceleratorType} backend: browser backends have no " +
                        "synchronous GPU->CPU readback (WebGPU/WebGL throw; Wasm would read " +
                        "stale data before in-flight kernels finish). Use " +
                        $"{asyncAlternative} instead.");
            }
        }

        /// <summary>
        /// Copies from the CPU source address into the given target view while
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="cpuData">The base address of the CPU buffer.</param>
        /// <param name="length">The number of elements to copy.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyFromCPU<T, TView>(
            this TView target,
            ref T cpuData,
            long length)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            target.CopyFromCPU(
                target.GetDefaultStream(),
                ref cpuData,
                length);

        /// <summary>
        /// Copies from the CPU source address into the given target view while
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="cpuData">The base address of the CPU buffer.</param>
        /// <param name="length">The number of elements to copy.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyFromCPU<T, TView>(
            this TView target,
            AcceleratorStream stream,
            ref T cpuData,
            long length)
            where TView : IContiguousArrayView<T>
            where T : unmanaged
        {
            // Copy async into memory
            target.CopyFromCPUUnsafeAsync(stream, ref cpuData, length);
            // Upload is fire-and-forget: wait on desktop (DMA in flight), no-op on browser
            // (host source consumed synchronously). NOT the throwing sync Synchronize().
            stream.EnsureHostCopyConsumed();
        }

        #endregion

        #region Copy elements to/from CPU (specialized Views)

        // Remarks:
        // The following functions rearrange the input/output data in the CPU. To this
        // extent, the transfer functions perform a single bulk copy to transfer all data
        // items (including items to be discarded). This functionality could be improved
        // by splitting the single copy operation into several small ones in certain
        // cases. Support for this feature remains future work.

        /// <summary>
        /// Copies the contents of the 1D array into the given 1D view using the default
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CopyFromCPU<T>(
            this ArrayView1D<T, Stride1D.General> view,
            T[] data)
            where T : unmanaged =>
            view.CopyFromCPU(view.GetDefaultStream(), data);

        /// <summary>
        /// Copies the contents of the 1D array into the given 1D view using the given
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method transposes the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static unsafe void CopyFromCPU<T>(
            this ArrayView1D<T, Stride1D.General> view,
            AcceleratorStream stream,
            T[] data)
            where T : unmanaged
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (view.HasNoData())
                return;
            if (data.Length < view.Extent.X)
                throw new ArgumentOutOfRangeException(nameof(data));

            var tempBuffer = new T[view.Length];
            fixed (T* ptr = data)
            {
                var span = new ReadOnlySpan<T>(ptr, data.Length);

                // Reorder the input elements and store them in the result buffer
                var extent = (Index1D)view.Extent;
                for (int x = 0; x < extent.X; ++x)
                {
                    int targetElementIndex = view.Stride.ComputeElementIndex(x);
                    tempBuffer[targetElementIndex] = span[x];
                }
            }
            fixed (T* ptr = tempBuffer)
            {
                view.BaseView.CopyFromCPU(
                    stream,
                    new ReadOnlySpan<T>(ptr, tempBuffer.Length));
            }
        }

        /// <summary>
        /// Copies the contents of the 2D array into the given 2D view using the default
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CopyFromCPU<T>(
            this ArrayView2D<T, Stride2D.General> view,
            T[,] data)
            where T : unmanaged =>
            view.CopyFromCPU(view.GetDefaultStream(), data);

        /// <summary>
        /// Copies the contents of the 2D array into the given 2D view using the given
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static unsafe void CopyFromCPU<T>(
            this ArrayView2D<T, Stride2D.General> view,
            AcceleratorStream stream,
            T[,] data)
            where T : unmanaged
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (view.HasNoData())
                return;
            if (data.GetLength(0) < view.Extent.X || data.GetLength(1) < view.Extent.Y)
                throw new ArgumentOutOfRangeException(nameof(data));

            var tempBuffer = new T[view.Length];
            fixed (T* ptr = data)
            {
                var span = new ReadOnlySpan<T>(ptr, data.Length);

                // Reorder the input elements and store them in the result buffer
                var extent = (Index2D)view.Extent;
                for (int x = 0; x < extent.X; ++x)
                {
                    for (int y = 0; y < extent.Y; ++y)
                    {
                        int targetElementIndex = view.Stride.ComputeElementIndex(
                            new Index2D(x, y));
                        int sourceElementIndex = x * (int)view.Extent.Y + y;

                        tempBuffer[targetElementIndex] = span[sourceElementIndex];
                    }
                }
            }
            fixed (T* ptr = tempBuffer)
            {
                view.BaseView.CopyFromCPU(
                    stream,
                    new ReadOnlySpan<T>(ptr, tempBuffer.Length));
            }
        }

        /// <summary>
        /// Copies the contents of the 3D array into the given 3D view using the default
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CopyFromCPU<T>(
            this ArrayView3D<T, Stride3D.General> view,
            T[,,] data)
            where T : unmanaged =>
            view.CopyFromCPU(view.GetDefaultStream(), data);

        /// <summary>
        /// Copies the contents of the 3D array into the given 3D view using the given
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static unsafe void CopyFromCPU<T>(
            this ArrayView3D<T, Stride3D.General> view,
            AcceleratorStream stream,
            T[,,] data)
            where T : unmanaged
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (view.HasNoData())
                return;
            if (data.GetLength(0) < view.Extent.X ||
                data.GetLength(1) < view.Extent.Y ||
                data.GetLength(2) < view.Extent.Z)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }

            var tempBuffer = new T[view.Length];
            fixed (T* ptr = data)
            {
                var span = new ReadOnlySpan<T>(ptr, data.Length);

                // Reorder the input elements and store them in the result buffer
                var extent = (Index3D)view.Extent;
                for (int x = 0; x < extent.X; ++x)
                {
                    for (int y = 0; y < extent.Y; ++y)
                    {
                        for (int z = 0; z < extent.Z; ++z)
                        {
                            int targetElementIndex = view.Stride.ComputeElementIndex(
                                new Index3D(x, y, z));
                            int sourceElementIndex =
                                (x * (int)view.Extent.Y + y) *
                                (int)view.Extent.Z + z;

                            tempBuffer[targetElementIndex] = span[sourceElementIndex];
                        }
                    }
                }
            }
            fixed (T* ptr = tempBuffer)
            {
                view.BaseView.CopyFromCPU(
                    stream,
                    new ReadOnlySpan<T>(ptr, tempBuffer.Length));
            }
        }

        /// <summary>
        /// Copies the contents of the 1D view into the given 1D array using the default
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static unsafe void CopyToCPU<T>(
            this ArrayView1D<T, Stride1D.General> view,
            T[] data)
            where T : unmanaged =>
            view.CopyToCPU(view.GetDefaultStream(), data);

        /// <summary>
        /// Copies the contents of the 1D view into the given 1D array using the given
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static unsafe void CopyToCPU<T>(
            this ArrayView1D<T, Stride1D.General> view,
            AcceleratorStream stream,
            T[] data)
            where T : unmanaged
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length < 1)
                return;
            if (data.Length < view.Extent.X)
                throw new ArgumentOutOfRangeException(nameof(data));

            var tempBuffer = new T[view.BaseView.Length];
            fixed (T* ptr = tempBuffer)
            {
                var span = new Span<T>(ptr, tempBuffer.Length);
                view.BaseView.CopyToCPU(stream, span);

                // Reorder the input elements and store them in the result buffer
                var extent = (Index1D)view.Extent;
                var stride = view.Stride;
                for (int x = 0; x < extent.X; ++x)
                {
                    int elementIndex = stride.ComputeElementIndex(x);
                    data[x] = span[elementIndex];
                }
            }
        }

        /// <summary>
        /// Copies the contents of the 2D view into the given 2D array using the default
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CopyToCPU<T>(
            this ArrayView2D<T, Stride2D.General> view,
            T[,] data)
            where T : unmanaged =>
            view.CopyToCPU(view.GetDefaultStream(), data);

        /// <summary>
        /// Copies the contents of the 2D view into the given 2D array using the given
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static unsafe void CopyToCPU<T>(
            this ArrayView2D<T, Stride2D.General> view,
            AcceleratorStream stream,
            T[,] data)
            where T : unmanaged
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length < 1)
                return;
            if (data.GetLength(0) < view.Extent.X || data.GetLength(1) < view.Extent.Y)
                throw new ArgumentOutOfRangeException(nameof(data));

            var tempBuffer = new T[view.BaseView.Length];
            fixed (T* ptr = tempBuffer)
            {
                var span = new Span<T>(ptr, tempBuffer.Length);
                view.BaseView.CopyToCPU(stream, span);

                // Reorder the input elements and store them in the result buffer
                var extent = (Index2D)view.Extent;
                var stride = view.Stride;
                for (int x = 0; x < extent.X; ++x)
                {
                    for (int y = 0; y < extent.Y; ++y)
                    {
                        var multiDimIndex = new Index2D(x, y);
                        int elementIndex = stride.ComputeElementIndex(multiDimIndex);
                        data[x, y] = span[elementIndex];
                    }
                }
            }
        }

        /// <summary>
        /// Copies the contents of the 3D view into the given 3D array using the default
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CopyToCPU<T>(
            this ArrayView3D<T, Stride3D.General> view,
            T[,,] data)
            where T : unmanaged =>
            view.CopyToCPU(view.GetDefaultStream(), data);

        /// <summary>
        /// Copies the contents of the 3D view into the given 3D array using the given
        /// stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// CAUTION: this method reorders the data on the CPU.
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static unsafe void CopyToCPU<T>(
            this ArrayView3D<T, Stride3D.General> view,
            AcceleratorStream stream,
            T[,,] data)
            where T : unmanaged
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length < 1)
                return;
            if (data.GetLength(0) < view.Extent.X ||
                data.GetLength(1) < view.Extent.Y ||
                data.GetLength(2) < view.Extent.Z)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }

            var tempBuffer = new T[view.BaseView.Length];
            fixed (T* ptr = tempBuffer)
            {
                var span = new Span<T>(ptr, tempBuffer.Length);
                view.BaseView.CopyToCPU(stream, span);

                // Reorder the input elements and store them in the result buffer
                var extent = (Index3D)view.Extent;
                var stride = view.Stride;
                for (int x = 0; x < extent.X; ++x)
                {
                    for (int y = 0; y < extent.Y; ++y)
                    {
                        for (int z = 0; z < extent.Z; ++z)
                        {
                            var multiDimIndex = new Index3D(x, y, z);
                            int elementIndex = stride.ComputeElementIndex(multiDimIndex);
                            data[x, y, z] = span[elementIndex];
                        }
                    }
                }
            }
        }


        #endregion

        #region Copy from/to Spans

        /// <summary>
        /// Copies from the source view into the given CPU data array while
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="span">The CPU data target.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static unsafe void CopyToCPU<T, TView>(
            this TView source,
            AcceleratorStream stream,
            in Span<T> span)
            where TView : IContiguousArrayView<T>
            where T : unmanaged
        {
            if (span.IsEmpty || span.Length < 1)
                return;

            fixed (T* ptr = span)
            {
                source.CopyToCPUUnsafeAsync(
                    stream,
                    ref Unsafe.AsRef<T>(ptr),
                    span.Length);
                stream.Synchronize();
            }
        }

        /// <summary>
        /// Copies from the CPU source span into the given target view while
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="span">The CPU data source.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static unsafe void CopyFromCPU<T, TView>(
            this TView target,
            AcceleratorStream stream,
            in ReadOnlySpan<T> span)
            where TView : IContiguousArrayView<T>
            where T : unmanaged
        {
            if (span.IsEmpty || span.Length < 1)
                return;

            fixed (T* ptr = span)
            {
                target.CopyFromCPUUnsafeAsync(
                    stream,
                    ref Unsafe.AsRef<T>(ptr),
                    span.Length);
                // Upload is fire-and-forget: wait on desktop, no-op on browser (sync-consumed).
                stream.EnsureHostCopyConsumed();
            }
        }

        /// <summary>
        /// Copies from the source view into the given CPU data array using the default
        /// stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="span">The CPU data target.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static unsafe void CopyToCPU<T, TView>(
            this TView source,
            in Span<T> span)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            source.CopyToCPU(source.GetDefaultStream(), span);

        /// <summary>
        /// Copies from the CPU source span into the given target view using the default
        /// stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="span">The CPU data source.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static unsafe void CopyFromCPU<T, TView>(
            this TView target,
            in ReadOnlySpan<T> span)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            target.CopyFromCPU(target.GetDefaultStream(), span);

        #endregion

        #region Copy to/from arrays

        /// <summary>
        /// Copies the contents of the 1D view into the given 1D array using the default
        /// accelerator stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static void CopyToCPU<T>(
            this ArrayView<T> view,
            T[] data)
            where T : unmanaged =>
            ((ArrayView1D<T, Stride1D.Dense>)view).CopyToCPU(data);

        /// <summary>
        /// Copies the contents of the 1D view into the given 1D array using the given
        /// accelerator stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static void CopyToCPU<T>(
            this ArrayView<T> view,
            AcceleratorStream stream,
            T[] data)
            where T : unmanaged =>
            ((ArrayView1D<T, Stride1D.Dense>)view).CopyToCPU(stream, data);

        /// <summary>
        /// Copies the contents of the 1D array into the given 1D view using the default
        /// accelerator stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static void CopyFromCPU<T>(
            this ArrayView<T> view,
            T[] data)
            where T : unmanaged =>
            ((ArrayView1D<T, Stride1D.Dense>)view).CopyFromCPU(data);

        /// <summary>
        /// Copies the contents of the 1D array into the given 1D view using the
        /// given stream.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="data">The target data array.</param>
        /// <remarks>
        /// This method is not supported on accelerators.
        /// </remarks>
        [NotInsideKernel]
        public static void CopyFromCPU<T>(
            this ArrayView<T> view,
            AcceleratorStream stream,
            T[] data)
            where T : unmanaged =>
            ((ArrayView1D<T, Stride1D.Dense>)view).CopyFromCPU(stream, data);

        #endregion

        #region Copy to/from Page Lock async

        /// <summary>
        /// Copies from the source view into the given page locked memory without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="pageLockScope">The page locked memory.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [Obsolete("Use PageLockScope.ArrayView instead")]
        public static void CopyToPageLockedAsync<T, TView>(
            this TView source,
            AcceleratorStream stream,
            PageLockScope<T> pageLockScope)
            where TView : IContiguousArrayView<T>
            where T : unmanaged
        {
            if (pageLockScope == null)
                throw new ArgumentNullException(nameof(pageLockScope));
            if (pageLockScope.LengthInBytes < 1)
                return;

            using var buffer = CPUMemoryBuffer.Create(
                source.GetAccelerator(),
                pageLockScope.AddrOfLockedObject,
                pageLockScope.LengthInBytes,
                Interop.SizeOf<byte>());
            source.Buffer.CopyTo(
                stream,
                source.IndexInBytes,
                buffer.AsRawArrayView());
            if (pageLockScope is NullPageLockScope<T>)
                stream.Synchronize();
        }

        /// <summary>
        /// Copies from the page locked memory into the given target view without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="pageLockScope">The page locked memory.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [Obsolete("Use PageLockScope.ArrayView instead")]
        public static void CopyFromPageLockedAsync<T, TView>(
            this TView target,
            AcceleratorStream stream,
            PageLockScope<T> pageLockScope)
            where TView : IContiguousArrayView<T>
            where T : unmanaged
        {
            if (pageLockScope == null)
                throw new ArgumentNullException(nameof(pageLockScope));
            if (pageLockScope.LengthInBytes < 1)
                return;

            using var buffer = CPUMemoryBuffer.Create(
                target.GetAccelerator(),
                pageLockScope.AddrOfLockedObject,
                pageLockScope.LengthInBytes,
                Interop.SizeOf<byte>());
            target.Buffer.CopyFrom(
                stream,
                buffer.AsRawArrayView(),
                target.IndexInBytes);
            if (pageLockScope is NullPageLockScope<T>)
                stream.Synchronize();
        }

        /// <summary>
        /// Copies from the source view into the given page locked memory without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="pageLockScope">The page locked memory.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [Obsolete("Use PageLockScope.ArrayView instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyToPageLockedAsync<T, TView>(
            this TView source,
            PageLockScope<T> pageLockScope)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            CopyToPageLockedAsync(
                source,
                source.GetDefaultStream(),
                pageLockScope);

        /// <summary>
        /// Copies from the page locked memory into the given target view without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="pageLockScope">The page locked memory.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [Obsolete("Use PageLockScope.ArrayView instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyFromPageLockedAsync<T, TView>(
            this TView target,
            PageLockScope<T> pageLockScope)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            CopyFromPageLockedAsync(
                target,
                target.GetDefaultStream(),
                pageLockScope);

        /// <summary>
        /// Copies from the source view into the given page locked memory without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="pageLockedArray">The page locked memory.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [Obsolete("Use PageLockScope.ArrayView instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyToPageLockedAsync<T, TView>(
            this TView source,
            AcceleratorStream stream,
            PageLockedArray<T> pageLockedArray)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            source.CopyToPageLockedAsync(
                stream,
                pageLockedArray.Scope.AsNotNull());

        /// <summary>
        /// Copies from the page locked memory into the given target view without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="pageLockedArray">The page locked memory.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [Obsolete("Use PageLockScope.ArrayView instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyFromPageLockedAsync<T, TView>(
            this TView target,
            AcceleratorStream stream,
            PageLockedArray<T> pageLockedArray)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            target.CopyFromPageLockedAsync(
                stream,
                pageLockedArray.Scope.AsNotNull());

        /// <summary>
        /// Copies from the source view into the given page locked memory without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="source">The source view instance.</param>
        /// <param name="pageLockedArray">The page locked memory.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [Obsolete("Use PageLockScope.ArrayView instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyToPageLockedAsync<T, TView>(
            this TView source,
            PageLockedArray<T> pageLockedArray)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            source.CopyToPageLockedAsync(pageLockedArray.Scope.AsNotNull());

        /// <summary>
        /// Copies from the page locked memory into the given target view without
        /// synchronizing the current accelerator stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <param name="target">The target view instance.</param>
        /// <param name="pageLockedArray">The page locked memory.</param>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [Obsolete("Use PageLockScope.ArrayView instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyFromPageLockedAsync<T, TView>(
            this TView target,
            PageLockedArray<T> pageLockedArray)
            where TView : IContiguousArrayView<T>
            where T : unmanaged =>
            target.CopyFromPageLockedAsync(pageLockedArray.Scope.AsNotNull());

        #endregion

        #region Array Methods

        /// <summary>
        /// Copies the current contents into a new array using
        /// the default accelerator stream.
        /// </summary>
        /// <param name="view">The source view instance.</param>
        /// <returns>A new array holding the requested contents.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] GetAsArray<T>(this ArrayView<T> view)
            where T : unmanaged =>
            view.GetAsArray(view.GetDefaultStream());

        /// <summary>
        /// Copies the current contents into a new array.
        /// </summary>
        /// <param name="view">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <returns>A new array holding the requested contents.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static T[] GetAsArray<T>(
            this ArrayView<T> view,
            AcceleratorStream stream)
            where T : unmanaged
        {
            if (view.HasNoData())
                return Array.Empty<T>();

            if (view.UsesAutoPageLocking())
            {
                // Extract the managed .Net array from the locked array, as this instance
                // will not be disposed by the using statement.
                using var lockedArray = view.GetAsPageLocked(stream);
                return lockedArray.GetArray();
            }

            var result = new T[view.Length];
            view.CopyToCPU(stream, new Span<T>(result, 0, result.Length));
            return result;
        }

        /// <summary>
        /// Copies the current contents into a new array using
        /// the default accelerator stream.
        /// </summary>
        /// <param name="view">The source view instance.</param>
        /// <returns>A new array holding the requested contents.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PageLockedArray1D<T> GetAsPageLocked<T>(this ArrayView<T> view)
            where T : unmanaged =>
            view.GetAsPageLocked(view.GetDefaultStream());

        /// <summary>
        /// Copies the current contents into a new array.
        /// </summary>
        /// <param name="view">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <returns>A new array holding the requested contents.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static PageLockedArray1D<T> GetAsPageLocked<T>(
            this ArrayView<T> view,
            AcceleratorStream stream)
            where T : unmanaged
        {
            if (view.HasNoData())
                return PageLockedArray1D<T>.Empty;
            var accelerator = view.GetAccelerator();
            var result = accelerator.AllocatePageLocked1D<T>(
                view.Length,
                uninitialized: true);
            view.CopyTo(stream, result.ArrayView);
            stream.Synchronize();
            return result;
        }

        #endregion

        #region Raw Array Methods

        /// <summary>
        /// Copies the current contents into a new byte array.
        /// </summary>
        /// <param name="view">The source view instance.</param>
        /// <returns>A new array holding the requested contents.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static ArraySegment<byte> GetRawData<TView>(this TView view)
            where TView : IContiguousArrayView =>
            view.GetRawData(view.GetDefaultStream());

        /// <summary>
        /// Copies the current contents into a new byte array.
        /// </summary>
        /// <param name="view">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <returns>A new array holding the requested contents.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static ArraySegment<byte> GetRawData<TView>(
            this TView view,
            AcceleratorStream stream)
            where TView : IContiguousArrayView =>
            view.GetRawData(stream, 0, view.LengthInBytes);

        /// <summary>
        /// Copies the current contents into a new byte array.
        /// </summary>
        /// <param name="view">The source view instance.</param>
        /// <param name="byteOffset">The offset within the view in bytes.</param>
        /// <param name="byteExtent">The number of bytes to load.</param>
        /// <returns>A new array holding the requested contents.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static ArraySegment<byte> GetRawData<TView>(
            this TView view,
            long byteOffset,
            long byteExtent)
            where TView : IContiguousArrayView =>
            view.GetRawData(view.GetDefaultStream(), byteOffset, byteExtent);

        /// <summary>
        /// Copies the current contents into a new byte array.
        /// </summary>
        /// <param name="view">The source view instance.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="byteOffset">The offset within the view in bytes.</param>
        /// <param name="byteExtent">The number of bytes to load.</param>
        /// <returns>A new array holding the requested contents.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static unsafe ArraySegment<byte> GetRawData<TView>(
            this TView view,
            AcceleratorStream stream,
            long byteOffset,
            long byteExtent)
            where TView : IContiguousArrayView
        {
            var rawOffset = TypeNode.Align(byteOffset, view.ElementSize);
            var rawExtent = TypeNode.Align(byteExtent, view.ElementSize);

            IndexTypeExtensions.AssertIntIndexRange(rawOffset);
            IndexTypeExtensions.AssertIntIndexRange(rawExtent);

            var result = new byte[rawExtent];
            var rawView = view.AsRawArrayView();
            rawView.CopyToCPU(
                stream,
                new Span<byte>(
                    result,
                    (int)(rawOffset / view.ElementSize),
                    (int)rawExtent));

            return new ArraySegment<byte>(
                result,
                0,
                (int)(byteExtent + (rawExtent - byteExtent)));
        }

        #endregion

        #region Data Allocations

        /// <summary>
        /// Allocates a buffer with the specified content on the given accelerator
        /// using the default stream.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The parent accelerator.</param>
        /// <param name="data">The source CPU data.</param>
        /// <returns>An allocated buffer on this accelerator.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static MemoryBuffer1D<T, Stride1D.Dense> Allocate1D<T>(
            this Accelerator accelerator,
            T[] data)
            where T : unmanaged =>
            Allocate1D<T>(accelerator, accelerator.DefaultStream, data);

        /// <summary>
        /// Allocates a buffer with the specified content on the given accelerator.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The parent accelerator.</param>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="data">The source CPU data.</param>
        /// <returns>An allocated buffer on this accelerator.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public static MemoryBuffer1D<T, Stride1D.Dense> Allocate1D<T>(
            this Accelerator accelerator,
            AcceleratorStream stream,
            T[] data)
            where T : unmanaged
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));

            if (data.Length < 1)
            {
                return new MemoryBuffer1D<T, Stride1D.Dense>(
                    accelerator,
                    ArrayView1D<T, Stride1D.Dense>.Empty);
            }

            // Allocate the raw buffer
            var buffer = accelerator.Allocate1D<T>(data.Length);

            // Copy the data
            buffer.View.CopyFromCPU(stream, data);

            return buffer;
        }

        #endregion

        #region Array/View Casts

        /// <summary>
        /// Converts a raw .Net array into an internal view representation.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="array">The managed array instance.</param>
        /// <returns>The created raw array view.</returns>
        [UtilityIntrinsic(UtilityIntrinsicKind.CastArrayToView)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ArrayView<T> AsRawArrayView<T>(Array array)
            where T : unmanaged
        {
            var arraySource = CPUMemoryBuffer.FromArray(array, Interop.SizeOf<T>());
            return new ArrayView<T>(arraySource, 0L, array.LongLength);
        }

        /// <summary>
        /// Converts the array into a view representation.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="array">The managed array instance.</param>
        /// <returns>The converted array view.</returns>
        /// <remarks>Note that this operation is supported in kernels only.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<T> AsContiguousArrayView<T>(this T[] array)
            where T : unmanaged =>
            AsRawArrayView<T>(array);

        /// <summary>
        /// Converts the array into a view representation.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="array">The managed array instance.</param>
        /// <returns>The converted array view.</returns>
        /// <remarks>Note that this operation is supported in kernels only.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.Dense> AsArrayView<T>(this T[] array)
            where T : unmanaged =>
            AsContiguousArrayView(array);

        /// <summary>
        /// Converts the array into a view representation.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="array">The managed array instance.</param>
        /// <returns>The converted array view.</returns>
        /// <remarks>Note that this operation is supported in kernels only.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView2D<T, Stride2D.DenseY> AsArrayView<T>(this T[,] array)
            where T : unmanaged
        {
            var baseView = AsRawArrayView<T>(array);
            int width = array.GetLength(0);
            int height = array.GetLength(1);
            return new ArrayView2D<T, Stride2D.DenseY>(
                baseView,
                new LongIndex2D(width, height),
                new Stride2D.DenseY(height));
        }

        /// <summary>
        /// Converts the array into a view representation.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="array">The managed array instance.</param>
        /// <returns>The converted array view.</returns>
        /// <remarks>Note that this operation is supported in kernels only.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView3D<T, Stride3D.DenseZY> AsArrayView<T>(this T[,,] array)
            where T : unmanaged
        {
            var baseView = AsRawArrayView<T>(array);
            int width = array.GetLength(0);
            int height = array.GetLength(1);
            int depth = array.GetLength(2);
            return new ArrayView3D<T, Stride3D.DenseZY>(
                baseView,
                new LongIndex3D(width, height, depth),
                new Stride3D.DenseZY(height * depth, depth));
        }

        #endregion
    }

    partial struct ArrayView1D<T, TStride>
    {
        /// <summary>
        /// Converts this array view into a general 1D view.
        /// </summary>
        /// <returns>The converted general 1D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView1D<T, Stride1D.General> To1DView() =>
            new ArrayView1D<T, Stride1D.General>(
                BaseView,
                Extent,
                Stride.To1DStride());

        /// <summary>
        /// Converts this array view into a 2D view.
        /// </summary>
        /// <typeparam name="TOtherStride">The stride type.</typeparam>
        /// <param name="extent">The target extent to use.</param>
        /// <param name="stride">The target stride to use.</param>
        /// <returns>The converted 2D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView2D<T, TOtherStride> As2DView<TOtherStride>(
            LongIndex2D extent,
            TOtherStride stride)
            where TOtherStride : struct, IStride2D
        {
            long size = stride.ComputeBufferLength(extent);
            Trace.Assert(size <= Length, "Extent out of range");
            var baseView = BaseView.SubView(0, size);
            return new ArrayView2D<T, TOtherStride>(
                baseView,
                extent,
                stride);
        }

        /// <summary>
        /// Converts this array view into a 2D view with X being the leading dimension.
        /// </summary>
        /// <param name="extent">The target extent to use.</param>
        /// <returns>The converted 2D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView2D<T, Stride2D.DenseX> As2DDenseXView(LongIndex2D extent)
        {
            IndexTypeExtensions.AssertIntIndexRange(extent.X);
            return As2DView(extent, new Stride2D.DenseX((int)extent.X));
        }

        /// <summary>
        /// Converts this array view into a 2D view with Y being the leading dimension.
        /// </summary>
        /// <param name="extent">The target extent to use.</param>
        /// <returns>The converted 2D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView2D<T, Stride2D.DenseY> As2DDenseYView(LongIndex2D extent)
        {
            IndexTypeExtensions.AssertIntIndexRange(extent.Y);
            return As2DView(extent, new Stride2D.DenseY((int)extent.Y));
        }

        /// <summary>
        /// Converts this array view into a pitched 2D view with X being the leading
        /// dimension.
        /// </summary>
        /// <param name="extent">The target extent to use.</param>
        /// <param name="xAlignmentInBytes">
        /// The alignment in bytes of the leading dimension.
        /// </param>
        /// <returns>The converted 2D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView2D<T, Stride2D.DenseX> As2DPitchedXView(
            LongIndex2D extent,
            int xAlignmentInBytes)
        {
            IndexTypeExtensions.AssertIntIndexRange(extent.X);

            var yStride = StrideExtensions.GetPitchedLeadingDimension<T>(
                extent.X,
                xAlignmentInBytes);
            IndexTypeExtensions.AssertIntIndexRange(yStride);

            return As2DView(extent, new Stride2D.DenseX((int)yStride));
        }

        /// <summary>
        /// Converts this array view into a pitched2D view with Y being the leading
        /// dimension.
        /// </summary>
        /// <param name="extent">The target extent to use.</param>
        /// <param name="yAlignmentInBytes">
        /// The alignment in bytes of the leading dimension.
        /// </param>
        /// <returns>The converted 2D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView2D<T, Stride2D.DenseY> As2DPitchedYView(
            LongIndex2D extent,
            int yAlignmentInBytes)
        {
            IndexTypeExtensions.AssertIntIndexRange(extent.Y);

            var xStride = StrideExtensions.GetPitchedLeadingDimension<T>(
                extent.Y,
                yAlignmentInBytes);
            IndexTypeExtensions.AssertIntIndexRange(xStride);

            return As2DView(extent, new Stride2D.DenseY((int)xStride));
        }

        /// <summary>
        /// Converts the given view into a 3D view.
        /// </summary>
        /// <typeparam name="TOtherStride">The stride type.</typeparam>
        /// <param name="extent">The target extent to use.</param>
        /// <param name="stride">The target stride to use.</param>
        /// <returns>The converted 3D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView3D<T, TOtherStride> As3DView<TOtherStride>(
            LongIndex3D extent,
            TOtherStride stride)
            where TOtherStride : struct, IStride3D
        {
            long size = stride.ComputeBufferLength(extent);
            Trace.Assert(size <= Length, "Extent out of range");
            var baseView = BaseView.SubView(0, size);
            return new ArrayView3D<T, TOtherStride>(
                baseView,
                extent,
                stride);
        }

        /// <summary>
        /// Converts this array view into a 3D view with XY being the leading dimensions.
        /// </summary>
        /// <param name="extent">The target extent to use.</param>
        /// <returns>The converted 3D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView3D<T, Stride3D.DenseXY> As3DDenseXYView(LongIndex3D extent)
        {
            IndexTypeExtensions.AssertIntIndexRange(extent.X);
            IndexTypeExtensions.AssertIntIndexRange(extent.Y);
            IndexTypeExtensions.AssertIntIndexRange(extent.X * extent.Y);
            return As3DView(
                extent,
                new Stride3D.DenseXY((int)extent.X, (int)(extent.X * extent.Y)));
        }

        /// <summary>
        /// Converts this array view into a 3D view with ZY being the leading dimensions.
        /// </summary>
        /// <param name="extent">The target extent to use.</param>
        /// <returns>The converted 3D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView3D<T, Stride3D.DenseZY> As3DDenseZYView(LongIndex3D extent)
        {
            IndexTypeExtensions.AssertIntIndexRange(extent.Y);
            IndexTypeExtensions.AssertIntIndexRange(extent.Z);
            IndexTypeExtensions.AssertIntIndexRange(extent.Y * extent.Z);
            return As3DView(
                extent,
                new Stride3D.DenseZY((int)(extent.Y * extent.Z), (int)extent.Z));
        }

        /// <summary>
        /// Converts this array view into a pitched 3D view with XY being the leading
        /// dimensions.
        /// </summary>
        /// <param name="extent">The target extent to use.</param>
        /// <param name="xyAlignmentInBytes">
        /// The alignment in bytes of the leading dimension.
        /// </param>
        /// <returns>The converted 3D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView3D<T, Stride3D.DenseXY> As3DPitchedXYView(
            LongIndex3D extent,
            int xyAlignmentInBytes)
        {
            IndexTypeExtensions.AssertIntIndexRange(extent.X);
            IndexTypeExtensions.AssertIntIndexRange(extent.Y);
            IndexTypeExtensions.AssertIntIndexRange(extent.X * extent.Y);

            var zStride = StrideExtensions.GetPitchedLeadingDimension<T>(
                extent.X * extent.Y,
                xyAlignmentInBytes);
            IndexTypeExtensions.AssertIntIndexRange(zStride);

            return As3DView(
                extent,
                new Stride3D.DenseXY((int)extent.X, (int)zStride));
        }

        /// <summary>
        /// Converts this array view into a pitched 3D view with ZY being the leading
        /// dimensions.
        /// </summary>
        /// <param name="extent">The target extent to use.</param>
        /// <param name="zyAlignmentInBytes">
        /// The alignment in bytes of the leading dimension.
        /// </param>
        /// <returns>The converted 3D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView3D<T, Stride3D.DenseZY> As3DPitchedZYView(
            LongIndex3D extent,
            int zyAlignmentInBytes)
        {
            IndexTypeExtensions.AssertIntIndexRange(extent.Y);
            IndexTypeExtensions.AssertIntIndexRange(extent.Z);
            IndexTypeExtensions.AssertIntIndexRange(extent.Y * extent.Z);

            var xStride = StrideExtensions.GetPitchedLeadingDimension<T>(
                extent.Y * extent.Z,
                zyAlignmentInBytes);
            IndexTypeExtensions.AssertIntIndexRange(xStride);

            return As3DView(
                extent,
                new Stride3D.DenseZY((int)xStride, (int)extent.Z));
        }
    }

    partial struct ArrayView2D<T, TStride>
    {
        #region Casts

        /// <summary>
        /// Converts this array view into a general 1D view.
        /// </summary>
        /// <returns>The converted general 1D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView1D<T, Stride1D.General> To1DView() =>
            new ArrayView1D<T, Stride1D.General>(
                BaseView,
                Extent.Size,
                Stride.To1DStride());

        /// <summary>
        /// Converts this array view into a dense version with leading dimension X.
        /// </summary>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView2D<T, Stride2D.DenseX> AsDenseX()
        {
            Trace.Assert(Stride.XStride == 1, "Incompatible dense stride");
            return new ArrayView2D<T, Stride2D.DenseX>(
                BaseView,
                Extent,
                new Stride2D.DenseX(Stride.YStride));
        }

        /// <summary>
        /// Converts this array view into a dense version with leading dimension Y.
        /// </summary>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView2D<T, Stride2D.DenseY> AsDenseY()
        {
            Trace.Assert(Stride.YStride == 1, "Incompatible dense stride");
            return new ArrayView2D<T, Stride2D.DenseY>(
                BaseView,
                Extent,
                new Stride2D.DenseY(Stride.XStride));
        }

        /// <summary>
        /// Converts the given view into a 1D view.
        /// </summary>
        /// <typeparam name="TOtherStride">The stride type.</typeparam>
        /// <param name="stride">The target stride to use.</param>
        /// <returns>The converted 1D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView1D<T, TOtherStride> As1DView<TOtherStride>(
            TOtherStride stride)
            where TOtherStride : struct, IStride1D
        {
            long bufferLength = stride.ComputeBufferLength(Length);
            var baseView = BaseView.SubView(0, bufferLength);
            return new ArrayView1D<T, TOtherStride>(
                baseView,
                bufferLength,
                stride);
        }

        /// <summary>
        /// Converts the given view into a 3D view.
        /// </summary>
        /// <typeparam name="TOtherStride">The stride type.</typeparam>
        /// <param name="extent">The extent to use.</param>
        /// <param name="stride">The target stride to use.</param>
        /// <returns>The converted 1D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView3D<T, TOtherStride> As3DView<TOtherStride>(
            LongIndex3D extent,
            TOtherStride stride)
            where TOtherStride : struct, IStride3D
        {
            long bufferLength = stride.ComputeBufferLength(extent);
            var baseView = BaseView.SubView(0, bufferLength);
            return new ArrayView3D<T, TOtherStride>(
                baseView,
                extent,
                stride);
        }

        #endregion
    }

    partial struct ArrayView3D<T, TStride>
    {
        #region Casts

        /// <summary>
        /// Converts this array view into a general 1D view.
        /// </summary>
        /// <returns>The converted general 1D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView1D<T, Stride1D.General> To1DView() =>
            new ArrayView1D<T, Stride1D.General>(
                BaseView,
                Extent.Size,
                Stride.To1DStride());

        /// <summary>
        /// Converts this array view into a dense version with leading dimensions XY.
        /// </summary>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView3D<T, Stride3D.DenseXY> AsDenseXY()
        {
            Trace.Assert(Stride.XStride == 1, "Incompatible dense stride");
            return new ArrayView3D<T, Stride3D.DenseXY>(
                BaseView,
                Extent,
                new Stride3D.DenseXY(Stride.YStride, Stride.YStride * Stride.ZStride));
        }

        /// <summary>
        /// Converts this array view into a dense version with leading dimensions ZY.
        /// </summary>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView3D<T, Stride3D.DenseZY> AsDenseZY()
        {
            Trace.Assert(Stride.ZStride == 1, "Incompatible dense stride");
            return new ArrayView3D<T, Stride3D.DenseZY>(
                BaseView,
                Extent,
                new Stride3D.DenseZY(Stride.XStride * Stride.YStride, Stride.YStride));
        }

        /// <summary>
        /// Converts the given view into a 1D view.
        /// </summary>
        /// <typeparam name="TOtherStride">The stride type.</typeparam>
        /// <param name="stride">The target stride to use.</param>
        /// <returns>The converted 1D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView1D<T, TOtherStride> As1DView<TOtherStride>(
            TOtherStride stride)
            where TOtherStride : struct, IStride1D
        {
            long bufferLength = stride.ComputeBufferLength(Length);
            var baseView = BaseView.SubView(0, bufferLength);
            return new ArrayView1D<T, TOtherStride>(
                baseView,
                bufferLength,
                stride);
        }

        /// <summary>
        /// Converts the given view into a 3D view.
        /// </summary>
        /// <typeparam name="TOtherStride">The stride type.</typeparam>
        /// <param name="extent">The extent to use.</param>
        /// <param name="stride">The target stride to use.</param>
        /// <returns>The converted 1D view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView3D<T, TOtherStride> As3DView<TOtherStride>(
            LongIndex3D extent,
            TOtherStride stride)
            where TOtherStride : struct, IStride3D
        {
            long bufferLength = stride.ComputeBufferLength(extent);
            var baseView = BaseView.SubView(0, bufferLength);
            return new ArrayView3D<T, TOtherStride>(
                baseView,
                extent,
                stride);
        }

        #endregion
    }
}
