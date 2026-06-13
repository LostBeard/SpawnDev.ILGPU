// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2022 ILGPU Project
//                                    www.ilgpu.net
//
// File: ScanExtensions.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Algorithms.Resources;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static ILGPU.Algorithms.GroupExtensions;

// disable: max_line_length

namespace ILGPU.Algorithms
{
    /// <summary>
    /// Represents the scan operation type.
    /// </summary>
    public enum ScanKind
    {
        /// <summary>
        /// An inclusive scan operation.
        /// </summary>
        Inclusive,

        /// <summary>
        /// An exclusive scan operation.
        /// </summary>
        Exclusive
    }

    #region Scan Delegates

    /// <summary>
    /// Represents a scan operation using a shuffle and operation logic.
    /// </summary>
    /// <typeparam name="T">The underlying type of the scan operation.</typeparam>
    /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
    /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
    /// <param name="stream">The accelerator stream.</param>
    /// <param name="input">The input elements to scan.</param>
    /// <param name="output">The output view to store the scanned values.</param>
    /// <param name="temp">The temp view to store temporary results.</param>
    public delegate void Scan<T, TStrideIn, TStrideOut>(
        AcceleratorStream stream,
        ArrayView1D<T, TStrideIn> input,
        ArrayView1D<T, TStrideOut> output,
        ArrayView<int> temp)
        where T : unmanaged
        where TStrideIn : struct, IStride1D
        where TStrideOut : struct, IStride1D;

    /// <summary>
    /// Represents a scan operation using a shuffle and operation logic.
    /// </summary>
    /// <typeparam name="T">The underlying type of the scan operation.</typeparam>
    /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
    /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
    /// <param name="stream">The accelerator stream.</param>
    /// <param name="input">The input elements to scan.</param>
    /// <param name="output">The output view to store the scanned values.</param>
    public delegate void BufferedScan<T, TStrideIn, TStrideOut>(
        AcceleratorStream stream,
        ArrayView1D<T, TStrideIn> input,
        ArrayView1D<T, TStrideOut> output)
        where T : unmanaged
        where TStrideIn : struct, IStride1D
        where TStrideOut : struct, IStride1D;

    #endregion

    /// <summary>
    /// Represents a scan provider for a scan operation.
    /// </summary>
    /// <remarks>Members of this class are not thread safe.</remarks>
    public sealed class ScanProvider : AlgorithmObject
    {
        #region Instance

        [SuppressMessage(
            "Microsoft.Usage",
            "CA2213: Disposable fields should be disposed",
            Justification = "This is disposed in DisposeAccelerator")]
        private readonly MemoryBuffer1D<int, Stride1D.Dense> tempBuffer;

        internal ScanProvider(Accelerator accelerator, LongIndex1D dataLength)
            : base(accelerator)
        {
            tempBuffer = accelerator.Allocate1D<int>(dataLength);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new buffered scan operation.
        /// </summary>
        /// <typeparam name="T">The underlying type of the scan operation.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
        /// <typeparam name="TScanOperation">The type of the scan operation.</typeparam>
        /// <param name="kind">The scan kind.</param>
        /// <returns>The created scan handler.</returns>
        public BufferedScan<T, TStrideIn, TStrideOut> CreateScan<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation>(
            ScanKind kind)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var scan = Accelerator.CreateScan<T, TStrideIn, TStrideOut, TScanOperation>(
                kind);
            return (stream, input, output) =>
                scan(stream, input, output, tempBuffer.View);
        }

        #endregion

        #region IDisposable

        /// <inheritdoc cref="AcceleratorObject.DisposeAcceleratorObject(bool)"/>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (disposing)
                tempBuffer.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// Contains extension methods for scan operations.
    /// </summary>
    public static partial class ScanExtensions
    {
        #region Scan Helpers

        /// <summary>
        /// Computes the required number of temp-storage elements of type
        /// <typeparamref name="T"/> for a scan operation and the given data length.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="dataLength">The number of data elements to scan.</param>
        /// <returns>The required number of temp-storage elements in 32 bit ints.</returns>
        public static LongIndex1D ComputeScanTempStorageSize<T>(
            this Accelerator accelerator,
            LongIndex1D dataLength)
            where T : unmanaged
        {
            return accelerator.AcceleratorType switch
            {
                AcceleratorType.CPU => 1,
                // Wasm hybrid scan: uses single-group for small data (RadixSort
                // histograms), multi-pass for large data. The multi-pass scan's
                // TempViewManager needs gridDim+1 elements, but RadixSort pre-
                // allocates based on this value. Keep it at the multi-pass formula
                // so there's enough temp for either path. The formula is small
                // (typically ~131 ints ≈ 524 bytes).
                AcceleratorType.Wasm => Interop.ComputeRelativeSizeOf<int, T>(
                    accelerator.MaxNumGroupsExtent.Item1 + 1)
                    + 2 * 63,
                // Single-pass scan: CreateSinglePassScan makes 2 Allocate() calls
                // (Allocate<T>, Allocate<int>). Each adds up to 63 ints of 256-byte padding.
                AcceleratorType.Cuda => ComputeNumIntElementsForSinglePassScan<T>()
                    + 2 * 63, // 2 allocations × 63 ints max padding
                // Multi-pass scan: add padding for TempViewManager 256-byte alignment.
                // CreateMultiPassScan makes 1 Allocate() call from TempViewManager,
                // plus the internal scan chain may add 1 more.
                _ => Interop.ComputeRelativeSizeOf<int, T>(
                    accelerator.MaxNumGroupsExtent.Item1 + 1)
                    + 2 * 63, // 2 allocations × 63 ints max padding
            };
        }

        #endregion

        #region Scan Primitives

        /// <summary>
        /// An abstract scan implementation that implements required low-level scan
        /// functionality.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        internal interface IScanImplementation<T, TScanOperation>
            : IGroupScan<T, TScanOperation>
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            /// <summary>
            /// Performs an all-reduction for all threads in the current group.
            /// </summary>
            /// <param name="value">The value from the current thread.</param>
            /// <returns>The reduced value.</returns>
            T AllReduce(T value);

            /// <summary>
            /// Prepares all threads in the current group for the next iteration
            /// of a multi-iteration scan.
            /// </summary>
            T NextIteration(T lastBoundary, T currentBoundary, T currentValue);
        }

        /// <summary>
        /// An inclusive-scan implementation.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        readonly struct InclusiveScanImplementation<T, TScanOperation>
            : IScanImplementation<T, TScanOperation>
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            /// <summary cref="IGroupScan{T, TScanOperation}.Scan(T)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Scan(T value) => InclusiveScan<T, TScanOperation>(value);

            /// <summary cref="IGroupScan{T, TScanOperation}.Scan(
            /// T, out ScanBoundaries{T})"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Scan(T value, out ScanBoundaries<T> boundaries) =>
                InclusiveScanWithBoundaries<T, TScanOperation>(value, out boundaries);

            /// <summary cref="IScanImplementation{T, TScanOperation}.AllReduce(T)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T AllReduce(T value) =>
                AllReduce<T, TScanOperation>(value);

            /// <summary cref="IScanImplementation{T, TScanOperation}.
            /// NextIteration(T, T, T)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T NextIteration(T leftBoundary, T rightBoundary, T currentValue) =>
                InclusiveScanNextIteration<T, TScanOperation>(
                    leftBoundary,
                    rightBoundary,
                    currentValue);
        }

        /// <summary>
        /// An exclusive-scan implementation.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        readonly struct ExclusiveScanImplementation<T, TScanOperation>
            : IScanImplementation<T, TScanOperation>
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            /// <summary cref="IGroupScan{T, TScanOperation}.Scan(T)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Scan(T value) => ExclusiveScan<T, TScanOperation>(value);

            /// <summary cref="IGroupScan{T, TScanOperation}.Scan(
            /// T, out ScanBoundaries{T})"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Scan(T value, out ScanBoundaries<T> boundaries) =>
                ExclusiveScanWithBoundaries<T, TScanOperation>(value, out boundaries);

            /// <summary cref="IScanImplementation{T, TScanOperation}.AllReduce(T)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T AllReduce(T value) =>
                AllReduce<T, TScanOperation>(value);

            /// <summary cref="IScanImplementation{T, TScanOperation}.
            /// NextIteration(T, T, T)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T NextIteration(T leftBoundary, T rightBoundary, T currentValue) =>
                ExclusiveScanNextIteration<T, TScanOperation>(
                    leftBoundary,
                    rightBoundary,
                    currentValue);
        }

        /// <summary>
        /// Computes the right tile boundary.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TScanOperation">The scan-operation type.</typeparam>
        /// <typeparam name="TGroupScanImplementation">
        /// The group-scan implementation type.
        /// </typeparam>
        /// <param name="tileInfo">The current tile info.</param>
        /// <param name="input">The input view.</param>
        /// <returns>The resolved right boundary for all threads in the group.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T ComputeTileRightBoundary<
            T,
            TStrideIn,
            TScanOperation,
            TGroupScanImplementation>(
            TileInfo tileInfo,
            ArrayView1D<T, TStrideIn> input)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
            where TGroupScanImplementation
                : struct, IScanImplementation<T, TScanOperation>
        {
            TScanOperation scanOperation = default;
            TGroupScanImplementation groupScan = default;

            T rightBoundary = tileInfo.StartIndex < tileInfo.MaxLength ?
                input[tileInfo.StartIndex] :
                scanOperation.Identity;

            // Perform a scan of all items in this group
            rightBoundary = groupScan.AllReduce(rightBoundary);

            // Perform a linear scan over all elements in the current tile
            for (
                int i = tileInfo.StartIndex + Group.DimX;
                i < tileInfo.EndIndex;
                i += Group.DimX)
            {
                var inputValue = i < tileInfo.MaxLength
                    ? input[i]
                    : scanOperation.Identity;

                var reduced = groupScan.AllReduce(inputValue);
                rightBoundary = scanOperation.Apply(rightBoundary, reduced);
            }
            return rightBoundary;
        }

        /// <summary>
        /// Computes a single scan within a single tile.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
        /// <typeparam name="TScanOperation">The scan-operation type.</typeparam>
        /// <typeparam name="TGroupScanImplementation">
        /// The group-scan implementation type.
        /// </typeparam>
        /// <param name="tileInfo">The current tile info.</param>
        /// <param name="input">The input view.</param>
        /// <param name="output">The output view.</param>
        /// <param name="leftBoundary">
        /// The left boundary (e.g. of the previous tile).
        /// </param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeTileScan<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation,
            TGroupScanImplementation>(
            TileInfo tileInfo,
            ArrayView1D<T, TStrideIn> input,
            ArrayView1D<T, TStrideOut> output,
            T leftBoundary)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
            where TGroupScanImplementation :
            struct, IScanImplementation<T, TScanOperation>
        {
            TScanOperation scanOperation = default;
            TGroupScanImplementation groupScan = default;

            // Fetch initial current value
            T inputValue = tileInfo.StartIndex < tileInfo.MaxLength ?
                input[tileInfo.StartIndex] :
                scanOperation.Identity;

            // Perform a scan of all items in this group
            var current = groupScan.Scan(inputValue, out var localBoundaries);

            if (tileInfo.StartIndex < tileInfo.MaxLength)
                output[tileInfo.StartIndex] = scanOperation.Apply(leftBoundary, current);

            // Adjust all scan results according to the previously computed result
            for (
                int i = tileInfo.StartIndex + Group.DimX;
                i < tileInfo.EndIndex;
                i += Group.DimX)
            {
                leftBoundary = groupScan.NextIteration(
                    leftBoundary,
                    localBoundaries.RightBoundary,
                    inputValue);

                inputValue = i < tileInfo.MaxLength ? input[i] : scanOperation.Identity;

                current = groupScan.Scan(inputValue, out localBoundaries);
                if (i < tileInfo.MaxLength)
                    output[i] = scanOperation.Apply(leftBoundary, current);
            }
        }

        #endregion

        #region Single-Group Scan

        /// <summary>
        /// Performs a scan operation within a single group only.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        /// <typeparam name="TGroupScanImplementation">
        /// The actual group-scan implementation that provides the required group-level
        /// functionality.
        /// </typeparam>
        /// <param name="input">The input elements to scan.</param>
        /// <param name="output">The output view to store the scanned values.</param>
        internal static void SingleGroupScanKernel<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation,
            TGroupScanImplementation>(
            ArrayView1D<T, TStrideIn> input,
            ArrayView1D<T, TStrideOut> output)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
            where TGroupScanImplementation :
                struct,
                IScanImplementation<T, TScanOperation>
        {
            var tileInfo = new TileInfo(
                input.IntLength,
                XMath.DivRoundUp(input.IntLength, Group.DimX));

            TScanOperation scanOperation = default;

            ComputeTileScan<
                T,
                TStrideIn,
                TStrideOut,
                TScanOperation,
                TGroupScanImplementation>(
                tileInfo,
                input,
                output,
                scanOperation.Identity);
        }

        /// <summary>
        /// Creates a new single group scan.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="kind">The scan kind.</param>
        /// <returns>The created scan operation.</returns>
        private static Scan<T, TStrideIn, TStrideOut> CreateSingleGroupScan<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation>(
            Accelerator accelerator,
            ScanKind kind)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            Action<AcceleratorStream, KernelConfig, ArrayView1D<T, TStrideIn>,
                ArrayView1D<T, TStrideOut>> kernel;
            if (kind == ScanKind.Inclusive)
            {
                kernel = accelerator.LoadKernel<ArrayView1D<T, TStrideIn>,
                    ArrayView1D<T, TStrideOut>>(
                    SingleGroupScanKernel<
                        T,
                        TStrideIn,
                        TStrideOut,
                        TScanOperation,
                        InclusiveScanImplementation<T, TScanOperation>>);
            }
            else
            {
                kernel = accelerator.LoadKernel<ArrayView1D<T, TStrideIn>,
                    ArrayView1D<T, TStrideOut>>(
                    SingleGroupScanKernel<
                        T,
                        TStrideIn,
                        TStrideOut,
                        TScanOperation,
                        ExclusiveScanImplementation<T, TScanOperation>>);
            }
            return (stream, input, output, temp) =>
            {
                if (!input.IsValid)
                    throw new ArgumentNullException(nameof(input));
                if (!output.IsValid)
                    throw new ArgumentNullException(nameof(output));
                if (output.Length < input.Length)
                    throw new ArgumentOutOfRangeException(nameof(output));
                if (input.Length > int.MaxValue)
                {
                    throw new NotSupportedException(
                        ErrorMessages.NotSupportedArrayView64);
                }
                kernel(stream, (1, accelerator.MaxNumThreadsPerGroup), input, output);
            };
        }

        #endregion

        #region Single-Pass Scan

        /// <summary>
        /// Performs a scan operation with a single pass.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        /// <typeparam name="TGroupScanImplementation">
        /// The actual group-scan implementation that provides the required group-level
        /// functionality.
        /// </typeparam>
        /// <param name="input">The input elements to scan.</param>
        /// <param name="output">The output view to store the scanned values.</param>
        /// <param name="sequentialGroupExecutor">
        /// The sequential group executor to use.
        /// </param>
        /// <param name="boundaryValue">
        /// The boundary value target in global memory to share intermediate results.
        /// </param>
        /// <param name="numIterationsPerGroup">
        /// The number of iterations per group.
        /// </param>
        internal static void SinglePassScanKernel<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation,
            TGroupScanImplementation>(
            ArrayView1D<T, TStrideIn> input,
            ArrayView1D<T, TStrideOut> output,
            SequentialGroupExecutor sequentialGroupExecutor,
            VariableView<T> boundaryValue,
            Index1D numIterationsPerGroup)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
            where TGroupScanImplementation :
                struct,
                IScanImplementation<T, TScanOperation>
        {
            var tileInfo = new TileInfo(input.IntLength, numIterationsPerGroup);

            TScanOperation scanOperation = default;

            T leftBoundary = scanOperation.Identity;

            // Determine our right boundary and resolve our left boundary
            T rightBoundary = ComputeTileRightBoundary<
                T,
                TStrideIn,
                TScanOperation,
                TGroupScanImplementation>(
                tileInfo,
                input);

            // Sync groups and wait for the current one to become active
            sequentialGroupExecutor.Wait();

            // Read the right boundary of the previous group (if possible)
            // This is our new left boundary (if required)
            if (Grid.IdxX > 0)
                leftBoundary = boundaryValue.Value;

            // Wait for all threads in the group to read the same boundary value
            Group.Barrier();

            // If we are the first thread in the group, update the boundary value for
            // the next group
            if (Group.IsFirstThread)
                boundaryValue.Value = scanOperation.Apply(leftBoundary, rightBoundary);

            // Wait for all changes
            MemoryFence.DeviceLevel();
            Group.Barrier();

            sequentialGroupExecutor.Release();

            // Perform the final tile scan
            ComputeTileScan<
                T,
                TStrideIn,
                TStrideOut,
                TScanOperation,
                TGroupScanImplementation>(
                tileInfo,
                input,
                output,
                leftBoundary);
        }

        /// <summary>
        /// Computes the required number of elements of size <see cref="int"/>.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <returns>
        /// The required number of <see cref="int"/> elements in temporary memory.
        /// </returns>
        private static long ComputeNumIntElementsForSinglePassScan<T>()
            where T : unmanaged =>
            Interop.ComputeRelativeSizeOf<int, T>(1) + 1;

        /// <summary>
        /// Creates a new single pass scan.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="kind">The scan kind.</param>
        /// <returns>The created scan operation.</returns>
        private static Scan<T, TStrideIn, TStrideOut> CreateSinglePassScan<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation>(
            Accelerator accelerator,
            ScanKind kind)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var initializer = accelerator.CreateInitializer<int, Stride1D.Dense>();

            Action<AcceleratorStream, KernelConfig, ArrayView1D<T, TStrideIn>,
                ArrayView1D<T, TStrideOut>, SequentialGroupExecutor, VariableView<T>,
                Index1D> kernel;
            if (kind == ScanKind.Inclusive)
            {
                kernel = accelerator.LoadKernel<
                    ArrayView1D<T, TStrideIn>,
                    ArrayView1D<T, TStrideOut>,
                    SequentialGroupExecutor,
                    VariableView<T>,
                    Index1D>(
                    SinglePassScanKernel<
                        T,
                        TStrideIn,
                        TStrideOut,
                        TScanOperation,
                        InclusiveScanImplementation<T,
                        TScanOperation>>);
            }
            else
            {
                kernel = accelerator.LoadKernel<
                    ArrayView1D<T, TStrideIn>,
                    ArrayView1D<T, TStrideOut>,
                    SequentialGroupExecutor,
                    VariableView<T>,
                    Index1D>(
                    SinglePassScanKernel<
                        T,
                        TStrideIn,
                        TStrideOut,
                        TScanOperation,
                        ExclusiveScanImplementation<T,
                        TScanOperation>>);
            }

            long numIntTElementsLong = ComputeNumIntElementsForSinglePassScan<T>();
            IndexTypeExtensions.AssertIntIndexRange(numIntTElementsLong);

            return (stream, input, output, temp) =>
            {
                if (!input.IsValid)
                    throw new ArgumentNullException(nameof(input));
                if (!output.IsValid)
                    throw new ArgumentNullException(nameof(output));
                if (output.Length < input.Length)
                    throw new ArgumentOutOfRangeException(nameof(output));
                if (input.Length > int.MaxValue)
                {
                    throw new NotSupportedException(
                        ErrorMessages.NotSupportedArrayView64);
                }

                var viewManager = new TempViewManager(temp, nameof(temp));
                var tempView = viewManager.Allocate<T>();
                var executorView = viewManager.Allocate<int>();

                initializer(stream, temp.SubView(0, viewManager.NumInts), default);

                var extent = accelerator.ComputeGridStrideLoopExtent(
                    input.IntLength,
                    out int numIterationsPerGroup);

                kernel(
                    stream,
                    extent,
                    input,
                    output,
                    new SequentialGroupExecutor(executorView),
                    tempView,
                    numIterationsPerGroup);
            };
        }

        #endregion

        #region Multi-Pass Scan

        /// <summary>
        /// Performs the first pass in the scope of a multi-pass scan.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        /// <typeparam name="TGroupScanImplementation">
        /// The actual group-scan implementation that provides the required group-level
        /// functionality.
        /// </typeparam>
        /// <param name="input">The input elements to scan.</param>
        /// <param name="rightBoundaries">The right boundaries to store.</param>
        /// <param name="numIterationsPerGroup">
        /// The number of iterations per group.
        /// </param>
        internal static void MultiPassScanKernel1<
            T,
            TStrideIn,
            TScanOperation,
            TGroupScanImplementation>(
            ArrayView1D<T, TStrideIn> input,
            ArrayView<T> rightBoundaries,
            Index1D numIterationsPerGroup)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
            where TGroupScanImplementation :
                struct,
                IScanImplementation<T, TScanOperation>
        {
            var tileInfo = new TileInfo(input.IntLength, numIterationsPerGroup);

            T rightBoundary = ComputeTileRightBoundary<
                T,
                TStrideIn,
                TScanOperation,
                TGroupScanImplementation>(
                tileInfo,
                input);

            if (Group.IsFirstThread)
                rightBoundaries[Grid.IdxX] = rightBoundary;
        }

        /// <summary>
        /// Performs the second pass in the scope of a multi-pass scan.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        /// <typeparam name="TGroupScanImplementation">
        /// The actual group-scan implementation that provides the required group-level
        /// functionality.
        /// </typeparam>
        /// <param name="input">The input elements to scan.</param>
        /// <param name="rightBoundaries">The right boundaries to use.</param>
        /// <param name="output">The scanned values.</param>
        /// <param name="numIterationsPerGroup">
        /// The number of iterations per group.
        /// </param>
        internal static void MultiPassScanKernel2<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation,
            TGroupScanImplementation>(
            ArrayView1D<T, TStrideIn> input,
            ArrayView<T> rightBoundaries,
            ArrayView1D<T, TStrideOut> output,
            Index1D numIterationsPerGroup)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
            where TGroupScanImplementation :
                struct,
                IScanImplementation<T, TScanOperation>
        {
            var tileInfo = new TileInfo(input.IntLength, numIterationsPerGroup);

            TScanOperation scanOperation = default;
            TGroupScanImplementation groupScan = default;

            var localRightBoundary = Group.IdxX < rightBoundaries.Length
                ? rightBoundaries[Group.IdxX]
                : scanOperation.Identity;
            var scannedLeftBoundaries = groupScan.Scan(localRightBoundary);
            T leftBoundary = Group.Broadcast(scannedLeftBoundaries, Grid.IdxX);

            ComputeTileScan<
                T,
                TStrideIn,
                TStrideOut,
                TScanOperation,
                TGroupScanImplementation>(
                tileInfo,
                input,
                output,
                leftBoundary);
        }

        /// <summary>
        /// Creates a new multi pass scan.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
        /// <typeparam name="TScanOperation">The scan operation.</typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="kind">The scan kind.</param>
        /// <returns>The created scan operation.</returns>
        private static Scan<T, TStrideIn, TStrideOut> CreateMultiPassScan<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation>(
            Accelerator accelerator,
            ScanKind kind)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var initializer = accelerator.CreateInitializer<T, Stride1D.Dense>();

            // Pass the workgroup size as a kernel specialization so that backends
            // with compile-time workgroup sizes (WebGPU/WGSL) can bake the correct
            // @workgroup_size into the shader.  ComputeGridStrideLoopExtent always
            // returns MaxNumThreadsPerGroup as groupDim, so this is safe.
            var scanSpec = new KernelSpecialization(
                accelerator.MaxNumThreadsPerGroup, null);

            Action<AcceleratorStream, KernelConfig, ArrayView1D<T, TStrideIn>,
                ArrayView<T>, Index1D> pass1Kernel;
            Action<AcceleratorStream, KernelConfig, ArrayView1D<T, TStrideIn>,
                ArrayView<T>, ArrayView1D<T, TStrideOut>, Index1D> pass2Kernel;
            if (kind == ScanKind.Inclusive)
            {
                pass1Kernel = accelerator.LoadKernel<
                    ArrayView1D<T, TStrideIn>, ArrayView<T>, Index1D>(
                    MultiPassScanKernel1<T, TStrideIn, TScanOperation,
                        InclusiveScanImplementation<T, TScanOperation>>,
                    scanSpec);
                pass2Kernel = accelerator.LoadKernel<ArrayView1D<T, TStrideIn>,
                    ArrayView<T>, ArrayView1D<T, TStrideOut>, Index1D>(
                    MultiPassScanKernel2<T, TStrideIn, TStrideOut, TScanOperation,
                        InclusiveScanImplementation<T, TScanOperation>>,
                    scanSpec);
            }
            else
            {
                pass1Kernel = accelerator.LoadKernel<
                    ArrayView1D<T, TStrideIn>, ArrayView<T>, Index1D>(
                    MultiPassScanKernel1<T, TStrideIn, TScanOperation,
                        ExclusiveScanImplementation<T, TScanOperation>>,
                    scanSpec);
                pass2Kernel = accelerator.LoadKernel<ArrayView1D<T, TStrideIn>,
                    ArrayView<T>, ArrayView1D<T, TStrideOut>, Index1D>(
                    MultiPassScanKernel2<T, TStrideIn, TStrideOut, TScanOperation,
                        ExclusiveScanImplementation<T, TScanOperation>>,
                    scanSpec);
            }

            return (stream, input, output, temp) =>
            {
                if (!input.IsValid)
                    throw new ArgumentNullException(nameof(input));
                if (!output.IsValid)
                    throw new ArgumentNullException(nameof(output));
                if (output.Length < input.Length)
                    throw new ArgumentOutOfRangeException(nameof(output));
                if (input.Length > int.MaxValue)
                {
                    throw new NotSupportedException(
                        ErrorMessages.NotSupportedArrayView64);
                }

                var (gridDim, groupDim) = accelerator.ComputeGridStrideLoopExtent(
                    input.IntLength,
                    out int numIterationsPerGroup);

                var viewManager = new TempViewManager(temp, nameof(temp));
                var tempView = viewManager.Allocate<T>(gridDim + 1);

                TScanOperation scanOperation = default;
                initializer(stream, tempView, scanOperation.Identity);
                if (tempView.Length > 1)
                {
                    var offsetView =
                        tempView.SubView(
                            kind == ScanKind.Inclusive ? 1 : 0,
                            tempView.Length - 1);
                    pass1Kernel(
                        stream,
                        (gridDim, groupDim),
                        input,
                        offsetView,
                        numIterationsPerGroup);

                    // Fence between scan passes: pass 2 reads boundary values written by
                    // pass 1. All backends need this barrier — even WebGPU, where multiple
                    // dispatchWorkgroups() within the same compute pass share no implicit
                    // storage-buffer barrier. The copy+sync forces a pipeline flush.
                    {
                        using var resultBuffer = accelerator.Allocate1D<T>(tempView.Length);
                        resultBuffer.View.CopyFrom(stream, tempView);
                        stream.Flush();
                    }
                }

                pass2Kernel(
                    stream,
                    (gridDim, groupDim),
                    input,
                    tempView,
                    output,
                    numIterationsPerGroup);

                // Flush the command encoder so the scan output lives in its own
                // command buffer.  Without this, the scan's pass2 dispatch and
                // the caller's next dispatch (e.g. RadixSortKernel2) share one
                // command buffer.  WebGPU's implicit storage-buffer barrier
                // between compute passes *should* suffice, but browser WebGPU
                // implementations can have subtle barrier bugs that cause rare,
                // non-deterministic data races.  An explicit Flush() forces a
                // command-buffer boundary on WebGPU (submit without waiting),
                // giving the queue explicit ordering guarantees.
                stream.Flush();
            };
        }

        #endregion

        #region WebGPU Multi-Pass Scan

        // WebGPU-specific multi-pass scan implementation.
        // Uses simple flat kernels that avoid the TileInfo/ComputeTileScan
        // infrastructure. The standard MultiPassScanKernel1/2 generate WGSL
        // code with complex control flow (grid-stride loops, TileInfo struct,
        // generic scan implementations) that triggers a code generation bug
        // in the WGSL backend — producing incorrect results for non-uniform
        // data while working for uniform data. These simplified kernels
        // produce identical results with straightforward control flow.

        /// <summary>
        /// WebGPU pass 1: each workgroup computes AllReduce of its tile.
        /// Handles multiple iterations per group when the input exceeds
        /// gridDim * groupDim elements.
        /// </summary>
        internal static void WebGPUScanPass1<
            T,
            TStrideIn,
            TScanOperation>(
            ArrayView1D<T, TStrideIn> input,
            ArrayView<T> rightBoundaries,
            Index1D numIterationsPerGroup)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            TScanOperation op = default;
            int tileSize = Group.DimX * numIterationsPerGroup;
            int baseIdx = Grid.IdxX * tileSize + Group.IdxX;

            // First chunk
            T value = baseIdx < input.IntLength
                ? input[baseIdx]
                : op.Identity;
            T boundary = AllReduce<T, TScanOperation>(value);

            // Additional chunks (grid-stride within tile)
            for (int i = baseIdx + Group.DimX; i < (Grid.IdxX + 1) * tileSize; i += Group.DimX)
            {
                T v = i < input.IntLength ? input[i] : op.Identity;
                T r = AllReduce<T, TScanOperation>(v);
                boundary = op.Apply(boundary, r);
            }

            if (Group.IsFirstThread)
                rightBoundaries[Grid.IdxX] = boundary;
        }

        /// <summary>
        /// WebGPU pass 2: scan boundaries, broadcast left boundary per
        /// workgroup, then scan each tile and add the left boundary offset.
        /// </summary>
        internal static void WebGPUInclusiveScanPass2<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation>(
            ArrayView1D<T, TStrideIn> input,
            ArrayView<T> rightBoundaries,
            ArrayView1D<T, TStrideOut> output,
            Index1D numIterationsPerGroup)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            TScanOperation op = default;
            int tileSize = Group.DimX * numIterationsPerGroup;

            // Step 1: Scan the right boundaries to get left boundary per group
            T localRB = Group.IdxX < rightBoundaries.Length
                ? rightBoundaries[Group.IdxX]
                : op.Identity;
            T scanned = InclusiveScan<T, TScanOperation>(localRB);
            T leftBoundary = Group.Broadcast(scanned, Grid.IdxX);

            // Step 2: Scan each chunk of the tile
            int baseIdx = Grid.IdxX * tileSize + Group.IdxX;

            // First chunk
            T inputVal = baseIdx < input.IntLength
                ? input[baseIdx]
                : op.Identity;
            T current = InclusiveScanWithBoundaries<T, TScanOperation>(
                inputVal, out var bounds);

            if (baseIdx < input.IntLength)
                output[baseIdx] = op.Apply(leftBoundary, current);

            // Additional chunks
            for (int i = baseIdx + Group.DimX; i < (Grid.IdxX + 1) * tileSize; i += Group.DimX)
            {
                leftBoundary = op.Apply(leftBoundary, bounds.RightBoundary);

                inputVal = i < input.IntLength ? input[i] : op.Identity;
                current = InclusiveScanWithBoundaries<T, TScanOperation>(
                    inputVal, out bounds);

                if (i < input.IntLength)
                    output[i] = op.Apply(leftBoundary, current);
            }
        }

        /// <summary>
        /// WebGPU pass 2 for exclusive scan.
        /// </summary>
        internal static void WebGPUExclusiveScanPass2<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation>(
            ArrayView1D<T, TStrideIn> input,
            ArrayView<T> rightBoundaries,
            ArrayView1D<T, TStrideOut> output,
            Index1D numIterationsPerGroup)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            TScanOperation op = default;
            int tileSize = Group.DimX * numIterationsPerGroup;

            // Step 1: Scan boundaries
            T localRB = Group.IdxX < rightBoundaries.Length
                ? rightBoundaries[Group.IdxX]
                : op.Identity;
            T scanned = InclusiveScan<T, TScanOperation>(localRB);
            T leftBoundary = Group.Broadcast(scanned, Grid.IdxX);

            // Step 2: Exclusive scan of each chunk
            int baseIdx = Grid.IdxX * tileSize + Group.IdxX;

            T inputVal = baseIdx < input.IntLength
                ? input[baseIdx]
                : op.Identity;
            T current = ExclusiveScanWithBoundaries<T, TScanOperation>(
                inputVal, out var bounds);

            if (baseIdx < input.IntLength)
                output[baseIdx] = op.Apply(leftBoundary, current);

            for (int i = baseIdx + Group.DimX; i < (Grid.IdxX + 1) * tileSize; i += Group.DimX)
            {
                T nextBoundary = op.Apply(leftBoundary, bounds.RightBoundary);
                leftBoundary = op.Apply(
                    nextBoundary,
                    Group.Broadcast(inputVal, Group.DimX - 1));

                inputVal = i < input.IntLength ? input[i] : op.Identity;
                current = ExclusiveScanWithBoundaries<T, TScanOperation>(
                    inputVal, out bounds);

                if (i < input.IntLength)
                    output[i] = op.Apply(leftBoundary, current);
            }
        }

        /// <summary>
        /// Creates a WebGPU-specific multi-pass scan using simplified kernels
        /// that avoid the TileInfo/ComputeTileScan infrastructure.
        /// </summary>
        private static Scan<T, TStrideIn, TStrideOut> CreateWebGPUMultiPassScan<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation>(
            Accelerator accelerator,
            ScanKind kind)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var initializer = accelerator.CreateInitializer<T, Stride1D.Dense>();
            var scanSpec = new KernelSpecialization(
                accelerator.MaxNumThreadsPerGroup, null);

            var pass1Kernel = accelerator.LoadKernel<
                ArrayView1D<T, TStrideIn>, ArrayView<T>, Index1D>(
                WebGPUScanPass1<T, TStrideIn, TScanOperation>,
                scanSpec);

            Action<AcceleratorStream, KernelConfig, ArrayView1D<T, TStrideIn>,
                ArrayView<T>, ArrayView1D<T, TStrideOut>, Index1D> pass2Kernel;
            if (kind == ScanKind.Inclusive)
            {
                pass2Kernel = accelerator.LoadKernel<ArrayView1D<T, TStrideIn>,
                    ArrayView<T>, ArrayView1D<T, TStrideOut>, Index1D>(
                    WebGPUInclusiveScanPass2<T, TStrideIn, TStrideOut, TScanOperation>,
                    scanSpec);
            }
            else
            {
                pass2Kernel = accelerator.LoadKernel<ArrayView1D<T, TStrideIn>,
                    ArrayView<T>, ArrayView1D<T, TStrideOut>, Index1D>(
                    WebGPUExclusiveScanPass2<T, TStrideIn, TStrideOut, TScanOperation>,
                    scanSpec);
            }

            return (stream, input, output, temp) =>
            {
                if (!input.IsValid)
                    throw new ArgumentNullException(nameof(input));
                if (!output.IsValid)
                    throw new ArgumentNullException(nameof(output));
                if (output.Length < input.Length)
                    throw new ArgumentOutOfRangeException(nameof(output));
                if (input.Length > int.MaxValue)
                {
                    throw new NotSupportedException(
                        ErrorMessages.NotSupportedArrayView64);
                }

                var (gridDim, groupDim) = accelerator.ComputeGridStrideLoopExtent(
                    input.IntLength,
                    out int numIterationsPerGroup);

                var viewManager = new TempViewManager(temp, nameof(temp));
                var tempView = viewManager.Allocate<T>(gridDim + 1);

                TScanOperation scanOperation = default;
                initializer(stream, tempView, scanOperation.Identity);
                if (tempView.Length > 1)
                {
                    // Always prepend identity at tempView[0] so that
                    // InclusiveScan in pass2 produces the correct left
                    // boundary for both inclusive and exclusive scan.
                    var offsetView =
                        tempView.SubView(1, tempView.Length - 1);
                    pass1Kernel(
                        stream,
                        (gridDim, groupDim),
                        input,
                        offsetView,
                        numIterationsPerGroup);

                    // Fence between scan passes — ensures pass1 output is visible
                    // to pass2. On Wasm, dispatches are serialized (async, one at a
                    // time) so the fence is unnecessary and CopyFrom would fail
                    // (NativePtr is only valid during dispatch).
                    if (accelerator.AcceleratorType != AcceleratorType.Wasm)
                    {
                        using var resultBuffer = accelerator.Allocate1D<T>(tempView.Length);
                        resultBuffer.View.CopyFrom(stream, tempView);
                        stream.Flush();
                    }
                }

                pass2Kernel(
                    stream,
                    (gridDim, groupDim),
                    input,
                    tempView,
                    output,
                    numIterationsPerGroup);

                // Flush the command encoder so the scan output lives in its own
                // command buffer.  Without this, the scan's pass2 dispatch and
                // the caller's next dispatch (e.g. RadixSortKernel2) share one
                // command buffer.  WebGPU's implicit storage-buffer barrier
                // between compute passes *should* suffice, but browser WebGPU
                // implementations can have subtle barrier bugs that cause rare,
                // non-deterministic data races.  An explicit Flush() forces a
                // command-buffer boundary on WebGPU (submit without waiting),
                // giving the queue explicit ordering guarantees.
                stream.Flush();
            };
        }

        /// <summary>
        /// Creates a hybrid Wasm scan that uses single-group for small data
        /// (≤ groupSize elements, 1 dispatch) and multi-pass for large data
        /// (multiple groups, 3 dispatches). Multi-pass kernels are compiled
        /// lazily on first use to prevent OOM from upfront compilation of
        /// unused kernel modules (e.g., RadixSort only scans small histograms).
        /// </summary>
        private static Scan<T, TStrideIn, TStrideOut> CreateWasmHybridScan<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation>(
            Accelerator accelerator,
            ScanKind kind)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var singleGroupScan = CreateSingleGroupScan<
                T, TStrideIn, TStrideOut, TScanOperation>(accelerator, kind);
            Scan<T, TStrideIn, TStrideOut>? multiPassScan = null;

            int maxGroupSize = accelerator.MaxNumThreadsPerGroup;

            return (stream, input, output, temp) =>
            {
                if (input.Length <= maxGroupSize)
                {
                    singleGroupScan(stream, input, output, temp);
                }
                else
                {
                    multiPassScan ??= CreateWebGPUMultiPassScan<
                        T, TStrideIn, TStrideOut, TScanOperation>(
                        accelerator, kind);
                    multiPassScan(stream, input, output, temp);
                }
            };
        }

        #endregion

        #region Scan

        /// <summary>
        /// Creates a new scan operation.
        /// </summary>
        /// <typeparam name="T">The underlying type of the scan operation.</typeparam>
        /// <typeparam name="TStrideIn">The stride of the input view.</typeparam>
        /// <typeparam name="TStrideOut">The stride of the output view.</typeparam>
        /// <typeparam name="TScanOperation">The type of the scan operation.</typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="kind">The scan kind.</param>
        /// <returns>The created scan handler.</returns>
        public static Scan<T, TStrideIn, TStrideOut> CreateScan<
            T,
            TStrideIn,
            TStrideOut,
            TScanOperation>(
            this Accelerator accelerator,
            ScanKind kind)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            // Scan is fire-and-forget multi-dispatch (its inter-pass barrier is a SUBMIT — CopyFrom +
            // stream.Flush() — not a completion-wait, and it never reads back to host mid-operation),
            // so the synchronous builder is valid on EVERY backend including browser. Each backend
            // routes to its proven sync builder below. (Sync/async contract:
            // Plans/sync-async-contract-2026-06-13.)
            switch (accelerator.AcceleratorType)
            {
                case AcceleratorType.CPU:
                    return CreateSingleGroupScan<T, TStrideIn, TStrideOut, TScanOperation>(
                        accelerator, kind);
                case AcceleratorType.Cuda:
                    return CreateSinglePassScan<T, TStrideIn, TStrideOut, TScanOperation>(
                        accelerator, kind);
                case AcceleratorType.OpenCL:
                    return CreateMultiPassScan<T, TStrideIn, TStrideOut, TScanOperation>(
                        accelerator, kind);
                // Scan is FIRE-AND-FORGET multi-dispatch on browser (no completion-wait, no
                // device->host readback mid-operation), so the sync builder is valid on every
                // browser backend per the sync/async contract (Plans/sync-async-contract-2026-06-13).
                case AcceleratorType.WebGPU:
                    // Generic multi-pass scan: inter-pass barrier is CopyFrom + stream.Flush()
                    // (a SUBMIT — valid synchronously on WebGPU), not the throwing Synchronize.
                    return CreateMultiPassScan<T, TStrideIn, TStrideOut, TScanOperation>(
                        accelerator, kind);
                case AcceleratorType.WebGL:
                    // Hillis-Steele multi-dispatch; the draw-call boundary is the barrier.
                    return CreateWebGLHillisSteeleScan<T, TStrideIn, TStrideOut, TScanOperation>(
                        accelerator, kind);
                case AcceleratorType.Wasm:
                    // Single-group grid-stride scan: Wasm's serialized worker dispatch orders the
                    // work; avoids the generic multi-pass CopyFrom that fails mid-graph on Wasm.
                    return CreateSingleGroupScan<T, TStrideIn, TStrideOut, TScanOperation>(
                        accelerator, kind);
                default:
                    return CreateMultiPassScan<T, TStrideIn, TStrideOut, TScanOperation>(
                        accelerator, kind);
            }
        }

        #region WebGL Hillis-Steele Multi-Pass Scan (shared-mem/barrier/atomic-free emulation)

        // input (possibly strided) -> dense working buffer.
        private static void WebGLScanCopyIn<T, TStrideIn>(
            Index1D index,
            ArrayView1D<T, TStrideIn> input,
            ArrayView1D<T, Stride1D.Dense> output)
            where T : unmanaged
            where TStrideIn : struct, IStride1D =>
            output[index.X] = input[index.X];

        // One Hillis-Steele step: out[i] = (i >= d) ? op(in[i-d], in[i]) : in[i].
        // Reads only the previous pass's buffer (src); the draw-call boundary is the barrier.
        private static void WebGLScanStep<T, TScanOperation>(
            Index1D index,
            ArrayView1D<T, Stride1D.Dense> src,
            ArrayView1D<T, Stride1D.Dense> dst,
            int d)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            int i = index.X;
            if (i >= d)
            {
                TScanOperation op = default;
                dst[i] = op.Apply(src[i - d], src[i]); // (earlier, later) — order-preserving
            }
            else
                dst[i] = src[i];
        }

        // Inclusive result -> user output (dense -> possibly strided).
        private static void WebGLScanCopyOut<T, TStrideOut>(
            Index1D index,
            ArrayView1D<T, Stride1D.Dense> inclusive,
            ArrayView1D<T, TStrideOut> output)
            where T : unmanaged
            where TStrideOut : struct, IStride1D =>
            output[index.X] = inclusive[index.X];

        // Inclusive -> exclusive: shift right, lane 0 gets identity.
        private static void WebGLScanExclusiveShift<T, TScanOperation, TStrideOut>(
            Index1D index,
            ArrayView1D<T, Stride1D.Dense> inclusive,
            ArrayView1D<T, TStrideOut> output)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
            where TStrideOut : struct, IStride1D
        {
            int i = index.X;
            TScanOperation op = default;
            output[i] = i == 0 ? op.Identity : inclusive[i - 1];
        }

        private static Scan<T, TStrideIn, TStrideOut> CreateWebGLHillisSteeleScan<
            T, TStrideIn, TStrideOut, TScanOperation>(
            Accelerator accelerator,
            ScanKind kind)
            where T : unmanaged
            where TStrideIn : struct, IStride1D
            where TStrideOut : struct, IStride1D
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var copyIn = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<T, TStrideIn>, ArrayView1D<T, Stride1D.Dense>>(
                WebGLScanCopyIn<T, TStrideIn>);
            var step = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<T, Stride1D.Dense>, ArrayView1D<T, Stride1D.Dense>, int>(
                WebGLScanStep<T, TScanOperation>);
            var copyOut = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<T, Stride1D.Dense>, ArrayView1D<T, TStrideOut>>(
                WebGLScanCopyOut<T, TStrideOut>);
            var exclusiveShift = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<T, Stride1D.Dense>, ArrayView1D<T, TStrideOut>>(
                WebGLScanExclusiveShift<T, TScanOperation, TStrideOut>);

            return (stream, input, output, temp) =>
            {
                if (!input.IsValid)
                    throw new ArgumentNullException(nameof(input));
                int n = (int)input.Length;
                if (n < 1)
                    return;

                // Two dense ping-pong buffers replace shared memory.
                using var bufA = accelerator.Allocate1D<T>(n);
                using var bufB = accelerator.Allocate1D<T>(n);

                copyIn(stream, n, input, bufA.View);

                var src = bufA;
                var dst = bufB;
                for (int d = 1; d < n; d <<= 1)
                {
                    step(stream, n, src.View, dst.View, d);
                    var tmp = src; src = dst; dst = tmp;
                }

                // src now holds the inclusive scan.
                if (kind == ScanKind.Inclusive)
                    copyOut(stream, n, src.View, output);
                else
                    exclusiveShift(stream, n, src.View, output);
            };
        }

        #endregion

        /// <summary>
        /// Creates a new specialized scan provider that has its own cache.
        /// Note that the resulting provider has to be disposed manually.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="dataLength">The expected maximum data length to scan.</param>
        /// <returns>The created provider.</returns>
        public static ScanProvider CreateScanProvider(
            this Accelerator accelerator,
            LongIndex1D dataLength) =>
            dataLength < 1
            ? throw new ArgumentOutOfRangeException(nameof(dataLength))
            : new ScanProvider(accelerator, dataLength);

        /// <summary>
        /// Creates a new specialized scan provider that has its own cache.
        /// Note that the resulting provider has to be disposed manually.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="dataLength">
        /// The expected maximum data length to scan of a particular element type.
        /// </param>
        /// <returns>The created provider.</returns>
        public static ScanProvider CreateScanProvider<T>(
            this Accelerator accelerator,
            LongIndex1D dataLength)
            where T : unmanaged
        {
            var tempSize = accelerator.ComputeScanTempStorageSize<T>(dataLength);
            return CreateScanProvider(accelerator, tempSize);
        }

        #endregion
    }
}
