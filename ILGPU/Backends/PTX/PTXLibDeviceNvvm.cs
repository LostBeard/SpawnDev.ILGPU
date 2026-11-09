// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2021-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: PTXLibDeviceNvvm.tt/PTXLibDeviceNvvm.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: TextTransformHelpers.ttinclude
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
//                           Copyright (c) 2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: CudaLibDevice.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------


using ILGPU.IR;
using System.Collections.Generic;
using System.Text;

// disable: max_line_length

namespace ILGPU.Backends.PTX
{
    /// <summary>
    /// Contains the NVVM fragments for Cuda LibDevice functions.
    /// </summary>
    internal static class PTXLibDeviceNvvm
    {
        private const string irVersionFormat = @"
            !nvvmir.version = !{{!0}}
            !0 = !{{i32 {0}, i32 0}}";

        private const string prefix = @"
            target triple = ""nvptx64-unknown-cuda""
            target datalayout = ""e-p:64:64:64-i1:8:8-i8:8:8-i16:16:16-i32:32:32-i64:64:64-f32:32:32-f64:64:64-v16:16:16-v32:32:32-v64:64:64-v128:128:128-n16:32:64""";

        private const string __nv_abs = @"
            declare i32 @__nv_abs(i32 %x)

            define i32 @__ilgpu__nv_abs(i32 %x) {
            entry:
                %call = call i32 @__nv_abs(i32 %x)
                ret i32 %call
            }";

        private const string __nv_acos = @"
            declare double @__nv_acos(double %x)

            define double @__ilgpu__nv_acos(double %x) {
            entry:
                %call = call double @__nv_acos(double %x)
                ret double %call
            }";

        private const string __nv_acosf = @"
            declare float @__nv_acosf(float %x)

            define float @__ilgpu__nv_acosf(float %x) {
            entry:
                %call = call float @__nv_acosf(float %x)
                ret float %call
            }";

        private const string __nv_acosh = @"
            declare double @__nv_acosh(double %x)

            define double @__ilgpu__nv_acosh(double %x) {
            entry:
                %call = call double @__nv_acosh(double %x)
                ret double %call
            }";

        private const string __nv_acoshf = @"
            declare float @__nv_acoshf(float %x)

            define float @__ilgpu__nv_acoshf(float %x) {
            entry:
                %call = call float @__nv_acoshf(float %x)
                ret float %call
            }";

        private const string __nv_asin = @"
            declare double @__nv_asin(double %x)

            define double @__ilgpu__nv_asin(double %x) {
            entry:
                %call = call double @__nv_asin(double %x)
                ret double %call
            }";

        private const string __nv_asinf = @"
            declare float @__nv_asinf(float %x)

            define float @__ilgpu__nv_asinf(float %x) {
            entry:
                %call = call float @__nv_asinf(float %x)
                ret float %call
            }";

        private const string __nv_asinh = @"
            declare double @__nv_asinh(double %x)

            define double @__ilgpu__nv_asinh(double %x) {
            entry:
                %call = call double @__nv_asinh(double %x)
                ret double %call
            }";

        private const string __nv_asinhf = @"
            declare float @__nv_asinhf(float %x)

            define float @__ilgpu__nv_asinhf(float %x) {
            entry:
                %call = call float @__nv_asinhf(float %x)
                ret float %call
            }";

        private const string __nv_atan = @"
            declare double @__nv_atan(double %x)

            define double @__ilgpu__nv_atan(double %x) {
            entry:
                %call = call double @__nv_atan(double %x)
                ret double %call
            }";

        private const string __nv_atan2 = @"
            declare double @__nv_atan2(double %x,
            double %y)

            define double @__ilgpu__nv_atan2(double %x,
            double %y) {
            entry:
                %call = call double @__nv_atan2(double %x,double %y)
                ret double %call
            }";

        private const string __nv_atan2f = @"
            declare float @__nv_atan2f(float %x,
            float %y)

            define float @__ilgpu__nv_atan2f(float %x,
            float %y) {
            entry:
                %call = call float @__nv_atan2f(float %x,float %y)
                ret float %call
            }";

        private const string __nv_atanf = @"
            declare float @__nv_atanf(float %x)

            define float @__ilgpu__nv_atanf(float %x) {
            entry:
                %call = call float @__nv_atanf(float %x)
                ret float %call
            }";

        private const string __nv_atanh = @"
            declare double @__nv_atanh(double %x)

            define double @__ilgpu__nv_atanh(double %x) {
            entry:
                %call = call double @__nv_atanh(double %x)
                ret double %call
            }";

        private const string __nv_atanhf = @"
            declare float @__nv_atanhf(float %x)

            define float @__ilgpu__nv_atanhf(float %x) {
            entry:
                %call = call float @__nv_atanhf(float %x)
                ret float %call
            }";

        private const string __nv_brev = @"
            declare i32 @__nv_brev(i32 %x)

            define i32 @__ilgpu__nv_brev(i32 %x) {
            entry:
                %call = call i32 @__nv_brev(i32 %x)
                ret i32 %call
            }";

        private const string __nv_brevll = @"
            declare i64 @__nv_brevll(i64 %x)

            define i64 @__ilgpu__nv_brevll(i64 %x) {
            entry:
                %call = call i64 @__nv_brevll(i64 %x)
                ret i64 %call
            }";

        private const string __nv_byte_perm = @"
            declare i32 @__nv_byte_perm(i32 %x,
            i32 %y,
            i32 %z)

            define i32 @__ilgpu__nv_byte_perm(i32 %x,
            i32 %y,
            i32 %z) {
            entry:
                %call = call i32 @__nv_byte_perm(i32 %x,i32 %y,i32 %z)
                ret i32 %call
            }";

        private const string __nv_cbrt = @"
            declare double @__nv_cbrt(double %x)

            define double @__ilgpu__nv_cbrt(double %x) {
            entry:
                %call = call double @__nv_cbrt(double %x)
                ret double %call
            }";

        private const string __nv_cbrtf = @"
            declare float @__nv_cbrtf(float %x)

            define float @__ilgpu__nv_cbrtf(float %x) {
            entry:
                %call = call float @__nv_cbrtf(float %x)
                ret float %call
            }";

        private const string __nv_ceil = @"
            declare double @__nv_ceil(double %x)

            define double @__ilgpu__nv_ceil(double %x) {
            entry:
                %call = call double @__nv_ceil(double %x)
                ret double %call
            }";

        private const string __nv_ceilf = @"
            declare float @__nv_ceilf(float %x)

            define float @__ilgpu__nv_ceilf(float %x) {
            entry:
                %call = call float @__nv_ceilf(float %x)
                ret float %call
            }";

        private const string __nv_clz = @"
            declare i32 @__nv_clz(i32 %x)

            define i32 @__ilgpu__nv_clz(i32 %x) {
            entry:
                %call = call i32 @__nv_clz(i32 %x)
                ret i32 %call
            }";

        private const string __nv_clzll = @"
            declare i64 @__nv_clzll(i64 %x)

            define i64 @__ilgpu__nv_clzll(i64 %x) {
            entry:
                %call = call i64 @__nv_clzll(i64 %x)
                ret i64 %call
            }";

        private const string __nv_copysign = @"
            declare double @__nv_copysign(double %x,
            double %y)

            define double @__ilgpu__nv_copysign(double %x,
            double %y) {
            entry:
                %call = call double @__nv_copysign(double %x,double %y)
                ret double %call
            }";

        private const string __nv_copysignf = @"
            declare float @__nv_copysignf(float %x,
            float %y)

            define float @__ilgpu__nv_copysignf(float %x,
            float %y) {
            entry:
                %call = call float @__nv_copysignf(float %x,float %y)
                ret float %call
            }";

        private const string __nv_cos = @"
            declare double @__nv_cos(double %x)

            define double @__ilgpu__nv_cos(double %x) {
            entry:
                %call = call double @__nv_cos(double %x)
                ret double %call
            }";

        private const string __nv_cosf = @"
            declare float @__nv_cosf(float %x)

            define float @__ilgpu__nv_cosf(float %x) {
            entry:
                %call = call float @__nv_cosf(float %x)
                ret float %call
            }";

        private const string __nv_cosh = @"
            declare double @__nv_cosh(double %x)

            define double @__ilgpu__nv_cosh(double %x) {
            entry:
                %call = call double @__nv_cosh(double %x)
                ret double %call
            }";

        private const string __nv_coshf = @"
            declare float @__nv_coshf(float %x)

            define float @__ilgpu__nv_coshf(float %x) {
            entry:
                %call = call float @__nv_coshf(float %x)
                ret float %call
            }";

        private const string __nv_cospi = @"
            declare double @__nv_cospi(double %x)

            define double @__ilgpu__nv_cospi(double %x) {
            entry:
                %call = call double @__nv_cospi(double %x)
                ret double %call
            }";

        private const string __nv_cospif = @"
            declare float @__nv_cospif(float %x)

            define float @__ilgpu__nv_cospif(float %x) {
            entry:
                %call = call float @__nv_cospif(float %x)
                ret float %call
            }";

        private const string __nv_dadd_rd = @"
            declare double @__nv_dadd_rd(double %x,
            double %y)

            define double @__ilgpu__nv_dadd_rd(double %x,
            double %y) {
            entry:
                %call = call double @__nv_dadd_rd(double %x,double %y)
                ret double %call
            }";

        private const string __nv_dadd_rn = @"
            declare double @__nv_dadd_rn(double %x,
            double %y)

            define double @__ilgpu__nv_dadd_rn(double %x,
            double %y) {
            entry:
                %call = call double @__nv_dadd_rn(double %x,double %y)
                ret double %call
            }";

        private const string __nv_dadd_ru = @"
            declare double @__nv_dadd_ru(double %x,
            double %y)

            define double @__ilgpu__nv_dadd_ru(double %x,
            double %y) {
            entry:
                %call = call double @__nv_dadd_ru(double %x,double %y)
                ret double %call
            }";

        private const string __nv_dadd_rz = @"
            declare double @__nv_dadd_rz(double %x,
            double %y)

            define double @__ilgpu__nv_dadd_rz(double %x,
            double %y) {
            entry:
                %call = call double @__nv_dadd_rz(double %x,double %y)
                ret double %call
            }";

        private const string __nv_ddiv_rd = @"
            declare double @__nv_ddiv_rd(double %x,
            double %y)

            define double @__ilgpu__nv_ddiv_rd(double %x,
            double %y) {
            entry:
                %call = call double @__nv_ddiv_rd(double %x,double %y)
                ret double %call
            }";

        private const string __nv_ddiv_rn = @"
            declare double @__nv_ddiv_rn(double %x,
            double %y)

            define double @__ilgpu__nv_ddiv_rn(double %x,
            double %y) {
            entry:
                %call = call double @__nv_ddiv_rn(double %x,double %y)
                ret double %call
            }";

        private const string __nv_ddiv_ru = @"
            declare double @__nv_ddiv_ru(double %x,
            double %y)

            define double @__ilgpu__nv_ddiv_ru(double %x,
            double %y) {
            entry:
                %call = call double @__nv_ddiv_ru(double %x,double %y)
                ret double %call
            }";

        private const string __nv_ddiv_rz = @"
            declare double @__nv_ddiv_rz(double %x,
            double %y)

            define double @__ilgpu__nv_ddiv_rz(double %x,
            double %y) {
            entry:
                %call = call double @__nv_ddiv_rz(double %x,double %y)
                ret double %call
            }";

        private const string __nv_dmul_rd = @"
            declare double @__nv_dmul_rd(double %x,
            double %y)

            define double @__ilgpu__nv_dmul_rd(double %x,
            double %y) {
            entry:
                %call = call double @__nv_dmul_rd(double %x,double %y)
                ret double %call
            }";

        private const string __nv_dmul_rn = @"
            declare double @__nv_dmul_rn(double %x,
            double %y)

            define double @__ilgpu__nv_dmul_rn(double %x,
            double %y) {
            entry:
                %call = call double @__nv_dmul_rn(double %x,double %y)
                ret double %call
            }";

        private const string __nv_dmul_ru = @"
            declare double @__nv_dmul_ru(double %x,
            double %y)

            define double @__ilgpu__nv_dmul_ru(double %x,
            double %y) {
            entry:
                %call = call double @__nv_dmul_ru(double %x,double %y)
                ret double %call
            }";

        private const string __nv_dmul_rz = @"
            declare double @__nv_dmul_rz(double %x,
            double %y)

            define double @__ilgpu__nv_dmul_rz(double %x,
            double %y) {
            entry:
                %call = call double @__nv_dmul_rz(double %x,double %y)
                ret double %call
            }";

        private const string __nv_double2float_rd = @"
            declare float @__nv_double2float_rd(double %x)

            define float @__ilgpu__nv_double2float_rd(double %x) {
            entry:
                %call = call float @__nv_double2float_rd(double %x)
                ret float %call
            }";

        private const string __nv_double2float_rn = @"
            declare float @__nv_double2float_rn(double %x)

            define float @__ilgpu__nv_double2float_rn(double %x) {
            entry:
                %call = call float @__nv_double2float_rn(double %x)
                ret float %call
            }";

        private const string __nv_double2float_ru = @"
            declare float @__nv_double2float_ru(double %x)

            define float @__ilgpu__nv_double2float_ru(double %x) {
            entry:
                %call = call float @__nv_double2float_ru(double %x)
                ret float %call
            }";

        private const string __nv_double2float_rz = @"
            declare float @__nv_double2float_rz(double %x)

            define float @__ilgpu__nv_double2float_rz(double %x) {
            entry:
                %call = call float @__nv_double2float_rz(double %x)
                ret float %call
            }";

        private const string __nv_double2hiint = @"
            declare i32 @__nv_double2hiint(double %x)

            define i32 @__ilgpu__nv_double2hiint(double %x) {
            entry:
                %call = call i32 @__nv_double2hiint(double %x)
                ret i32 %call
            }";

        private const string __nv_double2int_rd = @"
            declare i32 @__nv_double2int_rd(double %x)

            define i32 @__ilgpu__nv_double2int_rd(double %x) {
            entry:
                %call = call i32 @__nv_double2int_rd(double %x)
                ret i32 %call
            }";

        private const string __nv_double2int_rn = @"
            declare i32 @__nv_double2int_rn(double %x)

            define i32 @__ilgpu__nv_double2int_rn(double %x) {
            entry:
                %call = call i32 @__nv_double2int_rn(double %x)
                ret i32 %call
            }";

        private const string __nv_double2int_ru = @"
            declare i32 @__nv_double2int_ru(double %x)

            define i32 @__ilgpu__nv_double2int_ru(double %x) {
            entry:
                %call = call i32 @__nv_double2int_ru(double %x)
                ret i32 %call
            }";

        private const string __nv_double2int_rz = @"
            declare i32 @__nv_double2int_rz(double %x)

            define i32 @__ilgpu__nv_double2int_rz(double %x) {
            entry:
                %call = call i32 @__nv_double2int_rz(double %x)
                ret i32 %call
            }";

        private const string __nv_double2ll_rd = @"
            declare i64 @__nv_double2ll_rd(double %x)

            define i64 @__ilgpu__nv_double2ll_rd(double %x) {
            entry:
                %call = call i64 @__nv_double2ll_rd(double %x)
                ret i64 %call
            }";

        private const string __nv_double2ll_rn = @"
            declare i64 @__nv_double2ll_rn(double %x)

            define i64 @__ilgpu__nv_double2ll_rn(double %x) {
            entry:
                %call = call i64 @__nv_double2ll_rn(double %x)
                ret i64 %call
            }";

        private const string __nv_double2ll_ru = @"
            declare i64 @__nv_double2ll_ru(double %x)

            define i64 @__ilgpu__nv_double2ll_ru(double %x) {
            entry:
                %call = call i64 @__nv_double2ll_ru(double %x)
                ret i64 %call
            }";

        private const string __nv_double2ll_rz = @"
            declare i64 @__nv_double2ll_rz(double %x)

            define i64 @__ilgpu__nv_double2ll_rz(double %x) {
            entry:
                %call = call i64 @__nv_double2ll_rz(double %x)
                ret i64 %call
            }";

        private const string __nv_double2loint = @"
            declare i32 @__nv_double2loint(double %x)

            define i32 @__ilgpu__nv_double2loint(double %x) {
            entry:
                %call = call i32 @__nv_double2loint(double %x)
                ret i32 %call
            }";

        private const string __nv_double2uint_rd = @"
            declare i32 @__nv_double2uint_rd(double %x)

            define i32 @__ilgpu__nv_double2uint_rd(double %x) {
            entry:
                %call = call i32 @__nv_double2uint_rd(double %x)
                ret i32 %call
            }";

        private const string __nv_double2uint_rn = @"
            declare i32 @__nv_double2uint_rn(double %x)

            define i32 @__ilgpu__nv_double2uint_rn(double %x) {
            entry:
                %call = call i32 @__nv_double2uint_rn(double %x)
                ret i32 %call
            }";

        private const string __nv_double2uint_ru = @"
            declare i32 @__nv_double2uint_ru(double %x)

            define i32 @__ilgpu__nv_double2uint_ru(double %x) {
            entry:
                %call = call i32 @__nv_double2uint_ru(double %x)
                ret i32 %call
            }";

        private const string __nv_double2uint_rz = @"
            declare i32 @__nv_double2uint_rz(double %x)

            define i32 @__ilgpu__nv_double2uint_rz(double %x) {
            entry:
                %call = call i32 @__nv_double2uint_rz(double %x)
                ret i32 %call
            }";

        private const string __nv_double2ull_rd = @"
            declare i64 @__nv_double2ull_rd(double %x)

            define i64 @__ilgpu__nv_double2ull_rd(double %x) {
            entry:
                %call = call i64 @__nv_double2ull_rd(double %x)
                ret i64 %call
            }";

        private const string __nv_double2ull_rn = @"
            declare i64 @__nv_double2ull_rn(double %x)

            define i64 @__ilgpu__nv_double2ull_rn(double %x) {
            entry:
                %call = call i64 @__nv_double2ull_rn(double %x)
                ret i64 %call
            }";

        private const string __nv_double2ull_ru = @"
            declare i64 @__nv_double2ull_ru(double %x)

            define i64 @__ilgpu__nv_double2ull_ru(double %x) {
            entry:
                %call = call i64 @__nv_double2ull_ru(double %x)
                ret i64 %call
            }";

        private const string __nv_double2ull_rz = @"
            declare i64 @__nv_double2ull_rz(double %x)

            define i64 @__ilgpu__nv_double2ull_rz(double %x) {
            entry:
                %call = call i64 @__nv_double2ull_rz(double %x)
                ret i64 %call
            }";

        private const string __nv_double_as_longlong = @"
            declare i64 @__nv_double_as_longlong(double %x)

            define i64 @__ilgpu__nv_double_as_longlong(double %x) {
            entry:
                %call = call i64 @__nv_double_as_longlong(double %x)
                ret i64 %call
            }";

        private const string __nv_drcp_rd = @"
            declare double @__nv_drcp_rd(double %x)

            define double @__ilgpu__nv_drcp_rd(double %x) {
            entry:
                %call = call double @__nv_drcp_rd(double %x)
                ret double %call
            }";

        private const string __nv_drcp_rn = @"
            declare double @__nv_drcp_rn(double %x)

            define double @__ilgpu__nv_drcp_rn(double %x) {
            entry:
                %call = call double @__nv_drcp_rn(double %x)
                ret double %call
            }";

        private const string __nv_drcp_ru = @"
            declare double @__nv_drcp_ru(double %x)

            define double @__ilgpu__nv_drcp_ru(double %x) {
            entry:
                %call = call double @__nv_drcp_ru(double %x)
                ret double %call
            }";

        private const string __nv_drcp_rz = @"
            declare double @__nv_drcp_rz(double %x)

            define double @__ilgpu__nv_drcp_rz(double %x) {
            entry:
                %call = call double @__nv_drcp_rz(double %x)
                ret double %call
            }";

        private const string __nv_dsqrt_rd = @"
            declare double @__nv_dsqrt_rd(double %x)

            define double @__ilgpu__nv_dsqrt_rd(double %x) {
            entry:
                %call = call double @__nv_dsqrt_rd(double %x)
                ret double %call
            }";

        private const string __nv_dsqrt_rn = @"
            declare double @__nv_dsqrt_rn(double %x)

            define double @__ilgpu__nv_dsqrt_rn(double %x) {
            entry:
                %call = call double @__nv_dsqrt_rn(double %x)
                ret double %call
            }";

        private const string __nv_dsqrt_ru = @"
            declare double @__nv_dsqrt_ru(double %x)

            define double @__ilgpu__nv_dsqrt_ru(double %x) {
            entry:
                %call = call double @__nv_dsqrt_ru(double %x)
                ret double %call
            }";

        private const string __nv_dsqrt_rz = @"
            declare double @__nv_dsqrt_rz(double %x)

            define double @__ilgpu__nv_dsqrt_rz(double %x) {
            entry:
                %call = call double @__nv_dsqrt_rz(double %x)
                ret double %call
            }";

        private const string __nv_erf = @"
            declare double @__nv_erf(double %x)

            define double @__ilgpu__nv_erf(double %x) {
            entry:
                %call = call double @__nv_erf(double %x)
                ret double %call
            }";

        private const string __nv_erfc = @"
            declare double @__nv_erfc(double %x)

            define double @__ilgpu__nv_erfc(double %x) {
            entry:
                %call = call double @__nv_erfc(double %x)
                ret double %call
            }";

        private const string __nv_erfcf = @"
            declare float @__nv_erfcf(float %x)

            define float @__ilgpu__nv_erfcf(float %x) {
            entry:
                %call = call float @__nv_erfcf(float %x)
                ret float %call
            }";

        private const string __nv_erfcinv = @"
            declare double @__nv_erfcinv(double %x)

            define double @__ilgpu__nv_erfcinv(double %x) {
            entry:
                %call = call double @__nv_erfcinv(double %x)
                ret double %call
            }";

        private const string __nv_erfcinvf = @"
            declare float @__nv_erfcinvf(float %x)

            define float @__ilgpu__nv_erfcinvf(float %x) {
            entry:
                %call = call float @__nv_erfcinvf(float %x)
                ret float %call
            }";

        private const string __nv_erfcx = @"
            declare double @__nv_erfcx(double %x)

            define double @__ilgpu__nv_erfcx(double %x) {
            entry:
                %call = call double @__nv_erfcx(double %x)
                ret double %call
            }";

        private const string __nv_erfcxf = @"
            declare float @__nv_erfcxf(float %x)

            define float @__ilgpu__nv_erfcxf(float %x) {
            entry:
                %call = call float @__nv_erfcxf(float %x)
                ret float %call
            }";

        private const string __nv_erff = @"
            declare float @__nv_erff(float %x)

            define float @__ilgpu__nv_erff(float %x) {
            entry:
                %call = call float @__nv_erff(float %x)
                ret float %call
            }";

        private const string __nv_erfinv = @"
            declare double @__nv_erfinv(double %x)

            define double @__ilgpu__nv_erfinv(double %x) {
            entry:
                %call = call double @__nv_erfinv(double %x)
                ret double %call
            }";

        private const string __nv_erfinvf = @"
            declare float @__nv_erfinvf(float %x)

            define float @__ilgpu__nv_erfinvf(float %x) {
            entry:
                %call = call float @__nv_erfinvf(float %x)
                ret float %call
            }";

        private const string __nv_exp = @"
            declare double @__nv_exp(double %x)

            define double @__ilgpu__nv_exp(double %x) {
            entry:
                %call = call double @__nv_exp(double %x)
                ret double %call
            }";

        private const string __nv_exp10 = @"
            declare double @__nv_exp10(double %x)

            define double @__ilgpu__nv_exp10(double %x) {
            entry:
                %call = call double @__nv_exp10(double %x)
                ret double %call
            }";

        private const string __nv_exp10f = @"
            declare float @__nv_exp10f(float %x)

            define float @__ilgpu__nv_exp10f(float %x) {
            entry:
                %call = call float @__nv_exp10f(float %x)
                ret float %call
            }";

        private const string __nv_exp2 = @"
            declare double @__nv_exp2(double %x)

            define double @__ilgpu__nv_exp2(double %x) {
            entry:
                %call = call double @__nv_exp2(double %x)
                ret double %call
            }";

        private const string __nv_exp2f = @"
            declare float @__nv_exp2f(float %x)

            define float @__ilgpu__nv_exp2f(float %x) {
            entry:
                %call = call float @__nv_exp2f(float %x)
                ret float %call
            }";

        private const string __nv_expf = @"
            declare float @__nv_expf(float %x)

            define float @__ilgpu__nv_expf(float %x) {
            entry:
                %call = call float @__nv_expf(float %x)
                ret float %call
            }";

        private const string __nv_expm1 = @"
            declare double @__nv_expm1(double %x)

            define double @__ilgpu__nv_expm1(double %x) {
            entry:
                %call = call double @__nv_expm1(double %x)
                ret double %call
            }";

        private const string __nv_expm1f = @"
            declare float @__nv_expm1f(float %x)

            define float @__ilgpu__nv_expm1f(float %x) {
            entry:
                %call = call float @__nv_expm1f(float %x)
                ret float %call
            }";

        private const string __nv_fabs = @"
            declare double @__nv_fabs(double %x)

            define double @__ilgpu__nv_fabs(double %x) {
            entry:
                %call = call double @__nv_fabs(double %x)
                ret double %call
            }";

        private const string __nv_fabsf = @"
            declare float @__nv_fabsf(float %x)

            define float @__ilgpu__nv_fabsf(float %x) {
            entry:
                %call = call float @__nv_fabsf(float %x)
                ret float %call
            }";

        private const string __nv_fadd_rd = @"
            declare float @__nv_fadd_rd(float %x,
            float %y)

            define float @__ilgpu__nv_fadd_rd(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fadd_rd(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fadd_rn = @"
            declare float @__nv_fadd_rn(float %x,
            float %y)

            define float @__ilgpu__nv_fadd_rn(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fadd_rn(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fadd_ru = @"
            declare float @__nv_fadd_ru(float %x,
            float %y)

            define float @__ilgpu__nv_fadd_ru(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fadd_ru(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fadd_rz = @"
            declare float @__nv_fadd_rz(float %x,
            float %y)

            define float @__ilgpu__nv_fadd_rz(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fadd_rz(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fast_cosf = @"
            declare float @__nv_fast_cosf(float %x)

            define float @__ilgpu__nv_fast_cosf(float %x) {
            entry:
                %call = call float @__nv_fast_cosf(float %x)
                ret float %call
            }";

        private const string __nv_fast_exp10f = @"
            declare float @__nv_fast_exp10f(float %x)

            define float @__ilgpu__nv_fast_exp10f(float %x) {
            entry:
                %call = call float @__nv_fast_exp10f(float %x)
                ret float %call
            }";

        private const string __nv_fast_expf = @"
            declare float @__nv_fast_expf(float %x)

            define float @__ilgpu__nv_fast_expf(float %x) {
            entry:
                %call = call float @__nv_fast_expf(float %x)
                ret float %call
            }";

        private const string __nv_fast_fdividef = @"
            declare float @__nv_fast_fdividef(float %x,
            float %y)

            define float @__ilgpu__nv_fast_fdividef(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fast_fdividef(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fast_log10f = @"
            declare float @__nv_fast_log10f(float %x)

            define float @__ilgpu__nv_fast_log10f(float %x) {
            entry:
                %call = call float @__nv_fast_log10f(float %x)
                ret float %call
            }";

        private const string __nv_fast_log2f = @"
            declare float @__nv_fast_log2f(float %x)

            define float @__ilgpu__nv_fast_log2f(float %x) {
            entry:
                %call = call float @__nv_fast_log2f(float %x)
                ret float %call
            }";

        private const string __nv_fast_logf = @"
            declare float @__nv_fast_logf(float %x)

            define float @__ilgpu__nv_fast_logf(float %x) {
            entry:
                %call = call float @__nv_fast_logf(float %x)
                ret float %call
            }";

        private const string __nv_fast_powf = @"
            declare float @__nv_fast_powf(float %x,
            float %y)

            define float @__ilgpu__nv_fast_powf(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fast_powf(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fast_sincosf = @"
            declare void @__nv_fast_sincosf(float %x,
            float* %sin,
            float* %cos)

            define void @__ilgpu__nv_fast_sincosf(float %x,
            float* %sin,
            float* %cos) {
            entry:
                call void @__nv_fast_sincosf(float %x,float* %sin,float* %cos)
                ret void
            }";

        private const string __nv_fast_sinf = @"
            declare float @__nv_fast_sinf(float %x)

            define float @__ilgpu__nv_fast_sinf(float %x) {
            entry:
                %call = call float @__nv_fast_sinf(float %x)
                ret float %call
            }";

        private const string __nv_fast_tanf = @"
            declare float @__nv_fast_tanf(float %x)

            define float @__ilgpu__nv_fast_tanf(float %x) {
            entry:
                %call = call float @__nv_fast_tanf(float %x)
                ret float %call
            }";

        private const string __nv_fdim = @"
            declare double @__nv_fdim(double %x,
            double %y)

            define double @__ilgpu__nv_fdim(double %x,
            double %y) {
            entry:
                %call = call double @__nv_fdim(double %x,double %y)
                ret double %call
            }";

        private const string __nv_fdimf = @"
            declare float @__nv_fdimf(float %x,
            float %y)

            define float @__ilgpu__nv_fdimf(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fdimf(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fdiv_rd = @"
            declare float @__nv_fdiv_rd(float %x,
            float %y)

            define float @__ilgpu__nv_fdiv_rd(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fdiv_rd(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fdiv_rn = @"
            declare float @__nv_fdiv_rn(float %x,
            float %y)

            define float @__ilgpu__nv_fdiv_rn(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fdiv_rn(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fdiv_ru = @"
            declare float @__nv_fdiv_ru(float %x,
            float %y)

            define float @__ilgpu__nv_fdiv_ru(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fdiv_ru(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fdiv_rz = @"
            declare float @__nv_fdiv_rz(float %x,
            float %y)

            define float @__ilgpu__nv_fdiv_rz(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fdiv_rz(float %x,float %y)
                ret float %call
            }";

        private const string __nv_ffs = @"
            declare i32 @__nv_ffs(i32 %x)

            define i32 @__ilgpu__nv_ffs(i32 %x) {
            entry:
                %call = call i32 @__nv_ffs(i32 %x)
                ret i32 %call
            }";

        private const string __nv_ffsll = @"
            declare i32 @__nv_ffsll(i64 %x)

            define i32 @__ilgpu__nv_ffsll(i64 %x) {
            entry:
                %call = call i32 @__nv_ffsll(i64 %x)
                ret i32 %call
            }";

        private const string __nv_finitef = @"
            declare i32 @__nv_finitef(float %x)

            define i32 @__ilgpu__nv_finitef(float %x) {
            entry:
                %call = call i32 @__nv_finitef(float %x)
                ret i32 %call
            }";

        private const string __nv_float2half_rn = @"
            declare i16 @__nv_float2half_rn(float %x)

            define i16 @__ilgpu__nv_float2half_rn(float %x) {
            entry:
                %call = call i16 @__nv_float2half_rn(float %x)
                ret i16 %call
            }";

        private const string __nv_float2int_rd = @"
            declare i32 @__nv_float2int_rd(float %x)

            define i32 @__ilgpu__nv_float2int_rd(float %x) {
            entry:
                %call = call i32 @__nv_float2int_rd(float %x)
                ret i32 %call
            }";

        private const string __nv_float2int_rn = @"
            declare i32 @__nv_float2int_rn(float %x)

            define i32 @__ilgpu__nv_float2int_rn(float %x) {
            entry:
                %call = call i32 @__nv_float2int_rn(float %x)
                ret i32 %call
            }";

        private const string __nv_float2int_ru = @"
            declare i32 @__nv_float2int_ru(float %x)

            define i32 @__ilgpu__nv_float2int_ru(float %x) {
            entry:
                %call = call i32 @__nv_float2int_ru(float %x)
                ret i32 %call
            }";

        private const string __nv_float2int_rz = @"
            declare i32 @__nv_float2int_rz(float %x)

            define i32 @__ilgpu__nv_float2int_rz(float %x) {
            entry:
                %call = call i32 @__nv_float2int_rz(float %x)
                ret i32 %call
            }";

        private const string __nv_float2ll_rd = @"
            declare i64 @__nv_float2ll_rd(float %x)

            define i64 @__ilgpu__nv_float2ll_rd(float %x) {
            entry:
                %call = call i64 @__nv_float2ll_rd(float %x)
                ret i64 %call
            }";

        private const string __nv_float2ll_rn = @"
            declare i64 @__nv_float2ll_rn(float %x)

            define i64 @__ilgpu__nv_float2ll_rn(float %x) {
            entry:
                %call = call i64 @__nv_float2ll_rn(float %x)
                ret i64 %call
            }";

        private const string __nv_float2ll_ru = @"
            declare i64 @__nv_float2ll_ru(float %x)

            define i64 @__ilgpu__nv_float2ll_ru(float %x) {
            entry:
                %call = call i64 @__nv_float2ll_ru(float %x)
                ret i64 %call
            }";

        private const string __nv_float2ll_rz = @"
            declare i64 @__nv_float2ll_rz(float %x)

            define i64 @__ilgpu__nv_float2ll_rz(float %x) {
            entry:
                %call = call i64 @__nv_float2ll_rz(float %x)
                ret i64 %call
            }";

        private const string __nv_float2uint_rd = @"
            declare i32 @__nv_float2uint_rd(float %x)

            define i32 @__ilgpu__nv_float2uint_rd(float %x) {
            entry:
                %call = call i32 @__nv_float2uint_rd(float %x)
                ret i32 %call
            }";

        private const string __nv_float2uint_rn = @"
            declare i32 @__nv_float2uint_rn(float %x)

            define i32 @__ilgpu__nv_float2uint_rn(float %x) {
            entry:
                %call = call i32 @__nv_float2uint_rn(float %x)
                ret i32 %call
            }";

        private const string __nv_float2uint_ru = @"
            declare i32 @__nv_float2uint_ru(float %x)

            define i32 @__ilgpu__nv_float2uint_ru(float %x) {
            entry:
                %call = call i32 @__nv_float2uint_ru(float %x)
                ret i32 %call
            }";

        private const string __nv_float2uint_rz = @"
            declare i32 @__nv_float2uint_rz(float %x)

            define i32 @__ilgpu__nv_float2uint_rz(float %x) {
            entry:
                %call = call i32 @__nv_float2uint_rz(float %x)
                ret i32 %call
            }";

        private const string __nv_float2ull_rd = @"
            declare i64 @__nv_float2ull_rd(float %x)

            define i64 @__ilgpu__nv_float2ull_rd(float %x) {
            entry:
                %call = call i64 @__nv_float2ull_rd(float %x)
                ret i64 %call
            }";

        private const string __nv_float2ull_rn = @"
            declare i64 @__nv_float2ull_rn(float %x)

            define i64 @__ilgpu__nv_float2ull_rn(float %x) {
            entry:
                %call = call i64 @__nv_float2ull_rn(float %x)
                ret i64 %call
            }";

        private const string __nv_float2ull_ru = @"
            declare i64 @__nv_float2ull_ru(float %x)

            define i64 @__ilgpu__nv_float2ull_ru(float %x) {
            entry:
                %call = call i64 @__nv_float2ull_ru(float %x)
                ret i64 %call
            }";

        private const string __nv_float2ull_rz = @"
            declare i64 @__nv_float2ull_rz(float %x)

            define i64 @__ilgpu__nv_float2ull_rz(float %x) {
            entry:
                %call = call i64 @__nv_float2ull_rz(float %x)
                ret i64 %call
            }";

        private const string __nv_float_as_int = @"
            declare i32 @__nv_float_as_int(float %x)

            define i32 @__ilgpu__nv_float_as_int(float %x) {
            entry:
                %call = call i32 @__nv_float_as_int(float %x)
                ret i32 %call
            }";

        private const string __nv_floor = @"
            declare double @__nv_floor(double %x)

            define double @__ilgpu__nv_floor(double %x) {
            entry:
                %call = call double @__nv_floor(double %x)
                ret double %call
            }";

        private const string __nv_floorf = @"
            declare float @__nv_floorf(float %x)

            define float @__ilgpu__nv_floorf(float %x) {
            entry:
                %call = call float @__nv_floorf(float %x)
                ret float %call
            }";

        private const string __nv_fma = @"
            declare double @__nv_fma(double %x,
            double %y,
            double %z)

            define double @__ilgpu__nv_fma(double %x,
            double %y,
            double %z) {
            entry:
                %call = call double @__nv_fma(double %x,double %y,double %z)
                ret double %call
            }";

        private const string __nv_fma_rd = @"
            declare double @__nv_fma_rd(double %x,
            double %y,
            double %z)

            define double @__ilgpu__nv_fma_rd(double %x,
            double %y,
            double %z) {
            entry:
                %call = call double @__nv_fma_rd(double %x,double %y,double %z)
                ret double %call
            }";

        private const string __nv_fma_rn = @"
            declare double @__nv_fma_rn(double %x,
            double %y,
            double %z)

            define double @__ilgpu__nv_fma_rn(double %x,
            double %y,
            double %z) {
            entry:
                %call = call double @__nv_fma_rn(double %x,double %y,double %z)
                ret double %call
            }";

        private const string __nv_fma_ru = @"
            declare double @__nv_fma_ru(double %x,
            double %y,
            double %z)

            define double @__ilgpu__nv_fma_ru(double %x,
            double %y,
            double %z) {
            entry:
                %call = call double @__nv_fma_ru(double %x,double %y,double %z)
                ret double %call
            }";

        private const string __nv_fma_rz = @"
            declare double @__nv_fma_rz(double %x,
            double %y,
            double %z)

            define double @__ilgpu__nv_fma_rz(double %x,
            double %y,
            double %z) {
            entry:
                %call = call double @__nv_fma_rz(double %x,double %y,double %z)
                ret double %call
            }";

        private const string __nv_fmaf = @"
            declare float @__nv_fmaf(float %x,
            float %y,
            float %z)

            define float @__ilgpu__nv_fmaf(float %x,
            float %y,
            float %z) {
            entry:
                %call = call float @__nv_fmaf(float %x,float %y,float %z)
                ret float %call
            }";

        private const string __nv_fmaf_rd = @"
            declare float @__nv_fmaf_rd(float %x,
            float %y,
            float %z)

            define float @__ilgpu__nv_fmaf_rd(float %x,
            float %y,
            float %z) {
            entry:
                %call = call float @__nv_fmaf_rd(float %x,float %y,float %z)
                ret float %call
            }";

        private const string __nv_fmaf_rn = @"
            declare float @__nv_fmaf_rn(float %x,
            float %y,
            float %z)

            define float @__ilgpu__nv_fmaf_rn(float %x,
            float %y,
            float %z) {
            entry:
                %call = call float @__nv_fmaf_rn(float %x,float %y,float %z)
                ret float %call
            }";

        private const string __nv_fmaf_ru = @"
            declare float @__nv_fmaf_ru(float %x,
            float %y,
            float %z)

            define float @__ilgpu__nv_fmaf_ru(float %x,
            float %y,
            float %z) {
            entry:
                %call = call float @__nv_fmaf_ru(float %x,float %y,float %z)
                ret float %call
            }";

        private const string __nv_fmaf_rz = @"
            declare float @__nv_fmaf_rz(float %x,
            float %y,
            float %z)

            define float @__ilgpu__nv_fmaf_rz(float %x,
            float %y,
            float %z) {
            entry:
                %call = call float @__nv_fmaf_rz(float %x,float %y,float %z)
                ret float %call
            }";

        private const string __nv_fmax = @"
            declare double @__nv_fmax(double %x,
            double %y)

            define double @__ilgpu__nv_fmax(double %x,
            double %y) {
            entry:
                %call = call double @__nv_fmax(double %x,double %y)
                ret double %call
            }";

        private const string __nv_fmaxf = @"
            declare float @__nv_fmaxf(float %x,
            float %y)

            define float @__ilgpu__nv_fmaxf(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fmaxf(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fmin = @"
            declare double @__nv_fmin(double %x,
            double %y)

            define double @__ilgpu__nv_fmin(double %x,
            double %y) {
            entry:
                %call = call double @__nv_fmin(double %x,double %y)
                ret double %call
            }";

        private const string __nv_fminf = @"
            declare float @__nv_fminf(float %x,
            float %y)

            define float @__ilgpu__nv_fminf(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fminf(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fmod = @"
            declare double @__nv_fmod(double %x,
            double %y)

            define double @__ilgpu__nv_fmod(double %x,
            double %y) {
            entry:
                %call = call double @__nv_fmod(double %x,double %y)
                ret double %call
            }";

        private const string __nv_fmodf = @"
            declare float @__nv_fmodf(float %x,
            float %y)

            define float @__ilgpu__nv_fmodf(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fmodf(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fmul_rd = @"
            declare float @__nv_fmul_rd(float %x,
            float %y)

            define float @__ilgpu__nv_fmul_rd(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fmul_rd(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fmul_rn = @"
            declare float @__nv_fmul_rn(float %x,
            float %y)

            define float @__ilgpu__nv_fmul_rn(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fmul_rn(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fmul_ru = @"
            declare float @__nv_fmul_ru(float %x,
            float %y)

            define float @__ilgpu__nv_fmul_ru(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fmul_ru(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fmul_rz = @"
            declare float @__nv_fmul_rz(float %x,
            float %y)

            define float @__ilgpu__nv_fmul_rz(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fmul_rz(float %x,float %y)
                ret float %call
            }";

        private const string __nv_frcp_rd = @"
            declare float @__nv_frcp_rd(float %x)

            define float @__ilgpu__nv_frcp_rd(float %x) {
            entry:
                %call = call float @__nv_frcp_rd(float %x)
                ret float %call
            }";

        private const string __nv_frcp_rn = @"
            declare float @__nv_frcp_rn(float %x)

            define float @__ilgpu__nv_frcp_rn(float %x) {
            entry:
                %call = call float @__nv_frcp_rn(float %x)
                ret float %call
            }";

        private const string __nv_frcp_ru = @"
            declare float @__nv_frcp_ru(float %x)

            define float @__ilgpu__nv_frcp_ru(float %x) {
            entry:
                %call = call float @__nv_frcp_ru(float %x)
                ret float %call
            }";

        private const string __nv_frcp_rz = @"
            declare float @__nv_frcp_rz(float %x)

            define float @__ilgpu__nv_frcp_rz(float %x) {
            entry:
                %call = call float @__nv_frcp_rz(float %x)
                ret float %call
            }";

        private const string __nv_frexp = @"
            declare double @__nv_frexp(double %x,
            i32* %y)

            define double @__ilgpu__nv_frexp(double %x,
            i32* %y) {
            entry:
                %call = call double @__nv_frexp(double %x,i32* %y)
                ret double %call
            }";

        private const string __nv_frexpf = @"
            declare float @__nv_frexpf(float %x,
            i32* %y)

            define float @__ilgpu__nv_frexpf(float %x,
            i32* %y) {
            entry:
                %call = call float @__nv_frexpf(float %x,i32* %y)
                ret float %call
            }";

        private const string __nv_frsqrt_rn = @"
            declare float @__nv_frsqrt_rn(float %x)

            define float @__ilgpu__nv_frsqrt_rn(float %x) {
            entry:
                %call = call float @__nv_frsqrt_rn(float %x)
                ret float %call
            }";

        private const string __nv_fsqrt_rd = @"
            declare float @__nv_fsqrt_rd(float %x)

            define float @__ilgpu__nv_fsqrt_rd(float %x) {
            entry:
                %call = call float @__nv_fsqrt_rd(float %x)
                ret float %call
            }";

        private const string __nv_fsqrt_rb = @"
            declare float @__nv_fsqrt_rb(float %x)

            define float @__ilgpu__nv_fsqrt_rb(float %x) {
            entry:
                %call = call float @__nv_fsqrt_rb(float %x)
                ret float %call
            }";

        private const string __nv_fsqrt_ru = @"
            declare float @__nv_fsqrt_ru(float %x)

            define float @__ilgpu__nv_fsqrt_ru(float %x) {
            entry:
                %call = call float @__nv_fsqrt_ru(float %x)
                ret float %call
            }";

        private const string __nv_fsqrt_rz = @"
            declare float @__nv_fsqrt_rz(float %x)

            define float @__ilgpu__nv_fsqrt_rz(float %x) {
            entry:
                %call = call float @__nv_fsqrt_rz(float %x)
                ret float %call
            }";

        private const string __nv_fsub_rd = @"
            declare float @__nv_fsub_rd(float %x,
            float %y)

            define float @__ilgpu__nv_fsub_rd(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fsub_rd(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fsub_rn = @"
            declare float @__nv_fsub_rn(float %x,
            float %y)

            define float @__ilgpu__nv_fsub_rn(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fsub_rn(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fsub_ru = @"
            declare float @__nv_fsub_ru(float %x,
            float %y)

            define float @__ilgpu__nv_fsub_ru(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fsub_ru(float %x,float %y)
                ret float %call
            }";

        private const string __nv_fsub_rz = @"
            declare float @__nv_fsub_rz(float %x,
            float %y)

            define float @__ilgpu__nv_fsub_rz(float %x,
            float %y) {
            entry:
                %call = call float @__nv_fsub_rz(float %x,float %y)
                ret float %call
            }";

        private const string __nv_hadd = @"
            declare i32 @__nv_hadd(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_hadd(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_hadd(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_half2float = @"
            declare float @__nv_half2float(i16 %x)

            define float @__ilgpu__nv_half2float(i16 %x) {
            entry:
                %call = call float @__nv_half2float(i16 %x)
                ret float %call
            }";

        private const string __nv_hiloint2double = @"
            declare double @__nv_hiloint2double(i32 %x,
            i32 %y)

            define double @__ilgpu__nv_hiloint2double(i32 %x,
            i32 %y) {
            entry:
                %call = call double @__nv_hiloint2double(i32 %x,i32 %y)
                ret double %call
            }";

        private const string __nv_hypot = @"
            declare double @__nv_hypot(double %x,
            double %y)

            define double @__ilgpu__nv_hypot(double %x,
            double %y) {
            entry:
                %call = call double @__nv_hypot(double %x,double %y)
                ret double %call
            }";

        private const string __nv_hypotf = @"
            declare float @__nv_hypotf(float %x,
            float %y)

            define float @__ilgpu__nv_hypotf(float %x,
            float %y) {
            entry:
                %call = call float @__nv_hypotf(float %x,float %y)
                ret float %call
            }";

        private const string __nv_ilogb = @"
            declare i32 @__nv_ilogb(double %x)

            define i32 @__ilgpu__nv_ilogb(double %x) {
            entry:
                %call = call i32 @__nv_ilogb(double %x)
                ret i32 %call
            }";

        private const string __nv_ilogbf = @"
            declare i32 @__nv_ilogbf(float %x)

            define i32 @__ilgpu__nv_ilogbf(float %x) {
            entry:
                %call = call i32 @__nv_ilogbf(float %x)
                ret i32 %call
            }";

        private const string __nv_int2double_rn = @"
            declare double @__nv_int2double_rn(i32 %x)

            define double @__ilgpu__nv_int2double_rn(i32 %x) {
            entry:
                %call = call double @__nv_int2double_rn(i32 %x)
                ret double %call
            }";

        private const string __nv_int2float_rd = @"
            declare float @__nv_int2float_rd(i32 %x)

            define float @__ilgpu__nv_int2float_rd(i32 %x) {
            entry:
                %call = call float @__nv_int2float_rd(i32 %x)
                ret float %call
            }";

        private const string __nv_int2float_rn = @"
            declare float @__nv_int2float_rn(i32 %x)

            define float @__ilgpu__nv_int2float_rn(i32 %x) {
            entry:
                %call = call float @__nv_int2float_rn(i32 %x)
                ret float %call
            }";

        private const string __nv_int2float_ru = @"
            declare float @__nv_int2float_ru(i32 %x)

            define float @__ilgpu__nv_int2float_ru(i32 %x) {
            entry:
                %call = call float @__nv_int2float_ru(i32 %x)
                ret float %call
            }";

        private const string __nv_int2float_rz = @"
            declare float @__nv_int2float_rz(i32 %x)

            define float @__ilgpu__nv_int2float_rz(i32 %x) {
            entry:
                %call = call float @__nv_int2float_rz(i32 %x)
                ret float %call
            }";

        private const string __nv_int_as_float = @"
            declare float @__nv_int_as_float(i32 %x)

            define float @__ilgpu__nv_int_as_float(i32 %x) {
            entry:
                %call = call float @__nv_int_as_float(i32 %x)
                ret float %call
            }";

        private const string __nv_isfinited = @"
            declare i32 @__nv_isfinited(double %x)

            define i32 @__ilgpu__nv_isfinited(double %x) {
            entry:
                %call = call i32 @__nv_isfinited(double %x)
                ret i32 %call
            }";

        private const string __nv_isinfd = @"
            declare i32 @__nv_isinfd(double %x)

            define i32 @__ilgpu__nv_isinfd(double %x) {
            entry:
                %call = call i32 @__nv_isinfd(double %x)
                ret i32 %call
            }";

        private const string __nv_isinff = @"
            declare i32 @__nv_isinff(float %x)

            define i32 @__ilgpu__nv_isinff(float %x) {
            entry:
                %call = call i32 @__nv_isinff(float %x)
                ret i32 %call
            }";

        private const string __nv_isnand = @"
            declare i32 @__nv_isnand(double %x)

            define i32 @__ilgpu__nv_isnand(double %x) {
            entry:
                %call = call i32 @__nv_isnand(double %x)
                ret i32 %call
            }";

        private const string __nv_isnanf = @"
            declare i32 @__nv_isnanf(float %x)

            define i32 @__ilgpu__nv_isnanf(float %x) {
            entry:
                %call = call i32 @__nv_isnanf(float %x)
                ret i32 %call
            }";

        private const string __nv_j0 = @"
            declare double @__nv_j0(double %x)

            define double @__ilgpu__nv_j0(double %x) {
            entry:
                %call = call double @__nv_j0(double %x)
                ret double %call
            }";

        private const string __nv_j0f = @"
            declare float @__nv_j0f(float %x)

            define float @__ilgpu__nv_j0f(float %x) {
            entry:
                %call = call float @__nv_j0f(float %x)
                ret float %call
            }";

        private const string __nv_j1 = @"
            declare double @__nv_j1(double %x)

            define double @__ilgpu__nv_j1(double %x) {
            entry:
                %call = call double @__nv_j1(double %x)
                ret double %call
            }";

        private const string __nv_j1f = @"
            declare float @__nv_j1f(float %x)

            define float @__ilgpu__nv_j1f(float %x) {
            entry:
                %call = call float @__nv_j1f(float %x)
                ret float %call
            }";

        private const string __nv_jn = @"
            declare double @__nv_jn(i32 %x,
            double %y)

            define double @__ilgpu__nv_jn(i32 %x,
            double %y) {
            entry:
                %call = call double @__nv_jn(i32 %x,double %y)
                ret double %call
            }";

        private const string __nv_jnf = @"
            declare float @__nv_jnf(i32 %x,
            float %y)

            define float @__ilgpu__nv_jnf(i32 %x,
            float %y) {
            entry:
                %call = call float @__nv_jnf(i32 %x,float %y)
                ret float %call
            }";

        private const string __nv_ldexp = @"
            declare double @__nv_ldexp(double %x,
            i32 %y)

            define double @__ilgpu__nv_ldexp(double %x,
            i32 %y) {
            entry:
                %call = call double @__nv_ldexp(double %x,i32 %y)
                ret double %call
            }";

        private const string __nv_ldexpf = @"
            declare float @__nv_ldexpf(float %x,
            i32 %y)

            define float @__ilgpu__nv_ldexpf(float %x,
            i32 %y) {
            entry:
                %call = call float @__nv_ldexpf(float %x,i32 %y)
                ret float %call
            }";

        private const string __nv_lgamma = @"
            declare double @__nv_lgamma(double %x)

            define double @__ilgpu__nv_lgamma(double %x) {
            entry:
                %call = call double @__nv_lgamma(double %x)
                ret double %call
            }";

        private const string __nv_lgammaf = @"
            declare float @__nv_lgammaf(float %x)

            define float @__ilgpu__nv_lgammaf(float %x) {
            entry:
                %call = call float @__nv_lgammaf(float %x)
                ret float %call
            }";

        private const string __nv_ll2double_rd = @"
            declare double @__nv_ll2double_rd(i64 %x)

            define double @__ilgpu__nv_ll2double_rd(i64 %x) {
            entry:
                %call = call double @__nv_ll2double_rd(i64 %x)
                ret double %call
            }";

        private const string __nv_ll2double_rn = @"
            declare double @__nv_ll2double_rn(i64 %x)

            define double @__ilgpu__nv_ll2double_rn(i64 %x) {
            entry:
                %call = call double @__nv_ll2double_rn(i64 %x)
                ret double %call
            }";

        private const string __nv_ll2double_ru = @"
            declare double @__nv_ll2double_ru(i64 %x)

            define double @__ilgpu__nv_ll2double_ru(i64 %x) {
            entry:
                %call = call double @__nv_ll2double_ru(i64 %x)
                ret double %call
            }";

        private const string __nv_ll2double_rz = @"
            declare double @__nv_ll2double_rz(i64 %x)

            define double @__ilgpu__nv_ll2double_rz(i64 %x) {
            entry:
                %call = call double @__nv_ll2double_rz(i64 %x)
                ret double %call
            }";

        private const string __nv_ll2float_rd = @"
            declare float @__nv_ll2float_rd(i64 %x)

            define float @__ilgpu__nv_ll2float_rd(i64 %x) {
            entry:
                %call = call float @__nv_ll2float_rd(i64 %x)
                ret float %call
            }";

        private const string __nv_ll2float_rn = @"
            declare float @__nv_ll2float_rn(i64 %x)

            define float @__ilgpu__nv_ll2float_rn(i64 %x) {
            entry:
                %call = call float @__nv_ll2float_rn(i64 %x)
                ret float %call
            }";

        private const string __nv_ll2float_ru = @"
            declare float @__nv_ll2float_ru(i64 %x)

            define float @__ilgpu__nv_ll2float_ru(i64 %x) {
            entry:
                %call = call float @__nv_ll2float_ru(i64 %x)
                ret float %call
            }";

        private const string __nv_ll2float_rz = @"
            declare float @__nv_ll2float_rz(i64 %x)

            define float @__ilgpu__nv_ll2float_rz(i64 %x) {
            entry:
                %call = call float @__nv_ll2float_rz(i64 %x)
                ret float %call
            }";

        private const string __nv_llabs = @"
            declare i64 @__nv_llabs(i64 %x)

            define i64 @__ilgpu__nv_llabs(i64 %x) {
            entry:
                %call = call i64 @__nv_llabs(i64 %x)
                ret i64 %call
            }";

        private const string __nv_llmax = @"
            declare i64 @__nv_llmax(i64 %x,
            i64 %y)

            define i64 @__ilgpu__nv_llmax(i64 %x,
            i64 %y) {
            entry:
                %call = call i64 @__nv_llmax(i64 %x,i64 %y)
                ret i64 %call
            }";

        private const string __nv_llmin = @"
            declare i64 @__nv_llmin(i64 %x,
            i64 %y)

            define i64 @__ilgpu__nv_llmin(i64 %x,
            i64 %y) {
            entry:
                %call = call i64 @__nv_llmin(i64 %x,i64 %y)
                ret i64 %call
            }";

        private const string __nv_llrint = @"
            declare i64 @__nv_llrint(double %x)

            define i64 @__ilgpu__nv_llrint(double %x) {
            entry:
                %call = call i64 @__nv_llrint(double %x)
                ret i64 %call
            }";

        private const string __nv_llrintf = @"
            declare i64 @__nv_llrintf(float %x)

            define i64 @__ilgpu__nv_llrintf(float %x) {
            entry:
                %call = call i64 @__nv_llrintf(float %x)
                ret i64 %call
            }";

        private const string __nv_llround = @"
            declare i64 @__nv_llround(double %x)

            define i64 @__ilgpu__nv_llround(double %x) {
            entry:
                %call = call i64 @__nv_llround(double %x)
                ret i64 %call
            }";

        private const string __nv_llroundf = @"
            declare i64 @__nv_llroundf(float %x)

            define i64 @__ilgpu__nv_llroundf(float %x) {
            entry:
                %call = call i64 @__nv_llroundf(float %x)
                ret i64 %call
            }";

        private const string __nv_log = @"
            declare double @__nv_log(double %x)

            define double @__ilgpu__nv_log(double %x) {
            entry:
                %call = call double @__nv_log(double %x)
                ret double %call
            }";

        private const string __nv_log10 = @"
            declare double @__nv_log10(double %x)

            define double @__ilgpu__nv_log10(double %x) {
            entry:
                %call = call double @__nv_log10(double %x)
                ret double %call
            }";

        private const string __nv_log10f = @"
            declare float @__nv_log10f(float %x)

            define float @__ilgpu__nv_log10f(float %x) {
            entry:
                %call = call float @__nv_log10f(float %x)
                ret float %call
            }";

        private const string __nv_log1p = @"
            declare double @__nv_log1p(double %x)

            define double @__ilgpu__nv_log1p(double %x) {
            entry:
                %call = call double @__nv_log1p(double %x)
                ret double %call
            }";

        private const string __nv_log1pf = @"
            declare float @__nv_log1pf(float %x)

            define float @__ilgpu__nv_log1pf(float %x) {
            entry:
                %call = call float @__nv_log1pf(float %x)
                ret float %call
            }";

        private const string __nv_log2 = @"
            declare double @__nv_log2(double %x)

            define double @__ilgpu__nv_log2(double %x) {
            entry:
                %call = call double @__nv_log2(double %x)
                ret double %call
            }";

        private const string __nv_log2f = @"
            declare float @__nv_log2f(float %x)

            define float @__ilgpu__nv_log2f(float %x) {
            entry:
                %call = call float @__nv_log2f(float %x)
                ret float %call
            }";

        private const string __nv_logb = @"
            declare double @__nv_logb(double %x)

            define double @__ilgpu__nv_logb(double %x) {
            entry:
                %call = call double @__nv_logb(double %x)
                ret double %call
            }";

        private const string __nv_logbf = @"
            declare float @__nv_logbf(float %x)

            define float @__ilgpu__nv_logbf(float %x) {
            entry:
                %call = call float @__nv_logbf(float %x)
                ret float %call
            }";

        private const string __nv_logf = @"
            declare float @__nv_logf(float %x)

            define float @__ilgpu__nv_logf(float %x) {
            entry:
                %call = call float @__nv_logf(float %x)
                ret float %call
            }";

        private const string __nv_longlong_as_double = @"
            declare double @__nv_longlong_as_double(i64 %x)

            define double @__ilgpu__nv_longlong_as_double(i64 %x) {
            entry:
                %call = call double @__nv_longlong_as_double(i64 %x)
                ret double %call
            }";

        private const string __nv_max = @"
            declare i32 @__nv_max(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_max(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_max(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_min = @"
            declare i32 @__nv_min(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_min(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_min(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_modf = @"
            declare double @__nv_modf(double %x,
            double* %b)

            define double @__ilgpu__nv_modf(double %x,
            double* %b) {
            entry:
                %call = call double @__nv_modf(double %x,double* %b)
                ret double %call
            }";

        private const string __nv_modff = @"
            declare float @__nv_modff(float %x,
            float* %b)

            define float @__ilgpu__nv_modff(float %x,
            float* %b) {
            entry:
                %call = call float @__nv_modff(float %x,float* %b)
                ret float %call
            }";

        private const string __nv_mul24 = @"
            declare i32 @__nv_mul24(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_mul24(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_mul24(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_mul64hi = @"
            declare i64 @__nv_mul64hi(i64 %x,
            i64 %y)

            define i64 @__ilgpu__nv_mul64hi(i64 %x,
            i64 %y) {
            entry:
                %call = call i64 @__nv_mul64hi(i64 %x,i64 %y)
                ret i64 %call
            }";

        private const string __nv_mulhi = @"
            declare i32 @__nv_mulhi(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_mulhi(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_mulhi(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_nan = @"
            declare double @__nv_nan(i8* %tagp)

            define double @__ilgpu__nv_nan(i8* %tagp) {
            entry:
                %call = call double @__nv_nan(i8* %tagp)
                ret double %call
            }";

        private const string __nv_nanf = @"
            declare float @__nv_nanf(i8* %tagp)

            define float @__ilgpu__nv_nanf(i8* %tagp) {
            entry:
                %call = call float @__nv_nanf(i8* %tagp)
                ret float %call
            }";

        private const string __nv_nearbyint = @"
            declare double @__nv_nearbyint(double %x)

            define double @__ilgpu__nv_nearbyint(double %x) {
            entry:
                %call = call double @__nv_nearbyint(double %x)
                ret double %call
            }";

        private const string __nv_nearbyintf = @"
            declare float @__nv_nearbyintf(float %x)

            define float @__ilgpu__nv_nearbyintf(float %x) {
            entry:
                %call = call float @__nv_nearbyintf(float %x)
                ret float %call
            }";

        private const string __nv_nextafter = @"
            declare double @__nv_nextafter(double %x)

            define double @__ilgpu__nv_nextafter(double %x) {
            entry:
                %call = call double @__nv_nextafter(double %x)
                ret double %call
            }";

        private const string __nv_nextafterf = @"
            declare float @__nv_nextafterf(float %x)

            define float @__ilgpu__nv_nextafterf(float %x) {
            entry:
                %call = call float @__nv_nextafterf(float %x)
                ret float %call
            }";

        private const string __nv_normcdf = @"
            declare double @__nv_normcdf(double %x)

            define double @__ilgpu__nv_normcdf(double %x) {
            entry:
                %call = call double @__nv_normcdf(double %x)
                ret double %call
            }";

        private const string __nv_normcdff = @"
            declare float @__nv_normcdff(float %x)

            define float @__ilgpu__nv_normcdff(float %x) {
            entry:
                %call = call float @__nv_normcdff(float %x)
                ret float %call
            }";

        private const string __nv_normcdfinv = @"
            declare double @__nv_normcdfinv(double %x)

            define double @__ilgpu__nv_normcdfinv(double %x) {
            entry:
                %call = call double @__nv_normcdfinv(double %x)
                ret double %call
            }";

        private const string __nv_normcdfinvf = @"
            declare float @__nv_normcdfinvf(float %x)

            define float @__ilgpu__nv_normcdfinvf(float %x) {
            entry:
                %call = call float @__nv_normcdfinvf(float %x)
                ret float %call
            }";

        private const string __nv_popc = @"
            declare i32 @__nv_popc(i32 %x)

            define i32 @__ilgpu__nv_popc(i32 %x) {
            entry:
                %call = call i32 @__nv_popc(i32 %x)
                ret i32 %call
            }";

        private const string __nv_popcll = @"
            declare i32 @__nv_popcll(i64 %x)

            define i32 @__ilgpu__nv_popcll(i64 %x) {
            entry:
                %call = call i32 @__nv_popcll(i64 %x)
                ret i32 %call
            }";

        private const string __nv_pow = @"
            declare double @__nv_pow(double %x,
            double %y)

            define double @__ilgpu__nv_pow(double %x,
            double %y) {
            entry:
                %call = call double @__nv_pow(double %x,double %y)
                ret double %call
            }";

        private const string __nv_powf = @"
            declare float @__nv_powf(float %x,
            float %y)

            define float @__ilgpu__nv_powf(float %x,
            float %y) {
            entry:
                %call = call float @__nv_powf(float %x,float %y)
                ret float %call
            }";

        private const string __nv_powi = @"
            declare double @__nv_powi(double %x,
            i32 %y)

            define double @__ilgpu__nv_powi(double %x,
            i32 %y) {
            entry:
                %call = call double @__nv_powi(double %x,i32 %y)
                ret double %call
            }";

        private const string __nv_powif = @"
            declare float @__nv_powif(float %x,
            i32 %y)

            define float @__ilgpu__nv_powif(float %x,
            i32 %y) {
            entry:
                %call = call float @__nv_powif(float %x,i32 %y)
                ret float %call
            }";

        private const string __nv_rcbrt = @"
            declare double @__nv_rcbrt(double %x)

            define double @__ilgpu__nv_rcbrt(double %x) {
            entry:
                %call = call double @__nv_rcbrt(double %x)
                ret double %call
            }";

        private const string __nv_rcbrtf = @"
            declare float @__nv_rcbrtf(float %x)

            define float @__ilgpu__nv_rcbrtf(float %x) {
            entry:
                %call = call float @__nv_rcbrtf(float %x)
                ret float %call
            }";

        private const string __nv_remainder = @"
            declare double @__nv_remainder(double %x,
            double %y)

            define double @__ilgpu__nv_remainder(double %x,
            double %y) {
            entry:
                %call = call double @__nv_remainder(double %x,double %y)
                ret double %call
            }";

        private const string __nv_remainderf = @"
            declare float @__nv_remainderf(float %x,
            float %y)

            define float @__ilgpu__nv_remainderf(float %x,
            float %y) {
            entry:
                %call = call float @__nv_remainderf(float %x,float %y)
                ret float %call
            }";

        private const string __nv_remquo = @"
            declare double @__nv_remquo(double %x,
            double %y,
            i32* %quo)

            define double @__ilgpu__nv_remquo(double %x,
            double %y,
            i32* %quo) {
            entry:
                %call = call double @__nv_remquo(double %x,double %y,i32* %quo)
                ret double %call
            }";

        private const string __nv_remquof = @"
            declare float @__nv_remquof(float %x,
            float %y,
            i32* %quo)

            define float @__ilgpu__nv_remquof(float %x,
            float %y,
            i32* %quo) {
            entry:
                %call = call float @__nv_remquof(float %x,float %y,i32* %quo)
                ret float %call
            }";

        private const string __nv_rhadd = @"
            declare i32 @__nv_rhadd(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_rhadd(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_rhadd(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_rint = @"
            declare double @__nv_rint(double %x)

            define double @__ilgpu__nv_rint(double %x) {
            entry:
                %call = call double @__nv_rint(double %x)
                ret double %call
            }";

        private const string __nv_rintf = @"
            declare float @__nv_rintf(float %x)

            define float @__ilgpu__nv_rintf(float %x) {
            entry:
                %call = call float @__nv_rintf(float %x)
                ret float %call
            }";

        private const string __nv_round = @"
            declare double @__nv_round(double %x)

            define double @__ilgpu__nv_round(double %x) {
            entry:
                %call = call double @__nv_round(double %x)
                ret double %call
            }";

        private const string __nv_roundf = @"
            declare float @__nv_roundf(float %x)

            define float @__ilgpu__nv_roundf(float %x) {
            entry:
                %call = call float @__nv_roundf(float %x)
                ret float %call
            }";

        private const string __nv_rsqrt = @"
            declare double @__nv_rsqrt(double %x)

            define double @__ilgpu__nv_rsqrt(double %x) {
            entry:
                %call = call double @__nv_rsqrt(double %x)
                ret double %call
            }";

        private const string __nv_rsqrtf = @"
            declare float @__nv_rsqrtf(float %x)

            define float @__ilgpu__nv_rsqrtf(float %x) {
            entry:
                %call = call float @__nv_rsqrtf(float %x)
                ret float %call
            }";

        private const string __nv_sad = @"
            declare i32 @__nv_sad(i32 %x,
            i32 %y,
            i32 %z)

            define i32 @__ilgpu__nv_sad(i32 %x,
            i32 %y,
            i32 %z) {
            entry:
                %call = call i32 @__nv_sad(i32 %x,i32 %y,i32 %z)
                ret i32 %call
            }";

        private const string __nv_saturatef = @"
            declare float @__nv_saturatef(float %x)

            define float @__ilgpu__nv_saturatef(float %x) {
            entry:
                %call = call float @__nv_saturatef(float %x)
                ret float %call
            }";

        private const string __nv_scalbn = @"
            declare double @__nv_scalbn(double %x,
            i32 %y)

            define double @__ilgpu__nv_scalbn(double %x,
            i32 %y) {
            entry:
                %call = call double @__nv_scalbn(double %x,i32 %y)
                ret double %call
            }";

        private const string __nv_scalbnf = @"
            declare float @__nv_scalbnf(float %x,
            i32 %y)

            define float @__ilgpu__nv_scalbnf(float %x,
            i32 %y) {
            entry:
                %call = call float @__nv_scalbnf(float %x,i32 %y)
                ret float %call
            }";

        private const string __nv_signbitd = @"
            declare i32 @__nv_signbitd(double %x)

            define i32 @__ilgpu__nv_signbitd(double %x) {
            entry:
                %call = call i32 @__nv_signbitd(double %x)
                ret i32 %call
            }";

        private const string __nv_signbitf = @"
            declare i32 @__nv_signbitf(float %x)

            define i32 @__ilgpu__nv_signbitf(float %x) {
            entry:
                %call = call i32 @__nv_signbitf(float %x)
                ret i32 %call
            }";

        private const string __nv_sin = @"
            declare double @__nv_sin(double %x)

            define double @__ilgpu__nv_sin(double %x) {
            entry:
                %call = call double @__nv_sin(double %x)
                ret double %call
            }";

        private const string __nv_sincos = @"
            declare void @__nv_sincos(double %x,
            double* %sin,
            double* %cos)

            define void @__ilgpu__nv_sincos(double %x,
            double* %sin,
            double* %cos) {
            entry:
                call void @__nv_sincos(double %x,double* %sin,double* %cos)
                ret void
            }";

        private const string __nv_sincosf = @"
            declare void @__nv_sincosf(float %x,
            float* %sin,
            float* %cos)

            define void @__ilgpu__nv_sincosf(float %x,
            float* %sin,
            float* %cos) {
            entry:
                call void @__nv_sincosf(float %x,float* %sin,float* %cos)
                ret void
            }";

        private const string __nv_sincospi = @"
            declare void @__nv_sincospi(double %x,
            double* %sin,
            double* %cos)

            define void @__ilgpu__nv_sincospi(double %x,
            double* %sin,
            double* %cos) {
            entry:
                call void @__nv_sincospi(double %x,double* %sin,double* %cos)
                ret void
            }";

        private const string __nv_sincospif = @"
            declare void @__nv_sincospif(float %x,
            float* %sin,
            float* %cos)

            define void @__ilgpu__nv_sincospif(float %x,
            float* %sin,
            float* %cos) {
            entry:
                call void @__nv_sincospif(float %x,float* %sin,float* %cos)
                ret void
            }";

        private const string __nv_sinf = @"
            declare float @__nv_sinf(float %x)

            define float @__ilgpu__nv_sinf(float %x) {
            entry:
                %call = call float @__nv_sinf(float %x)
                ret float %call
            }";

        private const string __nv_sinh = @"
            declare double @__nv_sinh(double %x)

            define double @__ilgpu__nv_sinh(double %x) {
            entry:
                %call = call double @__nv_sinh(double %x)
                ret double %call
            }";

        private const string __nv_sinhf = @"
            declare float @__nv_sinhf(float %x)

            define float @__ilgpu__nv_sinhf(float %x) {
            entry:
                %call = call float @__nv_sinhf(float %x)
                ret float %call
            }";

        private const string __nv_sinpi = @"
            declare double @__nv_sinpi(double %x)

            define double @__ilgpu__nv_sinpi(double %x) {
            entry:
                %call = call double @__nv_sinpi(double %x)
                ret double %call
            }";

        private const string __nv_sinpif = @"
            declare float @__nv_sinpif(float %x)

            define float @__ilgpu__nv_sinpif(float %x) {
            entry:
                %call = call float @__nv_sinpif(float %x)
                ret float %call
            }";

        private const string __nv_sqrt = @"
            declare double @__nv_sqrt(double %x)

            define double @__ilgpu__nv_sqrt(double %x) {
            entry:
                %call = call double @__nv_sqrt(double %x)
                ret double %call
            }";

        private const string __nv_sqrtf = @"
            declare float @__nv_sqrtf(float %x)

            define float @__ilgpu__nv_sqrtf(float %x) {
            entry:
                %call = call float @__nv_sqrtf(float %x)
                ret float %call
            }";

        private const string __nv_tan = @"
            declare double @__nv_tan(double %x)

            define double @__ilgpu__nv_tan(double %x) {
            entry:
                %call = call double @__nv_tan(double %x)
                ret double %call
            }";

        private const string __nv_tanf = @"
            declare float @__nv_tanf(float %x)

            define float @__ilgpu__nv_tanf(float %x) {
            entry:
                %call = call float @__nv_tanf(float %x)
                ret float %call
            }";

        private const string __nv_tanh = @"
            declare double @__nv_tanh(double %x)

            define double @__ilgpu__nv_tanh(double %x) {
            entry:
                %call = call double @__nv_tanh(double %x)
                ret double %call
            }";

        private const string __nv_tanhf = @"
            declare float @__nv_tanhf(float %x)

            define float @__ilgpu__nv_tanhf(float %x) {
            entry:
                %call = call float @__nv_tanhf(float %x)
                ret float %call
            }";

        private const string __nv_tgamma = @"
            declare double @__nv_tgamma(double %x)

            define double @__ilgpu__nv_tgamma(double %x) {
            entry:
                %call = call double @__nv_tgamma(double %x)
                ret double %call
            }";

        private const string __nv_tgammaf = @"
            declare float @__nv_tgammaf(float %x)

            define float @__ilgpu__nv_tgammaf(float %x) {
            entry:
                %call = call float @__nv_tgammaf(float %x)
                ret float %call
            }";

        private const string __nv_trunc = @"
            declare double @__nv_trunc(double %x)

            define double @__ilgpu__nv_trunc(double %x) {
            entry:
                %call = call double @__nv_trunc(double %x)
                ret double %call
            }";

        private const string __nv_truncf = @"
            declare float @__nv_truncf(float %x)

            define float @__ilgpu__nv_truncf(float %x) {
            entry:
                %call = call float @__nv_truncf(float %x)
                ret float %call
            }";

        private const string __nv_uhadd = @"
            declare i32 @__nv_uhadd(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_uhadd(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_uhadd(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_uint2double_rn = @"
            declare double @__nv_uint2double_rn(i32 %x)

            define double @__ilgpu__nv_uint2double_rn(i32 %x) {
            entry:
                %call = call double @__nv_uint2double_rn(i32 %x)
                ret double %call
            }";

        private const string __nv_uint2float_rd = @"
            declare float @__nv_uint2float_rd(i32 %x)

            define float @__ilgpu__nv_uint2float_rd(i32 %x) {
            entry:
                %call = call float @__nv_uint2float_rd(i32 %x)
                ret float %call
            }";

        private const string __nv_uint2float_rn = @"
            declare float @__nv_uint2float_rn(i32 %x)

            define float @__ilgpu__nv_uint2float_rn(i32 %x) {
            entry:
                %call = call float @__nv_uint2float_rn(i32 %x)
                ret float %call
            }";

        private const string __nv_uint2float_ru = @"
            declare float @__nv_uint2float_ru(i32 %x)

            define float @__ilgpu__nv_uint2float_ru(i32 %x) {
            entry:
                %call = call float @__nv_uint2float_ru(i32 %x)
                ret float %call
            }";

        private const string __nv_uint2float_rz = @"
            declare float @__nv_uint2float_rz(i32 %x)

            define float @__ilgpu__nv_uint2float_rz(i32 %x) {
            entry:
                %call = call float @__nv_uint2float_rz(i32 %x)
                ret float %call
            }";

        private const string __nv_ull2double_rd = @"
            declare double @__nv_ull2double_rd(i64 %x)

            define double @__ilgpu__nv_ull2double_rd(i64 %x) {
            entry:
                %call = call double @__nv_ull2double_rd(i64 %x)
                ret double %call
            }";

        private const string __nv_ull2double_rn = @"
            declare double @__nv_ull2double_rn(i64 %x)

            define double @__ilgpu__nv_ull2double_rn(i64 %x) {
            entry:
                %call = call double @__nv_ull2double_rn(i64 %x)
                ret double %call
            }";

        private const string __nv_ull2double_ru = @"
            declare double @__nv_ull2double_ru(i64 %x)

            define double @__ilgpu__nv_ull2double_ru(i64 %x) {
            entry:
                %call = call double @__nv_ull2double_ru(i64 %x)
                ret double %call
            }";

        private const string __nv_ull2double_rz = @"
            declare double @__nv_ull2double_rz(i64 %x)

            define double @__ilgpu__nv_ull2double_rz(i64 %x) {
            entry:
                %call = call double @__nv_ull2double_rz(i64 %x)
                ret double %call
            }";

        private const string __nv_ull2float_rd = @"
            declare float @__nv_ull2float_rd(i64 %x)

            define float @__ilgpu__nv_ull2float_rd(i64 %x) {
            entry:
                %call = call float @__nv_ull2float_rd(i64 %x)
                ret float %call
            }";

        private const string __nv_ull2float_rn = @"
            declare float @__nv_ull2float_rn(i64 %x)

            define float @__ilgpu__nv_ull2float_rn(i64 %x) {
            entry:
                %call = call float @__nv_ull2float_rn(i64 %x)
                ret float %call
            }";

        private const string __nv_ull2float_ru = @"
            declare float @__nv_ull2float_ru(i64 %x)

            define float @__ilgpu__nv_ull2float_ru(i64 %x) {
            entry:
                %call = call float @__nv_ull2float_ru(i64 %x)
                ret float %call
            }";

        private const string __nv_ull2float_rz = @"
            declare float @__nv_ull2float_rz(i64 %x)

            define float @__ilgpu__nv_ull2float_rz(i64 %x) {
            entry:
                %call = call float @__nv_ull2float_rz(i64 %x)
                ret float %call
            }";

        private const string __nv_ullmax = @"
            declare i64 @__nv_ullmax(i64 %x,
            i64 %y)

            define i64 @__ilgpu__nv_ullmax(i64 %x,
            i64 %y) {
            entry:
                %call = call i64 @__nv_ullmax(i64 %x,i64 %y)
                ret i64 %call
            }";

        private const string __nv_ullmin = @"
            declare i64 @__nv_ullmin(i64 %x,
            i64 %y)

            define i64 @__ilgpu__nv_ullmin(i64 %x,
            i64 %y) {
            entry:
                %call = call i64 @__nv_ullmin(i64 %x,i64 %y)
                ret i64 %call
            }";

        private const string __nv_umax = @"
            declare i32 @__nv_umax(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_umax(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_umax(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_umin = @"
            declare i32 @__nv_umin(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_umin(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_umin(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_umul24 = @"
            declare i32 @__nv_umul24(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_umul24(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_umul24(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_umul64hi = @"
            declare i64 @__nv_umul64hi(i64 %x,
            i64 %y)

            define i64 @__ilgpu__nv_umul64hi(i64 %x,
            i64 %y) {
            entry:
                %call = call i64 @__nv_umul64hi(i64 %x,i64 %y)
                ret i64 %call
            }";

        private const string __nv_umulhi = @"
            declare i32 @__nv_umulhi(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_umulhi(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_umulhi(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_urhadd = @"
            declare i32 @__nv_urhadd(i32 %x,
            i32 %y)

            define i32 @__ilgpu__nv_urhadd(i32 %x,
            i32 %y) {
            entry:
                %call = call i32 @__nv_urhadd(i32 %x,i32 %y)
                ret i32 %call
            }";

        private const string __nv_usad = @"
            declare i32 @__nv_usad(i32 %x,
            i32 %y,
            i32 %z)

            define i32 @__ilgpu__nv_usad(i32 %x,
            i32 %y,
            i32 %z) {
            entry:
                %call = call i32 @__nv_usad(i32 %x,i32 %y,i32 %z)
                ret i32 %call
            }";

        private const string __nv_y0 = @"
            declare double @__nv_y0(double %x)

            define double @__ilgpu__nv_y0(double %x) {
            entry:
                %call = call double @__nv_y0(double %x)
                ret double %call
            }";

        private const string __nv_y0f = @"
            declare float @__nv_y0f(float %x)

            define float @__ilgpu__nv_y0f(float %x) {
            entry:
                %call = call float @__nv_y0f(float %x)
                ret float %call
            }";

        private const string __nv_y1 = @"
            declare double @__nv_y1(double %x)

            define double @__ilgpu__nv_y1(double %x) {
            entry:
                %call = call double @__nv_y1(double %x)
                ret double %call
            }";

        private const string __nv_y1f = @"
            declare float @__nv_y1f(float %x)

            define float @__ilgpu__nv_y1f(float %x) {
            entry:
                %call = call float @__nv_y1f(float %x)
                ret float %call
            }";

        private const string __nv_yn = @"
            declare double @__nv_yn(double %x)

            define double @__ilgpu__nv_yn(double %x) {
            entry:
                %call = call double @__nv_yn(double %x)
                ret double %call
            }";

        private const string __nv_ynf = @"
            declare float @__nv_ynf(float %x)

            define float @__ilgpu__nv_ynf(float %x) {
            entry:
                %call = call float @__nv_ynf(float %x)
                ret float %call
            }";

        private static readonly Dictionary<string, string> fragments =
            new Dictionary<string, string>()
            {
                { "__nv_abs", __nv_abs },
                { "__nv_acos", __nv_acos },
                { "__nv_acosf", __nv_acosf },
                { "__nv_acosh", __nv_acosh },
                { "__nv_acoshf", __nv_acoshf },
                { "__nv_asin", __nv_asin },
                { "__nv_asinf", __nv_asinf },
                { "__nv_asinh", __nv_asinh },
                { "__nv_asinhf", __nv_asinhf },
                { "__nv_atan", __nv_atan },
                { "__nv_atan2", __nv_atan2 },
                { "__nv_atan2f", __nv_atan2f },
                { "__nv_atanf", __nv_atanf },
                { "__nv_atanh", __nv_atanh },
                { "__nv_atanhf", __nv_atanhf },
                { "__nv_brev", __nv_brev },
                { "__nv_brevll", __nv_brevll },
                { "__nv_byte_perm", __nv_byte_perm },
                { "__nv_cbrt", __nv_cbrt },
                { "__nv_cbrtf", __nv_cbrtf },
                { "__nv_ceil", __nv_ceil },
                { "__nv_ceilf", __nv_ceilf },
                { "__nv_clz", __nv_clz },
                { "__nv_clzll", __nv_clzll },
                { "__nv_copysign", __nv_copysign },
                { "__nv_copysignf", __nv_copysignf },
                { "__nv_cos", __nv_cos },
                { "__nv_cosf", __nv_cosf },
                { "__nv_cosh", __nv_cosh },
                { "__nv_coshf", __nv_coshf },
                { "__nv_cospi", __nv_cospi },
                { "__nv_cospif", __nv_cospif },
                { "__nv_dadd_rd", __nv_dadd_rd },
                { "__nv_dadd_rn", __nv_dadd_rn },
                { "__nv_dadd_ru", __nv_dadd_ru },
                { "__nv_dadd_rz", __nv_dadd_rz },
                { "__nv_ddiv_rd", __nv_ddiv_rd },
                { "__nv_ddiv_rn", __nv_ddiv_rn },
                { "__nv_ddiv_ru", __nv_ddiv_ru },
                { "__nv_ddiv_rz", __nv_ddiv_rz },
                { "__nv_dmul_rd", __nv_dmul_rd },
                { "__nv_dmul_rn", __nv_dmul_rn },
                { "__nv_dmul_ru", __nv_dmul_ru },
                { "__nv_dmul_rz", __nv_dmul_rz },
                { "__nv_double2float_rd", __nv_double2float_rd },
                { "__nv_double2float_rn", __nv_double2float_rn },
                { "__nv_double2float_ru", __nv_double2float_ru },
                { "__nv_double2float_rz", __nv_double2float_rz },
                { "__nv_double2hiint", __nv_double2hiint },
                { "__nv_double2int_rd", __nv_double2int_rd },
                { "__nv_double2int_rn", __nv_double2int_rn },
                { "__nv_double2int_ru", __nv_double2int_ru },
                { "__nv_double2int_rz", __nv_double2int_rz },
                { "__nv_double2ll_rd", __nv_double2ll_rd },
                { "__nv_double2ll_rn", __nv_double2ll_rn },
                { "__nv_double2ll_ru", __nv_double2ll_ru },
                { "__nv_double2ll_rz", __nv_double2ll_rz },
                { "__nv_double2loint", __nv_double2loint },
                { "__nv_double2uint_rd", __nv_double2uint_rd },
                { "__nv_double2uint_rn", __nv_double2uint_rn },
                { "__nv_double2uint_ru", __nv_double2uint_ru },
                { "__nv_double2uint_rz", __nv_double2uint_rz },
                { "__nv_double2ull_rd", __nv_double2ull_rd },
                { "__nv_double2ull_rn", __nv_double2ull_rn },
                { "__nv_double2ull_ru", __nv_double2ull_ru },
                { "__nv_double2ull_rz", __nv_double2ull_rz },
                { "__nv_double_as_longlong", __nv_double_as_longlong },
                { "__nv_drcp_rd", __nv_drcp_rd },
                { "__nv_drcp_rn", __nv_drcp_rn },
                { "__nv_drcp_ru", __nv_drcp_ru },
                { "__nv_drcp_rz", __nv_drcp_rz },
                { "__nv_dsqrt_rd", __nv_dsqrt_rd },
                { "__nv_dsqrt_rn", __nv_dsqrt_rn },
                { "__nv_dsqrt_ru", __nv_dsqrt_ru },
                { "__nv_dsqrt_rz", __nv_dsqrt_rz },
                { "__nv_erf", __nv_erf },
                { "__nv_erfc", __nv_erfc },
                { "__nv_erfcf", __nv_erfcf },
                { "__nv_erfcinv", __nv_erfcinv },
                { "__nv_erfcinvf", __nv_erfcinvf },
                { "__nv_erfcx", __nv_erfcx },
                { "__nv_erfcxf", __nv_erfcxf },
                { "__nv_erff", __nv_erff },
                { "__nv_erfinv", __nv_erfinv },
                { "__nv_erfinvf", __nv_erfinvf },
                { "__nv_exp", __nv_exp },
                { "__nv_exp10", __nv_exp10 },
                { "__nv_exp10f", __nv_exp10f },
                { "__nv_exp2", __nv_exp2 },
                { "__nv_exp2f", __nv_exp2f },
                { "__nv_expf", __nv_expf },
                { "__nv_expm1", __nv_expm1 },
                { "__nv_expm1f", __nv_expm1f },
                { "__nv_fabs", __nv_fabs },
                { "__nv_fabsf", __nv_fabsf },
                { "__nv_fadd_rd", __nv_fadd_rd },
                { "__nv_fadd_rn", __nv_fadd_rn },
                { "__nv_fadd_ru", __nv_fadd_ru },
                { "__nv_fadd_rz", __nv_fadd_rz },
                { "__nv_fast_cosf", __nv_fast_cosf },
                { "__nv_fast_exp10f", __nv_fast_exp10f },
                { "__nv_fast_expf", __nv_fast_expf },
                { "__nv_fast_fdividef", __nv_fast_fdividef },
                { "__nv_fast_log10f", __nv_fast_log10f },
                { "__nv_fast_log2f", __nv_fast_log2f },
                { "__nv_fast_logf", __nv_fast_logf },
                { "__nv_fast_powf", __nv_fast_powf },
                { "__nv_fast_sincosf", __nv_fast_sincosf },
                { "__nv_fast_sinf", __nv_fast_sinf },
                { "__nv_fast_tanf", __nv_fast_tanf },
                { "__nv_fdim", __nv_fdim },
                { "__nv_fdimf", __nv_fdimf },
                { "__nv_fdiv_rd", __nv_fdiv_rd },
                { "__nv_fdiv_rn", __nv_fdiv_rn },
                { "__nv_fdiv_ru", __nv_fdiv_ru },
                { "__nv_fdiv_rz", __nv_fdiv_rz },
                { "__nv_ffs", __nv_ffs },
                { "__nv_ffsll", __nv_ffsll },
                { "__nv_finitef", __nv_finitef },
                { "__nv_float2half_rn", __nv_float2half_rn },
                { "__nv_float2int_rd", __nv_float2int_rd },
                { "__nv_float2int_rn", __nv_float2int_rn },
                { "__nv_float2int_ru", __nv_float2int_ru },
                { "__nv_float2int_rz", __nv_float2int_rz },
                { "__nv_float2ll_rd", __nv_float2ll_rd },
                { "__nv_float2ll_rn", __nv_float2ll_rn },
                { "__nv_float2ll_ru", __nv_float2ll_ru },
                { "__nv_float2ll_rz", __nv_float2ll_rz },
                { "__nv_float2uint_rd", __nv_float2uint_rd },
                { "__nv_float2uint_rn", __nv_float2uint_rn },
                { "__nv_float2uint_ru", __nv_float2uint_ru },
                { "__nv_float2uint_rz", __nv_float2uint_rz },
                { "__nv_float2ull_rd", __nv_float2ull_rd },
                { "__nv_float2ull_rn", __nv_float2ull_rn },
                { "__nv_float2ull_ru", __nv_float2ull_ru },
                { "__nv_float2ull_rz", __nv_float2ull_rz },
                { "__nv_float_as_int", __nv_float_as_int },
                { "__nv_floor", __nv_floor },
                { "__nv_floorf", __nv_floorf },
                { "__nv_fma", __nv_fma },
                { "__nv_fma_rd", __nv_fma_rd },
                { "__nv_fma_rn", __nv_fma_rn },
                { "__nv_fma_ru", __nv_fma_ru },
                { "__nv_fma_rz", __nv_fma_rz },
                { "__nv_fmaf", __nv_fmaf },
                { "__nv_fmaf_rd", __nv_fmaf_rd },
                { "__nv_fmaf_rn", __nv_fmaf_rn },
                { "__nv_fmaf_ru", __nv_fmaf_ru },
                { "__nv_fmaf_rz", __nv_fmaf_rz },
                { "__nv_fmax", __nv_fmax },
                { "__nv_fmaxf", __nv_fmaxf },
                { "__nv_fmin", __nv_fmin },
                { "__nv_fminf", __nv_fminf },
                { "__nv_fmod", __nv_fmod },
                { "__nv_fmodf", __nv_fmodf },
                { "__nv_fmul_rd", __nv_fmul_rd },
                { "__nv_fmul_rn", __nv_fmul_rn },
                { "__nv_fmul_ru", __nv_fmul_ru },
                { "__nv_fmul_rz", __nv_fmul_rz },
                { "__nv_frcp_rd", __nv_frcp_rd },
                { "__nv_frcp_rn", __nv_frcp_rn },
                { "__nv_frcp_ru", __nv_frcp_ru },
                { "__nv_frcp_rz", __nv_frcp_rz },
                { "__nv_frexp", __nv_frexp },
                { "__nv_frexpf", __nv_frexpf },
                { "__nv_frsqrt_rn", __nv_frsqrt_rn },
                { "__nv_fsqrt_rd", __nv_fsqrt_rd },
                { "__nv_fsqrt_rb", __nv_fsqrt_rb },
                { "__nv_fsqrt_ru", __nv_fsqrt_ru },
                { "__nv_fsqrt_rz", __nv_fsqrt_rz },
                { "__nv_fsub_rd", __nv_fsub_rd },
                { "__nv_fsub_rn", __nv_fsub_rn },
                { "__nv_fsub_ru", __nv_fsub_ru },
                { "__nv_fsub_rz", __nv_fsub_rz },
                { "__nv_hadd", __nv_hadd },
                { "__nv_half2float", __nv_half2float },
                { "__nv_hiloint2double", __nv_hiloint2double },
                { "__nv_hypot", __nv_hypot },
                { "__nv_hypotf", __nv_hypotf },
                { "__nv_ilogb", __nv_ilogb },
                { "__nv_ilogbf", __nv_ilogbf },
                { "__nv_int2double_rn", __nv_int2double_rn },
                { "__nv_int2float_rd", __nv_int2float_rd },
                { "__nv_int2float_rn", __nv_int2float_rn },
                { "__nv_int2float_ru", __nv_int2float_ru },
                { "__nv_int2float_rz", __nv_int2float_rz },
                { "__nv_int_as_float", __nv_int_as_float },
                { "__nv_isfinited", __nv_isfinited },
                { "__nv_isinfd", __nv_isinfd },
                { "__nv_isinff", __nv_isinff },
                { "__nv_isnand", __nv_isnand },
                { "__nv_isnanf", __nv_isnanf },
                { "__nv_j0", __nv_j0 },
                { "__nv_j0f", __nv_j0f },
                { "__nv_j1", __nv_j1 },
                { "__nv_j1f", __nv_j1f },
                { "__nv_jn", __nv_jn },
                { "__nv_jnf", __nv_jnf },
                { "__nv_ldexp", __nv_ldexp },
                { "__nv_ldexpf", __nv_ldexpf },
                { "__nv_lgamma", __nv_lgamma },
                { "__nv_lgammaf", __nv_lgammaf },
                { "__nv_ll2double_rd", __nv_ll2double_rd },
                { "__nv_ll2double_rn", __nv_ll2double_rn },
                { "__nv_ll2double_ru", __nv_ll2double_ru },
                { "__nv_ll2double_rz", __nv_ll2double_rz },
                { "__nv_ll2float_rd", __nv_ll2float_rd },
                { "__nv_ll2float_rn", __nv_ll2float_rn },
                { "__nv_ll2float_ru", __nv_ll2float_ru },
                { "__nv_ll2float_rz", __nv_ll2float_rz },
                { "__nv_llabs", __nv_llabs },
                { "__nv_llmax", __nv_llmax },
                { "__nv_llmin", __nv_llmin },
                { "__nv_llrint", __nv_llrint },
                { "__nv_llrintf", __nv_llrintf },
                { "__nv_llround", __nv_llround },
                { "__nv_llroundf", __nv_llroundf },
                { "__nv_log", __nv_log },
                { "__nv_log10", __nv_log10 },
                { "__nv_log10f", __nv_log10f },
                { "__nv_log1p", __nv_log1p },
                { "__nv_log1pf", __nv_log1pf },
                { "__nv_log2", __nv_log2 },
                { "__nv_log2f", __nv_log2f },
                { "__nv_logb", __nv_logb },
                { "__nv_logbf", __nv_logbf },
                { "__nv_logf", __nv_logf },
                { "__nv_longlong_as_double", __nv_longlong_as_double },
                { "__nv_max", __nv_max },
                { "__nv_min", __nv_min },
                { "__nv_modf", __nv_modf },
                { "__nv_modff", __nv_modff },
                { "__nv_mul24", __nv_mul24 },
                { "__nv_mul64hi", __nv_mul64hi },
                { "__nv_mulhi", __nv_mulhi },
                { "__nv_nan", __nv_nan },
                { "__nv_nanf", __nv_nanf },
                { "__nv_nearbyint", __nv_nearbyint },
                { "__nv_nearbyintf", __nv_nearbyintf },
                { "__nv_nextafter", __nv_nextafter },
                { "__nv_nextafterf", __nv_nextafterf },
                { "__nv_normcdf", __nv_normcdf },
                { "__nv_normcdff", __nv_normcdff },
                { "__nv_normcdfinv", __nv_normcdfinv },
                { "__nv_normcdfinvf", __nv_normcdfinvf },
                { "__nv_popc", __nv_popc },
                { "__nv_popcll", __nv_popcll },
                { "__nv_pow", __nv_pow },
                { "__nv_powf", __nv_powf },
                { "__nv_powi", __nv_powi },
                { "__nv_powif", __nv_powif },
                { "__nv_rcbrt", __nv_rcbrt },
                { "__nv_rcbrtf", __nv_rcbrtf },
                { "__nv_remainder", __nv_remainder },
                { "__nv_remainderf", __nv_remainderf },
                { "__nv_remquo", __nv_remquo },
                { "__nv_remquof", __nv_remquof },
                { "__nv_rhadd", __nv_rhadd },
                { "__nv_rint", __nv_rint },
                { "__nv_rintf", __nv_rintf },
                { "__nv_round", __nv_round },
                { "__nv_roundf", __nv_roundf },
                { "__nv_rsqrt", __nv_rsqrt },
                { "__nv_rsqrtf", __nv_rsqrtf },
                { "__nv_sad", __nv_sad },
                { "__nv_saturatef", __nv_saturatef },
                { "__nv_scalbn", __nv_scalbn },
                { "__nv_scalbnf", __nv_scalbnf },
                { "__nv_signbitd", __nv_signbitd },
                { "__nv_signbitf", __nv_signbitf },
                { "__nv_sin", __nv_sin },
                { "__nv_sincos", __nv_sincos },
                { "__nv_sincosf", __nv_sincosf },
                { "__nv_sincospi", __nv_sincospi },
                { "__nv_sincospif", __nv_sincospif },
                { "__nv_sinf", __nv_sinf },
                { "__nv_sinh", __nv_sinh },
                { "__nv_sinhf", __nv_sinhf },
                { "__nv_sinpi", __nv_sinpi },
                { "__nv_sinpif", __nv_sinpif },
                { "__nv_sqrt", __nv_sqrt },
                { "__nv_sqrtf", __nv_sqrtf },
                { "__nv_tan", __nv_tan },
                { "__nv_tanf", __nv_tanf },
                { "__nv_tanh", __nv_tanh },
                { "__nv_tanhf", __nv_tanhf },
                { "__nv_tgamma", __nv_tgamma },
                { "__nv_tgammaf", __nv_tgammaf },
                { "__nv_trunc", __nv_trunc },
                { "__nv_truncf", __nv_truncf },
                { "__nv_uhadd", __nv_uhadd },
                { "__nv_uint2double_rn", __nv_uint2double_rn },
                { "__nv_uint2float_rd", __nv_uint2float_rd },
                { "__nv_uint2float_rn", __nv_uint2float_rn },
                { "__nv_uint2float_ru", __nv_uint2float_ru },
                { "__nv_uint2float_rz", __nv_uint2float_rz },
                { "__nv_ull2double_rd", __nv_ull2double_rd },
                { "__nv_ull2double_rn", __nv_ull2double_rn },
                { "__nv_ull2double_ru", __nv_ull2double_ru },
                { "__nv_ull2double_rz", __nv_ull2double_rz },
                { "__nv_ull2float_rd", __nv_ull2float_rd },
                { "__nv_ull2float_rn", __nv_ull2float_rn },
                { "__nv_ull2float_ru", __nv_ull2float_ru },
                { "__nv_ull2float_rz", __nv_ull2float_rz },
                { "__nv_ullmax", __nv_ullmax },
                { "__nv_ullmin", __nv_ullmin },
                { "__nv_umax", __nv_umax },
                { "__nv_umin", __nv_umin },
                { "__nv_umul24", __nv_umul24 },
                { "__nv_umul64hi", __nv_umul64hi },
                { "__nv_umulhi", __nv_umulhi },
                { "__nv_urhadd", __nv_urhadd },
                { "__nv_usad", __nv_usad },
                { "__nv_y0", __nv_y0 },
                { "__nv_y0f", __nv_y0f },
                { "__nv_y1", __nv_y1 },
                { "__nv_y1f", __nv_y1f },
                { "__nv_yn", __nv_yn },
                { "__nv_ynf", __nv_ynf },
            };

        /// <summary>
        /// Generates an NVVM module for the Cuda LibDevice functions (if any).
        /// </summary>
        /// <param name="majorIR">The NVVM IR major version.</param>
        /// <param name="methods">The methods to check.</param>
        /// <returns>The NVVM module, or an empty string.</returns>
        public static string GenerateNvvm(int majorIR, IEnumerable<Method> methods)
        {
            var builder = new StringBuilder();
            bool addPrefix = true;
            // Each fragment ONCE: a LibDevice function used by a kernel and by a [NoInlining] helper appears as two
            // methods, and a second `define` of the same wrapper made NVVM reject the whole module, which the PTX
            // backend then dropped (5.4.19 sweep: "Unknown symbol '__ilgpu__nv_ceil'").
            var emitted = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var method in methods)
            {
                if (method.HasSource &&
                    fragments.TryGetValue(method.Source.Name, out var methodNvvm) &&
                    emitted.Add(method.Source.Name))
                {
                    if (addPrefix)
                    {
                        builder.AppendLine(string.Format(irVersionFormat, majorIR));
                        builder.AppendLine(prefix);
                        addPrefix = false;
                    }

                    builder.AppendLine(methodNvvm);
                }
            }

            return builder.ToString();
        }
    }
}
