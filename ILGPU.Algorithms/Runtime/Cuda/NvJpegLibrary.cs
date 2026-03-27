// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2021-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: NvJpegLibrary.cs
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
    /// Represents an NvJpeg library.
    /// </summary>
    public sealed partial class NvJpegLibrary : DisposeBase
    {
        /// <summary>
        /// Constructs a new instance to wrap an NvJpeg library.
        /// </summary>
        public NvJpegLibrary(NvJpegAPI api, IntPtr libHandle)
        {
            API = api;
            LibHandle = libHandle;
        }

        /// <summary>
        /// The underlying API wrapper.
        /// </summary>
        public NvJpegAPI API { get; }

        /// <summary>
        /// The native handle.
        /// </summary>
        public IntPtr LibHandle { get; private set; }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                NvJpegException.ThrowIfFailed(
                    API.Destroy(LibHandle));
                LibHandle = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Creates a new NvJpeg state instance.
        /// </summary>
        public NvJpegState CreateState()
        {
            NvJpegException.ThrowIfFailed(
                API.JpegStateCreate(LibHandle, out IntPtr stateHandle));
            return new NvJpegState(API, stateHandle);
        }

        /// <inheritdoc cref="NvJpegAPI.GetImageInfo(
        ///     IntPtr,
        ///     ReadOnlySpan{byte},
        ///     out int,
        ///     out NvJpegChromaSubsampling,
        ///     out int[],
        ///     out int[])"/>
        public unsafe NvJpegStatus GetImageInfo(
            ReadOnlySpan<byte> imageBytes,
            out int numComponents,
            out NvJpegChromaSubsampling subsampling,
            out int[] widths,
            out int[] heights) =>
            API.GetImageInfo(
                LibHandle,
                imageBytes,
                out numComponents,
                out subsampling,
                out widths,
                out heights);

        /// <inheritdoc cref="NvJpegAPI.Decode(
        ///     IntPtr,
        ///     IntPtr,
        ///     ReadOnlySpan{byte},
        ///     NvJpegOutputFormat,
        ///     in NvJpegImage, CudaStream)"/>
        public unsafe NvJpegStatus Decode(
            NvJpegState state,
            ReadOnlySpan<byte> imageBytes,
            NvJpegOutputFormat outputFormat,
            in NvJpegImage destination,
            CudaStream stream) =>
            API.Decode(
                LibHandle,
                state.StateHandle,
                imageBytes,
                outputFormat,
                destination,
                stream);

        /// <inheritdoc cref="NvJpegAPI.Decode(
        ///     IntPtr,
        ///     IntPtr,
        ///     ReadOnlySpan{byte},
        ///     NvJpegOutputFormat,
        ///     in NvJpegImage)"/>
        public NvJpegStatus Decode(
            NvJpegState state,
            ReadOnlySpan<byte> imageBytes,
            NvJpegOutputFormat outputFormat,
            in NvJpegImage destination) =>
            API.Decode(
                LibHandle,
                state.StateHandle,
                imageBytes,
                outputFormat,
                destination);

        /// <summary>
        /// Creates a new NvJpeg encoder state instance.
        /// </summary>
        /// <param name="stream">The accelerator stream.</param>
        public NvJpegEncoderState CreateEncoderState(CudaStream stream)
        {
            NvJpegException.ThrowIfFailed(
                API.EncoderStateCreate(
                    LibHandle,
                    out IntPtr stateHandle,
                    stream.StreamPtr));
            return new NvJpegEncoderState(API, stateHandle);
        }

        /// <summary>
        /// Creates a new NvJpeg encoder state instance.
        /// </summary>
        public NvJpegEncoderState CreateEncoderState()
        {
            NvJpegException.ThrowIfFailed(
                API.EncoderStateCreate(
                    LibHandle,
                    out IntPtr stateHandle,
                    IntPtr.Zero));
            return new NvJpegEncoderState(API, stateHandle);
        }

        /// <summary>
        /// Creates new NvJpeg encoder parameters.
        /// </summary>
        /// <param name="stream">The accelerator stream.</param>
        public NvJpegEncoderParams CreateEncoderParams(CudaStream stream)
        {
            NvJpegException.ThrowIfFailed(
                API.EncoderParamsCreate(
                    LibHandle,
                    out IntPtr paramsHandle,
                    stream));
            return new NvJpegEncoderParams(API, paramsHandle);
        }

        /// <summary>
        /// Creates new NvJpeg encoder parameters.
        /// </summary>
        public NvJpegEncoderParams CreateEncoderParams()
        {
            NvJpegException.ThrowIfFailed(
                API.EncoderParamsCreate(
                    LibHandle,
                    out IntPtr paramsHandle));
            return new NvJpegEncoderParams(API, paramsHandle);
        }

        /// <inheritdoc cref="NvJpegAPI.EncoderParamsSetQuality(
        ///     IntPtr,
        ///     int,
        ///     CudaStream)"/>
        public NvJpegStatus EncoderParamsSetQuality(
            NvJpegEncoderParams encoderParams,
            int quality,
            CudaStream stream) =>
            API.EncoderParamsSetQuality(
                encoderParams.ParamsHandle,
                quality,
                stream);

        /// <inheritdoc cref="NvJpegAPI.EncoderParamsSetQuality(
        ///     IntPtr,
        ///     int)"/>
        public NvJpegStatus EncoderParamsSetQuality(
            NvJpegEncoderParams encoderParams,
            int quality) =>
            API.EncoderParamsSetQuality(
                encoderParams.ParamsHandle,
                quality);

        /// <inheritdoc cref="NvJpegAPI.EncoderParamsSetSamplingFactors(
        ///     IntPtr,
        ///     NvJpegChromaSubsampling,
        ///     CudaStream)"/>
        public NvJpegStatus EncoderParamsSetSamplingFactors(
            NvJpegEncoderParams encoderParams,
            NvJpegChromaSubsampling subsampling,
            CudaStream stream) =>
            API.EncoderParamsSetSamplingFactors(
                encoderParams.ParamsHandle,
                subsampling,
                stream);

        /// <inheritdoc cref="NvJpegAPI.EncoderParamsSetSamplingFactors(
        ///     IntPtr,
        ///     NvJpegChromaSubsampling)"/>
        public NvJpegStatus EncoderParamsSetSamplingFactors(
            NvJpegEncoderParams encoderParams,
            NvJpegChromaSubsampling subsampling) =>
            API.EncoderParamsSetSamplingFactors(
                encoderParams.ParamsHandle,
                subsampling);

        /// <inheritdoc cref="NvJpegAPI.EncodeYUV(
        ///     IntPtr,
        ///     IntPtr,
        ///     IntPtr,
        ///     in NvJpegImage,
        ///     NvJpegChromaSubsampling,
        ///     uint,
        ///     uint,
        ///     CudaStream)"/>
        public unsafe NvJpegStatus EncodeYUV(
            NvJpegEncoderState state,
            NvJpegEncoderParams encoderParams,
            in NvJpegImage source,
            NvJpegChromaSubsampling subsampling,
            uint width,
            uint height,
            CudaStream stream) =>
            API.EncodeYUV(
                LibHandle,
                state.StateHandle,
                encoderParams.ParamsHandle,
                source,
                subsampling,
                width,
                height,
                stream);

        /// <inheritdoc cref="NvJpegAPI.EncodeYUV(
        ///     IntPtr,
        ///     IntPtr,
        ///     IntPtr,
        ///     in NvJpegImage,
        ///     NvJpegChromaSubsampling,
        ///     uint,
        ///     uint)"/>
        public NvJpegStatus EncodeYUV(
            NvJpegEncoderState state,
            NvJpegEncoderParams encoderParams,
            in NvJpegImage source,
            NvJpegChromaSubsampling subsampling,
            uint width,
            uint height) =>
            API.EncodeYUV(
                LibHandle,
                state.StateHandle,
                encoderParams.ParamsHandle,
                source,
                subsampling,
                width,
                height);

        /// <inheritdoc cref="NvJpegAPI.EncodeImage(
        ///     IntPtr,
        ///     IntPtr,
        ///     IntPtr,
        ///     in NvJpegImage,
        ///     NvJpegInputFormat,
        ///     uint,
        ///     uint,
        ///     CudaStream)"/>
        public unsafe NvJpegStatus EncodeImage(
            NvJpegEncoderState state,
            NvJpegEncoderParams encoderParams,
            in NvJpegImage source,
            NvJpegInputFormat inputFormat,
            uint width,
            uint height,
            CudaStream stream) =>
            API.EncodeImage(
                LibHandle,
                state.StateHandle,
                encoderParams.ParamsHandle,
                source,
                inputFormat,
                width,
                height,
                stream);

        /// <inheritdoc cref="NvJpegAPI.EncodeImage(
        ///     IntPtr,
        ///     IntPtr,
        ///     IntPtr,
        ///     in NvJpegImage,
        ///     NvJpegInputFormat,
        ///     uint,
        ///     uint)"/>
        public NvJpegStatus EncodeImage(
            NvJpegEncoderState state,
            NvJpegEncoderParams encoderParams,
            in NvJpegImage source,
            NvJpegInputFormat inputFormat,
            uint width,
            uint height) =>
            API.EncodeImage(
                LibHandle,
                state.StateHandle,
                encoderParams.ParamsHandle,
                source,
                inputFormat,
                width,
                height);

        /// <inheritdoc cref="NvJpegAPI.EncodeRetrieveBitstream(
        ///     IntPtr,
        ///     IntPtr,
        ///     Span{byte},
        ///     out ulong,
        ///     CudaStream)"/>
        public NvJpegStatus EncodeRetrieveBitstream(
            NvJpegEncoderState state,
            Span<byte> data,
            out ulong length,
            CudaStream stream) =>
            API.EncodeRetrieveBitstream(
                LibHandle,
                state.StateHandle,
                data,
                out length,
                stream);

        /// <inheritdoc cref="NvJpegAPI.EncodeRetrieveBitstream(
        ///     IntPtr,
        ///     IntPtr,
        ///     Span{byte},
        ///     out ulong)"/>
        public NvJpegStatus EncodeRetrieveBitstream(
            NvJpegEncoderState state,
            Span<byte> data,
            out ulong length) =>
            API.EncodeRetrieveBitstream(
                LibHandle,
                state.StateHandle,
                data,
                out length);
    }
}
