// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: CudaStream.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using static ILGPU.Runtime.Cuda.CudaAPI;

namespace ILGPU.Runtime.Cuda
{
    /// <summary>
    /// Represents a Cuda stream.
    /// </summary>
    [SuppressMessage(
        "Microsoft.Naming",
        "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public sealed class CudaStream : AcceleratorStream
    {
        #region Instance

        private IntPtr streamPtr;
        private readonly bool responsibleForHandle;

        /// <summary>
        /// Constructs a new Cuda stream from the given native pointer.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        /// <param name="ptr">The native stream pointer.</param>
        /// <param name="responsible">
        /// Whether ILGPU is responsible of disposing this stream.
        /// </param>
        internal CudaStream(Accelerator accelerator, IntPtr ptr, bool responsible)
            : base(accelerator)
        {
            streamPtr = ptr;
            responsibleForHandle = responsible;
        }

        /// <summary>
        /// Constructs a new Cuda stream with given <see cref="StreamFlags"/>.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        /// <param name="flag">
        /// Stream flag to use. Allows blocking and non-blocking streams.
        /// </param>
        internal CudaStream(Accelerator accelerator, StreamFlags flag)
            : base(accelerator)
        {
            CudaException.ThrowIfFailed(
                CurrentAPI.CreateStream(
                    out streamPtr,
                    flag));
            responsibleForHandle = true;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the underlying native Cuda stream.
        /// </summary>
        public IntPtr StreamPtr => streamPtr;

        #endregion

        #region Methods

        /// <summary cref="AcceleratorStream.Synchronize"/>
        public override void Synchronize()
        {
            var binding = Accelerator.BindScoped();

            CudaException.ThrowIfFailed(
                CurrentAPI.SynchronizeStream(streamPtr));

            binding.Recover();
        }

        /// <inheritdoc/>
        protected override ProfilingMarker AddProfilingMarkerInternal()
        {
            using var binding = Accelerator.BindScoped();
            var profilingMarker = new CudaProfilingMarker(Accelerator);

            CudaException.ThrowIfFailed(
                CurrentAPI.RecordEvent(profilingMarker.EventPtr, StreamPtr));
            return profilingMarker;
        }

        #endregion

        #region Graph Capture

        /// <summary>
        /// Returns true when the CUDA driver on this machine exposes the graph API.
        /// </summary>
        public static bool SupportsGraphCapture => CudaGraphNativeMethods.IsSupported;

        /// <summary>
        /// Begins capturing every subsequent operation issued on this stream into a CUDA
        /// graph instead of executing it. End the sequence with <see cref="EndCapture"/>.
        /// </summary>
        /// <param name="mode">The capture safety mode (default: thread-local).</param>
        /// <remarks>
        /// The stream MUST NOT be the accelerator's default stream - the legacy NULL
        /// stream cannot be captured. Create a dedicated stream via
        /// <c>accelerator.CreateStream()</c> and launch the captured work on it using the
        /// explicit-stream kernel launchers (<c>LoadAutoGroupedKernel</c> /
        /// <c>LoadKernel</c>, NOT the <c>*StreamKernel</c> variants, which target the
        /// default stream). During capture, do NOT synchronize, read back to the host, or
        /// allocate device memory - those operations are illegal mid-capture.
        /// </remarks>
        public void BeginCapture(
            CudaStreamCaptureMode mode = CudaStreamCaptureMode.ThreadLocal)
        {
            if (StreamPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Cannot capture the default (NULL) CUDA stream. Capture requires a " +
                    "dedicated stream created via accelerator.CreateStream().");
            }
            using var binding = Accelerator.BindScoped();
            CudaGraphNativeMethods.BeginCapture(StreamPtr, mode);
        }

        /// <summary>
        /// Ends the capture started by <see cref="BeginCapture(CudaStreamCaptureMode)"/>
        /// and returns the recorded graph. Instantiate it with
        /// <see cref="CudaGraph.Instantiate"/> to get a replayable
        /// <see cref="CudaGraphExec"/>.
        /// </summary>
        /// <returns>The captured graph.</returns>
        public CudaGraph EndCapture()
        {
            using var binding = Accelerator.BindScoped();
            var graph = CudaGraphNativeMethods.EndCapture(StreamPtr);
            return new CudaGraph((CudaAccelerator)Accelerator, graph);
        }

        /// <summary>
        /// Returns the current capture status of this stream.
        /// </summary>
        public CudaStreamCaptureStatus CaptureStatus
        {
            get
            {
                using var binding = Accelerator.BindScoped();
                return CudaGraphNativeMethods.GetCaptureStatus(StreamPtr);
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes this Cuda stream.
        /// </summary>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (!responsibleForHandle || streamPtr == IntPtr.Zero)
                return;

            CudaException.VerifyDisposed(
                disposing,
                CurrentAPI.DestroyStream(streamPtr));
            streamPtr = IntPtr.Zero;
        }

        #endregion
    }
}
