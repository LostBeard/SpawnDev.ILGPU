// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                           Copyright (c) 2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: NvJpegNativeMethods.tt/NvJpegNativeMethods.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2021-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: DllLibraryImporter.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: TypeInformation.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: DllImporter.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: TextTemplateHelpers.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace ILGPU.Runtime.Cuda.API
{
    public enum NvJpegAPIVersion
    {
        V11,
        V12,
    }

    partial class NvJpegAPI
    {
        #region Creation

        private static NvJpegAPI? CreateInternal(NvJpegAPIVersion version)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                version == NvJpegAPIVersion.V11)
            {
                return new NvJpegAPI_Windows_V11();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                version == NvJpegAPIVersion.V11)
            {
                return new NvJpegAPI_Linux_V11();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                version == NvJpegAPIVersion.V12)
            {
                return new NvJpegAPI_Windows_V12();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                version == NvJpegAPIVersion.V12)
            {
                return new NvJpegAPI_Linux_V12();
            }
            return null;
        }

        #endregion

        #region Creation

        public abstract NvJpegStatus CreateSimple(
            [Out] out IntPtr libHandle);

        public abstract NvJpegStatus Destroy(
            [In] IntPtr libHandle);

        public abstract NvJpegStatus JpegStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle);

        public abstract NvJpegStatus JpegStateDestroy(
            [In] IntPtr stateHandle);

        public abstract NvJpegStatus EncoderStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle,
            [In] IntPtr cudaStreamHandle);

        public abstract NvJpegStatus EncoderStateDestroy(
            [In] IntPtr stateHandle);

        #endregion

        #region Retrieve Encoded Image Information

        public unsafe abstract NvJpegStatus GetImageInfo(
            [In] IntPtr libHandle,
            [In] byte* data,
            [In] ulong length,
            [Out] out int numComponents,
            [Out] out NvJpegChromaSubsampling sampling,
            [In] int* widths,
            [In] int* heights);

        #endregion

        #region Decode

        public unsafe abstract NvJpegStatus Decode(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In] ulong length,
            [In] NvJpegOutputFormat outputFormat,
            [In] NvJpegImage_Interop* destination,
            [In] IntPtr cudaStreamHandle);

        #endregion

        #region Encode

        public unsafe abstract NvJpegStatus EncodeYUV(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegChromaSubsampling subsampling,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        public unsafe abstract NvJpegStatus EncodeImage(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegInputFormat inputFormat,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        public abstract NvJpegStatus EncoderParamsCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr encoderParamsHandle,
            [In] IntPtr cudaStreamHandle);

        public abstract NvJpegStatus EncoderParamsDestroy(
            [In] IntPtr encoderParamsHandle);

        public abstract NvJpegStatus EncoderParamsSetQuality(
            [In] IntPtr encoderParamsHandle,
            [In] int quality,
            [In] IntPtr cudaStreamHandle);

        public abstract NvJpegStatus EncoderParamsSetSamplingFactors(
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegChromaSubsampling subsampling,
            [In] IntPtr cudaStreamHandle);

        public unsafe abstract NvJpegStatus EncodeRetrieveBitstream(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In, Out] ref ulong length,
            [In] IntPtr cudaStreamHandle);

        #endregion

        #region Utilities

        public abstract NvJpegStatus GetProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        public abstract NvJpegStatus GetCudartProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        #endregion

    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5393:Do not use unsafe DllImportSearchPath value")]
    internal sealed partial class NvJpegAPI_Windows_V11 : NvJpegAPI
    {
        #region Constants

        public const string LibName = "nvjpeg64_11.dll";

        #endregion

        #region Creation

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegCreateSimple"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegCreateSimple(
             out IntPtr libHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegCreateSimple"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegCreateSimple(
            [Out] out IntPtr libHandle);

        #endif
        public sealed override NvJpegStatus CreateSimple(
            [Out] out IntPtr libHandle) =>
            nvjpegCreateSimple(
                out libHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegDestroy(
             IntPtr libHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegDestroy(
            [In] IntPtr libHandle);

        #endif
        public sealed override NvJpegStatus Destroy(
            [In] IntPtr libHandle) =>
            nvjpegDestroy(
                libHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegJpegStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegJpegStateCreate(
             IntPtr libHandle,
             out IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegJpegStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegJpegStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus JpegStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle) =>
            nvjpegJpegStateCreate(
                libHandle,
                out stateHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegJpegStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegJpegStateDestroy(
             IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegJpegStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegJpegStateDestroy(
            [In] IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus JpegStateDestroy(
            [In] IntPtr stateHandle) =>
            nvjpegJpegStateDestroy(
                stateHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderStateCreate(
             IntPtr libHandle,
             out IntPtr stateHandle,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderStateCreate(
                libHandle,
                out stateHandle,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderStateDestroy(
             IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderStateDestroy(
            [In] IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus EncoderStateDestroy(
            [In] IntPtr stateHandle) =>
            nvjpegEncoderStateDestroy(
                stateHandle);

        #endregion

        #region Retrieve Encoded Image Information

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetImageInfo"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegGetImageInfo(
             IntPtr libHandle,
             byte* data,
             ulong length,
             out int numComponents,
             out NvJpegChromaSubsampling sampling,
             int* widths,
             int* heights);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetImageInfo"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegGetImageInfo(
            [In] IntPtr libHandle,
            [In] byte* data,
            [In] ulong length,
            [Out] out int numComponents,
            [Out] out NvJpegChromaSubsampling sampling,
            [In] int* widths,
            [In] int* heights);

        #endif
        public unsafe sealed override NvJpegStatus GetImageInfo(
            [In] IntPtr libHandle,
            [In] byte* data,
            [In] ulong length,
            [Out] out int numComponents,
            [Out] out NvJpegChromaSubsampling sampling,
            [In] int* widths,
            [In] int* heights) =>
            nvjpegGetImageInfo(
                libHandle,
                data,
                length,
                out numComponents,
                out sampling,
                widths,
                heights);

        #endregion

        #region Decode

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegDecode"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegDecode(
             IntPtr libHandle,
             IntPtr stateHandle,
             byte* data,
             ulong length,
             NvJpegOutputFormat outputFormat,
             NvJpegImage_Interop* destination,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegDecode"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegDecode(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In] ulong length,
            [In] NvJpegOutputFormat outputFormat,
            [In] NvJpegImage_Interop* destination,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus Decode(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In] ulong length,
            [In] NvJpegOutputFormat outputFormat,
            [In] NvJpegImage_Interop* destination,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegDecode(
                libHandle,
                stateHandle,
                data,
                length,
                outputFormat,
                destination,
                cudaStreamHandle);

        #endregion

        #region Encode

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeYUV"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeYUV(
             IntPtr libHandle,
             IntPtr stateHandle,
             IntPtr encoderParamsHandle,
             NvJpegImage_Interop* source,
             NvJpegChromaSubsampling subsampling,
             uint width,
             uint height,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeYUV"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeYUV(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegChromaSubsampling subsampling,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeYUV(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegChromaSubsampling subsampling,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeYUV(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                subsampling,
                width,
                height,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeImage"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeImage(
             IntPtr libHandle,
             IntPtr stateHandle,
             IntPtr encoderParamsHandle,
             NvJpegImage_Interop* source,
             NvJpegInputFormat inputFormat,
             uint width,
             uint height,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeImage"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeImage(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegInputFormat inputFormat,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeImage(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegInputFormat inputFormat,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeImage(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                inputFormat,
                width,
                height,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsCreate(
             IntPtr libHandle,
             out IntPtr encoderParamsHandle,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr encoderParamsHandle,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr encoderParamsHandle,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsCreate(
                libHandle,
                out encoderParamsHandle,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsDestroy(
             IntPtr encoderParamsHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsDestroy(
            [In] IntPtr encoderParamsHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsDestroy(
            [In] IntPtr encoderParamsHandle) =>
            nvjpegEncoderParamsDestroy(
                encoderParamsHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetQuality"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsSetQuality(
             IntPtr encoderParamsHandle,
             int quality,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetQuality"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsSetQuality(
            [In] IntPtr encoderParamsHandle,
            [In] int quality,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsSetQuality(
            [In] IntPtr encoderParamsHandle,
            [In] int quality,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsSetQuality(
                encoderParamsHandle,
                quality,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetSamplingFactors"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsSetSamplingFactors(
             IntPtr encoderParamsHandle,
             NvJpegChromaSubsampling subsampling,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetSamplingFactors"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsSetSamplingFactors(
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegChromaSubsampling subsampling,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsSetSamplingFactors(
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegChromaSubsampling subsampling,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsSetSamplingFactors(
                encoderParamsHandle,
                subsampling,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeRetrieveBitstream"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeRetrieveBitstream(
             IntPtr libHandle,
             IntPtr stateHandle,
             byte* data,
             ref ulong length,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeRetrieveBitstream"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeRetrieveBitstream(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In, Out] ref ulong length,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeRetrieveBitstream(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In, Out] ref ulong length,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeRetrieveBitstream(
                libHandle,
                stateHandle,
                data,
                ref length,
                cudaStreamHandle);

        #endregion

        #region Utilities

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegGetProperty(
             LibraryPropertyType type,
             out int value);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegGetProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        #endif
        public sealed override NvJpegStatus GetProperty(
            [In] LibraryPropertyType type,
            [Out] out int value) =>
            nvjpegGetProperty(
                type,
                out value);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetCudartProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegGetCudartProperty(
             LibraryPropertyType type,
             out int value);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetCudartProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegGetCudartProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        #endif
        public sealed override NvJpegStatus GetCudartProperty(
            [In] LibraryPropertyType type,
            [Out] out int value) =>
            nvjpegGetCudartProperty(
                type,
                out value);

        #endregion

    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5393:Do not use unsafe DllImportSearchPath value")]
    internal sealed partial class NvJpegAPI_Linux_V11 : NvJpegAPI
    {
        #region Constants

        public const string LibName = "libnvjpeg.so.11";

        #endregion

        #region Creation

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegCreateSimple"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegCreateSimple(
             out IntPtr libHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegCreateSimple"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegCreateSimple(
            [Out] out IntPtr libHandle);

        #endif
        public sealed override NvJpegStatus CreateSimple(
            [Out] out IntPtr libHandle) =>
            nvjpegCreateSimple(
                out libHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegDestroy(
             IntPtr libHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegDestroy(
            [In] IntPtr libHandle);

        #endif
        public sealed override NvJpegStatus Destroy(
            [In] IntPtr libHandle) =>
            nvjpegDestroy(
                libHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegJpegStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegJpegStateCreate(
             IntPtr libHandle,
             out IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegJpegStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegJpegStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus JpegStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle) =>
            nvjpegJpegStateCreate(
                libHandle,
                out stateHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegJpegStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegJpegStateDestroy(
             IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegJpegStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegJpegStateDestroy(
            [In] IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus JpegStateDestroy(
            [In] IntPtr stateHandle) =>
            nvjpegJpegStateDestroy(
                stateHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderStateCreate(
             IntPtr libHandle,
             out IntPtr stateHandle,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderStateCreate(
                libHandle,
                out stateHandle,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderStateDestroy(
             IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderStateDestroy(
            [In] IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus EncoderStateDestroy(
            [In] IntPtr stateHandle) =>
            nvjpegEncoderStateDestroy(
                stateHandle);

        #endregion

        #region Retrieve Encoded Image Information

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetImageInfo"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegGetImageInfo(
             IntPtr libHandle,
             byte* data,
             ulong length,
             out int numComponents,
             out NvJpegChromaSubsampling sampling,
             int* widths,
             int* heights);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetImageInfo"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegGetImageInfo(
            [In] IntPtr libHandle,
            [In] byte* data,
            [In] ulong length,
            [Out] out int numComponents,
            [Out] out NvJpegChromaSubsampling sampling,
            [In] int* widths,
            [In] int* heights);

        #endif
        public unsafe sealed override NvJpegStatus GetImageInfo(
            [In] IntPtr libHandle,
            [In] byte* data,
            [In] ulong length,
            [Out] out int numComponents,
            [Out] out NvJpegChromaSubsampling sampling,
            [In] int* widths,
            [In] int* heights) =>
            nvjpegGetImageInfo(
                libHandle,
                data,
                length,
                out numComponents,
                out sampling,
                widths,
                heights);

        #endregion

        #region Decode

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegDecode"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegDecode(
             IntPtr libHandle,
             IntPtr stateHandle,
             byte* data,
             ulong length,
             NvJpegOutputFormat outputFormat,
             NvJpegImage_Interop* destination,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegDecode"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegDecode(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In] ulong length,
            [In] NvJpegOutputFormat outputFormat,
            [In] NvJpegImage_Interop* destination,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus Decode(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In] ulong length,
            [In] NvJpegOutputFormat outputFormat,
            [In] NvJpegImage_Interop* destination,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegDecode(
                libHandle,
                stateHandle,
                data,
                length,
                outputFormat,
                destination,
                cudaStreamHandle);

        #endregion

        #region Encode

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeYUV"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeYUV(
             IntPtr libHandle,
             IntPtr stateHandle,
             IntPtr encoderParamsHandle,
             NvJpegImage_Interop* source,
             NvJpegChromaSubsampling subsampling,
             uint width,
             uint height,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeYUV"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeYUV(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegChromaSubsampling subsampling,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeYUV(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegChromaSubsampling subsampling,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeYUV(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                subsampling,
                width,
                height,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeImage"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeImage(
             IntPtr libHandle,
             IntPtr stateHandle,
             IntPtr encoderParamsHandle,
             NvJpegImage_Interop* source,
             NvJpegInputFormat inputFormat,
             uint width,
             uint height,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeImage"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeImage(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegInputFormat inputFormat,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeImage(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegInputFormat inputFormat,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeImage(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                inputFormat,
                width,
                height,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsCreate(
             IntPtr libHandle,
             out IntPtr encoderParamsHandle,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr encoderParamsHandle,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr encoderParamsHandle,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsCreate(
                libHandle,
                out encoderParamsHandle,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsDestroy(
             IntPtr encoderParamsHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsDestroy(
            [In] IntPtr encoderParamsHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsDestroy(
            [In] IntPtr encoderParamsHandle) =>
            nvjpegEncoderParamsDestroy(
                encoderParamsHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetQuality"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsSetQuality(
             IntPtr encoderParamsHandle,
             int quality,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetQuality"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsSetQuality(
            [In] IntPtr encoderParamsHandle,
            [In] int quality,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsSetQuality(
            [In] IntPtr encoderParamsHandle,
            [In] int quality,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsSetQuality(
                encoderParamsHandle,
                quality,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetSamplingFactors"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsSetSamplingFactors(
             IntPtr encoderParamsHandle,
             NvJpegChromaSubsampling subsampling,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetSamplingFactors"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsSetSamplingFactors(
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegChromaSubsampling subsampling,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsSetSamplingFactors(
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegChromaSubsampling subsampling,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsSetSamplingFactors(
                encoderParamsHandle,
                subsampling,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeRetrieveBitstream"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeRetrieveBitstream(
             IntPtr libHandle,
             IntPtr stateHandle,
             byte* data,
             ref ulong length,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeRetrieveBitstream"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeRetrieveBitstream(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In, Out] ref ulong length,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeRetrieveBitstream(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In, Out] ref ulong length,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeRetrieveBitstream(
                libHandle,
                stateHandle,
                data,
                ref length,
                cudaStreamHandle);

        #endregion

        #region Utilities

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegGetProperty(
             LibraryPropertyType type,
             out int value);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegGetProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        #endif
        public sealed override NvJpegStatus GetProperty(
            [In] LibraryPropertyType type,
            [Out] out int value) =>
            nvjpegGetProperty(
                type,
                out value);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetCudartProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegGetCudartProperty(
             LibraryPropertyType type,
             out int value);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetCudartProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegGetCudartProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        #endif
        public sealed override NvJpegStatus GetCudartProperty(
            [In] LibraryPropertyType type,
            [Out] out int value) =>
            nvjpegGetCudartProperty(
                type,
                out value);

        #endregion

    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5393:Do not use unsafe DllImportSearchPath value")]
    internal sealed partial class NvJpegAPI_Windows_V12 : NvJpegAPI
    {
        #region Constants

        public const string LibName = "nvjpeg64_12.dll";

        #endregion

        #region Creation

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegCreateSimple"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegCreateSimple(
             out IntPtr libHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegCreateSimple"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegCreateSimple(
            [Out] out IntPtr libHandle);

        #endif
        public sealed override NvJpegStatus CreateSimple(
            [Out] out IntPtr libHandle) =>
            nvjpegCreateSimple(
                out libHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegDestroy(
             IntPtr libHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegDestroy(
            [In] IntPtr libHandle);

        #endif
        public sealed override NvJpegStatus Destroy(
            [In] IntPtr libHandle) =>
            nvjpegDestroy(
                libHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegJpegStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegJpegStateCreate(
             IntPtr libHandle,
             out IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegJpegStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegJpegStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus JpegStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle) =>
            nvjpegJpegStateCreate(
                libHandle,
                out stateHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegJpegStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegJpegStateDestroy(
             IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegJpegStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegJpegStateDestroy(
            [In] IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus JpegStateDestroy(
            [In] IntPtr stateHandle) =>
            nvjpegJpegStateDestroy(
                stateHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderStateCreate(
             IntPtr libHandle,
             out IntPtr stateHandle,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderStateCreate(
                libHandle,
                out stateHandle,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderStateDestroy(
             IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderStateDestroy(
            [In] IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus EncoderStateDestroy(
            [In] IntPtr stateHandle) =>
            nvjpegEncoderStateDestroy(
                stateHandle);

        #endregion

        #region Retrieve Encoded Image Information

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetImageInfo"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegGetImageInfo(
             IntPtr libHandle,
             byte* data,
             ulong length,
             out int numComponents,
             out NvJpegChromaSubsampling sampling,
             int* widths,
             int* heights);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetImageInfo"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegGetImageInfo(
            [In] IntPtr libHandle,
            [In] byte* data,
            [In] ulong length,
            [Out] out int numComponents,
            [Out] out NvJpegChromaSubsampling sampling,
            [In] int* widths,
            [In] int* heights);

        #endif
        public unsafe sealed override NvJpegStatus GetImageInfo(
            [In] IntPtr libHandle,
            [In] byte* data,
            [In] ulong length,
            [Out] out int numComponents,
            [Out] out NvJpegChromaSubsampling sampling,
            [In] int* widths,
            [In] int* heights) =>
            nvjpegGetImageInfo(
                libHandle,
                data,
                length,
                out numComponents,
                out sampling,
                widths,
                heights);

        #endregion

        #region Decode

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegDecode"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegDecode(
             IntPtr libHandle,
             IntPtr stateHandle,
             byte* data,
             ulong length,
             NvJpegOutputFormat outputFormat,
             NvJpegImage_Interop* destination,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegDecode"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegDecode(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In] ulong length,
            [In] NvJpegOutputFormat outputFormat,
            [In] NvJpegImage_Interop* destination,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus Decode(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In] ulong length,
            [In] NvJpegOutputFormat outputFormat,
            [In] NvJpegImage_Interop* destination,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegDecode(
                libHandle,
                stateHandle,
                data,
                length,
                outputFormat,
                destination,
                cudaStreamHandle);

        #endregion

        #region Encode

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeYUV"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeYUV(
             IntPtr libHandle,
             IntPtr stateHandle,
             IntPtr encoderParamsHandle,
             NvJpegImage_Interop* source,
             NvJpegChromaSubsampling subsampling,
             uint width,
             uint height,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeYUV"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeYUV(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegChromaSubsampling subsampling,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeYUV(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegChromaSubsampling subsampling,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeYUV(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                subsampling,
                width,
                height,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeImage"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeImage(
             IntPtr libHandle,
             IntPtr stateHandle,
             IntPtr encoderParamsHandle,
             NvJpegImage_Interop* source,
             NvJpegInputFormat inputFormat,
             uint width,
             uint height,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeImage"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeImage(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegInputFormat inputFormat,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeImage(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegInputFormat inputFormat,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeImage(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                inputFormat,
                width,
                height,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsCreate(
             IntPtr libHandle,
             out IntPtr encoderParamsHandle,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr encoderParamsHandle,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr encoderParamsHandle,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsCreate(
                libHandle,
                out encoderParamsHandle,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsDestroy(
             IntPtr encoderParamsHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsDestroy(
            [In] IntPtr encoderParamsHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsDestroy(
            [In] IntPtr encoderParamsHandle) =>
            nvjpegEncoderParamsDestroy(
                encoderParamsHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetQuality"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsSetQuality(
             IntPtr encoderParamsHandle,
             int quality,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetQuality"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsSetQuality(
            [In] IntPtr encoderParamsHandle,
            [In] int quality,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsSetQuality(
            [In] IntPtr encoderParamsHandle,
            [In] int quality,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsSetQuality(
                encoderParamsHandle,
                quality,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetSamplingFactors"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsSetSamplingFactors(
             IntPtr encoderParamsHandle,
             NvJpegChromaSubsampling subsampling,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetSamplingFactors"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsSetSamplingFactors(
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegChromaSubsampling subsampling,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsSetSamplingFactors(
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegChromaSubsampling subsampling,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsSetSamplingFactors(
                encoderParamsHandle,
                subsampling,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeRetrieveBitstream"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeRetrieveBitstream(
             IntPtr libHandle,
             IntPtr stateHandle,
             byte* data,
             ref ulong length,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeRetrieveBitstream"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeRetrieveBitstream(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In, Out] ref ulong length,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeRetrieveBitstream(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In, Out] ref ulong length,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeRetrieveBitstream(
                libHandle,
                stateHandle,
                data,
                ref length,
                cudaStreamHandle);

        #endregion

        #region Utilities

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegGetProperty(
             LibraryPropertyType type,
             out int value);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegGetProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        #endif
        public sealed override NvJpegStatus GetProperty(
            [In] LibraryPropertyType type,
            [Out] out int value) =>
            nvjpegGetProperty(
                type,
                out value);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetCudartProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegGetCudartProperty(
             LibraryPropertyType type,
             out int value);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetCudartProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegGetCudartProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        #endif
        public sealed override NvJpegStatus GetCudartProperty(
            [In] LibraryPropertyType type,
            [Out] out int value) =>
            nvjpegGetCudartProperty(
                type,
                out value);

        #endregion

    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5393:Do not use unsafe DllImportSearchPath value")]
    internal sealed partial class NvJpegAPI_Linux_V12 : NvJpegAPI
    {
        #region Constants

        public const string LibName = "libnvjpeg.so.12";

        #endregion

        #region Creation

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegCreateSimple"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegCreateSimple(
             out IntPtr libHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegCreateSimple"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegCreateSimple(
            [Out] out IntPtr libHandle);

        #endif
        public sealed override NvJpegStatus CreateSimple(
            [Out] out IntPtr libHandle) =>
            nvjpegCreateSimple(
                out libHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegDestroy(
             IntPtr libHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegDestroy(
            [In] IntPtr libHandle);

        #endif
        public sealed override NvJpegStatus Destroy(
            [In] IntPtr libHandle) =>
            nvjpegDestroy(
                libHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegJpegStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegJpegStateCreate(
             IntPtr libHandle,
             out IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegJpegStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegJpegStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus JpegStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle) =>
            nvjpegJpegStateCreate(
                libHandle,
                out stateHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegJpegStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegJpegStateDestroy(
             IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegJpegStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegJpegStateDestroy(
            [In] IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus JpegStateDestroy(
            [In] IntPtr stateHandle) =>
            nvjpegJpegStateDestroy(
                stateHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderStateCreate(
             IntPtr libHandle,
             out IntPtr stateHandle,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderStateCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderStateCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr stateHandle,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderStateCreate(
                libHandle,
                out stateHandle,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderStateDestroy(
             IntPtr stateHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderStateDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderStateDestroy(
            [In] IntPtr stateHandle);

        #endif
        public sealed override NvJpegStatus EncoderStateDestroy(
            [In] IntPtr stateHandle) =>
            nvjpegEncoderStateDestroy(
                stateHandle);

        #endregion

        #region Retrieve Encoded Image Information

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetImageInfo"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegGetImageInfo(
             IntPtr libHandle,
             byte* data,
             ulong length,
             out int numComponents,
             out NvJpegChromaSubsampling sampling,
             int* widths,
             int* heights);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetImageInfo"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegGetImageInfo(
            [In] IntPtr libHandle,
            [In] byte* data,
            [In] ulong length,
            [Out] out int numComponents,
            [Out] out NvJpegChromaSubsampling sampling,
            [In] int* widths,
            [In] int* heights);

        #endif
        public unsafe sealed override NvJpegStatus GetImageInfo(
            [In] IntPtr libHandle,
            [In] byte* data,
            [In] ulong length,
            [Out] out int numComponents,
            [Out] out NvJpegChromaSubsampling sampling,
            [In] int* widths,
            [In] int* heights) =>
            nvjpegGetImageInfo(
                libHandle,
                data,
                length,
                out numComponents,
                out sampling,
                widths,
                heights);

        #endregion

        #region Decode

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegDecode"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegDecode(
             IntPtr libHandle,
             IntPtr stateHandle,
             byte* data,
             ulong length,
             NvJpegOutputFormat outputFormat,
             NvJpegImage_Interop* destination,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegDecode"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegDecode(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In] ulong length,
            [In] NvJpegOutputFormat outputFormat,
            [In] NvJpegImage_Interop* destination,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus Decode(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In] ulong length,
            [In] NvJpegOutputFormat outputFormat,
            [In] NvJpegImage_Interop* destination,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegDecode(
                libHandle,
                stateHandle,
                data,
                length,
                outputFormat,
                destination,
                cudaStreamHandle);

        #endregion

        #region Encode

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeYUV"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeYUV(
             IntPtr libHandle,
             IntPtr stateHandle,
             IntPtr encoderParamsHandle,
             NvJpegImage_Interop* source,
             NvJpegChromaSubsampling subsampling,
             uint width,
             uint height,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeYUV"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeYUV(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegChromaSubsampling subsampling,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeYUV(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegChromaSubsampling subsampling,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeYUV(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                subsampling,
                width,
                height,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeImage"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeImage(
             IntPtr libHandle,
             IntPtr stateHandle,
             IntPtr encoderParamsHandle,
             NvJpegImage_Interop* source,
             NvJpegInputFormat inputFormat,
             uint width,
             uint height,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeImage"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeImage(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegInputFormat inputFormat,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeImage(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegImage_Interop* source,
            [In] NvJpegInputFormat inputFormat,
            [In] uint width,
            [In] uint height,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeImage(
                libHandle,
                stateHandle,
                encoderParamsHandle,
                source,
                inputFormat,
                width,
                height,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsCreate(
             IntPtr libHandle,
             out IntPtr encoderParamsHandle,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsCreate"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr encoderParamsHandle,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsCreate(
            [In] IntPtr libHandle,
            [Out] out IntPtr encoderParamsHandle,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsCreate(
                libHandle,
                out encoderParamsHandle,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsDestroy(
             IntPtr encoderParamsHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsDestroy"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsDestroy(
            [In] IntPtr encoderParamsHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsDestroy(
            [In] IntPtr encoderParamsHandle) =>
            nvjpegEncoderParamsDestroy(
                encoderParamsHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetQuality"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsSetQuality(
             IntPtr encoderParamsHandle,
             int quality,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetQuality"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsSetQuality(
            [In] IntPtr encoderParamsHandle,
            [In] int quality,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsSetQuality(
            [In] IntPtr encoderParamsHandle,
            [In] int quality,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsSetQuality(
                encoderParamsHandle,
                quality,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetSamplingFactors"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegEncoderParamsSetSamplingFactors(
             IntPtr encoderParamsHandle,
             NvJpegChromaSubsampling subsampling,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncoderParamsSetSamplingFactors"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegEncoderParamsSetSamplingFactors(
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegChromaSubsampling subsampling,
            [In] IntPtr cudaStreamHandle);

        #endif
        public sealed override NvJpegStatus EncoderParamsSetSamplingFactors(
            [In] IntPtr encoderParamsHandle,
            [In] NvJpegChromaSubsampling subsampling,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncoderParamsSetSamplingFactors(
                encoderParamsHandle,
                subsampling,
                cudaStreamHandle);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegEncodeRetrieveBitstream"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static partial
         NvJpegStatus nvjpegEncodeRetrieveBitstream(
             IntPtr libHandle,
             IntPtr stateHandle,
             byte* data,
             ref ulong length,
             IntPtr cudaStreamHandle);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegEncodeRetrieveBitstream"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal unsafe static extern
         NvJpegStatus nvjpegEncodeRetrieveBitstream(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In, Out] ref ulong length,
            [In] IntPtr cudaStreamHandle);

        #endif
        public unsafe sealed override NvJpegStatus EncodeRetrieveBitstream(
            [In] IntPtr libHandle,
            [In] IntPtr stateHandle,
            [In] byte* data,
            [In, Out] ref ulong length,
            [In] IntPtr cudaStreamHandle) =>
            nvjpegEncodeRetrieveBitstream(
                libHandle,
                stateHandle,
                data,
                ref length,
                cudaStreamHandle);

        #endregion

        #region Utilities

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegGetProperty(
             LibraryPropertyType type,
             out int value);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegGetProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        #endif
        public sealed override NvJpegStatus GetProperty(
            [In] LibraryPropertyType type,
            [Out] out int value) =>
            nvjpegGetProperty(
                type,
                out value);

        #if NET7_0_OR_GREATER
        [LibraryImport(LibName,
            EntryPoint = "nvjpegGetCudartProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static partial
         NvJpegStatus nvjpegGetCudartProperty(
             LibraryPropertyType type,
             out int value);

        #else
        [DllImport(LibName,
            EntryPoint = "nvjpegGetCudartProperty"
            )]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        internal static extern
         NvJpegStatus nvjpegGetCudartProperty(
            [In] LibraryPropertyType type,
            [Out] out int value);

        #endif
        public sealed override NvJpegStatus GetCudartProperty(
            [In] LibraryPropertyType type,
            [Out] out int value) =>
            nvjpegGetCudartProperty(
                type,
                out value);

        #endregion

    }

}


#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member