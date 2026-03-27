// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2021-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: NvJpegAPI.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;

namespace ILGPU.Runtime.Cuda.API
{
    /// <summary>
    /// An implementation of the nvJpeg API.
    /// </summary>
    public abstract partial class NvJpegAPI
    {
        #region Static

        /// <summary>
        /// Creates a new API wrapper.
        /// </summary>
        /// <param name="version">The nvJPEG version to use.</param>
        /// <returns>The created API wrapper.</returns>
        public static NvJpegAPI Create(NvJpegAPIVersion? version) =>
            version.HasValue
            ? CreateInternal(version.Value)
                ?? throw new DllNotFoundException(nameof(NvJpegAPI))
            : CreateLatest();

        /// <summary>
        /// Creates a new API wrapper using the latest installed version.
        /// </summary>
        /// <returns>The created API wrapper.</returns>
        private static NvJpegAPI CreateLatest()
        {
            Exception? firstException = null;
            var versions = Enum.GetValues<NvJpegAPIVersion>();
            for (var i = versions.Length - 1; i >= 0; i--)
            {
                var version = versions[i];
                var api = CreateInternal(version);
                if (api is null)
                    continue;

                try
                {
                    var status = api.GetProperty(
                        LibraryPropertyType.MAJOR_VERSION,
                        out _);
                    if (status == NvJpegStatus.NVJPEG_STATUS_SUCCESS)
                        return api;
                }
                catch (Exception ex) when (
                    ex is DllNotFoundException ||
                    ex is EntryPointNotFoundException)
                {
                    firstException ??= ex;
                }
            }

            throw firstException ?? new DllNotFoundException(nameof(NvJpegAPI));
        }

        #endregion

        #region Methods

        /// <summary>
        /// Retrieves information about the supplied JPEG.
        /// </summary>
        /// <param name="libHandle">The NvJPEG library handle.</param>
        /// <param name="imageBytes">The JPEG image bytes.</param>
        /// <param name="numComponents">Filled in with the number of components.</param>
        /// <param name="subsampling">Filled in with the subsampling.</param>
        /// <param name="widths">Filled in with the widths.</param>
        /// <param name="heights">Filled in with the heights.</param>
        /// <returns>The error code.</returns>
        public unsafe NvJpegStatus GetImageInfo(
            IntPtr libHandle,
            ReadOnlySpan<byte> imageBytes,
            out int numComponents,
            out NvJpegChromaSubsampling subsampling,
            out int[] widths,
            out int[] heights)
        {
            widths = new int[NvJpegConstants.NVJPEG_MAX_COMPONENT];
            heights = new int[NvJpegConstants.NVJPEG_MAX_COMPONENT];

            fixed (byte* imageBytesPtr = imageBytes)
            fixed (int* widthsPtr = widths)
            fixed (int* heightsPtr = heights)
            {
                return GetImageInfo(
                    libHandle,
                    imageBytesPtr,
                    (ulong)imageBytes.Length,
                    out numComponents,
                    out subsampling,
                    widthsPtr,
                    heightsPtr);
            }
        }

        /// <summary>
        /// Performs single image decode.
        /// </summary>
        /// <param name="libHandle">The NvJPEG library handle</param>
        /// <param name="stateHandle">The NvJPEG state handle.</param>
        /// <param name="imageBytes">The JPEG image bytes.</param>
        /// <param name="outputFormat">The desired output format.</param>
        /// <param name="destination">The destination buffer.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <returns>The error code.</returns>
        public unsafe NvJpegStatus Decode(
            IntPtr libHandle,
            IntPtr stateHandle,
            ReadOnlySpan<byte> imageBytes,
            NvJpegOutputFormat outputFormat,
            in NvJpegImage destination,
            CudaStream? stream)
        {
            var imageInterop = destination.ToInterop();

            fixed (byte* imageBytesPtr = imageBytes)
            {
                return Decode(
                    libHandle,
                    stateHandle,
                    imageBytesPtr,
                    (ulong)imageBytes.Length,
                    outputFormat,
                    &imageInterop,
                    stream?.StreamPtr ?? IntPtr.Zero);
            }
        }

        /// <inheritdoc cref="Decode(
        ///     IntPtr,
        ///     IntPtr,
        ///     ReadOnlySpan{byte},
        ///     NvJpegOutputFormat,
        ///     in NvJpegImage,
        ///     CudaStream)"/>
        public NvJpegStatus Decode(
            IntPtr libHandle,
            IntPtr stateHandle,
            ReadOnlySpan<byte> imageBytes,
            NvJpegOutputFormat outputFormat,
            in NvJpegImage destination) =>
            Decode(
                libHandle,
                stateHandle,
                imageBytes,
                outputFormat,
                destination,
                null);

        /// <summary>
        /// Performs single image encode to YUV.
        /// </summary>
        /// <param name="libHandle">The NvJPEG library handle.</param>
        /// <param name="stateHandle">The NvJPEG state handle.</param>
        /// <param name="encoderParamsHandle">The NvJPEG encoder parameters handle.</param>
        /// <param name="source">The source image buffer.</param>
        /// <param name="subsampling">The chroma subsampling.</param>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <returns>The error code.</returns>
        public unsafe NvJpegStatus EncodeYUV(
            IntPtr libHandle,
            IntPtr stateHandle,
            IntPtr encoderParamsHandle,
            in NvJpegImage source,
            NvJpegChromaSubsampling subsampling,
            uint width,
            uint height,
            CudaStream? stream)
        {
            var imageInterop = source.ToInterop();

            NvJpegImage_Interop* sourceInterop = &imageInterop;
            return EncodeYUV(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                sourceInterop,
                subsampling,
                width,
                height,
                stream?.StreamPtr ?? IntPtr.Zero);
        }

        /// <inheritdoc cref="EncodeYUV(
        ///     IntPtr,
        ///     IntPtr,
        ///     IntPtr,
        ///     in NvJpegImage,
        ///     NvJpegChromaSubsampling,
        ///     uint,
        ///     uint,
        ///     CudaStream)"/>
        public NvJpegStatus EncodeYUV(
            IntPtr libHandle,
            IntPtr stateHandle,
            IntPtr encoderParamsHandle,
            in NvJpegImage source,
            NvJpegChromaSubsampling subsampling,
            uint width,
            uint height) =>
            EncodeYUV(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                subsampling,
                width,
                height,
                null);

        /// <summary>
        /// Performs single image encode.
        /// </summary>
        /// <param name="libHandle">The NvJPEG library handle.</param>
        /// <param name="stateHandle">The NvJPEG state handle.</param>
        /// <param name="encoderParamsHandle">The NvJPEG encoder parameters handle.</param>
        /// <param name="source">The source image buffer.</param>
        /// <param name="inputFormat">The input format.</param>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <returns>The error code.</returns>
        public unsafe NvJpegStatus EncodeImage(
            IntPtr libHandle,
            IntPtr stateHandle,
            IntPtr encoderParamsHandle,
            in NvJpegImage source,
            NvJpegInputFormat inputFormat,
            uint width,
            uint height,
            CudaStream? stream)
        {
            var imageInterop = source.ToInterop();

            NvJpegImage_Interop* sourceInterop = &imageInterop;
            return EncodeImage(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                sourceInterop,
                inputFormat,
                width,
                height,
                stream?.StreamPtr ?? IntPtr.Zero);
        }

        /// <inheritdoc cref="EncodeImage(
        ///     IntPtr,
        ///     IntPtr,
        ///     IntPtr,
        ///     in NvJpegImage,
        ///     NvJpegInputFormat,
        ///     uint,
        ///     uint,
        ///     CudaStream)"/>
        public NvJpegStatus EncodeImage(
            IntPtr libHandle,
            IntPtr stateHandle,
            IntPtr encoderParamsHandle,
            in NvJpegImage source,
            NvJpegInputFormat inputFormat,
            uint width,
            uint height) =>
            EncodeImage(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                inputFormat,
                width,
                height,
                null);

        /// <summary>
        /// Creates encoder parameters.
        /// </summary>
        /// <param name="libHandle">The NvJPEG library handle.</param>
        /// <param name="encoderParamsHandle">The created encoder parameters handle.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <returns>The error code.</returns>
        public NvJpegStatus EncoderParamsCreate(
            IntPtr libHandle,
            out IntPtr encoderParamsHandle,
            CudaStream? stream) =>
            EncoderParamsCreate(
                libHandle,
                out encoderParamsHandle,
                stream?.StreamPtr ?? IntPtr.Zero);

        /// <inheritdoc cref="EncoderParamsCreate(IntPtr, out IntPtr, CudaStream)"/>
        public NvJpegStatus EncoderParamsCreate(
            IntPtr libHandle,
            out IntPtr encoderParamsHandle) =>
            EncoderParamsCreate(
                libHandle,
                out encoderParamsHandle,
                null);

        /// <summary>
        /// Sets the encoder quality.
        /// </summary>
        /// <param name="encoderParamsHandle">The encoder params handle.</param>
        /// <param name="quality">The quality value.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <returns>The error code.</returns>
        public NvJpegStatus EncoderParamsSetQuality(
            IntPtr encoderParamsHandle,
            int quality,
            CudaStream? stream) =>
            EncoderParamsSetQuality(
                encoderParamsHandle,
                quality,
                stream?.StreamPtr ?? IntPtr.Zero);

        /// <inheritdoc cref="EncoderParamsSetQuality(IntPtr, int, CudaStream)"/>
        public NvJpegStatus EncoderParamsSetQuality(
            IntPtr encoderParamsHandle,
            int quality) =>
            EncoderParamsSetQuality(
                encoderParamsHandle,
                quality,
                null);

        /// <summary>
        /// Sets encoder chroma subsampling factors.
        /// </summary>
        /// <param name="encoderParamsHandle">The encoder params handle.</param>
        /// <param name="subsampling">The chroma subsampling.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <returns>The error code.</returns>
        public NvJpegStatus EncoderParamsSetSamplingFactors(
            IntPtr encoderParamsHandle,
            NvJpegChromaSubsampling subsampling,
            CudaStream? stream) =>
            EncoderParamsSetSamplingFactors(
                encoderParamsHandle,
                subsampling,
                stream?.StreamPtr ?? IntPtr.Zero);

        /// <inheritdoc cref="EncoderParamsSetSamplingFactors(IntPtr, NvJpegChromaSubsampling, CudaStream)"/>
        public NvJpegStatus EncoderParamsSetSamplingFactors(
            IntPtr encoderParamsHandle,
            NvJpegChromaSubsampling subsampling) =>
            EncoderParamsSetSamplingFactors(
                encoderParamsHandle,
                subsampling,
                null);

        /// <summary>
        /// Retrieves encoded JPEG bitstream.
        /// </summary>
        /// <param name="libHandle">The NvJPEG library handle.</param>
        /// <param name="stateHandle">The NvJPEG state handle.</param>
        /// <param name="data">Destination data buffer.</param>
        /// <param name="length">Length of the destination data buffer.</param>
        /// <param name="stream">The accelerator stream.</param>
        /// <returns>The error code.</returns>
        public unsafe NvJpegStatus EncodeRetrieveBitstream(
            IntPtr libHandle,
            IntPtr stateHandle,
            Span<byte> data,
            out ulong length,
            CudaStream? stream)
        {
            fixed (byte* dataPtr = data)
            {
                length = (ulong)data.Length;
                return EncodeRetrieveBitstream(
                    libHandle,
                    stateHandle,
                    dataPtr,
                    ref length,
                    stream?.StreamPtr ?? IntPtr.Zero);
            }
        }

        /// <inheritdoc cref="EncodeRetrieveBitstream(IntPtr, IntPtr, Span{byte}, out ulong, CudaStream)"/>
        public NvJpegStatus EncodeRetrieveBitstream(
            IntPtr libHandle,
            IntPtr stateHandle,
            Span<byte> data,
            out ulong length) =>
            EncodeRetrieveBitstream(
                libHandle,
                stateHandle,
                data,
                out length,
                null);

        #endregion
    }
}
