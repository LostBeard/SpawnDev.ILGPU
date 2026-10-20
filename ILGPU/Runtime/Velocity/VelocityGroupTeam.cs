// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityGroupTeam.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;

namespace ILGPU.Runtime.Velocity
{
    /// <summary>
    /// The warps that execute one thread group of more than a warp together, one
    /// thread per warp: they share the group's shared memory and meet at barriers.
    /// A team processes one group after the other (the next group starts when all
    /// warps are done with the current one).
    /// </summary>
    sealed class VelocityGroupTeam : DisposeBase
    {
        /// <summary>
        /// The distance between the slots of two warps in <see cref="slots"/> (in
        /// ints): each warp writes its own cache line.
        /// </summary>
        private const int SlotPadding = 16;

        private readonly int[] slots;

        /// <summary>
        /// Constructs a team of the given number of warps.
        /// </summary>
        public VelocityGroupTeam(VelocityAccelerator accelerator, int numWarps)
        {
            NumWarps = numWarps;
            SharedPool = new VelocityMemoryBufferPool(
                accelerator,
                accelerator.MaxSharedMemoryPerGroup);
            KernelBarrier = new VelocityGroupBarrier(numWarps);
            GroupEnd = new VelocityGroupBarrier(numWarps);
            slots = new int[numWarps * SlotPadding];
        }

        /// <summary>
        /// The number of warps of the team.
        /// </summary>
        public int NumWarps { get; }

        /// <summary>
        /// The shared memory of the group: every warp addresses the same bytes (the
        /// warps allocate in the same order, so their offsets agree).
        /// </summary>
        public VelocityMemoryBufferPool SharedPool { get; }

        /// <summary>
        /// The barrier the kernel code meets at.
        /// </summary>
        public VelocityGroupBarrier KernelBarrier { get; }

        /// <summary>
        /// The barrier all warps meet at when they are done with a group.
        /// </summary>
        public VelocityGroupBarrier GroupEnd { get; }

        /// <summary>
        /// Sums a value of every warp of the group: each warp contributes its own and
        /// everybody gets the total (the group's predicate barriers and popcounts).
        /// Warps that have returned contribute zero.
        /// </summary>
        public int GroupSum(int warpIndex, int value)
        {
            slots[warpIndex * SlotPadding] = value;
            KernelBarrier.SignalAndWait();
            int sum = 0;
            for (int i = 0; i < NumWarps; ++i)
                sum += slots[i * SlotPadding];

            // Nobody may write its next value before everybody has read this round
            KernelBarrier.SignalAndWait();
            return sum;
        }

        /// <summary>
        /// A warp has finished the kernel: it contributes nothing to sums and is not
        /// waited for at barriers any more.
        /// </summary>
        public void WarpFinished(int warpIndex)
        {
            slots[warpIndex * SlotPadding] = 0;
            KernelBarrier.Leave();
        }

        /// <summary>
        /// Aborts all barriers: a warp failed.
        /// </summary>
        public void Abort()
        {
            KernelBarrier.Abort();
            GroupEnd.Abort();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SharedPool.Dispose();
            base.Dispose(disposing);
        }
    }
}
