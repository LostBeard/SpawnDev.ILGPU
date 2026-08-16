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
}
