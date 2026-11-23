using System.Text.RegularExpressions;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Backends.EntryPoints;
using ILGPU.Runtime;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

/// <summary>
/// `DemoConsole -- clearcache-wgsl`: does Context.ClearCache(Default) (accelerator.ReleaseCompilerCaches) change the WGSL the
/// WebGPU backend generates for a kernel compiled AFTER it? Offline (no GPU): one context + backend, the artifact cache off so
/// every compile really compiles. For each probe kernel it compares (names normalised: x_123 -> x_#) the WGSL from a FRESH
/// context with the WGSL from the long-lived context, compiled after other kernels and a ClearCache (first compile there), and
/// a kernel compiled BEFORE the clear compiled again after it. Investigates Geordi's 2026-11-23 report: a Codecs LZ4 dispatch ran
/// past the GPU watchdog only when ReleaseCompilerCaches ran on the long-lived accelerator.
/// </summary>
static class ClearCacheWgslProbe
{
    // shared helper, called by several kernels (re-imported after a ClearCache)
    static long Mix(long x, long salt)
    {
        long h = x * unchecked((long)0x9E3779B97F4A7C15UL) ^ salt;
        h ^= (long)((ulong)h >> 29);
        return h * 3 + (x >> 7);
    }

    // 64-bit index arithmetic that a 32-bit read narrows (NarrowLowWordArithmetic), sub-word view, helper call
    static void ProbeA(Index1D i, ArrayView<byte> src, ArrayView<int> dst, long baseOffset)
    {
        long j = (long)(int)i + baseOffset;
        dst[i] = src[(int)(j & 0xFFFF)] + (int)(Mix(j, 5) & 0xFF);
    }

    static void ProbeB(Index1D i, ArrayView<uint> data, ArrayView<long> acc, long n)
    {
        long k = (long)(int)i * 4;
        uint v = data[(int)(k % n)];
        acc[i] = Mix(k, v) + XMath.Max((long)v, k);
    }

    static void ProbeC(Index1D i, ArrayView<ushort> a, ArrayView<int> b)
    {
        int x = a[i] | (a[i + 1] << 16);
        b[i] = (int)(Mix(x, 9) >> 3) + Atomic.Add(ref b[0], 0);
    }

    static string Normalise(string wgsl) => Regex.Replace(wgsl, @"\b([A-Za-z]\w*?)_\d+\b", "$1_#");

    static (Context, WebGPUBackend) NewBackend()
    {
        var ctx = Context.Create(b => { b.Default().EnableAlgorithms(); b.EnableWebGPUAlgorithms(); });
        var features = new HashSet<string>(StringComparer.Ordinal) { "shader-f16", "subgroups" };
        var backend = new WebGPUBackend(ctx, WebGPUBackendOptions.Default, features);
        return (ctx, backend);
    }

    static string Wgsl(WebGPUBackend backend, Delegate kernel) =>
        ((WebGPUCompiledKernel)backend.Compile(EntryPointDescription.FromImplicitlyGroupedKernel(kernel.Method), KernelSpecialization.Empty)).WGSLSource;

    public static int Run(string[] args)
    {
        bool savedCache = ShaderArtifactCache.Enabled;
        ShaderArtifactCache.Enabled = false;
        try
        {
            var kernels = new (string Name, Delegate K)[]
            {
                ("ProbeA", (Action<Index1D, ArrayView<byte>, ArrayView<int>, long>)ProbeA),
                ("ProbeB", (Action<Index1D, ArrayView<uint>, ArrayView<long>, long>)ProbeB),
                ("ProbeC", (Action<Index1D, ArrayView<ushort>, ArrayView<int>>)ProbeC),
            };

            // reference: each kernel in its own fresh context
            var fresh = new Dictionary<string, string>();
            foreach (var (name, k) in kernels)
            {
                var (c, b) = NewBackend();
                using (c) using (b) fresh[name] = Normalise(Wgsl(b, k));
            }

            int differing = 0;
            var (ctx, backend) = NewBackend();
            using (ctx) using (backend)
            {
                // A compiled before the clear; B and C compiled for the first time after it (Geordi's case)
                var aBefore = Normalise(Wgsl(backend, kernels[0].K));
                ctx.ClearCache(ClearCacheMode.Default);
                var after = new Dictionary<string, string>
                {
                    ["ProbeB"] = Normalise(Wgsl(backend, kernels[1].K)),
                    ["ProbeC"] = Normalise(Wgsl(backend, kernels[2].K)),
                    ["ProbeA"] = Normalise(Wgsl(backend, kernels[0].K)), // compiled again after the clear
                };
                Report("ProbeA before the clear", fresh["ProbeA"], aBefore, ref differing);
                foreach (var name in new[] { "ProbeB", "ProbeC", "ProbeA" })
                    Report($"{name} after the clear", fresh[name], after[name], ref differing);
            }
            Console.WriteLine(differing == 0 ? "RESULT: identical WGSL in every case" : $"RESULT: {differing} case(s) differ");
            return differing;
        }
        finally { ShaderArtifactCache.Enabled = savedCache; }
    }

    static void Report(string what, string expected, string actual, ref int differing)
    {
        if (expected == actual) { Console.WriteLine($"  same     {what} ({actual.Length} chars)"); return; }
        differing++;
        int d = 0;
        while (d < Math.Min(expected.Length, actual.Length) && expected[d] == actual[d]) d++;
        int from = Math.Max(0, d - 120);
        Console.WriteLine($"  DIFFERS  {what}: fresh {expected.Length} chars, here {actual.Length}; first difference at {d}");
        Console.WriteLine("    fresh: " + expected.Substring(from, Math.Min(400, expected.Length - from)).Replace("\n", "\n           "));
        Console.WriteLine("    here : " + actual.Substring(from, Math.Min(400, actual.Length - from)).Replace("\n", "\n           "));
    }
}
