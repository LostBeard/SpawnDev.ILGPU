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
        /// <summary>
        /// Rounds of <see cref="Thread.SpinWait(int)"/> (8 iterations each, a few
        /// microseconds in all) a waiting warp spins before it yields its processor.
        /// </summary>
        private const int SpinsBeforeYield = 200;

        /// <summary>
        /// True when more threads than processors take part in the launch (set per launch):
        /// a waiting warp must then give its processor up at once, as spinning would burn
        /// the time slice of a thread that is needed to complete the barrier.
        /// </summary>
        public bool Oversubscribed { get; set; }

        private readonly object sync = new();
        private readonly int initialParticipants;
        private volatile int phase;
        private volatile bool aborted;

        // The warps that take part (high 32 bits) and the ones that have arrived (low 32
        // bits) in one word: arriving and leaving are compare-and-swap loops, no lock
        private long state;

        /// <summary>
        /// Constructs a barrier for the given number of warps.
        /// </summary>
        public VelocityGroupBarrier(int participants)
        {
            initialParticipants = participants;
            state = (long)participants << 32;
        }

        /// <summary>
        /// Prepares the barrier for the next group (all warps arrive again).
        /// </summary>
        public void Reset()
        {
            lock (sync)
            {
                aborted = false;
                Volatile.Write(ref state, (long)initialParticipants << 32);
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
            // No warp can complete this round before this one has arrived, so the phase
            // read here is the phase of this round
            int myPhase = phase;
            while (true)
            {
                if (aborted)
                    throw new OperationCanceledException();
                long s = Volatile.Read(ref state);
                int parts = (int)(s >> 32), arr = (int)s;
                if (arr + 1 >= parts)
                {
                    // The last warp: re-arm, then release the others by advancing the phase
                    if (Interlocked.CompareExchange(ref state, (long)parts << 32, s) == s)
                    {
                        phase = myPhase + 1;
                        return;
                    }
                }
                else if (Interlocked.CompareExchange(ref state, s + 1, s) == s)
                {
                    break;
                }
            }

            // Spin first while every warp of the launch has a processor (they arrive
            // within microseconds), then give the processor up; with more warps than
            // processors a spinning warp would only burn the time slice of the one that
            // is needed, so it yields at once and never sleeps for a millisecond
            if (Oversubscribed)
            {
                var spinner = new SpinWait();
                while (phase == myPhase)
                    spinner.SpinOnce(-1);
            }
            else
            {
                int spins = 0;
                while (phase == myPhase)
                {
                    if (++spins < SpinsBeforeYield)
                    {
                        Thread.SpinWait(8);
                    }
                    else
                    {
                        Thread.Yield();
                        spins = 0;
                    }
                }
            }
            if (aborted)
                throw new OperationCanceledException();
        }

        /// <summary>
        /// The warp has finished the kernel: it is not waited for any more. The last
        /// warp to leave re-arms the barrier for the next group.
        /// </summary>
        public void Leave()
        {
            while (true)
            {
                long s = Volatile.Read(ref state);
                int parts = (int)(s >> 32), arr = (int)s;
                int newParts = parts - 1;
                if (newParts <= 0)
                {
                    // Everybody is done: ready for the next group
                    if (Interlocked.CompareExchange(ref state, (long)initialParticipants << 32, s) == s)
                        return;
                }
                else if (arr >= newParts)
                {
                    // The warps that were waiting for this one are complete now
                    if (Interlocked.CompareExchange(ref state, (long)newParts << 32, s) == s)
                    {
                        phase++;
                        return;
                    }
                }
                else if (Interlocked.CompareExchange(ref state, ((long)newParts << 32) | (uint)arr, s) == s)
                {
                    return;
                }
            }
        }
    }
}
