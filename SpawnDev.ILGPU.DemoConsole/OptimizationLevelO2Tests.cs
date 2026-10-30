using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Velocity;
using SpawnDev.ILGPU;
using SpawnDev.UnitTesting;

/// <summary>
/// Kernels compiled at <see cref="OptimizationLevel.O2"/> in their own context, so they guard O2 whatever the default is
/// (it was O1 when they were written; O2 since 5.4.9). A full sweep with O2 as the default (2026-10-27) failed 50 tests
/// that pass at O1; these guard the causes. The OpenCL one lives in <see cref="OpenCLTests"/> (the serialized opencl lane).
/// </summary>
public class OptimizationLevelO2Tests
{
    const int BarrierLoopGroupSize = 64;

    static void BarrierLoopKernel(ArrayView<int> data, ArrayView<int> output, int start, int end, int scale)
    {
        var shared = SharedMemory.Allocate<int>(BarrierLoopGroupSize);
        int t = Group.IdxX;
        int g = Grid.GlobalIndex.X;
        int acc = 0;
        for (int d = start; d < end; d++)
        {
            shared[t] = data[g] * d + scale;
            Group.Barrier();
            acc += shared[(t + d) % BarrierLoopGroupSize];
            Group.Barrier();
        }
        output[g] = acc;
    }

    // O2's aggressive CodePlacement rebuilds a block bottom-up: `acc += shared[j]` is placed first (it feeds the loop
    // phi), and its shared load moved below the Group.Barrier() that ends the body, because Movement's sweep skipped the
    // block's last memory value. Every other thread could then overwrite the slot first: a race on every backend at O2.
    // Velocity runs a group's warps one after another, so it gets it wrong every time (191 of 192 outputs); CUDA and
    // OpenCL survived by running in near lockstep.
    static async Task BarrierLoopAtO2(VelocityDeviceType deviceType)
    {
        using var context = Context.Create(builder => builder.Velocity(deviceType).Optimize(OptimizationLevel.O2));
        using var accelerator = context.GetVelocityDevice(0).CreateAccelerator(context);
        const int groups = 3, n = groups * BarrierLoopGroupSize, start = 2, end = 9, scale = 11;
        var data = new int[n];
        for (int i = 0; i < n; i++) data[i] = i * 3 - 70;
        using var dData = accelerator.Allocate1D(data);
        using var dOut = accelerator.Allocate1D<int>(n);
        accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, int, int, int>(BarrierLoopKernel)(
            new KernelConfig(groups, BarrierLoopGroupSize), dData.View, dOut.View, start, end, scale);
        await accelerator.SynchronizeAsync();
        var got = await dOut.CopyToHostAsync<int>();
        for (int g = 0; g < n; g++)
        {
            int group = g / BarrierLoopGroupSize, t = g % BarrierLoopGroupSize, want = 0;
            for (int d = start; d < end; d++)
                want += data[group * BarrierLoopGroupSize + (t + d) % BarrierLoopGroupSize] * d + scale;
            if (got[g] != want)
                throw new Exception($"{deviceType} at O2: output[{g}] = {got[g]}, expected {want}");
        }
    }

    [TestMethod]
    public Task O2_BarrierLoop_SharedLoadStaysBetweenBarriers_VelocityScalar2() => BarrierLoopAtO2(VelocityDeviceType.Scalar2);

    [TestMethod]
    public Task O2_BarrierLoop_SharedLoadStaysBetweenBarriers_VelocityVector128() => BarrierLoopAtO2(VelocityDeviceType.Vector128);

    // SpawnDev.ILGPU.ML's naive Conv2DImpl (one thread per output, bounds `continue`s in the ky/kx loops), as of ML 1aad20be.
    static void NaiveConvKernel(Index1D idx, ArrayView<float> input, ArrayView<float> weight, ArrayView<float> bias,
        ArrayView<float> output, int inC, int inH, int inW, int k, int pad, int outH, int outW)
    {
        int ox = idx % outW;
        int rem = idx / outW;
        int oy = rem % outH;
        int oc = rem / outH;
        float sum = bias[oc];
        for (int ic = 0; ic < inC; ic++)
        {
            int icBase = ic * inH * inW;
            int wcBase = oc * inC * k * k + ic * k * k;
            for (int ky = 0; ky < k; ky++)
            {
                int iy = oy + ky - pad;
                if (iy < 0 || iy >= inH) continue;
                for (int kx = 0; kx < k; kx++)
                {
                    int ix = ox + kx - pad;
                    if (ix < 0 || ix >= inW) continue;
                    sum += input[icBase + iy * inW + ix] * weight[wcBase + ky * k + kx];
                }
            }
        }
        output[idx] = sum;
    }

    // O2's CodePlacement put every value at the common dominator of its uses with no regard for loops, so it sank what
    // LoopInvariantCodeMotion had just hoisted back into the innermost loop: `idx % outW` (a per-lane integer remainder on
    // Velocity) and the address math ran once per kx step. ptxas and the OpenCL driver hoist it again; Velocity's IL does
    // not, and ran ML's style-mosaic Conv nodes 1.74x slower at O2 than at O1 (Tuvok, 2026-10-30). Timed against O1 in the
    // same process, interleaved, best of 5: 1.76x before the fix, ~1.0x after.
    [TestMethod]
    public async Task O2_NaiveConv_NotSlowerThanO1_VelocityVector128()
    {
        const int inC = 64, outC = 64, size = 56, k = 3, pad = 1, n = outC * size * size, reps = 5;
        var rnd = new Random(7);
        var input = new float[inC * size * size];
        var weight = new float[outC * inC * k * k];
        var bias = new float[outC];
        for (int i = 0; i < input.Length; i++) input[i] = (float)rnd.NextDouble() - 0.5f;
        for (int i = 0; i < weight.Length; i++) weight[i] = (float)rnd.NextDouble() - 0.5f;
        for (int i = 0; i < bias.Length; i++) bias[i] = (float)rnd.NextDouble() - 0.5f;

        var levels = new[] { OptimizationLevel.O1, OptimizationLevel.O2 };
        var contexts = new List<Context>();
        var accelerators = new List<Accelerator>();
        var buffers = new List<IDisposable>();
        try
        {
            var runs = new List<Action>();
            var outputs = new List<MemoryBuffer1D<float, Stride1D.Dense>>();
            foreach (var level in levels)
            {
                var context = Context.Create(builder => builder.Velocity(VelocityDeviceType.Vector128).Optimize(level));
                contexts.Add(context);
                var accelerator = context.GetVelocityDevice(0).CreateAccelerator(context);
                accelerators.Add(accelerator);
                var dIn = accelerator.Allocate1D(input);
                var dW = accelerator.Allocate1D(weight);
                var dB = accelerator.Allocate1D(bias);
                var dOut = accelerator.Allocate1D<float>(n);
                buffers.AddRange(new IDisposable[] { dIn, dW, dB, dOut });
                outputs.Add(dOut);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>,
                    ArrayView<float>, ArrayView<float>, int, int, int, int, int, int, int>(NaiveConvKernel);
                void Run()
                {
                    kernel(n, dIn.View, dW.View, dB.View, dOut.View, inC, size, size, k, pad, size, size);
                    accelerator.Synchronize();
                }
                Run(); // compile + warm up
                runs.Add(Run);
            }

            var best = new[] { double.MaxValue, double.MaxValue };
            var sw = new System.Diagnostics.Stopwatch();
            for (int r = 0; r < reps; r++)
            {
                for (int l = 0; l < levels.Length; l++)
                {
                    sw.Restart();
                    runs[l]();
                    best[l] = Math.Min(best[l], sw.Elapsed.TotalMilliseconds);
                }
            }

            for (int l = 0; l < levels.Length; l++)
            {
                var got = await outputs[l].CopyToHostAsync<float>();
                for (int i = 0; i < n; i += 61)
                {
                    int ox = i % size, rem = i / size, oy = rem % size, oc = rem / size;
                    float want = bias[oc];
                    for (int ic = 0; ic < inC; ic++)
                        for (int ky = 0; ky < k; ky++)
                        {
                            int iy = oy + ky - pad;
                            if (iy < 0 || iy >= size) continue;
                            for (int kx = 0; kx < k; kx++)
                            {
                                int ix = ox + kx - pad;
                                if (ix < 0 || ix >= size) continue;
                                want += input[ic * size * size + iy * size + ix] * weight[oc * inC * k * k + ic * k * k + ky * k + kx];
                            }
                        }
                    if (Math.Abs(got[i] - want) > 1e-3f)
                        throw new Exception($"{levels[l]}: output[{i}] = {got[i]}, expected {want}");
                }
            }

            double ratio = best[1] / best[0];
            Console.WriteLine($"Velocity naive conv: O1 {best[0]:F1} ms, O2 {best[1]:F1} ms, O2/O1 {ratio:F2}");
            if (ratio > 1.3)
                throw new Exception($"O2 is {ratio:F2}x slower than O1 on Velocity (O1 {best[0]:F1} ms, O2 {best[1]:F1} ms)");
        }
        finally
        {
            foreach (var buffer in buffers) buffer.Dispose();
            foreach (var accelerator in accelerators) accelerator.Dispose();
            foreach (var context in contexts) context.Dispose();
        }
    }
}
