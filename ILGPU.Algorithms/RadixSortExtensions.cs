// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RadixSortExtensions.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Algorithms.Resources;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Algorithms.Sequencers;
using ILGPU.Runtime;
using ILGPU.Util;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ILGPU.Algorithms
{
    #region Delegates

    /// <summary>
    /// Represents a radix sort operation using a shuffle and operation logic.
    /// </summary>
    /// <typeparam name="T">The underlying type of the sort operation.</typeparam>
    /// <typeparam name="TStride">The stride of all values.</typeparam>
    /// <param name="stream">The accelerator stream.</param>
    /// <param name="view">The elements to sort.</param>
    /// <param name="temp">The temp view to store temporary results.</param>
    /// <remarks>The view buffer will be changed during the sorting operation.</remarks>
    public delegate void RadixSort<T, TStride>(
        AcceleratorStream stream,
        ArrayView1D<T, TStride> view,
        ArrayView<int> temp)
        where T : unmanaged
        where TStride : struct, IStride1D;

    /// <summary>
    /// Asynchronous counterpart of <see cref="RadixSort{T, TStride}"/> — the portable,
    /// all-backend radix sort. The synchronous <see cref="RadixSort{T, TStride}"/> is
    /// desktop-only (its internal scan flushes the command buffer between passes, which throws
    /// on the browser backends under the "sync = desktop-only" contract); this awaits the async
    /// scan (<see cref="ScanExtensions.CreateScanAsync{T, TStrideIn, TStrideOut, TScanOperation}"/>)
    /// and works on every backend.
    /// </summary>
    /// <typeparam name="T">The underlying type of the sort operation.</typeparam>
    /// <typeparam name="TStride">The stride of all values.</typeparam>
    /// <param name="stream">The accelerator stream.</param>
    /// <param name="view">The elements to sort.</param>
    /// <param name="temp">The temp view to store temporary results.</param>
    /// <returns>A task that completes when the sorted output is on the device.</returns>
    /// <remarks>The view buffer will be changed during the sorting operation.</remarks>
    public delegate Task RadixSortAsync<T, TStride>(
        AcceleratorStream stream,
        ArrayView1D<T, TStride> view,
        ArrayView<int> temp)
        where T : unmanaged
        where TStride : struct, IStride1D;

    /// <summary>
    /// Represents a radix sort operation that sorts (key, value) pair instances.
    /// </summary>
    /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
    /// <typeparam name="TKeyStride">The stride of all keys.</typeparam>
    /// <typeparam name="TValue">The value type of each element.</typeparam>
    /// <typeparam name="TValueStride">The stride of all values.</typeparam>
    /// <param name="stream">The accelerator stream.</param>
    /// <param name="keys">The keys to sort.</param>
    /// <param name="values">The corresponding values.</param>
    /// <param name="temp">The temp view to store temporary results.</param>
    /// <remarks>The view buffers will be changed during the sorting operation.</remarks>
    public delegate void RadixSortPairs<TKey, TKeyStride, TValue, TValueStride>(
        AcceleratorStream stream,
        ArrayView1D<TKey, TKeyStride> keys,
        ArrayView1D<TValue, TValueStride> values,
        ArrayView<int> temp)
        where TKey : unmanaged
        where TKeyStride : struct, IStride1D
        where TValue : unmanaged
        where TValueStride : struct, IStride1D;

    /// <summary>
    /// Represents a radix sort operation that sorts (key, value) pair instances using
    /// a sequencer to provide all values for each key in the beginning of the operation.
    /// </summary>
    /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
    /// <typeparam name="TKeyStride">The stride of all keys.</typeparam>
    /// <typeparam name="TValue">The value type of each element.</typeparam>
    /// <typeparam name="TValueStride">The stride of all values.</typeparam>
    /// <typeparam name="TSequencer">The sequencer type to generate values.</typeparam>
    /// <param name="stream">The accelerator stream.</param>
    /// <param name="keys">The keys to sort.</param>
    /// <param name="outputValues">The determined output values.</param>
    /// <param name="sequencer">The sequencer to generate the key-value pairs.</param>
    /// <param name="temp">The temp view to store temporary results.</param>
    /// <remarks>The view buffers will be changed during the sorting operation.</remarks>
    public delegate void RadixSortPairs<
        TKey,
        TKeyStride,
        TValue,
        TValueStride,
        TSequencer>(
        AcceleratorStream stream,
        ArrayView1D<TKey, TKeyStride> keys,
        ArrayView1D<TValue, TValueStride> outputValues,
        TSequencer sequencer,
        ArrayView<int> temp)
        where TKey : unmanaged
        where TKeyStride : struct, IStride1D
        where TValue : unmanaged
        where TValueStride : struct, IStride1D
        where TSequencer : struct, ISequencer<TValue>;

    /// <summary>
    /// Represents a radix sort operation using a shuffle and operation logic.
    /// </summary>
    /// <typeparam name="T">The underlying type of the sort operation.</typeparam>
    /// <typeparam name="TStride">The stride of all values.</typeparam>
    /// <param name="stream">The accelerator stream.</param>
    /// <param name="view">The elements to sort.</param>
    /// <remarks>The view buffer will be changed during the sorting operation.</remarks>
    public delegate void BufferedRadixSort<T, TStride>(
        AcceleratorStream stream,
        ArrayView1D<T, TStride> view)
        where T : unmanaged
        where TStride : struct, IStride1D;

    /// <summary>
    /// Asynchronous counterpart of <see cref="BufferedRadixSort{T, TStride}"/> (carries its own
    /// temp buffer). Portable across all backends; the synchronous form is desktop-only.
    /// </summary>
    public delegate Task BufferedRadixSortAsync<T, TStride>(
        AcceleratorStream stream,
        ArrayView1D<T, TStride> view)
        where T : unmanaged
        where TStride : struct, IStride1D;

    /// <summary>
    /// Represents a radix sort operation that sorts (key, value) pair instances.
    /// </summary>
    /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
    /// <typeparam name="TKeyStride">The stride of all keys.</typeparam>
    /// <typeparam name="TValue">The value type of each element.</typeparam>
    /// <typeparam name="TValueStride">The stride of all values.</typeparam>
    /// <param name="stream">The accelerator stream.</param>
    /// <param name="keys">The keys to sort.</param>
    /// <param name="values">The corresponding values.</param>
    /// <remarks>The view buffers will be changed during the sorting operation.</remarks>
    public delegate void BufferedRadixSortPairs<TKey, TKeyStride, TValue, TValueStride>(
        AcceleratorStream stream,
        ArrayView1D<TKey, TKeyStride> keys,
        ArrayView1D<TValue, TValueStride> values)
        where TKey : unmanaged
        where TKeyStride : struct, IStride1D
        where TValue : unmanaged
        where TValueStride : struct, IStride1D;

    /// <summary>
    /// Represents a radix sort operation that sorts (key, value) pair instances using
    /// a sequencer to provide all values for each key in the beginning of the operation.
    /// </summary>
    /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
    /// <typeparam name="TKeyStride">The stride of all keys.</typeparam>
    /// <typeparam name="TValue">The value type of each element.</typeparam>
    /// <typeparam name="TValueStride">The stride of all values.</typeparam>
    /// <typeparam name="TSequencer">The sequencer type to generate values.</typeparam>
    /// <param name="stream">The accelerator stream.</param>
    /// <param name="keys">The keys to sort.</param>
    /// <param name="outputValues">The determined output values.</param>
    /// <param name="sequencer">The sequencer to generate the key-value pairs.</param>
    /// <remarks>The view buffers will be changed during the sorting operation.</remarks>
    public delegate void BufferedRadixSortPairs<
        TKey,
        TKeyStride,
        TValue,
        TValueStride,
        TSequencer>(
        AcceleratorStream stream,
        ArrayView1D<TKey, TKeyStride> keys,
        ArrayView1D<TValue, TValueStride> outputValues,
        TSequencer sequencer)
        where TKey : unmanaged
        where TKeyStride : struct, IStride1D
        where TValue : unmanaged
        where TValueStride : struct, IStride1D
        where TSequencer : struct, ISequencer<TValue>;

    /// <summary>
    /// Asynchronous counterpart of
    /// <see cref="RadixSortPairs{TKey, TKeyStride, TValue, TValueStride}"/> — the portable,
    /// all-backend pair sort. The synchronous form is desktop-only (its internal scan flushes
    /// between passes, throwing on browser); this awaits the async scan.
    /// </summary>
    public delegate Task RadixSortPairsAsync<TKey, TKeyStride, TValue, TValueStride>(
        AcceleratorStream stream,
        ArrayView1D<TKey, TKeyStride> keys,
        ArrayView1D<TValue, TValueStride> values,
        ArrayView<int> temp)
        where TKey : unmanaged
        where TKeyStride : struct, IStride1D
        where TValue : unmanaged
        where TValueStride : struct, IStride1D;

    /// <summary>
    /// Asynchronous counterpart of
    /// <see cref="RadixSortPairs{TKey, TKeyStride, TValue, TValueStride, TSequencer}"/> (sequencer
    /// form) — portable across all backends; see
    /// <see cref="RadixSortPairsAsync{TKey, TKeyStride, TValue, TValueStride}"/> for why.
    /// </summary>
    public delegate Task RadixSortPairsAsync<
        TKey,
        TKeyStride,
        TValue,
        TValueStride,
        TSequencer>(
        AcceleratorStream stream,
        ArrayView1D<TKey, TKeyStride> keys,
        ArrayView1D<TValue, TValueStride> outputValues,
        TSequencer sequencer,
        ArrayView<int> temp)
        where TKey : unmanaged
        where TKeyStride : struct, IStride1D
        where TValue : unmanaged
        where TValueStride : struct, IStride1D
        where TSequencer : struct, ISequencer<TValue>;

    /// <summary>
    /// Asynchronous counterpart of
    /// <see cref="BufferedRadixSortPairs{TKey, TKeyStride, TValue, TValueStride}"/>
    /// (carries its own temp buffer). Portable across all backends.
    /// </summary>
    public delegate Task BufferedRadixSortPairsAsync<TKey, TKeyStride, TValue, TValueStride>(
        AcceleratorStream stream,
        ArrayView1D<TKey, TKeyStride> keys,
        ArrayView1D<TValue, TValueStride> values)
        where TKey : unmanaged
        where TKeyStride : struct, IStride1D
        where TValue : unmanaged
        where TValueStride : struct, IStride1D;

    /// <summary>
    /// Asynchronous counterpart of
    /// <see cref="BufferedRadixSortPairs{TKey, TKeyStride, TValue, TValueStride, TSequencer}"/>
    /// (sequencer form, carries its own temp buffer). Portable across all backends.
    /// </summary>
    public delegate Task BufferedRadixSortPairsAsync<
        TKey,
        TKeyStride,
        TValue,
        TValueStride,
        TSequencer>(
        AcceleratorStream stream,
        ArrayView1D<TKey, TKeyStride> keys,
        ArrayView1D<TValue, TValueStride> outputValues,
        TSequencer sequencer)
        where TKey : unmanaged
        where TKeyStride : struct, IStride1D
        where TValue : unmanaged
        where TValueStride : struct, IStride1D
        where TSequencer : struct, ISequencer<TValue>;

    #endregion

    /// <summary>
    /// Represents a radix-sort provider for a radix sort operation.
    /// </summary>
    /// <remarks>Members of this class are not thread safe.</remarks>
    public sealed class RadixSortProvider : AlgorithmObject
    {
        #region Instance

        [SuppressMessage(
            "Microsoft.Usage",
            "CA2213: Disposable fields should be disposed",
            Justification = "This is disposed in DisposeAccelerator")]
        private readonly MemoryBuffer1D<int, Stride1D.Dense> tempBuffer;

        internal RadixSortProvider(Accelerator accelerator, int tempSize)
            : base(accelerator)
        {
            tempBuffer = accelerator.Allocate1D<int>(tempSize);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new radix sort operation.
        /// </summary>
        /// <typeparam name="T">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TStride">The stride of all values.</typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <returns>The created radix sort handler.</returns>
        public BufferedRadixSort<T, TStride> CreateRadixSort<
            T,
            TStride,
            TRadixSortOperation>()
            where T : unmanaged
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            var radixSort = Accelerator.CreateRadixSort<
                T,
                TStride,
                TRadixSortOperation>();
            return (stream, input) => radixSort(stream, input, tempBuffer.View);
        }

        /// <summary>
        /// Creates a new radix sort pairs operation that sorts tuples of (key, value)
        /// instances.
        /// </summary>
        /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TKeyStride">The stride of all keys.</typeparam>
        /// <typeparam name="TValue">The value type of each element.</typeparam>
        /// <typeparam name="TValueStride">The stride of all values.</typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <returns>The created radix sort handler.</returns>
        public BufferedRadixSortPairs<TKey, TKeyStride, TValue, TValueStride>
            CreateRadixSortPairs<
            TKey,
            TKeyStride,
            TValue,
            TValueStride,
            TRadixSortOperation>()
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            var radixSort = Accelerator.CreateRadixSortPairs<
                TKey,
                TKeyStride,
                TValue,
                TValueStride,
                TRadixSortOperation>();
            return (stream, keys, values) =>
                radixSort(stream, keys, values, tempBuffer.View);
        }

        /// <summary>
        /// Creates a new radix sort pairs operation that sorts tuples of (key, value)
        /// instances.
        /// </summary>
        /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TKeyStride">The stride of all keys.</typeparam>
        /// <typeparam name="TValue">The value type of each element.</typeparam>
        /// <typeparam name="TValueStride">The stride of all values.</typeparam>
        /// <typeparam name="TSequencer">
        /// The sequencer type to generate values.
        /// </typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <returns>The created radix sort handler.</returns>
        public BufferedRadixSortPairs<TKey, TKeyStride, TValue, TValueStride, TSequencer>
            CreateRadixSortPairs<
            TKey,
            TKeyStride,
            TValue,
            TValueStride,
            TSequencer,
            TRadixSortOperation>()
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TSequencer : struct, ISequencer<TValue>
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            var radixSort = Accelerator.CreateRadixSortPairs<
                TKey,
                TKeyStride,
                TValue,
                TValueStride,
                TSequencer,
                TRadixSortOperation>();
            return (stream, keys, values, sequencer) =>
                radixSort(stream, keys, values, sequencer, tempBuffer.View);
        }

        /// <summary>
        /// Creates a new asynchronous buffered radix sort (portable across all backends).
        /// Async counterpart of <see cref="CreateRadixSort{T, TStride, TRadixSortOperation}"/>.
        /// </summary>
        public BufferedRadixSortAsync<T, TStride> CreateRadixSortAsync<
            T,
            TStride,
            TRadixSortOperation>()
            where T : unmanaged
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            var radixSort = Accelerator.CreateRadixSortAsync<T, TStride, TRadixSortOperation>();
            return (stream, view) => radixSort(stream, view, tempBuffer.View);
        }

        /// <summary>
        /// Creates a new asynchronous buffered radix sort pairs operation (portable across all
        /// backends). Async counterpart of
        /// <see cref="CreateRadixSortPairs{TKey, TKeyStride, TValue, TValueStride, TRadixSortOperation}"/>.
        /// </summary>
        public BufferedRadixSortPairsAsync<TKey, TKeyStride, TValue, TValueStride>
            CreateRadixSortPairsAsync<
            TKey,
            TKeyStride,
            TValue,
            TValueStride,
            TRadixSortOperation>()
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            var radixSort = Accelerator.CreateRadixSortPairsAsync<
                TKey,
                TKeyStride,
                TValue,
                TValueStride,
                TRadixSortOperation>();
            return (stream, keys, values) =>
                radixSort(stream, keys, values, tempBuffer.View);
        }

        /// <summary>
        /// Creates a new asynchronous buffered radix sort pairs operation (sequencer form,
        /// portable across all backends). Async counterpart of
        /// <see cref="CreateRadixSortPairs{TKey, TKeyStride, TValue, TValueStride, TSequencer, TRadixSortOperation}"/>.
        /// </summary>
        public BufferedRadixSortPairsAsync<TKey, TKeyStride, TValue, TValueStride, TSequencer>
            CreateRadixSortPairsAsync<
            TKey,
            TKeyStride,
            TValue,
            TValueStride,
            TSequencer,
            TRadixSortOperation>()
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TSequencer : struct, ISequencer<TValue>
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            var radixSort = Accelerator.CreateRadixSortPairsAsync<
                TKey,
                TKeyStride,
                TValue,
                TValueStride,
                TSequencer,
                TRadixSortOperation>();
            return (stream, keys, values, sequencer) =>
                radixSort(stream, keys, values, sequencer, tempBuffer.View);
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
    /// Contains extension methods for radix-sort operations.
    /// </summary>
    public static class RadixSortExtensions
    {
        /// <summary>
        /// DIAGNOSTIC: Per-pass hook. Called after each radix sort pass with (bitIdx, counterView).
        /// The stream is already synchronized before this is called.
        /// counterView has 4*numGroups entries: the per-group bucket counts from Kernel1.
        /// </summary>
        public static Action<int, ArrayView<int>>? PerPassHook { get; set; }

        #region RadixSort Helpers

        /// <summary>
        /// A pass delegate for the first pass.
        /// </summary>
        private delegate void Pass1KernelDelegate<T, TStride>(
            AcceleratorStream stream,
            KernelConfig config,
            ArrayView1D<T, TStride> view,
            ArrayView<int> counter,
            SpecializedValue<int> groupSize,
            int numGroups,
            int paddedLength,
            int shift)
            where T : unmanaged
            where TStride : struct, IStride1D;

        /// <summary>
        /// A pass delegate for the first pass.
        /// </summary>
        private delegate void CPUPass1KernelDelegate<T, TStride>(
            AcceleratorStream stream,
            KernelConfig config,
            ArrayView1D<T, TStride> input,
            ArrayView<T> output,
            ArrayView<int> counter,
            int numGroups,
            int numIterationsPerGroup,
            int shift)
            where T : unmanaged
            where TStride : struct, IStride1D;

        /// <summary>
        /// A pass delegate for the second pass.
        /// </summary>
        private delegate void Pass2KernelDelegate<T, TInputStride, TOutputStride>(
            AcceleratorStream stream,
            KernelConfig config,
            ArrayView1D<T, TInputStride> input,
            ArrayView1D<T, TOutputStride> output,
            ArrayView<int> counter,
            int numGroups,
            int paddedLength,
            int shift)
            where T : unmanaged
            where TInputStride : struct, IStride1D
            where TOutputStride : struct, IStride1D;

        /// <summary>
        /// A pass delegate for the second pass.
        /// </summary>
        private delegate void CPUPass2KernelDelegate<T, TStride>(
            AcceleratorStream stream,
            KernelConfig config,
            ArrayView<T> input,
            ArrayView1D<T, TStride> output,
            ArrayView<int> counter,
            int numGroups,
            int numIterationsPerGroup,
            int shift)
            where T : unmanaged
            where TStride : struct, IStride1D;

        /// <summary>
        /// Computes the required number of temp-storage elements for a radix sort pairs
        /// operation and the given data length.
        /// </summary>
        /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TValue">The value type of each element.</typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="dataLength">The number of data elements to sort.</param>
        /// <returns>
        /// The required number of temp-storage elements in 32 bit ints.
        /// </returns>
        public static Index1D ComputeRadixSortPairsTempStorageSize<
            TKey,
            TValue,
            TRadixSortOperation>(
            this Accelerator accelerator,
            Index1D dataLength)
            where TKey : unmanaged
            where TValue : unmanaged
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            var tempSortSize = ComputeRadixSortTempStorageSize<
                RadixSortPair<TKey, TValue>,
                RadixSortPairsOperation<TKey, TValue, TRadixSortOperation>>(
                accelerator,
                dataLength);

            var tempBufferSize = XMath.DivRoundUp(
                Interop.SizeOf<RadixSortPair<TKey, TValue>>() * dataLength,
                sizeof(int));
            // CreateRadixSortPairs allocates the pairs buffer first; TempViewManager adds up to
            // 63 ints of padding (256-byte alignment). That padding consumes from the remainder
            // passed to the inner radix sort, so we must add it to the total.
            const int maxPaddingPerAlloc = 63; // 256/sizeof(int) - 1
            var extraPadding = maxPaddingPerAlloc; // 1 alloc in outer CreateRadixSortPairs
            return tempSortSize + tempBufferSize + extraPadding;
        }

        /// <summary>
        /// Computes the required number of temp-storage elements for a radix sort
        /// operation and the given data length.
        /// </summary>
        /// <typeparam name="T">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="dataLength">The number of data elements to sort.</param>
        /// <returns>
        /// The required number of temp-storage elements in 32 bit ints.
        /// </returns>
        public static Index1D ComputeRadixSortTempStorageSize<T, TRadixSortOperation>(
            this Accelerator accelerator,
            Index1D dataLength)
            where T : unmanaged
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            LongIndex1D tempScanMemoryLong =
                accelerator.ComputeScanTempStorageSize<T>(dataLength);
            IndexTypeExtensions.AssertIntIndexRange(tempScanMemoryLong);
            Index1D tempScanMemory = tempScanMemoryLong.ToIntIndex();

            int numGroups;
            if (accelerator.AcceleratorType == AcceleratorType.CPU)
                numGroups = accelerator.MaxNumThreads;
            else
            {
                var (gridDim, _) = accelerator.ComputeGridStrideLoopExtent(
                    dataLength,
                    out int numIterationsPerGroup);
                numGroups = gridDim * numIterationsPerGroup;
            }

            long numIntTElementsLong = Interop.ComputeRelativeSizeOf<int, T>(dataLength);
            IndexTypeExtensions.AssertIntIndexRange(numIntTElementsLong);
            int numIntTElements = (int)numIntTElementsLong;

            const int unrollFactor = 4;
            int baseSize = numGroups * unrollFactor * 2 + numIntTElements + tempScanMemory;
            // Add padding for TempViewManager 256-byte alignment.
            // VerifyArguments makes 4 Allocate() calls, each potentially adding up to
            // 63 ints of padding. For CreateRadixSortPairs, the first TempViewManager
            // also does 1 alloc before passing the remainder to the inner radixSort,
            // so that first alloc's padding (up to 63) consumes from the remainder.
            const int maxAllocations = 5;
            const int maxPaddingPerAlloc = 63; // 256/sizeof(int) - 1
            return baseSize + maxAllocations * maxPaddingPerAlloc;
        }

        /// <summary>
        /// Computes the amount of shared memory in bytes that is needed to perform a
        /// group-wide radix sorting.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="groupSize">The size of the group.</param>
        /// <returns>The size of shared memory in bytes.</returns>
        public static int ComputeGroupWideRadixSortSharedMemorySize<T>(
            this Accelerator accelerator,
            int groupSize)
            where T : unmanaged
        {
            var arrayLength = Interop.SizeOf<T>() * groupSize;
            var keyLength = accelerator.WarpSize * sizeof(int);

            return arrayLength + arrayLength % sizeof(int) + 2 * keyLength + sizeof(int);
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Represents a single key/value pair in the scope of radix sort operation.
        /// </summary>
        /// <typeparam name="TKey">The key type to sort.</typeparam>
        /// <typeparam name="TValue">The value type associated to each key.</typeparam>
        public readonly struct RadixSortPair<TKey, TValue>
            where TKey : unmanaged
            where TValue : unmanaged
        {
            /// <summary>
            /// Constructs a new radix-sort pair.
            /// </summary>
            /// <param name="key">The key.</param>
            /// <param name="value">The value.</param>
            public RadixSortPair(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }

            /// <summary>
            /// Returns the key part of this pair that is used for sorting.
            /// </summary>
            public TKey Key { get; }

            /// <summary>
            /// Returns the associated value part of this pair.
            /// </summary>
            public TValue Value { get; }

            /// <summary>
            /// Returns the string representation of this pair.
            /// </summary>
            public readonly override string ToString() => $"({Key}, {Value})";
        }

        /// <summary>
        /// Represents a wrapper operation that works on the key part of a merged
        /// <see cref="RadixSortPair{TKey, TValue}"/> instance.
        /// </summary>
        /// <typeparam name="TKey">The key type to sort.</typeparam>
        /// <typeparam name="TValue">The value type associated to each key.</typeparam>
        /// <typeparam name="TOperation">The underlying operation type.</typeparam>
        public readonly struct RadixSortPairsOperation<TKey, TValue, TOperation> :
            IRadixSortOperation<RadixSortPair<TKey, TValue>>
            where TKey : unmanaged
            where TValue : unmanaged
            where TOperation : struct, IRadixSortOperation<TKey>
        {
            /// <summary>
            /// Returns the default operation.
            /// </summary>
            private static TOperation GetOperation() => default;

            /// <summary>
            /// Returns the number of bits of the parent
            /// <typeparamref name="TOperation"/>.
            /// </summary>
            public readonly int NumBits => GetOperation().NumBits;

            /// <summary>
            /// Returns the default key-value pair based of the parent
            /// <typeparamref name="TOperation"/>.
            /// </summary>
            public readonly RadixSortPair<TKey, TValue> DefaultValue
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => new RadixSortPair<TKey, TValue>(
                    GetOperation().DefaultValue,
                    default);
            }

            /// <summary>
            /// Extracts the bits from the key part of the given radix-sort pair using
            /// the parent <typeparamref name="TOperation"/>.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly int ExtractRadixBits(
                RadixSortPair<TKey, TValue> value,
                int shift,
                int bitMask) =>
                GetOperation().ExtractRadixBits(value.Key, shift, bitMask);
        }

        #endregion

        #region RadixSort Implementation

        private static readonly MethodInfo CPURadixSortKernel1Method =
            typeof(RadixSortExtensions).GetMethod(
                nameof(CPURadixSortKernel1),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        private static readonly MethodInfo CPURadixSortKernel2Method =
            typeof(RadixSortExtensions).GetMethod(
                nameof(CPURadixSortKernel2),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        private static readonly MethodInfo RadixSortKernel1Method =
            typeof(RadixSortExtensions).GetMethod(
                nameof(RadixSortKernel1),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        private static readonly MethodInfo RadixSortKernel2Method =
            typeof(RadixSortExtensions).GetMethod(
                nameof(RadixSortKernel2),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// Represents a single specialization.
        /// </summary>
        internal interface IRadixSortSpecialization
        {
            /// <summary>
            /// Returns the associated constant unroll factor.
            /// </summary>
            int UnrollFactor { get; }

            /// <summary>
            /// Returns the number of bits to increment for the
            /// next radix-sort iteration.
            /// </summary>
            int BitIncrement { get; }
        }

        /// <summary>
        /// Combines the given key and values (given by the sequencer) into a merged
        /// buffer consisting of <see cref="RadixSortPair{TKey, TValue}"/> instances.
        /// </summary>
        internal static void GatherRadixSortPairsKernel<
            TKey,
            TKeyStride,
            TValue,
            TSequencer>(
            Index1D index,
            ArrayView1D<TKey, TKeyStride> keys,
            TSequencer sequencer,
            ArrayView<RadixSortPair<TKey, TValue>> target)
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TSequencer : struct, ISequencer<TValue>
        {
            target[index] = new RadixSortPair<TKey, TValue>(
                keys[index],
                sequencer.ComputeSequenceElement(index));
        }

        /// <summary>
        /// Scatters views of <see cref="RadixSortPair{TKey, TValue}"/> instances into
        /// distinct key and value views.
        /// </summary>
        internal static void ScatterRadixSortPairsKernel<
            TKey,
            TKeyStride,
            TValue,
            TValueStride>(
            Index1D index,
            ArrayView<RadixSortPair<TKey, TValue>> source,
            ArrayView1D<TKey, TKeyStride> keys,
            ArrayView1D<TValue, TValueStride> values)
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
        {
            var pair = source[index];
            keys[index] = pair.Key;
            values[index] = pair.Value;
        }

        /// <summary>
        /// Performs the first radix-sort pass.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStride">The stride of all elements.</typeparam>
        /// <typeparam name="TOperation">The radix-sort operation.</typeparam>
        /// <typeparam name="TSpecialization">The specialization type.</typeparam>
        /// <param name="view">The input view to use.</param>
        /// <param name="counter">The global counter view.</param>
        /// <param name="groupSize">The number of threads in the group.</param>
        /// <param name="numGroups">The number of virtually launched groups.</param>
        /// <param name="paddedLength">The padded length of the input view.</param>
        /// <param name="shift">The bit shift to use.</param>
        internal static void RadixSortKernel1<T, TStride, TOperation, TSpecialization>(
            ArrayView1D<T, TStride> view,
            ArrayView<int> counter,
            SpecializedValue<int> groupSize,
            int numGroups,
            int paddedLength,
            int shift)
            where T : unmanaged
            where TStride : struct, IStride1D
            where TOperation : struct, IRadixSortOperation<T>
            where TSpecialization : struct, IRadixSortSpecialization
        {
            TSpecialization specialization = default;
            var scanMemory = SharedMemory.Allocate<int>(
                groupSize * specialization.UnrollFactor);

            int gridIdx = Grid.IdxX;
            for (
                int i = Grid.GlobalIndex.X;
                i < paddedLength;
                i += GridExtensions.GridStrideLoopStride)
            {
                bool inRange = i < view.Length;

                // Read value from global memory
                TOperation operation = default;
                T value = operation.DefaultValue;
                if (inRange)
                    value = view[i];
                var bits = operation.ExtractRadixBits(
                    value,
                    shift,
                    specialization.UnrollFactor - 1);

                for (int j = 0; j < specialization.UnrollFactor; ++j)
                    scanMemory[Group.IdxX + groupSize * j] = 0;
                if (inRange)
                    scanMemory[Group.IdxX + groupSize * bits] = 1;
                Group.Barrier();

                for (int j = 0; j < specialization.UnrollFactor; ++j)
                {
                    var address = Group.IdxX + groupSize * j;
                    scanMemory[address] =
                        GroupExtensions.ExclusiveScan<int, AddInt32>(scanMemory[address]);
                }
                Group.Barrier();

                if (Group.IdxX == Group.DimX - 1)
                {
                    // Write counters to global memory
                    for (int j = 0; j < specialization.UnrollFactor; ++j)
                    {
                        ref var newOffset = ref scanMemory[Group.IdxX + groupSize * j];
                        newOffset += Utilities.Select(
                            Bitwise.And(inRange, j == bits), 1, 0);
                        counter[j * numGroups + gridIdx] = newOffset;
                    }
                }
                Group.Barrier();

                var gridSize = gridIdx * Group.DimX;
                Index1D pos = gridSize + scanMemory[Group.IdxX + groupSize * bits] -
                    Utilities.Select(
                        Bitwise.And(inRange, Group.IdxX == Group.DimX - 1), 1, 0);
                for (int j = 1; j <= bits; ++j)
                {
                    pos += scanMemory[groupSize * j - 1] +
                        Utilities.Select(j - 1 == bits, 1, 0);
                }

                // Pre-sort the current value into the corresponding segment
                if (inRange)
                    view[pos] = value;
                Group.Barrier();

                gridIdx += Grid.DimX;
            }
        }

        /// <summary>
        /// Performs the first radix-sort pass on the CPU.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStride">The stride of the input view.</typeparam>
        /// <typeparam name="TOperation">The radix-sort operation.</typeparam>
        /// <typeparam name="TSpecialization">The specialization type.</typeparam>
        /// <param name="input">The input view to use.</param>
        /// <param name="output">The input view to use.</param>
        /// <param name="counter">The global counter view.</param>
        /// <param name="numGroups">The number of virtually launched groups.</param>
        /// <param name="numIterationsPerGroup">
        /// The number of iterations per group.
        /// </param>
        /// <param name="shift">The bit shift to use.</param>
        internal static void CPURadixSortKernel1<
            T,
            TStride,
            TOperation,
            TSpecialization>(
            ArrayView1D<T, TStride> input,
            ArrayView<T> output,
            ArrayView<int> counter,
            int numGroups,
            int numIterationsPerGroup,
            int shift)
            where T : unmanaged
            where TStride : struct, IStride1D
            where TOperation : struct, IRadixSortOperation<T>
            where TSpecialization : struct, IRadixSortSpecialization
        {
            TSpecialization specialization = default;
            var scanMemory = SharedMemory.Allocate<int>(specialization.UnrollFactor);
            var addMemory = SharedMemory.Allocate<int>(specialization.UnrollFactor);

            for (int j = 0; j < specialization.UnrollFactor; ++j)
            {
                scanMemory[j] = 0;
                addMemory[j] = 0;
            }

            var tileInfo = new TileInfo(input.IntLength, numIterationsPerGroup);

            // Compute local segment information
            for (Index1D i = tileInfo.StartIndex; i < tileInfo.MaxLength; ++i)
            {
                // Read value from global memory
                TOperation operation = default;
                T value = input[i];
                var bits = operation.ExtractRadixBits(
                    value,
                    shift,
                    specialization.UnrollFactor - 1);
                ++scanMemory[bits];
            }

            // Store global counter
            for (int j = 0; j < specialization.UnrollFactor; ++j)
                counter[numGroups * j + Grid.IdxX] = scanMemory[j];

            int scanned = 0;
            int previous = scanMemory[0];
            for (int j = 1; j < specialization.UnrollFactor; ++j)
            {
                scanned += previous;
                previous = scanMemory[j];
                scanMemory[j] = scanned;
            }
            scanMemory[0] = 0;

            // Pre-sort the current value into the corresponding segment
            for (Index1D i = tileInfo.StartIndex; i < tileInfo.MaxLength; ++i)
            {
                // Read value from global memory
                TOperation operation = default;
                T value = input[i];
                var bits = operation.ExtractRadixBits(
                    value,
                    shift,
                    specialization.UnrollFactor - 1);

                Index1D pos = tileInfo.StartIndex;
                pos += addMemory[bits]++;
                pos += scanMemory[bits];
                output[pos] = value;
            }
        }

        /// <summary>
        /// Resolves the exclusive scan-value from the given counter view.
        /// </summary>
        /// <param name="index">The current index.</param>
        /// <param name="counter">The counter view.</param>
        /// <returns>The exclusive sum.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetExclusiveCount(Index1D index, ArrayView<int> counter)
        {
            // Clamp to avoid OOB: WGSL select() evaluates both arms unconditionally
            // after IfConversion, so counter[index-1] executes even when index==0.
            int idx = (int)index - 1;
            if (idx < 0) idx = 0;
            return index < Index1D.One ? 0 : counter[idx];
        }

        /// <summary>
        /// Performs the second radix-sort pass.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TInputStride">The stride of the input view.</typeparam>
        /// <typeparam name="TOutputStride">The stride of the output view.</typeparam>
        /// <typeparam name="TOperation">The radix-sort operation.</typeparam>
        /// <typeparam name="TSpecialization">The specialization type.</typeparam>
        /// <param name="input">The input view to use.</param>
        /// <param name="output">The output view to use.</param>
        /// <param name="counter">The global counter view.</param>
        /// <param name="numGroups">The number of virtually launched groups.</param>
        /// <param name="paddedLength">The padded length of the input view.</param>
        /// <param name="shift">The bit shift to use.</param>
        internal static void RadixSortKernel2<
            T,
            TInputStride,
            TOutputStride,
            TOperation,
            TSpecialization>(
            ArrayView1D<T, TInputStride> input,
            ArrayView1D<T, TOutputStride> output,
            ArrayView<int> counter,
            int numGroups,
            int paddedLength,
            int shift)
            where T : unmanaged
            where TInputStride : struct, IStride1D
            where TOutputStride : struct, IStride1D
            where TOperation : struct, IRadixSortOperation<T>
            where TSpecialization : struct, IRadixSortSpecialization
        {
            var gridIdx = Grid.IdxX;

            for (
                int i = Grid.GlobalIndex.X;
                i < paddedLength;
                i += GridExtensions.GridStrideLoopStride)
            {
                bool inRange = i < input.Length;

                // Read value from global memory
                TOperation operation = default;
                T value = operation.DefaultValue;
                if (inRange)
                    value = input[i];

                TSpecialization specialization = default;
                var bits = operation.ExtractRadixBits(
                    value,
                    shift,
                    specialization.UnrollFactor - 1);

                int offset = 0;
                int pos = GetExclusiveCount(bits * numGroups + gridIdx, counter) +
                    Group.IdxX;

                for (int w = 0; w < bits; ++w)
                {
                    var address = w * numGroups + gridIdx;

                    int baseCounter = counter[address];
                    int negativeOffset = GetExclusiveCount(address, counter);

                    offset += baseCounter - negativeOffset;
                }

                pos -= offset;

                if (inRange)
                    output[pos] = value;

                gridIdx += Grid.DimX;
            }
        }

        /// <summary>
        /// Performs the second radix-sort pass.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <typeparam name="TStride">The stride of the output view.</typeparam>
        /// <typeparam name="TOperation">The radix-sort operation.</typeparam>
        /// <typeparam name="TSpecialization">The specialization type.</typeparam>
        /// <param name="input">The input view to use.</param>
        /// <param name="output">The output view to use.</param>
        /// <param name="counter">The global counter view.</param>
        /// <param name="numGroups">The number of virtually launched groups.</param>
        /// <param name="numIterationsPerGroup">
        /// The number of iterations per group.
        /// </param>
        /// <param name="shift">The bit shift to use.</param>
        internal static void CPURadixSortKernel2<
            T,
            TStride,
            TOperation,
            TSpecialization>(
            ArrayView<T> input,
            ArrayView1D<T, TStride> output,
            ArrayView<int> counter,
            int numGroups,
            int numIterationsPerGroup,
            int shift)
            where T : unmanaged
            where TStride : struct, IStride1D
            where TOperation : struct, IRadixSortOperation<T>
            where TSpecialization : struct, IRadixSortSpecialization
        {
            TSpecialization specialization = default;
            var tileInfo = new TileInfo(input.IntLength, numIterationsPerGroup);

            for (Index1D i = tileInfo.StartIndex; i < tileInfo.MaxLength; ++i)
            {
                // Read value from global memory
                TOperation operation = default;
                T value = input[i];

                var bits = operation.ExtractRadixBits(
                    value,
                    shift,
                    specialization.UnrollFactor - 1);

                int offset = 0;
                int pos = GetExclusiveCount(bits * numGroups + Grid.IdxX, counter) +
                    i - tileInfo.StartIndex;

                // Compute offset
                for (int w = 0; w < bits; ++w)
                {
                    var address = w * numGroups + Grid.IdxX;

                    int baseCounter = counter[address];
                    int negativeOffset = GetExclusiveCount(address, counter);

                    offset += baseCounter - negativeOffset;
                }

                pos -= offset;

                // Move value
                output[pos] = value;
            }
        }

        #endregion

        #region RadixSort Specializations

        /// <summary>
        /// A specialization with unroll factor 4.
        /// </summary>
        readonly struct Specialization4 : IRadixSortSpecialization
        {
            /// <summary cref="IRadixSortSpecialization.UnrollFactor"/>
            public int UnrollFactor => 4;

            /// <summary cref="IRadixSortSpecialization.BitIncrement"/>
            public int BitIncrement => 2;
        }

        #endregion

        #region RadixSort

        /// <summary>
        /// Creates a new radix sort pairs operation that retrieves its values by
        /// calling the given sequencer.
        /// </summary>
        /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TKeyStride">The stride of all keys.</typeparam>
        /// <typeparam name="TValue">The value type of each element.</typeparam>
        /// <typeparam name="TValueStride">The stride of all values.</typeparam>
        /// <typeparam name="TSequencer">
        /// The sequencer type to generate values.
        /// </typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <returns>The created radix sort handler.</returns>
        public static RadixSortPairs<TKey, TKeyStride, TValue, TValueStride, TSequencer>
            CreateRadixSortPairs<
            TKey,
            TKeyStride,
            TValue,
            TValueStride,
            TSequencer,
            TRadixSortOperation>(
            this Accelerator accelerator)
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TSequencer : struct, ISequencer<TValue>
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            var gatherKernel = accelerator.LoadAutoGroupedKernel<
                Index1D,
                ArrayView1D<TKey, TKeyStride>,
                TSequencer,
                ArrayView<RadixSortPair<TKey, TValue>>>(GatherRadixSortPairsKernel);
            var scatterKernel = accelerator.LoadAutoGroupedKernel<
                Index1D,
                ArrayView<RadixSortPair<TKey, TValue>>,
                ArrayView1D<TKey, TKeyStride>,
                ArrayView1D<TValue, TValueStride>>(ScatterRadixSortPairsKernel);
            var radixSort = CreateRadixSort<
                RadixSortPair<TKey, TValue>,
                Stride1D.Dense,
                RadixSortPairsOperation<TKey, TValue, TRadixSortOperation>>(accelerator);
            return (stream, keys, values, sequencer, tempView) =>
            {
                if (!keys.IsValid)
                    throw new ArgumentNullException(nameof(keys));
                if (!values.IsValid)
                    throw new ArgumentNullException(nameof(values));
                if (keys.Length != values.Length)
                    throw new ArgumentOutOfRangeException(nameof(values));

                // Allocate temp memory
                var viewManager = new TempViewManager(tempView, nameof(tempView));
                var elements = viewManager.Allocate<
                    RadixSortPair<TKey, TValue>>(
                    keys.Length);

                // keys, values => (key, value)s
                gatherKernel(stream, elements.IntLength, keys, sequencer, elements);

                // sort elements
                radixSort(
                    stream,
                    elements,
                    viewManager.TempView.SubView(viewManager.NumInts));

                // (key, value)s => keys, values
                scatterKernel(stream, elements.IntLength, elements, keys, values);
            };
        }

        /// <summary>
        /// Asynchronous counterpart of
        /// <see cref="CreateRadixSortPairs{TKey, TKeyStride, TValue, TValueStride, TSequencer, TRadixSortOperation}(Accelerator)"/>.
        /// Gather + the portable async keys sort
        /// (<see cref="CreateRadixSortAsync{T, TStride, TRadixSortOperation}(Accelerator)"/>) + scatter;
        /// the gather/scatter kernels are plain dispatches, only the inner sort awaits the async scan.
        /// </summary>
        public static RadixSortPairsAsync<TKey, TKeyStride, TValue, TValueStride, TSequencer>
            CreateRadixSortPairsAsync<
            TKey,
            TKeyStride,
            TValue,
            TValueStride,
            TSequencer,
            TRadixSortOperation>(
            this Accelerator accelerator)
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TSequencer : struct, ISequencer<TValue>
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            var gatherKernel = accelerator.LoadAutoGroupedKernel<
                Index1D,
                ArrayView1D<TKey, TKeyStride>,
                TSequencer,
                ArrayView<RadixSortPair<TKey, TValue>>>(GatherRadixSortPairsKernel);
            var scatterKernel = accelerator.LoadAutoGroupedKernel<
                Index1D,
                ArrayView<RadixSortPair<TKey, TValue>>,
                ArrayView1D<TKey, TKeyStride>,
                ArrayView1D<TValue, TValueStride>>(ScatterRadixSortPairsKernel);
            var radixSortAsync = CreateRadixSortAsync<
                RadixSortPair<TKey, TValue>,
                Stride1D.Dense,
                RadixSortPairsOperation<TKey, TValue, TRadixSortOperation>>(accelerator);
            return async (stream, keys, values, sequencer, tempView) =>
            {
                if (!keys.IsValid)
                    throw new ArgumentNullException(nameof(keys));
                if (!values.IsValid)
                    throw new ArgumentNullException(nameof(values));
                if (keys.Length != values.Length)
                    throw new ArgumentOutOfRangeException(nameof(values));

                var viewManager = new TempViewManager(tempView, nameof(tempView));
                var elements = viewManager.Allocate<
                    RadixSortPair<TKey, TValue>>(
                    keys.Length);

                gatherKernel(stream, elements.IntLength, keys, sequencer, elements);

                await radixSortAsync(
                    stream,
                    elements,
                    viewManager.TempView.SubView(viewManager.NumInts)).ConfigureAwait(false);

                scatterKernel(stream, elements.IntLength, elements, keys, values);
            };
        }

        /// <summary>
        /// Creates a new radix sort pairs operation that uses a values source view.
        /// </summary>
        /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TKeyStride">The stride of all keys.</typeparam>
        /// <typeparam name="TValue">The value type of each element.</typeparam>
        /// <typeparam name="TValueStride">The stride of all values.</typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <returns>The created radix sort handler.</returns>
        public static RadixSortPairs<TKey, TKeyStride, TValue, TValueStride>
            CreateRadixSortPairs<
            TKey,
            TKeyStride,
            TValue,
            TValueStride,
            TRadixSortOperation>(
            this Accelerator accelerator)
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            // WebGL: gather-only transform feedback. Sort the pairs by SCATTER (the struct-packed
            // gather/scatter pairs path needs a multi-field scatter). Compute each element's stable
            // 1-bit-split destination from the keys, then scatter BOTH keys and values by that dest.
            // Handles 4-byte (int/uint/float -> R32I/R32UI/R32F) AND 8-byte (long/double, i64/f64
            // emulated as two uint texels -> componentsPerElement=2) key/value types. Packed Half is
            // sub-word and needs the unpacked-f32 working representation (handled below).
            //
            // Half KEY (+ any 4/8-byte non-Half value): sort the keys via the unpacked f32 representation
            // while scattering the values with the generic int/float/uint program. Bound by reflection
            // (this method is generic on TKey, so T == Half can't bind the IRadixSortOperation<Half>
            // constraint statically). A Half VALUE is not yet supported (no consuming test); it falls
            // through to the generic CPU/default path.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL &&
                accelerator is IScatterProvider scatterProviderHalfKey &&
                typeof(TKey) == typeof(Half) &&
                (Interop.SizeOf<TValue>() == 4 || Interop.SizeOf<TValue>() == 8) &&
                typeof(TValue) != typeof(Half))
            {
                var handler = typeof(RadixSortExtensions)
                    .GetMethod(nameof(CreateWebGLScatterRadixSortPairsHalfKey),
                        BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(
                        typeof(TKeyStride), typeof(TValue), typeof(TValueStride),
                        typeof(TRadixSortOperation))
                    .Invoke(null, new object[] { accelerator, scatterProviderHalfKey })!;
                return (RadixSortPairs<TKey, TKeyStride, TValue, TValueStride>)handler;
            }

            if (accelerator.AcceleratorType == AcceleratorType.WebGL &&
                accelerator is IScatterProvider scatterProviderPairs &&
                (Interop.SizeOf<TKey>() == 4 || Interop.SizeOf<TKey>() == 8) &&
                (Interop.SizeOf<TValue>() == 4 || Interop.SizeOf<TValue>() == 8) &&
                typeof(TKey) != typeof(Half) && typeof(TValue) != typeof(Half))
                return CreateWebGLScatterRadixSortPairs<
                    TKey, TKeyStride, TValue, TValueStride, TRadixSortOperation>(
                    accelerator, scatterProviderPairs);

            var radixSortPairs = accelerator.CreateRadixSortPairs<
                TKey,
                TKeyStride,
                TValue,
                TValueStride,
                ViewSourceSequencer<TValue, TValueStride>,
                TRadixSortOperation>();
            return (stream, keys, values, tempView) =>
            {
                radixSortPairs(
                    stream,
                    keys,
                    values,
                    new ViewSourceSequencer<TValue, TValueStride>(values),
                    tempView);
            };
        }

        /// <summary>
        /// Asynchronous counterpart of
        /// <see cref="CreateRadixSortPairs{TKey, TKeyStride, TValue, TValueStride, TRadixSortOperation}(Accelerator)"/>.
        /// The WebGL scatter paths are flush-free (draw-call boundaries) so they wrap the sync
        /// builder; everything else routes through the async sequencer variant. Calls the private
        /// WebGL builders directly so it stays valid after sync CreateRadixSortPairs throws on browser.
        /// </summary>
        public static RadixSortPairsAsync<TKey, TKeyStride, TValue, TValueStride>
            CreateRadixSortPairsAsync<
            TKey,
            TKeyStride,
            TValue,
            TValueStride,
            TRadixSortOperation>(
            this Accelerator accelerator)
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            // WebGL Half-key scatter (flush-free): wrap the private reflection-bound builder.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL &&
                accelerator is IScatterProvider scatterProviderHalfKey &&
                typeof(TKey) == typeof(Half) &&
                (Interop.SizeOf<TValue>() == 4 || Interop.SizeOf<TValue>() == 8) &&
                typeof(TValue) != typeof(Half))
            {
                var handler = typeof(RadixSortExtensions)
                    .GetMethod(nameof(CreateWebGLScatterRadixSortPairsHalfKey),
                        BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(
                        typeof(TKeyStride), typeof(TValue), typeof(TValueStride),
                        typeof(TRadixSortOperation))
                    .Invoke(null, new object[] { accelerator, scatterProviderHalfKey })!;
                return WrapSyncRadixSortPairs(
                    (RadixSortPairs<TKey, TKeyStride, TValue, TValueStride>)handler);
            }

            // WebGL generic scatter (flush-free): wrap the private builder.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL &&
                accelerator is IScatterProvider scatterProviderPairs &&
                (Interop.SizeOf<TKey>() == 4 || Interop.SizeOf<TKey>() == 8) &&
                (Interop.SizeOf<TValue>() == 4 || Interop.SizeOf<TValue>() == 8) &&
                typeof(TKey) != typeof(Half) && typeof(TValue) != typeof(Half))
                return WrapSyncRadixSortPairs(
                    CreateWebGLScatterRadixSortPairs<
                        TKey, TKeyStride, TValue, TValueStride, TRadixSortOperation>(
                        accelerator, scatterProviderPairs));

            // Everything else: gather + async keys sort + scatter via the async sequencer variant.
            var radixSortPairsAsync = accelerator.CreateRadixSortPairsAsync<
                TKey,
                TKeyStride,
                TValue,
                TValueStride,
                ViewSourceSequencer<TValue, TValueStride>,
                TRadixSortOperation>();
            return (stream, keys, values, tempView) =>
                radixSortPairsAsync(
                    stream,
                    keys,
                    values,
                    new ViewSourceSequencer<TValue, TValueStride>(values),
                    tempView);
        }

        /// <summary>
        /// Wraps a synchronous <see cref="RadixSortPairs{TKey, TKeyStride, TValue, TValueStride}"/>
        /// as its async counterpart for backends whose sync pair sort performs no browser-throwing
        /// primitive (WebGL's flush-free scatter sort). The work completes synchronously, so the
        /// task is already completed — NOT the fake <c>Task.Run(syncGpuWork)</c> anti-pattern.
        /// </summary>
        private static RadixSortPairsAsync<TKey, TKeyStride, TValue, TValueStride>
            WrapSyncRadixSortPairs<TKey, TKeyStride, TValue, TValueStride>(
            RadixSortPairs<TKey, TKeyStride, TValue, TValueStride> sync)
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D =>
            (stream, keys, values, temp) =>
            {
                sync(stream, keys, values, temp);
                return Task.CompletedTask;
            };

        /// <summary>
        /// Creates a new radix sort operation.
        /// </summary>
        /// <typeparam name="T">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TStride">The stride of all values.</typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <returns>The created radix sort handler.</returns>
        public static RadixSort<T, TStride> CreateRadixSort<
            T,
            TStride,
            TRadixSortOperation>(
            this Accelerator accelerator)
            where T : unmanaged
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            var initializer = accelerator.CreateInitializer<int, Stride1D.Dense>();
            var inclusiveScan = accelerator.CreateScan<
                int,
                Stride1D.Dense,
                Stride1D.Dense,
                AddInt32>(ScanKind.Inclusive);

            var specializationType = typeof(Specialization4);
            var specialization = new Specialization4();

            TRadixSortOperation sortOperation = default;
            if (sortOperation.NumBits < 1 ||
                sortOperation.NumBits % (specialization.BitIncrement * 2) != 0)
            {
                throw new NotSupportedException(
                    ErrorMessages.NotSupportedNumberOfRadixSortBits);
            }

            // WebGL: transform feedback is gather-only — no in-kernel scatter and limited loop
            // codegen (a gather + in-kernel binary search hangs the GL context). Use the backend's
            // render-points-to-texture SCATTER primitive (IScatterProvider): per bit, extract the bit,
            // exclusive-scan the flags, compute each element's stable 1-bit-split destination
            // element-wise, then SCATTER. No in-kernel loop, no shared memory.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL &&
                accelerator is IScatterProvider scatterProvider)
            {
                // Half is sub-word (2-per-texel packed) - the whole-texel scatter can't move an
                // individual Half, so it sorts via an unpacked f32 working representation. Bound by
                // reflection: this method is generic on T, so the compiler can't see T == Half to bind
                // the IRadixSortOperation<Half> constraint statically (the runtime type always satisfies
                // it). Called once per handler creation, not per dispatch.
                return CreateWebGLScatterRadixSortDispatch<
                    T, TStride, TRadixSortOperation>(accelerator, scatterProvider);
            }

            if (accelerator.AcceleratorType == AcceleratorType.CPU)
            {
                var pass1Kernel = accelerator.LoadKernel<CPUPass1KernelDelegate<T,
                    TStride>>(CPURadixSortKernel1Method.MakeGenericMethod(
                    typeof(T), typeof(TStride), typeof(TRadixSortOperation),
                    specializationType));
                var pass2Kernel = accelerator.LoadKernel<CPUPass2KernelDelegate<T,
                    TStride>>(CPURadixSortKernel2Method.MakeGenericMethod(
                    typeof(T), typeof(TStride), typeof(TRadixSortOperation),
                    specializationType));

                return (stream, input, tempView) =>
                {
                    if (input.Length > int.MaxValue)
                    {
                        throw new NotSupportedException(
                            ErrorMessages.NotSupportedArrayView64);
                    }

                    var (gridDim, groupDim) = (accelerator.MaxNumThreads, 1);
                    int numVirtualGroups = gridDim;
                    long numIterationsPerGroupLong =
                        XMath.DivRoundUp(input.Length, gridDim);
                    IndexTypeExtensions.AssertIntIndexRange(numIterationsPerGroupLong);
                    int numIterationsPerGroup = (int)numIterationsPerGroupLong;

                    VerifyArguments<T, TStride, TRadixSortOperation>(
                        accelerator,
                        input,
                        tempView,
                        specialization.UnrollFactor,
                        numVirtualGroups,
                        out var counterView,
                        out var counterView2,
                        out var tempScanView,
                        out var tempOutputView);

                    TRadixSortOperation radixSortOperation = default;
                    for (
                        int bitIdx = 0;
                        bitIdx < radixSortOperation.NumBits;
                        bitIdx += specialization.BitIncrement)
                    {
                        // Write to the temp output view
                        initializer(stream, counterView, 0);
                        pass1Kernel(
                            stream,
                            (gridDim, groupDim),
                            input,
                            tempOutputView,
                            counterView,
                            numVirtualGroups,
                            numIterationsPerGroup,
                            bitIdx);

                        inclusiveScan(
                            stream,
                            counterView,
                            counterView2,
                            tempScanView);
                        pass2Kernel(
                            stream,
                            (gridDim, groupDim),
                            tempOutputView,
                            input,
                            counterView2,
                            numVirtualGroups,
                            numIterationsPerGroup,
                            bitIdx);
                    }
                };
            }
            else
            {
                // WebGPU/Wasm need explicit KernelSpecialization to bake the correct
                // workgroup size into compiled code, enabling static SharedMemory
                // allocations (SpecializedValue<int> becomes a compile-time constant).
                var radixSpec = (accelerator.AcceleratorType == AcceleratorType.WebGPU
                    || accelerator.AcceleratorType == AcceleratorType.Wasm)
                    ? new KernelSpecialization(
                        accelerator.MaxNumThreadsPerGroup, null)
                    : KernelSpecialization.Empty;

                // Load specialized versions for dense and strided views
                var pass1Kernel = accelerator.LoadKernel<Pass1KernelDelegate<T,
                    TStride>>(RadixSortKernel1Method.MakeGenericMethod(
                    typeof(T), typeof(TStride), typeof(TRadixSortOperation),
                    specializationType), radixSpec);
                var pass1DenseKernel = accelerator.LoadKernel<Pass1KernelDelegate<T,
                    Stride1D.Dense>>(RadixSortKernel1Method.MakeGenericMethod(
                    typeof(T), typeof(Stride1D.Dense), typeof(TRadixSortOperation),
                    specializationType), radixSpec);

                // Load specialized versions for dense and strided views
                var pass2Kernel = accelerator.LoadKernel<Pass2KernelDelegate<T,
                    TStride, Stride1D.Dense>>(RadixSortKernel2Method.MakeGenericMethod(
                    typeof(T), typeof(Stride1D.Dense), typeof(TStride),
                    typeof(TRadixSortOperation), specializationType), radixSpec);
                var pass2DenseKernel = accelerator.LoadKernel<Pass2KernelDelegate<T,
                    Stride1D.Dense, TStride>>(RadixSortKernel2Method.MakeGenericMethod(
                    typeof(T), typeof(TStride), typeof(Stride1D.Dense),
                    typeof(TRadixSortOperation), specializationType), radixSpec);

                return (stream, input, tempView) =>
                {
                    var (gridDim, groupDim) = accelerator.ComputeGridStrideLoopExtent(
                        input.IntLength,
                        out int numIterationsPerGroup);
                    int numVirtualGroups = gridDim * numIterationsPerGroup;
                    int lengthInformation = XMath.DivRoundUp(input.IntLength, groupDim) *
                        groupDim;
                    VerifyArguments<T, TStride, TRadixSortOperation>(
                        accelerator,
                        input,
                        tempView,
                        specialization.UnrollFactor,
                        numVirtualGroups,
                        out var counterView,
                        out var counterView2,
                        out var tempScanView,
                        out var tempOutputView);

                    // Perform the first step writing to the temp scan view
                    TRadixSortOperation radixSortOperation = default;

                    for (int bitIdx = 0; bitIdx < radixSortOperation.NumBits;)
                    {
                        initializer(stream, counterView, 0);
                        pass1Kernel(
                            stream,
                            (gridDim, groupDim),
                            input,
                            counterView,
                            SpecializedValue.New<int>(groupDim),
                            numVirtualGroups,
                            lengthInformation,
                            bitIdx);

                        inclusiveScan(
                            stream,
                            counterView,
                            counterView2,
                            tempScanView);

                        pass2Kernel(
                            stream,
                            (gridDim, groupDim),
                            input,
                            tempOutputView,
                            counterView2,
                            numVirtualGroups,
                            lengthInformation,
                            bitIdx);

                        // DIAGNOSTIC: per-pass hook with counter data
                        if (PerPassHook != null)
                        {
                            stream.Synchronize();
                            PerPassHook(bitIdx, counterView);
                        }

                        bitIdx += specialization.BitIncrement;
                        Debug.Assert(bitIdx < radixSortOperation.NumBits);

                        initializer(stream, counterView, 0);

                        pass1DenseKernel(
                            stream,
                            (gridDim, groupDim),
                            tempOutputView,
                            counterView,
                            SpecializedValue.New<int>(groupDim),
                            numVirtualGroups,
                            lengthInformation,
                            bitIdx);

                        inclusiveScan(
                            stream,
                            counterView,
                            counterView2,
                            tempScanView);

                        pass2DenseKernel(
                            stream,
                            (gridDim, groupDim),
                            tempOutputView,
                            input,
                            counterView2,
                            numVirtualGroups,
                            lengthInformation,
                            bitIdx);

                        // DIAGNOSTIC: per-pass hook with counter data
                        if (PerPassHook != null)
                        {
                            stream.Synchronize();
                            PerPassHook(bitIdx, counterView);
                        }

                        bitIdx += specialization.BitIncrement;
                    }
                };
            }
        }

        /// <summary>
        /// Creates a new asynchronous radix sort — the portable, all-backend counterpart of
        /// <see cref="CreateRadixSort{T, TStride, TRadixSortOperation}(Accelerator)"/>.
        /// The synchronous radix sort is desktop-only (its internal scan flushes the command
        /// buffer between passes, which throws on the browser backends under the
        /// "sync = desktop-only" contract); this awaits the async scan and runs everywhere.
        /// </summary>
        /// <typeparam name="T">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TStride">The stride of all values.</typeparam>
        /// <typeparam name="TRadixSortOperation">The radix sort operation type.</typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <returns>The created async radix sort handler.</returns>
        public static RadixSortAsync<T, TStride> CreateRadixSortAsync<
            T,
            TStride,
            TRadixSortOperation>(
            this Accelerator accelerator)
            where T : unmanaged
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            switch (accelerator.AcceleratorType)
            {
                // Deferred command encoder — the internal scan needs await FlushAsync()
                // between passes (shared by WebGPU and the Wasm multi-group path).
                case AcceleratorType.WebGPU:
                case AcceleratorType.Wasm:
                    return CreateMultiPassRadixSortAsync<
                        T, TStride, TRadixSortOperation>(accelerator);
                // WebGL scatter radix sort uses the flush-free WebGL scan + scatter draw calls
                // (no browser-throwing primitive), so wrapping the sync builder is genuine, not
                // fake-async. Call the private builder directly so this stays valid after sync
                // CreateRadixSort begins throwing on browser backends.
                case AcceleratorType.WebGL
                    when accelerator is IScatterProvider scatterProvider:
                    return WrapSyncRadixSort(
                        CreateWebGLScatterRadixSortDispatch<
                            T, TStride, TRadixSortOperation>(accelerator, scatterProvider));
                // CPU / Cuda / OpenCL: sync radix sort runs on desktop (its scan's flush is a
                // no-op); wrapping the sync builder is correct (it does not throw on desktop).
                default:
                    return WrapSyncRadixSort(
                        accelerator.CreateRadixSort<T, TStride, TRadixSortOperation>());
            }
        }

        /// <summary>
        /// Wraps a synchronous <see cref="RadixSort{T, TStride}"/> as a
        /// <see cref="RadixSortAsync{T, TStride}"/> for backends whose sync radix sort performs
        /// no browser-throwing primitive (desktop, and WebGL's flush-free scatter sort). The
        /// work genuinely completes synchronously, so the task is already completed — this is
        /// NOT the fake <c>Task.Run(syncGpuWork)</c> anti-pattern.
        /// </summary>
        private static RadixSortAsync<T, TStride> WrapSyncRadixSort<T, TStride>(
            RadixSort<T, TStride> sync)
            where T : unmanaged
            where TStride : struct, IStride1D =>
            (stream, view, temp) =>
            {
                sync(stream, view, temp);
                return Task.CompletedTask;
            };

        /// <summary>
        /// Selects the WebGL scatter radix sort builder (Half uses the unpacked-f32 variant;
        /// all other key types use the direct scatter). Shared by the sync
        /// <see cref="CreateRadixSort{T, TStride, TRadixSortOperation}(Accelerator)"/> WebGL
        /// branch and <see cref="CreateRadixSortAsync{T, TStride, TRadixSortOperation}(Accelerator)"/>.
        /// </summary>
        private static RadixSort<T, TStride> CreateWebGLScatterRadixSortDispatch<
            T,
            TStride,
            TRadixSortOperation>(
            Accelerator accelerator,
            IScatterProvider scatterProvider)
            where T : unmanaged
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            // Half is sub-word (2-per-texel packed) — the whole-texel scatter can't move an
            // individual Half, so it sorts via an unpacked f32 working representation. Bound by
            // reflection: this method is generic on T, so the compiler can't see T == Half to bind
            // the IRadixSortOperation<Half> constraint statically (the runtime type always satisfies
            // it). Called once per handler creation, not per dispatch.
            if (typeof(T) == typeof(Half))
            {
                var handler = typeof(RadixSortExtensions)
                    .GetMethod(nameof(CreateWebGLScatterRadixSortHalf),
                        BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(typeof(TStride), typeof(TRadixSortOperation))
                    .Invoke(null, new object[] { accelerator, scatterProvider })!;
                return (RadixSort<T, TStride>)handler;
            }
            return CreateWebGLScatterRadixSort<T, TStride, TRadixSortOperation>(
                accelerator, scatterProvider);
        }

        /// <summary>
        /// Async twin of the WebGPU/Wasm multi-pass radix sort built inside
        /// <see cref="CreateRadixSort{T, TStride, TRadixSortOperation}(Accelerator)"/>. Identical
        /// per-bit pass loop, except the internal inclusive scan is awaited
        /// (<see cref="ScanExtensions.CreateScanAsync{T, TStrideIn, TStrideOut, TScanOperation}"/>,
        /// whose <c>await FlushAsync()</c> provides the inter-pass command-buffer boundary the sync
        /// scan's <c>Flush()</c> gave) and the diagnostic per-pass hook awaits the real drain.
        /// </summary>
        private static RadixSortAsync<T, TStride> CreateMultiPassRadixSortAsync<
            T,
            TStride,
            TRadixSortOperation>(
            Accelerator accelerator)
            where T : unmanaged
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            var initializer = accelerator.CreateInitializer<int, Stride1D.Dense>();
            var inclusiveScanAsync = accelerator.CreateScanAsync<
                int,
                Stride1D.Dense,
                Stride1D.Dense,
                AddInt32>(ScanKind.Inclusive);

            var specializationType = typeof(Specialization4);
            var specialization = new Specialization4();

            TRadixSortOperation sortOperation = default;
            if (sortOperation.NumBits < 1 ||
                sortOperation.NumBits % (specialization.BitIncrement * 2) != 0)
            {
                throw new NotSupportedException(
                    ErrorMessages.NotSupportedNumberOfRadixSortBits);
            }

            // WebGPU/Wasm need explicit KernelSpecialization to bake the correct
            // workgroup size into compiled code, enabling static SharedMemory
            // allocations (SpecializedValue<int> becomes a compile-time constant).
            var radixSpec = (accelerator.AcceleratorType == AcceleratorType.WebGPU
                || accelerator.AcceleratorType == AcceleratorType.Wasm)
                ? new KernelSpecialization(
                    accelerator.MaxNumThreadsPerGroup, null)
                : KernelSpecialization.Empty;

            // Load specialized versions for dense and strided views
            var pass1Kernel = accelerator.LoadKernel<Pass1KernelDelegate<T,
                TStride>>(RadixSortKernel1Method.MakeGenericMethod(
                typeof(T), typeof(TStride), typeof(TRadixSortOperation),
                specializationType), radixSpec);
            var pass1DenseKernel = accelerator.LoadKernel<Pass1KernelDelegate<T,
                Stride1D.Dense>>(RadixSortKernel1Method.MakeGenericMethod(
                typeof(T), typeof(Stride1D.Dense), typeof(TRadixSortOperation),
                specializationType), radixSpec);

            // Load specialized versions for dense and strided views
            var pass2Kernel = accelerator.LoadKernel<Pass2KernelDelegate<T,
                TStride, Stride1D.Dense>>(RadixSortKernel2Method.MakeGenericMethod(
                typeof(T), typeof(Stride1D.Dense), typeof(TStride),
                typeof(TRadixSortOperation), specializationType), radixSpec);
            var pass2DenseKernel = accelerator.LoadKernel<Pass2KernelDelegate<T,
                Stride1D.Dense, TStride>>(RadixSortKernel2Method.MakeGenericMethod(
                typeof(T), typeof(TStride), typeof(Stride1D.Dense),
                typeof(TRadixSortOperation), specializationType), radixSpec);

            return async (stream, input, tempView) =>
            {
                var (gridDim, groupDim) = accelerator.ComputeGridStrideLoopExtent(
                    input.IntLength,
                    out int numIterationsPerGroup);
                int numVirtualGroups = gridDim * numIterationsPerGroup;
                int lengthInformation = XMath.DivRoundUp(input.IntLength, groupDim) *
                    groupDim;
                VerifyArguments<T, TStride, TRadixSortOperation>(
                    accelerator,
                    input,
                    tempView,
                    specialization.UnrollFactor,
                    numVirtualGroups,
                    out var counterView,
                    out var counterView2,
                    out var tempScanView,
                    out var tempOutputView);

                // Perform the first step writing to the temp scan view
                TRadixSortOperation radixSortOperation = default;

                for (int bitIdx = 0; bitIdx < radixSortOperation.NumBits;)
                {
                    initializer(stream, counterView, 0);
                    pass1Kernel(
                        stream,
                        (gridDim, groupDim),
                        input,
                        counterView,
                        SpecializedValue.New<int>(groupDim),
                        numVirtualGroups,
                        lengthInformation,
                        bitIdx);

                    await inclusiveScanAsync(
                        stream,
                        counterView,
                        counterView2,
                        tempScanView).ConfigureAwait(false);

                    pass2Kernel(
                        stream,
                        (gridDim, groupDim),
                        input,
                        tempOutputView,
                        counterView2,
                        numVirtualGroups,
                        lengthInformation,
                        bitIdx);

                    // DIAGNOSTIC: per-pass hook with counter data
                    if (PerPassHook != null)
                    {
                        await stream.SynchronizeAsync().ConfigureAwait(false);
                        PerPassHook(bitIdx, counterView);
                    }

                    bitIdx += specialization.BitIncrement;
                    Debug.Assert(bitIdx < radixSortOperation.NumBits);

                    initializer(stream, counterView, 0);

                    pass1DenseKernel(
                        stream,
                        (gridDim, groupDim),
                        tempOutputView,
                        counterView,
                        SpecializedValue.New<int>(groupDim),
                        numVirtualGroups,
                        lengthInformation,
                        bitIdx);

                    await inclusiveScanAsync(
                        stream,
                        counterView,
                        counterView2,
                        tempScanView).ConfigureAwait(false);

                    pass2DenseKernel(
                        stream,
                        (gridDim, groupDim),
                        tempOutputView,
                        input,
                        counterView2,
                        numVirtualGroups,
                        lengthInformation,
                        bitIdx);

                    // DIAGNOSTIC: per-pass hook with counter data
                    if (PerPassHook != null)
                    {
                        await stream.SynchronizeAsync().ConfigureAwait(false);
                        PerPassHook(bitIdx, counterView);
                    }

                    bitIdx += specialization.BitIncrement;
                }
            };
        }

        #region WebGL scatter-based Radix Sort (stable 1-bit split via IScatterProvider; no in-kernel loop)

        private static void WebGLScatterRadixCopyIn<T, TStride>(
            Index1D index, ArrayView1D<T, TStride> input, ArrayView1D<T, Stride1D.Dense> output)
            where T : unmanaged
            where TStride : struct, IStride1D =>
            output[index.X] = input[index.X];

        private static void WebGLScatterRadixCopyOut<T, TStride>(
            Index1D index, ArrayView1D<T, Stride1D.Dense> input, ArrayView1D<T, TStride> output)
            where T : unmanaged
            where TStride : struct, IStride1D =>
            output[index.X] = input[index.X];

        private static void WebGLScatterRadixExtractBit<T, TRadixSortOperation>(
            Index1D index, ArrayView1D<T, Stride1D.Dense> keys,
            ArrayView1D<int, Stride1D.Dense> flags, int bit)
            where T : unmanaged
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            TRadixSortOperation op = default;
            flags[index.X] = op.ExtractRadixBits(keys[index.X], bit, 1);
        }

        // dest[i] = stable 1-bit-split target: zeros (flag 0) keep their order first, then ones.
        // onePrefix = exclusive scan of flags (# ones before i); totalFalse from the last element.
        // Element-wise (no loop) — sidesteps WebGL's vertex-shader loop-codegen wall.
        private static void WebGLScatterRadixComputeDest(
            Index1D index, ArrayView1D<int, Stride1D.Dense> flags,
            ArrayView1D<int, Stride1D.Dense> onePrefix, ArrayView1D<int, Stride1D.Dense> dest, int n)
        {
            int i = index.X;
            int totalOnes = onePrefix[n - 1] + flags[n - 1];
            int totalFalse = n - totalOnes;
            dest[i] = flags[i] == 0 ? (i - onePrefix[i]) : (totalFalse + onePrefix[i]);
        }

        // Scatter glsl-type for the radix working buffers. MUST match how the WebGL backend represents
        // these buffers in normal kernel dispatches (GLSLKernelFunctionGenerator.GetBufferElementType):
        //   Float32 -> "float" (R32F);  Int32 (int AND uint) -> "int" (R32I);
        //   Int64 (long/ulong) AND Float64 (double) -> "uint" (R32UI), emulated as TWO uint texels.
        // Using the wrong program makes the scatter's sampler read mismatched-format buffers as garbage
        // (this was the uint bug: "uint"/R32UI read R32I keys). Bits are preserved; unsigned/float ORDER
        // comes from ExtractRadixBits. 64-bit types pair with componentsPerElement=2 (see WebGLScatterCpe).
        private static string WebGLScatterValueType<T>() where T : unmanaged =>
            typeof(T) == typeof(float) ? "float"
            : typeof(T) == typeof(double) || typeof(T) == typeof(long) || typeof(T) == typeof(ulong) ? "uint"
            : "int";

        // Texels per element for the scatter: 8-byte i64/f64 are emulated as two 32-bit texels.
        private static int WebGLScatterCpe<T>() where T : unmanaged => Interop.SizeOf<T>() == 8 ? 2 : 1;

        private static RadixSort<T, TStride> CreateWebGLScatterRadixSort<
            T, TStride, TRadixSortOperation>(Accelerator accelerator, IScatterProvider scatter)
            where T : unmanaged
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            var copyIn = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<T, TStride>, ArrayView1D<T, Stride1D.Dense>>(
                WebGLScatterRadixCopyIn<T, TStride>);
            var copyOut = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<T, Stride1D.Dense>, ArrayView1D<T, TStride>>(
                WebGLScatterRadixCopyOut<T, TStride>);
            var extractBit = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<T, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int>(
                WebGLScatterRadixExtractBit<T, TRadixSortOperation>);
            var computeDest = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>,
                ArrayView1D<int, Stride1D.Dense>, int>(WebGLScatterRadixComputeDest);
            var exclusiveScan = accelerator.CreateScan<
                int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Exclusive);

            int numBits = default(TRadixSortOperation).NumBits;
            string valueType = WebGLScatterValueType<T>();

            return (stream, view, temp) =>
            {
                int n = (int)view.Length;
                if (n <= 1)
                    return;

                using var keysA = accelerator.Allocate1D<T>(n);
                using var keysB = accelerator.Allocate1D<T>(n);
                using var flags = accelerator.Allocate1D<int>(n);
                using var onePrefix = accelerator.Allocate1D<int>(n);
                using var dest = accelerator.Allocate1D<int>(n);
                using var scanTemp = accelerator.Allocate1D<int>(1); // WebGL scan ignores temp

                copyIn(stream, n, view, keysA.View);

                var src = keysA;
                var dst = keysB;
                for (int bit = 0; bit < numBits; bit++)
                {
                    extractBit(stream, n, src.View, flags.View, bit);
                    exclusiveScan(stream, flags.View, onePrefix.View, scanTemp.View);
                    computeDest(stream, n, flags.View, onePrefix.View, dest.View, n);
                    // dst[dest[i]] = src[i] (GPU-side render-to-texture scatter; stays GPU-resident)
                    scatter.Scatter(dst.View, src.View, dest.View, n, valueType);
                    var tmp = src; src = dst; dst = tmp;
                }

                copyOut(stream, n, src.View, view);
            };
        }

        // Key-value variant: compute the split destination from the KEYS, then scatter both keys and
        // values by that same dest so they stay aligned. 4-byte key+value only (guarded at the call site).
        private static RadixSortPairs<TKey, TKeyStride, TValue, TValueStride>
            CreateWebGLScatterRadixSortPairs<TKey, TKeyStride, TValue, TValueStride, TRadixSortOperation>(
            Accelerator accelerator, IScatterProvider scatter)
            where TKey : unmanaged
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            var copyInKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TKey, TKeyStride>, ArrayView1D<TKey, Stride1D.Dense>>(
                WebGLScatterRadixCopyIn<TKey, TKeyStride>);
            var copyOutKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TKey, Stride1D.Dense>, ArrayView1D<TKey, TKeyStride>>(
                WebGLScatterRadixCopyOut<TKey, TKeyStride>);
            var copyInVals = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TValue, TValueStride>, ArrayView1D<TValue, Stride1D.Dense>>(
                WebGLScatterRadixCopyIn<TValue, TValueStride>);
            var copyOutVals = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TValue, Stride1D.Dense>, ArrayView1D<TValue, TValueStride>>(
                WebGLScatterRadixCopyOut<TValue, TValueStride>);
            var extractBit = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TKey, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int>(
                WebGLScatterRadixExtractBit<TKey, TRadixSortOperation>);
            var computeDest = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>,
                ArrayView1D<int, Stride1D.Dense>, int>(WebGLScatterRadixComputeDest);
            var exclusiveScan = accelerator.CreateScan<
                int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Exclusive);

            int numBits = default(TRadixSortOperation).NumBits;
            string keyType = WebGLScatterValueType<TKey>();
            string valType = WebGLScatterValueType<TValue>();
            int keyCpe = WebGLScatterCpe<TKey>();
            int valCpe = WebGLScatterCpe<TValue>();

            return (stream, keys, values, tempView) =>
            {
                int n = (int)keys.Length;
                if (n <= 1)
                    return;

                using var keysA = accelerator.Allocate1D<TKey>(n);
                using var keysB = accelerator.Allocate1D<TKey>(n);
                using var valsA = accelerator.Allocate1D<TValue>(n);
                using var valsB = accelerator.Allocate1D<TValue>(n);
                using var flags = accelerator.Allocate1D<int>(n);
                using var onePrefix = accelerator.Allocate1D<int>(n);
                using var dest = accelerator.Allocate1D<int>(n);
                using var scanTemp = accelerator.Allocate1D<int>(1);

                copyInKeys(stream, n, keys, keysA.View);
                copyInVals(stream, n, values, valsA.View);

                var kSrc = keysA;
                var kDst = keysB;
                var vSrc = valsA;
                var vDst = valsB;
                for (int bit = 0; bit < numBits; bit++)
                {
                    extractBit(stream, n, kSrc.View, flags.View, bit);
                    exclusiveScan(stream, flags.View, onePrefix.View, scanTemp.View);
                    computeDest(stream, n, flags.View, onePrefix.View, dest.View, n);
                    scatter.Scatter(kDst.View, kSrc.View, dest.View, n, keyType, keyCpe);
                    scatter.Scatter(vDst.View, vSrc.View, dest.View, n, valType, valCpe);
                    var kt = kSrc; kSrc = kDst; kDst = kt;
                    var vt = vSrc; vSrc = vDst; vDst = vt;
                }

                copyOutKeys(stream, n, kSrc.View, keys);
                copyOutVals(stream, n, vSrc.View, values);
            };
        }

        // --- Half-key scatter radix (sub-word, needs an unpacked working representation) ---
        // Half is 16-bit and the WebGL backend packs 2 Halves per R32I texel. The render-to-texture
        // scatter writes WHOLE 32-bit texels, so it can't move an individual Half (and the multi-texel
        // `cpe` param only helps the multi-texel 64-bit case, never a SUB-texel value). So sort using an
        // UNPACKED f32 working representation (1 element per R32F texel - the proven "float" scatter path):
        //   - copy-in widens each Half to f32 (lossless: f16 is a strict subset of f32),
        //   - the radix bit is derived by narrowing back to Half and calling the canonical
        //     ExtractRadixBits<Half> (single source of truth; handles negatives via the sign-flip +
        //     ones-complement transform),
        //   - copy-out narrows the sorted f32 back to Half (exact round-trip for any value that began as a
        //     Half). Denormal Halves flush to signed zero on the WebGL Half load - a structural property
        //     of WebGL's f16 emulation shared by every Half op, not specific to the sort.

        private static void WebGLScatterRadixCopyInHalf<TStride>(
            Index1D index, ArrayView1D<Half, TStride> input,
            ArrayView1D<float, Stride1D.Dense> output)
            where TStride : struct, IStride1D =>
            output[index.X] = (float)input[index.X];

        private static void WebGLScatterRadixCopyOutHalf<TStride>(
            Index1D index, ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<Half, TStride> output)
            where TStride : struct, IStride1D =>
            output[index.X] = (Half)input[index.X];

        private static void WebGLScatterRadixExtractBitHalf<TRadixSortOperation>(
            Index1D index, ArrayView1D<float, Stride1D.Dense> keys,
            ArrayView1D<int, Stride1D.Dense> flags, int bit)
            where TRadixSortOperation : struct, IRadixSortOperation<Half>
        {
            TRadixSortOperation op = default;
            flags[index.X] = op.ExtractRadixBits((Half)keys[index.X], bit, 1);
        }

        // Keys-only Half sort. Invoked by reflection from CreateRadixSort (the outer method is generic on
        // T; the compiler can't see T == Half, so it can't bind the IRadixSortOperation<Half> constraint
        // statically - the runtime type always satisfies it). Called once per handler, not per dispatch.
        private static RadixSort<Half, TStride> CreateWebGLScatterRadixSortHalf<
            TStride, TRadixSortOperation>(Accelerator accelerator, IScatterProvider scatter)
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<Half>
        {
            var copyIn = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<Half, TStride>, ArrayView1D<float, Stride1D.Dense>>(
                WebGLScatterRadixCopyInHalf<TStride>);
            var copyOut = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<Half, TStride>>(
                WebGLScatterRadixCopyOutHalf<TStride>);
            var extractBit = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int>(
                WebGLScatterRadixExtractBitHalf<TRadixSortOperation>);
            var computeDest = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>,
                ArrayView1D<int, Stride1D.Dense>, int>(WebGLScatterRadixComputeDest);
            var exclusiveScan = accelerator.CreateScan<
                int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Exclusive);

            int numBits = default(TRadixSortOperation).NumBits; // 16

            return (stream, view, temp) =>
            {
                int n = (int)view.Length;
                if (n <= 1)
                    return;

                using var keysA = accelerator.Allocate1D<float>(n);
                using var keysB = accelerator.Allocate1D<float>(n);
                using var flags = accelerator.Allocate1D<int>(n);
                using var onePrefix = accelerator.Allocate1D<int>(n);
                using var dest = accelerator.Allocate1D<int>(n);
                using var scanTemp = accelerator.Allocate1D<int>(1);

                copyIn(stream, n, view, keysA.View);

                var src = keysA;
                var dst = keysB;
                for (int bit = 0; bit < numBits; bit++)
                {
                    extractBit(stream, n, src.View, flags.View, bit);
                    exclusiveScan(stream, flags.View, onePrefix.View, scanTemp.View);
                    computeDest(stream, n, flags.View, onePrefix.View, dest.View, n);
                    scatter.Scatter(dst.View, src.View, dest.View, n, "float");
                    var tmp = src; src = dst; dst = tmp;
                }

                copyOut(stream, n, src.View, view);
            };
        }

        // Half-KEY pairs sort (Half key + any 4/8-byte non-Half value). Keys use the unpacked f32 working
        // representation; values use the same int/float/uint scatter program as the generic pairs path.
        // Invoked by reflection from CreateRadixSortPairs (same constraint-binding reason as above).
        private static RadixSortPairs<Half, TKeyStride, TValue, TValueStride>
            CreateWebGLScatterRadixSortPairsHalfKey<
                TKeyStride, TValue, TValueStride, TRadixSortOperation>(
            Accelerator accelerator, IScatterProvider scatter)
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<Half>
        {
            var copyInKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<Half, TKeyStride>, ArrayView1D<float, Stride1D.Dense>>(
                WebGLScatterRadixCopyInHalf<TKeyStride>);
            var copyOutKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<Half, TKeyStride>>(
                WebGLScatterRadixCopyOutHalf<TKeyStride>);
            var copyInVals = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TValue, TValueStride>, ArrayView1D<TValue, Stride1D.Dense>>(
                WebGLScatterRadixCopyIn<TValue, TValueStride>);
            var copyOutVals = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TValue, Stride1D.Dense>, ArrayView1D<TValue, TValueStride>>(
                WebGLScatterRadixCopyOut<TValue, TValueStride>);
            var extractBit = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int>(
                WebGLScatterRadixExtractBitHalf<TRadixSortOperation>);
            var computeDest = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>,
                ArrayView1D<int, Stride1D.Dense>, int>(WebGLScatterRadixComputeDest);
            var exclusiveScan = accelerator.CreateScan<
                int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Exclusive);

            int numBits = default(TRadixSortOperation).NumBits; // 16
            string valType = WebGLScatterValueType<TValue>();
            int valCpe = WebGLScatterCpe<TValue>();

            return (stream, keys, values, tempView) =>
            {
                int n = (int)keys.Length;
                if (n <= 1)
                    return;

                using var keysA = accelerator.Allocate1D<float>(n);
                using var keysB = accelerator.Allocate1D<float>(n);
                using var valsA = accelerator.Allocate1D<TValue>(n);
                using var valsB = accelerator.Allocate1D<TValue>(n);
                using var flags = accelerator.Allocate1D<int>(n);
                using var onePrefix = accelerator.Allocate1D<int>(n);
                using var dest = accelerator.Allocate1D<int>(n);
                using var scanTemp = accelerator.Allocate1D<int>(1);

                copyInKeys(stream, n, keys, keysA.View);
                copyInVals(stream, n, values, valsA.View);

                var kSrc = keysA;
                var kDst = keysB;
                var vSrc = valsA;
                var vDst = valsB;
                for (int bit = 0; bit < numBits; bit++)
                {
                    extractBit(stream, n, kSrc.View, flags.View, bit);
                    exclusiveScan(stream, flags.View, onePrefix.View, scanTemp.View);
                    computeDest(stream, n, flags.View, onePrefix.View, dest.View, n);
                    scatter.Scatter(kDst.View, kSrc.View, dest.View, n, "float", 1);
                    scatter.Scatter(vDst.View, vSrc.View, dest.View, n, valType, valCpe);
                    var kt = kSrc; kSrc = kDst; kDst = kt;
                    var vt = vSrc; vSrc = vDst; vDst = vt;
                }

                copyOutKeys(stream, n, kSrc.View, keys);
                copyOutVals(stream, n, vSrc.View, values);
            };
        }

        #endregion

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void VerifyArguments<T, TStride, TRadixSortOperation>(
            Accelerator accelerator,
            ArrayView1D<T, TStride> input,
            ArrayView<int> tempView,
            int unrollFactor,
            int numVirtualGroups,
            out ArrayView<int> counterView,
            out ArrayView<int> counterView2,
            out ArrayView<int> tempScanView,
            out ArrayView<T> tempOutputView)
            where T : unmanaged
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            if (!input.IsValid)
                throw new ArgumentNullException(nameof(input));

            var viewManager = new TempViewManager(tempView, nameof(tempView));

            int counterOffset = numVirtualGroups * unrollFactor;
            long tempScanMemorySize =
                accelerator.ComputeScanTempStorageSize<T>(counterOffset);

            tempOutputView = viewManager.Allocate<T>(input.Length);
            counterView = viewManager.Allocate<int>(counterOffset);
            counterView2 = viewManager.Allocate<int>(counterOffset);
            tempScanView = viewManager.Allocate<int>(tempScanMemorySize);
        }

        /// <summary>
        /// Creates a new specialized radix-sort provider that has its own cache.
        /// Note that the resulting provider has to be disposed manually.
        /// </summary>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="tempStorageSize">
        /// The number of 32bit integers to use as a temp storage.
        /// </param>
        /// <returns>The created provider.</returns>
        public static RadixSortProvider CreateRadixSortProvider(
            this Accelerator accelerator,
            Index1D tempStorageSize) =>
            tempStorageSize < 1
            ? throw new ArgumentOutOfRangeException(nameof(tempStorageSize))
            : new RadixSortProvider(accelerator, tempStorageSize);

        /// <summary>
        /// Allocates a temporary memory view.
        /// </summary>
        /// <typeparam name="T">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="dataLength">The expected maximum data length to sort.</param>
        /// <returns>The allocated temporary view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RadixSortProvider CreateRadixSortProvider<T, TRadixSortOperation>(
            this Accelerator accelerator,
            Index1D dataLength)
            where T : unmanaged
            where TRadixSortOperation : struct, IRadixSortOperation<T>
        {
            var tempSize = accelerator.ComputeRadixSortTempStorageSize<
                T,
                TRadixSortOperation>(dataLength);
            return CreateRadixSortProvider(accelerator, tempSize);
        }

        /// <summary>
        /// Allocates a temporary memory view.
        /// </summary>
        /// <typeparam name="TKey">The underlying type of the sort operation.</typeparam>
        /// <typeparam name="TValue">The value type of each element.</typeparam>
        /// <typeparam name="TRadixSortOperation">
        /// The type of the radix-sort operation.
        /// </typeparam>
        /// <param name="accelerator">The accelerator.</param>
        /// <param name="dataLength">
        /// The expected maximum data length to sort (number of keys).
        /// </param>
        /// <returns>The allocated temporary view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RadixSortProvider CreateRadixSortProvider<
            TKey,
            TValue,
            TRadixSortOperation>(
            this Accelerator accelerator,
            Index1D dataLength)
            where TKey : unmanaged
            where TValue : unmanaged
            where TRadixSortOperation : struct, IRadixSortOperation<TKey>
        {
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<
                TKey,
                TValue,
                TRadixSortOperation>(dataLength);
            return CreateRadixSortProvider(accelerator, tempSize);
        }

        #endregion
    }
}
