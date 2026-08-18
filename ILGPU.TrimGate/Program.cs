// ILGPU trim gate.
//
// Exercises the code paths that resolve members BY NAME at runtime, which the IL
// trimmer cannot see. Under a trimmed publish these fail either loudly
// (MissingMethodException / "Not supported intrinsic type" / null constructor) or
// SILENTLY (a struct field trimmed away changes the GPU layout and the kernel
// returns wrong numbers). Both classes are covered below.
using System;
using System.Linq;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Frontend.Intrinsic;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;

int failures = 0;

void Step(string name, Action action)
{
    try { action(); Console.WriteLine($"  OK   {name}"); }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"  FAIL {name}");
        for (var e = ex; e != null; e = e.InnerException)
            Console.WriteLine($"       {e.GetType().Name}: {e.Message}");
        Console.WriteLine(ex.StackTrace);
    }
}

Console.WriteLine("ILGPU trim gate");

Step("RemappedIntrinsics..cctor", () =>
    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
        typeof(RemappedIntrinsics).TypeHandle));

Step("AlgorithmContext..cctor", () =>
    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
        typeof(AlgorithmContext).TypeHandle));

Context ctx = null;
Step("Context.Create(EnableAlgorithms)", () =>
    ctx = Context.Create(b => b.Default().EnableAlgorithms()));

CPUAccelerator acc = null;
Step("CreateCPUAccelerator", () => acc = ctx.CreateCPUAccelerator(0));

// 1. System.Math remapping - the overload that was trimmed away in SpawnDev.AI.
Step("kernel: Math.Clamp(double)", () =>
{
    var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, double>(
        (i, v, s) => v[i] = Math.Clamp(i * s, 0.0, 10.0));
    using var buf = acc.Allocate1D<double>(8);
    k((int)buf.Length, buf.View, 2.0);
    acc.Synchronize();
    Expect(buf.GetAsArray1D(),
        Enumerable.Range(0, 8).Select(i => Math.Clamp(i * 2.0, 0.0, 10.0)).ToArray(),
        "Math.Clamp");
});

// 2. MathF + XMath remapping (ILGPU.Algorithms intrinsic tables).
Step("kernel: XMath/MathF", () =>
{
    var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(
        (i, v) => v[i] = XMath.Sqrt(MathF.Abs((float)(int)i - 4f)) + XMath.Rcp(2f));
    using var buf = acc.Allocate1D<float>(8);
    k((int)buf.Length, buf.View);
    acc.Synchronize();
    var want = Enumerable.Range(0, 8)
        .Select(i => MathF.Sqrt(MathF.Abs(i - 4f)) + 0.5f).ToArray();
    Expect(buf.GetAsArray1D(), want, "XMath", 1e-5f);
});

// 3. SILENT-CORRUPTION GUARD: a struct kernel parameter. ILGPU lays the struct out
//    on the device by walking type.GetFields via reflection. If the trimmer removes
//    a field the app never touches directly, the layout shifts and the kernel
//    returns wrong values WITHOUT throwing.
Step("kernel: struct parameter layout", () =>
{
    var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, Payload>(
        (i, v, p) => v[i] = p.A + p.B * 2f + p.C * 3f + (p.Flag != 0 ? 1000f : 0f));
    using var buf = acc.Allocate1D<float>(4);
    var payload = new Payload { A = 1f, NeverReadPadding = 7f, B = 10f, C = 100f, Flag = 1 };
    k((int)buf.Length, buf.View, payload);
    acc.Synchronize();
    var want = Enumerable.Repeat(1f + 20f + 300f + 1000f, 4).ToArray();
    Expect(buf.GetAsArray1D(), want, "struct layout");
});

// 5. Lambda Kernels: captured scalars live in a compiler-generated display class,
//    and ILGPU reads that class's fields by reflection. Nothing in the app reads
//    them back directly, so this is the same "trimmed field" hazard as the struct.
Step("kernel: captured scalar (lambda kernel)", () =>
{
    int capturedMul = 7;
    float capturedAdd = 1.5f;
    var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(
        (i, v) => v[i] = i * capturedMul + capturedAdd);
    using var buf = acc.Allocate1D<float>(6);
    k((int)buf.Length, buf.View);
    acc.Synchronize();
    var want = Enumerable.Range(0, 6).Select(i => i * 7 + 1.5f).ToArray();
    Expect(buf.GetAsArray1D(), want, "captured scalar");
});

// 4. ILGPU.Algorithms host-level pipeline (Scan) - what the ML stack actually uses.
Step("algorithms: Scan", () =>
{
    var scan = acc.CreateScan<int, Stride1D.Dense, Stride1D.Dense,
        ILGPU.Algorithms.ScanReduceOperations.AddInt32>(ScanKind.Inclusive);
    var input = Enumerable.Range(1, 16).ToArray();
    using var src = acc.Allocate1D(input);
    using var dst = acc.Allocate1D<int>(input.Length);
    var tempSize = acc.ComputeScanTempStorageSize<int>(dst.Length);
    using var temp = acc.Allocate1D<int>(Math.Max(tempSize, 1));
    scan(acc.DefaultStream, src.View, dst.View, temp.View);
    acc.Synchronize();
    var want = new int[input.Length];
    var run = 0;
    for (int i = 0; i < input.Length; i++) { run += input[i]; want[i] = run; }
    Expect(dst.GetAsArray1D(), want, "Scan");
});

acc?.Dispose();
ctx?.Dispose();
Console.WriteLine(failures == 0 ? "TRIM GATE: PASS" : $"TRIM GATE: FAIL ({failures})");
return failures == 0 ? 0 : 1;

static void Expect<T>(T[] got, T[] want, string what, double tol = 0)
    where T : IConvertible
{
    if (got.Length != want.Length)
        throw new InvalidOperationException(
            $"{what}: length {got.Length}, want {want.Length}");
    for (int i = 0; i < got.Length; i++)
    {
        var g = got[i].ToDouble(null);
        var w = want[i].ToDouble(null);
        if (Math.Abs(g - w) > tol)
            throw new InvalidOperationException(
                $"{what}: element {i} = {g}, want {w} " +
                $"(got [{string.Join(", ", got)}])");
    }
    Console.WriteLine($"       {what} verified: [{string.Join(", ", got)}]");
}

// Deliberately contains a field the host code never reads directly, so a trimmed
// field would shift the device-side layout.
public struct Payload
{
    public float A;
    public float NeverReadPadding;
    public float B;
    public float C;
    public int Flag;
}
