// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                    Copyright (c) 2024-2026 SpawnDev / LostBeard
//
// File: WasmWarpExtensions.cs
//
// Wasm-specific warp-level scan and reduce implementations.
// Uses SharedMemory fallbacks since Wasm has no warp shuffle primitives.
// Mirrors WebGPUWarpExtensions.cs.
// ---------------------------------------------------------------------------------------

using ILGPU;
using ILGPU.Algorithms.ScanReduceOperations;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Wasm.Algorithms
{
    /// <summary>
    /// Custom Wasm-specific warp-level implementations.
    /// Implemented using shared memory fallbacks — no warp shuffle required.
    /// </summary>
    static class WasmWarpExtensions
    {
        #region Reduce

        /// <summary cref="global::ILGPU.Algorithms.WarpExtensions.Reduce{T, TReduction}(T)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Reduce<T, TReduction>(T value)
            where T : unmanaged
            where TReduction : struct, IScanReduceOperation<T> =>
            AllReduce<T, TReduction>(value);

        /// <summary cref="global::ILGPU.Algorithms.WarpExtensions.AllReduce{T, TReduction}(T)"/>
        public static T AllReduce<T, TReduction>(T value)
            where T : unmanaged
            where TReduction : struct, IScanReduceOperation<T>
        {
            // Warp-level reduce using shared memory.
            // Each thread in the warp writes to shared memory, then lane 0 reduces.
            var sharedMemory = SharedMemory.Allocate<T>(1024);
            int warpOffset = Warp.WarpIdx * Warp.WarpSize;
            sharedMemory[warpOffset + Warp.LaneIdx] = value;
            Group.Barrier();

            if (Warp.IsFirstLane)
            {
                TReduction reduction = default;
                T result = sharedMemory[warpOffset];
                int warpEnd = warpOffset + Warp.WarpSize;
                for (int i = warpOffset + 1; i < warpEnd; ++i)
                    result = reduction.Apply(result, sharedMemory[i]);
                sharedMemory[warpOffset] = result;
            }
            Group.Barrier();

            return sharedMemory[warpOffset];
        }

        #endregion

        #region Scan

        /// <summary cref="global::ILGPU.Algorithms.WarpExtensions.ExclusiveScan{T, TScan}(T)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ExclusiveScan<T, TScanOperation>(T value)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var sharedMemory = SharedMemory.Allocate<T>(1024);
            int warpOffset = Warp.WarpIdx * Warp.WarpSize;
            sharedMemory[warpOffset + Warp.LaneIdx] = value;
            Group.Barrier();

            // First lane performs sequential inclusive scan within the warp
            if (Warp.IsFirstLane)
            {
                TScanOperation scanOp = default;
                int warpEnd = warpOffset + Warp.WarpSize;
                for (int i = warpOffset + 1; i < warpEnd; ++i)
                {
                    sharedMemory[i] = scanOp.Apply(
                        sharedMemory[i - 1],
                        sharedMemory[i]);
                }
            }
            Group.Barrier();

            // Convert inclusive to exclusive: shift right, first lane gets identity
            return Warp.IsFirstLane
                ? default(TScanOperation).Identity
                : sharedMemory[warpOffset + Warp.LaneIdx - 1];
        }

        /// <summary cref="global::ILGPU.Algorithms.WarpExtensions.InclusiveScan{T, TScan}(T)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T InclusiveScan<T, TScanOperation>(T value)
            where T : unmanaged
            where TScanOperation : struct, IScanReduceOperation<T>
        {
            var sharedMemory = SharedMemory.Allocate<T>(1024);
            int warpOffset = Warp.WarpIdx * Warp.WarpSize;
            sharedMemory[warpOffset + Warp.LaneIdx] = value;
            Group.Barrier();

            // First lane performs sequential inclusive scan within the warp
            if (Warp.IsFirstLane)
            {
                TScanOperation scanOp = default;
                int warpEnd = warpOffset + Warp.WarpSize;
                for (int i = warpOffset + 1; i < warpEnd; ++i)
                {
                    sharedMemory[i] = scanOp.Apply(
                        sharedMemory[i - 1],
                        sharedMemory[i]);
                }
            }
            Group.Barrier();

            return sharedMemory[warpOffset + Warp.LaneIdx];
        }

        #endregion
    }
}
