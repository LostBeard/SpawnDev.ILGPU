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
        /// Submits all queued operations to the device WITHOUT waiting for them to finish.
        /// </summary>
        /// <remarks>
        /// The "start the work, do not wait" counterpart to <see cref="Synchronize"/>. Desktop
        /// streams (CPU, CUDA, OpenCL) submit work as it is enqueued, so the default is a no-op.
        /// Browser streams (WebGPU, WebGL, Wasm) batch dispatches and override this to submit the
        /// pending batch. All current streams submit synchronously.
        /// </remarks>
        public virtual void Flush() { }

        /// <summary>
        /// Completion step for a SYNCHRONOUS host-&gt;device copy (upload) issued via
        /// <c>CopyFromCPUUnsafeAsync</c>. The default WAITS via <see cref="Synchronize"/>, which is
        /// correct on desktop (CPU/CUDA/OpenCL) where the upload DMA is still in flight after the
        /// "unsafe async" copy returns, so the host source must not be reused until it drains.
        /// </summary>
        /// <remarks>
        /// Browser streams (WebGPU/WebGL/Wasm) override this to a NO-OP: their host-&gt;device upload
        /// (<c>queue.writeBuffer</c> / SharedArrayBuffer memcpy / backing-array copy) consumes the
        /// host source SYNCHRONOUSLY at copy time, so there is nothing left to wait for. This is the
        /// fire-and-forget upload half of the sync/async contract - distinct from the public
        /// <see cref="Synchronize"/> (a wait-for-completion that is async-only on browser and THROWS).
        /// A host-&gt;device upload is honest synchronously on every local backend, so it stays sync;
        /// a remote/P2P stream whose upload is an async network send overrides this to throw.
        /// </remarks>
        protected internal virtual void EnsureHostCopyConsumed() => Synchronize();

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
