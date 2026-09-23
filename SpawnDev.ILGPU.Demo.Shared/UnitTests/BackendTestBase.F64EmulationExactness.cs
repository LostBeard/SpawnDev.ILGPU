using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGL;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Exactness of the emulated-f64 paths on WebGPU and WebGL, under BOTH emulation modes (Dekker
    // vec2<f32> - the production default - and Ozaki vec4<f32>), plus the 64-bit-integer <-> float
    // conversions. Every input and every expected result is chosen to be exactly representable in
    // Dekker's ~48-bit significand, so a mismatch is a real bug, not the representation's limit.
    // Kernels compiled on an accelerator keep the f64 mode they were compiled with, so each mode
    // runs its OWN kernel method (the *_Dekker / *_Ozaki pairs share one AggressiveInlining body).
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// Runs <paramref name="body"/> once per f64 emulation mode on the emulated backends (variant
        /// 0 = Dekker, 1 = Ozaki), restoring the accelerator's mode afterwards; once (variant 0) on
        /// backends with native f64.
        /// </summary>
        async Task ForEachF64Mode(Accelerator accelerator, Func<int, string, Task> body)
        {
            if (accelerator is WebGPUAccelerator webgpu)
            {
                var saved = webgpu.F64Mode;
                try
                {
                    webgpu.F64Mode = F64EmulationMode.Dekker; await body(0, "Dekker");
                    webgpu.F64Mode = F64EmulationMode.Ozaki; await body(1, "Ozaki");
                }
                finally { webgpu.F64Mode = saved; }
            }
            else if (accelerator is WebGLAccelerator webgl)
            {
                var saved = webgl.F64Mode;
                try
                {
                    webgl.F64Mode = F64EmulationMode.Dekker; await body(0, "Dekker");
                    webgl.F64Mode = F64EmulationMode.Ozaki; await body(1, "Ozaki");
                }
                finally { webgl.F64Mode = saved; }
            }
            else
            {
                await body(0, "native");
            }
        }

        // ---- 1. arithmetic round trip: load -> f64 op -> store must not move the value ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_RoundTripBody(Index1D i, ArrayView<double> a, ArrayView<double> zero, ArrayView<double> sum, ArrayView<double> prod)
        {
            sum[i] = a[i] + zero[i];
            prod[i] = a[i] * (zero[i] + 1.0);
        }
        static void F64X_RoundTrip_Dekker(Index1D i, ArrayView<double> a, ArrayView<double> z, ArrayView<double> s, ArrayView<double> p) => F64X_RoundTripBody(i, a, z, s, p);
        static void F64X_RoundTrip_Ozaki(Index1D i, ArrayView<double> a, ArrayView<double> z, ArrayView<double> s, ArrayView<double> p) => F64X_RoundTripBody(i, a, z, s, p);

        [TestMethod]
        public async Task F64Emulation_ArithmeticRoundTrip_IsExact() => await RunEmulatedTest(async accelerator =>
        {
            // 33554431 = 2^25 - 1 needs 25 bits: its double-float form after an add is
            // hi = 2^25, lo = -1 - a NEGATIVE low word, which the f64 -> IEEE-bits store must honor.
            var values = new double[] { 33554431.0, -33554431.0, 12345678.75, -12345678.75, 1099511627777.0,
                281474976710655.0, -140737488355327.0, 0.5, -3.0, 16777217.0, 4503599627370496.0 };
            await ForEachF64Mode(accelerator, async (variant, mode) =>
            {
                int n = values.Length;
                using var a = accelerator.Allocate1D(values);
                using var z = accelerator.Allocate1D(new double[n]);
                using var s = accelerator.Allocate1D<double>(n);
                using var p = accelerator.Allocate1D<double>(n);
                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(
                    variant == 0 ? F64X_RoundTrip_Dekker : F64X_RoundTrip_Ozaki);
                k(n, a.View, z.View, s.View, p.View);
                await accelerator.SynchronizeAsync();
                var gs = await s.CopyToHostAsync<double>();
                var gp = await p.CopyToHostAsync<double>();
                for (int i = 0; i < n; i++)
                    if (gs[i] != values[i] || gp[i] != values[i])
                        throw new Exception($"[{mode}] f64 round trip of {values[i]:R}: a+0={gs[i]:R} a*1={gp[i]:R}");
            });
        });

        // ---- 2. integer -> double ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_ToDoubleBody(Index1D i, ArrayView<int> si, ArrayView<uint> ui, ArrayView<long> sl, ArrayView<ulong> ul, ArrayView<double> o)
        {
            o[i * 4 + 0] = si[i];
            o[i * 4 + 1] = ui[i];
            o[i * 4 + 2] = sl[i];
            o[i * 4 + 3] = ul[i];
        }
        static void F64X_ToDouble_Dekker(Index1D i, ArrayView<int> a, ArrayView<uint> b, ArrayView<long> c, ArrayView<ulong> d, ArrayView<double> o) => F64X_ToDoubleBody(i, a, b, c, d, o);
        static void F64X_ToDouble_Ozaki(Index1D i, ArrayView<int> a, ArrayView<uint> b, ArrayView<long> c, ArrayView<ulong> d, ArrayView<double> o) => F64X_ToDoubleBody(i, a, b, c, d, o);

        [TestMethod]
        public async Task F64Emulation_IntegerToDouble_IsExact() => await RunEmulatedTest(async accelerator =>
        {
            var si = new int[] { int.MaxValue, int.MinValue, 16777217, -16777217, 123456789, -1, 0 };
            var ui = new uint[] { uint.MaxValue, 0x80000001u, 16777217u, 1u, 0x7FFFFFFFu, 0u, 3000000000u };
            // Every expected double below has <= 48 significant bits (2^60+1 and ulong.MaxValue ROUND to
            // 2^60 and 2^64 in IEEE double - the conversion must round in the integer domain first).
            var sl = new long[] { (1L << 47) - 1, -(1L << 47), 1L << 62, long.MinValue, (1L << 60) + 1, 123456789012345L, -2L };
            var ul = new ulong[] { ulong.MaxValue, 1UL << 63, (1UL << 48) - 1, 18000000000000000000UL, 3UL, 0UL, (1UL << 60) + 1 };
            await ForEachF64Mode(accelerator, async (variant, mode) =>
            {
                int n = si.Length;
                using var bsi = accelerator.Allocate1D(si);
                using var bui = accelerator.Allocate1D(ui);
                using var bsl = accelerator.Allocate1D(sl);
                using var bul = accelerator.Allocate1D(ul);
                using var o = accelerator.Allocate1D<double>(n * 4);
                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<uint>, ArrayView<long>, ArrayView<ulong>, ArrayView<double>>(
                    variant == 0 ? F64X_ToDouble_Dekker : F64X_ToDouble_Ozaki);
                k(n, bsi.View, bui.View, bsl.View, bul.View, o.View);
                await accelerator.SynchronizeAsync();
                var got = await o.CopyToHostAsync<double>();
                for (int i = 0; i < n; i++)
                {
                    double[] exp = { si[i], ui[i], sl[i], ul[i] };
                    string[] what = { $"int {si[i]}", $"uint {ui[i]}", $"long {sl[i]}", $"ulong {ul[i]}" };
                    for (int c = 0; c < 4; c++)
                        if (got[i * 4 + c] != exp[c])
                            throw new Exception($"[{mode}] (double)({what[c]}) = {got[i * 4 + c]:R}, expected {exp[c]:R}");
                }
            });
        });

        // ---- 3. double -> integer (truncation toward zero, in range) ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_FromDoubleBody(Index1D i, ArrayView<double> d, ArrayView<int> si, ArrayView<uint> ui, ArrayView<long> sl, ArrayView<ulong> ul)
        {
            double x = d[i];
            si[i] = (int)x;
            ui[i] = (uint)Math.Abs(x);
            sl[i] = (long)x;
            ul[i] = (ulong)Math.Abs(x);
        }
        static void F64X_FromDouble_Dekker(Index1D i, ArrayView<double> d, ArrayView<int> a, ArrayView<uint> b, ArrayView<long> c, ArrayView<ulong> e) => F64X_FromDoubleBody(i, d, a, b, c, e);
        static void F64X_FromDouble_Ozaki(Index1D i, ArrayView<double> d, ArrayView<int> a, ArrayView<uint> b, ArrayView<long> c, ArrayView<ulong> e) => F64X_FromDoubleBody(i, d, a, b, c, e);

        [TestMethod]
        public async Task F64Emulation_DoubleToInteger_InRange_Truncates() => await RunEmulatedTest(async accelerator =>
        {
            // All inside every target's range (|x| < 2^31 for the int column), so the in-range truncation
            // is identical on every platform and the host cast is the oracle.
            var d = new double[] { 12345678.75, -12345678.75, 16777217.0, -2.5, 0.25, 2147483647.0, -2147483648.0, 33554431.0 };
            await ForEachF64Mode(accelerator, async (variant, mode) =>
            {
                int n = d.Length;
                using var bd = accelerator.Allocate1D(d);
                using var bsi = accelerator.Allocate1D<int>(n);
                using var bui = accelerator.Allocate1D<uint>(n);
                using var bsl = accelerator.Allocate1D<long>(n);
                using var bul = accelerator.Allocate1D<ulong>(n);
                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<int>, ArrayView<uint>, ArrayView<long>, ArrayView<ulong>>(
                    variant == 0 ? F64X_FromDouble_Dekker : F64X_FromDouble_Ozaki);
                k(n, bd.View, bsi.View, bui.View, bsl.View, bul.View);
                await accelerator.SynchronizeAsync();
                var gsi = await bsi.CopyToHostAsync<int>();
                var gui = await bui.CopyToHostAsync<uint>();
                var gsl = await bsl.CopyToHostAsync<long>();
                var gul = await bul.CopyToHostAsync<ulong>();
                for (int i = 0; i < n; i++)
                {
                    double x = d[i];
                    if (gsi[i] != (int)x || gui[i] != (uint)Math.Abs(x) || gsl[i] != (long)x || gul[i] != (ulong)Math.Abs(x))
                        throw new Exception($"[{mode}] double {x:R} -> int {gsi[i]} uint {gui[i]} long {gsl[i]} ulong {gul[i]}; expected {(int)x} {(uint)Math.Abs(x)} {(long)x} {(ulong)Math.Abs(x)}");
                }
            });
        });

        // ---- 5. double math: exact operations, in a kernel body AND in a [NoInlining] helper ----
        // The helper RETURNS one result selected by `op` (a switch - IR SwitchBranch) rather than
        // storing into an ArrayView parameter: WebGL's Transform-Feedback model only lets the kernel
        // store at its own output slots, so a helper cannot write a buffer there.

        const int F64XMathOuts = 19;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double F64X_MathOp(double x, double y, int op)
        {
            switch (op)
            {
                case 0: return Math.Abs(x);
                case 1: return Math.Floor(x);
                case 2: return Math.Ceiling(x);
                case 3: return Math.Min(x, y);
                case 4: return Math.Max(x, y);
                case 5: return -x;
                case 6: return x + y;
                case 7: return x - y;
                case 8: return x * y;
                case 9: return x / 4.0;
                case 10: return Math.Sqrt(Math.Abs(x) * Math.Abs(x));
                case 11: return double.IsNaN(x) ? 1.0 : 0.0;
                case 12: return double.IsInfinity(x) ? 1.0 : 0.0;
                case 13: return x % y;
                case 14: return Math.Round(x);
                case 15: return Math.Truncate(x);
                case 16: return Math.Round(x, MidpointRounding.AwayFromZero);
                case 17: return global::ILGPU.Algorithms.XMath.IEEERemainder(x, y); // Math.IEEERemainder calls Math.Sign (a throw path)
                // Math.Clamp and Math.Sign(double) contain throw paths, which kernels cannot compile.
                default: return Math.Min(Math.Max(x, -100.25), 100.5);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static double F64X_MathHelper(double x, double y, int op) => F64X_MathOp(x, y, op);

        // One [NoInlining] helper PER OP for the grouped kernels below (the op constant folds the switch
        // away in ILGPU). F64X_MathHelper - one helper switching over all 19 ops - stays in the all-ops
        // Dekker gate, which is what covers a switch inside a helper. Called from the Ozaki groups it
        // cost 6-24 s of FXC per group (measured 2026-09-23): D3D inlines the helper's whole 19-op
        // body at each call site before it can fold the constant op, while each Ozaki op on its own
        // compiles in 0.07-0.74 s.
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H0(double x, double y) => F64X_MathOp(x, y, 0);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H1(double x, double y) => F64X_MathOp(x, y, 1);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H2(double x, double y) => F64X_MathOp(x, y, 2);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H3(double x, double y) => F64X_MathOp(x, y, 3);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H4(double x, double y) => F64X_MathOp(x, y, 4);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H5(double x, double y) => F64X_MathOp(x, y, 5);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H6(double x, double y) => F64X_MathOp(x, y, 6);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H7(double x, double y) => F64X_MathOp(x, y, 7);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H8(double x, double y) => F64X_MathOp(x, y, 8);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H9(double x, double y) => F64X_MathOp(x, y, 9);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H10(double x, double y) => F64X_MathOp(x, y, 10);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H11(double x, double y) => F64X_MathOp(x, y, 11);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H12(double x, double y) => F64X_MathOp(x, y, 12);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H13(double x, double y) => F64X_MathOp(x, y, 13);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H14(double x, double y) => F64X_MathOp(x, y, 14);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H15(double x, double y) => F64X_MathOp(x, y, 15);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H16(double x, double y) => F64X_MathOp(x, y, 16);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H17(double x, double y) => F64X_MathOp(x, y, 17);
        [MethodImpl(MethodImplOptions.NoInlining)] static double F64X_H18(double x, double y) => F64X_MathOp(x, y, 18);

        // Every result is exactly representable in 48 bits (sqrt(x*x) = |x| with |x| < 2^24, so
        // x*x < 2^48; the products, sums and remainders stay within 48 bits). 2.5 + 2^-30 (and its
        // negative, and 0.5 + 2^-30) put an exact .5 TIE in the f32 hi component with the deciding
        // bits in lo: rounding the components separately gives round(2.5) + round(2^-30) = 2 where
        // the value rounds to 3.
        static readonly double[] F64XMathXs = { 12345678.75, -12345678.75, 3.0, -2.5, 0.0, 16777215.0, -1234.5, double.NaN, double.PositiveInfinity, -7.5, 2.5, 3.5, 99.75,
            2.5 + Math.ScaleB(1.0, -30), -(2.5 + Math.ScaleB(1.0, -30)), 0.5 + Math.ScaleB(1.0, -30) };
        static readonly double[] F64XMathYs = { 0.25, 2.0, -7.5, -2.5, 1.0, 3.0, 1234.5, 1.0, 1.0, 2.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 };

        /// <summary>Checks every op's kernel and helper result (19 per input) against the host.</summary>
        static void F64X_CheckMath(double[] xs, double[] ys, double[] gk, double[] gh, string mode)
        {
            int n = xs.Length;
            string[] names = { "Abs", "Floor", "Ceiling", "Min", "Max", "Neg", "x+y", "x-y", "x*y", "x/4", "Sqrt(x*x)", "IsNaN", "IsInfinity", "x%y", "Round", "Truncate", "RoundAwayFromZero", "IEEERemainder", "Clamp" };
            for (int i = 0; i < n; i++)
            {
                double x = xs[i], y = ys[i];
                for (int c = 0; c < F64XMathOuts; c++)
                {
                    // Min/Max with a NaN operand: CUDA/OpenCL return the other operand (fmin/fmax)
                    // where .NET propagates NaN - a separate semantic question, not exercised here.
                    if ((c == 3 || c == 4 || c == 18) && double.IsNaN(x)) continue;
                    double e = F64X_MathOp(x, y, c), kv = gk[i * F64XMathOuts + c], hv = gh[i * F64XMathOuts + c];
                    // Sqrt under Dekker: a ~48-bit representation lands within a few units of
                    // 2^-48 relative, not always on the exact double. Everything else -
                    // and Sqrt under Ozaki / native - must be bit-exact.
                    bool same(double v) => v.Equals(e) || (c == 10 && mode == "Dekker" && Math.Abs(v - e) <= Math.Abs(e) * 1e-13);
                    if (!same(kv) || !same(hv))
                        throw new Exception($"[{mode}] {names[c]}(x={x:R}, y={y:R}): kernel={kv:R} helper={hv:R} expected={e:R}");
                }
            }
        }

        // ALL 19 ops inline AND through 19 helper calls in ONE Dekker kernel - the default mode. It is
        // the compile-time gate: FXC once took over 10 minutes on this shader (the GPU watchdog killed
        // the device; every later WebGPU/WebGL test failed with it) - a NoInlining helper emitted as a
        // loop { switch } state machine, and branchy emulation functions. 1.8 s (WebGPU) / 2.6 s
        // (WebGL) of FXC after the fix; the 30 s budget fails a regression.
        static void F64X_MathAll_Dekker(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> inKernel, ArrayView<double> inHelper)
        {
            double x = xs[i], y = ys[i];
            int b = i * F64XMathOuts;
            inKernel[b + 0] = Math.Abs(x);
            inKernel[b + 1] = Math.Floor(x);
            inKernel[b + 2] = Math.Ceiling(x);
            inKernel[b + 3] = Math.Min(x, y);
            inKernel[b + 4] = Math.Max(x, y);
            inKernel[b + 5] = -x;
            inKernel[b + 6] = x + y;
            inKernel[b + 7] = x - y;
            inKernel[b + 8] = x * y;
            inKernel[b + 9] = x / 4.0;
            inKernel[b + 10] = Math.Sqrt(Math.Abs(x) * Math.Abs(x));
            inKernel[b + 11] = double.IsNaN(x) ? 1.0 : 0.0;
            inKernel[b + 12] = double.IsInfinity(x) ? 1.0 : 0.0;
            inKernel[b + 13] = x % y;
            inKernel[b + 14] = Math.Round(x);
            inKernel[b + 15] = Math.Truncate(x);
            inKernel[b + 16] = Math.Round(x, MidpointRounding.AwayFromZero);
            inKernel[b + 17] = global::ILGPU.Algorithms.XMath.IEEERemainder(x, y);
            inKernel[b + 18] = Math.Min(Math.Max(x, -100.25), 100.5);
            inHelper[b + 0] = F64X_MathHelper(x, y, 0);
            inHelper[b + 1] = F64X_MathHelper(x, y, 1);
            inHelper[b + 2] = F64X_MathHelper(x, y, 2);
            inHelper[b + 3] = F64X_MathHelper(x, y, 3);
            inHelper[b + 4] = F64X_MathHelper(x, y, 4);
            inHelper[b + 5] = F64X_MathHelper(x, y, 5);
            inHelper[b + 6] = F64X_MathHelper(x, y, 6);
            inHelper[b + 7] = F64X_MathHelper(x, y, 7);
            inHelper[b + 8] = F64X_MathHelper(x, y, 8);
            inHelper[b + 9] = F64X_MathHelper(x, y, 9);
            inHelper[b + 10] = F64X_MathHelper(x, y, 10);
            inHelper[b + 11] = F64X_MathHelper(x, y, 11);
            inHelper[b + 12] = F64X_MathHelper(x, y, 12);
            inHelper[b + 13] = F64X_MathHelper(x, y, 13);
            inHelper[b + 14] = F64X_MathHelper(x, y, 14);
            inHelper[b + 15] = F64X_MathHelper(x, y, 15);
            inHelper[b + 16] = F64X_MathHelper(x, y, 16);
            inHelper[b + 17] = F64X_MathHelper(x, y, 17);
            inHelper[b + 18] = F64X_MathHelper(x, y, 18);
        }

        // An ACYCLIC [NoInlining] helper must be emitted as a forward-only state machine (one pass of
        // `if (current_block == k)` blocks), not `loop { switch (current_block) }`: FXC - WebGPU's
        // compiler wherever Dawn cannot use DXC - took 136 s on F64X_MathAll_Dekker with the loop and
        // 2 s without. Chrome here compiles with DXC, which does not care, so no timing test on this
        // machine can see a regression; this checks the generated WGSL itself.
        [TestMethod]
        public async Task WGSL_AcyclicNoInliningHelper_IsForwardOnlyStateMachine() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WGSL codegen check (WebGPU lane only).");
            var generated = ShaderCompiler.Generate(
                (Action<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>)F64X_MathAll_Dekker,
                CapabilityProfiles.FromAccelerator(webgpu, webgpu.EnabledFeatures));
            string wgsl = generated.Source ?? "";
            int start = wgsl.IndexOf("fn F64X_MathHelper", StringComparison.Ordinal);
            if (start < 0) throw new Exception("F64X_MathHelper was not emitted as a WGSL function.");
            int end = wgsl.IndexOf("\n}", start, StringComparison.Ordinal);
            string helper = wgsl.Substring(start, (end < 0 ? wgsl.Length : end) - start);
            if (helper.Contains("loop {") || !helper.Contains("if (current_block == "))
                throw new Exception("The acyclic helper is not a forward-only state machine:\n" + helper);
            await Task.CompletedTask;
        });

        // The ops run in GROUPS, one kernel per group: D3D's FXC (Chrome's WebGPU and WebGL shader
        // compiler on Windows) compiles superlinearly in shader size, and all 19 ops inline plus 19
        // helper calls in ONE Ozaki (quad-float) kernel took 130 s (WebGPU) / 190 s (WebGL) to compile.
        // Each group writes its OWN buffers at i * groupSize + j, in store order: WebGL's Transform
        // Feedback places store j of thread i at exactly that index (the one-store-per-thread
        // contract, WebGL/CLAUDE.md); a subset of a wider i * 19 + op layout lands elsewhere.
        static readonly int[][] F64X_MathGroupOps = {
            new[] { 0, 1, 2, 3, 4, 5 },
            new[] { 6, 7, 8, 9, 11, 12 },
            new[] { 10, 13 },
            new[] { 14, 15, 16, 18 },
            new[] { 17 },
        };
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_MathG0(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> inKernel, ArrayView<double> inHelper)
        {
            double x = xs[i], y = ys[i];
            int b = i * 6;
            inKernel[b + 0] = Math.Abs(x);
            inKernel[b + 1] = Math.Floor(x);
            inKernel[b + 2] = Math.Ceiling(x);
            inKernel[b + 3] = Math.Min(x, y);
            inKernel[b + 4] = Math.Max(x, y);
            inKernel[b + 5] = -x;
            inHelper[b + 0] = F64X_H0(x, y);
            inHelper[b + 1] = F64X_H1(x, y);
            inHelper[b + 2] = F64X_H2(x, y);
            inHelper[b + 3] = F64X_H3(x, y);
            inHelper[b + 4] = F64X_H4(x, y);
            inHelper[b + 5] = F64X_H5(x, y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_MathG1(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> inKernel, ArrayView<double> inHelper)
        {
            double x = xs[i], y = ys[i];
            int b = i * 6;
            inKernel[b + 0] = x + y;
            inKernel[b + 1] = x - y;
            inKernel[b + 2] = x * y;
            inKernel[b + 3] = x / 4.0;
            inKernel[b + 4] = double.IsNaN(x) ? 1.0 : 0.0;
            inKernel[b + 5] = double.IsInfinity(x) ? 1.0 : 0.0;
            inHelper[b + 0] = F64X_H6(x, y);
            inHelper[b + 1] = F64X_H7(x, y);
            inHelper[b + 2] = F64X_H8(x, y);
            inHelper[b + 3] = F64X_H9(x, y);
            inHelper[b + 4] = F64X_H11(x, y);
            inHelper[b + 5] = F64X_H12(x, y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_MathG2(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> inKernel, ArrayView<double> inHelper)
        {
            double x = xs[i], y = ys[i];
            int b = i * 2;
            inKernel[b + 0] = Math.Sqrt(Math.Abs(x) * Math.Abs(x));
            inKernel[b + 1] = x % y;
            inHelper[b + 0] = F64X_H10(x, y);
            inHelper[b + 1] = F64X_H13(x, y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_MathG3(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> inKernel, ArrayView<double> inHelper)
        {
            double x = xs[i], y = ys[i];
            int b = i * 4;
            inKernel[b + 0] = Math.Round(x);
            inKernel[b + 1] = Math.Truncate(x);
            inKernel[b + 2] = Math.Round(x, MidpointRounding.AwayFromZero);
            inKernel[b + 3] = Math.Min(Math.Max(x, -100.25), 100.5);
            inHelper[b + 0] = F64X_H14(x, y);
            inHelper[b + 1] = F64X_H15(x, y);
            inHelper[b + 2] = F64X_H16(x, y);
            inHelper[b + 3] = F64X_H18(x, y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_MathG4(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> inKernel, ArrayView<double> inHelper)
        {
            double x = xs[i], y = ys[i];
            int b = i * 1;
            inKernel[b + 0] = global::ILGPU.Algorithms.XMath.IEEERemainder(x, y);
            inHelper[b + 0] = F64X_H17(x, y);
        }
        static void F64X_Math_Dekker_G0(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG0(i, a, b, c, d);
        static void F64X_Math_Ozaki_G0(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG0(i, a, b, c, d);
        static void F64X_Math_Dekker_G1(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG1(i, a, b, c, d);
        static void F64X_Math_Ozaki_G1(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG1(i, a, b, c, d);
        static void F64X_Math_Dekker_G2(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG2(i, a, b, c, d);
        static void F64X_Math_Ozaki_G2(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG2(i, a, b, c, d);
        static void F64X_Math_Dekker_G3(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG3(i, a, b, c, d);
        static void F64X_Math_Ozaki_G3(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG3(i, a, b, c, d);
        static void F64X_Math_Dekker_G4(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG4(i, a, b, c, d);
        static void F64X_Math_Ozaki_G4(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> d) => F64X_MathG4(i, a, b, c, d);
        static readonly Action<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>[] F64X_MathDekkerGroups =
            { F64X_Math_Dekker_G0, F64X_Math_Dekker_G1, F64X_Math_Dekker_G2, F64X_Math_Dekker_G3, F64X_Math_Dekker_G4 };
        static readonly Action<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>[] F64X_MathOzakiGroups =
            { F64X_Math_Ozaki_G0, F64X_Math_Ozaki_G1, F64X_Math_Ozaki_G2, F64X_Math_Ozaki_G3, F64X_Math_Ozaki_G4 };

        // Ten shaders (5 groups x 2 modes). Offline FXC on ANGLE's HLSL (2026-09-23, per-op helpers): Dekker
        // 0.1-0.4 s per group; Ozaki (quad-float) 3.4 / 4.1 / 5.5 / 6.7 / 13.8 s - about 35 s in all, past
        // the 30 s default. The 13.8 s group is Sqrt and % twice each: an Ozaki load + store alone is
        // ~313 FXC instruction slots and Sqrt / % / Round each add 1000-1500, and FXC is superlinear in
        // the total.
        [TestMethod(Timeout = 120000)]
        public async Task F64Emulation_MathOperations_KernelAndNoInliningHelper_AreExact() => await RunEmulatedTest(async accelerator =>
        {
            var xs = F64XMathXs;
            var ys = F64XMathYs;
            await ForEachF64Mode(accelerator, async (variant, mode) =>
            {
                int n = xs.Length;
                using var bx = accelerator.Allocate1D(xs);
                using var by = accelerator.Allocate1D(ys);
                var gk = new double[n * F64XMathOuts];
                var gh = new double[n * F64XMathOuts];
                var groups = variant == 0 ? F64X_MathDekkerGroups : F64X_MathOzakiGroups;
                for (int g = 0; g < groups.Length; g++)
                {
                    var ops = F64X_MathGroupOps[g];
                    using var ok = accelerator.Allocate1D<double>(n * ops.Length);
                    using var oh = accelerator.Allocate1D<double>(n * ops.Length);
                    // One line per group (PMT_CONSOLE_LOG=F64X): a compile hang names its own group.
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(groups[g]);
                    k(n, bx.View, by.View, ok.View, oh.View);
                    await accelerator.SynchronizeAsync();
                    Console.WriteLine($"[F64X] {accelerator.AcceleratorType} {mode} group {g}: {sw.ElapsedMilliseconds} ms");
                    var rk = await ok.CopyToHostAsync<double>();
                    var rh = await oh.CopyToHostAsync<double>();
                    for (int i = 0; i < n; i++)
                    {
                        for (int j = 0; j < ops.Length; j++)
                        {
                            gk[i * F64XMathOuts + ops[j]] = rk[i * ops.Length + j];
                            gh[i * F64XMathOuts + ops[j]] = rh[i * ops.Length + j];
                        }
                    }
                }
                F64X_CheckMath(xs, ys, gk, gh, mode);
            });
        });

        [TestMethod]
        public async Task F64Emulation_AllMathOpsInOneDekkerKernel_CompileAndAreExact() => await RunEmulatedTest(async accelerator =>
        {
            var xs = F64XMathXs;
            var ys = F64XMathYs;
            await ForEachF64Mode(accelerator, async (variant, mode) =>
            {
                int n = xs.Length;
                using var bx = accelerator.Allocate1D(xs);
                using var by = accelerator.Allocate1D(ys);
                if (variant != 0) return; // Dekker (default) only - see F64X_MathAll_Dekker
                using var ok = accelerator.Allocate1D<double>(n * F64XMathOuts);
                using var oh = accelerator.Allocate1D<double>(n * F64XMathOuts);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(F64X_MathAll_Dekker);
                k(n, bx.View, by.View, ok.View, oh.View);
                await accelerator.SynchronizeAsync();
                Console.WriteLine($"[F64X] {accelerator.AcceleratorType} {mode} all-ops kernel: {sw.ElapsedMilliseconds} ms");
                var gk = await ok.CopyToHostAsync<double>();
                var gh = await oh.CopyToHostAsync<double>();
                F64X_CheckMath(xs, ys, gk, gh, mode);
            });
        });

        // float %: C# truncates (fmod) - -7.5f % 2f is -1.5f, not the floored 0.5f.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F64X_FloatRemHelper(float x, float y) => x % y;

        static void F64X_FloatRem(Index1D i, ArrayView<float> xs, ArrayView<float> ys, ArrayView<float> inKernel, ArrayView<float> inHelper)
        {
            inKernel[i] = xs[i] % ys[i];
            inHelper[i] = F64X_FloatRemHelper(xs[i], ys[i]);
        }

        [TestMethod]
        public async Task FloatRemainder_Truncates_KernelAndNoInliningHelper() => await RunTest(async accelerator =>
        {
            var xs = new float[] { -7.5f, 7.5f, -7.5f, 7.5f, 5.25f, -0.5f };
            var ys = new float[] { 2f, -2f, -2f, 2f, 1.5f, 3f };
            int n = xs.Length;
            using var bx = accelerator.Allocate1D(xs);
            using var by = accelerator.Allocate1D(ys);
            using var ok = accelerator.Allocate1D<float>(n);
            using var oh = accelerator.Allocate1D<float>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(F64X_FloatRem);
            k(n, bx.View, by.View, ok.View, oh.View);
            await accelerator.SynchronizeAsync();
            var gk = await ok.CopyToHostAsync<float>();
            var gh = await oh.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                float e = xs[i] % ys[i];
                if (gk[i] != e || gh[i] != e)
                    throw new Exception($"{xs[i]} % {ys[i]}: kernel={gk[i]} helper={gh[i]} expected={e}");
            }
        });

        // float Round (to EVEN - GLSL round() leaves .5 to the implementation) and long Min/Max
        // (GLSL min/max on the uvec2 words compare each word on its own).
        [MethodImpl(MethodImplOptions.NoInlining)]
        static long F64X_LongMinHelper(long a, long b) => Math.Min(a, b);

        static void F64X_RoundMinMax(Index1D i, ArrayView<float> f, ArrayView<long> a, ArrayView<long> b, ArrayView<float> of, ArrayView<long> omin, ArrayView<long> omax, ArrayView<long> ominh)
        {
            of[i] = MathF.Round(f[i]);
            omin[i] = Math.Min(a[i], b[i]);
            omax[i] = Math.Max(a[i], b[i]);
            ominh[i] = F64X_LongMinHelper(a[i], b[i]);
        }

        [TestMethod]
        public async Task FloatRoundToEven_And_LongMinMax() => await RunEmulatedTest(async accelerator =>
        {
            var f = new float[] { 2.5f, 3.5f, -2.5f, 0.5f, 1.5f, 7.25f };
            var a = new long[] { 1L << 40, -(1L << 40), -1L, 5L, (1L << 32) - 1, long.MinValue };
            var b = new long[] { (1L << 32) + 7, 3L, 1L, -5L, 1L << 32, long.MaxValue };
            int n = f.Length;
            using var bf = accelerator.Allocate1D(f);
            using var ba = accelerator.Allocate1D(a);
            using var bb = accelerator.Allocate1D(b);
            using var of = accelerator.Allocate1D<float>(n);
            using var omin = accelerator.Allocate1D<long>(n);
            using var omax = accelerator.Allocate1D<long>(n);
            using var ominh = accelerator.Allocate1D<long>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<long>, ArrayView<long>, ArrayView<float>, ArrayView<long>, ArrayView<long>, ArrayView<long>>(F64X_RoundMinMax);
            k(n, bf.View, ba.View, bb.View, of.View, omin.View, omax.View, ominh.View);
            await accelerator.SynchronizeAsync();
            var gf = await of.CopyToHostAsync<float>();
            var gmin = await omin.CopyToHostAsync<long>();
            var gmax = await omax.CopyToHostAsync<long>();
            var gminh = await ominh.CopyToHostAsync<long>();
            for (int i = 0; i < n; i++)
            {
                if (gf[i] != MathF.Round(f[i]))
                    throw new Exception($"MathF.Round({f[i]}) = {gf[i]}, expected {MathF.Round(f[i])}");
                long mn = Math.Min(a[i], b[i]), mx = Math.Max(a[i], b[i]);
                if (gmin[i] != mn || gmax[i] != mx || gminh[i] != mn)
                    throw new Exception($"Min/Max({a[i]}, {b[i]}) = {gmin[i]}/{gmax[i]} helper {gminh[i]}, expected {mn}/{mx}");
            }
        });

        // ---- 6. transcendentals: at least f32 accuracy (the emulated backends have no
        //         double-precision transcendental implementations) ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_TransBody(Index1D i, ArrayView<double> xs, ArrayView<double> o)
        {
            double x = xs[i];
            o[i * 5 + 0] = Math.Sin(x);
            o[i * 5 + 1] = Math.Cos(x);
            o[i * 5 + 2] = Math.Exp(x);
            o[i * 5 + 3] = Math.Log(x + 2.0);
            o[i * 5 + 4] = Math.Sqrt(x + 2.0);
        }
        static void F64X_Trans_Dekker(Index1D i, ArrayView<double> a, ArrayView<double> b) => F64X_TransBody(i, a, b);
        static void F64X_Trans_Ozaki(Index1D i, ArrayView<double> a, ArrayView<double> b) => F64X_TransBody(i, a, b);

        [TestMethod]
        public async Task F64Emulation_Transcendentals_AtLeastFloatAccuracy() => await RunEmulatedTest(async accelerator =>
        {
            var xs = new double[] { 0.5, -1.25, 1.0, 0.0, 2.75, -0.3 };
            await ForEachF64Mode(accelerator, async (variant, mode) =>
            {
                int n = xs.Length;
                using var bx = accelerator.Allocate1D(xs);
                using var o = accelerator.Allocate1D<double>(n * 5);
                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>>(
                    variant == 0 ? F64X_Trans_Dekker : F64X_Trans_Ozaki);
                k(n, bx.View, o.View);
                await accelerator.SynchronizeAsync();
                var g = await o.CopyToHostAsync<double>();
                string[] names = { "Sin", "Cos", "Exp", "Log(x+2)", "Sqrt(x+2)" };
                for (int i = 0; i < n; i++)
                {
                    double x = xs[i];
                    double[] e = { Math.Sin(x), Math.Cos(x), Math.Exp(x), Math.Log(x + 2.0), Math.Sqrt(x + 2.0) };
                    for (int c = 0; c < 5; c++)
                    {
                        double got = g[i * 5 + c];
                        if (Math.Abs(got - e[c]) > 2e-6 * Math.Max(1.0, Math.Abs(e[c])))
                            throw new Exception($"[{mode}] {names[c]}({x:R}) = {got:R}, expected {e[c]:R}");
                    }
                }
            });
        });

        // ---- 7. Math.Abs on long ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        static long F64X_LongAbsHelper(long v) => Math.Abs(v);

        static void F64X_LongAbs(Index1D i, ArrayView<long> v, ArrayView<long> inKernel, ArrayView<long> inHelper)
        {
            inKernel[i] = Math.Abs(v[i]);
            inHelper[i] = F64X_LongAbsHelper(v[i]);
        }

        [TestMethod]
        public async Task I64Emulation_Abs_KernelAndNoInliningHelper() => await RunEmulatedTest(async accelerator =>
        {
            var v = new long[] { -5L, 5L, -(1L << 40) - 3, 1L << 62, -1L, 0L, long.MaxValue, -long.MaxValue };
            int n = v.Length;
            using var bv = accelerator.Allocate1D(v);
            using var ok = accelerator.Allocate1D<long>(n);
            using var oh = accelerator.Allocate1D<long>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<long>, ArrayView<long>>(F64X_LongAbs);
            k(n, bv.View, ok.View, oh.View);
            await accelerator.SynchronizeAsync();
            var gk = await ok.CopyToHostAsync<long>();
            var gh = await oh.CopyToHostAsync<long>();
            for (int i = 0; i < n; i++)
                if (gk[i] != Math.Abs(v[i]) || gh[i] != Math.Abs(v[i]))
                    throw new Exception($"Math.Abs({v[i]}): kernel={gk[i]} helper={gh[i]} expected={Math.Abs(v[i])}");
        });

        // ---- 4. 64-bit integer <-> float ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void F64X_LongFloatBody(Index1D i, ArrayView<long> sl, ArrayView<ulong> ul, ArrayView<float> f, ArrayView<float> of, ArrayView<long> ol, ArrayView<ulong> oul)
        {
            of[i * 2 + 0] = sl[i];
            of[i * 2 + 1] = ul[i];
            ol[i] = (long)f[i];
            oul[i] = (ulong)Math.Abs(f[i]);
        }
        static void F64X_LongFloat(Index1D i, ArrayView<long> a, ArrayView<ulong> b, ArrayView<float> c, ArrayView<float> d, ArrayView<long> e, ArrayView<ulong> g) => F64X_LongFloatBody(i, a, b, c, d, e, g);

        [TestMethod]
        public async Task I64Emulation_LongFloatConversions_AreExact() => await RunEmulatedTest(async accelerator =>
        {
            var sl = new long[] { (1L << 40) + 1, long.MinValue, 16777217L, -16777217L, 123456789012345L, -7L };
            var ul = new ulong[] { ulong.MaxValue, 1UL << 63, (1UL << 40) + 1, 16777217UL, 5UL, 0UL };
            // Floats well inside long range; their integer values have up to 24 significant bits.
            var f = new float[] { 1.5e18f, -1.5e18f, 16777216f, -12345.75f, 3.0e10f, 0.9f };
            int n = sl.Length;
            using var bsl = accelerator.Allocate1D(sl);
            using var bul = accelerator.Allocate1D(ul);
            using var bf = accelerator.Allocate1D(f);
            using var of = accelerator.Allocate1D<float>(n * 2);
            using var ol = accelerator.Allocate1D<long>(n);
            using var oul = accelerator.Allocate1D<ulong>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<ulong>, ArrayView<float>, ArrayView<float>, ArrayView<long>, ArrayView<ulong>>(F64X_LongFloat);
            k(n, bsl.View, bul.View, bf.View, of.View, ol.View, oul.View);
            await accelerator.SynchronizeAsync();
            var gf = await of.CopyToHostAsync<float>();
            var gl = await ol.CopyToHostAsync<long>();
            var gul = await oul.CopyToHostAsync<ulong>();
            for (int i = 0; i < n; i++)
            {
                if (gf[i * 2] != (float)sl[i] || gf[i * 2 + 1] != (float)ul[i])
                    throw new Exception($"(float)long {sl[i]} = {gf[i * 2]:R} (expected {(float)sl[i]:R}); (float)ulong {ul[i]} = {gf[i * 2 + 1]:R} (expected {(float)ul[i]:R})");
                if (gl[i] != (long)f[i] || gul[i] != (ulong)Math.Abs(f[i]))
                    throw new Exception($"float {f[i]:R} -> long {gl[i]} (expected {(long)f[i]}), ulong {gul[i]} (expected {(ulong)Math.Abs(f[i])})");
            }
        });
    }
}
