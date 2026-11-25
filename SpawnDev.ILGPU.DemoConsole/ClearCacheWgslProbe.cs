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
        // SCAN_NO_FEATURES=1: an adapter without shader-f16 / subgroups (emulated f16), e.g. to compile the WGSL with Node Dawn
        var features = Environment.GetEnvironmentVariable("SCAN_NO_FEATURES") == "1"
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal) { "shader-f16", "subgroups" };
        var backend = new WebGPUBackend(ctx, WebGPUBackendOptions.Default, features);
        return (ctx, backend);
    }

    static string Wgsl(WebGPUBackend backend, Delegate kernel) =>
        ((WebGPUCompiledKernel)backend.Compile(EntryPointDescription.FromImplicitlyGroupedKernel(kernel.Method), KernelSpecialization.Empty)).WGSLSource;

    // `clearcache-wgsl --all`: DETERMINISM scan of every suite kernel. One context + backend (as in a page): every kernel
    // compiled once, then every kernel compiled again (the IR node ids have moved on by then), RAW WGSL compared. Different
    // text for the same kernel = a compiled-shader cache miss and a duplicate pipeline (ShaderCanonicalNames, 5.4.46).
    // The backends the scan can drive offline: WGSL text, GLSL text, or the Wasm module bytes (the Wasm worker module
    // cache and the WebGL program cache key on them the way the WebGPU shader cache keys on the WGSL).
    static (IDisposable Context, global::ILGPU.Backends.Backend Backend) NewScanBackend(string which)
    {
        if (which == "webgpu") return NewBackend();
        var ctx = Context.Create(b => b.Default().EnableAlgorithms());
        global::ILGPU.Backends.Backend backend = which switch
        {
            "wasm" => new SpawnDev.ILGPU.Wasm.Backend.WasmBackend(ctx, new SpawnDev.ILGPU.Wasm.Backend.WasmBackendOptions()),
            "webgl" => new SpawnDev.ILGPU.WebGL.Backend.WebGLBackend(ctx, new SpawnDev.ILGPU.WebGL.Backend.WebGLBackendOptions()),
            _ => throw new ArgumentException("backend: webgpu | wasm | webgl"),
        };
        return (ctx, backend);
    }

    static string ScanOutput(string which, global::ILGPU.Backends.Backend backend, EntryPointDescription entry, KernelSpecialization spec)
    {
        if (which == "wasm")
        {
            SpawnDev.ILGPU.Wasm.Backend.WasmBackend.LastWasmBinary = null;
            backend.Compile(entry, spec);
            return Convert.ToHexString(SpawnDev.ILGPU.Wasm.Backend.WasmBackend.LastWasmBinary ?? Array.Empty<byte>());
        }
        var compiled = backend.Compile(entry, spec);
        return which == "webgl"
            ? ((SpawnDev.ILGPU.WebGL.Backend.WebGLCompiledKernel)compiled).GLSLSource
            : ((WebGPUCompiledKernel)compiled).WGSLSource;
    }

    static int RunAll(string which)
    {
        // SCAN_ASM=<dll>[;<dll>...]: the kernels of these assemblies instead of the suite (as in --unreachable)
        var scanAsm = Environment.GetEnvironmentVariable("SCAN_ASM");
        var asms = string.IsNullOrEmpty(scanAsm)
            ? new[] { typeof(SpawnDev.ILGPU.Demo.Shared.UnitTests.BackendTestBase).Assembly }
            : scanAsm.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(System.Reflection.Assembly.LoadFrom).ToArray();
        var spec = new KernelSpecialization(64, null);
        var kernels = new List<(string Name, System.Reflection.MethodInfo M)>();
        var seen = new HashSet<string>();
        foreach (var type in asms.SelectMany(a => { try { return a.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).Select(t => t!).ToArray(); } }))
        {
            if (type.ContainsGenericParameters) continue;
            foreach (var m in type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
            {
                System.Reflection.ParameterInfo[] ps;
                try
                {
                    if (m.IsGenericMethodDefinition || m.ReturnType != typeof(void) || m.GetMethodBody() == null) continue;
                    ps = m.GetParameters();
                }
                catch { continue; }   // a signature that needs an assembly this process does not have
                if (ps.Length == 0) continue;
                bool ok = true;
                foreach (var p in ps) if (!p.ParameterType.IsValueType || p.ParameterType.IsByRef) { ok = false; break; }
                if (!ok) continue;
                string name = $"{type.Name}.{m.Name}";
                if (!seen.Add(name)) name += "_" + m.MetadataToken.ToString("x");
                kernels.Add((name, m));
            }
        }
        // SCAN_TIME=1: compile every kernel once in one context, nothing else (compile time and the IR the context keeps;
        // a warm-up context first, so the compiler itself is jitted)
        if (Environment.GetEnvironmentVariable("SCAN_TIME") == "1")
        {
            string? CompileTimed(global::ILGPU.Backends.Backend be, System.Reflection.MethodInfo m)
            {
                try
                {
                    EntryPointDescription entry;
                    try { entry = EntryPointDescription.FromImplicitlyGroupedKernel(m); }
                    catch (NotSupportedException) { entry = EntryPointDescription.FromExplicitlyGroupedKernel(m); }
                    return ScanOutput(which, be, entry, spec);
                }
                catch (Exception ex)
                {
                    // SCAN_ERR=1: the kernels that do not compile in this context, with the innermost exception
                    if (Environment.GetEnvironmentVariable("SCAN_ERR") == "1")
                    {
                        var inner = ex;
                        while (inner.InnerException != null) inner = inner.InnerException;
                        var at = (inner.StackTrace ?? "").Split(Environment.NewLine).FirstOrDefault(l => l.Contains("ILGPU.IR") || l.Contains("Backend"))?.Trim();
                        if (Environment.GetEnvironmentVariable("SCAN_FULL") == m.Name) Console.WriteLine(ex.ToString()); Console.WriteLine($"FAIL {m.DeclaringType?.Name}.{m.Name}: {inner.GetType().Name}: {inner.Message.Split(Environment.NewLine)[0]} | {at}");
                    }
                    return null;
                }
            }
            int limit = int.TryParse(Environment.GetEnvironmentVariable("SCAN_LIMIT"), out var lim) ? lim : kernels.Count;
            // SCAN_OK=<file>: only kernels that compile in a context of their own (an unsupported one can fail inside the
            // optimizer and leave its methods untransformed in a shared context, which then fails every later kernel).
            // The file is written on the first run (one fresh context per kernel).
            var okFile = Environment.GetEnvironmentVariable("SCAN_OK");
            HashSet<string>? okNames = null;
            if (okFile != null)
            {
                if (!File.Exists(okFile))
                {
                    var okList = new List<string>();
                    foreach (var (name, m) in kernels)
                    {
                        var (c0, b0) = NewScanBackend(which);
                        using (c0) using (b0) if (CompileTimed(b0, m) != null) okList.Add(name);
                    }
                    File.WriteAllLines(okFile, okList);
                }
                okNames = new HashSet<string>(File.ReadAllLines(okFile));
            }
            var list = kernels.Where(k => okNames == null || okNames.Contains(k.Name)).Take(limit).ToList();
            var (wc, wb) = NewScanBackend(which);
            using (wc) using (wb) foreach (var (_, m) in list.Take(60)) CompileTimed(wb, m);
            GC.Collect(); GC.WaitForPendingFinalizers();
            var (tc, tb) = NewScanBackend(which);
            using (tc) using (tb)
            {
                long chars = 0; int ok = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var (_, m) in list) { var w = CompileTimed(tb, m); if (w != null) { ok++; chars += w.Length; } }
                sw.Stop();
                long heapBefore = GC.GetTotalMemory(true);
                tb.Context.ClearCache(ClearCacheMode.Default);
                long heapAfter = GC.GetTotalMemory(true);
                Console.WriteLine($"TIME ({which}): {ok} of {list.Count} kernels in one context: {sw.Elapsed.TotalMilliseconds:F0} ms, {chars:N0} chars, IR kept {(heapBefore - heapAfter) / 1048576.0:F1} MiB");
            }
            return 0;
        }
        // pass 1: each kernel in its OWN fresh context (its text when nothing was compiled before it);
        // pass 2: all kernels once each, in REVERSE order, in ONE context (each with a different compile history).
        // Each kernel is compiled once per context: a kernel that fails part-way (an unsupported feature) can leave a
        // context in which a RE-compile of another kernel crashes in IfConversion (seen 2026-11-23; tracked separately).
        string? Compile(global::ILGPU.Backends.Backend backend, System.Reflection.MethodInfo m)
        {
            try
            {
                EntryPointDescription entry;
                try { entry = EntryPointDescription.FromImplicitlyGroupedKernel(m); }
                catch (NotSupportedException) { entry = EntryPointDescription.FromExplicitlyGroupedKernel(m); }
                return ScanOutput(which, backend, entry, spec);
            }
            catch { return null; }
        }
        var first = new Dictionary<string, string>();
        var swFresh = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (name, m) in kernels)
        {
            var (c, b) = NewScanBackend(which);
            using (c) using (b) { var w = Compile(b, m); if (w != null) first[name] = w; }
        }
        double freshMs = swFresh.Elapsed.TotalMilliseconds;
        var swShared = System.Diagnostics.Stopwatch.StartNew();
        int same = 0, differ = 0, shown = 0;
        var (ctx, backend) = NewScanBackend(which);
        using (ctx) using (backend)
        {
            for (int k = kernels.Count - 1; k >= 0; k--)
            {
                var (name, m) = kernels[k];
                if (!first.TryGetValue(name, out var w1)) continue;
                var w2 = Compile(backend, m);
                // SCAN_CLEAR=1: the context forgets its IR after every compile (what each kernel costs then, next to sharing)
                if (Environment.GetEnvironmentVariable("SCAN_CLEAR") == "1") backend.Context.ClearCache(ClearCacheMode.Default);
                if (w2 == null) continue;
                if (w1 == w2) { same++; continue; }
                differ++;
                var dumpDir = Environment.GetEnvironmentVariable("SCAN_DUMP");
                if (dumpDir != null) { Directory.CreateDirectory(dumpDir); File.WriteAllText(Path.Combine(dumpDir, name + ".fresh.txt"), w1); File.WriteAllText(Path.Combine(dumpDir, name + ".shared.txt"), w2); }
                if (shown++ < 5)
                {
                    int d = 0;
                    while (d < Math.Min(w1.Length, w2.Length) && w1[d] == w2[d]) d++;
                    int from = Math.Max(0, d - 60);
                    Console.WriteLine($"  DIFFERS {name} at {d}:");
                    Console.WriteLine("    fresh : " + w1.Substring(from, Math.Min(160, w1.Length - from)).Replace('\n', '|'));
                    Console.WriteLine("    shared: " + w2.Substring(from, Math.Min(160, w2.Length - from)).Replace('\n', '|'));
                }
            }
            swShared.Stop();
            // the managed memory the context itself holds (its IR: the methods of every kernel, and their inline copies):
            // the drop when its IR caches are cleared
            long before = GC.GetTotalMemory(true);
            backend.Context.ClearCache(ClearCacheMode.Default);
            long after = GC.GetTotalMemory(true);
            Console.WriteLine($"HEAP: the shared context's IR of all these kernels: {(before - after) / 1048576.0:F1} MiB");
        }
        Console.WriteLine($"TIMING: fresh-context pass {freshMs:F0} ms, shared-context pass {swShared.Elapsed.TotalMilliseconds:F0} ms");
        Console.WriteLine($"RESULT ({which}): {first.Count} kernels; in a shared context after other kernels: {same} byte-identical, {differ} different");
        return differ;
    }

    public static int Run(string[] args)
    {
        bool savedCache = ShaderArtifactCache.Enabled;
        if (args.Length > 1 && args[1] == "--unreachable")
        {
            // `clearcache-wgsl --unreachable`: every suite kernel's WGSL scanned for a statement right after an unconditional
            // exit (return / break / continue) in the same block. Chrome (Tint) logs "warning: code is unreachable" for it
            // once per shader module (Lore's RX 590 console, TRELLIS.2, 2026-11-24).
            ShaderArtifactCache.Enabled = false;
            try
            {
                // SCAN_ASM=<dll>[;<dll>...]: scan the kernels of these assemblies instead of the suite (e.g. ILGPU.ML)
                var scanAsm = Environment.GetEnvironmentVariable("SCAN_ASM");
                var asms = string.IsNullOrEmpty(scanAsm)
                    ? new[] { typeof(SpawnDev.ILGPU.Demo.Shared.UnitTests.BackendTestBase).Assembly }
                    : scanAsm.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(System.Reflection.Assembly.LoadFrom).ToArray();
                var spec = new KernelSpecialization(64, null);
                int kernels = 0, hits = 0, shown = 0;
                var exit = new Regex(@"^\s*(return\b[^;]*;|break;|continue;)\s*(//.*)?$");
                var shapes = new Dictionary<string, int>();
                foreach (var type in asms.SelectMany(a => { try { return a.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).Select(t => t!).ToArray(); } }))
                {
                    if (type.ContainsGenericParameters) continue;
                    foreach (var m in type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        System.Reflection.ParameterInfo[] ps;
                        try
                        {
                            if (m.IsGenericMethodDefinition || m.ReturnType != typeof(void) || m.GetMethodBody() == null) continue;
                            ps = m.GetParameters();
                        }
                        catch { continue; }   // a signature that needs an assembly this process does not have
                        if (ps.Length == 0) continue;
                        bool ok = true;
                        foreach (var p in ps) if (!p.ParameterType.IsValueType || p.ParameterType.IsByRef) { ok = false; break; }
                        if (!ok) continue;
                        string? wgsl = null;
                        var (c, b) = NewBackend();
                        using (c) using (b)
                        {
                            try
                            {
                                EntryPointDescription entry;
                                try { entry = EntryPointDescription.FromImplicitlyGroupedKernel(m); }
                                catch (NotSupportedException) { entry = EntryPointDescription.FromExplicitlyGroupedKernel(m); }
                                wgsl = ((WebGPUCompiledKernel)b.Compile(entry, spec)).WGSLSource;
                            }
                            catch (Exception ex) when (ex.ToString().Contains("placeholder")) { Console.WriteLine($"PLACEHOLDER {type.Name}.{m.Name}: {ex.GetBaseException().Message}"); }
                            catch { }
                        }
                        if (wgsl == null) continue;
                        kernels++;
                        // SCAN_DUMP=<dir>: every kernel's WGSL to a file (for a Tint pass: Dawn's getCompilationInfo)
                        var dumpDir = Environment.GetEnvironmentVariable("SCAN_DUMP");
                        if (dumpDir != null)
                        {
                            Directory.CreateDirectory(dumpDir);
                            File.WriteAllText(Path.Combine(dumpDir, $"{type.Name}.{m.Name}_{m.MetadataToken:x}.wgsl"), wgsl);
                        }
                        var lines = wgsl.Split('\n');
                        bool kernelHit = false;
                        for (int i = 0; i + 1 < lines.Length; i++)
                        {
                            if (!exit.IsMatch(lines[i])) continue;
                            // the next non-blank, non-comment line: unreachable unless it closes the block or starts a case
                            int j = i + 1;
                            while (j < lines.Length && (lines[j].Trim().Length == 0 || lines[j].Trim().StartsWith("//"))) j++;
                            if (j >= lines.Length) break;
                            var next = lines[j].Trim();
                            if (next.StartsWith("}") || next.StartsWith("case ") || next.StartsWith("default")) continue;
                            hits++;
                            kernelHit = true;
                            var shape = Regex.Replace(lines[i].Trim(), @"\b[A-Za-z_]\w*_\d+\b", "v") + " => " + Regex.Replace(next, @"\b[A-Za-z_]\w*_\d+\b", "v");
                            if (shape.Length > 120) shape = shape.Substring(0, 120);
                            shapes[shape] = shapes.TryGetValue(shape, out var n) ? n + 1 : 1;
                            if (shown < 4)
                            {
                                shown++;
                                Console.WriteLine($"  {type.Name}.{m.Name} line {i + 1}:");
                                for (int k = Math.Max(0, i - 4); k <= Math.Min(lines.Length - 1, j + 1); k++) Console.WriteLine("    | " + lines[k]);
                            }
                        }
                        if (kernelHit) Console.WriteLine($"HIT {type.Name}.{m.Name}");
                    }
                }
                foreach (var (s, n) in shapes.OrderByDescending(x => x.Value)) Console.WriteLine($"  {n} x {s}");
                Console.WriteLine($"UNREACHABLE: {kernels} kernels, {hits} statements after an unconditional exit");
                return hits;
            }
            finally { ShaderArtifactCache.Enabled = savedCache; }
        }
        if (args.Length > 1 && args[1] == "--recompile")
        {
            // every suite kernel compiled once in ONE context, then again: a second compile of the same kernel in the same
            // context crashed in IfConversion (NullReferenceException, seen 2026-11-23)
            ShaderArtifactCache.Enabled = false;
            try
            {
                var asm = typeof(SpawnDev.ILGPU.Demo.Shared.UnitTests.BackendTestBase).Assembly;
                var spec = new KernelSpecialization(64, null);
                var (ctx, backend) = NewBackend();
                using (ctx) using (backend)
                {
                    var list = new List<System.Reflection.MethodInfo>();
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.ContainsGenericParameters) continue;
                        foreach (var m in type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                        {
                            if (m.IsGenericMethodDefinition || m.ReturnType != typeof(void) || m.GetMethodBody() == null) continue;
                            var ps = m.GetParameters();
                            if (ps.Length == 0) continue;
                            bool ok = true;
                            foreach (var p in ps) if (!p.ParameterType.IsValueType || p.ParameterType.IsByRef) { ok = false; break; }
                            if (ok) list.Add(m);
                        }
                    }
                    int pass1Fail = 0, pass2Fail = 0;
                    var firstOk = new HashSet<System.Reflection.MethodInfo>();
                    var crashes = new Dictionary<string, int>();
                    string? firstCrash = null, poisoner = null;
                    for (int pass = 1; pass <= 2; pass++)
                    {
                        foreach (var m in list)
                        {
                            try
                            {
                                EntryPointDescription entry;
                                try { entry = EntryPointDescription.FromImplicitlyGroupedKernel(m); }
                                catch (NotSupportedException) { entry = EntryPointDescription.FromExplicitlyGroupedKernel(m); }
                                backend.Compile(entry, spec);
                                if (pass == 1) firstOk.Add(m);
                            }
                            catch (Exception ex)
                            {
                                if (pass == 1)
                                {
                                    pass1Fail++;
                                    if (poisoner == null && firstOk.Count > 0)
                                    {
                                        // does a kernel that compiled before still compile after this failure?
                                        try
                                        {
                                            var probe = firstOk.First();
                                            EntryPointDescription pe;
                                            try { pe = EntryPointDescription.FromImplicitlyGroupedKernel(probe); }
                                            catch (NotSupportedException) { pe = EntryPointDescription.FromExplicitlyGroupedKernel(probe); }
                                            backend.Compile(pe, spec);
                                        }
                                        catch
                                        {
                                            var inner1 = ex is AggregateException ae1 ? ae1.InnerExceptions[0] : ex;
                                            poisoner = m.DeclaringType?.Name + "." + m.Name + ": " + inner1.GetType().Name + ": " + inner1.Message;
                                        }
                                    }
                                    continue;
                                }
                                if (!firstOk.Contains(m)) continue;
                                pass2Fail++;
                                var inner = ex is AggregateException ae ? ae.InnerExceptions[0] : ex;
                                var frame = inner.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "";
                                var key = inner.GetType().Name + " " + frame;
                                crashes[key] = crashes.TryGetValue(key, out var c) ? c + 1 : 1;
                                firstCrash ??= m.DeclaringType?.Name + "." + m.Name + ": " + inner + "\n--- outer ---\n" + ex.StackTrace;
                            }
                        }
                    }
                    Console.WriteLine($"RECOMPILE: {list.Count} kernels; pass 1 failures {pass1Fail} (unsupported features); compiled in pass 1 but failed in pass 2: {pass2Fail}");
                    foreach (var (k, n) in crashes) Console.WriteLine($"  {n} x {k}");
                    Console.WriteLine("POISONER (first failed compile after which a good kernel no longer compiles): " + (poisoner ?? "none"));
                    if (firstCrash != null) Console.WriteLine("first: " + string.Join("\n", firstCrash.Split('\n').Where(l => !l.Contains("System.Threading") && !l.Contains("End of stack")).Take(30)));
                    return pass2Fail;
                }
            }
            finally { ShaderArtifactCache.Enabled = savedCache; }
        }
        if (args.Length > 1 && args[1] == "--all")
        {
            ShaderArtifactCache.Enabled = false;
            try { return RunAll(args.Length > 2 ? args[2] : "webgpu"); }
            finally { ShaderArtifactCache.Enabled = savedCache; }
        }
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
                try { var again = Wgsl(backend, kernels[0].K); Console.WriteLine("  same-context recompile without a clear: " + (again.Length > 0 ? "ok" : "empty") + (Normalise(again) == aBefore ? ", same text" : ", DIFFERENT text")); }
                catch (Exception ex) { Console.WriteLine("  same-context recompile without a clear THROWS: " + (ex is AggregateException ae ? ae.InnerExceptions[0].GetType().Name + " " + ae.InnerExceptions[0].Message : ex.GetType().Name + " " + ex.Message)); }
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
