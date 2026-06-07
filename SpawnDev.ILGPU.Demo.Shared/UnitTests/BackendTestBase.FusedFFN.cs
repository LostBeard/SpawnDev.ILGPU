using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Fusion prep (Geordi+Tuvok decoder-block fusion): verifies the v0 fused FFN kernel
    // (MatMul + bias-Add + GELU in ONE dispatch) transpiles + is correct on all 6 backends BEFORE it's
    // wired into the GraphExecutor. Key unknown it closes: does XMath.Tanh lower on WGSL/GLSL/Wasm (the
    // GELU tanh-approx)? Compared against a CPU reference (MathF.Tanh).
    public abstract partial class BackendTestBase
    {
        // Y[m,n] = act( sum_k X[m,k]*W[k,n] + B[n] ).  activation: 1 = GELU (tanh approx).
        static void FusedLinearActivation(
            Index1D idx,
            ArrayView<float> X, ArrayView<float> W, ArrayView<float> B, ArrayView<float> Y,
            int M, int K, int N, int activation)
        {
            if (idx >= M * N) return;
            int m = idx / N, n = idx % N;

            float acc = 0f;
            int xRow = m * K;
            for (int k = 0; k < K; k++)
                acc += X[xRow + k] * W[k * N + n];
            acc += B[n];

            float outv = acc;
            if (activation == 1)
            {
                const float c = 0.7978845608f; // sqrt(2/pi)
                float inner = c * (acc + 0.044715f * acc * acc * acc);
                outv = 0.5f * acc * (1f + XMath.Tanh(inner));
            }
            Y[m * N + n] = outv;
        }

        [TestMethod]
        public async Task FusedFFN_LinearGELU_Transpiles() => await RunTest(async accelerator =>
        {
            const int M = 4, K = 8, N = 4;
            var X = new float[M * K];
            var W = new float[K * N];
            var B = new float[N];
            var rng = new Random(11);
            for (int i = 0; i < X.Length; i++) X[i] = (float)(rng.NextDouble() - 0.5);
            for (int i = 0; i < W.Length; i++) W[i] = (float)(rng.NextDouble() - 0.5);
            for (int i = 0; i < B.Length; i++) B[i] = (float)(rng.NextDouble() - 0.5);

            using var xBuf = accelerator.Allocate1D(X);
            using var wBuf = accelerator.Allocate1D(W);
            using var bBuf = accelerator.Allocate1D(B);
            using var yBuf = accelerator.Allocate1D<float>(M * N);

            var kern = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>,
                int, int, int, int>(FusedLinearActivation);
            kern((Index1D)(M * N), xBuf.View, wBuf.View, bBuf.View, yBuf.View, M, K, N, 1);
            await accelerator.SynchronizeAsync();

            var got = await yBuf.CopyToHostAsync<float>();
            for (int m = 0; m < M; m++)
                for (int n = 0; n < N; n++)
                {
                    float acc = 0f;
                    for (int k = 0; k < K; k++) acc += X[m * K + k] * W[k * N + n];
                    acc += B[n];
                    float inner = 0.7978845608f * (acc + 0.044715f * acc * acc * acc);
                    float expected = 0.5f * acc * (1f + MathF.Tanh(inner));
                    float actual = got[m * N + n];
                    if (MathF.Abs(actual - expected) > 0.01f * MathF.Abs(expected) + 0.002f)
                        throw new Exception(
                            $"FusedFFN wrong at [{m},{n}]: expected {expected} got {actual} (XMath.Tanh mislowered or fusion wrong?).");
                }
        });

        // --- v1: TILED shared-memory fused FFN (the real perf win) ---
        // Tiled GEMM with shared-memory blocking, fused bias-Add + GELU, ONE dispatch.
        // This is the kernel the decoder-block fusion (43% text-gen lever) actually ships -
        // v0 above is the per-element correctness/transpile baseline; this is the performant form.
        //
        // 1D grid + 1D group (TILE*TILE=256 threads), manual 2D index derivation - IDENTICAL layout to
        // the production SpawnDev.ILGPU.ML MatMulKernel.TiledMatMulImpl (proven on WebGPU/Wasm/CUDA/OpenCL).
        // A 2D group instead traps the Wasm dispatcher ("remainder by zero" in DispatchToWorkers - a latent
        // 2D-group ILGPU bug, tracked separately) and 2D index mapping has historically been buggy on WebGPU;
        // the production code uses 1D for exactly these reasons. CPU (64-thread cap) + WebGL (no shared mem)
        // can't launch a 256-thread tiled group - they are capability-gated in the test bodies / WebGL override.
        // Barriers sit in UNIFORM control flow (loop bound numKTiles identical for every thread) - WebGPU-safe.
        // Out-of-range threads still load zeros + hit every barrier; they only skip the final store.
        const int FusedTile = 16;

        static void FusedLinearActivationTiled(
            ArrayView<float> X, ArrayView<float> W, ArrayView<float> B, ArrayView<float> Y,
            int M, int K, int N, int numTilesN, int activation)
        {
            const int TILE = 16; // must equal FusedTile (const for SharedMemory.Allocate + group size)
            var As = SharedMemory.Allocate<float>(TILE * TILE);
            var Ws = SharedMemory.Allocate<float>(TILE * TILE);

            int tileIdx = Grid.IdxX;             // 1D grid -> 2D tile index
            int tileRow = tileIdx / numTilesN;
            int tileCol = tileIdx % numTilesN;

            int localIdx = Group.IdxX;           // 1D group (256) -> 2D local index
            int tx = localIdx / TILE;            // row within tile (0..TILE-1)
            int ty = localIdx % TILE;            // col within tile (0..TILE-1)

            int row = tileRow * TILE + tx;       // global output row
            int col = tileCol * TILE + ty;       // global output column
            int txT = tx * TILE;

            float acc = 0f;
            int numKTiles = (K + TILE - 1) / TILE;
            for (int t = 0; t < numKTiles; t++)
            {
                int aCol = t * TILE + ty;
                As[txT + ty] = (row < M && aCol < K) ? X[row * K + aCol] : 0f;
                int bRow = t * TILE + tx;
                Ws[txT + ty] = (bRow < K && col < N) ? W[bRow * N + col] : 0f;
                Group.Barrier();

                for (int k = 0; k < TILE; k++)
                    acc += As[txT + k] * Ws[k * TILE + ty];
                Group.Barrier();
            }

            if (row < M && col < N)
            {
                acc += B[col];
                float outv = acc;
                if (activation == 1)
                {
                    // GELU, tanh approximation
                    const float c = 0.7978845608f; // sqrt(2/pi)
                    float inner = c * (acc + 0.044715f * acc * acc * acc);
                    outv = 0.5f * acc * (1f + XMath.Tanh(inner));
                }
                else if (activation == 2)
                {
                    // GELU, erf form (PyTorch nn.GELU default / ONNX Erf subgraph).
                    // Bit-faithful to SpawnDev.ILGPU.ML ElementWiseKernels.GELUInPlaceImpl
                    // (Abramowitz & Stegun 5-term erf, max error 1.5e-7) so the fused kernel
                    // is a drop-in replacement for the graph's MatMul->Add->Erf-Gelu subgraph
                    // WITHOUT shifting the GPT-2==ORT argmax.
                    float x = acc;
                    if (x > 10f) outv = x;
                    else if (x < -10f) outv = 0f;
                    else
                    {
                        const float INV_SQRT2 = 0.7071067811865475f;
                        float z = x * INV_SQRT2;
                        float az = z < 0f ? -z : z;
                        const float p = 0.3275911f;
                        const float a1 = 0.254829592f, a2 = -0.284496736f,
                                    a3 = 1.421413741f, a4 = -1.453152027f, a5 = 1.061405429f;
                        float t = 1f / (1f + p * az);
                        float t2 = t * t, t3 = t2 * t, t4 = t3 * t, t5 = t4 * t;
                        float erfAbs = 1f - (a1 * t + a2 * t2 + a3 * t3 + a4 * t4 + a5 * t5) * XMath.Exp(-az * az);
                        float erf = z < 0f ? -erfAbs : erfAbs;
                        outv = 0.5f * x * (1f + erf);
                    }
                }
                Y[row * N + col] = outv;
            }
        }

        // CPU reference for the erf-GELU path (mirrors SpawnDev.ILGPU.ML GELUInPlaceImpl exactly).
        static float ErfGeluRef(float x)
        {
            if (x > 10f) return x;
            if (x < -10f) return 0f;
            const float INV_SQRT2 = 0.7071067811865475f;
            float z = x * INV_SQRT2;
            float az = MathF.Abs(z);
            const float p = 0.3275911f;
            const float a1 = 0.254829592f, a2 = -0.284496736f,
                        a3 = 1.421413741f, a4 = -1.453152027f, a5 = 1.061405429f;
            float t = 1f / (1f + p * az);
            float t2 = t * t, t3 = t2 * t, t4 = t3 * t, t5 = t4 * t;
            float erfAbs = 1f - (a1 * t + a2 * t2 + a3 * t3 + a4 * t4 + a5 * t5) * MathF.Exp(-az * az);
            float erf = z < 0f ? -erfAbs : erfAbs;
            return 0.5f * x * (1f + erf);
        }

        [TestMethod]
        public async Task FusedFFN_TiledLinearGELU_Correct() => await RunTest(async accelerator =>
        {
            // Capability gate: the tiled kernel needs a FusedTile*FusedTile (256) thread group.
            // CPU (64-thread cap) and any backend with a smaller group cap can't launch it - skip there
            // (production uses a simple non-tiled fused fallback on those backends).
            if (accelerator.MaxNumThreadsPerGroup < FusedTile * FusedTile)
                throw new UnsupportedTestException(
                    $"{accelerator.AcceleratorType}: tiled FFN needs a {FusedTile * FusedTile}-thread group; " +
                    $"MaxNumThreadsPerGroup={accelerator.MaxNumThreadsPerGroup}");

            // Non-tile-multiple dims to exercise the partial-tile bounds guards.
            const int M = 70, K = 130, N = 100;
            var X = new float[M * K];
            var W = new float[K * N];
            var B = new float[N];
            var rng = new Random(23);
            for (int i = 0; i < X.Length; i++) X[i] = (float)(rng.NextDouble() - 0.5);
            for (int i = 0; i < W.Length; i++) W[i] = (float)(rng.NextDouble() - 0.5);
            for (int i = 0; i < B.Length; i++) B[i] = (float)(rng.NextDouble() - 0.5);

            using var xBuf = accelerator.Allocate1D(X);
            using var wBuf = accelerator.Allocate1D(W);
            using var bBuf = accelerator.Allocate1D(B);
            using var yBuf = accelerator.Allocate1D<float>(M * N);

            int numTilesN = (N + FusedTile - 1) / FusedTile;
            int numTilesM = (M + FusedTile - 1) / FusedTile;
            int totalTiles = numTilesM * numTilesN;

            var kern = accelerator.LoadStreamKernel<
                ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>,
                int, int, int, int, int>(FusedLinearActivationTiled);
            kern(new KernelConfig(totalTiles, FusedTile * FusedTile),
                xBuf.View, wBuf.View, bBuf.View, yBuf.View, M, K, N, numTilesN, 1);
            await accelerator.SynchronizeAsync();

            var got = await yBuf.CopyToHostAsync<float>();
            float maxRel = 0f;
            for (int m = 0; m < M; m++)
                for (int n = 0; n < N; n++)
                {
                    float acc = 0f;
                    for (int k = 0; k < K; k++) acc += X[m * K + k] * W[k * N + n];
                    acc += B[n];
                    float inner = 0.7978845608f * (acc + 0.044715f * acc * acc * acc);
                    float expected = 0.5f * acc * (1f + MathF.Tanh(inner));
                    float actual = got[m * N + n];
                    float tol = 0.01f * MathF.Abs(expected) + 0.003f;
                    float err = MathF.Abs(actual - expected);
                    if (err > tol)
                        throw new Exception(
                            $"Tiled FusedFFN wrong at [{m},{n}]: expected {expected} got {actual} " +
                            $"(err {err} > tol {tol}; tiling/barrier/bounds-guard bug?).");
                    float rel = err / (MathF.Abs(expected) + 1e-6f);
                    if (rel > maxRel) maxRel = rel;
                }
        });

        [TestMethod]
        public async Task FusedFFN_TiledErfGELU_Correct() => await RunTest(async accelerator =>
        {
            // erf-GELU path (activation==2) - the GPT-2 / PyTorch-default variant the decoder
            // fusion must use to preserve the GPT-2==ORT argmax. Verifies XMath.Exp + the A&S
            // erf polynomial transpile + the fused result matches the ML-side erf reference.
            if (accelerator.MaxNumThreadsPerGroup < FusedTile * FusedTile)
                throw new UnsupportedTestException(
                    $"{accelerator.AcceleratorType}: tiled FFN needs a {FusedTile * FusedTile}-thread group; " +
                    $"MaxNumThreadsPerGroup={accelerator.MaxNumThreadsPerGroup}");

            const int M = 70, K = 130, N = 100;
            var X = new float[M * K];
            var W = new float[K * N];
            var B = new float[N];
            var rng = new Random(29);
            // Wider spread so some pre-activation values land in the GELU saturation tails (|x|>10).
            for (int i = 0; i < X.Length; i++) X[i] = (float)(rng.NextDouble() - 0.5) * 2f;
            for (int i = 0; i < W.Length; i++) W[i] = (float)(rng.NextDouble() - 0.5) * 2f;
            for (int i = 0; i < B.Length; i++) B[i] = (float)(rng.NextDouble() - 0.5);

            using var xBuf = accelerator.Allocate1D(X);
            using var wBuf = accelerator.Allocate1D(W);
            using var bBuf = accelerator.Allocate1D(B);
            using var yBuf = accelerator.Allocate1D<float>(M * N);

            int numTilesN = (N + FusedTile - 1) / FusedTile;
            int numTilesM = (M + FusedTile - 1) / FusedTile;
            int totalTiles = numTilesM * numTilesN;

            var kern = accelerator.LoadStreamKernel<
                ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>,
                int, int, int, int, int>(FusedLinearActivationTiled);
            kern(new KernelConfig(totalTiles, FusedTile * FusedTile),
                xBuf.View, wBuf.View, bBuf.View, yBuf.View, M, K, N, numTilesN, 2);
            await accelerator.SynchronizeAsync();

            var got = await yBuf.CopyToHostAsync<float>();
            for (int m = 0; m < M; m++)
                for (int n = 0; n < N; n++)
                {
                    float acc = 0f;
                    for (int k = 0; k < K; k++) acc += X[m * K + k] * W[k * N + n];
                    acc += B[n];
                    float expected = ErfGeluRef(acc);
                    float actual = got[m * N + n];
                    float tol = 0.01f * MathF.Abs(expected) + 0.003f;
                    float err = MathF.Abs(actual - expected);
                    if (err > tol)
                        throw new Exception(
                            $"Tiled erf-GELU FusedFFN wrong at [{m},{n}]: expected {expected} got {actual} " +
                            $"(err {err} > tol {tol}; XMath.Exp/erf-poly mislowered or fusion wrong?).");
                }
        });
    }
}
