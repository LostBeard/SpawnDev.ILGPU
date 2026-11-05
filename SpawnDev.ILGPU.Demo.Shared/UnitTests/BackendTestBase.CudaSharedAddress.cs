using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Unsafe.AsPointer in kernels (2026-11-05): the address a reference holds, as an integer, so inline PTX that ADDRESSES memory can take it:
    // ld.shared / ldmatrix / cp.async need a shared-state-space address operand, which CudaAsm could not express (its ref-parameter mode loads
    // the value, not the address). On CUDA a SharedMemory allocation's pointer is already a shared-space address register, so
    // (ulong)Unsafe.AsPointer(ref sh[i]) is exactly that operand. CUDA only.
    public abstract partial class BackendTestBase
    {
        // each thread reads its NEIGHBOUR's shared slot through inline ld.shared with the address from Unsafe.AsPointer
        static unsafe void CudaSharedAddressLdKernel(ArrayView<uint> o)
        {
            var sh = SharedMemory.Allocate<uint>(256);
            int t = Group.IdxX;
            sh[t] = (uint)(t * 7 + 3);
            Group.Barrier();
            ulong addr = (ulong)Unsafe.AsPointer(ref sh[(t + 1) & 255]);
            CudaAsm.Emit("ld.shared.u32 %0, [%1];", out uint v, addr);
            o[Grid.IdxX * 256 + t] = v;
        }

        // ldmatrix.x1: the 8 x 8 b16 matrix whose row r starts at the address lane r provides; lane l receives row l / 4, columns 2 (l % 4) .. +1
        static unsafe void CudaSharedAddressLdmatrixKernel(ArrayView<uint> o)
        {
            var sh = SharedMemory.Allocate<uint>(8 * 4);     // 8 rows x 8 halves = 8 x 4 words
            int lane = Group.IdxX;
            if (lane < 32) sh[lane] = (uint)lane * 0x00020002u + 0x00010000u;   // halves (2 lane, 2 lane + 1)
            Group.Barrier();
            ulong addr = (ulong)Unsafe.AsPointer(ref sh[(lane & 7) * 4]);
            CudaAsm.Emit("ldmatrix.sync.aligned.m8n8.x1.shared.b16 {%0}, [%1];", out uint r0, addr);
            o[lane] = r0;
        }

        [TestMethod]
        public async Task CudaAsm_SharedAddress_FromAsPointer() => await RunTest(async accelerator =>
        {
            if (accelerator is not CudaAccelerator cuda)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: inline PTX is CUDA-only");
            using var ob = accelerator.Allocate1D<uint>(2 * 256);
            accelerator.LoadStreamKernel<ArrayView<uint>>(CudaSharedAddressLdKernel)(new KernelConfig(2, 256), ob.View);
            var got = await ob.CopyToHostAsync();
            for (int g = 0; g < 2; g++)
                for (int t = 0; t < 256; t++)
                {
                    uint want = (uint)(((t + 1) & 255) * 7 + 3);
                    if (got[g * 256 + t] != want) throw new Exception($"ld.shared through Unsafe.AsPointer: group {g} thread {t} read {got[g * 256 + t]}, expected {want}");
                }
            string ldm = "ldmatrix: needs sm_75";
            if (cuda.Architecture is { } arch && arch.Major * 10 + arch.Minor >= 75)
            {
                using var mb = accelerator.Allocate1D<uint>(32);
                accelerator.LoadStreamKernel<ArrayView<uint>>(CudaSharedAddressLdmatrixKernel)(new KernelConfig(1, 32), mb.View);
                var m = await mb.CopyToHostAsync();
                for (int l = 0; l < 32; l++)
                {
                    // row l / 4, columns 2 (l % 4), 2 (l % 4) + 1; the matrix value at (row, col) is the half index row * 8 + col
                    int row = l / 4, col = 2 * (l % 4);
                    uint want = (uint)(row * 8 + col) | ((uint)(row * 8 + col + 1) << 16);
                    if (m[l] != want) throw new Exception($"ldmatrix through Unsafe.AsPointer: lane {l} got 0x{m[l]:X8}, expected 0x{want:X8}");
                }
                ldm = "ldmatrix.x1 fragment layout correct";
            }
            Console.WriteLine($"[CudaAsm] ld.shared via Unsafe.AsPointer correct over 2 groups; {ldm}");
        });
    }
}
