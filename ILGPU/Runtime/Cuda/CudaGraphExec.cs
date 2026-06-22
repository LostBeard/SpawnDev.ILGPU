// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: CudaGraphExec.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;

namespace ILGPU.Runtime.Cuda
{
    /// <summary>
    /// Represents an instantiated, launchable CUDA graph. Launching it replays the entire
    /// captured kernel sequence with one driver call (<c>cuGraphLaunch</c>), eliminating
    /// per-kernel host dispatch overhead - the win for short, fixed-shape sequences such
    /// as LLM decode (M=1), where the same hundreds of kernels run every token.
    /// </summary>
    /// <remarks>
    /// The device pointers and launch configuration baked in at capture time are fixed.
    /// To vary per-replay inputs (e.g. a new token id or KV position), have the captured
    /// kernels read those values from a device buffer whose address is stable and update
    /// that buffer's contents between launches - do NOT reallocate the buffer.
    /// </remarks>
    public sealed class CudaGraphExec : AcceleratorObject
    {
        #region Instance

        private IntPtr execPtr;

        internal CudaGraphExec(CudaAccelerator accelerator, IntPtr execPtr)
            : base(accelerator)
        {
            this.execPtr = execPtr;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the underlying native <c>CUgraphExec</c> handle.
        /// </summary>
        public IntPtr ExecPtr => execPtr;

        #endregion

        #region Methods

        /// <summary>
        /// Launches (replays) this executable graph on the given stream. Asynchronous -
        /// the launch returns immediately; synchronize the stream when you need the
        /// results.
        /// </summary>
        /// <param name="stream">The stream to replay on.</param>
        public void Launch(CudaStream stream)
        {
            if (execPtr == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(CudaGraphExec));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            using var binding = stream.BindScoped();
            CudaGraphNativeMethods.Launch(execPtr, stream.StreamPtr);
        }

        /// <summary>
        /// Uploads this executable graph to the device ahead of the first launch
        /// (<c>cuGraphUpload</c>). Optional - the first <see cref="Launch(CudaStream)"/>
        /// uploads implicitly; calling this beforehand moves that one-time cost out of the
        /// timed path.
        /// </summary>
        /// <param name="stream">The stream to upload on.</param>
        public void Upload(CudaStream stream)
        {
            if (execPtr == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(CudaGraphExec));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            using var binding = stream.BindScoped();
            CudaGraphNativeMethods.Upload(execPtr, stream.StreamPtr);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes this executable graph. The associated accelerator is bound and active.
        /// </summary>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (execPtr == IntPtr.Zero)
                return;

            CudaException.VerifyDisposed(
                disposing,
                CudaGraphNativeMethods.DestroyExec(execPtr));
            execPtr = IntPtr.Zero;
        }

        #endregion
    }
}
