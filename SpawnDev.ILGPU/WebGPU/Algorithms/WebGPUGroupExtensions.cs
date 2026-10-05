// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                    Copyright (c) 2024-2026 SpawnDev / LostBeard
//
// File: WebGPUGroupExtensions.cs
//
// WebGPU-specific group-level scan and reduce implementations.
// Uses SharedMemory + Group.Barrier() approach (no warp shuffle needed).
// ---------------------------------------------------------------------------------------

using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using System;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.WebGPU.Algorithms
{
    /// <summary>
    /// Custom WebGPU-specific group-level implementations.
    /// Uses shared memory and barriers — all supported by the WebGPU backend.
    /// </summary>
    static class WebGPUGroupExtensions
    {
        #region Reduce

        /// <summary cref="GroupExtensions.Reduce{T, TReduction}(T)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Reduce<T, TReduction>(T value)
            where T : unmanaged
            where TReduction : struct, IScanReduceOperation<T> =>
            AllReduce<T, TReduction>(value);

        /// <summary cref="GroupExtensions.AllReduce{T, TReduction}(T)"/>
        /// <remarks>
        /// Allocation size (2048) must be at least as large as the maximum workgroup
        /// size (1024 on WebGPU) so every thread can write its value. Using 2048 also
        /// keeps it distinct from common histogram buffers (1024) to prevent shared
        /// memory aliasing in the WGSL code generator's type+size matcher.
        /// </remarks>
        public static T AllReduce<T, TReduction>(T value)
            where T : unmanaged
            where TReduction : struct, IScanReduceOperation<T>
        {
            // Use shared memory approach — no warp shuffle needed.
            // Every thread writes its value, then first thread reduces.
            // Size 2048 — must cover max workgroup size (1024) and stay distinct
            // from histogram buffers (1024) to avoid WGSL shared memory aliasing.
            var sharedMemory = SharedMemory.Allocate<T>(2048);
            sharedMemory[Group.LinearIndex] = value;
            Group.Barrier();

            // First thread performs sequential reduction
            if (Group.IsFirstThread)
            {
                TReduction reduction = default;
                T result = sharedMemory[0];
                for (int i = 1; i < Group.Dimension.Size; ++i)
                    result = reduction.Apply(result, sharedMemory[i]);
                sharedMemory[0] = result;
            }
            Group.Barrier();

            return sharedMemory[0];
        }

        #endregion

        #region Scan

        /// <summary cref="GroupExtensions.ExclusiveScan{T, TScanOperation}(T)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ExclusiveScan<T, TScanOperation>(T value)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T> =>
            ExclusiveScanWithBoundaries<T, TScanOperation>(value, out var _);

        /// <summary cref="GroupExtensions.InclusiveScan{T, TScanOperation}(T)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T InclusiveScan<T, TScanOperation>(T value)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T> =>
            InclusiveScanWithBoundaries<T, TScanOperation>(value, out var _);

        /// <summary cref="GroupExtensions.ExclusiveScanWithBoundaries{T, TScanOperation}(
        /// T, out ScanBoundaries{T})"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ExclusiveScanWithBoundaries<T, TScanOperation>(
            T value,
            out ScanBoundaries<T> boundaries)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var sharedMemory = InclusiveScanImplementation<T, TScanOperation>(value);
            // The EXCLUSIVE scan's boundaries, as the CPU (ILGroupExtensions) and OpenCL/PTX define them: the right
            // boundary is the last lane's exclusive value - the sum of every lane BUT the last - because
            // ExclusiveScanNextIteration adds the last lane's value itself. The inclusive total here counted the last
            // element twice at every chunk of a multi-chunk scan: CreateScan(Exclusive) went wrong from the first
            // 1,024-element chunk whose last element was non-zero (SpawnScene densify, 2026-10-05).
            boundaries = new ScanBoundaries<T>(
                sharedMemory[0],
                Group.Dimension.Size > 1
                    ? sharedMemory[Group.Dimension.Size - 2]
                    : default(TScanOperation).Identity);
            T result = Group.IsFirstThread
                ? default(TScanOperation).Identity
                : sharedMemory[Group.LinearIndex - 1];
            // Barrier ensures all threads have finished reading shared memory
            // before a subsequent scan call overwrites it.
            Group.Barrier();
            return result;
        }

        /// <summary cref="GroupExtensions.InclusiveScanWithBoundaries{T, TScanOperation}(
        /// T, out ScanBoundaries{T})"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T InclusiveScanWithBoundaries<T, TScanOperation>(
            T value,
            out ScanBoundaries<T> boundaries)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var sharedMemory = InclusiveScanImplementation<T, TScanOperation>(
                value);
            boundaries = new ScanBoundaries<T>(
                sharedMemory[0],
                sharedMemory[Group.Dimension.Size - 1]);
            T result = sharedMemory[Group.LinearIndex];
            // Barrier ensures all threads have finished reading shared memory
            // before a subsequent scan call overwrites it.
            Group.Barrier();
            return result;
        }

        /// <summary>
        /// Performs a group-wide inclusive scan using shared memory.
        /// </summary>
        /// <remarks>
        /// IMPORTANT: The allocation size (2048) must differ from other shared memory
        /// allocations used in the same kernel (e.g. RadixSortKernel1's histogram buffer
        /// of groupSize*unrollFactor = 1024 ints). The WGSL code generator resolves
        /// shared memory allocations by (elementType, arraySize). If two allocations
        /// share the same type+size, they can be aliased to the same var&lt;workgroup&gt;
        /// variable, corrupting data. Using 2048 here ensures the scan workspace is
        /// distinguishable from any 1024-element histogram buffer.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ArrayView<T> InclusiveScanImplementation<T, TScanOperation>(
            T value)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            // Load values into shared memory
            // Size 2048 (not 1024) to avoid aliasing with same-typed, same-sized
            // shared allocations in the calling kernel (see remarks above).
            var sharedMemory = SharedMemory.Allocate<T>(2048);
            sharedMemory[Group.LinearIndex] = value;
            Group.Barrier();

            // First thread performs sequential inclusive scan
            if (Group.IsFirstThread)
            {
                TScanOperation scanOperation = default;
                for (int i = 1; i < Group.Dimension.Size; ++i)
                {
                    sharedMemory[i] = scanOperation.Apply(
                        sharedMemory[i - 1],
                        sharedMemory[i]);
                }
            }
            Group.Barrier();

            return sharedMemory;
        }

        /// <summary>
        /// Prepares for the next iteration of a group-wide exclusive scan.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ExclusiveScanNextIteration<T, TScanOperation>(
            T leftBoundary,
            T rightBoundary,
            T currentValue)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var scanOperation = default(TScanOperation);
            var nextBoundary = scanOperation.Apply(leftBoundary, rightBoundary);
            return scanOperation.Apply(
                nextBoundary,
                Group.Broadcast(currentValue, Group.DimX - 1));
        }

        /// <summary>
        /// Prepares for the next iteration of a group-wide inclusive scan.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T InclusiveScanNextIteration<T, TScanOperation>(
            T leftBoundary,
            T rightBoundary,
            T currentValue)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var scanOperation = default(TScanOperation);
            return scanOperation.Apply(leftBoundary, rightBoundary);
        }

        #endregion
    }
}
