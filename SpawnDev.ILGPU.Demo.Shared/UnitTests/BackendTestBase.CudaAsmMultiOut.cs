using System;
using System.Reflection;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Backends;
using ILGPU.Backends.EntryPoints;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // CudaAsm.Emit with several OUTPUT operands (2026-10-17). Before: Emit could return one value, and an instruction with
    // several result registers (mma.sync writes four) had to go through EmitRef, which round-trips every operand through a
    // local-memory slot. After: Emit(asm, out T0, out T1, ..., T_k ...) keeps operands in registers, and the PTX backend binds
    // an `out` local that is used only as an emit output to the register the statement wrote (no st.local / ld.local).
    // CUDA only: inline PTX has no meaning elsewhere.
    public abstract partial class BackendTestBase
    {
        static void CudaAsmFourOutKernel(Index1D i, ArrayView<uint> a, ArrayView<uint> b, ArrayView<uint> o)
        {
            uint x = a[i], y = b[i];
            CudaAsm.Emit(
                "{\n\t" +
                "   add.u32 %0, %4, %5;\n\t" +
                "   sub.u32 %1, %4, %5;\n\t" +
                "   mul.lo.u32 %2, %4, %5;\n\t" +
                "   xor.b32 %3, %4, %5;\n\t" +
                "}",
                out uint sum, out uint diff, out uint prod, out uint xr, x, y);
            o[i * 4] = sum;
            o[i * 4 + 1] = diff;
            o[i * 4 + 2] = prod;
            o[i * 4 + 3] = xr;
        }

        // two outputs where one is carried across loop iterations (the register must not alias the previous iteration's value)
        static void CudaAsmLoopCarriedKernel(Index1D i, ArrayView<uint> a, ArrayView<uint> o)
        {
            uint acc = 0, last = 0;
            for (int k = 0; k < 8; k++)
            {
                uint v = a[i] + (uint)k;
                CudaAsm.Emit("add.u32 %0, %2, %3; mul.lo.u32 %1, %2, %3;", out uint s, out uint m, acc, v);
                acc = s;
                last = m;
            }
            o[i * 2] = acc;
            o[i * 2 + 1] = last;
        }

        [TestMethod]
        public async Task CudaAsm_FourOutputs_ComputeInRegisters() => await RunTest(async accelerator =>
        {
            if (accelerator is not CudaAccelerator cuda)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: inline PTX is CUDA-only");
            const int n = 1024;
            var a = new uint[n]; var b = new uint[n];
            for (int i = 0; i < n; i++) { a[i] = (uint)(i * 2654435761u); b[i] = (uint)(i * 40503u + 7); }
            using var ab = accelerator.Allocate1D(a); using var bb = accelerator.Allocate1D(b);
            using var ob = accelerator.Allocate1D<uint>(n * 4);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>, ArrayView<uint>>(CudaAsmFourOutKernel);
            kernel((Index1D)n, ab.View, bb.View, ob.View);
            await accelerator.SynchronizeAsync();
            var got = await ob.CopyToHostAsync<uint>();
            for (int i = 0; i < n; i++)
            {
                uint x = a[i], y = b[i];
                if (got[i * 4] != unchecked(x + y) || got[i * 4 + 1] != unchecked(x - y) || got[i * 4 + 2] != unchecked(x * y) || got[i * 4 + 3] != (x ^ y))
                    throw new Exception($"CudaAsm 4-output mismatch at [{i}]: got {got[i * 4]},{got[i * 4 + 1]},{got[i * 4 + 2]},{got[i * 4 + 3]}");
            }
            // the outputs must live in registers: no local-memory traffic in the generated PTX
            var method = typeof(BackendTestBase).GetMethod(nameof(CudaAsmFourOutKernel), BindingFlags.Static | BindingFlags.NonPublic)!;
            var entry = EntryPointDescription.FromImplicitlyGroupedKernel(method);
            var ptx = ((global::ILGPU.Backends.PTX.PTXCompiledKernel)cuda.Backend.Compile(entry, default)).PTXAssembly;
            if (ptx.Contains("st.local") || ptx.Contains("ld.local"))
                throw new Exception("inline PTX outputs were stored to local memory (st.local/ld.local in the PTX)");
        });

        [TestMethod]
        public async Task CudaAsm_LoopCarriedOutputs_KeepTheirValues() => await RunTest(async accelerator =>
        {
            if (accelerator is not CudaAccelerator)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: inline PTX is CUDA-only");
            const int n = 512;
            var a = new uint[n];
            for (int i = 0; i < n; i++) a[i] = (uint)(i * 97 + 3);
            using var ab = accelerator.Allocate1D(a);
            using var ob = accelerator.Allocate1D<uint>(n * 2);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(CudaAsmLoopCarriedKernel);
            kernel((Index1D)n, ab.View, ob.View);
            await accelerator.SynchronizeAsync();
            var got = await ob.CopyToHostAsync<uint>();
            for (int i = 0; i < n; i++)
            {
                uint acc = 0, last = 0;
                for (int k = 0; k < 8; k++) { uint v = a[i] + (uint)k; uint s = unchecked(acc + v), m = unchecked(acc * v); acc = s; last = m; }
                if (got[i * 2] != acc || got[i * 2 + 1] != last)
                    throw new Exception($"loop-carried emit outputs wrong at [{i}]: expected {acc},{last} got {got[i * 2]},{got[i * 2 + 1]}");
            }
        });
    }
}
