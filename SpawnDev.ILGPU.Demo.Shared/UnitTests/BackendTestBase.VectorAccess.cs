using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Backends;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.OpenCL;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // OpenCL vector loads / stores (5.4.15-local.3): a global load or store of a structure of four (two) 32-bit fields whose address the
    // pointer alignment analysis proves aligned to its size is one float4 / int4 access, as PTX's ld.v4 / st.v4 from the same analysis.
    // Before, the OpenCL backend dereferenced the structure pointer (four 32-bit accesses): SpawnDev.ILGPU.ML's Q4_K decode GEMV gained
    // ~3x from 128-bit loads on CUDA and ~10% on OpenCL (Tuvok 2026-11-02). Never decided by the type alone: an unaligned vector
    // access is undefined in OpenCL C, so a 16-byte structure at a 4-byte-aligned offset must stay scalar.
    public abstract partial class BackendTestBase
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct VecAccessI4 { public int A, B, C, D; }

        [StructLayout(LayoutKind.Sequential)]
        public struct VecAccessF2 { public float A, B; }

        // a 16-byte structure at byte offset 4 of a 20-byte element: its address is only 4-byte aligned
        [StructLayout(LayoutKind.Sequential)]
        public struct VecAccessPadded { public float Pad; public AsAlignedF4 V; }

        static void VectorAccessAlignedKernel(Index1D i, ArrayView<AsAlignedF4> f, ArrayView<VecAccessI4> n, ArrayView<VecAccessF2> h,
            ArrayView<AsAlignedF4> fo, ArrayView<VecAccessI4> no, ArrayView<VecAccessF2> ho)
        {
            var a = f.AsAligned16()[i];
            var b = n.AsAligned16()[i];
            var c = h[i];
            fo.AsAligned16()[i] = new AsAlignedF4 { A = a.D, B = a.C + b.A, C = a.B * 2f, D = a.A - c.B };
            no.AsAligned16()[i] = new VecAccessI4 { A = b.D, B = b.C ^ 0x5A5A, C = b.B + 7, D = b.A * 3 };
            ho[i] = new VecAccessF2 { A = c.B, B = c.A + a.A };
        }

        // the misaligned load (field V of an element), stored as a whole element (WebGL takes whole-element stores only)
        static void VectorAccessPaddedKernel(Index1D i, ArrayView<VecAccessPadded> p, ArrayView<VecAccessPadded> po)
        {
            var v = p[i].V;
            po[i] = new VecAccessPadded { Pad = p[i].Pad + 1f, V = new AsAlignedF4 { A = v.D, B = v.C, C = v.B, D = v.A + p[i].Pad } };
        }

        // the misaligned store: field V alone, through its 4-byte-aligned address (not WebGL: no single-field stores there)
        static void VectorAccessPaddedFieldStoreKernel(Index1D i, ArrayView<VecAccessPadded> p, ArrayView<VecAccessPadded> po)
        {
            var v = p[i].V;
            po[i].V = new AsAlignedF4 { A = v.D, B = v.C, C = v.B, D = v.A + p[i].Pad };
        }

        // the OpenCL C ILGPU generates for an implicitly grouped kernel (null on other backends)
        static string? OpenCLSourceOf(Accelerator accelerator, Delegate kernel)
        {
            if (accelerator.AcceleratorType != AcceleratorType.OpenCL) return null;
            var backend = (Backend)typeof(Accelerator).GetProperty("Backend", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(accelerator)!;
            var compiled = backend.Compile(EntryPointDescription.FromImplicitlyGroupedKernel(kernel.Method), KernelSpecialization.Empty);
            return ((CLCompiledKernel)compiled).Source;
        }

        /// <summary>
        /// Aligned global loads and stores of float4 / int4 / float2 structures (through AsAligned16 and plain element views) read and write
        /// the right values on every backend, and on OpenCL they are vector accesses (global float4* / int4* / float2* in the generated C).
        /// </summary>
        [TestMethod]
        public async Task VectorAccess_AlignedStructs_LoadStore_Correct_AndVectorOnOpenCL() => await RunTest(async accelerator =>
        {
            const int n = 1000;
            var f = new AsAlignedF4[n]; var q = new VecAccessI4[n]; var h = new VecAccessF2[n];
            for (int i = 0; i < n; i++)
            {
                f[i] = new AsAlignedF4 { A = i * 0.5f, B = i + 1f, C = -i * 0.25f, D = 3f + i };
                q[i] = new VecAccessI4 { A = i, B = -i * 3, C = i * 7 + 1, D = 1 << (i % 30) };
                h[i] = new VecAccessF2 { A = i * 1.5f, B = -i };
            }
            using var fb = accelerator.Allocate1D(f); using var qb = accelerator.Allocate1D(q); using var hb = accelerator.Allocate1D(h);
            using var fo = accelerator.Allocate1D<AsAlignedF4>(n); using var qo = accelerator.Allocate1D<VecAccessI4>(n); using var ho = accelerator.Allocate1D<VecAccessF2>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<AsAlignedF4>, ArrayView<VecAccessI4>, ArrayView<VecAccessF2>,
                ArrayView<AsAlignedF4>, ArrayView<VecAccessI4>, ArrayView<VecAccessF2>>(VectorAccessAlignedKernel);
            kernel((Index1D)n, fb.View, qb.View, hb.View, fo.View, qo.View, ho.View);
            await accelerator.SynchronizeAsync();
            var gf = await fo.CopyToHostAsync<AsAlignedF4>(); var gq = await qo.CopyToHostAsync<VecAccessI4>(); var gh = await ho.CopyToHostAsync<VecAccessF2>();
            for (int i = 0; i < n; i++)
            {
                var a = f[i]; var b = q[i]; var c = h[i];
                var ef = new AsAlignedF4 { A = a.D, B = a.C + b.A, C = a.B * 2f, D = a.A - c.B };
                var eq = new VecAccessI4 { A = b.D, B = b.C ^ 0x5A5A, C = b.B + 7, D = b.A * 3 };
                var eh = new VecAccessF2 { A = c.B, B = c.A + a.A };
                if (!gf[i].Equals(ef) || !gq[i].Equals(eq) || !gh[i].Equals(eh))
                    throw new Exception($"[{i}]: float4 ({gf[i].A},{gf[i].B},{gf[i].C},{gf[i].D}) want ({ef.A},{ef.B},{ef.C},{ef.D}); int4 ({gq[i].A},{gq[i].B},{gq[i].C},{gq[i].D}) " +
                        $"want ({eq.A},{eq.B},{eq.C},{eq.D}); float2 ({gh[i].A},{gh[i].B}) want ({eh.A},{eh.B})");
            }
            var src = OpenCLSourceOf(accelerator, (Action<Index1D, ArrayView<AsAlignedF4>, ArrayView<VecAccessI4>, ArrayView<VecAccessF2>,
                ArrayView<AsAlignedF4>, ArrayView<VecAccessI4>, ArrayView<VecAccessF2>>)VectorAccessAlignedKernel);
            if (src != null)
                foreach (var v in new[] { "global float4*", "global int4*", "global float2*" })
                    if (!src.Contains(v)) throw new Exception($"OpenCL: aligned accesses were not vectorized: no '{v}' in the generated kernel");
        });

        /// <summary>
        /// A 16-byte structure at a 4-byte-aligned address (field V at offset 4 of a 20-byte element) is read and written field by field on
        /// every backend; on OpenCL the generated kernel has no vector access (an unaligned float4 dereference is undefined in OpenCL C).
        /// </summary>
        [TestMethod]
        public async Task VectorAccess_MisalignedStruct_StaysScalar_AndCorrect() => await RunTest(async accelerator =>
        {
            const int n = 777;
            var p = new VecAccessPadded[n];
            for (int i = 0; i < n; i++) p[i] = new VecAccessPadded { Pad = i * 0.125f, V = new AsAlignedF4 { A = i, B = -i * 2f, C = i * 0.5f, D = 9f - i } };
            using var pb = accelerator.Allocate1D(p);
            var kernels = new List<(string Name, Action<Index1D, ArrayView<VecAccessPadded>, ArrayView<VecAccessPadded>> Kernel, bool FieldStore)> { ("whole-element store", VectorAccessPaddedKernel, false) };
            if (accelerator.AcceleratorType != AcceleratorType.WebGL) kernels.Add(("field store", VectorAccessPaddedFieldStoreKernel, true));
            foreach (var (name, fn, fieldStore) in kernels)
            {
                using var po = accelerator.Allocate1D<VecAccessPadded>(n);
                po.MemSetToZero();
                var kernel = accelerator.LoadAutoGroupedStreamKernel(fn);
                kernel((Index1D)n, pb.View, po.View);
                await accelerator.SynchronizeAsync();
                var got = await po.CopyToHostAsync<VecAccessPadded>();
                for (int i = 0; i < n; i++)
                {
                    var v = p[i].V;
                    var e = new VecAccessPadded { Pad = fieldStore ? 0f : p[i].Pad + 1f, V = new AsAlignedF4 { A = v.D, B = v.C, C = v.B, D = v.A + p[i].Pad } };
                    if (!got[i].Equals(e))
                        throw new Exception($"{name} [{i}]: got Pad {got[i].Pad} V ({got[i].V.A},{got[i].V.B},{got[i].V.C},{got[i].V.D}), want Pad {e.Pad} V ({e.V.A},{e.V.B},{e.V.C},{e.V.D})");
                }
                var src = OpenCLSourceOf(accelerator, fn);
                if (src != null && (src.Contains("float4*") || src.Contains("int4*") || src.Contains("float2*")))
                    throw new Exception($"OpenCL ({name}): a 16-byte structure at a 4-byte-aligned address was accessed as a vector (undefined behaviour)");
            }
        });
    }
}
