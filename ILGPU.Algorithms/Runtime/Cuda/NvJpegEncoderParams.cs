// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2021-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: NvJpegEncoderParams.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime.Cuda.API;
using ILGPU.Util;
using System;

namespace ILGPU.Runtime.Cuda
{
    /// <summary>
    /// Represents NvJpeg encoder parameters.
    /// </summary>
    public sealed partial class NvJpegEncoderParams : DisposeBase
    {
        /// <summary>
        /// Constructs a new instance to wrap NvJpeg encoder parameters.
        /// </summary>
        public NvJpegEncoderParams(NvJpegAPI api, IntPtr paramsHandle)
        {
            API = api;
            ParamsHandle = paramsHandle;
        }

        /// <summary>
        /// The underlying API wrapper.
        /// </summary>
        public NvJpegAPI API { get; }

        /// <summary>
        /// The native handle.
        /// </summary>
        public IntPtr ParamsHandle { get; private set; }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                NvJpegException.ThrowIfFailed(
                    API.EncoderParamsDestroy(ParamsHandle));
                ParamsHandle = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }
    }
}
