// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: CudaGraph.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace ILGPU.Runtime.Cuda
{
    /// <summary>
    /// Direct bindings to the CUDA Driver API graph entry points.
    /// </summary>
    /// <remarks>
    /// ILGPU's generated <see cref="CudaAPI"/> table does not include the graph API, so
    /// these are bound by hand against the already-loaded CUDA driver - the same approach
    /// <c>NvvmAPI</c> uses for the NVVM library. They resolve the platform driver name via
    /// <see cref="CudaAPI.LibNameWindows"/> / <see cref="CudaAPI.LibNameLinux"/> so the
    /// graph API works on Windows and Linux without touching the existing DllImport
    /// resolver. If these are ever promoted into <c>CudaAPI.xml</c>, this file can be
    /// deleted in favor of the generated methods.
    /// </remarks>
    internal static unsafe class CudaGraphNativeMethods
    {
        // CUresult cuStreamBeginCapture_v2(CUstream, CUstreamCaptureMode)
        private static readonly delegate* unmanaged[Cdecl]<
            IntPtr, int, CudaError> cuStreamBeginCapture;

        // CUresult cuStreamEndCapture(CUstream, CUgraph*)
        private static readonly delegate* unmanaged[Cdecl]<
            IntPtr, IntPtr*, CudaError> cuStreamEndCapture;

        // CUresult cuStreamIsCapturing(CUstream, CUstreamCaptureStatus*)
        private static readonly delegate* unmanaged[Cdecl]<
            IntPtr, int*, CudaError> cuStreamIsCapturing;

        // CUresult cuGraphInstantiateWithFlags(CUgraphExec*, CUgraph, cuuint64_t)
        private static readonly delegate* unmanaged[Cdecl]<
            IntPtr*, IntPtr, ulong, CudaError> cuGraphInstantiateWithFlags;

        // CUresult cuGraphLaunch(CUgraphExec, CUstream)
        private static readonly delegate* unmanaged[Cdecl]<
            IntPtr, IntPtr, CudaError> cuGraphLaunch;

        // CUresult cuGraphUpload(CUgraphExec, CUstream)
        private static readonly delegate* unmanaged[Cdecl]<
            IntPtr, IntPtr, CudaError> cuGraphUpload;

        // CUresult cuGraphExecDestroy(CUgraphExec)
        private static readonly delegate* unmanaged[Cdecl]<
            IntPtr, CudaError> cuGraphExecDestroy;

        // CUresult cuGraphDestroy(CUgraph)
        private static readonly delegate* unmanaged[Cdecl]<
            IntPtr, CudaError> cuGraphDestroy;

        /// <summary>
        /// True when every graph entry point resolved against the loaded CUDA driver.
        /// </summary>
        public static bool IsSupported { get; }

        static CudaGraphNativeMethods()
        {
            if (!TryLoadDriver(out var driver))
                return;

            // Resolve every entry point; if any is missing (very old driver) the whole
            // feature is reported unsupported rather than half-working.
            if (TryGet(driver, "cuStreamBeginCapture_v2", out var p0) &&
                TryGet(driver, "cuStreamEndCapture", out var p1) &&
                TryGet(driver, "cuStreamIsCapturing", out var p2) &&
                TryGet(driver, "cuGraphInstantiateWithFlags", out var p3) &&
                TryGet(driver, "cuGraphLaunch", out var p4) &&
                TryGet(driver, "cuGraphUpload", out var p5) &&
                TryGet(driver, "cuGraphExecDestroy", out var p6) &&
                TryGet(driver, "cuGraphDestroy", out var p7))
            {
                cuStreamBeginCapture =
                    (delegate* unmanaged[Cdecl]<IntPtr, int, CudaError>)p0;
                cuStreamEndCapture =
                    (delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, CudaError>)p1;
                cuStreamIsCapturing =
                    (delegate* unmanaged[Cdecl]<IntPtr, int*, CudaError>)p2;
                cuGraphInstantiateWithFlags =
                    (delegate* unmanaged[Cdecl]<IntPtr*, IntPtr, ulong, CudaError>)p3;
                cuGraphLaunch =
                    (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, CudaError>)p4;
                cuGraphUpload =
                    (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, CudaError>)p5;
                cuGraphExecDestroy =
                    (delegate* unmanaged[Cdecl]<IntPtr, CudaError>)p6;
                cuGraphDestroy =
                    (delegate* unmanaged[Cdecl]<IntPtr, CudaError>)p7;
                IsSupported = true;
            }
        }

        private static bool TryLoadDriver(out IntPtr driver)
        {
            // The driver is already resident (cuInit ran during accelerator creation);
            // NativeLibrary.TryLoad returns the existing handle. Try the platform name
            // first, then common Linux soname variants.
            string[] candidates = OperatingSystem.IsWindows()
                ? new[] { CudaAPI.LibNameWindows }
                : new[]
                {
                    CudaAPI.LibNameLinux, "libcuda.so", "libcuda.so.1", "libcuda"
                };
            foreach (var name in candidates)
            {
                if (NativeLibrary.TryLoad(name, out driver))
                    return true;
            }
            driver = IntPtr.Zero;
            return false;
        }

        private static bool TryGet(IntPtr driver, string export, out IntPtr address) =>
            NativeLibrary.TryGetExport(driver, export, out address);

        private static void EnsureSupported()
        {
            if (!IsSupported)
            {
                throw new NotSupportedException(
                    "The CUDA driver on this machine does not expose the CUDA graph API " +
                    "(cuGraph*). A newer NVIDIA driver is required.");
            }
        }

        public static void BeginCapture(IntPtr stream, CudaStreamCaptureMode mode)
        {
            EnsureSupported();
            CudaException.ThrowIfFailed(cuStreamBeginCapture(stream, (int)mode));
        }

        public static IntPtr EndCapture(IntPtr stream)
        {
            EnsureSupported();
            IntPtr graph;
            CudaException.ThrowIfFailed(cuStreamEndCapture(stream, &graph));
            return graph;
        }

        public static CudaStreamCaptureStatus GetCaptureStatus(IntPtr stream)
        {
            EnsureSupported();
            int status;
            CudaException.ThrowIfFailed(cuStreamIsCapturing(stream, &status));
            return (CudaStreamCaptureStatus)status;
        }

        public static IntPtr Instantiate(IntPtr graph)
        {
            EnsureSupported();
            IntPtr exec;
            CudaException.ThrowIfFailed(
                cuGraphInstantiateWithFlags(&exec, graph, 0UL));
            return exec;
        }

        public static void Launch(IntPtr graphExec, IntPtr stream)
        {
            EnsureSupported();
            CudaException.ThrowIfFailed(cuGraphLaunch(graphExec, stream));
        }

        public static void Upload(IntPtr graphExec, IntPtr stream)
        {
            EnsureSupported();
            CudaException.ThrowIfFailed(cuGraphUpload(graphExec, stream));
        }

        public static CudaError DestroyExec(IntPtr graphExec) =>
            IsSupported ? cuGraphExecDestroy(graphExec) : CudaError.CUDA_SUCCESS;

        public static CudaError DestroyGraph(IntPtr graph) =>
            IsSupported ? cuGraphDestroy(graph) : CudaError.CUDA_SUCCESS;
    }

    /// <summary>
    /// Represents a captured CUDA graph (a recorded sequence of GPU operations).
    /// Instantiate it into a <see cref="CudaGraphExec"/> to replay the sequence with a
    /// single <c>cuGraphLaunch</c> instead of re-issuing every kernel launch on the host.
    /// </summary>
    /// <remarks>
    /// Obtain one via <see cref="CudaStream.EndCapture"/> after a
    /// <see cref="CudaStream.BeginCapture(CudaStreamCaptureMode)"/> /
    /// run-one-step / end-capture sequence. The captured device pointers and launch
    /// configuration are fixed at capture time, so the buffers a captured kernel reads
    /// and writes must stay allocated and at stable addresses for the lifetime of any
    /// derived <see cref="CudaGraphExec"/>.
    /// </remarks>
    public sealed class CudaGraph : AcceleratorObject
    {
        #region Instance

        private IntPtr graphPtr;

        internal CudaGraph(CudaAccelerator accelerator, IntPtr graphPtr)
            : base(accelerator)
        {
            this.graphPtr = graphPtr;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the underlying native <c>CUgraph</c> handle.
        /// </summary>
        public IntPtr GraphPtr => graphPtr;

        #endregion

        #region Methods

        /// <summary>
        /// Instantiates this graph into an executable graph that can be launched and
        /// re-launched. Allocate once, replay per decode step.
        /// </summary>
        /// <returns>The executable graph.</returns>
        public CudaGraphExec Instantiate()
        {
            if (graphPtr == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(CudaGraph));
            using var binding = Accelerator.BindScoped();
            var exec = CudaGraphNativeMethods.Instantiate(graphPtr);
            return new CudaGraphExec((CudaAccelerator)Accelerator, exec);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes this graph. The associated accelerator is bound and active.
        /// </summary>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (graphPtr == IntPtr.Zero)
                return;

            CudaException.VerifyDisposed(
                disposing,
                CudaGraphNativeMethods.DestroyGraph(graphPtr));
            graphPtr = IntPtr.Zero;
        }

        #endregion
    }
}
