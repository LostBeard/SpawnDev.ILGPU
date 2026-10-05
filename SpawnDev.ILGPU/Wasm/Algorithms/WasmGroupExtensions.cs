// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                    Copyright (c) 2024-2026 SpawnDev / LostBeard
//
// File: WasmGroupExtensions.cs
//
// Wasm-specific group-level scan and reduce implementations.
// Uses SharedMemory + Group.Barrier() approach (no warp shuffle needed).
// Mirrors ILGroupExtensions.cs / WebGPUGroupExtensions.cs.
// ---------------------------------------------------------------------------------------

using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using System;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Wasm.Algorithms
{
    /// <summary>
    /// Custom Wasm-specific group-level implementations.
    /// Uses shared memory and barriers — both supported by the Wasm backend.
    /// </summary>
    static class WasmGroupExtensions
    {
        #region Reduce

        /// <summary cref="GroupExtensions.Reduce{T, TReduction}(T)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Reduce<T, TReduction>(T value)
            where T : unmanaged
            where TReduction : struct, IScanReduceOperation<T> =>
            AllReduce<T, TReduction>(value);

        /// <summary cref="GroupExtensions.AllReduce{T, TReduction}(T)"/>
        public static T AllReduce<T, TReduction>(T value)
            where T : unmanaged
            where TReduction : struct, IScanReduceOperation<T>
        {
            // Use shared memory approach — no warp shuffle needed.
            // Every thread writes its value, then first thread reduces.
            var sharedMemory = SharedMemory.Allocate<T>(1024);
            sharedMemory[Group.IdxX] = value;
            Group.Barrier();

            // First thread performs sequential reduction
            if (Group.IdxX == 0)
            {
                TReduction reduction = default;
                T result = sharedMemory[0];
                for (int i = 1; i < Group.DimX; ++i)
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

            // Copy scan results to a SEPARATE shared alloca to defeat IR pointer aliasing.
            // Uses size 256 (not 1024) to ensure the IR/codegen treats this as a
            // DISTINCT alloca from the scan workspace (which is 1024). Same-size allocas
            // may get deduped by SetupSharedAllocations, causing overlap in shared memory.
            var scanResults = SharedMemory.Allocate<T>(256);
            scanResults[Group.IdxX] = sharedMemory[Group.IdxX];
            Group.Barrier();

            // Right boundary = the group's INCLUSIVE total, paired with an ExclusiveScanNextIteration that adds nothing
            // more (PTX's pairing). Computing the exclusive right boundary here instead (CPU/OpenCL's pairing) made the
            // Wasm backend emit a function that fails to compile ("expected 1 elements on the stack for return, found
            // 0"), with a ternary and with Math.Max alike - a Wasm codegen bug, recorded separately.
            boundaries = new ScanBoundaries<T>(
                scanResults[0],
                scanResults[Group.DimX - 1]);
            return Group.IdxX == 0
                ? default(TScanOperation).Identity
                : scanResults[Group.IdxX - 1];
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
            var sharedMemory = InclusiveScanImplementation<T, TScanOperation>(value);

            var scanResults = SharedMemory.Allocate<T>(256);
            scanResults[Group.IdxX] = sharedMemory[Group.IdxX];
            Group.Barrier();

            boundaries = new ScanBoundaries<T>(
                scanResults[0],
                scanResults[Group.DimX - 1]);
            return scanResults[Group.IdxX];
        }

        /// <summary>
        /// Performs a group-wide inclusive scan using shared memory.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ArrayView<T> InclusiveScanImplementation<T, TScanOperation>(
            T value)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            // Load values into shared memory
            var sharedMemory = SharedMemory.Allocate<T>(1024);
            sharedMemory[Group.IdxX] = value;
            Group.Barrier();

            // First thread performs sequential inclusive scan
            if (Group.IdxX == 0)
            {
                TScanOperation scanOperation = default;
                for (int i = 1; i < Group.DimX; ++i)
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
            // rightBoundary is the previous chunk's INCLUSIVE total (ExclusiveScanWithBoundaries above), so the next chunk
            // starts at left + right. This also added the last lane's value (CPU/OpenCL's pairing, for an EXCLUSIVE
            // right boundary) and counted it twice per chunk: CreateScan(Exclusive) went wrong past one chunk
            // (2026-10-05). No barrier is needed in place of the Broadcast's: each scan call's own barriers order its
            // shared-memory writes after the previous call's reads.
            var scanOperation = default(TScanOperation);
            return scanOperation.Apply(leftBoundary, rightBoundary);
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
