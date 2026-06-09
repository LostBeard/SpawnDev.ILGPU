using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // WebGPU/WebGL cap each dispatch dimension at maxComputeWorkgroupsPerDimension (65535).
    // An EXPLICIT 2D-grid KernelConfig with GridDim.X > 65535 AND GridDim.Y > 1 (so the existing
    // 1D workY==1 fold doesn't apply) used to throw "Dispatch workgroup count X exceeds max compute
    // workgroups per dimension". This is the SD-Turbo 4096x4096 attention matmul crash: 16,777,216
    // output elems / 256-thread group = 65536 workgroups in X, one over the limit, dispatched as
    // (65536, 5, 1). The backend must transparently fold the X overflow into the free Z dim and
    // reconstruct Grid.IdxX / Grid.DimX, exactly like CUDA/HIP large-grid handling.
    //
    // This test forces GridDim.X = 65536 (one over) with GridDim.Y = 2 and a 1-thread group, and
    // verifies Grid.IdxX, Grid.IdxY, and Grid.DimX are ALL correct (a wrong fold silently computes
    // wrong indices, not just throws). CPU/CUDA/OpenCL/Wasm already pass; WebGPU/WebGL pass after the
    // auto-tile fix.
    public abstract partial class BackendTestBase
    {
        const int GOFgx = 65536;  // grid dim X (groups) — ONE OVER the 65535 WebGPU/WebGL limit
        const int GOFgy = 2;      // grid dim Y (groups) — > 1, so the 1D (workY==1) fold path is skipped
        const int GOFk = 7;

        static void Grid2DOverflowKernel(ArrayView<int> output)
        {
            int gx = Grid.IdxX;
            int gy = Grid.IdxY;
            // Index uses Grid.DimX (must be the LOGICAL 65536 after the fold, not the physical num_workgroups.x).
            output[gy * Grid.DimX + gx] = gx * GOFk + gy;
        }

        [TestMethod(Timeout = 120000)]
        public async Task Grid2D_ExceedsMaxWorkgroupsX_FoldsCorrectly() => await RunTest(async accelerator =>
        {
            // WebGL has no shared-memory/atomics but this kernel uses neither; it does need explicit
            // grouped dispatch though. If a backend genuinely can't do an explicit 1-thread 2D grid,
            // skip — but CPU/CUDA/OpenCL/Wasm/WebGPU all can.
            int n = GOFgx * GOFgy;
            using var output = accelerator.Allocate1D<int>(n);

            var kern = accelerator.LoadStreamKernel<ArrayView<int>>(Grid2DOverflowKernel);
            kern(new KernelConfig(new Index2D(GOFgx, GOFgy), new Index2D(1, 1)), output.View);
            await accelerator.SynchronizeAsync();

            var r = await output.CopyToHostAsync<int>();
            int errors = 0, firstBadCell = -1, firstBadGot = 0, firstBadExp = 0;
            for (int gy = 0; gy < GOFgy; gy++)
                for (int gx = 0; gx < GOFgx; gx++)
                {
                    int cell = gy * GOFgx + gx;
                    int expected = gx * GOFk + gy;
                    if (r[cell] != expected)
                    {
                        if (errors == 0) { firstBadCell = cell; firstBadGot = r[cell]; firstBadExp = expected; }
                        errors++;
                    }
                }
            if (errors > 0)
                throw new Exception(
                    $"Grid2D overflow fold WRONG: {errors}/{n} cells incorrect. First @cell {firstBadCell} " +
                    $"(gx={firstBadCell % GOFgx}, gy={firstBadCell / GOFgx}): got {firstBadGot}, expected {firstBadExp}. " +
                    $"GridDim.X={GOFgx} (>65535) must fold X-overflow into Z with Grid.IdxX/Grid.DimX reconstructed.");
        });
    }
}
