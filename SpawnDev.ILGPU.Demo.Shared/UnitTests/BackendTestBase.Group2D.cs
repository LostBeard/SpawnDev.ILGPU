using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2D/3D group support: the Wasm backend historically assumed 1D groups (groupDimX == groupSize)
    // and trapped "remainder by zero" on any 2D-group launch, while CPU/CUDA/OpenCL/WebGPU handle them.
    // These tests dispatch real 2D-group kernels and verify the per-dimension Group.Idx / Grid.Idx /
    // Group.Dim / Grid.Dim decomposition against a CPU reference. A 32-thread group (8x4) stays under
    // the CPU accelerator's 64-thread cap so CPU runs them too and serves as the cross-backend oracle.
    public abstract partial class BackendTestBase
    {
        const int G2DBx = 8;  // group dim X
        const int G2DBy = 4;  // group dim Y  (8*4 = 32 threads, <= CPU 64-thread cap)
        const int G2DGx = 3;  // grid dim X (groups)
        const int G2DGy = 5;  // grid dim Y (groups)

        // No shared memory / no barriers — purely exercises the index/dimension decomposition.
        static void Group2DProbeKernel(ArrayView<int> idxOut, ArrayView<int> dimOut, int cols)
        {
            int col = Grid.IdxX * G2DBx + Group.IdxX;
            int row = Grid.IdxY * G2DBy + Group.IdxY;
            int idx = row * cols + col;
            // Encode the four indices and the four dimensions so the host can verify every one.
            idxOut[idx] = Grid.IdxX * 1000000 + Grid.IdxY * 10000 + Group.IdxX * 100 + Group.IdxY;
            dimOut[idx] = Group.DimX * 1000000 + Group.DimY * 10000 + Grid.DimX * 100 + Grid.DimY;
        }

        [TestMethod]
        public async Task Group2D_IndexDecomposition_Correct() => await RunTest(async accelerator =>
        {
            int cols = G2DGx * G2DBx;   // 24
            int rows = G2DGy * G2DBy;   // 20
            int total = rows * cols;

            using var idxBuf = accelerator.Allocate1D<int>(total);
            using var dimBuf = accelerator.Allocate1D<int>(total);

            var kern = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, int>(Group2DProbeKernel);
            kern(new KernelConfig(new Index2D(G2DGx, G2DGy), new Index2D(G2DBx, G2DBy)),
                idxBuf.View, dimBuf.View, cols);
            await accelerator.SynchronizeAsync();

            var idx = await idxBuf.CopyToHostAsync<int>();
            var dim = await dimBuf.CopyToHostAsync<int>();

            int expectedDim = G2DBx * 1000000 + G2DBy * 10000 + G2DGx * 100 + G2DGy;
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    int gridX = col / G2DBx, locX = col % G2DBx;
                    int gridY = row / G2DBy, locY = row % G2DBy;
                    int expectedIdx = gridX * 1000000 + gridY * 10000 + locX * 100 + locY;
                    int cell = row * cols + col;
                    if (idx[cell] != expectedIdx)
                        throw new Exception(
                            $"2D group index decomposition wrong at (row={row},col={col}): " +
                            $"expected {expectedIdx} (gridX={gridX},gridY={gridY},locX={locX},locY={locY}) " +
                            $"got {idx[cell]} (decoded gridX={idx[cell] / 1000000},gridY={idx[cell] / 10000 % 100}," +
                            $"locX={idx[cell] / 100 % 100},locY={idx[cell] % 100}). 2D group/grid decomposition bug.");
                    if (dim[cell] != expectedDim)
                        throw new Exception(
                            $"2D group/grid DIM wrong at (row={row},col={col}): expected {expectedDim} " +
                            $"(GroupDim {G2DBx}x{G2DBy}, GridDim {G2DGx}x{G2DGy}) got {dim[cell]}.");
                }
        });

        // 2D group WITH shared memory + barrier — exercises the barrier dispatcher path (the one that
        // trapped on the tiled fused FFN). Each thread writes its linear local id (derived from the 2D
        // Group.Idx) into shared memory; if the 2D decomposition collapsed, local ids would collide and
        // the per-group sum would be wrong. Thread (0,0) sums and writes one result per group.
        static void Group2DSharedReduceKernel(ArrayView<int> output)
        {
            var sh = SharedMemory.Allocate<int>(G2DBx * G2DBy);
            int lx = Group.IdxX, ly = Group.IdxY;
            int lid = ly * G2DBx + lx; // unique only if Group.IdxX/Y decompose correctly
            sh[lid] = lid;
            Group.Barrier();
            if (lx == 0 && ly == 0)
            {
                int n = G2DBx * G2DBy;
                int sum = 0;
                for (int i = 0; i < n; i++) sum += sh[i];
                int groupId = Grid.IdxY * Grid.DimX + Grid.IdxX;
                output[groupId] = sum;
            }
        }

        [TestMethod]
        public async Task Group2D_SharedMemBarrier_Correct() => await RunTest(async accelerator =>
        {
            int numGroups = G2DGx * G2DGy;
            using var outBuf = accelerator.Allocate1D<int>(numGroups);

            var kern = accelerator.LoadStreamKernel<ArrayView<int>>(Group2DSharedReduceKernel);
            kern(new KernelConfig(new Index2D(G2DGx, G2DGy), new Index2D(G2DBx, G2DBy)), outBuf.View);
            await accelerator.SynchronizeAsync();

            var got = await outBuf.CopyToHostAsync<int>();
            int n = G2DBx * G2DBy;
            int expected = n * (n - 1) / 2; // sum 0..n-1; wrong if 2D local ids collide
            for (int g = 0; g < numGroups; g++)
                if (got[g] != expected)
                    throw new Exception(
                        $"2D group shared-mem reduce wrong for group {g}: expected {expected} got {got[g]} " +
                        $"(collision => 2D Group.Idx decomposition or barrier-path bug).");
        });

        // --- 3D group decomposition (backs the "2D/3D groups" claim) ---
        const int G3Dx = 4, G3Dy = 2, G3Dz = 2;   // 4*2*2 = 16 threads (<= CPU 64-thread cap)
        const int G3DGx = 2, G3DGy = 3;            // 2D grid of 3D groups

        static void Group3DProbeKernel(ArrayView<int> idxOut, ArrayView<int> dimOut, int cols, int rows)
        {
            // Map (grid, group) to a unique global cell using all 3 group dims (Z folded into the row).
            int col = Grid.IdxX * G3Dx + Group.IdxX;
            int row = (Grid.IdxY * G3Dz + Group.IdxZ) * G3Dy + Group.IdxY;
            int idx = row * cols + col;
            idxOut[idx] = Group.IdxX * 1000000 + Group.IdxY * 10000 + Group.IdxZ * 100 + Grid.IdxX * 10 + Grid.IdxY;
            dimOut[idx] = Group.DimX * 1000000 + Group.DimY * 10000 + Group.DimZ * 100;
        }

        [TestMethod]
        public async Task Group3D_IndexDecomposition_Correct() => await RunTest(async accelerator =>
        {
            int cols = G3DGx * G3Dx;            // 2*4 = 8
            int rows = G3DGy * G3Dz * G3Dy;     // 3*2*2 = 12
            int total = rows * cols;

            using var idxBuf = accelerator.Allocate1D<int>(total);
            using var dimBuf = accelerator.Allocate1D<int>(total);

            var kern = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, int, int>(Group3DProbeKernel);
            kern(new KernelConfig(new Index3D(G3DGx, G3DGy, 1), new Index3D(G3Dx, G3Dy, G3Dz)),
                idxBuf.View, dimBuf.View, cols, rows);
            await accelerator.SynchronizeAsync();

            var idx = await idxBuf.CopyToHostAsync<int>();
            var dim = await dimBuf.CopyToHostAsync<int>();

            int expectedDim = G3Dx * 1000000 + G3Dy * 10000 + G3Dz * 100;
            for (int gridY = 0; gridY < G3DGy; gridY++)
                for (int gz = 0; gz < G3Dz; gz++)
                    for (int gy = 0; gy < G3Dy; gy++)
                        for (int gridX = 0; gridX < G3DGx; gridX++)
                            for (int gx = 0; gx < G3Dx; gx++)
                            {
                                int col = gridX * G3Dx + gx;
                                int row = (gridY * G3Dz + gz) * G3Dy + gy;
                                int cell = row * cols + col;
                                int expIdx = gx * 1000000 + gy * 10000 + gz * 100 + gridX * 10 + gridY;
                                if (idx[cell] != expIdx)
                                    throw new Exception(
                                        $"3D group decomposition wrong at cell({row},{col}): expected {expIdx} " +
                                        $"(gx={gx},gy={gy},gz={gz},gridX={gridX},gridY={gridY}) got {idx[cell]}.");
                                if (dim[cell] != expectedDim)
                                    throw new Exception(
                                        $"3D group DIM wrong at cell({row},{col}): expected {expectedDim} " +
                                        $"(GroupDim {G3Dx}x{G3Dy}x{G3Dz}) got {dim[cell]}.");
                            }
        });
    }
}
