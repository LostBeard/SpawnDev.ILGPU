// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: CudaStreamCaptureMode.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

namespace ILGPU.Runtime.Cuda
{
    /// <summary>
    /// Determines the interaction of a stream-capture sequence with concurrently running
    /// captures on other threads. Mirrors the CUDA driver enum
    /// <c>CUstreamCaptureMode</c>.
    /// </summary>
    public enum CudaStreamCaptureMode
    {
        /// <summary>
        /// Captures globally - potentially unsafe operations on ANY thread are disallowed
        /// for the duration of the capture (<c>CU_STREAM_CAPTURE_MODE_GLOBAL</c>).
        /// </summary>
        Global = 0,

        /// <summary>
        /// Restricts the safety check to the capturing thread only
        /// (<c>CU_STREAM_CAPTURE_MODE_THREAD_LOCAL</c>). The recommended default for a
        /// self-contained per-step decode capture.
        /// </summary>
        ThreadLocal = 1,

        /// <summary>
        /// Disables the safety checks entirely - the caller is fully responsible for
        /// avoiding disallowed operations during capture
        /// (<c>CU_STREAM_CAPTURE_MODE_RELAXED</c>).
        /// </summary>
        Relaxed = 2,
    }

    /// <summary>
    /// Reports whether a stream is currently capturing. Mirrors the CUDA driver enum
    /// <c>CUstreamCaptureStatus</c>.
    /// </summary>
    public enum CudaStreamCaptureStatus
    {
        /// <summary>
        /// The stream is not capturing (<c>CU_STREAM_CAPTURE_STATUS_NONE</c>).
        /// </summary>
        None = 0,

        /// <summary>
        /// The stream is actively capturing
        /// (<c>CU_STREAM_CAPTURE_STATUS_ACTIVE</c>).
        /// </summary>
        Active = 1,

        /// <summary>
        /// The stream was capturing but an error invalidated the capture sequence
        /// (<c>CU_STREAM_CAPTURE_STATUS_INVALIDATED</c>).
        /// </summary>
        Invalidated = 2,
    }
}
