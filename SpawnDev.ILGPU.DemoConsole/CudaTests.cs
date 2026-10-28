using ILGPU;
using ILGPU.Algorithms.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

public class CudaTests : BackendTestBase
{
    public CudaTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
    protected override string BackendName => "CUDA";

    static void UniformsLoopCounterKernel(Index1D i, ArrayView<int> data, ArrayView<int> o)
    {
        int n = 0;
        while (n < data[i])
            n++;
        if (n > 3) o[i] = n * 7;
        else o[i] = -n;
    }

    static void UniformsAtomicSlotKernel(Index1D i, ArrayView<int> counter, ArrayView<int> o)
    {
        int slot = Atomic.Add(ref counter[0], 1);
        if (slot < 5) o[i] = 1;
        else o[i] = 2;
    }

    static void UniformsSharedWordKernel(Index1D i, ArrayView<int> state, ArrayView<int> o)
    {
        int cur = state[0];
        if (cur > 3) o[i] = 1;
        else o[i] = 2;
    }

    // At O2 (the default since 5.4.9) the PTX backend marks a branch `bra.uni` when ILGPU's Uniforms analysis calls its
    // condition uniform, and ptxas may then compute that condition once per warp. The analysis followed data only, so a
    // loop counter threads leave at different iterations (a phi of uniform values), an atomic's result and a load from
    // one address all counted as uniform; SpawnDev.Codecs' CUDA JPEG Huffman kernels came out wrong (2026-10-28). Every
    // branch in these kernels depends on the thread, so none may be `bra.uni`. Checks the PTX: a wrong `.uni` does not
    // fail reliably at run time in a small kernel.
    [TestMethod]
    public async Task O2_PtxBranchesOnThreadDependentValues_AreNotUniform()
    {
        using var context = Context.Create(builder => builder.Cuda().Optimize(OptimizationLevel.O2));
        if (context.GetCudaDevices().Count == 0)
            throw new UnsupportedTestException("No CUDA devices found");
        using var accelerator = context.CreateCudaAccelerator(0);
        var bad = new List<string>();
        foreach (var (name, method) in new (string, System.Reflection.MethodInfo)[]
        {
            ("loop counter", ((Action<Index1D, ArrayView<int>, ArrayView<int>>)UniformsLoopCounterKernel).Method),
            ("atomic slot", ((Action<Index1D, ArrayView<int>, ArrayView<int>>)UniformsAtomicSlotKernel).Method),
            ("load of one word", ((Action<Index1D, ArrayView<int>, ArrayView<int>>)UniformsSharedWordKernel).Method),
        })
        {
            var ptx = ((ILGPU.Backends.PTX.PTXCompiledKernel)accelerator.CompileKernel(
                ILGPU.Backends.EntryPoints.EntryPointDescription.FromImplicitlyGroupedKernel(method))).PTXAssembly;
            // Conditional branches only: an unconditional `bra.uni` is uniform by definition.
            int uni = System.Text.RegularExpressions.Regex.Matches(ptx, @"@!?%p\d+\s+bra\.uni\b").Count;
            if (uni != 0) bad.Add($"{name}: {uni} conditional bra.uni");
        }
        await Task.CompletedTask;
        if (bad.Count > 0) throw new Exception("thread-dependent branches emitted as uniform: " + string.Join("; ", bad));
    }

    protected override Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
    {
        var context = Context.Create(builder => builder.AllAccelerators().EnableAlgorithms());
        var cudaDevices = context.GetCudaDevices();
        if (cudaDevices.Count == 0)
        {
            context.Dispose();
            throw new UnsupportedTestException("No CUDA devices found");
        }
        var accelerator = cudaDevices[0].CreateAccelerator(context);
        return Task.FromResult<(Context, Accelerator)>((context, accelerator));
    }

    // cuDriverGetVersion can report a newer CUDA than the driver's PTX JIT accepts
    // (430.x reports 10.2 but its JIT stops at PTX ISA 6.4); the accelerator must end up
    // on an ISA the driver actually loads.
    [TestMethod]
    public async Task Tests_PTX_AcceleratorInstructionSet_IsAcceptedByDriverJit() =>
        await RunTest(async accelerator =>
    {
        var cuda = (CudaAccelerator)accelerator;
        cuda.Bind();
        var ptx = $".version {cuda.InstructionSet}\n" +
            $".target {cuda.Architecture.ToString().ToLowerInvariant()}\n" +
            ".address_size 64\n";
        var error = CudaAPI.CurrentAPI.LoadModule(out var module, ptx, out var log);
        if (error == CudaError.CUDA_SUCCESS)
            CudaAPI.CurrentAPI.DestroyModule(module);
        else
            throw new Exception(
                $"driver JIT rejected the accelerator's PTX ISA {cuda.InstructionSet} ({error}): {log}");
        await Task.CompletedTask;
    });

    // Half arithmetic and compares on hostile bit patterns against System.Half (IEEE, RNE).
    // Devices below sm_53 have no f16 arithmetic and run this through PTXFloat16Emulation;
    // native devices run the same kernel on the real instructions.
    static void HalfHostileOpsKernel(
        Index1D idx,
        ArrayView<global::ILGPU.Half> a,
        ArrayView<global::ILGPU.Half> b,
        ArrayView<global::ILGPU.Half> sum,
        ArrayView<global::ILGPU.Half> difference,
        ArrayView<global::ILGPU.Half> product,
        ArrayView<int> flags)
    {
        var x = a[idx];
        var y = b[idx];
        sum[idx] = x + y;
        difference[idx] = x - y;
        product[idx] = x * y;
        int f = 0;
        if (x < y) f |= 1;
        if (x <= y) f |= 2;
        if (x == y) f |= 4;
        if (x != y) f |= 8;
        flags[idx] = f;
    }

    [TestMethod]
    public async Task Tests_PTX_Float16_ArithmeticAndCompare_MatchCpuOracle() =>
        await RunTest(async accelerator =>
    {
        // +0, -0, min/max subnormal, min normal, 1, -1, 1+2^-10, 2^-11 (exact tie against 1),
        // 2^-24 (below the tie), max finite, +inf, -inf, two NaNs, 1/3, 2048.
        ushort[] patterns =
        {
            0x0000, 0x8000, 0x0001, 0x03FF, 0x0400, 0x3C00, 0xBC00, 0x3C01,
            0x1000, 0x0100, 0x7BFF, 0x7C00, 0xFC00, 0x7E00, 0xFE01, 0x3555, 0x6800,
        };
        int n = patterns.Length * patterns.Length;
        var left = new global::ILGPU.Half[n];
        var right = new global::ILGPU.Half[n];
        for (int i = 0; i < patterns.Length; i++)
        for (int j = 0; j < patterns.Length; j++)
        {
            left[i * patterns.Length + j] = global::ILGPU.Half.FromRawBits(patterns[i]);
            right[i * patterns.Length + j] = global::ILGPU.Half.FromRawBits(patterns[j]);
        }
        using var aBuf = accelerator.Allocate1D(left);
        using var bBuf = accelerator.Allocate1D(right);
        using var sumBuf = accelerator.Allocate1D<global::ILGPU.Half>(n);
        using var diffBuf = accelerator.Allocate1D<global::ILGPU.Half>(n);
        using var prodBuf = accelerator.Allocate1D<global::ILGPU.Half>(n);
        using var flagBuf = accelerator.Allocate1D<int>(n);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>,
            ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>,
            ArrayView<global::ILGPU.Half>, ArrayView<int>>(HalfHostileOpsKernel);
        kernel((Index1D)n, aBuf.View, bBuf.View, sumBuf.View, diffBuf.View, prodBuf.View,
            flagBuf.View);
        await accelerator.SynchronizeAsync();
        var sum = await sumBuf.CopyToHostAsync<global::ILGPU.Half>();
        var diff = await diffBuf.CopyToHostAsync<global::ILGPU.Half>();
        var prod = await prodBuf.CopyToHostAsync<global::ILGPU.Half>();
        var flags = await flagBuf.CopyToHostAsync<int>();

        var errors = new List<string>();
        void Check(string op, int k, ushort expected, global::ILGPU.Half actual)
        {
            // Any NaN matches any NaN; everything else must match bit for bit (incl. -0).
            bool ok = ExactHalfOracle.IsNaN(expected)
                ? ExactHalfOracle.IsNaN(actual.RawValue)
                : actual.RawValue == expected;
            if (!ok && errors.Count < 8)
                errors.Add($"{op}(0x{patterns[k / patterns.Length]:X4}, " +
                    $"0x{patterns[k % patterns.Length]:X4}): expected " +
                    $"0x{expected:X4}, got 0x{actual.RawValue:X4}");
        }
        for (int k = 0; k < n; k++)
        {
            ushort xb = patterns[k / patterns.Length];
            ushort yb = patterns[k % patterns.Length];
            var x = BitConverter.UInt16BitsToHalf(xb);
            var y = BitConverter.UInt16BitsToHalf(yb);
            Check("add", k, ExactHalfOracle.Add(xb, yb), sum[k]);
            Check("sub", k, ExactHalfOracle.Sub(xb, yb), diff[k]);
            Check("mul", k, ExactHalfOracle.Mul(xb, yb), prod[k]);
            int expectedFlags = (x < y ? 1 : 0) | (x <= y ? 2 : 0) | (x == y ? 4 : 0)
                | (x != y ? 8 : 0);
            if (flags[k] != expectedFlags && errors.Count < 8)
                errors.Add($"compare(0x{patterns[k / patterns.Length]:X4}, " +
                    $"0x{patterns[k % patterns.Length]:X4}): expected flags {expectedFlags}, " +
                    $"got {flags[k]}");
        }
        if (errors.Count > 0)
            throw new Exception("Half ops differ from the CPU oracle: " +
                string.Join("; ", errors));
    });

    // Hostile bit patterns for the three-operand and fast-math tests: signed zeros,
    // subnormals (incl. 2^-15), exact ties against 1.0, a product that cancels against
    // -(1+2^-9) into a subnormal, max finite, infinities and NaNs.
    static readonly ushort[] HostilePatterns =
    {
        0x0000, 0x8000, 0x0001, 0x03FF, 0x0200, 0x0400, 0x3C00, 0xBC00, 0x3C01, 0x3C02,
        0xBC02, 0x1000, 0x0100, 0x7BFF, 0xFBFF, 0x7C00, 0xFC00, 0x7E00, 0x3555, 0x6800,
    };

    // fma.rn.f16 as inline PTX: no public Half API reaches that instruction (FmaFP32 is a
    // managed f32 body on every architecture), so this is the only way to run the rewrite.
    // The helper keeps a .func in the module, which the rewrite has to cover as well.
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static ushort HalfFmaHelper(ushort a, ushort b, ushort c)
    {
        CudaAsm.Emit("fma.rn.f16 %0, %1, %2, %3;", out ushort result, a, b, c);
        return result;
    }

    static void HalfFmaKernel(
        Index1D idx,
        ArrayView<ushort> a,
        ArrayView<ushort> b,
        ArrayView<ushort> c,
        ArrayView<ushort> inlined,
        ArrayView<ushort> viaHelper)
    {
        ushort x = a[idx], y = b[idx], z = c[idx];
        CudaAsm.Emit("fma.rn.f16 %0, %1, %2, %3;", out ushort result, x, y, z);
        inlined[idx] = result;
        viaHelper[idx] = HalfFmaHelper(x, y, z);
    }

    [TestMethod]
    public async Task Tests_PTX_Float16_Fma_MatchesExactOracle() =>
        await RunTest(async accelerator =>
    {
        // Every triple of the hostile patterns; the oracle rounds a*b+c once, exactly.
        int p = HostilePatterns.Length;
        var triples = new List<(ushort A, ushort B, ushort C)>();
        for (int i = 0; i < p * p * p; i++)
            triples.Add((HostilePatterns[i / (p * p)], HostilePatterns[i / p % p],
                HostilePatterns[i % p]));
        // Double-rounding traps: the exact sum is a hair (2^-26, below what f32 holds next to
        // 1.0) off an f16 tie. Rounding the sum through f32 lands exactly on the tie and then
        // rounds to even, which is the wrong neighbour for these. 99 * 331 = 2^15 + 1 and
        // 217 * 151 = 2^15 - 1, so the products are 2^-11 + 2^-26 and 2^-11 - 2^-26.
        static ushort Bits(float v) => BitConverter.HalfToUInt16Bits((System.Half)v);
        const float Q = 1f / 8192f; // 2^-13
        foreach (var (x, y, z) in new[]
        {
            (99 * Q, 331 * Q, 1f),            // 1 + 2^-11 + 2^-26: above the tie, up is right
            (217 * Q, 151 * Q, 1f + 1f / 1024), // 1 + 2^-10 + 2^-11 - 2^-26: below, down is right
        })
        {
            triples.Add((Bits(x), Bits(y), Bits(z)));
            triples.Add((Bits(-x), Bits(y), Bits(-z)));
        }
        int n = triples.Count;
        var a = new ushort[n];
        var b = new ushort[n];
        var c = new ushort[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = triples[i].A;
            b[i] = triples[i].B;
            c[i] = triples[i].C;
        }
        using var aBuf = accelerator.Allocate1D(a);
        using var bBuf = accelerator.Allocate1D(b);
        using var cBuf = accelerator.Allocate1D(c);
        using var inlinedBuf = accelerator.Allocate1D<ushort>(n);
        using var helperBuf = accelerator.Allocate1D<ushort>(n);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView<ushort>, ArrayView<ushort>, ArrayView<ushort>,
            ArrayView<ushort>, ArrayView<ushort>>(HalfFmaKernel);
        kernel((Index1D)n, aBuf.View, bBuf.View, cBuf.View, inlinedBuf.View, helperBuf.View);
        await accelerator.SynchronizeAsync();
        var inlined = await inlinedBuf.CopyToHostAsync<ushort>();
        var viaHelper = await helperBuf.CopyToHostAsync<ushort>();

        var errors = new List<string>();
        for (int i = 0; i < n; i++)
        {
            ushort expected = ExactHalfOracle.Fma(a[i], b[i], c[i]);
            foreach (var (name, actual) in new[] { ("inline", inlined[i]), ("helper", viaHelper[i]) })
            {
                bool ok = ExactHalfOracle.IsNaN(expected)
                    ? ExactHalfOracle.IsNaN(actual)
                    : actual == expected;
                if (!ok && errors.Count < 8)
                    errors.Add($"fma {name}(0x{a[i]:X4}, 0x{b[i]:X4}, 0x{c[i]:X4}): " +
                        $"expected 0x{expected:X4}, got 0x{actual:X4}");
            }
        }
        if (errors.Count > 0)
            throw new Exception("Half fma differs from the exact oracle: " +
                string.Join("; ", errors));
    });

    // The real f16 PTX instructions (plain and .ftz), written as inline PTX: Half's operators
    // compute in f32 and never emit them, so this is what exercises the add/sub/mul/min/max
    // rewrite. Each op sits on its own line, as the rewrite works line by line.
    static void HalfInlineArithmeticKernel(
        Index1D idx, ArrayView<ushort> a, ArrayView<ushort> b, ArrayView<ushort> o)
    {
        ushort x = a[idx], y = b[idx];
        CudaAsm.Emit("add.f16 %0, %1, %2;", out ushort add, x, y);
        CudaAsm.Emit("sub.f16 %0, %1, %2;", out ushort sub, x, y);
        CudaAsm.Emit("mul.f16 %0, %1, %2;", out ushort mul, x, y);
        CudaAsm.Emit("add.ftz.f16 %0, %1, %2;", out ushort addFtz, x, y);
        CudaAsm.Emit("sub.ftz.f16 %0, %1, %2;", out ushort subFtz, x, y);
        CudaAsm.Emit("mul.ftz.f16 %0, %1, %2;", out ushort mulFtz, x, y);
        int i = idx * 6;
        o[i] = add;
        o[i + 1] = sub;
        o[i + 2] = mul;
        o[i + 3] = addFtz;
        o[i + 4] = subFtz;
        o[i + 5] = mulFtz;
    }

    // min.f16 / max.f16 are native only from sm_80 (and sm_53+ below that fails in ptxas), so
    // they run in their own kernel that the test skips on such devices.
    static void HalfInlineMinMaxKernel(
        Index1D idx, ArrayView<ushort> a, ArrayView<ushort> b, ArrayView<ushort> o)
    {
        ushort x = a[idx], y = b[idx];
        CudaAsm.Emit("min.f16 %0, %1, %2;", out ushort min, x, y);
        CudaAsm.Emit("max.f16 %0, %1, %2;", out ushort max, x, y);
        CudaAsm.Emit("min.ftz.f16 %0, %1, %2;", out ushort minFtz, x, y);
        CudaAsm.Emit("max.ftz.f16 %0, %1, %2;", out ushort maxFtz, x, y);
        int i = idx * 4;
        o[i] = min;
        o[i + 1] = max;
        o[i + 2] = minFtz;
        o[i + 3] = maxFtz;
    }

    [TestMethod]
    public async Task Tests_PTX_Float16_InlineInstructions_MatchExactOracle() =>
        await RunTest(async accelerator =>
    {
        var cuda = (CudaAccelerator)accelerator;
        int p = HostilePatterns.Length;
        int n = p * p;
        var a = new ushort[n];
        var b = new ushort[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = HostilePatterns[i / p];
            b[i] = HostilePatterns[i % p];
        }
        using var aBuf = accelerator.Allocate1D(a);
        using var bBuf = accelerator.Allocate1D(b);

        var errors = new List<string>();
        void Check(string op, int i, ushort expected, ushort actual)
        {
            bool ok = ExactHalfOracle.IsNaN(expected)
                ? ExactHalfOracle.IsNaN(actual)
                : actual == expected;
            if (!ok && errors.Count < 8)
                errors.Add($"{op}(0x{a[i]:X4}, 0x{b[i]:X4}): expected 0x{expected:X4}, " +
                    $"got 0x{actual:X4}");
        }

        using (var outBuf = accelerator.Allocate1D<ushort>(n * 6))
        {
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<ushort>, ArrayView<ushort>, ArrayView<ushort>>(
                HalfInlineArithmeticKernel);
            kernel((Index1D)n, aBuf.View, bBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var o = await outBuf.CopyToHostAsync<ushort>();
            for (int i = 0; i < n; i++)
            {
                Check("add", i, ExactHalfOracle.Add(a[i], b[i]), o[i * 6]);
                Check("sub", i, ExactHalfOracle.Sub(a[i], b[i]), o[i * 6 + 1]);
                Check("mul", i, ExactHalfOracle.Mul(a[i], b[i]), o[i * 6 + 2]);
                Check("add.ftz", i, ExactHalfOracle.Add(a[i], b[i], true), o[i * 6 + 3]);
                Check("sub.ftz", i, ExactHalfOracle.Sub(a[i], b[i], true), o[i * 6 + 4]);
                Check("mul.ftz", i, ExactHalfOracle.Mul(a[i], b[i], true), o[i * 6 + 5]);
            }
        }

        bool minMaxNative = cuda.Capabilities.Float16Native
            && cuda.Architecture < CudaArchitecture.SM_80;
        if (!minMaxNative)
        {
            using var outBuf = accelerator.Allocate1D<ushort>(n * 4);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<ushort>, ArrayView<ushort>, ArrayView<ushort>>(
                HalfInlineMinMaxKernel);
            kernel((Index1D)n, aBuf.View, bBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var o = await outBuf.CopyToHostAsync<ushort>();
            for (int i = 0; i < n; i++)
            {
                Check("min", i, ExactHalfOracle.Min(a[i], b[i]), o[i * 4]);
                Check("max", i, ExactHalfOracle.Max(a[i], b[i]), o[i * 4 + 1]);
                Check("min.ftz", i, ExactHalfOracle.Min(a[i], b[i], true), o[i * 4 + 2]);
                Check("max.ftz", i, ExactHalfOracle.Max(a[i], b[i], true), o[i * 4 + 3]);
            }
        }
        if (errors.Count > 0)
            throw new Exception("Inline f16 instructions differ from the exact oracle: " +
                string.Join("; ", errors));
    });
    static void DoubleToHalfKernel(
        Index1D idx, ArrayView<double> source, ArrayView<global::ILGPU.Half> target) =>
        target[idx] = (global::ILGPU.Half)source[idx];

    // cvt.rn.f16.f64 rounds twice on sm_50 hardware (through f32); the rewrite must not use it.
    [TestMethod]
    public async Task Tests_PTX_Float16_DoubleToHalf_RoundsOnce() =>
        await RunTest(async accelerator =>
    {
        double ulp11 = Math.Pow(2, -11);
        var values = new List<double>
        {
            1 + ulp11 + Math.Pow(2, -40),            // just above the tie: up
            1 + 2 * ulp11 + ulp11 - Math.Pow(2, -40), // just below the tie: down
            1 + ulp11,                                // exact tie: even (down)
            1 + 2 * ulp11 + ulp11,                    // exact tie: even (up)
            Math.Pow(2, -25) + Math.Pow(2, -60),      // above half the smallest subnormal
            Math.Pow(2, -25),                         // exact tie to zero
            Math.Pow(2, -25) - Math.Pow(2, -60),
            65520.0, 65519.99999999999, 65504.0, 1e-30, 1e30,
            0.0, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity,
        };
        int count = values.Count;
        for (int i = 0; i < count; i++)
            values.Add(-values[i]);
        var source = values.ToArray();
        using var sourceBuf = accelerator.Allocate1D(source);
        using var targetBuf = accelerator.Allocate1D<global::ILGPU.Half>(source.Length);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<double>, ArrayView<global::ILGPU.Half>>(DoubleToHalfKernel);
        kernel((Index1D)source.Length, sourceBuf.View, targetBuf.View);
        await accelerator.SynchronizeAsync();
        var target = await targetBuf.CopyToHostAsync<global::ILGPU.Half>();
        var errors = new List<string>();
        for (int i = 0; i < source.Length; i++)
        {
            ushort expected = ExactHalfOracle.FromDouble(source[i]);
            ushort actual = target[i].RawValue;
            bool ok = ExactHalfOracle.IsNaN(expected)
                ? ExactHalfOracle.IsNaN(actual)
                : actual == expected;
            if (!ok && errors.Count < 8)
                errors.Add($"({source[i]:R}): expected 0x{expected:X4}, got 0x{actual:X4}");
        }
        if (errors.Count > 0)
            throw new Exception("double -> Half is not a single rounding: " +
                string.Join("; ", errors));
    });

    // Any f16 instruction the rewrite does not know must be rejected at compile time with a
    // message naming it, never left for ptxas to fail with "requires .target sm_53".
    [TestMethod]
    public Task Tests_PTX_Float16_Emulation_RejectsUnemulatedInstructions()
    {
        var emulation = typeof(Context).Assembly.GetType(
            "ILGPU.Backends.PTX.PTXFloat16Emulation", throwOnError: true)!;
        var rewrite = emulation.GetMethod(
            "Rewrite",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        string Rewrite(string ptx) =>
            (string)rewrite.Invoke(null, new object[] { ptx, CudaArchitecture.SM_50 })!;

        foreach (var op in new[]
        {
            "atom.global.add.noftz.f16\t%rs1, [%rd1], %rs2;",
            "tanh.approx.f16\t%rs1, %rs2;",
            "add.f16x2\t%r1, %r2, %r3;",
            "fma.rn.ftz.f16\t%rs1, %rs2, %rs3, %rs4;",
        })
        {
            try
            {
                Rewrite("\t" + op + "\n");
            }
            catch (System.Reflection.TargetInvocationException ex)
                when (ex.InnerException is CapabilityNotSupportedException inner)
            {
                string name = op.Split('\t')[0];
                if (!inner.Message.Contains(name) || !inner.Message.Contains("SM_50"))
                    throw new Exception($"Message does not name '{name}' and SM_50: {inner.Message}");
                continue;
            }
            throw new Exception($"'{op}' was not rejected by the f16 rewrite");
        }

        // What it does know must leave no native f16 arithmetic behind.
        string rewritten = Rewrite(
            "\tadd.ftz.f16\t%rs1, %rs2, %rs3;\n\tfma.rn.f16\t%rs1, %rs2, %rs3, 15360;\n" +
            "\t@%p1 setp.lt.f16\t%p2, %rs1, %rs2;\n\tex2.approx.f16\t%rs1, %rs2;\n");
        foreach (var line in rewritten.Split('\n'))
        {
            string t = System.Text.RegularExpressions.Regex.Replace(
                line.Trim(), @"^@!?%\w+\s+", string.Empty);
            bool nativeCvt = t.StartsWith("cvt.f32.f16") || t.StartsWith("cvt.f64.f16")
                || t.StartsWith("cvt.rn.f16.f32") || t.StartsWith("cvt.rn.f16.f64");
            if (t.Contains(".f16") && !nativeCvt)
                throw new Exception($"Native f16 instruction left after the rewrite: {t}");
        }
        return Task.CompletedTask;
    }

    // PTX vector memory intrinsics (Discussion #5 / PR #4 additions)
    // ld.v2.f32 vectorized load + st.v2.f32 vectorized store
    static void PTXVecF32x2CopyKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        int i = idx * 2;
        Float2 v = PTXMemory.LoadF32x2(ref src[i]);
        PTXMemory.StoreF32x2(ref dst[i], v);
    }

    [TestMethod]
    public async Task Tests_PTXVectorMemory_F32x2_LoadStore() =>
        await RunTest(async accelerator =>
    {
        const int N = 64; // must be multiple of 2
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 1.5f + 0.1f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(PTXVecF32x2CopyKernel);
        kernel((Index1D)(N / 2), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"PTXVecF32x2: index {i}: expected {input[i]}, got {output[i]}");
    });

    // ld.v4.f32 vectorized load + st.v4.f32 vectorized store
    static void PTXVecF32x4CopyKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        int i = idx * 4;
        Float4 v = PTXMemory.LoadF32x4(ref src[i]);
        PTXMemory.StoreF32x4(ref dst[i], v);
    }

    [TestMethod]
    public async Task Tests_PTXVectorMemory_F32x4_LoadStore() =>
        await RunTest(async accelerator =>
    {
        const int N = 64; // must be multiple of 4
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 2.0f - 5.5f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(PTXVecF32x4CopyKernel);
        kernel((Index1D)(N / 4), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"PTXVecF32x4: index {i}: expected {input[i]}, got {output[i]}");
    });

    // st.v2.f32 via scalar (x, y) form - each thread reads two scalars then stores vectorized
    static void PTXVecF32x2StoreScalarsKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        int i = idx * 2;
        PTXMemory.StoreF32x2(ref dst[i], src[i], src[i + 1]);
    }

    [TestMethod]
    public async Task Tests_PTXVectorMemory_F32x2_StoreScalars() =>
        await RunTest(async accelerator =>
    {
        const int N = 64;
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 0.75f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(PTXVecF32x2StoreScalarsKernel);
        kernel((Index1D)(N / 2), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"PTXVecF32x2Scalars: index {i}: expected {input[i]}, got {output[i]}");
    });

    // st.v4.f32 via scalar (x, y, z, w) form
    static void PTXVecF32x4StoreScalarsKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        int i = idx * 4;
        PTXMemory.StoreF32x4(ref dst[i], src[i], src[i + 1], src[i + 2], src[i + 3]);
    }

    [TestMethod]
    public async Task Tests_PTXVectorMemory_F32x4_StoreScalars() =>
        await RunTest(async accelerator =>
    {
        const int N = 64;
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 1.25f - 10.0f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(PTXVecF32x4StoreScalarsKernel);
        kernel((Index1D)(N / 4), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"PTXVecF32x4Scalars: index {i}: expected {input[i]}, got {output[i]}");
    });

    // ArrayView LoadVectorized / StoreVectorized using Float2 (vectorized copy via aligned cast)
    static void VectorizedFloat2CopyKernel(
        Index1D idx,
        ArrayView<float> src,
        ArrayView<float> dst)
    {
        // LoadVectorized loads a Float2 at element-index (idx*2) with 8-byte alignment
        var v = src.LoadVectorized<float, Float2>(idx * 2, 8);
        dst.StoreVectorized<float, Float2>(idx * 2, v, 8);
    }

    [TestMethod]
    public async Task Tests_ArrayView_LoadVectorized_Float2() =>
        await RunTest(async accelerator =>
    {
        const int N = 64;
        var input = new float[N];
        for (int i = 0; i < N; i++) input[i] = i * 3.0f + 1.0f;
        using var srcBuf = accelerator.Allocate1D<float>(N);
        using var dstBuf = accelerator.Allocate1D<float>(N);
        srcBuf.CopyFromCPU(input);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>>(VectorizedFloat2CopyKernel);
        kernel((Index1D)(N / 2), srcBuf.View, dstBuf.View);
        await accelerator.SynchronizeAsync();
        var output = await dstBuf.CopyToHostAsync<float>();
        for (int i = 0; i < N; i++)
            if (output[i] != input[i])
                throw new Exception($"LoadVectorizedFloat2: index {i}: expected {input[i]}, got {output[i]}");
    });

    /// <summary>Test holder of idle device memory: gives buffers back newest-first when asked.</summary>
    sealed class IdleMemoryHolder : IDeviceMemoryReclaimer
    {
        public readonly List<MemoryBuffer1D<byte, Stride1D.Dense>> Idle = new();
        public long Reclaimed;
        public long LastUsedTimestamp { get; set; }
        public long ReclaimableBytes { get { long n = 0; foreach (var b in Idle) n += b.LengthInBytes; return n; } }
        public long Reclaim(long bytesWanted)
        {
            long freed = 0;
            while (freed < bytesWanted && Idle.Count > 0)
            {
                var b = Idle[^1];
                Idle.RemoveAt(Idle.Count - 1);
                freed += b.LengthInBytes;
                b.Dispose();
            }
            Reclaimed += freed;
            return freed;
        }
        public void DisposeAll() { foreach (var b in Idle) b.Dispose(); Idle.Clear(); }
    }

    /// <summary>
    /// An allocation that would cut into the headroom makes a registered holder give back idle memory BEFORE the
    /// allocation, so the device never runs out. A driver with CUDA sysmem fallback (Windows' default) never fails
    /// the allocation - it spills it into system RAM - so reclaiming only after a failure never ran there
    /// (Vertex 2026-10-12: 7.3 GB idle in buffer pools, 1.4 GB spilled, replies took minutes). Red-check: without
    /// the pre-allocation check the three new chunks spill and free memory ends below the headroom.
    /// </summary>
    [TestMethod]
    public async Task Tests_MemoryPressure_ReclaimsIdleMemoryBeforeAllocating() =>
        await RunTest(async accelerator =>
    {
        if (!accelerator.TryGetFreeMemory(out long free))
            throw new UnsupportedTestException("backend cannot report free device memory");
        const long Chunk = 256L * 1024 * 1024;
        const long Headroom = 512L * 1024 * 1024;
        accelerator.DeviceMemoryHeadroomBytes = Headroom;
        var holder = new IdleMemoryHolder();
        var fresh = new List<MemoryBuffer1D<byte, Stride1D.Dense>>();
        try
        {
            // Fill the device with idle memory until only headroom + 2 chunks are free (nothing registered yet,
            // so this filling does not reclaim anything).
            while (free > Headroom + 2 * Chunk)
            {
                holder.Idle.Add(accelerator.Allocate1D<byte>(Chunk));
                accelerator.TryGetFreeMemory(out free);
            }
            if (holder.Idle.Count < 4)
                throw new UnsupportedTestException($"too little free device memory to test ({free / 1048576} MiB)");
            accelerator.RegisterReclaimer(holder);

            // Three more chunks do not fit above the headroom: the holder must make room first.
            for (int i = 0; i < 3; i++)
                fresh.Add(accelerator.Allocate1D<byte>(Chunk));
            foreach (var b in fresh)
                b.MemSetToZero();
            await accelerator.SynchronizeAsync();
            accelerator.TryGetFreeMemory(out long after);

            if (holder.Reclaimed < Chunk)
                throw new Exception($"holder gave back {holder.Reclaimed / 1048576} MiB; expected at least one {Chunk / 1048576} MiB chunk");
            // Allow half a chunk of slack for the driver's own bookkeeping; a spill leaves far less than this.
            if (after < Headroom - Chunk / 2)
                throw new Exception($"free device memory fell to {after / 1048576} MiB, below the {Headroom / 1048576} MiB headroom: the allocations spilled");
        }
        finally
        {
            accelerator.UnregisterReclaimer(holder);
            accelerator.DeviceMemoryHeadroomBytes = null;
            foreach (var b in fresh) b.Dispose();
            holder.DisposeAll();
        }
    });

    /// <summary>The least recently used holder gives memory back first; the active one keeps its buffers.</summary>
    [TestMethod]
    public async Task Tests_MemoryPressure_LeastRecentlyUsedHolderGivesBackFirst() =>
        await RunTest(async accelerator =>
    {
        const long Chunk = 16L * 1024 * 1024;
        var idle = new IdleMemoryHolder { LastUsedTimestamp = 100 };
        var active = new IdleMemoryHolder { LastUsedTimestamp = 200 };
        try
        {
            for (int i = 0; i < 2; i++)
            {
                idle.Idle.Add(accelerator.Allocate1D<byte>(Chunk));
                active.Idle.Add(accelerator.Allocate1D<byte>(Chunk));
            }
            // Registered active-first so list order cannot pass for LRU order.
            accelerator.RegisterReclaimer(active);
            accelerator.RegisterReclaimer(idle);
            long freed = accelerator.ReclaimDeviceMemory(Chunk + Chunk / 2);
            if (freed != 2 * Chunk || idle.Reclaimed != 2 * Chunk || active.Reclaimed != 0)
                throw new Exception($"freed {freed}, least recent gave {idle.Reclaimed}, most recent gave {active.Reclaimed}; " +
                                    $"expected the least recent to give both its chunks and the most recent none");
            freed = accelerator.ReclaimDeviceMemory(Chunk);
            if (freed != Chunk || active.Reclaimed != Chunk)
                throw new Exception($"second reclaim freed {freed}; the most recent holder should give one chunk once the other is empty");
        }
        finally
        {
            accelerator.UnregisterReclaimer(idle);
            accelerator.UnregisterReclaimer(active);
            idle.DisposeAll();
            active.DisposeAll();
        }
        await Task.CompletedTask;
    });
}
