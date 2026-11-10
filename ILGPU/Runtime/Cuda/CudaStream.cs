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
using System.Threading;
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
            // While any stream captures, memory-pressure reclaim stands down: a cuMemFree (or cuMemAlloc)
            // during a Global-mode capture invalidates it and poisons the context (see MemoryPressure).
            if (!_capturing)
            {
                _capturing = true;
                Interlocked.Increment(ref Accelerator.ActiveStreamCaptures);
            }
        }

        private bool _capturing;

        private void CaptureEnded()
        {
            if (!_capturing)
                return;
            _capturing = false;
            Interlocked.Decrement(ref Accelerator.ActiveStreamCaptures);
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
            try
            {
                var graph = CudaGraphNativeMethods.EndCapture(StreamPtr);
                return new CudaGraph((CudaAccelerator)Accelerator, graph);
            }
            finally
            {
                // An invalidated capture throws here, but the stream has still left capture mode.
                CaptureEnded();
            }
        }

        private IntPtr _waitEvent;

        /// <summary>
        /// Makes the work issued on this stream from now on wait for all work issued on <paramref name="other"/> so far
        /// (an event recorded on <paramref name="other"/> plus <c>cuStreamWaitEvent</c> on this stream). The host does not
        /// block.
        /// </summary>
        /// <param name="other">The stream whose work issued so far must finish first.</param>
        /// <remarks>
        /// Inside a capture this forks and joins: while <paramref name="other"/> captures, this stream joins the same
        /// capture and its launches become a parallel branch of the graph; waiting back on that branch from the
        /// capturing stream before <see cref="EndCapture"/> joins it again.
        /// <code>
        /// main.BeginCapture();
        /// side.WaitFor(main);        // fork: side joins main's capture
        /// launchA(main, ...);        // branch 1
        /// launchB(side, ...);        // branch 2, may run concurrently with branch 1
        /// main.WaitFor(side);        // join (required: every forked stream must rejoin before EndCapture)
        /// using var graph = main.EndCapture();
        /// </code>
        /// Outside a capture it is an ordinary cross-stream dependency.
        /// </remarks>
        public void WaitFor(CudaStream other)
        {
            if (other is null)
                throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this))
                return;
            if (other.Accelerator != Accelerator)
                throw new ArgumentException("Both streams must belong to the same accelerator.", nameof(other));
            using var binding = Accelerator.BindScoped();
            if (_waitEvent == IntPtr.Zero)
            {
                CudaException.ThrowIfFailed(
                    CurrentAPI.CreateEvent(out _waitEvent, CudaEventFlags.CU_EVENT_DISABLE_TIMING));
            }
            // a wait uses the event's most recent record at the time of the call, so one event per waiting stream
            // serves every WaitFor on it
            CudaException.ThrowIfFailed(CurrentAPI.RecordEvent(_waitEvent, other.StreamPtr));
            CudaGraphNativeMethods.StreamWaitEvent(StreamPtr, _waitEvent);
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
            CaptureEnded(); // a stream destroyed mid-capture no longer captures
            if (_waitEvent != IntPtr.Zero)
            {
                CurrentAPI.DestroyEvent(_waitEvent);
                _waitEvent = IntPtr.Zero;
            }
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
