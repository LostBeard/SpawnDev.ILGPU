using ILGPU;
using ILGPU.Algorithms.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

public class CudaTests : BackendTestBase
{
    public CudaTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
    protected override string BackendName => "CUDA";

    protected override Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
    {
        var context = Context.Create(builder => builder.AllAccelerators().EnableAlgorithms());
        var cudaDevices = context.GetCudaDevices();
        if (cudaDevices.Count == 0)
        {
            context.Dispose();
            throw new UnsupportedTestException("No CUDA devices found");
        }
        var accelerator = cudaDevices[0].CreateAccelerator(context);
        return Task.FromResult<(Context, Accelerator)>((context, accelerator));
    }

    // cuDriverGetVersion can report a newer CUDA than the driver's PTX JIT accepts
    // (430.x reports 10.2 but its JIT stops at PTX ISA 6.4); the accelerator must end up
    // on an ISA the driver actually loads.
    [TestMethod]
    public async Task Tests_PTX_AcceleratorInstructionSet_IsAcceptedByDriverJit() =>
        await RunTest(async accelerator =>
    {
        var cuda = (CudaAccelerator)accelerator;
        cuda.Bind();
        var ptx = $".version {cuda.InstructionSet}\n" +
            $".target {cuda.Architecture.ToString().ToLowerInvariant()}\n" +
            ".address_size 64\n";
        var error = CudaAPI.CurrentAPI.LoadModule(out var module, ptx, out var log);
        if (error == CudaError.CUDA_SUCCESS)
            CudaAPI.CurrentAPI.DestroyModule(module);
        else
            throw new Exception(
                $"driver JIT rejected the accelerator's PTX ISA {cuda.InstructionSet} ({error}): {log}");
        await Task.CompletedTask;
    });

    // Half arithmetic and compares on hostile bit patterns against System.Half (IEEE, RNE).
    // Devices below sm_53 have no f16 arithmetic and run this through PTXFloat16Emulation;
    // native devices run the same kernel on the real instructions.
    static void HalfHostileOpsKernel(
        Index1D idx,
        ArrayView<global::ILGPU.Half> a,
        ArrayView<global::ILGPU.Half> b,
        ArrayView<global::ILGPU.Half> sum,
        ArrayView<global::ILGPU.Half> difference,
        ArrayView<global::ILGPU.Half> product,
        ArrayView<int> flags)
    {
        var x = a[idx];
        var y = b[idx];
        sum[idx] = x + y;
        difference[idx] = x - y;
        product[idx] = x * y;
        int f = 0;
        if (x < y) f |= 1;
        if (x <= y) f |= 2;
        if (x == y) f |= 4;
        if (x != y) f |= 8;
        flags[idx] = f;
    }

    [TestMethod]
    public async Task Tests_PTX_Float16_ArithmeticAndCompare_MatchCpuOracle() =>
        await RunTest(async accelerator =>
    {
        // +0, -0, min/max subnormal, min normal, 1, -1, 1+2^-10, 2^-11 (exact tie against 1),
        // 2^-24 (below the tie), max finite, +inf, -inf, two NaNs, 1/3, 2048.
        ushort[] patterns =
        {
            0x0000, 0x8000, 0x0001, 0x03FF, 0x0400, 0x3C00, 0xBC00, 0x3C01,
            0x1000, 0x0100, 0x7BFF, 0x7C00, 0xFC00, 0x7E00, 0xFE01, 0x3555, 0x6800,
        };
        int n = patterns.Length * patterns.Length;
        var left = new global::ILGPU.Half[n];
        var right = new global::ILGPU.Half[n];
        for (int i = 0; i < patterns.Length; i++)
        for (int j = 0; j < patterns.Length; j++)
        {
            left[i * patterns.Length + j] = global::ILGPU.Half.FromRawBits(patterns[i]);
            right[i * patterns.Length + j] = global::ILGPU.Half.FromRawBits(patterns[j]);
        }
        using var aBuf = accelerator.Allocate1D(left);
        using var bBuf = accelerator.Allocate1D(right);
        using var sumBuf = accelerator.Allocate1D<global::ILGPU.Half>(n);
        using var diffBuf = accelerator.Allocate1D<global::ILGPU.Half>(n);
        using var prodBuf = accelerator.Allocate1D<global::ILGPU.Half>(n);
        using var flagBuf = accelerator.Allocate1D<int>(n);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>,
            ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>,
            ArrayView<global::ILGPU.Half>, ArrayView<int>>(HalfHostileOpsKernel);
        kernel((Index1D)n, aBuf.View, bBuf.View, sumBuf.View, diffBuf.View, prodBuf.View,
            flagBuf.View);
        await accelerator.SynchronizeAsync();
        var sum = await sumBuf.CopyToHostAsync<global::ILGPU.Half>();
        var diff = await diffBuf.CopyToHostAsync<global::ILGPU.Half>();
        var prod = await prodBuf.CopyToHostAsync<global::ILGPU.Half>();
        var flags = await flagBuf.CopyToHostAsync<int>();

        var errors = new List<string>();
        void Check(string op, int k, System.Half expected, global::ILGPU.Half actual)
        {
            // Any NaN matches any NaN; everything else must match bit for bit (incl. -0).
            bool ok = System.Half.IsNaN(expected)
                ? (actual.RawValue & 0x7FFF) > 0x7C00
                : actual.RawValue == BitConverter.HalfToUInt16Bits(expected);
            if (!ok && errors.Count < 8)
                errors.Add($"{op}(0x{patterns[k / patterns.Length]:X4}, " +
                    $"0x{patterns[k % patterns.Length]:X4}): expected " +
                    $"0x{BitConverter.HalfToUInt16Bits(expected):X4}, got 0x{actual.RawValue:X4}");
        }
        for (int k = 0; k < n; k++)
        {
            var x = BitConverter.UInt16BitsToHalf(patterns[k / patterns.Length]);
            var y = BitConverter.UInt16BitsToHalf(patterns[k % patterns.Length]);
            Check("add", k, x + y, sum[k]);
            Check("sub", k, x - y, diff[k]);
            Check("mul", k, x * y, prod[k]);
            int expectedFlags = (x < y ? 1 : 0) | (x <= y ? 2 : 0) | (x == y ? 4 : 0)
                | (x != y ? 8 : 0);
            if (flags[k] != expectedFlags && errors.Count < 8)
                errors.Add($"compare(0x{patterns[k / patterns.Length]:X4}, " +
                    $"0x{patterns[k % patterns.Length]:X4}): expected flags {expectedFlags}, " +
                    $"got {flags[k]}");
        }
        if (errors.Count > 0)
            throw new Exception("Half ops differ from the CPU oracle: " +
                string.Join("; ", errors));
    });

    // PTX vector memory intrinsics (Discussion #5 / PR #4 additions)
    // ld.v2.f32 vectorized load + st.v2.f32 vectorized store
    static void PTXVecF32x2CopyKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        int i = idx * 2;
        Float2 v = PTXMemory.LoadF32x2(ref src[i]);
        PTXMemory.StoreF32x2(ref dst[i], v);
    }

    [TestMethod]
    public async Task Tests_PTXVectorMemory_F32x2_LoadStore() =>
        await RunTest(async accelerator =>
    {
        const int N = 64; // must be multiple of 2
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 1.5f + 0.1f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(PTXVecF32x2CopyKernel);
        kernel((Index1D)(N / 2), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"PTXVecF32x2: index {i}: expected {input[i]}, got {output[i]}");
    });

    // ld.v4.f32 vectorized load + st.v4.f32 vectorized store
    static void PTXVecF32x4CopyKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        int i = idx * 4;
        Float4 v = PTXMemory.LoadF32x4(ref src[i]);
        PTXMemory.StoreF32x4(ref dst[i], v);
    }

    [TestMethod]
    public async Task Tests_PTXVectorMemory_F32x4_LoadStore() =>
        await RunTest(async accelerator =>
    {
        const int N = 64; // must be multiple of 4
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 2.0f - 5.5f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(PTXVecF32x4CopyKernel);
        kernel((Index1D)(N / 4), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"PTXVecF32x4: index {i}: expected {input[i]}, got {output[i]}");
    });

    // st.v2.f32 via scalar (x, y) form - each thread reads two scalars then stores vectorized
    static void PTXVecF32x2StoreScalarsKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        int i = idx * 2;
        PTXMemory.StoreF32x2(ref dst[i], src[i], src[i + 1]);
    }

    [TestMethod]
    public async Task Tests_PTXVectorMemory_F32x2_StoreScalars() =>
        await RunTest(async accelerator =>
    {
        const int N = 64;
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 0.75f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(PTXVecF32x2StoreScalarsKernel);
        kernel((Index1D)(N / 2), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"PTXVecF32x2Scalars: index {i}: expected {input[i]}, got {output[i]}");
    });

    // st.v4.f32 via scalar (x, y, z, w) form
    static void PTXVecF32x4StoreScalarsKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        int i = idx * 4;
        PTXMemory.StoreF32x4(ref dst[i], src[i], src[i + 1], src[i + 2], src[i + 3]);
    }

    [TestMethod]
    public async Task Tests_PTXVectorMemory_F32x4_StoreScalars() =>
        await RunTest(async accelerator =>
    {
        const int N = 64;
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 1.25f - 10.0f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(PTXVecF32x4StoreScalarsKernel);
        kernel((Index1D)(N / 4), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"PTXVecF32x4Scalars: index {i}: expected {input[i]}, got {output[i]}");
    });

    // ArrayView LoadVectorized / StoreVectorized using Float2 (vectorized copy via aligned cast)
    static void VectorizedFloat2CopyKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        // LoadVectorized loads a Float2 at element-index (idx*2) with 8-byte alignment
        var v = src.LoadVectorized<float, Float2>(idx * 2, 8);
        dst.StoreVectorized<float, Float2>(idx * 2, v, 8);
    }

    [TestMethod]
    public async Task Tests_ArrayView_LoadVectorized_Float2() =>
        await RunTest(async accelerator =>
    {
        const int N = 64;
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 3.0f + 1.0f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(VectorizedFloat2CopyKernel);
        kernel((Index1D)(N / 2), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"LoadVectorizedFloat2: index {i}: expected {input[i]}, got {output[i]}");
    });

    /// <summary>Test holder of idle device memory: gives buffers back newest-first when asked.</summary>
    sealed class IdleMemoryHolder : IDeviceMemoryReclaimer
    {
        public readonly List<MemoryBuffer1D<byte, Stride1D.Dense>> Idle = new();
        public long Reclaimed;
        public long LastUsedTimestamp { get; set; }
        public long ReclaimableBytes { get { long n = 0; foreach (var b in Idle) n += b.LengthInBytes; return n; } }
        public long Reclaim(long bytesWanted)
        {
            long freed = 0;
            while (freed < bytesWanted && Idle.Count > 0)
            {
                var b = Idle[^1];
                Idle.RemoveAt(Idle.Count - 1);
                freed += b.LengthInBytes;
                b.Dispose();
            }
            Reclaimed += freed;
            return freed;
        }
        public void DisposeAll() { foreach (var b in Idle) b.Dispose(); Idle.Clear(); }
    }

    /// <summary>
    /// An allocation that would cut into the headroom makes a registered holder give back idle memory BEFORE the
    /// allocation, so the device never runs out. A driver with CUDA sysmem fallback (Windows' default) never fails
    /// the allocation - it spills it into system RAM - so reclaiming only after a failure never ran there
    /// (Vertex 2026-10-12: 7.3 GB idle in buffer pools, 1.4 GB spilled, replies took minutes). Red-check: without
    /// the pre-allocation check the three new chunks spill and free memory ends below the headroom.
    /// </summary>
    [TestMethod]
    public async Task Tests_MemoryPressure_ReclaimsIdleMemoryBeforeAllocating() =>
        await RunTest(async accelerator =>
    {
        if (!accelerator.TryGetFreeMemory(out long free))
            throw new UnsupportedTestException("backend cannot report free device memory");
        const long Chunk = 256L * 1024 * 1024;
        const long Headroom = 512L * 1024 * 1024;
        accelerator.DeviceMemoryHeadroomBytes = Headroom;
        var holder = new IdleMemoryHolder();
        var fresh = new List<MemoryBuffer1D<byte, Stride1D.Dense>>();
        try
        {
            // Fill the device with idle memory until only headroom + 2 chunks are free (nothing registered yet,
            // so this filling does not reclaim anything).
            while (free > Headroom + 2 * Chunk)
            {
                holder.Idle.Add(accelerator.Allocate1D<byte>(Chunk));
                accelerator.TryGetFreeMemory(out free);
            }
            if (holder.Idle.Count < 4)
                throw new UnsupportedTestException($"too little free device memory to test ({free / 1048576} MiB)");
            accelerator.RegisterReclaimer(holder);

            // Three more chunks do not fit above the headroom: the holder must make room first.
            for (int i = 0; i < 3; i++)
                fresh.Add(accelerator.Allocate1D<byte>(Chunk));
            foreach (var b in fresh)
                b.MemSetToZero();
            await accelerator.SynchronizeAsync();
            accelerator.TryGetFreeMemory(out long after);

            if (holder.Reclaimed < Chunk)
                throw new Exception($"holder gave back {holder.Reclaimed / 1048576} MiB; expected at least one {Chunk / 1048576} MiB chunk");
            // Allow half a chunk of slack for the driver's own bookkeeping; a spill leaves far less than this.
            if (after < Headroom - Chunk / 2)
                throw new Exception($"free device memory fell to {after / 1048576} MiB, below the {Headroom / 1048576} MiB headroom: the allocations spilled");
        }
        finally
        {
            accelerator.UnregisterReclaimer(holder);
            accelerator.DeviceMemoryHeadroomBytes = null;
            foreach (var b in fresh) b.Dispose();
            holder.DisposeAll();
        }
    });

    /// <summary>The least recently used holder gives memory back first; the active one keeps its buffers.</summary>
    [TestMethod]
    public async Task Tests_MemoryPressure_LeastRecentlyUsedHolderGivesBackFirst() =>
        await RunTest(async accelerator =>
    {
        const long Chunk = 16L * 1024 * 1024;
        var idle = new IdleMemoryHolder { LastUsedTimestamp = 100 };
        var active = new IdleMemoryHolder { LastUsedTimestamp = 200 };
        try
        {
            for (int i = 0; i < 2; i++)
            {
                idle.Idle.Add(accelerator.Allocate1D<byte>(Chunk));
                active.Idle.Add(accelerator.Allocate1D<byte>(Chunk));
            }
            // Registered active-first so list order cannot pass for LRU order.
            accelerator.RegisterReclaimer(active);
            accelerator.RegisterReclaimer(idle);
            long freed = accelerator.ReclaimDeviceMemory(Chunk + Chunk / 2);
            if (freed != 2 * Chunk || idle.Reclaimed != 2 * Chunk || active.Reclaimed != 0)
                throw new Exception($"freed {freed}, least recent gave {idle.Reclaimed}, most recent gave {active.Reclaimed}; " +
                                    $"expected the least recent to give both its chunks and the most recent none");
            freed = accelerator.ReclaimDeviceMemory(Chunk);
            if (freed != Chunk || active.Reclaimed != Chunk)
                throw new Exception($"second reclaim freed {freed}; the most recent holder should give one chunk once the other is empty");
        }
        finally
        {
            accelerator.UnregisterReclaimer(idle);
            accelerator.UnregisterReclaimer(active);
            idle.DisposeAll();
            active.DisposeAll();
        }
        await Task.CompletedTask;
    });
}
