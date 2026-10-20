// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityGroupBarrier.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Threading;

namespace ILGPU.Runtime.Velocity
{
    /// <summary>
    /// A reusable barrier for the warps of one thread group (one thread per warp).
    /// Unlike <see cref="Barrier"/> it follows the GPU rule: a warp whose kernel has
    /// returned <see cref="Leave"/>s the barrier and is no longer waited for, so a
    /// kernel in which whole warps return early (<c>if (index &gt;= n) return;</c>)
    /// does not deadlock the warps that still synchronize. The group can be aborted
    /// when one warp throws, which releases everybody.
    /// </summary>
    sealed class VelocityGroupBarrier
    {
        private readonly object sync = new();
        private readonly int initialParticipants;
        private int participants;
        private int arrived;
        private volatile int phase;
        private volatile bool aborted;

        /// <summary>
        /// Constructs a barrier for the given number of warps.
        /// </summary>
        public VelocityGroupBarrier(int participants)
        {
            initialParticipants = this.participants = participants;
        }

        /// <summary>
        /// Prepares the barrier for the next group (all warps arrive again).
        /// </summary>
        public void Reset()
        {
            lock (sync)
            {
                participants = initialParticipants;
                arrived = 0;
                aborted = false;
            }
        }

        /// <summary>
        /// Releases everybody waiting and makes every later call throw: one warp of the
        /// group failed, the others must not wait for it.
        /// </summary>
        public void Abort()
        {
            aborted = true;
            lock (sync)
                phase++;
        }

        /// <summary>
        /// Waits until all warps of the group that have not returned arrived.
        /// </summary>
        public void SignalAndWait()
        {
            int myPhase;
            lock (sync)
            {
                if (aborted)
                    throw new OperationCanceledException();
                myPhase = phase;
                if (++arrived >= participants)
                {
                    // The last warp releases the others
                    arrived = 0;
                    phase = myPhase + 1;
                    return;
                }
            }

            // Spin first (the warps usually arrive within microseconds), then give the
            // processor to a warp that has not arrived: more warps than processors must
            // still make progress, so this never sleeps for a millisecond
            var spinner = new SpinWait();
            while (phase == myPhase)
                spinner.SpinOnce(-1);
            if (aborted)
                throw new OperationCanceledException();
        }

        /// <summary>
        /// The warp has finished the kernel: it is not waited for any more. The last
        /// warp to leave re-arms the barrier for the next group.
        /// </summary>
        public void Leave()
        {
            lock (sync)
            {
                if (--participants <= 0)
                {
                    // Everybody is done: ready for the next group
                    participants = initialParticipants;
                    arrived = 0;
                }
                else if (arrived >= participants)
                {
                    // The warps that were waiting for this one are complete now
                    arrived = 0;
                    phase++;
                }
            }
        }
    }
}
