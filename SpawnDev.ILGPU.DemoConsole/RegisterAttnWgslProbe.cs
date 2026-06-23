using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Offline WGSL dump of the register per-query attention SHAPE that produces an INVALID
/// WebGPU pipeline (Tuvok 2026-06-23, tuvok-to-geordi-WEBGPU-register-attention-wgsl-invalid-pipeline):
/// a const-16 `new float[16]` per-lane register accumulator (unrolled -> scalar-replace) +
/// `Warp.ShuffleXor` butterfly reduce inside a runtime kv loop, 6 ArrayViews, warp=32 group.
/// Dumps the WGSL via ShaderCompiler (offline, no device) using the subgroup-enabled profile,
/// so we can see what Tint/Dawn rejects.
///   dotnet run --project SpawnDev.ILGPU.DemoConsole -- register-attn-wgsl
/// </summary>
internal static class RegisterAttnWgslProbe
{
    private const int TILE = 16; // per-lane const register tile (D/nLanes)

    // 6 ArrayViews + meta. One 32-lane warp = block; nLanes lanes cooperate per query.
    static void RegAttnKernel(
        ArrayView<float> q, ArrayView<float> k, ArrayView<float> v,
        ArrayView<float> o, ArrayView<int> meta, ArrayView<float> scale)
    {
        int lane = Group.IdxX;          // 0..31
        int nLanes = meta[0];           // D/16 (e.g. 4 for hd=64)
        int KV = meta[1];
        int D = meta[2];

        var acc = new float[TILE];      // const-16 register accumulator (unrolls -> registers)
        for (int d = 0; d < TILE; d++) acc[d] = 0f;

        for (int j = 0; j < KV; j++)
        {
            // partial Q.K dot over this lane's tile
            float pd = 0f;
            for (int d = 0; d < TILE; d++)
                pd += q[lane * TILE + d] * k[j * D + lane * TILE + d];

            // butterfly reduce the partial dot across the nLanes cooperating lanes
            for (int off = nLanes / 2; off > 0; off >>= 1)
                pd += Warp.ShuffleXor(pd, off);

            float w = pd * scale[0];
            for (int d = 0; d < TILE; d++)
                acc[d] += w * v[j * D + lane * TILE + d];
        }

        for (int d = 0; d < TILE; d++)
            o[lane * TILE + d] = acc[d];
    }

    public static Task<int> Run()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "register_attn_wgsl");
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"[register-attn-wgsl] out dir: {outDir}");
        var spec = new KernelSpecialization(32, null); // 32-lane warp group
        var m = typeof(RegisterAttnWgslProbe).GetMethod(nameof(RegAttnKernel), BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var profile in new[] { CapabilityProfiles.WebGPUFull, CapabilityProfiles.WebGPUNoSubgroups })
        {
            try
            {
                var result = ShaderCompiler.Generate(m, profile, spec);
                var wgsl = result.Source ?? "(null)";
                var path = Path.Combine(outDir, $"RegAttn_{profile.Name}.wgsl");
                File.WriteAllText(path, wgsl);
                Console.WriteLine($"  [{profile.Name}] len={wgsl.Length} hasErrors={result.HasErrors} -> {path}");
                // quick checks
                Console.WriteLine($"     enable-subgroups={wgsl.Contains("enable subgroups;")} subgroupShuffle={wgsl.Contains("subgroupShuffle")} subgroup_size={wgsl.Contains("subgroup_size")} placeholder-left={wgsl.Contains("PLACEHOLDER")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{profile.Name}] EXCEPTION: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }
        return Task.FromResult(0);
    }
}
