using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Sequential `if (a || b) { reserve; if (full) { undo; return; } write }` blocks - AubsCraft's LOD mesh kernel
    // (6 cube faces). The early return makes the function exit the post-dominator of every branch, and the WGSL walker
    // merged there: each arm re-emitted the rest of the kernel, 3x per block. 4 faces were already 10 MB of WGSL and
    // 20 s of codegen; the 6-face kernel never finished (one core spinning in the render worker, map never started).
    // Regression since 4.10.0. Fixed with CodeGen.StructuredReturnMerge (merge where the non-returning paths meet).
    //
    // Every thread has its own counter and capacity, so which faces are written (and where the early return cuts the
    // rest off) is deterministic and checked against a host oracle on every backend.
    public abstract partial class BackendTestBase
    {
        const int EarlyReturnFaces = 6;
        const int EarlyReturnSlots = EarlyReturnFaces * 2;

        // 6 faces, plain neighbor test: face f is emitted when bit f of i is set OR its data cell is empty.
        static void EarlyReturnFacesKernel(Index1D i, ArrayView<int> data, ArrayView<int> cap, ArrayView<int> counter, ArrayView<int> outp)
        {
            int b = i * EarlyReturnSlots, d = i * EarlyReturnFaces;
            if ((i & 1) != 0 || data[d] == 0) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 10; outp[b + o + 1] = i; }
            if ((i & 2) != 0 || data[d + 1] == 0) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 11; outp[b + o + 1] = i; }
            if ((i & 4) != 0 || data[d + 2] == 0) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 12; outp[b + o + 1] = i; }
            if ((i & 8) != 0 || data[d + 3] == 0) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 13; outp[b + o + 1] = i; }
            if ((i & 16) != 0 || data[d + 4] == 0) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 14; outp[b + o + 1] = i; }
            if ((i & 32) != 0 || data[d + 5] == 0) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 15; outp[b + o + 1] = i; }
        }

        // The first 3 faces: compiles in well under a second even with the old exponential walker, so a regression fails
        // the size check below instead of hanging codegen.
        static void EarlyReturnFaces3Kernel(Index1D i, ArrayView<int> data, ArrayView<int> cap, ArrayView<int> counter, ArrayView<int> outp)
        {
            int b = i * EarlyReturnSlots, d = i * EarlyReturnFaces;
            if ((i & 1) != 0 || data[d] == 0) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 10; outp[b + o + 1] = i; }
            if ((i & 2) != 0 || data[d + 1] == 0) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 11; outp[b + o + 1] = i; }
            if ((i & 4) != 0 || data[d + 2] == 0) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 12; outp[b + o + 1] = i; }
        }

        // The LOD kernel's neighbor check: a loop that returns from inside it.
        static bool EarlyReturnAnySolid(ArrayView<int> data, int start, int count)
        {
            for (int k = 0; k < count && start + k < data.IntLength; k++)
                if (data[start + k] > 1) return true;
            return false;
        }

        // 6 faces, helper neighbor test: face f is emitted when bit f of i is set OR no cell in its 3-cell window is solid.
        static void EarlyReturnFacesHelperKernel(Index1D i, ArrayView<int> data, ArrayView<int> cap, ArrayView<int> counter, ArrayView<int> outp)
        {
            int b = i * EarlyReturnSlots, d = i * EarlyReturnFaces;
            if ((i & 1) != 0 || !EarlyReturnAnySolid(data, d, 3)) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 20; outp[b + o + 1] = i; }
            if ((i & 2) != 0 || !EarlyReturnAnySolid(data, d + 1, 3)) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 21; outp[b + o + 1] = i; }
            if ((i & 4) != 0 || !EarlyReturnAnySolid(data, d + 2, 3)) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 22; outp[b + o + 1] = i; }
            if ((i & 8) != 0 || !EarlyReturnAnySolid(data, d + 3, 3)) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 23; outp[b + o + 1] = i; }
            if ((i & 16) != 0 || !EarlyReturnAnySolid(data, d + 4, 3)) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 24; outp[b + o + 1] = i; }
            if ((i & 32) != 0 || !EarlyReturnAnySolid(data, d + 5, 3)) { int o = Atomic.Add(ref counter[i], 2); if (o + 2 > cap[i]) { Atomic.Add(ref counter[i], -2); return; } outp[b + o] = 25; outp[b + o + 1] = i; }
        }

        /// <summary>Host oracle for both kernels: per thread, the faces in order, each reserving 2 slots until the
        /// thread's capacity is reached, where the face undoes its reservation and the thread stops.</summary>
        static (int[] Out, int[] Counter) EarlyReturnOracle(int n, int[] data, int[] cap, bool helper)
        {
            var outp = new int[n * EarlyReturnSlots];
            Array.Fill(outp, -1);
            var counter = new int[n];
            for (int i = 0; i < n; i++)
            {
                int b = i * EarlyReturnSlots, d = i * EarlyReturnFaces;
                for (int f = 0; f < EarlyReturnFaces; f++)
                {
                    bool neighborEmpty;
                    if (helper)
                    {
                        bool anySolid = false;
                        for (int k = 0; k < 3 && d + f + k < data.Length; k++)
                            if (data[d + f + k] > 1) { anySolid = true; break; }
                        neighborEmpty = !anySolid;
                    }
                    else neighborEmpty = data[d + f] == 0;
                    if ((i & (1 << f)) == 0 && !neighborEmpty) continue;
                    int o = counter[i];
                    if (o + 2 > cap[i]) break; // the reservation is undone, then return
                    counter[i] = o + 2;
                    outp[b + o] = (helper ? 20 : 10) + f;
                    outp[b + o + 1] = i;
                }
            }
            return (outp, counter);
        }

        async Task EarlyReturnMergeRun(Accelerator accelerator, bool helper)
        {
            const int n = 64; // every combination of the 6 "edge" bits
            foreach (int seed in new[] { 3, 11 })
            {
                var rng = new Random(seed);
                var data = new int[n * EarlyReturnFaces];
                for (int k = 0; k < data.Length; k++) data[k] = rng.Next(0, 3);           // 0 empty, 1 plant, 2 solid
                var cap = new int[n];
                for (int i = 0; i < n; i++) cap[i] = 2 * rng.Next(0, EarlyReturnFaces + 1); // 0..12: some threads fill up
                var outInit = new int[n * EarlyReturnSlots];
                Array.Fill(outInit, -1);

                using var dataBuf = accelerator.Allocate1D(data);
                using var capBuf = accelerator.Allocate1D(cap);
                using var counterBuf = accelerator.Allocate1D(new int[n]);
                using var outBuf = accelerator.Allocate1D(outInit);
                if (helper)
                    accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(EarlyReturnFacesHelperKernel)(n, dataBuf.View, capBuf.View, counterBuf.View, outBuf.View);
                else
                    accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(EarlyReturnFacesKernel)(n, dataBuf.View, capBuf.View, counterBuf.View, outBuf.View);
                await accelerator.SynchronizeAsync();
                var gotOut = await outBuf.CopyToHostAsync<int>();
                var gotCounter = await counterBuf.CopyToHostAsync<int>();

                var (expectOut, expectCounter) = EarlyReturnOracle(n, data, cap, helper);
                for (int i = 0; i < n; i++)
                {
                    if (gotCounter[i] != expectCounter[i])
                        throw new Exception($"seed {seed} counter[{i}] = {gotCounter[i]}, expected {expectCounter[i]} (cap {cap[i]})");
                    for (int s = 0; s < EarlyReturnSlots; s++)
                    {
                        int k = i * EarlyReturnSlots + s;
                        if (gotOut[k] != expectOut[k])
                            throw new Exception($"seed {seed} thread {i} slot {s} = {gotOut[k]}, expected {expectOut[k]} (cap {cap[i]})");
                    }
                }
            }
        }

        [TestMethod]
        public async Task EarlyReturnMerge_SequentialOrBlocks_CorrectOutput() => await RunTest(async accelerator =>
            await EarlyReturnMergeRun(accelerator, helper: false));

        [TestMethod]
        public async Task EarlyReturnMerge_SequentialOrBlocksLoopHelper_CorrectOutput() => await RunTest(async accelerator =>
            await EarlyReturnMergeRun(accelerator, helper: true));

        // The same control flow without atomics or scattered writes, so it runs on every backend including WebGL (whose
        // vertex-shader pipeline has neither): each thread writes only outp[i], the mask of faces it emitted so far.
        static void EarlyReturnMaskKernel(Index1D i, ArrayView<int> data, ArrayView<int> cap, ArrayView<int> outp)
        {
            int d = i * EarlyReturnFaces, used = 0, mask = 0;
            outp[i] = 0;
            if ((i & 1) != 0 || data[d] == 0) { if (used + 2 > cap[i]) return; used += 2; mask |= 1; outp[i] = mask; }
            if ((i & 2) != 0 || data[d + 1] == 0) { if (used + 2 > cap[i]) return; used += 2; mask |= 2; outp[i] = mask; }
            if ((i & 4) != 0 || data[d + 2] == 0) { if (used + 2 > cap[i]) return; used += 2; mask |= 4; outp[i] = mask; }
            if ((i & 8) != 0 || data[d + 3] == 0) { if (used + 2 > cap[i]) return; used += 2; mask |= 8; outp[i] = mask; }
            if ((i & 16) != 0 || data[d + 4] == 0) { if (used + 2 > cap[i]) return; used += 2; mask |= 16; outp[i] = mask; }
            if ((i & 32) != 0 || data[d + 5] == 0) { if (used + 2 > cap[i]) return; used += 2; mask |= 32; outp[i] = mask; }
        }

        [TestMethod]
        public async Task EarlyReturnMerge_SequentialOrBlocksNoAtomics_CorrectOutput() => await RunTest(async accelerator =>
        {
            const int n = 64;
            foreach (int seed in new[] { 3, 11 })
            {
                var rng = new Random(seed);
                var data = new int[n * EarlyReturnFaces];
                for (int k = 0; k < data.Length; k++) data[k] = rng.Next(0, 3);
                var cap = new int[n];
                for (int i = 0; i < n; i++) cap[i] = 2 * rng.Next(0, EarlyReturnFaces + 1);

                using var dataBuf = accelerator.Allocate1D(data);
                using var capBuf = accelerator.Allocate1D(cap);
                using var outBuf = accelerator.Allocate1D<int>(n);
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(EarlyReturnMaskKernel)(n, dataBuf.View, capBuf.View, outBuf.View);
                await accelerator.SynchronizeAsync();
                var got = await outBuf.CopyToHostAsync<int>();

                var (_, counter) = EarlyReturnOracle(n, data, cap, helper: false);
                for (int i = 0; i < n; i++)
                {
                    // The oracle's counter says how many faces were emitted; they are the first emitted ones in order.
                    int mask = 0, emitted = counter[i] / 2, d = i * EarlyReturnFaces;
                    for (int f = 0; f < EarlyReturnFaces && emitted > 0; f++)
                        if ((i & (1 << f)) != 0 || data[d + f] == 0) { mask |= 1 << f; emitted--; }
                    if (got[i] != mask)
                        throw new Exception($"seed {seed} mask[{i}] = {got[i]}, expected {mask} (cap {cap[i]})");
                }
            }
        });

        // The shader must stay proportional to the source: count the atomicAdd calls the WGSL emits (2 per face in the
        // source; the `||` re-emits a face body once for its second operand, so at most 4 per face). Red-checked
        // 2026-10-02 with the return-aware merge switched off: 3 faces x52, 6 faces x1456 (and the real LOD kernel's
        // codegen never finished); with it, x11 and x23.
        [TestMethod]
        public async Task EarlyReturnMerge_SequentialOrBlocks_WgslStaysLinear() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator)
                throw new UnsupportedTestException("WebGPU-only: inspects the generated WGSL.");

            EarlyReturnCheckWgslSize(accelerator, nameof(EarlyReturnFaces3Kernel), EarlyReturnFaces3Kernel, faces: 3);
            EarlyReturnCheckWgslSize(accelerator, nameof(EarlyReturnFacesKernel), EarlyReturnFacesKernel, faces: 6);
            await Task.CompletedTask;
        });

        static void EarlyReturnCheckWgslSize(Accelerator accelerator, string kernelName,
            Action<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>> kernel, int faces)
        {
            string? wgsl = null;
            Action<string, string, WGSLEntry> handler = (name, source, info) =>
            {
                if (name.Contains(kernelName, StringComparison.Ordinal)) wgsl = source;
            };
            // The correctness tests may have compiled this kernel already, and a cache hit (the accelerator's kernel cache
            // or the process-wide ShaderArtifactCache) never reaches the code generator.
            accelerator.ClearCache(ClearCacheMode.Everything);
            bool artifactCacheWas = SpawnDev.ILGPU.ShaderArtifactCache.Enabled;
            SpawnDev.ILGPU.ShaderArtifactCache.Enabled = false;
            WebGPUBackend.OnShaderCompiled += handler;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _ = accelerator.LoadAutoGroupedStreamKernel(kernel);
            }
            finally
            {
                WebGPUBackend.OnShaderCompiled -= handler;
                SpawnDev.ILGPU.ShaderArtifactCache.Enabled = artifactCacheWas;
            }
            if (wgsl == null) throw new Exception($"Did not capture {kernelName}'s WGSL via OnShaderCompiled.");

            int atomics = 0;
            for (int at = wgsl.IndexOf("atomicAdd(", StringComparison.Ordinal); at >= 0; at = wgsl.IndexOf("atomicAdd(", at + 1, StringComparison.Ordinal))
                atomics++;
            if (atomics > 4 * faces)
                throw new Exception($"{kernelName}: WGSL emitted atomicAdd x{atomics} for {2 * faces} in the source ({wgsl.Length} chars, {sw.ElapsedMilliseconds} ms): early-return blocks are being duplicated.");
        }
    }
}
