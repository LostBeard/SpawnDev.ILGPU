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

        // ───────────────────────────────────────────────────────────────────────────
        //  v2: REGISTER-BLOCKED fused FFN - the true perf ceiling for the real GPT-2 FFN sizes
        //  (768->3072, 3072->768, both >= 64), which production MatMul routes to RegisterBlockedMatMul.
        //  Mirrors SpawnDev.ILGPU.ML RegisterBlockedMatMul.RegBlockedImpl (BLOCK=16, REG=4, TILE=64;
        //  each of 256 threads computes a 4x4 register block) + fused bias-Add + activation in the
        //  write-back. Same activation contract {0=linear, 1=GELU tanh, 2=GELU erf}.
        // ───────────────────────────────────────────────────────────────────────────
        const int RbBlock = 16;            // 16x16 = 256 threads
        const int RbReg = 4;               // each thread computes REG x REG = 4x4 outputs
        const int RbTile = RbBlock * RbReg; // 64x64 output tile

        // Fused bias-add + activation, shared by the register-blocked write-back (one helper, 16 call sites).
        // activation: 0=linear (bias only), 1=GELU tanh-approx, 2=GELU erf-approx (A&S, GPT-2 default).
        static float FusedActivate(float acc, float bias, int activation)
        {
            float v = acc + bias;
            if (activation == 1)
            {
                const float c = 0.7978845608f; // sqrt(2/pi)
                float inner = c * (v + 0.044715f * v * v * v);
                return 0.5f * v * (1f + XMath.Tanh(inner));
            }
            if (activation == 2)
            {
                float x = v;
                if (x > 10f) return x;
                if (x < -10f) return 0f;
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
                return 0.5f * x * (1f + erf);
            }
            return v; // activation == 0: linear
        }

        static void FusedRegBlockedLinearActivation(
            ArrayView<float> X, ArrayView<float> W, ArrayView<float> Bias, ArrayView<float> Y,
            int M, int K, int N, int numTilesN, int activation)
        {
            const int BLOCK = 16;
            const int REG = 4;
            const int TILE = BLOCK * REG; // 64
            var aTile = SharedMemory.Allocate<float>(TILE * BLOCK); // 64x16
            var bTile = SharedMemory.Allocate<float>(BLOCK * TILE); // 16x64

            int tileIdx = Grid.IdxX;
            int tileRow = tileIdx / numTilesN;
            int tileCol = tileIdx % numTilesN;

            int localIdx = Group.IdxX;
            int threadRow = localIdx / BLOCK; // 0..15
            int threadCol = localIdx % BLOCK; // 0..15

            float c00 = 0, c01 = 0, c02 = 0, c03 = 0;
            float c10 = 0, c11 = 0, c12 = 0, c13 = 0;
            float c20 = 0, c21 = 0, c22 = 0, c23 = 0;
            float c30 = 0, c31 = 0, c32 = 0, c33 = 0;

            int numKTiles = (K + BLOCK - 1) / BLOCK;
            for (int t = 0; t < numKTiles; t++)
            {
                for (int r = 0; r < REG; r++)
                {
                    int aRow = tileRow * TILE + threadRow * REG + r;
                    int aCol = t * BLOCK + threadCol;
                    int sIdx = (threadRow * REG + r) * BLOCK + threadCol;
                    aTile[sIdx] = (aRow < M && aCol < K) ? X[aRow * K + aCol] : 0f;
                }
                for (int r = 0; r < REG; r++)
                {
                    int bRow = t * BLOCK + threadRow;
                    int bCol = tileCol * TILE + threadCol * REG + r;
                    int sIdx = threadRow * TILE + threadCol * REG + r;
                    bTile[sIdx] = (bRow < K && bCol < N) ? W[bRow * N + bCol] : 0f;
                }
                Group.Barrier();

                for (int k = 0; k < BLOCK; k++)
                {
                    float a0 = aTile[(threadRow * REG + 0) * BLOCK + k];
                    float a1 = aTile[(threadRow * REG + 1) * BLOCK + k];
                    float a2 = aTile[(threadRow * REG + 2) * BLOCK + k];
                    float a3 = aTile[(threadRow * REG + 3) * BLOCK + k];
                    float b0 = bTile[k * TILE + threadCol * REG + 0];
                    float b1 = bTile[k * TILE + threadCol * REG + 1];
                    float b2 = bTile[k * TILE + threadCol * REG + 2];
                    float b3 = bTile[k * TILE + threadCol * REG + 3];
                    c00 += a0 * b0; c01 += a0 * b1; c02 += a0 * b2; c03 += a0 * b3;
                    c10 += a1 * b0; c11 += a1 * b1; c12 += a1 * b2; c13 += a1 * b3;
                    c20 += a2 * b0; c21 += a2 * b1; c22 += a2 * b2; c23 += a2 * b3;
                    c30 += a3 * b0; c31 += a3 * b1; c32 += a3 * b2; c33 += a3 * b3;
                }
                Group.Barrier();
            }

            int baseRow = tileRow * TILE + threadRow * REG;
            int baseCol = tileCol * TILE + threadCol * REG;

            // Fused bias-Add + activation in the write-back (bias indexed by output column).
            if (baseRow + 0 < M) {
                if (baseCol + 0 < N) Y[(baseRow + 0) * N + baseCol + 0] = FusedActivate(c00, Bias[baseCol + 0], activation);
                if (baseCol + 1 < N) Y[(baseRow + 0) * N + baseCol + 1] = FusedActivate(c01, Bias[baseCol + 1], activation);
                if (baseCol + 2 < N) Y[(baseRow + 0) * N + baseCol + 2] = FusedActivate(c02, Bias[baseCol + 2], activation);
                if (baseCol + 3 < N) Y[(baseRow + 0) * N + baseCol + 3] = FusedActivate(c03, Bias[baseCol + 3], activation);
            }
            if (baseRow + 1 < M) {
                if (baseCol + 0 < N) Y[(baseRow + 1) * N + baseCol + 0] = FusedActivate(c10, Bias[baseCol + 0], activation);
                if (baseCol + 1 < N) Y[(baseRow + 1) * N + baseCol + 1] = FusedActivate(c11, Bias[baseCol + 1], activation);
                if (baseCol + 2 < N) Y[(baseRow + 1) * N + baseCol + 2] = FusedActivate(c12, Bias[baseCol + 2], activation);
                if (baseCol + 3 < N) Y[(baseRow + 1) * N + baseCol + 3] = FusedActivate(c13, Bias[baseCol + 3], activation);
            }
            if (baseRow + 2 < M) {
                if (baseCol + 0 < N) Y[(baseRow + 2) * N + baseCol + 0] = FusedActivate(c20, Bias[baseCol + 0], activation);
                if (baseCol + 1 < N) Y[(baseRow + 2) * N + baseCol + 1] = FusedActivate(c21, Bias[baseCol + 1], activation);
                if (baseCol + 2 < N) Y[(baseRow + 2) * N + baseCol + 2] = FusedActivate(c22, Bias[baseCol + 2], activation);
                if (baseCol + 3 < N) Y[(baseRow + 2) * N + baseCol + 3] = FusedActivate(c23, Bias[baseCol + 3], activation);
            }
            if (baseRow + 3 < M) {
                if (baseCol + 0 < N) Y[(baseRow + 3) * N + baseCol + 0] = FusedActivate(c30, Bias[baseCol + 0], activation);
                if (baseCol + 1 < N) Y[(baseRow + 3) * N + baseCol + 1] = FusedActivate(c31, Bias[baseCol + 1], activation);
                if (baseCol + 2 < N) Y[(baseRow + 3) * N + baseCol + 2] = FusedActivate(c32, Bias[baseCol + 2], activation);
                if (baseCol + 3 < N) Y[(baseRow + 3) * N + baseCol + 3] = FusedActivate(c33, Bias[baseCol + 3], activation);
            }
        }

        async Task RunRegBlockedFusedFFN(Accelerator accelerator, int activation, int seed, float spread)
        {
            if (accelerator.MaxNumThreadsPerGroup < RbBlock * RbBlock)
                throw new UnsupportedTestException(
                    $"{accelerator.AcceleratorType}: register-blocked FFN needs a {RbBlock * RbBlock}-thread group; " +
                    $"MaxNumThreadsPerGroup={accelerator.MaxNumThreadsPerGroup}");

            // M,N >= RbTile (64) so the register-blocked path is exercised; non-multiples of 64/16 for bounds.
            const int M = 70, K = 130, N = 100;
            var X = new float[M * K];
            var W = new float[K * N];
            var B = new float[N];
            var rng = new Random(seed);
            for (int i = 0; i < X.Length; i++) X[i] = (float)(rng.NextDouble() - 0.5) * spread;
            for (int i = 0; i < W.Length; i++) W[i] = (float)(rng.NextDouble() - 0.5) * spread;
            for (int i = 0; i < B.Length; i++) B[i] = (float)(rng.NextDouble() - 0.5);

            using var xBuf = accelerator.Allocate1D(X);
            using var wBuf = accelerator.Allocate1D(W);
            using var bBuf = accelerator.Allocate1D(B);
            using var yBuf = accelerator.Allocate1D<float>(M * N);

            int numTilesN = (N + RbTile - 1) / RbTile;
            int numTilesM = (M + RbTile - 1) / RbTile;
            int totalTiles = numTilesM * numTilesN;

            var kern = accelerator.LoadStreamKernel<
                ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>,
                int, int, int, int, int>(FusedRegBlockedLinearActivation);
            kern(new KernelConfig(totalTiles, RbBlock * RbBlock),
                xBuf.View, wBuf.View, bBuf.View, yBuf.View, M, K, N, numTilesN, activation);
            await accelerator.SynchronizeAsync();

            var got = await yBuf.CopyToHostAsync<float>();
            for (int m = 0; m < M; m++)
                for (int n = 0; n < N; n++)
                {
                    float acc = 0f;
                    for (int k = 0; k < K; k++) acc += X[m * K + k] * W[k * N + n];
                    acc += B[n];
                    float expected;
                    if (activation == 1)
                    {
                        float inner = 0.7978845608f * (acc + 0.044715f * acc * acc * acc);
                        expected = 0.5f * acc * (1f + MathF.Tanh(inner));
                    }
                    else if (activation == 2) expected = ErfGeluRef(acc);
                    else expected = acc;

                    float actual = got[m * N + n];
                    float tol = 0.01f * MathF.Abs(expected) + 0.003f;
                    float err = MathF.Abs(actual - expected);
                    if (err > tol)
                        throw new Exception(
                            $"RegBlocked fused FFN (act={activation}) wrong at [{m},{n}]: expected {expected} got {actual} " +
                            $"(err {err} > tol {tol}; register-block/load-index/bias/activation bug?).");
                }
        }

        [TestMethod]
        public async Task FusedFFN_RegBlockedTanhGELU_Correct() =>
            await RunTest(async accelerator => await RunRegBlockedFusedFFN(accelerator, activation: 1, seed: 41, spread: 1f));

        [TestMethod]
        public async Task FusedFFN_RegBlockedErfGELU_Correct() =>
            await RunTest(async accelerator => await RunRegBlockedFusedFFN(accelerator, activation: 2, seed: 43, spread: 2f));
    }
}
