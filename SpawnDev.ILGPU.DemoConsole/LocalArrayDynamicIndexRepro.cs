using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

/// <summary>
/// Repro for Tuvok's codegen gap: a per-thread device-LOCAL array (`new float[N]`,
/// compile-time size) that is WRITTEN and READ by a RUNTIME index inside a loop throws
/// "An internal compiler error has been detected" at kernel JIT on CUDA. Caps the
/// universal per-query (flash-class) attention at the shared-mem workaround (5.3x) instead
/// of register accumulators.
///
/// This isolates the pattern in SpawnDev.ILGPU (no ML dependency) and prints the FULL
/// inner exception chain so we see the actual codegen failure, not the wrapper.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- local-array-dyn
/// </summary>
internal static class LocalArrayDynamicIndexRepro
{
    const int N = 256;

    // Variant A: fixed-size local array, runtime-index WRITE in a loop, then runtime-index READ.
    static void DynWriteReadKernel(Index1D idx, ArrayView<float> output, int D)
    {
        var acc = new float[N];
        for (int dd = 0; dd < D; dd++) acc[dd] = 0f;            // dynamic write
        for (int k = 0; k < 4; k++)
            for (int dd = 0; dd < D; dd++)
                acc[dd] = acc[dd] * 0.5f + (float)k;            // dynamic read+write in loop
        float s = 0f;
        for (int dd = 0; dd < D; dd++) s += acc[dd];           // dynamic read
        output[idx] = s;
    }

    // Variant B: the online-softmax accumulator shape (correction * acc + weight*V).
    static void OnlineSoftmaxShapeKernel(Index1D idx, ArrayView<float> v, ArrayView<float> output, int D, int SKV)
    {
        var acc = new float[N];
        for (int dd = 0; dd < D; dd++) acc[dd] = 0f;
        float correction = 0.97f;
        for (int j = 0; j < SKV; j++)
        {
            float weight = 1.0f / (j + 1);
            for (int dd = 0; dd < D; dd++)
                acc[dd] = acc[dd] * correction + weight * v[(j % 4) * D + dd];
        }
        float s = 0f;
        for (int dd = 0; dd < D; dd++) s += acc[dd];
        output[idx] = s;
    }

    // Variant C: generic <T> kernel (Tuvok's is generic; the array stays float).
    static void GenericDynKernel<T>(Index1D idx, ArrayView<float> output, int D)
        where T : unmanaged
    {
        var acc = new float[N];
        for (int dd = 0; dd < D; dd++) acc[dd] = 0f;
        for (int dd = 0; dd < D; dd++) acc[dd] = acc[dd] + dd;
        float s = 0f;
        for (int dd = 0; dd < D; dd++) s += acc[dd];
        output[idx] = s;
    }

    public static async Task<int> Run()
    {
        using var context = Context.Create(b => b.Cuda());
        var dev = context.GetCudaDevice(0);
        if (dev == null) { Console.WriteLine("[local-array-dyn] no CUDA device"); return 1; }
        using var acc = dev.CreateCudaAccelerator(context);
        Console.WriteLine($"[local-array-dyn] {acc.Name}");

        int fails = 0;
        fails += TryVariant("A: dyn write+read in loop", () =>
        {
            var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, int>(DynWriteReadKernel);
            using var outBuf = acc.Allocate1D<float>(4);
            k((int)outBuf.Length, outBuf.View, 128);
            acc.Synchronize();
            var r = outBuf.GetAsArray1D();
            // CPU ref: acc[dd] starts 0; loop k=0..3: acc=acc*0.5+k. After k=0..3, each elem = same value; sum=128*val.
            float val = 0f; for (int kk = 0; kk < 4; kk++) val = val * 0.5f + kk;
            float expect = val * 128;
            Console.WriteLine($"    got {r[0]}, expect {expect}");
        });

        fails += TryVariant("B: online-softmax shape", () =>
        {
            var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int>(OnlineSoftmaxShapeKernel);
            using var v = acc.Allocate1D<float>(4 * 128);
            v.MemSetToZero(acc.DefaultStream);
            using var outBuf = acc.Allocate1D<float>(4);
            k((int)outBuf.Length, v.View, outBuf.View, 128, 8);
            acc.Synchronize();
            Console.WriteLine($"    compiled+ran (V all zero -> out {outBuf.GetAsArray1D()[0]})");
        });

        fails += TryVariant("C: generic <T> kernel", () =>
        {
            var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, int>(GenericDynKernel<float>);
            using var outBuf = acc.Allocate1D<float>(4);
            k((int)outBuf.Length, outBuf.View, 128);
            acc.Synchronize();
            Console.WriteLine($"    compiled+ran -> out {outBuf.GetAsArray1D()[0]}");
        });

        await Task.CompletedTask;
        Console.WriteLine($"[local-array-dyn] {(fails == 0 ? "ALL COMPILED" : fails + " VARIANT(S) FAILED")}");
        return fails == 0 ? 0 : 1;
    }

    private static int TryVariant(string name, Action body)
    {
        Console.WriteLine($"[local-array-dyn] variant {name}");
        try { body(); Console.WriteLine($"    -> OK"); return 0; }
        catch (Exception ex)
        {
            Console.WriteLine($"    -> FAILED: {ex.GetType().Name}: {ex.Message}");
            // Flatten AggregateException to reach the real codegen exception + its stack.
            var flat = (ex.InnerException as AggregateException)?.Flatten();
            if (flat != null)
            {
                foreach (var e in flat.InnerExceptions)
                {
                    Console.WriteLine($"       >> {e.GetType().FullName}: {e.Message}");
                    var st = (e.StackTrace ?? "").Split('\n');
                    for (int i = 0; i < Math.Min(14, st.Length); i++) Console.WriteLine($"          {st[i].Trim()}");
                }
            }
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 6)
            {
                Console.WriteLine($"       inner[{depth}] {inner.GetType().FullName}: {inner.Message}");
                inner = inner.InnerException; depth++;
            }
            if (ex.InnerException == null)
            {
                var st = (ex.StackTrace ?? "").Split('\n');
                for (int i = 0; i < Math.Min(10, st.Length); i++) Console.WriteLine($"       {st[i].Trim()}");
            }
            return 1;
        }
    }
}
