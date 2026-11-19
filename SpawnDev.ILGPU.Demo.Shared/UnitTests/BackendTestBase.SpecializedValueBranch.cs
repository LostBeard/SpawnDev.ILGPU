using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A SpecializedValue<int> kernel parameter selecting one of several code paths: every value launched must run its own
    // path with the right result, on every backend. The suite's only other SpecializedValue test (SharedBoolMemoryTest) needs
    // shared memory, so WebGL never ran a specialized kernel; SpawnDev.Codecs wants this shape to split a 9-op test kernel
    // whose cold WebGL compile takes ~28 s (each op would compile only its own path). Data 2026-11-19.
    public abstract partial class BackendTestBase
    {
        static void SpecializedBranchKernel(Index1D i, SpecializedValue<int> op, ArrayView<int> x, ArrayView<int> y)
        {
            int v = x[i];
            if (op == 0) y[i] = v * 3 + 1;
            else if (op == 1)
            {
                int acc = 0;
                for (int k = 0; k <= (v & 15); k++) acc += k * k;
                y[i] = acc;
            }
            else if (op == 2) y[i] = (v << 2) ^ 0x55;
            else y[i] = -v;
        }

        static int SpecializedBranchHost(int op, int v)
        {
            if (op == 0) return v * 3 + 1;
            if (op == 1) { int acc = 0; for (int k = 0; k <= (v & 15); k++) acc += k * k; return acc; }
            if (op == 2) return (v << 2) ^ 0x55;
            return -v;
        }

        [TestMethod]
        public async Task SpecializedValue_SelectsItsBranch_EveryValue() => await RunTest(async accelerator =>
        {
            const int n = 1024;
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = i * 37 - 5000;
            using var x = accelerator.Allocate1D(input);
            using var y = accelerator.Allocate1D<int>(n);
            int shadersBefore = (accelerator as SpawnDev.ILGPU.WebGPU.WebGPUAccelerator)?.CachedShaderCount ?? -1;
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, SpecializedValue<int>, ArrayView<int>, ArrayView<int>>(SpecializedBranchKernel);
            // every value twice, interleaved, so a launch reusing the previous value's compiled kernel would show
            foreach (int op in new[] { 0, 1, 2, 3, 2, 0, 3, 1 })
            {
                kernel(n, new SpecializedValue<int>(op), x.View, y.View);
                var got = await y.CopyToHostAsync<int>();
                for (int i = 0; i < n; i++)
                    if (got[i] != SpecializedBranchHost(op, input[i]))
                        throw new Exception($"{accelerator.AcceleratorType}: op {op}, x = {input[i]}: got {got[i]}, want {SpecializedBranchHost(op, input[i])}");
            }
            // Each value compiles its own kernel (ILGPU's SpecializationCache, Kernel.cs, shared by every accelerator): on WebGPU,
            // which can count its shaders, 4 distinct values = 4 shaders (measured 2026-11-19), so each holds only its own branch.
            if (accelerator is SpawnDev.ILGPU.WebGPU.WebGPUAccelerator webgpu && webgpu.CachedShaderCount - shadersBefore != 4)
                throw new Exception($"WebGPU compiled {webgpu.CachedShaderCount - shadersBefore} shaders for 4 specialized values; expected 4 (one per value)");
        });
    }
}
