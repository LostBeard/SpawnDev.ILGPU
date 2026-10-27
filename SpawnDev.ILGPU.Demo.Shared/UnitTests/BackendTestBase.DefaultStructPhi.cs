using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A struct that is `default` on one path and built on another, merged after the branch (a phi with the struct's
    // null value as one source). Velocity aliased the FIRST null value of each type to an empty local, and the compile
    // of a kernel merging it failed with a NullReferenceException in GetFieldInfo (SpawnDev.Codecs
    // Vp9KeyframeDecodeStepKernel, Geordi 2026-10-26: an 8-byte struct of sbytes). Every backend, against a CPU oracle.
    public abstract partial class BackendTestBase
    {
        public struct DefaultStructPhi_Bytes8
        {
            public sbyte A, B, C, D, E, F, G, H;
        }

        static DefaultStructPhi_Bytes8 DefaultStructPhi_Make(int seed)
        {
            DefaultStructPhi_Bytes8 r;
            r.A = (sbyte)seed; r.B = (sbyte)(seed + 1); r.C = (sbyte)(seed * 2); r.D = (sbyte)(seed - 3);
            r.E = (sbyte)(seed ^ 5); r.F = (sbyte)(seed >> 1); r.G = (sbyte)(-seed); r.H = 7;
            return r;
        }

        // The struct is carried around a loop with ONE field set per taken branch (a SetField on the loop phi): the loop
        // header merges the struct's null value (entry) with the previous iteration's (Geordi's IR: phi(null, phi)).
        static void DefaultStructPhi_Kernel(Index1D idx, ArrayView<int> cond, ArrayView<int> output, int steps)
        {
            DefaultStructPhi_Bytes8 s = default;
            for (int k = 0; k < steps; k++)
            {
                int c = cond[idx * steps + k];
                if (c == 1) s.A = (sbyte)(idx + k);
                if (c == 2) s.D = (sbyte)(k - idx);
                if (c == 3) s.H = (sbyte)(k + 1);
            }
            output[idx] = s.A + s.B + s.C + s.D + s.E + s.F + s.G + s.H * 100;
        }

        // The struct's default stored directly: a store of the null value.
        static void DefaultStructPhi_StoreKernel(Index1D idx, ArrayView<int> cond, ArrayView<DefaultStructPhi_Bytes8> output)
        {
            if (cond[idx] > 0) output[idx] = DefaultStructPhi_Make(idx);
            else output[idx] = default;
        }

        // Geordi's actual shape: two small LocalMemory byte arrays indexed only by constants, which ILGPU turns into
        // struct values that start as the struct's null value, carried around a loop.
        static void DefaultStructPhi_LocalArrayKernel(Index1D idx, ArrayView<int> cond, ArrayView<int> output, int steps)
        {
            int t = idx;
            var above = LocalMemory.Allocate<byte>(8);
            var left = LocalMemory.Allocate<byte>(8);
            for (int k = 0; k < steps; k++)
            {
                int c = cond[t * steps + k];
                if (c == 1) { above[0] = (byte)(t + k); left[7] = (byte)k; }
                if (c == 2) { above[3] = (byte)(k * 5); left[1] = (byte)(t & 15); }
                if (c == 3) above[7] = (byte)(k + 1);
            }
            int sum = 0;
            for (int i = 0; i < 8; i++) sum += above[i] * (i + 1) + left[i] * 100 * (i + 1);
            output[idx] = sum;
        }

        [TestMethod]
        public async Task DefaultStruct_LocalByteArraysInLoop_MatchesOracle() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 64, steps = 5;
            var cond = new int[n * steps];
            for (int i = 0; i < cond.Length; i++) cond[i] = (i * 7) % 5;
            using var dCond = accelerator.Allocate1D(cond);
            using var dOut = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(DefaultStructPhi_LocalArrayKernel)((Index1D)n, dCond.View, dOut.View, steps);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
            {
                var above = new byte[8];
                var left = new byte[8];
                for (int k = 0; k < steps; k++)
                {
                    int c = cond[t * steps + k];
                    if (c == 1) { above[0] = (byte)(t + k); left[7] = (byte)k; }
                    if (c == 2) { above[3] = (byte)(k * 5); left[1] = (byte)(t & 15); }
                    if (c == 3) above[7] = (byte)(k + 1);
                }
                int want = 0;
                for (int i = 0; i < 8; i++) want += above[i] * (i + 1) + left[i] * 100 * (i + 1);
                if (got[t] != want) throw new Exception($"output[{t}] = {got[t]}, expected {want}");
            }
        });

        [TestMethod]
        public async Task DefaultStruct_StoredDefault_MatchesOracle() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 48;
            var cond = new int[n];
            for (int i = 0; i < n; i++) cond[i] = i % 3 == 0 ? 0 : 1;
            var initial = new DefaultStructPhi_Bytes8[n];
            for (int i = 0; i < n; i++) initial[i] = DefaultStructPhi_Make(100 + i);
            using var dCond = accelerator.Allocate1D(cond);
            using var dOut = accelerator.Allocate1D(initial);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<DefaultStructPhi_Bytes8>>(DefaultStructPhi_StoreKernel)((Index1D)n, dCond.View, dOut.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<DefaultStructPhi_Bytes8>();
            for (int i = 0; i < n; i++)
            {
                var want = cond[i] > 0 ? DefaultStructPhi_Make(i) : default;
                if (!got[i].Equals(want)) throw new Exception($"output[{i}] (cond {cond[i]}): A={got[i].A} H={got[i].H}, expected A={want.A} H={want.H}");
            }
        });

        [TestMethod]
        public async Task DefaultStruct_PhiWithDefaultArm_MatchesOracle() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 64, steps = 5;
            var cond = new int[n * steps];
            for (int i = 0; i < cond.Length; i++) cond[i] = (i * 7) % 5;
            using var dCond = accelerator.Allocate1D(cond);
            using var dOut = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(DefaultStructPhi_Kernel)((Index1D)n, dCond.View, dOut.View, steps);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                DefaultStructPhi_Bytes8 s = default;
                for (int k = 0; k < steps; k++)
                {
                    int c = cond[i * steps + k];
                    if (c == 1) s.A = (sbyte)(i + k);
                    if (c == 2) s.D = (sbyte)(k - i);
                    if (c == 3) s.H = (sbyte)(k + 1);
                }
                int want = s.A + s.B + s.C + s.D + s.E + s.F + s.G + s.H * 100;
                if (got[i] != want) throw new Exception($"output[{i}] = {got[i]}, expected {want}");
            }
        });
    }
}
