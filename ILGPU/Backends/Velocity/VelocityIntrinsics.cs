// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityIntrinsics.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Intrinsics;
using ILGPU.IR.Values;
using System.Runtime.CompilerServices;

namespace ILGPU.Backends.Velocity
{
    /// <summary>
    /// Implements and initializes Velocity intrinsics.
    /// </summary>
    static partial class VelocityIntrinsics
    {
        #region Specializers

        /// <summary>
        /// Creates a new Velocity intrinsic.
        /// </summary>
        /// <param name="name">The name of the intrinsic.</param>
        /// <param name="mode">The implementation mode.</param>
        /// <returns>The created intrinsic.</returns>
        private static VelocityIntrinsic CreateIntrinsic(
            string name,
            IntrinsicImplementationMode mode) =>
            new(typeof(VelocityIntrinsics), name, mode);

        /// <summary>
        /// Registers all Velocity intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        public static void Register(IntrinsicImplementationManager manager)
        {
            RegisterBroadcasts(manager);
            RegisterWarpShuffles(manager);
            // no native packed dot product: the exact four-term form
            IntrinsicMath.RegisterPackedDotSoftware(
                manager,
                target => new VelocityIntrinsic(target, IntrinsicImplementationMode.Redirect));
        }

        #endregion

        #region Broadcasts

        /// <summary>
        /// Registers all broadcast intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        private static void RegisterBroadcasts(
            IntrinsicImplementationManager manager)
        {
            manager.RegisterBroadcast(
                BroadcastKind.GroupLevel,
                CreateIntrinsic(
                    nameof(GroupBroadcast),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterBroadcast(
                BroadcastKind.WarpLevel,
                CreateIntrinsic(
                    nameof(WarpBroadcast),
                    IntrinsicImplementationMode.Redirect));
        }

        /// <summary>
        /// Wraps a single warp-broadcast operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T WarpBroadcast<T>(T value, int laneIndex)
            where T : unmanaged =>
            Warp.Shuffle(value, laneIndex);

        /// <summary>
        /// A group-broadcast operation: the thread with the given index in the group
        /// hands its value to every thread of the group. A group may have several
        /// warps (a shuffle reaches only the lanes of one warp), so the value goes
        /// through shared memory: written by the source thread, read by everybody after
        /// a barrier, and the slot is not reused before everybody has read it.
        /// </summary>
        private static T GroupBroadcast<T>(T value, int groupIndex)
            where T : unmanaged
        {
            var slot = SharedMemory.Allocate<T>(1);
            if (Group.IdxX == groupIndex)
                slot[0] = value;
            Group.Barrier();
            var result = slot[0];
            Group.Barrier();
            return result;
        }

        #endregion
    }
}
