// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: CudaKernel.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.PTX;
using System;
using System.Diagnostics;
using System.Reflection;
using static ILGPU.Runtime.Cuda.CudaAPI;

namespace ILGPU.Runtime.Cuda
{
    /// <summary>
    /// Represents a Cuda kernel that can be directly launched on a GPU.
    /// </summary>
    public sealed class CudaKernel : Kernel
    {
        #region Instance

        /// <summary>
        /// Holds the pointer to the native Cuda module in memory.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private IntPtr modulePtr;

        /// <summary>
        /// Holds the pointer to the native Cuda function in memory.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private IntPtr functionPtr;

        /// <summary>
        /// Loads a compiled kernel into the given Cuda context as kernel program.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        /// <param name="kernel">The source kernel.</param>
        /// <param name="launcher">The launcher method for the given kernel.</param>
        internal CudaKernel(
            CudaAccelerator accelerator,
            PTXCompiledKernel kernel,
            MethodInfo? launcher)
            : base(accelerator, kernel, launcher)
        {
            var kernelLoaded = CurrentAPI.LoadModule(
                out modulePtr,
                kernel.PTXAssembly,
                CudaAccelerator.DefaultMaxRegistersPerThread,
                out string? errorLog);
            if (kernelLoaded != CudaError.CUDA_SUCCESS)
            {
                Trace.WriteLine("PTX Kernel loading failed:");
                if (string.IsNullOrWhiteSpace(errorLog))
                    Trace.WriteLine(">> No error information available");
                else
                    Trace.WriteLine(errorLog);

                // Put the reason IN the exception (a Trace listener is rarely attached).
                var ptxHeader = new System.Text.StringBuilder();
                foreach (var line in kernel.PTXAssembly.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.StartsWith(".version", StringComparison.Ordinal)
                        || t.StartsWith(".target", StringComparison.Ordinal)
                        || t.StartsWith(".address_size", StringComparison.Ordinal))
                    {
                        if (ptxHeader.Length > 0) ptxHeader.Append(' ');
                        ptxHeader.Append(t);
                    }
                    if (ptxHeader.Length > 200) break;
                }
                throw new CudaException(
                    kernelLoaded, kernel.Name, errorLog, ptxHeader.ToString());
            }
            else if (CudaAccelerator.VerboseModuleLoad
                && !string.IsNullOrWhiteSpace(errorLog))
            {
                // Surface ptxas info log when verbose mode is on. Used to diagnose
                // register pressure / spilling decisions by ptxas.
                Trace.WriteLine($"[ptxas] {kernel.Name}: {errorLog}");
            }
            CudaException.ThrowIfFailed(kernelLoaded);

            CudaException.ThrowIfFailed(
                CurrentAPI.GetModuleFunction(
                    out functionPtr,
                    modulePtr,
                    kernel.Name));

            if (CudaAccelerator.VerboseModuleLoad)
            {
                Trace.WriteLine($"[cuFuncAttr] {kernel.Name}: {DumpFunctionAttributes(functionPtr)}");
            }
        }

        [System.Runtime.InteropServices.DllImport("nvcuda", EntryPoint = "cuFuncGetAttribute")]
        private static extern int cuFuncGetAttribute(
            out int pi, int attrib, IntPtr funcHandle);

        private static string DumpFunctionAttributes(IntPtr funcHandle)
        {
            string[] names = {
                "MAX_THREADS_PER_BLOCK",  // 0
                "SHARED_SIZE_BYTES",       // 1
                "CONST_SIZE_BYTES",        // 2
                "LOCAL_SIZE_BYTES",        // 3
                "NUM_REGS",                // 4
                "PTX_VERSION",             // 5
                "BINARY_VERSION",          // 6
            };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < names.Length; i++)
            {
                int err = cuFuncGetAttribute(out int val, i, funcHandle);
                if (err == 0) sb.Append($"{names[i]}={val} ");
                else sb.Append($"{names[i]}=ERR({err}) ");
            }
            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the Cuda module pointer.
        /// </summary>
        public IntPtr ModulePtr => modulePtr;

        /// <summary>
        /// Returns the Cuda function pointer.
        /// </summary>
        public IntPtr FunctionPtr => functionPtr;

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes this Cuda kernel.
        /// </summary>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            CudaException.VerifyDisposed(
                disposing,
                CurrentAPI.DestroyModule(modulePtr));
            functionPtr = IntPtr.Zero;
            modulePtr = IntPtr.Zero;
        }

        #endregion
    }
}
