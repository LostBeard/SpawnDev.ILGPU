// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2022 ILGPU Project
//                                    www.ilgpu.net
//
// File: ArrayViews.tt/ArrayViews.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: TypeInformation.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------


using ILGPU.Util;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ILGPU.Runtime
{
    partial class ArrayViewExtensions
    {
        /// <summary>
        /// Aligns the given array view to the alignment of 16 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo16<T>(
            this ArrayView<T> view)
            where T : unmanaged =>
            AlignTo(view, 16);

        /// <summary>
        /// Aligns the given array view to the alignment of 16 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo16<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AlignTo(view.BaseView, 16);

        /// <summary>
        /// Ensures that the array view is aligned to 16 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<T> AsAligned16<T>(this ArrayView<T> view)
            where T : unmanaged =>
            AsAligned(view, 16);

        /// <summary>
        /// Ensures that the array view is aligned to 16 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.Dense> AsAligned16<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AsAligned(view.BaseView, 16);

        /// <summary>
        /// Aligns the given array view to the alignment of 32 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo32<T>(
            this ArrayView<T> view)
            where T : unmanaged =>
            AlignTo(view, 32);

        /// <summary>
        /// Aligns the given array view to the alignment of 32 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo32<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AlignTo(view.BaseView, 32);

        /// <summary>
        /// Ensures that the array view is aligned to 32 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<T> AsAligned32<T>(this ArrayView<T> view)
            where T : unmanaged =>
            AsAligned(view, 32);

        /// <summary>
        /// Ensures that the array view is aligned to 32 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.Dense> AsAligned32<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AsAligned(view.BaseView, 32);

        /// <summary>
        /// Aligns the given array view to the alignment of 64 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo64<T>(
            this ArrayView<T> view)
            where T : unmanaged =>
            AlignTo(view, 64);

        /// <summary>
        /// Aligns the given array view to the alignment of 64 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo64<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AlignTo(view.BaseView, 64);

        /// <summary>
        /// Ensures that the array view is aligned to 64 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<T> AsAligned64<T>(this ArrayView<T> view)
            where T : unmanaged =>
            AsAligned(view, 64);

        /// <summary>
        /// Ensures that the array view is aligned to 64 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.Dense> AsAligned64<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AsAligned(view.BaseView, 64);

        /// <summary>
        /// Aligns the given array view to the alignment of 128 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo128<T>(
            this ArrayView<T> view)
            where T : unmanaged =>
            AlignTo(view, 128);

        /// <summary>
        /// Aligns the given array view to the alignment of 128 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo128<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AlignTo(view.BaseView, 128);

        /// <summary>
        /// Ensures that the array view is aligned to 128 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<T> AsAligned128<T>(this ArrayView<T> view)
            where T : unmanaged =>
            AsAligned(view, 128);

        /// <summary>
        /// Ensures that the array view is aligned to 128 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.Dense> AsAligned128<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AsAligned(view.BaseView, 128);

        /// <summary>
        /// Aligns the given array view to the alignment of 256 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo256<T>(
            this ArrayView<T> view)
            where T : unmanaged =>
            AlignTo(view, 256);

        /// <summary>
        /// Aligns the given array view to the alignment of 256 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo256<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AlignTo(view.BaseView, 256);

        /// <summary>
        /// Ensures that the array view is aligned to 256 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<T> AsAligned256<T>(this ArrayView<T> view)
            where T : unmanaged =>
            AsAligned(view, 256);

        /// <summary>
        /// Ensures that the array view is aligned to 256 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.Dense> AsAligned256<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AsAligned(view.BaseView, 256);

        /// <summary>
        /// Aligns the given array view to the alignment of 512 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo512<T>(
            this ArrayView<T> view)
            where T : unmanaged =>
            AlignTo(view, 512);

        /// <summary>
        /// Aligns the given array view to the alignment of 512 bytes and
        /// returns a view spanning the initial unaligned parts of the given view and
        /// another view (main) spanning the remaining aligned elements of the given view.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>
        /// The prefix and main views pointing to non-aligned and aligned sub-views of
        /// the given view.
        /// </returns>
        public static (ArrayView<T> Prefix, ArrayView<T> Main) AlignTo512<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AlignTo(view.BaseView, 512);

        /// <summary>
        /// Ensures that the array view is aligned to 512 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView<T> AsAligned512<T>(this ArrayView<T> view)
            where T : unmanaged =>
            AsAligned(view, 512);

        /// <summary>
        /// Ensures that the array view is aligned to 512 of bytes
        /// and returns the input view. Note that this operation explicitly generates an
        /// operation in the ILGPU IR that preserves these semantics. This enables the
        /// generation of debug assertions and guides the internal vectorization analysis
        /// to assume the given alignment even though it might not be able to prove that
        /// the given alignment is valid.
        /// </summary>
        /// <param name="view">The source view.</param>
        /// <returns>The validated input view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArrayView1D<T, Stride1D.Dense> AsAligned512<T>(
            this ArrayView1D<T, Stride1D.Dense> view)
            where T : unmanaged =>
            AsAligned(view.BaseView, 512);

    }

    /// <summary>
    /// Represents a general view to an array on an accelerator.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TStride">The 1D stride type.</typeparam>
    [DebuggerTypeProxy(typeof(DebugArrayView1D<,>))]
    [DebuggerDisplay("Extent = {Extent}, Stride = {Stride}, Length = {Length}")]
    [StructLayout(LayoutKind.Sequential)]
    public readonly partial struct ArrayView1D<T, TStride> :
        IArrayView<T, Index1D, LongIndex1D, TStride>
        where T : unmanaged
        where TStride : struct, IStride1D
    {
        #region Static

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        public static readonly int ElementSize = ArrayView<T>.ElementSize;

        /// <summary>
        /// Represents an empty view that is not valid and has a length of 0 elements
        /// and a default stride initialized with its default value.
        /// </summary>
        public static readonly ArrayView1D<T, TStride> Empty = GetEmpty(default);

        /// <summary>
        /// Returns an empty view using the given stride.
        /// </summary>
        /// <param name="stride">The stride to use.</param>
        /// <returns>The created empty view instance.</returns>
        public static ArrayView1D<T, TStride> GetEmpty(TStride stride) =>
            new ArrayView1D<T, TStride>(
                ArrayView<T>.Empty,
                LongIndex1D.Zero,
                stride);

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new 1D array view.
        /// </summary>
        /// <param name="baseView">The source view.</param>
        /// <param name="extent">The extent (number of elements).</param>
        /// <param name="stride">The stride to use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView1D(
            ArrayView<T> baseView,
            LongIndex1D extent,
            TStride stride)
        {
            BaseView = baseView;
            Extent = extent;
            Stride = stride;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated buffer.
        /// </summary>
        public ArrayView<T> BaseView { get; }

        /// <summary>
        /// Returns the underlying memory buffer.
        /// </summary>
        /// <remarks>This property is not supported on accelerators.</remarks>
        readonly MemoryBuffer IArrayView.Buffer
        {
            [NotInsideKernel]
            get => BaseView.Buffer;
        }

        /// <summary>
        /// Returns the underlying element size.
        /// </summary>
        readonly int IArrayView.ElementSize => ElementSize;

        /// <summary>
        /// Returns the extent of this view.
        /// </summary>
        public LongIndex1D Extent { get; }

        /// <summary>
        /// Returns the 32-bit extent of this view.
        /// </summary>
        public readonly Index1D IntExtent => Extent.ToIntIndex();

        /// <summary>
        /// Returns the associated stride.
        /// </summary>
        public TStride Stride { get; }

        /// <summary>
        /// Returns true if this view points to a valid location.
        /// </summary>
        public readonly bool IsValid => BaseView.IsValid;

        /// <summary>
        /// Returns the strided length of this array view.
        /// </summary>
        public readonly long Length => Stride.ComputeBufferLength(Extent);

        /// <summary>
        /// Returns the strided 32-bit length of this array view.
        /// </summary>
        public readonly int IntLength
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                long result = Length;
                IndexTypeExtensions.AssertIntIndexRange(result);
                return (int)result;
            }
        }

        /// <summary>
        /// Returns the length of this array view in bytes. Sub-byte packed types (the 4-bit
        /// Int4 / UInt4 / Float4E2M1) report the packed ceil(Length * BitsPerElement / 8).
        /// </summary>
        public readonly long LengthInBytes =>
            (Length * (long)ArrayView<T>.BitsPerElement + 7) / 8;

        /// <summary>
        /// Access the element at the given 32-bit index.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[Index1D index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref BaseView[ComputeLinearIndex(index)];
        }


        /// <summary>
        /// Access the element at the given 64-bit index.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[LongIndex1D index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref BaseView[ComputeLinearIndex(index)];
        }


        #endregion

        #region Methods

        /// <summary>
        /// Computes the linear 32-bit element address using the given index.
        /// </summary>
        /// <param name="index">The element index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int ComputeLinearIndex(Index1D index) =>
            Stride.ComputeElementIndexChecked(index, IntExtent);

        /// <summary>
        /// Returns a sub view of the current view starting at the given
        /// 32-bit offset.
        /// </summary>
        /// <param name="index">The starting offset.</param>
        /// <param name="extent">The extent of the new sub view.</param>
        /// <returns>The raw sub view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView1D<T, TStride> SubView(
            Index1D index,
            Index1D extent)
        {
            Trace.Assert(
                Bitwise.And(
                    index.X >= 0,
                    Bitwise.Or(
                        index.X + extent.X <= (int)Extent.X,
                        Bitwise.And(index.X == 0, extent.X == 0))),
                "Index/Extent X out of bounds");
            int offset = ComputeLinearIndex(index);
            int length = Stride.ComputeBufferLength(extent);
            var view = BaseView.SubView(offset, length);
            return new ArrayView1D<T, TStride>(
                view,
                extent,
                Stride);
        }

        /// <summary>
        /// Computes the linear 64-bit element address using the given index.
        /// </summary>
        /// <param name="index">The element index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly long ComputeLinearIndex(LongIndex1D index) =>
            Stride.ComputeElementIndexChecked(index, Extent);

        /// <summary>
        /// Returns a sub view of the current view starting at the given
        /// 64-bit offset.
        /// </summary>
        /// <param name="index">The starting offset.</param>
        /// <param name="extent">The extent of the new sub view.</param>
        /// <returns>The raw sub view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView1D<T, TStride> SubView(
            LongIndex1D index,
            LongIndex1D extent)
        {
            Trace.Assert(
                Bitwise.And(
                    index.X >= 0,
                    Bitwise.Or(
                        index.X + extent.X <= (long)Extent.X,
                        Bitwise.And(index.X == 0, extent.X == 0))),
                "Index/Extent X out of bounds");
            long offset = ComputeLinearIndex(index);
            long length = Stride.ComputeBufferLength(extent);
            var view = BaseView.SubView(offset, length);
            return new ArrayView1D<T, TStride>(
                view,
                extent,
                Stride);
        }


        /// <summary>
        /// Returns a variable view that points to the element at the specified index.
        /// </summary>
        /// <param name="index">The variable index.</param>
        /// <returns>The resolved variable view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VariableView<T> VariableView(Index1D index) =>
            BaseView.VariableView(ComputeLinearIndex(index));

        /// <summary>
        /// Returns a variable view that points to the element at the specified index.
        /// </summary>
        /// <param name="index">The variable index.</param>
        /// <returns>The resolved variable view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VariableView<T> VariableView(LongIndex1D index) =>
            BaseView.VariableView(ComputeLinearIndex(index));

        /// <summary>
        /// Returns a contiguous view to this view.
        /// </summary>
        public readonly ArrayView<T> AsContiguous() => BaseView;

        /// <summary>
        /// Converts this array view into a dense version.
        /// </summary>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView1D<T, Stride1D.Dense> AsDense()
        {
            Trace.Assert(
                Stride.StrideExtent == Index1D.One,
                "Incompatible dense stride");
            return new ArrayView1D<T, Stride1D.Dense>(
                BaseView,
                Extent,
                default);
        }

        /// <summary>
        /// Converts this array view into a general version.
        /// </summary>
        /// <param name="stride">The generic stride information to use.</param>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView1D<T, Stride1D.General> AsGeneral(
            Stride1D.General stride) =>
            new ArrayView1D<T, Stride1D.General>(
                BaseView,
                Extent,
                stride);

        /// <summary>
        /// Converts this array view into a general version.
        /// </summary>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView1D<T, Stride1D.General> AsGeneral() =>
            AsGeneral(Stride.AsGeneral());

        #endregion

        #region Object

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public readonly override string ToString() => BaseView.ToString();

        #endregion
    }

    /// <summary>
    /// Represents a general view to an array on an accelerator.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TStride">The 2D stride type.</typeparam>
    [DebuggerTypeProxy(typeof(DebugArrayView2D<,>))]
    [DebuggerDisplay("Extent = {Extent}, Stride = {Stride}, Length = {Length}")]
    [StructLayout(LayoutKind.Sequential)]
    public readonly partial struct ArrayView2D<T, TStride> :
        IArrayView<T, Index2D, LongIndex2D, TStride>
        where T : unmanaged
        where TStride : struct, IStride2D
    {
        #region Static

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        public static readonly int ElementSize = ArrayView<T>.ElementSize;

        /// <summary>
        /// Represents an empty view that is not valid and has a length of 0 elements
        /// and a default stride initialized with its default value.
        /// </summary>
        public static readonly ArrayView2D<T, TStride> Empty = GetEmpty(default);

        /// <summary>
        /// Returns an empty view using the given stride.
        /// </summary>
        /// <param name="stride">The stride to use.</param>
        /// <returns>The created empty view instance.</returns>
        public static ArrayView2D<T, TStride> GetEmpty(TStride stride) =>
            new ArrayView2D<T, TStride>(
                ArrayView<T>.Empty,
                LongIndex2D.Zero,
                stride);

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new 2D array view.
        /// </summary>
        /// <param name="baseView">The source view.</param>
        /// <param name="extent">The extent (number of elements).</param>
        /// <param name="stride">The stride to use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView2D(
            ArrayView<T> baseView,
            LongIndex2D extent,
            TStride stride)
        {
            BaseView = baseView;
            Extent = extent;
            Stride = stride;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated buffer.
        /// </summary>
        public ArrayView<T> BaseView { get; }

        /// <summary>
        /// Returns the underlying memory buffer.
        /// </summary>
        /// <remarks>This property is not supported on accelerators.</remarks>
        readonly MemoryBuffer IArrayView.Buffer
        {
            [NotInsideKernel]
            get => BaseView.Buffer;
        }

        /// <summary>
        /// Returns the underlying element size.
        /// </summary>
        readonly int IArrayView.ElementSize => ElementSize;

        /// <summary>
        /// Returns the extent of this view.
        /// </summary>
        public LongIndex2D Extent { get; }

        /// <summary>
        /// Returns the 32-bit extent of this view.
        /// </summary>
        public readonly Index2D IntExtent => Extent.ToIntIndex();

        /// <summary>
        /// Returns the associated stride.
        /// </summary>
        public TStride Stride { get; }

        /// <summary>
        /// Returns true if this view points to a valid location.
        /// </summary>
        public readonly bool IsValid => BaseView.IsValid;

        /// <summary>
        /// Returns the strided length of this array view.
        /// </summary>
        public readonly long Length => Stride.ComputeBufferLength(Extent);

        /// <summary>
        /// Returns the strided 32-bit length of this array view.
        /// </summary>
        public readonly int IntLength
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                long result = Length;
                IndexTypeExtensions.AssertIntIndexRange(result);
                return (int)result;
            }
        }

        /// <summary>
        /// Returns the length of this array view in bytes. Sub-byte packed types (the 4-bit
        /// Int4 / UInt4 / Float4E2M1) report the packed ceil(Length * BitsPerElement / 8).
        /// </summary>
        public readonly long LengthInBytes =>
            (Length * (long)ArrayView<T>.BitsPerElement + 7) / 8;

        /// <summary>
        /// Access the element at the given 32-bit index.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[Index2D index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref BaseView[ComputeLinearIndex(index)];
        }

        /// <summary>
        /// Access the element at the given 32-bit index.
        /// </summary>
        /// <param name="x">
        /// The X index.
        /// </param>
        /// <param name="y">
        /// The Y index.
        /// </param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[
            int x, int y]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref this[new Index2D(x, y)];
        }

        /// <summary>
        /// Access the element at the given 64-bit index.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[LongIndex2D index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref BaseView[ComputeLinearIndex(index)];
        }

        /// <summary>
        /// Access the element at the given 64-bit index.
        /// </summary>
        /// <param name="x">
        /// The X index.
        /// </param>
        /// <param name="y">
        /// The Y index.
        /// </param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[
            long x, long y]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref this[new LongIndex2D(x, y)];
        }

        #endregion

        #region Methods

        /// <summary>
        /// Computes the linear 32-bit element address using the given index.
        /// </summary>
        /// <param name="index">The element index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int ComputeLinearIndex(Index2D index) =>
            Stride.ComputeElementIndexChecked(index, IntExtent);

        /// <summary>
        /// Returns a sub view of the current view starting at the given
        /// 32-bit offset.
        /// </summary>
        /// <param name="index">The starting offset.</param>
        /// <param name="extent">The extent of the new sub view.</param>
        /// <returns>The raw sub view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView2D<T, TStride> SubView(
            Index2D index,
            Index2D extent)
        {
            Trace.Assert(
                Bitwise.And(
                    index.X >= 0,
                    Bitwise.Or(
                        index.X + extent.X <= (int)Extent.X,
                        Bitwise.And(index.X == 0, extent.X == 0))),
                "Index/Extent X out of bounds");
            Trace.Assert(
                Bitwise.And(
                    index.Y >= 0,
                    Bitwise.Or(
                        index.Y + extent.Y <= (int)Extent.Y,
                        Bitwise.And(index.Y == 0, extent.Y == 0))),
                "Index/Extent Y out of bounds");
            int offset = ComputeLinearIndex(index);
            int length = Stride.ComputeBufferLength(extent);
            var view = BaseView.SubView(offset, length);
            return new ArrayView2D<T, TStride>(
                view,
                extent,
                Stride);
        }

        /// <summary>
        /// Computes the linear 64-bit element address using the given index.
        /// </summary>
        /// <param name="index">The element index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly long ComputeLinearIndex(LongIndex2D index) =>
            Stride.ComputeElementIndexChecked(index, Extent);

        /// <summary>
        /// Returns a sub view of the current view starting at the given
        /// 64-bit offset.
        /// </summary>
        /// <param name="index">The starting offset.</param>
        /// <param name="extent">The extent of the new sub view.</param>
        /// <returns>The raw sub view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView2D<T, TStride> SubView(
            LongIndex2D index,
            LongIndex2D extent)
        {
            Trace.Assert(
                Bitwise.And(
                    index.X >= 0,
                    Bitwise.Or(
                        index.X + extent.X <= (long)Extent.X,
                        Bitwise.And(index.X == 0, extent.X == 0))),
                "Index/Extent X out of bounds");
            Trace.Assert(
                Bitwise.And(
                    index.Y >= 0,
                    Bitwise.Or(
                        index.Y + extent.Y <= (long)Extent.Y,
                        Bitwise.And(index.Y == 0, extent.Y == 0))),
                "Index/Extent Y out of bounds");
            long offset = ComputeLinearIndex(index);
            long length = Stride.ComputeBufferLength(extent);
            var view = BaseView.SubView(offset, length);
            return new ArrayView2D<T, TStride>(
                view,
                extent,
                Stride);
        }


        /// <summary>
        /// Returns a variable view that points to the element at the specified index.
        /// </summary>
        /// <param name="index">The variable index.</param>
        /// <returns>The resolved variable view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VariableView<T> VariableView(Index2D index) =>
            BaseView.VariableView(ComputeLinearIndex(index));

        /// <summary>
        /// Returns a variable view that points to the element at the specified index.
        /// </summary>
        /// <param name="index">The variable index.</param>
        /// <returns>The resolved variable view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VariableView<T> VariableView(LongIndex2D index) =>
            BaseView.VariableView(ComputeLinearIndex(index));

        /// <summary>
        /// Returns a contiguous view to this view.
        /// </summary>
        public readonly ArrayView<T> AsContiguous() => BaseView;

        /// <summary>
        /// Converts this array view into a general version.
        /// </summary>
        /// <param name="stride">The generic stride information to use.</param>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView2D<T, Stride2D.General> AsGeneral(
            Stride2D.General stride) =>
            new ArrayView2D<T, Stride2D.General>(
                BaseView,
                Extent,
                stride);

        /// <summary>
        /// Converts this array view into a general version.
        /// </summary>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView2D<T, Stride2D.General> AsGeneral() =>
            AsGeneral(Stride.AsGeneral());

        #endregion

        #region Object

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public readonly override string ToString() => BaseView.ToString();

        #endregion
    }

    /// <summary>
    /// Represents a general view to an array on an accelerator.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TStride">The 3D stride type.</typeparam>
    [DebuggerTypeProxy(typeof(DebugArrayView3D<,>))]
    [DebuggerDisplay("Extent = {Extent}, Stride = {Stride}, Length = {Length}")]
    [StructLayout(LayoutKind.Sequential)]
    public readonly partial struct ArrayView3D<T, TStride> :
        IArrayView<T, Index3D, LongIndex3D, TStride>
        where T : unmanaged
        where TStride : struct, IStride3D
    {
        #region Static

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        public static readonly int ElementSize = ArrayView<T>.ElementSize;

        /// <summary>
        /// Represents an empty view that is not valid and has a length of 0 elements
        /// and a default stride initialized with its default value.
        /// </summary>
        public static readonly ArrayView3D<T, TStride> Empty = GetEmpty(default);

        /// <summary>
        /// Returns an empty view using the given stride.
        /// </summary>
        /// <param name="stride">The stride to use.</param>
        /// <returns>The created empty view instance.</returns>
        public static ArrayView3D<T, TStride> GetEmpty(TStride stride) =>
            new ArrayView3D<T, TStride>(
                ArrayView<T>.Empty,
                LongIndex3D.Zero,
                stride);

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new 3D array view.
        /// </summary>
        /// <param name="baseView">The source view.</param>
        /// <param name="extent">The extent (number of elements).</param>
        /// <param name="stride">The stride to use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView3D(
            ArrayView<T> baseView,
            LongIndex3D extent,
            TStride stride)
        {
            BaseView = baseView;
            Extent = extent;
            Stride = stride;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated buffer.
        /// </summary>
        public ArrayView<T> BaseView { get; }

        /// <summary>
        /// Returns the underlying memory buffer.
        /// </summary>
        /// <remarks>This property is not supported on accelerators.</remarks>
        readonly MemoryBuffer IArrayView.Buffer
        {
            [NotInsideKernel]
            get => BaseView.Buffer;
        }

        /// <summary>
        /// Returns the underlying element size.
        /// </summary>
        readonly int IArrayView.ElementSize => ElementSize;

        /// <summary>
        /// Returns the extent of this view.
        /// </summary>
        public LongIndex3D Extent { get; }

        /// <summary>
        /// Returns the 32-bit extent of this view.
        /// </summary>
        public readonly Index3D IntExtent => Extent.ToIntIndex();

        /// <summary>
        /// Returns the associated stride.
        /// </summary>
        public TStride Stride { get; }

        /// <summary>
        /// Returns true if this view points to a valid location.
        /// </summary>
        public readonly bool IsValid => BaseView.IsValid;

        /// <summary>
        /// Returns the strided length of this array view.
        /// </summary>
        public readonly long Length => Stride.ComputeBufferLength(Extent);

        /// <summary>
        /// Returns the strided 32-bit length of this array view.
        /// </summary>
        public readonly int IntLength
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                long result = Length;
                IndexTypeExtensions.AssertIntIndexRange(result);
                return (int)result;
            }
        }

        /// <summary>
        /// Returns the length of this array view in bytes. Sub-byte packed types (the 4-bit
        /// Int4 / UInt4 / Float4E2M1) report the packed ceil(Length * BitsPerElement / 8).
        /// </summary>
        public readonly long LengthInBytes =>
            (Length * (long)ArrayView<T>.BitsPerElement + 7) / 8;

        /// <summary>
        /// Access the element at the given 32-bit index.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[Index3D index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref BaseView[ComputeLinearIndex(index)];
        }

        /// <summary>
        /// Access the element at the given 32-bit index.
        /// </summary>
        /// <param name="x">
        /// The X index.
        /// </param>
        /// <param name="y">
        /// The Y index.
        /// </param>
        /// <param name="z">
        /// The Z index.
        /// </param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[
            int x, int y, int z]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref this[new Index3D(x, y, z)];
        }

        /// <summary>
        /// Access the element at the given 64-bit index.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[LongIndex3D index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref BaseView[ComputeLinearIndex(index)];
        }

        /// <summary>
        /// Access the element at the given 64-bit index.
        /// </summary>
        /// <param name="x">
        /// The X index.
        /// </param>
        /// <param name="y">
        /// The Y index.
        /// </param>
        /// <param name="z">
        /// The Z index.
        /// </param>
        /// <returns>The element at the given index.</returns>
        public readonly ref T this[
            long x, long y, long z]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref this[new LongIndex3D(x, y, z)];
        }

        #endregion

        #region Methods

        /// <summary>
        /// Computes the linear 32-bit element address using the given index.
        /// </summary>
        /// <param name="index">The element index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int ComputeLinearIndex(Index3D index) =>
            Stride.ComputeElementIndexChecked(index, IntExtent);

        /// <summary>
        /// Returns a sub view of the current view starting at the given
        /// 32-bit offset.
        /// </summary>
        /// <param name="index">The starting offset.</param>
        /// <param name="extent">The extent of the new sub view.</param>
        /// <returns>The raw sub view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView3D<T, TStride> SubView(
            Index3D index,
            Index3D extent)
        {
            Trace.Assert(
                Bitwise.And(
                    index.X >= 0,
                    Bitwise.Or(
                        index.X + extent.X <= (int)Extent.X,
                        Bitwise.And(index.X == 0, extent.X == 0))),
                "Index/Extent X out of bounds");
            Trace.Assert(
                Bitwise.And(
                    index.Y >= 0,
                    Bitwise.Or(
                        index.Y + extent.Y <= (int)Extent.Y,
                        Bitwise.And(index.Y == 0, extent.Y == 0))),
                "Index/Extent Y out of bounds");
            Trace.Assert(
                Bitwise.And(
                    index.Z >= 0,
                    Bitwise.Or(
                        index.Z + extent.Z <= (int)Extent.Z,
                        Bitwise.And(index.Z == 0, extent.Z == 0))),
                "Index/Extent Z out of bounds");
            int offset = ComputeLinearIndex(index);
            int length = Stride.ComputeBufferLength(extent);
            var view = BaseView.SubView(offset, length);
            return new ArrayView3D<T, TStride>(
                view,
                extent,
                Stride);
        }

        /// <summary>
        /// Computes the linear 64-bit element address using the given index.
        /// </summary>
        /// <param name="index">The element index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly long ComputeLinearIndex(LongIndex3D index) =>
            Stride.ComputeElementIndexChecked(index, Extent);

        /// <summary>
        /// Returns a sub view of the current view starting at the given
        /// 64-bit offset.
        /// </summary>
        /// <param name="index">The starting offset.</param>
        /// <param name="extent">The extent of the new sub view.</param>
        /// <returns>The raw sub view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView3D<T, TStride> SubView(
            LongIndex3D index,
            LongIndex3D extent)
        {
            Trace.Assert(
                Bitwise.And(
                    index.X >= 0,
                    Bitwise.Or(
                        index.X + extent.X <= (long)Extent.X,
                        Bitwise.And(index.X == 0, extent.X == 0))),
                "Index/Extent X out of bounds");
            Trace.Assert(
                Bitwise.And(
                    index.Y >= 0,
                    Bitwise.Or(
                        index.Y + extent.Y <= (long)Extent.Y,
                        Bitwise.And(index.Y == 0, extent.Y == 0))),
                "Index/Extent Y out of bounds");
            Trace.Assert(
                Bitwise.And(
                    index.Z >= 0,
                    Bitwise.Or(
                        index.Z + extent.Z <= (long)Extent.Z,
                        Bitwise.And(index.Z == 0, extent.Z == 0))),
                "Index/Extent Z out of bounds");
            long offset = ComputeLinearIndex(index);
            long length = Stride.ComputeBufferLength(extent);
            var view = BaseView.SubView(offset, length);
            return new ArrayView3D<T, TStride>(
                view,
                extent,
                Stride);
        }


        /// <summary>
        /// Returns a variable view that points to the element at the specified index.
        /// </summary>
        /// <param name="index">The variable index.</param>
        /// <returns>The resolved variable view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VariableView<T> VariableView(Index3D index) =>
            BaseView.VariableView(ComputeLinearIndex(index));

        /// <summary>
        /// Returns a variable view that points to the element at the specified index.
        /// </summary>
        /// <param name="index">The variable index.</param>
        /// <returns>The resolved variable view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VariableView<T> VariableView(LongIndex3D index) =>
            BaseView.VariableView(ComputeLinearIndex(index));

        /// <summary>
        /// Returns a contiguous view to this view.
        /// </summary>
        public readonly ArrayView<T> AsContiguous() => BaseView;

        /// <summary>
        /// Converts this array view into a general version.
        /// </summary>
        /// <param name="stride">The generic stride information to use.</param>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView3D<T, Stride3D.General> AsGeneral(
            Stride3D.General stride) =>
            new ArrayView3D<T, Stride3D.General>(
                BaseView,
                Extent,
                stride);

        /// <summary>
        /// Converts this array view into a general version.
        /// </summary>
        /// <returns>The updated array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ArrayView3D<T, Stride3D.General> AsGeneral() =>
            AsGeneral(Stride.AsGeneral());

        #endregion

        #region Object

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        /// <remarks>This method is not supported on accelerators.</remarks>
        [NotInsideKernel]
        public readonly override string ToString() => BaseView.ToString();

        #endregion
    }

}