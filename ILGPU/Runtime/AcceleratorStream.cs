// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: AcceleratorStream.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Resources;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Represents an abstract kernel stream for asynchronous processing.
    /// </summary>
    /// <remarks>Members of this class are not thread safe.</remarks>
    [SuppressMessage(
        "Microsoft.Naming",
        "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public abstract class AcceleratorStream : AcceleratorObject
    {
        #region Instance

        private readonly Action synchronizeAction;

        /// <summary>
        /// Constructs a new accelerator stream.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        protected AcceleratorStream(Accelerator accelerator)
            : base(accelerator)
        {
            synchronizeAction = () => Synchronize();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Synchronizes all queued operations.
        /// </summary>
        public abstract void Synchronize();

        /// <summary>
        /// Synchronizes all queued operations asynchronously.
        /// </summary>
        /// <returns>A task object to wait for.</returns>
        /// <remarks>
        /// The default implementation offloads the blocking
        /// <see cref="Synchronize"/> call to a thread-pool thread, which is correct
        /// for backends whose <see cref="Synchronize"/> genuinely blocks until the
        /// queue drains (CUDA, OpenCL, CPU). Single-threaded browser backends
        /// (Wasm, WebGPU, WebGL) cannot block-wait and their synchronous
        /// <see cref="Synchronize"/> is a non-blocking flush / no-op; those streams
        /// MUST override this to await their real async drain. Algorithm code that
        /// needs a host-visible result after an unawaited dispatch must await this
        /// (or <see cref="Accelerator.SynchronizeAsync"/>) rather than calling the
        /// synchronous <see cref="Synchronize"/>.
        /// </remarks>
        public virtual Task SynchronizeAsync() => Task.Run(synchronizeAction);

        /// <summary>
        /// Makes the associated accelerator the current one for this thread and
        /// returns a <see cref="ScopedAcceleratorBinding"/> object that allows
        /// to easily recover the old binding.
        /// </summary>
        /// <returns>A scoped binding object.</returns>
        public ScopedAcceleratorBinding BindScoped() => Accelerator.BindScoped();

        /// <summary>
        /// Adds a profiling marker into the stream.
        /// </summary>
        /// <returns>The profiling marker.</returns>
        public ProfilingMarker AddProfilingMarker() =>
            Accelerator.Context.Properties.EnableProfiling
            ? AddProfilingMarkerInternal()
            : throw new NotSupportedException(
                RuntimeErrorMessages.NotSupportedProfilingMarker);

        /// <summary>
        /// Adds a profiling marker into the stream.
        /// </summary>
        /// <returns>The profiling marker.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected abstract ProfilingMarker AddProfilingMarkerInternal();

        #endregion
    }
}
