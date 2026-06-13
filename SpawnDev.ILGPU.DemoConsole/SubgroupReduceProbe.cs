using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Offline probe for work-order #1 (subgroup/warp reduction intrinsics — verify-then-adopt).
/// Generates WGSL for the high-level reduce API ML would use (GroupExtensions.AllReduce/Reduce,
/// WarpExtensions.Reduce) against the subgroups-ENABLED profile and the subgroups-DISABLED
/// profile, and CONFIRMS that:
///   1. with subgroups: the body lowers to native subgroupAdd (the register-only fast path),
///   2. without subgroups: it lowers to a correct shared-memory fallback (no subgroupAdd).
/// No device, no browser, no dispatch (Rule 9 — no permission-prompt path, runs via DemoConsole).
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- subgroup-reduce
/// </summary>
internal static class SubgroupReduceProbe
{
    // Group-level all-reduce: every thread receives the workgroup-wide sum. This is the exact
    // shape an RMSNorm/softmax/attention-score/GEMV cooperative reduction would use.
    private static void GroupAllReduceFloatKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
    {
        int gid = Grid.GlobalIndex.X;
        float v = input[gid];
        output[gid] = GroupExtensions.AllReduce<float, AddFloat>(v);
    }

    // Group-level reduce: only the first thread receives the result.
    private static void GroupReduceFloatKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
    {
        int gid = Grid.GlobalIndex.X;
        float v = input[gid];
        float r = GroupExtensions.Reduce<float, AddFloat>(v);
        if (Group.IsFirstThread)
            output[0] = r;
    }

    // Warp-level reduce: pure register-only subgroup reduction (lane 0 holds the result).
    private static void WarpReduceFloatKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
    {
        int gid = Grid.GlobalIndex.X;
        float v = input[gid];
        output[gid] = WarpExtensions.Reduce<float, AddFloat>(v);
    }

    // Explicitly-grouped variants (no leading Index param -> FromExplicitlyGroupedKernel),
    // matching how the runtime AllReduce tests dispatch (LoadStreamKernel + KernelConfig).
    private static void GroupAllReduceFloatExplicit(ArrayView<float> input, ArrayView<float> output)
    {
        int gid = Grid.GlobalIndex.X;
        output[gid] = GroupExtensions.AllReduce<float, AddFloat>(input[gid]);
    }

    private static void GroupReduceFloatExplicit(ArrayView<float> input, ArrayView<float> output)
    {
        int gid = Grid.GlobalIndex.X;
        float r = GroupExtensions.Reduce<float, AddFloat>(input[gid]);
        if (Group.IsFirstThread) output[0] = r;
    }

    private record Probe(string Name, Delegate Kernel);

    public static Task<int> Run(string[] args)
    {
        string outDir = args.Length > 1
            ? args[1]
            : Path.Combine(Directory.GetCurrentDirectory(), "_subgroup_reduce_probe");
        Directory.CreateDirectory(outDir);

        bool verbose = args.Contains("-v");
        if (verbose) SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.VerboseLogging = true;

        var probes = new[]
        {
            new Probe("GroupAllReduceFloat", (Action<Index1D, ArrayView<float>, ArrayView<float>>)GroupAllReduceFloatKernel),
            new Probe("GroupReduceFloat",    (Action<Index1D, ArrayView<float>, ArrayView<float>>)GroupReduceFloatKernel),
            new Probe("WarpReduceFloat",     (Action<Index1D, ArrayView<float>, ArrayView<float>>)WarpReduceFloatKernel),
            new Probe("GroupAllReduceFloat_EXPLICIT", (Action<ArrayView<float>, ArrayView<float>>)GroupAllReduceFloatExplicit),
            new Probe("GroupReduceFloat_EXPLICIT",    (Action<ArrayView<float>, ArrayView<float>>)GroupReduceFloatExplicit),
        };

        // workgroup size 64 so the cross-subgroup aggregation path is exercised on typical HW.
        var spec = new KernelSpecialization(64, null);

        bool allOk = true;
        foreach (var probe in probes)
        {
            foreach (var (profile, expectSubgroup) in new[]
            {
                (CapabilityProfiles.WebGPUFull, true),         // subgroups available
                (CapabilityProfiles.WebGPUNoSubgroups, false), // shared-memory fallback
            })
            {
                string tag = $"{probe.Name} / {profile.Name}";
                try
                {
                    var result = ShaderCompiler.Generate(probe.Kernel, profile, spec);
                    var wgsl = result.Source ?? "";
                    bool hasSubgroupAdd = wgsl.Contains("subgroupAdd", StringComparison.Ordinal);
                    bool hasEnableSubgroups = wgsl.Contains("enable subgroups;", StringComparison.Ordinal);
                    bool hasSharedFallback = wgsl.Contains("shared", StringComparison.OrdinalIgnoreCase)
                        || wgsl.Contains("workgroupBarrier", StringComparison.Ordinal);

                    string path = Path.Combine(outDir, $"{probe.Name}__{profile.Name}.wgsl");
                    File.WriteAllText(path, wgsl);

                    bool ok = expectSubgroup ? hasSubgroupAdd : !hasSubgroupAdd;
                    allOk &= ok;
                    Console.WriteLine(
                        $"[{(ok ? "OK" : "FAIL")}] {tag}: len={wgsl.Length} " +
                        $"subgroupAdd={hasSubgroupAdd} enableSubgroups={hasEnableSubgroups} " +
                        $"hasShared/Barrier={hasSharedFallback} -> {path}");

                    // Echo the lines around the reduce so it can be eyeballed without opening the file.
                    var lines = wgsl.Split('\n');
                    int firstHit = Array.FindIndex(lines, l =>
                        l.Contains("subgroupAdd", StringComparison.Ordinal) ||
                        l.Contains("_grp_sg_results", StringComparison.Ordinal) ||
                        l.Contains("Reduce", StringComparison.Ordinal));
                    if (firstHit >= 0)
                    {
                        int from = Math.Max(0, firstHit - 2);
                        int to = Math.Min(lines.Length - 1, firstHit + 8);
                        for (int l = from; l <= to; l++)
                            Console.WriteLine($"      {lines[l].TrimEnd()}");
                    }
                }
                catch (Exception ex)
                {
                    allOk = false;
                    Console.Error.WriteLine($"[FAIL] {tag}: EXCEPTION {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"[subgroup-reduce] {(allOk ? "ALL OK" : "FAILURES PRESENT")} — WGSL written to {outDir}");
        return Task.FromResult(allOk ? 0 : 1);
    }
}
