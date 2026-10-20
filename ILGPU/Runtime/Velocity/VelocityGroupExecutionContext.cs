// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityGroupExecutionContext.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System.Threading;

namespace ILGPU.Runtime.Velocity
{
    /// <summary>
    /// Represents an execution context for a single thread group (or, for a group
    /// of more than a warp that has barriers, for one warp of its team).
    /// </summary>
    sealed class VelocityGroupExecutionContext : DisposeBase
    {
        private readonly VelocityAccelerator accelerator;
        private readonly int warpSize;

        // Own memory (a group that is at most one warp or has no barriers), created
        // when first needed
        private VelocityMemoryBufferPool? sharedMemoryPool;
        private VelocityMemoryBufferPool? localMemoryPool;

        // The team this warp belongs to (null: the context executes whole groups)
        private VelocityGroupTeam? team;
        private int warpIndex;

        // The shared memory offset in the team's shared memory: every warp allocates
        // in the same order and therefore ends up at the same addresses
        private int teamSharedOffset;

        /// <summary>
        /// Constructs a new execution context.
        /// </summary>
        /// <param name="accelerator">The parent velocity accelerator.</param>
        public VelocityGroupExecutionContext(VelocityAccelerator accelerator)
        {
            this.accelerator = accelerator;
            warpSize = accelerator.WarpSize;
        }

        /// <summary>
        /// Returns a view to dynamic shared memory (if any).
        /// </summary>
        public ArrayView<byte> DynamicSharedMemory { get; private set; }

        /// <summary>
        /// Makes this context one warp of a team (null: a context of its own).
        /// </summary>
        public void AttachTeam(VelocityGroupTeam? newTeam, int newWarpIndex)
        {
            team = newTeam;
            warpIndex = newWarpIndex;
        }

        /// <summary>
        /// Sets up the current thread grid information for the current thread group.
        /// </summary>
        public void SetupThreadGrid(int dynamicSharedMemoryLength)
        {
            // Reset everything
            sharedMemoryPool?.Reset();
            localMemoryPool?.Reset();
            teamSharedOffset = 0;

            // Allocate dynamic shared memory
            DynamicSharedMemory = default;
            if (dynamicSharedMemoryLength > 0)
            {
                DynamicSharedMemory =
                    GetSharedMemoryFromPool<byte>(dynamicSharedMemoryLength);
            }
        }

        /// <summary>
        /// Gets a chunk of shared memory of a certain type.
        /// </summary>
        /// <param name="length">The number of elements.</param>
        /// <typeparam name="T">The element type to allocate.</typeparam>
        /// <returns>A view pointing to the right chunk of shared memory.</returns>
        public ArrayView<T> GetSharedMemoryFromPool<T>(int length)
            where T : unmanaged
        {
            if (team != null)
                return team.SharedPool.AllocateAt<T>(ref teamSharedOffset, length);
            sharedMemoryPool ??= new VelocityMemoryBufferPool(
                accelerator,
                accelerator.MaxSharedMemoryPerGroup);
            return sharedMemoryPool.Allocate<T>(length);
        }

        /// <summary>
        /// Gets a chunk of local memory of a certain type.
        /// </summary>
        /// <param name="lengthInBytesPerThread">
        /// The number of bytes to allocate per thread.
        /// </param>
        /// <returns>A view pointing to the right chunk of local memory.</returns>
        public ArrayView<byte> GetLocalMemoryFromPool(int lengthInBytesPerThread)
        {
            localMemoryPool ??= new VelocityMemoryBufferPool(
                accelerator,
                accelerator.MaxSharedMemoryPerGroup);
            return localMemoryPool.Allocate<byte>(lengthInBytesPerThread * warpSize);
        }

        /// <summary>
        /// A group barrier: the warps of the group meet (a memory fence for a group
        /// of one warp).
        /// </summary>
        public void Barrier()
        {
            if (team == null)
                Interlocked.MemoryBarrier();
            else
                team.KernelBarrier.SignalAndWait();
        }

        /// <summary>
        /// Sums a value that every warp of the group contributes (the value itself
        /// for a group of one warp).
        /// </summary>
        public int GroupSum(int value) =>
            team == null ? value : team.GroupSum(warpIndex, value);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                sharedMemoryPool?.Dispose();
                localMemoryPool?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
