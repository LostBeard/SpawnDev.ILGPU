using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // WebGPU runs an unaligned byte MemSet / copy as a small kernel, preloaded when the accelerator is created so the
    // synchronous MemSet never compiles on the page thread. The accelerator's compiled-kernel cache holds weak references,
    // so the preloaded kernels must also be HELD: a GC between creation and the first unaligned MemSet otherwise dropped
    // them and that MemSet loaded one again (SpawnDev.Codecs' warm-up guard after GC.Collect, Geordi 2026-11-11).
    public abstract partial class BackendTestBase
    {
        [TestMethod(Timeout = 120000)]
        public async Task WebGPU_PreloadedByteKernels_SurviveGC()
        {
            // NOT RunTest: a FRESH accelerator, whose byte kernels no earlier test has used
            var (context, accelerator) = await CreateAcceleratorAsync();
            try
            {
                if (accelerator is not WebGPUAccelerator)
                    throw new UnsupportedTestException($"{accelerator.AcceleratorType}: WebGPU byte kernels only");
                using var buffer = accelerator.Allocate1D<byte>(1027);
                var init = new byte[1027];
                for (int i = 0; i < init.Length; i++) init[i] = (byte)(i * 7 + 1);
                buffer.CopyFromCPU(init);
                long loaded0 = accelerator.LoadedKernelCount;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                buffer.View.SubView(1, 7).MemSetToZero();          // unaligned start and length: the byte fill kernel
                buffer.View.SubView(13, 5).CopyFrom(buffer.View.SubView(501, 5));   // unaligned copy: the byte copy kernel
                long loaded1 = accelerator.LoadedKernelCount;
                if (loaded1 != loaded0)
                    throw new Exception($"an unaligned MemSet / copy after a GC loaded {loaded1 - loaded0} kernel(s): the preloaded byte kernels were not held");
                var got = await buffer.CopyToHostAsync<byte>();
                var want = (byte[])init.Clone();
                for (int i = 1; i < 8; i++) want[i] = 0;
                for (int i = 0; i < 5; i++) want[13 + i] = init[501 + i];
                for (int i = 0; i < want.Length; i++)
                    if (got[i] != want[i]) throw new Exception($"byte {i} = {got[i]}, expected {want[i]}");
            }
            finally
            {
                try { accelerator.Dispose(); } catch { }
                try { context.Dispose(); } catch { }
            }
        }
    }
}
