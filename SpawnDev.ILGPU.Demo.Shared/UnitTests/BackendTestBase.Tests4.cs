using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part 4: Tests inspired by ILGPU reference samples — expanded atomics, XMath, explicit grouping,
    //         prefix sum, dot product, while loops, multi-buffer, float rem, IsNaN/IsInf, and more.
    public abstract partial class BackendTestBase
    {
        #region Part 4 Kernel Definitions

        // --- Atomic And/Or/Xor (from SimpleAtomics sample) ---
        static void AtomicAndOrXorKernel(Index1D index, ArrayView<int> data, int constant)
        {
            Atomic.And(ref data[0], constant);
            Atomic.Or(ref data[1], constant);
            Atomic.Xor(ref data[2], constant);
        }

        // --- XMath Functions (from AlgorithmsMath sample) ---
        static void XMathFunctionsKernel(Index1D index, ArrayView<float> output, float c)
        {
            // XMath functions compile down to IR arithmetic nodes
            float sinhVal = MathF.Sinh(c + index);
            float atanVal = MathF.Atan(c);
            float expVal = MathF.Exp(c * 0.1f);
            float logVal = MathF.Log(1.0f + index);
            output[index] = sinhVal + atanVal + expVal + logVal;
        }

        // --- IntrinsicMath.Clamp (from SimpleMath sample) ---
        static void IntrinsicMathClampKernel(Index1D index, ArrayView<float> output)
        {
            float val = index * 3.0f - 10.0f; // range: -10, -7, -4, -1, 2, 5, 8, 11
            output[index] = IntrinsicMath.Clamp(val, 0.0f, 10.0f);
        }

        // --- System.Math.Clamp redirect regression ---
        // System.Math.Clamp(value, min, max) inlines `throw ThrowMinMaxException(...)`,
        // which the IL frontend cannot lower to IR -> NotSupportedException('Throw') at
        // PreCompile on every browser backend (WebGPU/WebGL/Wasm). The frontend
        // RemappedIntrinsics table now remaps Math.Clamp -> throw-free IntrinsicMath.Clamp
        // before inlining (same path as Math.Min/Max). These kernels lock that in.
        // Int overload mirrors the consumer repro: Math.Clamp((int)(channel*255f), 0, 255).
        static void MathClampIntKernel(Index1D index, ArrayView<int> output)
        {
            int val = (int)index * 100 - 250; // -250,-150,-50,50,150,250,350,450
            output[index] = Math.Clamp(val, 0, 255);
        }

        static void MathClampFloatKernel(Index1D index, ArrayView<float> output)
        {
            float val = index * 3.0f - 10.0f; // -10,-7,-4,-1,2,5,8,11
            output[index] = Math.Clamp(val, 0.0f, 10.0f);
        }

        // --- Multiple LoadStreamKernel on Same Accelerator ---
        // Regression test for _uf_group_iter redeclaration bug (PLANS.md Bug #2).
        // Two LoadStreamKernel kernels that both need the uniformity transform
        // must not collide on the synthetic counter variable name.
        static void StreamKernelA(ArrayView<float> data, int limit)
        {
            var gid = Grid.IdxX * Group.DimX + Group.IdxX;
            if (gid < limit)
                data[gid] = gid * 2.0f;
        }

        static void StreamKernelB(ArrayView<float> data, int limit, float offset)
        {
            var gid = Grid.IdxX * Group.DimX + Group.IdxX;
            if (gid < limit)
                data[gid] = data[gid] + offset;
        }

        // --- Explicitly Grouped Kernel (from ExplicitlyGroupedKernels sample) ---
        static void ExplicitGroupKernel(ArrayView<int> data, int constant)
        {
            var globalIndex = Grid.GlobalIndex.X;
            if (globalIndex < data.Length)
                data[globalIndex] = globalIndex + constant;
        }

        // Broader _uf_group_iter stress (2026-06-21): load MANY uniformity-transform kernels with DIFFERENT
        // Grid.IdxX-family shapes on one accelerator. If the synthetic-counter declaration leaks across
        // kernels, the later ones emit a duplicate/undeclared _uf_group_iter and the WGSL fails to compile.
        static void GridStreamC(ArrayView<float> data, int limit) // grid-stride loop (uses _uf_group_iter / grid-stride)
        {
            for (int i = Grid.IdxX * Group.DimX + Group.IdxX; i < limit; i += Group.DimX * Grid.DimX)
                data[i] = data[i] * 3.0f;
        }
        static void GridStreamD(ArrayView<float> data, int limit) // Grid.GlobalIndex.X form
        {
            var gid = Grid.GlobalIndex.X;
            if (gid < limit) data[gid] = data[gid] - 1.0f;
        }

        // --- 2D Grid Dispatch (batched kernel using Grid.IdxY for batch) ---
        static void Batched2DGridKernel(ArrayView<int> output, int cols)
        {
            int tileIdx = Grid.IdxX;
            int batch = Grid.IdxY;
            int localIdx = Group.IdxX;

            int col = tileIdx * Group.DimX + localIdx;
            if (col < cols)
                output[batch * cols + col] = batch * 1000 + col;
        }

        // --- Triple-Nested Loop Test (Conv2D-style pattern) ---
        // This kernel does a simplified 3x3 convolution pattern with triple-nested loops.
        // On WebGPU, triple-nested loops can produce wrong WGSL codegen.
        static void TripleNestedLoopKernel(Index1D idx, ArrayView<float> input, ArrayView<float> output, int channels, int kSize)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                for (int ky = 0; ky < kSize; ky++)
                {
                    for (int kx = 0; kx < kSize; kx++)
                    {
                        // Simple accumulation: sum += (c + 1) * (ky + 1) * (kx + 1)
                        sum += (c + 1) * (ky + 1) * (kx + 1);
                    }
                }
            }
            output[idx] = sum;
        }

        // --- Double-Nested Loop Test (should work) ---
        static void DoubleNestedLoopKernel(Index1D idx, ArrayView<float> output, int outerCount, int innerCount)
        {
            float sum = 0f;
            for (int a = 0; a < outerCount; a++)
            {
                for (int b = 0; b < innerCount; b++)
                {
                    sum += (a + 1) * (b + 1);
                }
            }
            output[idx] = sum;
        }

        // --- Realistic Triple-Nested Loop (Conv2D pattern with array accesses) ---
        // Unlike the simple TripleNestedLoopKernel, this reads from weight/input arrays
        // with computed indices inside the loops — matching Conv2D's actual IR pattern.
        static void Conv2DStyleTripleLoopKernel(
            Index1D idx,
            ArrayView<float> input,   // [inC * inH * inW]
            ArrayView<float> weight,  // [outC * inC * kH * kW]
            ArrayView<float> output,  // [outC * outH * outW]
            int inC, int inH, int inW,
            int outC, int kH, int kW)
        {
            int outH = inH - kH + 1;
            int outW = inW - kW + 1;
            int outHW = outH * outW;

            int oc = idx / outHW;
            int rem = idx % outHW;
            int oy = rem / outW;
            int ox = rem % outW;

            if (oc >= outC) return;

            float sum = 0f;
            for (int ic = 0; ic < inC; ic++)
            {
                for (int ky = 0; ky < kH; ky++)
                {
                    for (int kx = 0; kx < kW; kx++)
                    {
                        int iy = oy + ky;
                        int ix = ox + kx;
                        float inVal = input[ic * inH * inW + iy * inW + ix];
                        float wVal = weight[oc * inC * kH * kW + ic * kH * kW + ky * kW + kx];
                        sum += inVal * wVal;
                    }
                }
            }
            output[idx] = sum;
        }

        // --- 2D Grid Dispatch with Shared Memory (Batched MatMul pattern) ---
        // This matches the ML's BatchedTiledMatMulImpl: LoadStreamKernel + KernelConfig(Index2D)
        // + Grid.IdxY for batch + SharedMemory for tiling.
        const int TILE_2D_TEST = 8; // Small tile for testing
        static void BatchedTiledKernel(
            ArrayView<float> A,       // [batches * M * K]
            ArrayView<float> B,       // [batches * K * N]
            ArrayView<float> C,       // [batches * M * N]
            int M, int K, int N)
        {
            var tileA = SharedMemory.Allocate<float>(TILE_2D_TEST * TILE_2D_TEST);
            var tileB = SharedMemory.Allocate<float>(TILE_2D_TEST * TILE_2D_TEST);

            int batch = Grid.IdxY;
            int tileIdx = Grid.IdxX;
            int numTilesN = (N + TILE_2D_TEST - 1) / TILE_2D_TEST;
            int tileRow = tileIdx / numTilesN;
            int tileCol = tileIdx % numTilesN;

            int tid = Group.IdxX;
            int localRow = tid / TILE_2D_TEST;
            int localCol = tid % TILE_2D_TEST;

            int globalRow = tileRow * TILE_2D_TEST + localRow;
            int globalCol = tileCol * TILE_2D_TEST + localCol;

            float sum = 0f;
            int numTilesK = (K + TILE_2D_TEST - 1) / TILE_2D_TEST;
            int batchOffA = batch * M * K;
            int batchOffB = batch * K * N;

            for (int t = 0; t < numTilesK; t++)
            {
                int aCol = t * TILE_2D_TEST + localCol;
                int bRow = t * TILE_2D_TEST + localRow;

                tileA[localRow * TILE_2D_TEST + localCol] = (globalRow < M && aCol < K)
                    ? A[batchOffA + globalRow * K + aCol] : 0f;
                tileB[localRow * TILE_2D_TEST + localCol] = (bRow < K && globalCol < N)
                    ? B[batchOffB + bRow * N + globalCol] : 0f;

                Group.Barrier();

                for (int kk = 0; kk < TILE_2D_TEST; kk++)
                    sum += tileA[localRow * TILE_2D_TEST + kk] * tileB[kk * TILE_2D_TEST + localCol];

                Group.Barrier();
            }

            if (globalRow < M && globalCol < N)
                C[batch * M * N + globalRow * N + globalCol] = sum;
        }

        // --- Prefix Sum using Shared Memory ---
        static void PrefixSumKernel(ArrayView<int> data)
        {
            var tid = Group.IdxX;
            var shared = SharedMemory.Allocate<int>(64);
            shared[tid] = data[tid];
            Group.Barrier();

            // Inclusive prefix sum (Hillis-Steele)
            for (int offset = 1; offset < 64; offset *= 2)
            {
                int val = 0;
                if (tid >= offset)
                    val = shared[tid - offset];
                Group.Barrier();
                if (tid >= offset)
                    shared[tid] += val;
                Group.Barrier();
            }

            data[tid] = shared[tid];
        }

        // --- Dot Product with Shared Memory + Atomic ---
        static void DotProductKernel(
            ArrayView<float> a,
            ArrayView<float> b,
            ArrayView<float> result)
        {
            var tid = Group.IdxX;
            var shared = SharedMemory.Allocate<float>(64);

            shared[tid] = a[tid] * b[tid];
            Group.Barrier();

            // Tree reduction within the group
            for (int stride = 32; stride > 0; stride >>= 1)
            {
                if (tid < stride)
                    shared[tid] += shared[tid + stride];
                Group.Barrier();
            }

            if (tid == 0)
                Atomic.Add(ref result[0], shared[0]);
        }

        // --- While Loop (Collatz sequence length) ---
        static void WhileLoopKernel(Index1D index, ArrayView<int> output)
        {
            int n = index + 1; // start from 1
            int count = 0;
            while (n > 1)
            {
                if (n % 2 == 0)
                    n = n / 2;
                else
                    n = 3 * n + 1;
                count++;
            }
            output[index] = count;
        }

        // --- Multi-Buffer Input ---
        static void MultiBufferInputKernel(
            Index1D index,
            ArrayView<float> a,
            ArrayView<float> b,
            ArrayView<float> c,
            ArrayView<float> output)
        {
            output[index] = a[index] + b[index] * c[index];
        }

        // --- Conditional Expression ---
        static void ConditionalExpressionKernel(Index1D index, ArrayView<int> output)
        {
            int val = index;
            // Nested ternary expressions
            int result = val < 4 ? val * 10 :
                         val < 8 ? val * 20 :
                         val < 12 ? val * 30 : val * 40;
            output[index] = result;
        }

        // --- Array Fill Pattern ---
        static void ArrayFillPatternKernel(Index1D index, ArrayView<int> output, int stride, int value)
        {
            int pos = index * stride;
            if (pos < output.Length)
                output[pos] = value;
        }

        // --- Float Rem (verifies Wasm Rem fix) ---
        static void FloatRemKernel(Index1D index, ArrayView<float> output)
        {
            float val = index * 1.5f; // 0, 1.5, 3.0, 4.5, 6.0, 7.5, 9.0, 10.5
            output[index] = val % 4.0f;  // 0, 1.5, 3.0, 0.5, 2.0, 3.5, 1.0, 2.5
        }

        // --- IsNaN/IsInf Test (verifies Wasm fixes) ---
        static void IsNaNIsInfKernel(Index1D index, ArrayView<float> input, ArrayView<int> output)
        {
            float val = input[index];
            int result = 0;
            // Bit 0 = IsNaN, Bit 1 = IsInf — use intrinsics that compile to IsNaNF/IsInfF IR
            if (float.IsNaN(val)) result |= 1;
            if (float.IsInfinity(val)) result |= 2;
            output[index] = result;
        }

        // --- Bitwise Rotate ---
        static void BitwiseRotateKernel(Index1D index, ArrayView<int> output)
        {
            int val = 0x12345678;
            int shift = index % 32;
            // Rotate left: (val << shift) | (val >>> (32 - shift))
            int rotated = (val << shift) | ((int)((uint)val >> (32 - shift)));
            output[index] = rotated;
        }

        // --- Map kernel for Map-Reduce test ---
        static void MapSquareKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
        {
            output[index] = input[index] * input[index];
        }

        // --- Reduce kernel for Map-Reduce test ---
        static void ReduceSumKernel(ArrayView<float> data, ArrayView<float> result)
        {
            var tid = Group.IdxX;
            var shared = SharedMemory.Allocate<float>(64);
            shared[tid] = data[tid];
            Group.Barrier();

            for (int stride = 32; stride > 0; stride >>= 1)
            {
                if (tid < stride)
                    shared[tid] += shared[tid + stride];
                Group.Barrier();
            }

            if (tid == 0)
                result[0] = shared[0];
        }

        // --- Saturating Arithmetic (Min/Max clamping) ---
        static void SaturatingArithmeticKernel(Index1D index, ArrayView<float> output)
        {
            float val = (index - 8.0f) * 0.5f; // range: -4, -3.5, -3, ..., 3.5
            // Clamp to [-2, 2] using Math.Min(Math.Max(...))
            float clamped = Math.Min(Math.Max(val, -2.0f), 2.0f);
            // Normalize to [0, 1]
            float normalized = (clamped + 2.0f) * 0.25f;
            output[index] = normalized;
        }

        // --- CopySign (verifies upstream #1361 fix — argument order was swapped) ---
        static void CopySignKernel(Index1D index, ArrayView<float> magnitudes, ArrayView<float> signs, ArrayView<float> output)
        {
            output[index] = IntrinsicMath.CopySign(magnitudes[index], signs[index]);
        }

        // --- UInt to Float cast (verifies upstream #1309 fix — was going through double) ---
        static void UintToFloatCastKernel(Index1D index, ArrayView<uint> input, ArrayView<float> output)
        {
            uint val = input[index];
            output[index] = (float)val;
        }

        #endregion

        #region Part 4 Tests

        [TestMethod]
        public async Task AtomicAndOrXorTest() => await RunTest(async accelerator =>
        {
            using var buf = accelerator.Allocate1D<int>(3);
            var initData = new int[] { -1, 0, 0 }; // And identity, Or identity, Xor identity
            buf.CopyFromCPU(initData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(AtomicAndOrXorKernel);
            // Launch 8 threads with constant = 0b1010 (10)
            kernel((Index1D)8, buf.View, 0b1010);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();

            // And: -1 & 10 & 10 & ... = 10 (all threads And with same value)
            if (result[0] != 0b1010)
                throw new Exception($"Atomic.And failed. Expected {0b1010}, got {result[0]}");
            // Or: 0 | 10 | 10 | ... = 10
            if (result[1] != 0b1010)
                throw new Exception($"Atomic.Or failed. Expected {0b1010}, got {result[1]}");
            // Xor: 0 ^ 10 ^ 10 ^ ... = 0 (even number of XORs cancel out)
            if (result[2] != 0)
                throw new Exception($"Atomic.Xor failed. Expected 0, got {result[2]}");
        });

        [TestMethod]
        public async Task XMathFunctionsTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            float c = 0.1f;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, float>(XMathFunctionsKernel);
            kernel((Index1D)len, buf.View, c);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();

            for (int i = 0; i < len; i++)
            {
                float expected = MathF.Sinh(c + i) + MathF.Atan(c) + MathF.Exp(c * 0.1f) + MathF.Log(1.0f + i);
                if (MathF.Abs(result[i] - expected) > 0.01f * MathF.Abs(expected) + 0.01f)
                    throw new Exception($"XMath failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task IntrinsicMathClampTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(IntrinsicMathClampKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();

            float[] expected = { 0, 0, 0, 0, 2, 5, 8, 10 };
            for (int i = 0; i < len; i++)
                if (MathF.Abs(result[i] - expected[i]) > 0.01f)
                    throw new Exception($"IntrinsicMath.Clamp failed at {i}. Expected {expected[i]}, got {result[i]}");
        });

        [TestMethod]
        public async Task MathClampIntTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(MathClampIntKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();

            int[] expected = { 0, 0, 0, 50, 150, 250, 255, 255 };
            for (int i = 0; i < len; i++)
                if (result[i] != expected[i])
                    throw new Exception($"Math.Clamp(int) failed at {i}. Expected {expected[i]}, got {result[i]}");
        });

        [TestMethod]
        public async Task MathClampFloatTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(MathClampFloatKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();

            float[] expected = { 0, 0, 0, 0, 2, 5, 8, 10 };
            for (int i = 0; i < len; i++)
                if (MathF.Abs(result[i] - expected[i]) > 0.01f)
                    throw new Exception($"Math.Clamp(float) failed at {i}. Expected {expected[i]}, got {result[i]}");
        });

        [TestMethod]
        public async Task ExplicitGroupKernelTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            using var buf = accelerator.Allocate1D<int>(len);
            buf.MemSetToZero();

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>, int>(ExplicitGroupKernel);
            // 1 group of 64 threads
            kernel(new KernelConfig(1, 64), buf.View, 100);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();

            for (int i = 0; i < len; i++)
                if (result[i] != i + 100)
                    throw new Exception($"Explicit group kernel failed at {i}. Expected {i + 100}, got {result[i]}");
        });

        /// <summary>
        /// Regression test for 2D grid dispatch on WebGPU.
        /// LoadStreamKernel with KernelConfig(Index2D gridDim, Index2D groupDim)
        /// must correctly map Grid.IdxX and Grid.IdxY to workgroup_id.x and .y
        /// respectively — NOT linearize Grid.IdxX when Grid.IdxY is also used.
        /// </summary>
        [TestMethod]
        public async Task Batched2DGridDispatchTest() => await RunTest(async accelerator =>
        {
            // WebGL uses vertex shaders for compute — LoadStreamKernel with KernelConfig
            // and explicit Grid.IdxX/Y is not fully supported on WebGL yet.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL does not support LoadStreamKernel with 2D grid dispatch (no native workgroup support)");

            int batches = 4;
            int cols = 100;
            int groupSize = 32;
            int numGroups = (cols + groupSize - 1) / groupSize; // 4 groups
            int total = batches * cols;

            using var buf = accelerator.Allocate1D<int>(total);
            buf.MemSetToZero();

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>, int>(Batched2DGridKernel);
            var gridDim = new Index2D(numGroups, batches);
            var groupDim = new Index2D(groupSize, 1);
            kernel(new KernelConfig(gridDim, groupDim), buf.View, cols);
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync<int>();
            for (int b = 0; b < batches; b++)
                for (int c = 0; c < cols; c++)
                {
                    int expected = b * 1000 + c;
                    int actual = result[b * cols + c];
                    if (actual != expected)
                        throw new Exception($"2D grid dispatch failed at batch={b}, col={c}: expected={expected}, got={actual}");
                }
        });

        /// <summary>
        /// Regression test for triple-nested loops in WebGPU WGSL codegen.
        /// Triple-nested for(c){for(ky){for(kx)}} generates incorrect WGSL
        /// that truncates inner loop iterations. This is the Conv2D pattern.
        /// </summary>
        [TestMethod]
        public async Task TripleNestedLoopTest() => await RunTest(async accelerator =>
        {
            int count = 64;
            int channels = 8;
            int kSize = 3;

            // CPU reference: sum = sum over c,ky,kx of (c+1)*(ky+1)*(kx+1)
            // = (sum c+1 for c=0..7) * (sum ky+1 for ky=0..2) * (sum kx+1 for kx=0..2)
            // = (1+2+3+4+5+6+7+8) * (1+2+3) * (1+2+3) = 36 * 6 * 6 = 1296
            float expectedValue = 0;
            for (int c = 0; c < channels; c++)
                for (int ky = 0; ky < kSize; ky++)
                    for (int kx = 0; kx < kSize; kx++)
                        expectedValue += (c + 1) * (ky + 1) * (kx + 1);

            using var input = accelerator.Allocate1D<float>(count);
            using var output = accelerator.Allocate1D<float>(count);
            input.MemSetToZero();

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int>(TripleNestedLoopKernel);
            kernel((Index1D)count, input.View, output.View, channels, kSize);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync<float>();
            for (int i = 0; i < count; i++)
                if (MathF.Abs(result[i] - expectedValue) > 0.01f)
                    throw new Exception($"Triple nested loop failed at {i}: expected={expectedValue}, got={result[i]}");
        });

        [TestMethod]
        public async Task DoubleNestedLoopTest() => await RunTest(async accelerator =>
        {
            int count = 64;
            int outerCount = 8;
            int innerCount = 3;

            // Expected: sum(a=0..7)(a+1) * sum(b=0..2)(b+1) = 36 * 6 = 216
            float expectedValue = 0;
            for (int a = 0; a < outerCount; a++)
                for (int b = 0; b < innerCount; b++)
                    expectedValue += (a + 1) * (b + 1);

            using var output = accelerator.Allocate1D<float>(count);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, int, int>(DoubleNestedLoopKernel);
            kernel((Index1D)count, output.View, outerCount, innerCount);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync<float>();
            for (int i = 0; i < count; i++)
                if (MathF.Abs(result[i] - expectedValue) > 0.01f)
                    throw new Exception($"Double nested loop failed at {i}: expected={expectedValue}, got={result[i]}");
        });

        /// <summary>
        /// Realistic Conv2D-style triple-nested loop with array accesses.
        /// Unlike TripleNestedLoopTest (scalar math only), this reads from
        /// input/weight arrays with computed indices inside the loops —
        /// matching the actual IR pattern that Conv2D generates.
        /// If this passes but the simple test also passes, the WGSL loop
        /// codegen handles array-access patterns correctly.
        /// </summary>
        [TestMethod]
        public async Task Conv2DStyleTripleNestedLoopTest() => await RunTest(async accelerator =>
        {
            // Small Conv2D: 2 input channels, 5x5 input, 2 output channels, 3x3 kernel
            int inC = 2, inH = 5, inW = 5;
            int outC = 2, kH = 3, kW = 3;
            int outH = inH - kH + 1; // 3
            int outW = inW - kW + 1; // 3
            int totalOut = outC * outH * outW; // 2 * 3 * 3 = 18

            // Fill input with predictable values: input[ic,y,x] = ic*100 + y*10 + x
            var inputData = new float[inC * inH * inW];
            for (int ic = 0; ic < inC; ic++)
                for (int y = 0; y < inH; y++)
                    for (int x = 0; x < inW; x++)
                        inputData[ic * inH * inW + y * inW + x] = ic * 100f + y * 10f + x;

            // Fill weight with small values: weight[oc,ic,ky,kx] = 0.01 * (oc*100 + ic*10 + ky*3 + kx)
            var weightData = new float[outC * inC * kH * kW];
            for (int oc = 0; oc < outC; oc++)
                for (int ic = 0; ic < inC; ic++)
                    for (int ky = 0; ky < kH; ky++)
                        for (int kx = 0; kx < kW; kx++)
                            weightData[oc * inC * kH * kW + ic * kH * kW + ky * kW + kx]
                                = 0.01f * (oc * 100 + ic * 10 + ky * 3 + kx);

            // CPU reference
            var expected = new float[totalOut];
            for (int oc = 0; oc < outC; oc++)
                for (int oy = 0; oy < outH; oy++)
                    for (int ox = 0; ox < outW; ox++)
                    {
                        float sum = 0f;
                        for (int ic = 0; ic < inC; ic++)
                            for (int ky = 0; ky < kH; ky++)
                                for (int kx = 0; kx < kW; kx++)
                                    sum += inputData[ic * inH * inW + (oy + ky) * inW + (ox + kx)]
                                         * weightData[oc * inC * kH * kW + ic * kH * kW + ky * kW + kx];
                        expected[oc * outH * outW + oy * outW + ox] = sum;
                    }

            using var inputBuf = accelerator.Allocate1D(inputData);
            using var weightBuf = accelerator.Allocate1D(weightData);
            using var outputBuf = accelerator.Allocate1D<float>(totalOut);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>,
                int, int, int, int, int, int>(Conv2DStyleTripleLoopKernel);
            kernel((Index1D)totalOut, inputBuf.View, weightBuf.View, outputBuf.View,
                inC, inH, inW, outC, kH, kW);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<float>();
            for (int i = 0; i < totalOut; i++)
            {
                if (MathF.Abs(result[i] - expected[i]) > 0.1f)
                    throw new Exception($"Conv2D triple-nested loop failed at [{i}]: expected={expected[i]:F4}, got={result[i]:F4}");
            }
        });

        /// <summary>
        /// Batched tiled MatMul using LoadStreamKernel + KernelConfig(Index2D) +
        /// Grid.IdxY for batch + SharedMemory for tiling. This matches the ML
        /// engine's BatchedTiledMatMulImpl pattern exactly.
        /// </summary>
        [TestMethod]
        public async Task BatchedTiledMatMul2DGridTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL does not support LoadStreamKernel with shared memory + 2D grid");

            int batches = 4, M = 16, K = 16, N = 16;
            int totalA = batches * M * K;
            int totalB = batches * K * N;
            int totalC = batches * M * N;

            // Fill A and B with predictable values
            var aData = new float[totalA];
            var bData = new float[totalB];
            for (int b = 0; b < batches; b++)
                for (int r = 0; r < M; r++)
                    for (int c = 0; c < K; c++)
                        aData[b * M * K + r * K + c] = (b + 1) * 0.1f + r * 0.01f;
            for (int b = 0; b < batches; b++)
                for (int r = 0; r < K; r++)
                    for (int c = 0; c < N; c++)
                        bData[b * K * N + r * N + c] = (b + 1) * 0.01f + c * 0.001f;

            // CPU reference
            var expectedC = new float[totalC];
            for (int b = 0; b < batches; b++)
                for (int r = 0; r < M; r++)
                    for (int c = 0; c < N; c++)
                    {
                        float sum = 0f;
                        for (int k = 0; k < K; k++)
                            sum += aData[b * M * K + r * K + k] * bData[b * K * N + k * N + c];
                        expectedC[b * M * N + r * N + c] = sum;
                    }

            using var aBuf = accelerator.Allocate1D(aData);
            using var bBuf = accelerator.Allocate1D(bData);
            using var cBuf = accelerator.Allocate1D<float>(totalC);
            cBuf.MemSetToZero();

            var kernel = accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int>(BatchedTiledKernel);
            int numTilesM = (M + TILE_2D_TEST - 1) / TILE_2D_TEST;
            int numTilesN = (N + TILE_2D_TEST - 1) / TILE_2D_TEST;
            int totalTiles = numTilesM * numTilesN;

            var gridDim = new Index2D(totalTiles, batches);
            var groupDim = new Index2D(TILE_2D_TEST * TILE_2D_TEST, 1);
            kernel(new KernelConfig(gridDim, groupDim), aBuf.View, bBuf.View, cBuf.View, M, K, N);
            await accelerator.SynchronizeAsync();

            var result = await cBuf.CopyToHostAsync<float>();
            for (int b = 0; b < batches; b++)
                for (int r = 0; r < M; r++)
                    for (int c = 0; c < N; c++)
                    {
                        int idx = b * M * N + r * N + c;
                        if (MathF.Abs(result[idx] - expectedC[idx]) > 0.1f)
                            throw new Exception($"Batched tiled MatMul 2D grid failed at batch={b}, row={r}, col={c}: expected={expectedC[idx]:F4}, got={result[idx]:F4}");
                    }
        });

        /// <summary>
        /// Regression test for PLANS.md Bug #2: multiple LoadStreamKernel calls
        /// on the same accelerator that both need the WGSL uniformity transform.
        /// Without the fix, the second kernel emits "var _uf_group_iter" again,
        /// causing a WGSL "redeclaration" compilation error.
        /// </summary>
        [TestMethod]
        public async Task MultipleStreamKernelTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL does not support LoadStreamKernel");

            int length = 128;
            int groupSize = 32;
            int numGroups = (length + groupSize - 1) / groupSize;

            using var buf = accelerator.Allocate1D<float>(length);
            buf.MemSetToZero();

            // Load TWO stream kernels on the same accelerator
            var kernelA = accelerator.LoadStreamKernel<ArrayView<float>, int>(StreamKernelA);
            var kernelB = accelerator.LoadStreamKernel<ArrayView<float>, int, float>(StreamKernelB);

            // Dispatch kernel A: data[i] = i * 2
            kernelA(new KernelConfig(numGroups, groupSize), buf.View, length);
            await accelerator.SynchronizeAsync();

            // Dispatch kernel B: data[i] = data[i] + 10
            kernelB(new KernelConfig(numGroups, groupSize), buf.View, length, 10f);
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < length; i++)
            {
                float expected = i * 2.0f + 10f;
                if (MathF.Abs(result[i] - expected) > 0.01f)
                    throw new Exception($"Multiple LoadStreamKernel failed at [{i}]: expected={expected}, got={result[i]}");
            }
        });

        /// <summary>Broader _uf_group_iter stress: FOUR uniformity-transform kernels (two manual-global-index,
        /// a grid-stride loop, and a Grid.GlobalIndex.X form) loaded + dispatched on one accelerator. Confirms
        /// the synthetic-counter declaration does not leak across kernels (the redeclaration class).</summary>
        [TestMethod]
        public async Task MultipleGridIdxKernelsStressTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL does not support LoadStreamKernel");
            int length = 128, groupSize = 32, numGroups = (length + groupSize - 1) / groupSize;
            using var buf = accelerator.Allocate1D<float>(length);
            buf.MemSetToZero();
            var kA = accelerator.LoadStreamKernel<ArrayView<float>, int>(StreamKernelA);            // data[i] = i*2
            var kB = accelerator.LoadStreamKernel<ArrayView<float>, int, float>(StreamKernelB);     // data[i] += 10
            var kC = accelerator.LoadStreamKernel<ArrayView<float>, int>(GridStreamC);              // data[i] *= 3 (grid-stride)
            var kD = accelerator.LoadStreamKernel<ArrayView<float>, int>(GridStreamD);              // data[i] -= 1
            var cfg = new KernelConfig(numGroups, groupSize);
            kA(cfg, buf.View, length); await accelerator.SynchronizeAsync();
            kB(cfg, buf.View, length, 10f); await accelerator.SynchronizeAsync();
            kC(cfg, buf.View, length); await accelerator.SynchronizeAsync();
            kD(cfg, buf.View, length); await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < length; i++)
            {
                float expected = (i * 2.0f + 10f) * 3.0f - 1.0f;
                if (MathF.Abs(result[i] - expected) > 0.01f)
                    throw new Exception($"MultipleGridIdxKernelsStress at [{i}]: expected={expected}, got={result[i]} (_uf_group_iter leak across kernels?).");
            }
        });

        [TestMethod]
        public async Task PrefixSumTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var data = new int[len];
            for (int i = 0; i < len; i++) data[i] = 1; // all ones → prefix sum = 1, 2, 3, ..., 64
            using var buf = accelerator.Allocate1D(data);

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>>(PrefixSumKernel);
            kernel(new KernelConfig(1, 64), buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();

            for (int i = 0; i < len; i++)
            {
                int expected = i + 1;
                if (result[i] != expected)
                    throw new Exception($"Prefix sum failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task DotProductTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var aData = new float[len];
            var bData = new float[len];
            for (int i = 0; i < len; i++) { aData[i] = 1.0f; bData[i] = 2.0f; }

            using var bufA = accelerator.Allocate1D(aData);
            using var bufB = accelerator.Allocate1D(bData);
            using var bufR = accelerator.Allocate1D<float>(1);
            bufR.MemSetToZero();

            var kernel = accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>, ArrayView<float>>(DotProductKernel);
            kernel(new KernelConfig(1, 64), bufA.View, bufB.View, bufR.View);
            await accelerator.SynchronizeAsync();
            var result = await bufR.CopyToHostAsync<float>();

            float expected = 128.0f; // 64 * 1 * 2
            if (MathF.Abs(result[0] - expected) > 0.01f)
                throw new Exception($"Dot product failed. Expected {expected}, got {result[0]}");
        });

        [TestMethod]
        public async Task WhileLoopTest() => await RunTest(async accelerator =>
        {
            int len = 16;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(WhileLoopKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();

            // Compute expected Collatz sequence lengths on CPU
            for (int i = 0; i < len; i++)
            {
                int n = i + 1;
                int count = 0;
                while (n > 1) { n = (n % 2 == 0) ? n / 2 : 3 * n + 1; count++; }
                if (result[i] != count)
                    throw new Exception($"While loop (Collatz) failed at {i}. Expected {count}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task MultiBufferInputTest() => await RunTest(async accelerator =>
        {
            int len = 32;
            var aData = new float[len]; var bData = new float[len]; var cData = new float[len];
            for (int i = 0; i < len; i++) { aData[i] = i; bData[i] = 2.0f; cData[i] = 0.5f; }

            using var bufA = accelerator.Allocate1D(aData);
            using var bufB = accelerator.Allocate1D(bData);
            using var bufC = accelerator.Allocate1D(cData);
            using var bufOut = accelerator.Allocate1D<float>(len);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(MultiBufferInputKernel);
            kernel((Index1D)len, bufA.View, bufB.View, bufC.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<float>();

            for (int i = 0; i < len; i++)
            {
                float expected = i + 2.0f * 0.5f; // a + b * c
                if (MathF.Abs(result[i] - expected) > 0.01f)
                    throw new Exception($"Multi-buffer failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task ConditionalExpressionTest() => await RunTest(async accelerator =>
        {
            int len = 16;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(ConditionalExpressionKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();

            for (int i = 0; i < len; i++)
            {
                int expected = i < 4 ? i * 10 :
                               i < 8 ? i * 20 :
                               i < 12 ? i * 30 : i * 40;
                if (result[i] != expected)
                    throw new Exception($"Conditional expression failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task FloatRemTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(FloatRemKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();

            float[] expected = { 0f, 1.5f, 3.0f, 0.5f, 2.0f, 3.5f, 1.0f, 2.5f };
            for (int i = 0; i < len; i++)
                if (MathF.Abs(result[i] - expected[i]) > 0.01f)
                    throw new Exception($"Float Rem failed at {i}. Expected {expected[i]}, got {result[i]}");
        });

        [TestMethod]
        public async Task IsNaNIsInfTest() => await RunTest(async accelerator =>
        {
            var input = new float[]
            {
                0.0f,                        // normal
                float.NaN,                   // NaN
                float.PositiveInfinity,      // +Inf
                float.NegativeInfinity,      // -Inf
                1.0f,                        // normal
                -1.0f,                       // normal
                float.NaN,                   // NaN
                42.0f                        // normal
            };

            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<int>(input.Length);
            bufOut.MemSetToZero();

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<int>>(IsNaNIsInfKernel);
            kernel((Index1D)input.Length, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<int>();

            // Bit 0 = IsNaN (val != val), Bit 1 = IsInf (float.IsInfinity)
            // NaN: bit 0 only (1). +Inf/-Inf: bit 1 only (2). Normal: 0.
            int[] expected = { 0, 1, 2, 2, 0, 0, 1, 0 };
            for (int i = 0; i < input.Length; i++)
            {
                if (result[i] != expected[i])
                    throw new Exception($"IsNaN/IsInf failed at {i} (input={input[i]}). Expected {expected[i]}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task BitwiseRotateTest() => await RunTest(async accelerator =>
        {
            int len = 16;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(BitwiseRotateKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();

            int val = 0x12345678;
            for (int i = 0; i < len; i++)
            {
                int shift = i % 32;
                int expected = (val << shift) | (int)((uint)val >> (32 - shift));
                if (result[i] != expected)
                    throw new Exception($"Bitwise rotate failed at {i}. Expected 0x{expected:X8}, got 0x{result[i]:X8}");
            }
        });

        [TestMethod]
        public async Task MapReduceTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var data = new float[len];
            for (int i = 0; i < len; i++) data[i] = i + 1; // 1..64

            using var bufIn = accelerator.Allocate1D(data);
            using var bufMapped = accelerator.Allocate1D<float>(len);
            using var bufResult = accelerator.Allocate1D<float>(1);
            bufResult.MemSetToZero();

            // Step 1: Map (square each element)
            var mapKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>>(MapSquareKernel);
            mapKernel((Index1D)len, bufIn.View, bufMapped.View);
            await accelerator.SynchronizeAsync();

            // Step 2: Reduce (sum all mapped elements)
            var reduceKernel = accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>>(ReduceSumKernel);
            reduceKernel(new KernelConfig(1, 64), bufMapped.View, bufResult.View);
            await accelerator.SynchronizeAsync();

            var result = await bufResult.CopyToHostAsync<float>();

            // Expected: sum of squares 1^2 + 2^2 + ... + 64^2 = 64*65*129/6 = 89440
            float expected = 0;
            for (int i = 1; i <= len; i++) expected += i * i;
            if (MathF.Abs(result[0] - expected) > 1.0f)
                throw new Exception($"Map-Reduce failed. Expected {expected}, got {result[0]}");
        });

        [TestMethod]
        public async Task SaturatingArithmeticTest() => await RunTest(async accelerator =>
        {
            int len = 16;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(SaturatingArithmeticKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();

            for (int i = 0; i < len; i++)
            {
                float val = (i - 8.0f) * 0.5f;
                float clamped = MathF.Min(MathF.Max(val, -2.0f), 2.0f);
                float expected = (clamped + 2.0f) * 0.25f;
                if (MathF.Abs(result[i] - expected) > 0.01f)
                    throw new Exception($"Saturating arithmetic failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task ArrayFillPatternTest() => await RunTest(async accelerator =>
        {
            int count = 16;
            int stride = 2;
            int totalSize = count * stride;
            using var buf = accelerator.Allocate1D<int>(totalSize);
            buf.MemSetToZero();

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int, int>(ArrayFillPatternKernel);
            kernel((Index1D)count, buf.View, stride, 42);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();

            for (int i = 0; i < totalSize; i++)
            {
                int expected = (i % stride == 0) ? 42 : 0;
                if (result[i] != expected)
                    throw new Exception($"Array fill pattern failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task CopySignTest() => await RunTest(async accelerator =>
        {
            // Test CopySign(magnitude, sign) — verifies upstream #1361 fix
            var magnitudes = new float[] { 5f, 5f, -5f, -5f, 0f, 3.14f, 100f, 1f };
            var signs =      new float[] { 1f, -1f, 1f, -1f, -1f, -1f, 1f, 0f };
            int len = magnitudes.Length;

            using var bufMag = accelerator.Allocate1D(magnitudes);
            using var bufSign = accelerator.Allocate1D(signs);
            using var bufOut = accelerator.Allocate1D<float>(len);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(CopySignKernel);
            kernel((Index1D)len, bufMag.View, bufSign.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<float>();

            for (int i = 0; i < len; i++)
            {
                float expected = MathF.CopySign(magnitudes[i], signs[i]);
                if (result[i] != expected)
                    throw new Exception($"CopySign failed at {i}. CopySign({magnitudes[i]}, {signs[i]}) expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task UintToFloatCastTest() => await RunTest(async accelerator =>
        {
            // Test uint to float cast — verifies upstream #1309 fix
            var input = new uint[] { 0, 1, 42, 255, 1000, 65535, 100000, 4294967295 };
            int len = input.Length;

            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<float>(len);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<uint>, ArrayView<float>>(UintToFloatCastKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<float>();

            for (int i = 0; i < len; i++)
            {
                float expected = (float)input[i];
                if (result[i] != expected)
                    throw new Exception($"UInt to float cast failed at {i}. (float){input[i]}u expected {expected}, got {result[i]}");
            }
        });

        // --- Nested Struct ICE (upstream #1538) ---
        // 4-level nested record struct parameter + static struct member access
        // Types must be public so ILGPU runtime can access them during compilation.
        public readonly record struct ParameterLayer1(ParameterLayer2 p2);
        public readonly record struct ParameterLayer2(ParameterLayer3 p3);
        public readonly record struct ParameterLayer3(ParameterLayer4 p4);
        public readonly record struct ParameterLayer4(float a, TestVector1538 b);
        public readonly struct DataLayer1_1538
        {
            public static DataLayer2_1538 StaticMemberStruct { get; } = new(
                new TestVector1538(1.0f, 2.0f, 3.0f),
                4.0f);
        }
        public readonly struct DataLayer2_1538
        {
            public readonly TestVector1538 A;
            public readonly float B;
            public DataLayer2_1538(TestVector1538 a, float b) { A = a; B = b; }
        }
        public readonly record struct TestVector1538(float X, float Y, float Z);

        private static void NestedStructICEKernel(Index1D index, ParameterLayer1 p, ArrayView<float> output)
        {
            var v = DataLayer1_1538.StaticMemberStruct.A;
            output[index] = v.X + v.Y + v.Z;
        }

        [TestMethod]
        public async Task NestedStructICETest() => await RunTest(async accelerator =>
        {
            // Upstream #1538: kernel compilation should not throw ICE with deeply nested struct params
            int len = 4;
            using var bufOut = accelerator.Allocate1D<float>(len);

            var param = new ParameterLayer1(
                new ParameterLayer2(
                    new ParameterLayer3(
                        new ParameterLayer4(1.0f, new TestVector1538(1, 2, 3)))));

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ParameterLayer1, ArrayView<float>>(NestedStructICEKernel);
            kernel((Index1D)len, param, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<float>();

            float expected = 1.0f + 2.0f + 3.0f; // = 6.0f
            for (int i = 0; i < len; i++)
            {
                if (result[i] != expected)
                    throw new Exception($"Nested struct ICE test failed at {i}: expected {expected}, got {result[i]}");
            }
        });

        // --- BVH Ray Traversal Wrong Results (upstream #1539) ---
        // Complex kernel with while loop, stack-based BVH traversal, nested record structs.
        // Produces wrong results on AMD integrated GPU OpenCL.
        // On correct backends: all 100 rays should MISS (geometry is behind target plane).
        public readonly record struct Vec3_1539(float X, float Y, float Z)
        {
            public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
            public static Vec3_1539 operator -(Vec3_1539 l, Vec3_1539 r) =>
                new(l.X - r.X, l.Y - r.Y, l.Z - r.Z);
            public static Vec3_1539 operator *(Vec3_1539 v, float d) =>
                new(d * v.X, d * v.Y, d * v.Z);
        }

        public readonly record struct Ray_1539(Vec3_1539 Origin, Vec3_1539 Direction)
        {
            public readonly Vec3_1539 ReciprocalDirection =
                new(1f / Direction.X, 1f / Direction.Y, 1f / Direction.Z);
        }

        public readonly record struct AABB_1539(Vec3_1539 Min, Vec3_1539 Max);

        public readonly record struct BVHNode_1539(AABB_1539 BoundingBox, int ChildNodeIndex, int InnerNodeIndicator)
        {
            public bool IsInnerNode() => InnerNodeIndicator == -1;
        }

        private static float IntersectAABB_1539(Ray_1539 ray, AABB_1539 box)
        {
            float tx1 = (box.Min.X - ray.Origin.X) * ray.ReciprocalDirection.X;
            float tx2 = (box.Max.X - ray.Origin.X) * ray.ReciprocalDirection.X;
            float tMin = MathF.Min(tx1, tx2);
            float tMax = MathF.Max(tx1, tx2);

            float ty1 = (box.Min.Y - ray.Origin.Y) * ray.ReciprocalDirection.Y;
            float ty2 = (box.Max.Y - ray.Origin.Y) * ray.ReciprocalDirection.Y;
            tMin = MathF.Max(tMin, MathF.Min(ty1, ty2));
            tMax = MathF.Min(tMax, MathF.Max(ty1, ty2));

            float tz1 = (box.Min.Z - ray.Origin.Z) * ray.ReciprocalDirection.Z;
            float tz2 = (box.Max.Z - ray.Origin.Z) * ray.ReciprocalDirection.Z;
            tMin = MathF.Max(tMin, MathF.Min(tz1, tz2));
            tMax = MathF.Min(tMax, MathF.Max(tz1, tz2));

            if (tMax >= tMin && tMax > 0f)
                return tMin; // Hit
            return float.MaxValue; // Miss
        }

        private static float DistanceToGeometry_1539(Ray_1539 ray, BVHNode_1539[] nodes)
        {
            BVHNode_1539[] nodeStack = new BVHNode_1539[10];
            uint nodeStackPtr = 0;
            BVHNode_1539 node = nodes[0]; // root
            float nearestHitDistance = float.MaxValue;

            while (true)
            {
                if (node.IsInnerNode())
                {
                    BVHNode_1539 child1 = nodes[node.ChildNodeIndex];
                    BVHNode_1539 child2 = nodes[node.ChildNodeIndex + 1];
                    float dist1 = IntersectAABB_1539(ray, child1.BoundingBox);
                    float dist2 = IntersectAABB_1539(ray, child2.BoundingBox);

                    // THIS EXACT PATTERN triggers the AMD OpenCL bug:
                    // combining the condition inline instead of extracting to a variable
                    if (dist1 == float.MaxValue || dist1 > nearestHitDistance)
                    {
                        if (nodeStackPtr == 0)
                            break;
                        node = nodeStack[--nodeStackPtr];
                    }
                    else
                    {
                        node = child1;
                        if (dist2 != float.MaxValue && dist2 < nearestHitDistance)
                        {
                            nodeStack[nodeStackPtr++] = child2;
                        }
                    }
                }
                else
                {
                    // Leaf node — set distance far beyond any ray-to-target distance (= miss)
                    nearestHitDistance = 1000f;
                    if (nodeStackPtr == 0)
                        break;
                    node = nodeStack[--nodeStackPtr];
                }
            }
            return nearestHitDistance;
        }

        private static void BVHRayTraversalKernel_1539(Index1D index, ArrayView<int> resultBuffer)
        {
            const int numberOfPoints = 100;
            if (index >= numberOfPoints)
                return;

            var rayOrigin = new Vec3_1539(0f, 0f, 1f);
            float x = index % 10 - 5;
            float y = index / 10 - 5;
            var rayTarget = new Vec3_1539(x, y, -10f);

            // Geometry BEHIND target plane — should NOT be hit by any ray
            BVHNode_1539[] nodes =
            [
                new(new AABB_1539(new Vec3_1539(-2f, -2f, -15f), new Vec3_1539(2f, 2f, -18f)), 1, -1),  // root (inner)
                new(new AABB_1539(new Vec3_1539(-1f, -1f, -16f), new Vec3_1539(1f, 1f, -17f)), 42, 43), // leaf 1
                new(new AABB_1539(new Vec3_1539(-1f, -1f, -16f), new Vec3_1539(1f, 1f, -17f)), 42, 43), // leaf 2
            ];

            var originToTarget = rayTarget - rayOrigin;
            var originToTargetDistance = originToTarget.Length;
            var normalizedDir = originToTarget * (1f / originToTargetDistance);
            var ray = new Ray_1539(rayOrigin, normalizedDir);

            float hitDistance = DistanceToGeometry_1539(ray, nodes);
            bool geometryShadows = hitDistance < originToTargetDistance;
            resultBuffer[index] = geometryShadows ? 1 : 0;
        }

        [TestMethod]
        public async Task BVHRayTraversalTest() => await RunTest(async accelerator =>
        {
            // Upstream #1539: BVH traversal kernel produces wrong hit/miss on AMD OpenCL.
            // On correct backends, all 100 rays should MISS (0 hits).
            const int numberOfPoints = 100;
            using var bufOut = accelerator.Allocate1D<int>(numberOfPoints);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>>(BVHRayTraversalKernel_1539);
            kernel((Index1D)numberOfPoints, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<int>();

            int hits = 0;
            var hitIndices = new List<int>();
            for (int i = 0; i < numberOfPoints; i++)
                if (result[i] == 1) { hits++; hitIndices.Add(i); }

            if (hits != 0)
                throw new Exception($"BVH ray traversal: expected 0 hits (all miss), got {hits} hits out of {numberOfPoints}. Hit indices: [{string.Join(", ", hitIndices)}]");
        });

        // --- Math Intrinsics Extended Test ---
        // Tests operations that were previously missing or broken in browser backends:
        // - MathF.Truncate (Wasm/WebGPU were missing TruncF)
        // - MathF.Floor / MathF.Ceiling (regression test)
        // - MathF.Pow (WebGL stub was broken, returned x instead of x^y)
        // - MathF.Atan2 (WebGL stub was broken, returned y instead of atan2(y,x))
        // - MathF.Sqrt / MathF.Log (regression test)
        static void MathIntrinsicsExtendedKernel(Index1D index, ArrayView<float> output)
        {
            float val = (index + 1) * 1.7f; // 1.7, 3.4, 5.1, 6.8, 8.5, 10.2, 11.9, 13.6

            // Trunc: remove fractional part toward zero
            float truncVal = (float)(int)val; // cast to int truncates toward zero in ILGPU IR → compiles to TruncF
            
            // Floor: round toward negative infinity
            float floorVal = MathF.Floor(val);

            // Ceil: round toward positive infinity
            float ceilVal = MathF.Ceiling(val);

            // Pow: x^2
            float powVal = MathF.Pow(val, 2.0f);

            // Atan2: should give correct quadrant angle
            float atan2Val = MathF.Atan2(val, 1.0f);

            // Sqrt
            float sqrtVal = MathF.Sqrt(val);

            // Log (natural log)
            float logVal = MathF.Log(val);

            // Store all results packed: each index gets 7 floats at index*7
            // But since we have 1 output per thread, combine them into a single checksum
            output[index] = truncVal + floorVal + ceilVal + powVal + atan2Val + sqrtVal + logVal;
        }

        [TestMethod]
        public async Task MathIntrinsicsExtendedTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(MathIntrinsicsExtendedKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();

            for (int i = 0; i < len; i++)
            {
                float val = (i + 1) * 1.7f;

                float truncVal = (float)(int)val;
                float floorVal = MathF.Floor(val);
                float ceilVal = MathF.Ceiling(val);
                float powVal = MathF.Pow(val, 2.0f);
                float atan2Val = MathF.Atan2(val, 1.0f);
                float sqrtVal = MathF.Sqrt(val);
                float logVal = MathF.Log(val);
                float expected = truncVal + floorVal + ceilVal + powVal + atan2Val + sqrtVal + logVal;

                if (MathF.Abs(result[i] - expected) > 0.05f * MathF.Abs(expected) + 0.05f)
                    throw new Exception($"MathIntrinsicsExtended failed at {i}. val={val}, Expected {expected}, got {result[i]}");
            }
        });

        // --- Bit Operations Intrinsics Test ---
        // Tests PopCount, LeadingZeroCount, TrailingZeroCount which compile to
        // UnaryArithmeticKind.PopC, CLZ, CTZ IR nodes.
        static void BitOpsIntrinsicsKernel(Index1D index, ArrayView<int> input, ArrayView<int> output)
        {
            int val = input[index];
            // PopCount: number of set bits
            int popc = IntrinsicMath.PopCount(val);
            // LeadingZeroCount: number of leading zero bits
            int clz = IntrinsicMath.BitOperations.LeadingZeroCount(val);
            // TrailingZeroCount: number of trailing zero bits
            int ctz = IntrinsicMath.BitOperations.TrailingZeroCount(val);
            // Pack results: popc in bits 0-7, clz in bits 8-15, ctz in bits 16-23
            output[index] = popc | (clz << 8) | (ctz << 16);
        }

        [TestMethod]
        public async Task BitOpsIntrinsicsTest() => await RunTest(async accelerator =>
        {
            var input = new int[]
            {
                0,          // popc=0, clz=32, ctz=32
                1,          // popc=1, clz=31, ctz=0
                -1,         // popc=32, clz=0, ctz=0  (0xFFFFFFFF)
                0x80,       // popc=1, clz=24, ctz=7   (128)
                0xFF00,     // popc=8, clz=16, ctz=8
                0x12345678, // popc=13, clz=3, ctz=3
                int.MinValue, // popc=1, clz=0, ctz=31  (0x80000000)
                0x7FFFFFFF, // popc=31, clz=1, ctz=0  (int.MaxValue)
            };
            int len = input.Length;

            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<int>(len);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<int>>(BitOpsIntrinsicsKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<int>();

            for (int i = 0; i < len; i++)
            {
                int val = input[i];
                int expectedPopC = System.Numerics.BitOperations.PopCount((uint)val);
                int expectedCLZ = System.Numerics.BitOperations.LeadingZeroCount((uint)val);
                int expectedCTZ = System.Numerics.BitOperations.TrailingZeroCount((uint)val);
                int expected = expectedPopC | (expectedCLZ << 8) | (expectedCTZ << 16);

                if (result[i] != expected)
                {
                    int gotPopC = result[i] & 0xFF;
                    int gotCLZ = (result[i] >> 8) & 0xFF;
                    int gotCTZ = (result[i] >> 16) & 0xFF;
                    throw new Exception(
                        $"BitOps failed at {i} (val=0x{val:X8}). " +
                        $"PopC: expected {expectedPopC} got {gotPopC}, " +
                        $"CLZ: expected {expectedCLZ} got {gotCLZ}, " +
                        $"CTZ: expected {expectedCTZ} got {gotCTZ}");
                }
            }
        });

        #endregion
    }
}
