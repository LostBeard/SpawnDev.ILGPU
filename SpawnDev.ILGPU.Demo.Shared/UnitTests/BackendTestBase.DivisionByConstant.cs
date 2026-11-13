using System.Reflection;
using ILGPU;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Integer division and remainder by a CONSTANT, against the host, on every backend.
    //
    // 2026-11-13: the PTX backend emits a division or remainder by a constant power of two (or its negative) as shifts
    // and masks (PTXCodeGenerator.TryGenerateDivRemByPowerOfTwo): the assembler behind PTX does not reduce
    // "div.s32 r, a, 16", and a division in a kernel loop cost five times the rest of the loop. The reduction has to
    // give .NET's results: a signed quotient rounds toward zero, a signed remainder has the sign of the dividend, a
    // negative divisor negates the quotient and leaves the remainder alone. The other backends hand the division to a
    // compiler that reduces it itself; the test runs there too, so all of them are held to the same values.
    //
    // One thread computes ONE (value, operation) pair and stores it in its own slot (WebGL: one store per thread).
    // The operation table is a method the kernel and the host both call. Divisors that are not powers of two (3, 7,
    // 10, 1000) are in the table as controls: they take the plain division.
    public abstract partial class BackendTestBase
    {
        const int DivConst_Ops32 = 28;
        const int DivConst_OpsU32 = 12;
        const int DivConst_Ops64 = 20;
        const int DivConst_OpsU64 = 10;

        static int DivConst_Op32(int x, int op)
        {
            int r = 0;
            if (op == 0) r = x / 2;
            else if (op == 1) r = x % 2;
            else if (op == 2) r = x / 4;
            else if (op == 3) r = x % 4;
            else if (op == 4) r = x / 16;
            else if (op == 5) r = x % 16;
            else if (op == 6) r = x / 1024;
            else if (op == 7) r = x % 1024;
            else if (op == 8) r = x / 65536;
            else if (op == 9) r = x % 65536;
            else if (op == 10) r = x / 1073741824;
            else if (op == 11) r = x % 1073741824;
            else if (op == 12) r = x / -2;
            else if (op == 13) r = x % -2;
            else if (op == 14) r = x / -16;
            else if (op == 15) r = x % -16;
            else if (op == 16) r = x / -1073741824;
            else if (op == 17) r = x % -1073741824;
            else if (op == 18) r = x / 3;
            else if (op == 19) r = x % 3;
            else if (op == 20) r = x / 7;
            else if (op == 21) r = x % 7;
            else if (op == 22) r = x / 10;
            else if (op == 23) r = x % 10;
            else if (op == 24) r = x / 1000;
            else if (op == 25) r = x % -1000;
            else if (op == 26) r = (x / 8) * 8 + x % 8;     // must give x back
            else r = x / 32768 + x % 32768;
            return r;
        }

        static uint DivConst_OpU32(uint x, int op)
        {
            uint r = 0;
            if (op == 0) r = x / 2u;
            else if (op == 1) r = x % 2u;
            else if (op == 2) r = x / 16u;
            else if (op == 3) r = x % 16u;
            else if (op == 4) r = x / 65536u;
            else if (op == 5) r = x % 65536u;
            else if (op == 6) r = x / 2147483648u;
            else if (op == 7) r = x % 2147483648u;
            else if (op == 8) r = x / 7u;
            else if (op == 9) r = x % 7u;
            else if (op == 10) r = x / 1000u;
            else r = (x / 4u) * 4u + x % 4u;
            return r;
        }

        static long DivConst_Op64(long x, int op)
        {
            long r = 0;
            if (op == 0) r = x / 2;
            else if (op == 1) r = x % 2;
            else if (op == 2) r = x / 16;
            else if (op == 3) r = x % 16;
            else if (op == 4) r = x / 4294967296L;
            else if (op == 5) r = x % 4294967296L;
            else if (op == 6) r = x / 1099511627776L;
            else if (op == 7) r = x % 1099511627776L;
            else if (op == 8) r = x / 4611686018427387904L;
            else if (op == 9) r = x % 4611686018427387904L;
            else if (op == 10) r = x / -16;
            else if (op == 11) r = x % -16;
            else if (op == 12) r = x / -4611686018427387904L;
            else if (op == 13) r = x % -4611686018427387904L;
            else if (op == 14) r = x / 7;
            else if (op == 15) r = x % 7;
            else if (op == 16) r = x / 1000000007L;
            else if (op == 17) r = x % 1000000007L;
            else if (op == 18) r = (x / 8) * 8 + x % 8;
            else r = x / -2;
            return r;
        }

        static ulong DivConst_OpU64(ulong x, int op)
        {
            ulong r = 0;
            if (op == 0) r = x / 2UL;
            else if (op == 1) r = x % 2UL;
            else if (op == 2) r = x / 16UL;
            else if (op == 3) r = x % 16UL;
            else if (op == 4) r = x / 4294967296UL;
            else if (op == 5) r = x % 4294967296UL;
            else if (op == 6) r = x / 9223372036854775808UL;
            else if (op == 7) r = x % 9223372036854775808UL;
            else if (op == 8) r = x / 7UL;
            else r = x % 1000000007UL;
            return r;
        }

        static void DivConst_Kernel32(Index1D i, ArrayView<int> values, ArrayView<int> results)
        {
            int v = i.X / DivConst_Ops32;
            results[i] = DivConst_Op32(values[v], i.X - v * DivConst_Ops32);
        }

        static void DivConst_KernelU32(Index1D i, ArrayView<uint> values, ArrayView<uint> results)
        {
            int v = i.X / DivConst_OpsU32;
            results[i] = DivConst_OpU32(values[v], i.X - v * DivConst_OpsU32);
        }

        static void DivConst_Kernel64(Index1D i, ArrayView<long> values, ArrayView<long> results)
        {
            int v = i.X / DivConst_Ops64;
            results[i] = DivConst_Op64(values[v], i.X - v * DivConst_Ops64);
        }

        static void DivConst_KernelU64(Index1D i, ArrayView<ulong> values, ArrayView<ulong> results)
        {
            int v = i.X / DivConst_OpsU64;
            results[i] = DivConst_OpU64(values[v], i.X - v * DivConst_OpsU64);
        }

        // PTX shape checks: only divisions by powers of two
        static void DivConst_PowerOfTwoOnlyKernel(Index1D i, ArrayView<int> a, ArrayView<long> b, ArrayView<int> o, ArrayView<long> p)
        {
            int x = a[i];
            long y = b[i];
            o[i] = x / 16 + x % 16 + x / -8 + x % 64 + (int)((uint)x / 4u) + (int)((uint)x % 32u);
            p[i] = y / 16 + y % 16 + y / -8 + (long)((ulong)y / 4UL) + (long)((ulong)y % 32UL);
        }

        static void DivConst_OtherDivisorKernel(Index1D i, ArrayView<int> a, ArrayView<int> o)
        {
            o[i] = a[i] / 7;
        }

        static int[] DivConst_Values32()
        {
            var v = new List<int>
            {
                0, 1, -1, 2, -2, 3, -3, 7, -7, 8, -8, 9, -9, 15, 16, 17, -15, -16, -17, 1023, 1024, 1025, -1023, -1024, -1025,
                32767, 32768, 32769, -32767, -32768, -32769, 65535, 65536, 65537, -65535, -65536, -65537,
                0x3FFFFFFF, 0x40000000, 0x40000001, -0x3FFFFFFF, -0x40000000, -0x40000001,
                int.MaxValue, int.MaxValue - 1, int.MinValue, int.MinValue + 1, int.MinValue + 15, int.MinValue + 16,
            };
            var rng = new Random(1113);
            for (int k = 0; k < 47; k++) v.Add(rng.Next(int.MinValue, int.MaxValue));
            return v.ToArray();
        }

        [TestMethod]
        public async Task DivisionByConstant_Int32_MatchesHost() => await RunTest(async accelerator =>
        {
            int[] values = DivConst_Values32();
            // signed
            {
                int n = values.Length * DivConst_Ops32;
                using var dV = accelerator.Allocate1D(values);
                using var dR = accelerator.Allocate1D<int>(n);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(DivConst_Kernel32);
                kernel(n, dV.View, dR.View);
                await accelerator.SynchronizeAsync();
                var got = await dR.CopyToHostAsync<int>();
                var failures = new List<string>();
                for (int p = 0; p < n && failures.Count < 12; p++)
                {
                    int x = values[p / DivConst_Ops32], op = p % DivConst_Ops32;
                    int expected = DivConst_Op32(x, op);
                    if (got[p] != expected) failures.Add($"operation {op} on {x}: {got[p]}, expected {expected}");
                }
                if (failures.Count > 0) throw new Exception($"{accelerator.AcceleratorType}: signed 32-bit division by a constant:\n" + string.Join("\n", failures));
            }
            // unsigned
            {
                var uvalues = new uint[values.Length];
                for (int k = 0; k < values.Length; k++) uvalues[k] = unchecked((uint)values[k]);
                int n = uvalues.Length * DivConst_OpsU32;
                using var dV = accelerator.Allocate1D(uvalues);
                using var dR = accelerator.Allocate1D<uint>(n);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(DivConst_KernelU32);
                kernel(n, dV.View, dR.View);
                await accelerator.SynchronizeAsync();
                var got = await dR.CopyToHostAsync<uint>();
                var failures = new List<string>();
                for (int p = 0; p < n && failures.Count < 12; p++)
                {
                    uint x = uvalues[p / DivConst_OpsU32]; int op = p % DivConst_OpsU32;
                    uint expected = DivConst_OpU32(x, op);
                    if (got[p] != expected) failures.Add($"operation {op} on {x}: {got[p]}, expected {expected}");
                }
                if (failures.Count > 0) throw new Exception($"{accelerator.AcceleratorType}: unsigned 32-bit division by a constant:\n" + string.Join("\n", failures));
            }

            // CUDA: the generated PTX. Powers of two leave no division; another divisor keeps it; with the switch off
            // the divisions are back (so the first check is not passing for another reason).
            if (accelerator is CudaAccelerator cuda)
            {
                var pow2 = EntryPointDescription.FromImplicitlyGroupedKernel(
                    typeof(BackendTestBase).GetMethod(nameof(DivConst_PowerOfTwoOnlyKernel), BindingFlags.Static | BindingFlags.NonPublic)!);
                var other = EntryPointDescription.FromImplicitlyGroupedKernel(
                    typeof(BackendTestBase).GetMethod(nameof(DivConst_OtherDivisorKernel), BindingFlags.Static | BindingFlags.NonPublic)!);
                static int Count(string ptx, string instruction)
                {
                    int count = 0;
                    foreach (string line in ptx.Split('\n'))
                        if (line.TrimStart().StartsWith(instruction, StringComparison.Ordinal)) count++;
                    return count;
                }
                string reduced = ((PTXCompiledKernel)cuda.Backend.Compile(pow2, default)).PTXAssembly;
                if (Count(reduced, "div.") + Count(reduced, "rem.") != 0)
                    throw new Exception($"PTX of divisions by powers of two still has {Count(reduced, "div.")} div and {Count(reduced, "rem.")} rem instructions");
                string seven = ((PTXCompiledKernel)cuda.Backend.Compile(other, default)).PTXAssembly;
                if (Count(seven, "div.") != 1) throw new Exception($"PTX of x / 7 has {Count(seven, "div.")} div instructions, expected the plain division");
                bool before = PTXCodeGenerator.ReduceDivisionByPowerOfTwo;
                try
                {
                    PTXCodeGenerator.ReduceDivisionByPowerOfTwo = false;
                    string plain = ((PTXCompiledKernel)cuda.Backend.Compile(pow2, default)).PTXAssembly;
                    // 4 signed divisions and 5 remainders; the 2 unsigned divisions are shifts already in the IR
                    if (Count(plain, "div.") != 4 || Count(plain, "rem.") != 5)
                        throw new Exception($"with the reduction switched off the PTX has {Count(plain, "div.")} div and {Count(plain, "rem.")} rem instructions, expected 4 and 5");
                }
                finally { PTXCodeGenerator.ReduceDivisionByPowerOfTwo = before; }
            }
        });

        [TestMethod]
        public async Task DivisionByConstant_Int64_MatchesHost() => await RunEmulatedTest(async accelerator =>
        {
            var values = new List<long>
            {
                0, 1, -1, 2, -2, 7, -7, 8, -8, 15, 16, 17, -15, -16, -17, 4294967295L, 4294967296L, 4294967297L,
                -4294967295L, -4294967296L, -4294967297L, 1099511627775L, 1099511627776L, -1099511627777L,
                0x3FFFFFFFFFFFFFFFL, 0x4000000000000000L, 0x4000000000000001L, -0x3FFFFFFFFFFFFFFFL, -0x4000000000000000L,
                -0x4000000000000001L, long.MaxValue, long.MaxValue - 1, long.MinValue, long.MinValue + 1, long.MinValue + 15,
                int.MaxValue, int.MinValue, 1000000007L, -1000000007L,
            };
            var rng = new Random(1114);
            for (int k = 0; k < 25; k++) values.Add(rng.NextInt64(long.MinValue, long.MaxValue));
            long[] v = values.ToArray();
            {
                int n = v.Length * DivConst_Ops64;
                using var dV = accelerator.Allocate1D(v);
                using var dR = accelerator.Allocate1D<long>(n);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<long>>(DivConst_Kernel64);
                kernel(n, dV.View, dR.View);
                await accelerator.SynchronizeAsync();
                var got = await dR.CopyToHostAsync<long>();
                var failures = new List<string>();
                for (int p = 0; p < n && failures.Count < 12; p++)
                {
                    long x = v[p / DivConst_Ops64]; int op = p % DivConst_Ops64;
                    long expected = DivConst_Op64(x, op);
                    if (got[p] != expected) failures.Add($"operation {op} on {x}: {got[p]}, expected {expected}");
                }
                if (failures.Count > 0) throw new Exception($"{accelerator.AcceleratorType}: signed 64-bit division by a constant:\n" + string.Join("\n", failures));
            }
            {
                var uv = new ulong[v.Length];
                for (int k = 0; k < v.Length; k++) uv[k] = unchecked((ulong)v[k]);
                int n = uv.Length * DivConst_OpsU64;
                using var dV = accelerator.Allocate1D(uv);
                using var dR = accelerator.Allocate1D<ulong>(n);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ulong>, ArrayView<ulong>>(DivConst_KernelU64);
                kernel(n, dV.View, dR.View);
                await accelerator.SynchronizeAsync();
                var got = await dR.CopyToHostAsync<ulong>();
                var failures = new List<string>();
                for (int p = 0; p < n && failures.Count < 12; p++)
                {
                    ulong x = uv[p / DivConst_OpsU64]; int op = p % DivConst_OpsU64;
                    ulong expected = DivConst_OpU64(x, op);
                    if (got[p] != expected) failures.Add($"operation {op} on {x}: {got[p]}, expected {expected}");
                }
                if (failures.Count > 0) throw new Exception($"{accelerator.AcceleratorType}: unsigned 64-bit division by a constant:\n" + string.Join("\n", failures));
            }
        });
    }
}
