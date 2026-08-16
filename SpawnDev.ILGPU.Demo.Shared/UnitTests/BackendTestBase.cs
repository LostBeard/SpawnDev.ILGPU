using ILGPU;
using ILGPU.Runtime;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    /// <summary>
    /// Abstract base class containing all shared kernel tests.
    /// Each backend (WebGPU, Wasm, CPU) inherits and overrides CreateAcceleratorAsync().
    /// </summary>
    public abstract partial class BackendTestBase : IDisposable
    {
        /// <summary>Platform-appropriate crypto provider (injected via DI). All crypto goes through this.</summary>
        protected IPortableCrypto Crypto { get; }

        /// <summary>DI-configured WebTorrent client with tracker discovery.</summary>
        protected SpawnDev.WebTorrent.WebTorrentClient WebTorrentClient { get; }

        protected BackendTestBase(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient)
        {
            Crypto = crypto;
            WebTorrentClient = webTorrentClient;
        }

        /// <summary>Creates an Ed25519 signer with a fresh key pair from IPortableCrypto. Each call generates a new identity.</summary>
        protected async Task<SpawnDev.WebTorrent.Ed25519Signer> CreateEd25519Signer()
        {
            var signer = new SpawnDev.WebTorrent.Ed25519Signer(Crypto);
            await signer.GenerateKeyAsync();
            return signer;
        }

        /// <summary>Creates the backend-specific accelerator. Caller is responsible for disposing both.</summary>
        protected abstract Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync();

        /// <summary>Creates accelerator with 64-bit emulation enabled (for Long/Double tests). Default falls back to standard.</summary>
        protected virtual Task<(Context context, Accelerator accelerator)> CreateEmulatedAcceleratorAsync()
            => CreateAcceleratorAsync();

        /// <summary>Backend display name for error messages.</summary>
        protected abstract string BackendName { get; }

        // Cached accelerator — reused across all tests in this class instance
        private Context? _cachedContext;
        private Accelerator? _cachedAccelerator;
        private Context? _cachedEmulatedContext;
        private Accelerator? _cachedEmulatedAccelerator;

        /// <summary>
        /// Gets or creates the cached accelerator for this test class.
        /// Reuses a single context/accelerator across all tests to avoid
        /// resource exhaustion from rapid create/dispose cycles.
        /// </summary>
        private async Task<Accelerator> GetOrCreateAcceleratorAsync()
        {
            if (_cachedAccelerator == null)
            {
                var (context, accelerator) = await CreateAcceleratorAsync();
                _cachedContext = context;
                _cachedAccelerator = accelerator;
            }
            return _cachedAccelerator;
        }

        private async Task<Accelerator> GetOrCreateEmulatedAcceleratorAsync()
        {
            if (_cachedEmulatedAccelerator == null)
            {
                var (context, accelerator) = await CreateEmulatedAcceleratorAsync();
                _cachedEmulatedContext = context;
                _cachedEmulatedAccelerator = accelerator;
            }
            return _cachedEmulatedAccelerator;
        }

        // Helper to run a test body with cached accelerator.
        // On failure, invalidates the cached context so the next test
        // gets a fresh one (prevents cascading failures from GPU faults).
        protected async Task RunTest(Func<Accelerator, Task> testBody)
        {
            var accelerator = await GetOrCreateAcceleratorAsync();
            // DIAGNOSTIC (Tuvok 2026-05-29): per-pass radix counter localizer. No-op unless this
            // is the Wasm accelerator AND RadixCounterLocalizer.Enabled. Captures each radix pass's
            // Kernel1 bucket counts; on any test failure, appends a report pinpointing the FIRST
            // pass whose counter sum diverges (Kernel1 miscount) vs "counts OK -> scan/scatter bug".
            bool loc = RadixCounterLocalizer.Install(accelerator);
            try
            {
                await testBody(accelerator);
            }
            catch (UnsupportedTestException)
            {
                // A skip must pass through unchanged so the runner records a Skip, not a Fail.
                // Never wrap it with the localizer report (doing so turned WebGPU-only tests into
                // Wasm failures, since the localizer is installed on Wasm and wrapped the skip).
                throw;
            }
            catch (Exception ex)
            {
                string locReport = null;
                if (loc)
                {
                    try { (_, locReport) = await RadixCounterLocalizer.AnalyzeAsync(); }
                    catch (Exception aex) { locReport = "[RadixCounterLocalizer] analyze failed: " + aex.Message; }
                }
                // GPU fault may have corrupted the context — invalidate cache
                InvalidateCache();
                if (locReport != null)
                    throw new Exception(ex.Message + "\n\n" + locReport, ex);
                throw;
            }
            finally
            {
                if (loc) RadixCounterLocalizer.Uninstall();
            }
        }

        // Helper to run a test body that requires 64-bit emulation
        protected async Task RunEmulatedTest(Func<Accelerator, Task> testBody)
        {
            var accelerator = await GetOrCreateEmulatedAcceleratorAsync();
            try
            {
                await testBody(accelerator);
            }
            catch
            {
                InvalidateEmulatedCache();
                throw;
            }
        }

        private void InvalidateCache()
        {
            try { _cachedAccelerator?.Dispose(); }
            catch (Exception ex) { Console.WriteLine($"[Test] Accelerator disposal warning: {ex.Message}"); }
            _cachedAccelerator = null;
            try { _cachedContext?.Dispose(); }
            catch (Exception ex) { Console.WriteLine($"[Test] Context disposal warning: {ex.Message}"); }
            _cachedContext = null;
        }

        private void InvalidateEmulatedCache()
        {
            try { _cachedEmulatedAccelerator?.Dispose(); }
            catch (Exception ex) { Console.WriteLine($"[Test] Emulated accelerator disposal warning: {ex.Message}"); }
            _cachedEmulatedAccelerator = null;
            try { _cachedEmulatedContext?.Dispose(); }
            catch (Exception ex) { Console.WriteLine($"[Test] Emulated context disposal warning: {ex.Message}"); }
            _cachedEmulatedContext = null;
        }

        /// <summary>Disposes cached accelerator and context.</summary>
        public virtual void Dispose()
        {
            _cachedAccelerator?.Dispose();
            _cachedAccelerator = null;
            _cachedContext?.Dispose();
            _cachedContext = null;
            _cachedEmulatedAccelerator?.Dispose();
            _cachedEmulatedAccelerator = null;
            _cachedEmulatedContext?.Dispose();
            _cachedEmulatedContext = null;
        }

        /// <summary>
        /// Checks that the accelerator supports the specified feature.
        /// Override in backend-specific test classes to implement feature checks.
        /// </summary>
        protected virtual void RequireFeature(Accelerator accelerator, string featureName, string? reason = null)
        {
            // No-op by default. Backend-specific subclasses can override.
        }

        #region GPU Verification Helper

        /// <summary>
        /// Verify descending sort order and index integrity on GPU via GpuTestVerify.
        /// Throws on any violations.
        /// </summary>
        protected static async Task VerifyDescendingSortOnGpu(
            Accelerator accelerator,
            MemoryBuffer1D<int, Stride1D.Dense> keysBuf,
            MemoryBuffer1D<int, Stride1D.Dense> valuesBuf,
            int n,
            string testName,
            int[]? originalKeys = null,
            MemoryBuffer1D<int, Stride1D.Dense>? originalKeysBuf = null)
        {
            bool disposeOrigBuf = false;
            var origBuf = originalKeysBuf;
            if (origBuf == null && originalKeys != null)
            {
                origBuf = accelerator.Allocate1D(originalKeys);
                disposeOrigBuf = true;
            }

            try
            {
                // On WebGL (no atomics) GpuTestVerify.VerifyDescendingSort falls back to a CPU verify
                // internally (the sort itself still runs on the GPU). All other backends verify on GPU.
                var (orderViolations, duplicates, outOfRange, trackingErrors) =
                    await GpuTestVerify.VerifyDescendingSort(accelerator, keysBuf, valuesBuf, n, origBuf);

                if (orderViolations > 0)
                {
                    // Diagnostic: compare GPU output vs CPU reference
                    var diag = "";
                    try
                    {
                        var gpuKeys = await keysBuf.CopyToHostAsync();
                        // CPU reference sort (descending)
                        var cpuKeys = (int[])gpuKeys.Clone();
                        Array.Sort(cpuKeys);
                        Array.Reverse(cpuKeys); // descending

                        // Find first ROOT displacements (diff != -1, not cascade) + aggregate
                        // localization stats (Tuvok 2026-05-26): the residual Wasm sort race is
                        // rare (~14%/sweep) and can't be force-reproduced, so when it DOES fire we
                        // capture maximal context. localPos(i%256) histogram reveals thread-index
                        // clustering (localPos 0 = "first thread does scan" ILGroupExtensions:150;
                        // 255 = "last thread writes counters" RadixSortExtensions:728); diff buckets
                        // separate the ±1 residual from the large-magnitude dominant mode; span +
                        // distinct-group count shows whether errors cluster or scatter.
                        int mismatches = 0;
                        int shownRoot = 0;
                        int shownAny = 0;
                        var localPosHist = new int[256];
                        int diffPM1 = 0, diffSmall = 0, diffLarge = 0; // |diff|==1 / 2..16 / >16
                        int firstMm = -1, lastMm = -1;
                        var groupsHit = new System.Collections.Generic.HashSet<int>();
                        for (int i = 0; i < n; i++)
                        {
                            if (gpuKeys[i] != cpuKeys[i])
                            {
                                mismatches++;
                                int diff = gpuKeys[i] - cpuKeys[i];
                                int ad = diff < 0 ? -diff : diff;
                                if (ad == 1) diffPM1++; else if (ad <= 16) diffSmall++; else diffLarge++;
                                localPosHist[i % 256]++;
                                if (firstMm < 0) firstMm = i;
                                lastMm = i;
                                groupsHit.Add(i / 256);
                                // Show ROOT displacements (|diff| > 1) separately from cascade
                                if (ad > 1 && shownRoot < 10)
                                {
                                    diag += $"\n  ROOT[{i}]: gpu={gpuKeys[i]} cpu={cpuKeys[i]} (diff={diff}, group~{i/256}, localPos={i%256})";
                                    shownRoot++;
                                }
                                else if (shownAny < 5)
                                {
                                    diag += $"\n  [{i}]: gpu={gpuKeys[i]} cpu={cpuKeys[i]} (diff={diff}, group~{i/256})";
                                    shownAny++;
                                }
                            }
                        }
                        // Top-2 localPos buckets (for-loop, no LINQ in Blazor WASM)
                        int topLp1 = 0, topLp2 = 0;
                        for (int lp = 1; lp < 256; lp++)
                        {
                            if (localPosHist[lp] > localPosHist[topLp1]) { topLp2 = topLp1; topLp1 = lp; }
                            else if (localPosHist[lp] > localPosHist[topLp2]) topLp2 = lp;
                        }
                        diag = $"\n  Total mismatches vs CPU: {mismatches} (|diff|==1:{diffPM1}, 2..16:{diffSmall}, >16:{diffLarge})" +
                               $"\n  Span: first={firstMm} last={lastMm} distinct256Groups={groupsHit.Count}" +
                               $"\n  Top localPos(i%256): [{topLp1}]={localPosHist[topLp1]}, [{topLp2}]={localPosHist[topLp2]}" + diag;

                        // Also show order violations
                        int shownOrd = 0;
                        for (int i = 1; i < n && shownOrd < 5; i++)
                        {
                            if (gpuKeys[i] > gpuKeys[i - 1])
                            {
                                diag += $"\n  ORDER[{i}]: {gpuKeys[i-1]} -> {gpuKeys[i]} (group~{i/256})";
                                shownOrd++;
                            }
                        }
                    }
                    catch { diag = " (diagnostic readback failed)"; }
                    throw new Exception(
                        $"{testName}: Descending order violated {orderViolations} times out of {n} elements.{diag}");
                }
                if (outOfRange > 0 || duplicates > 0)
                {
                    var intDiag = "";
                    try
                    {
                        var gpuKeys = await keysBuf.CopyToHostAsync();
                        var cpuKeys = (int[])gpuKeys.Clone();
                        Array.Sort(cpuKeys);
                        Array.Reverse(cpuKeys);
                        int mismatches = 0;
                        int shown = 0;
                        for (int i = 0; i < n; i++)
                        {
                            if (gpuKeys[i] != cpuKeys[i])
                            {
                                mismatches++;
                                if (shown < 15)
                                {
                                    intDiag += $"\n  [{i}]: gpu={gpuKeys[i]} cpu={cpuKeys[i]} (diff={gpuKeys[i]-cpuKeys[i]}, group~{i/256})";
                                    shown++;
                                }
                            }
                        }
                        intDiag = $"\n  Total mismatches vs CPU: {mismatches}" + intDiag;
                    }
                    catch { intDiag = " (readback failed)"; }
                    throw new Exception(
                        $"{testName}: Index integrity failure — {duplicates} duplicates, {outOfRange} out-of-range out of {n} elements.{intDiag}");
                }
                if (trackingErrors > 0)
                    throw new Exception(
                        $"{testName}: Key-value tracking failed — {trackingErrors} mismatches out of {n} elements.");

                Console.WriteLine($"[GPU Verify] {testName}: {n} elements — order ✓, integrity ✓" +
                    (origBuf != null ? ", tracking ✓" : ""));
            }
            finally
            {
                if (disposeOrigBuf) origBuf?.Dispose();
            }
        }

        #endregion

        #region Structs

        public struct MyPoint
        {
            public float X;
            public float Y;
        }

        public struct InnerStruct
        {
            public float Val;
        }

        public struct OuterStruct
        {
            public InnerStruct Inner;
            public int ID;
        }

        public struct NestedInnerStruct
        {
            public int A;
            public int B;
        }

        public struct NestedOuterStruct
        {
            public NestedInnerStruct Inner;
            public float Value;
        }

        /// <summary>
        /// Simple struct for testing struct scalar kernel arguments.
        /// </summary>
        public struct ScalarStruct
        {
            public float X;
            public float Y;
        }

        #endregion

        #region Kernel Methods

        static void MyKernel(Index1D index, ArrayView<int> dataView, int constant)
        {
            dataView[index] = index + constant;
        }

        static void FloatKernel(Index1D index, ArrayView<float> dataView, float constant)
        {
            dataView[index] = index * 2.0f + constant;
        }

        static void MultiScalarKernel(Index1D index, ArrayView<int> dataView, int c1, int c2)
        {
            dataView[index] = index + c1 + c2;
        }

        static void Kernel2D(Index2D index, ArrayView2D<float, Stride2D.DenseX> dataView)
        {
            dataView[index] = index.X + index.Y * 100.0f;
        }

        static void Kernel3D(Index3D index, ArrayView3D<float, Stride3D.DenseXY> dataView)
        {
            dataView[index] = index.X + index.Y * 100.0f + index.Z * 1000.0f;
        }

        static void VectorAddKernel(Index1D index, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c)
        {
            c[index] = a[index] + b[index];
        }

        static void StructKernel(Index1D index, ArrayView<MyPoint> data)
        {
            var p = data[index];
            p.X += 1.0f;
            p.Y *= 2.0f;
            data[index] = p;
        }

        static void MathKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
        {
            float val = input[index];
            output[index] = MathF.Sin(val) + MathF.Cos(val) + MathF.Sqrt(MathF.Abs(val));
        }

        static void ControlFlowKernel(Index1D index, ArrayView<int> data)
        {
            int val = data[index];
            int ret = 0;
            if (val % 2 == 0)
            {
                for (int i = 0; i < 5; i++) ret += i;
            }
            else
            {
                ret = -1;
            }
            data[index] = ret;
        }

        static void AtomicKernel(Index1D index, ArrayView<int> data, ArrayView<Index1D> atomicData)
        {
            data[index] = index + 1;
            Atomic.Add(ref atomicData[0], (Index1D)(index + 1));
        }

        static void IntrinsicMathKernel(Index1D index, ArrayView<float> data)
        {
            if (index == 0) data[index] = MathF.Atan2(1.0f, 1.0f);
            else if (index == 1) data[index] = 2.0f * 3.0f + 4.0f; // FMA: a*b+c
            else if (index == 2) data[index] = 5.5f % 2.0f;
            else if (index == 5) data[index] = Math.Min(Math.Max(10.0f, 0.0f), 5.0f);
            else if (index == 7) data[index] = IntrinsicMathHelper(0.5f);
        }

        static float IntrinsicMathHelper(float val) { return val; }

        static void ConversionKernel(Index1D index, ArrayView<float> data)
        {
            float val = data[index];
            int intVal = (int)val;
            float floatVal = (float)intVal;
            data[index] = floatVal;
        }

        static void SharedMemoryKernel(Index1D index, ArrayView<int> data)
        {
            int tid = Group.IdxX;
            int gid = Grid.GlobalIndex.X;
            var shared = SharedMemory.Allocate<int>(64);
            shared[tid] = data[gid];
            Group.Barrier();
            int neighbor = (tid + 1) % 64;
            data[gid] = shared[neighbor];
        }

        static void NestedControlFlowKernel(Index1D index, ArrayView<int> data)
        {
            int sum = 0;
            for (int j = 0; j < 3; j++)
            {
                for (int k = 0; k < 3; k++) { sum += k; }
                if (j == 1) sum += 10;
            }
            data[index] = sum;
        }

        static int MyAdd(int a, int b) { return a + b; }

        static void FunctionCallKernel(Index1D index, ArrayView<int> data)
        {
            data[index] = MyAdd(index, 100);
        }

        static void CSharpSharedMemoryKernel(Index1D index, ArrayView<int> data)
        {
            int tid = Group.IdxX;
            int gid = Grid.GlobalIndex.X;
            var sharedMem = SharedMemory.Allocate<int>(64);
            sharedMem[tid] = data[gid];
            Group.Barrier();
            int reversedIndex = 63 - tid;
            int val = sharedMem[reversedIndex];
            data[gid] = val;
        }

        static void ComplexStructKernel(Index1D index, ArrayView<OuterStruct> data)
        {
            var item = data[index];
            item.Inner.Val += 1.0f;
            item.ID *= 2;
            data[index] = item;
        }

        static void AtomicCASKernel(Index1D index, ArrayView<int> data)
        {
            Atomic.CompareExchange(ref data[index], index, index + 100);
        }

        static void FMAKernel(Index1D index, ArrayView<float> data)
        {
            float a = (float)(int)index;
            float b = 2.0f;
            float c = 0.5f;
            data[index] = a * b + c; // Manual FMA: a*b+c
        }

        static void DynamicSharedKernel(Index1D index, ArrayView<int> data)
        {
            int tid = Group.IdxX;
            int gid = Grid.GlobalIndex.X;
            var shared = SharedMemory.GetDynamic<int>();
            shared[tid] = gid;
            Group.Barrier();
            int rev = 63 - tid;
            data[gid] = shared[rev];
        }

        protected static void DynamicSharedF64Kernel(Index1D index, ArrayView<double> data)
        {
            int tid = Group.IdxX;
            int gid = Grid.GlobalIndex.X;
            var shared = SharedMemory.GetDynamic<double>();
            shared[tid] = data[gid];
            Group.Barrier();
            int rev = 63 - tid;
            data[gid] = shared[rev];
        }

        static void IntMathKernel(Index1D index, ArrayView<int> input, ArrayView<int> output)
        {
            int val = input[index];
            if (index == 0 || index == 1) output[index] = Math.Abs(val);
            else if (index == 2) output[index] = Math.Min(val, 15);
            else if (index == 3) output[index] = Math.Max(val, 15);
            else if (index == 4) output[index] = Math.Min(Math.Max(val, 1), 5);
            else if (index == 5) output[index] = Math.Min(Math.Max(val, -200), -50);
            else output[index] = val;
        }

        static void MatrixMulKernel(Index2D index, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c, int size)
        {
            int row = index.Y; int col = index.X;
            if (row >= size || col >= size) return;
            float sum = 0.0f;
            for (int k = 0; k < size; k++) sum += a[row * size + k] * b[k * size + col];
            c[row * size + col] = sum;
        }

        static void SpecializedIntrinsicsKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
        {
            float val = input[index];
            if (index == 0) output[index] = global::ILGPU.Algorithms.XMath.Rsqrt(val);
            else if (index == 1 || index == 2) output[index] = MathF.Floor(val) + MathF.Ceiling(val);
            else if (index == 4) output[index] = global::ILGPU.Algorithms.XMath.Rcp(val);
            else output[index] = 0.0f;
        }

        static void BitManipulationKernel(Index1D index, ArrayView<int> input, ArrayView<int> output)
        {
            int val = input[index];
            if (index == 0) output[index] = System.Numerics.BitOperations.PopCount((uint)val);
            else if (index == 1) output[index] = System.Numerics.BitOperations.TrailingZeroCount(val);
            else if (index == 2) output[index] = System.Numerics.BitOperations.LeadingZeroCount((uint)val);
            else if (index == 3) output[index] = System.Numerics.BitOperations.PopCount((uint)val);
        }

        static void HistogramKernel(Index1D index, ArrayView<int> data, ArrayView<int> bins)
        {
            int binIdx = data[index];
            Atomic.Add(ref bins[binIdx], 1);
        }

        static void NestedLoopBreakKernel(Index1D index, ArrayView<int> output)
        {
            int acc = 0;
            for (int j = 0; j < 10; j++) { if (j == 5) break; acc++; }
            for (int k = 0; k < 5; k++) { if (k == 2) continue; acc++; }
            output[index] = acc;
        }

        /// <summary>
        /// Test kernel for the break-PHI bug: assignments before `break` in a loop
        /// must be visible after the loop exits. This is the exact pattern the WGSL
        /// transpiler was dropping — intermediate basic blocks between the break point
        /// and the merge block contain the assignment IR instructions.
        /// 
        /// Expected output[i]: hitStep * 100 + hitValue
        ///   For index 0: searches for val >= 5 in [0..9], finds at i=5 → 5*100+5 = 505
        ///   For index 1: searches for val >= 5 in [1..10], finds at i=4 → 4*100+5 = 405
        ///   etc.
        /// </summary>
        static void LoopBreakAssignmentKernel(Index1D index, ArrayView<int> output)
        {
            int hitStep = -1;
            int hitValue = -1;

            for (int i = 0; i < 10; i++)
            {
                int val = i + (int)index;
                if (val >= 5)
                {
                    hitStep = i;
                    hitValue = val;
                    break;
                }
            }

            // These values must reflect the assignments BEFORE the break
            if (hitStep >= 0)
                output[index] = hitStep * 100 + hitValue;
            else
                output[index] = -1;
        }

        static void HyperbolicKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
        {
            float val = input[index];
            if (index == 0) output[index] = MathF.Sinh(val);
            else if (index == 1) output[index] = MathF.Cosh(val);
            else if (index == 2) output[index] = MathF.Tanh(val);
        }

        static void SharedMemoryBarrierKernel(Index1D index, ArrayView<int> output)
        {
            int tid = Group.IdxX;
            var shared = SharedMemory.Allocate<int>(64);
            if (tid < 32) { shared[tid] = tid * 2; }
            Group.Barrier();
            if (tid >= 32) { int val = shared[tid - 32]; int gid = Grid.GlobalIndex.X; output[gid] = val; }
        }

        static void SelectKernel(Index1D index, ArrayView<int> input, ArrayView<int> output)
        {
            int val = input[index];
            output[index] = (val > 0) ? 1 : -1;
        }

        static void LinearBarrierKernel(Index1D index, ArrayView<int> output)
        {
            int tid = Group.IdxX;
            int gid = Grid.GlobalIndex.X;
            var shared = SharedMemory.Allocate<int>(64);
            shared[tid] = gid;
            Group.Barrier();
            int neighbor = (tid + 1) % 64;
            int val = shared[neighbor];
            output[gid] = val;
        }

        static void AdvancedMathKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
        {
            float val = input[index];
            output[index] = MathF.Tan(val) + MathF.Exp(val) + MathF.Log(MathF.Abs(val) + 1.0f) + MathF.Pow(val, 2.0f) + MathF.Min(val, 2.0f) + MathF.Max(val, 3.0f);
        }

        static void BitwiseKernel(Index1D index, ArrayView<int> data)
        {
            int val = data[index];
            data[index] = (val << 1) + (val >> 1) + (val & 1) + (val | 1) + (val ^ 1) + (~val);
        }

        static void InverseTrigKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
        {
            float val = input[index];
            if (index == 0) output[index] = MathF.Asin(val);
            else if (index == 1) output[index] = MathF.Acos(val);
            else if (index == 2) output[index] = MathF.Atan(val);
        }

        static void LargeBufferKernel(Index1D index, ArrayView<int> data) { data[index] = data[index] * 2; }
        static void SequentialKernel1(Index1D index, ArrayView<int> data) { data[index] = data[index] * 2; }
        static void SequentialKernel2(Index1D index, ArrayView<int> data) { data[index] = data[index] + 10; }

        static void UnsignedIntKernel(Index1D index, ArrayView<uint> data)
        {
            uint val = data[index];
            if (index == 0) data[index] = val / 3;
            else if (index == 1) data[index] = val % 4;
            else if (index == 2) data[index] = val + 1;
            else if (index == 3) data[index] = val + 100;
        }

        static void AtomicMinMaxKernel(Index1D index, ArrayView<int> minData, ArrayView<int> maxData)
        {
            Atomic.Min(ref minData[0], (int)index);
            Atomic.Max(ref maxData[0], (int)index);
        }

        static void BufferReuseKernel(Index1D index, ArrayView<int> data) { data[index] = data[index] + 1; }

        static void GridGroupDimensionKernel(Index1D index, ArrayView<int> output)
        {
            int globalId = Grid.GlobalIndex.X;
            int localId = Group.IdxX;
            int groupId = Grid.IdxX;
            int groupDim = Group.DimX;
            int baseIdx = globalId * 4;
            output[baseIdx] = globalId;
            output[baseIdx + 1] = localId;
            output[baseIdx + 2] = groupId;
            output[baseIdx + 3] = groupDim;
        }

        // Double/Long kernels
        protected static void DoublePrecisionKernel(Index1D index, ArrayView<double> input, ArrayView<double> output) { output[index] = input[index] * 2.0 + 1.0; }
        static void HalfPassthroughKernel(Index1D index, ArrayView<global::ILGPU.Half> data) { data[index] = data[index]; }
        static void HalfArithmeticKernel(Index1D index, ArrayView<global::ILGPU.Half> input, ArrayView<global::ILGPU.Half> output) { output[index] = (global::ILGPU.Half)((float)input[index] * 2f + 1f); }
        static void HalfMinMaxKernel(Index1D index, ArrayView<global::ILGPU.Half> a, ArrayView<global::ILGPU.Half> b, ArrayView<global::ILGPU.Half> output) { float va = (float)a[index], vb = (float)b[index]; output[index] = (global::ILGPU.Half)((va > vb ? va : vb) - (va < vb ? va : vb)); }
        static void HalfEdgeCasesKernel(Index1D index, ArrayView<global::ILGPU.Half> data) { data[index] = (global::ILGPU.Half)((float)data[index] * 1.0f); }
        static void HalfMixedTypeKernel(Index1D index, ArrayView<global::ILGPU.Half> halfIn, ArrayView<int> intIn, ArrayView<float> output) { output[index] = (float)halfIn[index] + (float)intIn[index]; }
        static void LongIntegerKernel(Index1D index, ArrayView<long> data) { data[index] = data[index] * 2 + 1; }
        static void LongArithmeticKernel(Index1D index, ArrayView<long> a, ArrayView<long> b, ArrayView<long> output) { output[index] = (a[index] + b[index]) - (a[index] * 2); }
        static void LongBitwiseKernel(Index1D index, ArrayView<long> input, ArrayView<long> output) { long val = input[index]; long mask = 0x0F0F0F0F0F0F0F0FL; output[index] = (val & mask) | ((val >> 4) & mask); }
        static void LongComparisonKernel(Index1D index, ArrayView<long> a, ArrayView<long> b, ArrayView<long> output) { output[index] = a[index] > b[index] ? a[index] : b[index]; }
        static void LongEdgeCasesKernel(Index1D index, ArrayView<long> data) { data[index] = data[index] + 1L - 1L; }
        static void DoubleMathKernel(Index1D index, ArrayView<double> input, ArrayView<double> output) { output[index] = Math.Sqrt(input[index]) * 2.0; }
        static void DoubleEdgeCasesKernel(Index1D index, ArrayView<double> data) { data[index] = data[index] * 1.0; }
        static void LongMultiBufferKernel(Index1D index, ArrayView<long> input, ArrayView<long> output) { output[index] = input[index] * 3 + 7; }
        static void DoubleMultiBufferKernel(Index1D index, ArrayView<double> input, ArrayView<double> output) { output[index] = input[index] * 3.0 + 0.5; }
        static void LongNegationKernel(Index1D index, ArrayView<long> data) { data[index] = -data[index]; }
        static void LongShiftKernel(Index1D index, ArrayView<long> input, ArrayView<long> output) { output[index] = (input[index] << 8) >> 4; }
        static void LongSignedCompareKernel(Index1D index, ArrayView<long> a, ArrayView<long> b, ArrayView<long> output) { output[index] = (a[index] < b[index]) ? 1L : 0L; }
        static void LongChainedOpsKernel(Index1D index, ArrayView<long> input, ArrayView<long> output) { output[index] = ((input[index] + 10L) * 3L - 5L); }
        static void LongLargeDatasetKernel(Index1D index, ArrayView<long> data) { data[index] = data[index] * 2L + 1L; }
        static void LongNegativeValuesKernel(Index1D index, ArrayView<long> data) { data[index] = data[index] * -1L + data[index]; }
        static void DoubleNegationKernel(Index1D index, ArrayView<double> data) { data[index] = -data[index]; }
        protected static void DoubleDivisionKernel(Index1D index, ArrayView<double> numerator, ArrayView<double> divisor, ArrayView<double> output) { output[index] = numerator[index] / divisor[index]; }
        protected static void DoubleChainedOpsKernel(Index1D index, ArrayView<double> input, ArrayView<double> output) { output[index] = (input[index] * 2.5 + 1.0) / 3.0; }
        static void DoubleLargeDatasetKernel(Index1D index, ArrayView<double> data) { data[index] = data[index] * 2.0 + 1.0; }
        protected static void DoubleMinMaxKernel(Index1D index, ArrayView<double> a, ArrayView<double> b, ArrayView<double> output) { double va = a[index], vb = b[index]; output[index] = (va > vb ? va : vb) - (va < vb ? va : vb); }
        protected static void DoublePrecisionVerifyKernel(Index1D index, ArrayView<double> data) { data[index] = data[index] * 10.0 / 10.0; }

        // More kernels
        static void MixedTypeKernel(Index1D index, ArrayView<int> intData, ArrayView<float> floatData, ArrayView<float> result) { result[index] = (float)intData[index] + floatData[index]; }
        static void EmptyBufferKernel(Index1D index, ArrayView<int> data) { data[index] = data[index] * 2; }
        static void LargeDispatchKernel(Index1D index, ArrayView<int> data) { data[index] = data[index] + 1; }
        static void ComparisonKernel(Index1D index, ArrayView<int> a, ArrayView<int> b, ArrayView<int> output)
        {
            int va = a[index], vb = b[index];
            if (index == 0) output[index] = (va > vb) ? 1 : 0;
            else if (index == 1) output[index] = (va < vb) ? 1 : 0;
            else if (index == 2) output[index] = (va >= vb) ? 1 : 0;
            else if (index == 3) output[index] = (va <= vb) ? 1 : 0;
            else if (index == 4) output[index] = (va == vb) ? 1 : 0;
            else if (index == 5) output[index] = (va != vb) ? 1 : 0;
        }

        static void ShortCircuitKernel(Index1D index, ArrayView<int> a, ArrayView<int> b, ArrayView<int> output)
        {
            bool valA = a[index] != 0, valB = b[index] != 0;
            bool result;
            if (index < 2) result = valA && valB;
            else result = valA || valB;
            output[index] = result ? 1 : 0;
        }

        static void NaNInfinityKernel(Index1D index, ArrayView<float> input, ArrayView<int> output)
        {
            if (index == 0) { float inf = 1.0f / 0.0f; output[index] = (inf > 0.0f) ? 1 : 0; }
            else if (index == 1) { float negInf = -1.0f / 0.0f; output[index] = (negInf < 0.0f) ? 1 : 0; }
            else if (index == 2) { float nan = 0.0f / 0.0f; output[index] = (nan == nan) ? 1 : 0; }
            else { float nan = 0.0f / 0.0f; output[index] = (nan != nan) ? 1 : 0; }
        }

        static void ReductionKernel(Index1D index, ArrayView<int> data, ArrayView<int> sum) { Atomic.Add(ref sum[0], data[index]); }
        static void GatherKernel(Index1D index, ArrayView<int> data, ArrayView<int> indices, ArrayView<int> output) { output[index] = data[indices[index]]; }

        // ML's GatherAxis0FloatImpl pattern (DistilGPT-2 / DistilBERT embedding lookup):
        //   output[idx] = data[(int)indices[outRow] * innerSize + col]
        // with bounds-checked srcIdx via data.Length. Exercises:
        //   - integer divide/modulo of Index1D
        //   - float->int cast of indices[outRow]
        //   - `data.Length` (long property) used in bounds check
        //   - conditional ternary returning data[srcIdx] vs 0f
        // Repro for the WebGL all-zero output regression Data identified
        // (data-to-geordi-webgl-gather-is-the-bug-2026-05-03.md, node 002 Gather).
        static void GatherAxis0FloatLikeMlKernel(Index1D idx,
            ArrayView1D<float, Stride1D.Dense> data,
            ArrayView1D<float, Stride1D.Dense> indices,
            ArrayView1D<float, Stride1D.Dense> output,
            int innerSize)
        {
            int outRow = idx / innerSize;
            int col = idx % innerSize;
            int srcRow = (int)indices[outRow];
            int srcIdx = srcRow * innerSize + col;
            output[idx] = (srcIdx >= 0 && srcIdx < data.Length)
                ? data[srcIdx] : 0f;
        }

        static void DeepNestingKernel(Index1D index, ArrayView<int> data)
        {
            int count = 0;
            if (true) { count++; if (true) { count++; if (true) { count++; if (true) { count++; if (true) { count++; } } } } }
            data[index] = count;
        }

        static void ZeroLoopKernel(Index1D index, ArrayView<int> data) { for (int i = 0; i < 0; i++) data[index] = 0; }

        static void MultiOutputKernel(Index1D index, ArrayView<int> input, ArrayView<int> sum, ArrayView<int> product, ArrayView<int> square)
        {
            int val = input[index]; sum[index] = val + 10; product[index] = val * 2; square[index] = val * val;
        }

        static void MatMulKernel(Index1D index, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c, int size)
        {
            int row = index / size, col = index % size;
            float s = 0.0f;
            for (int k = 0; k < size; k++) s += a[row * size + k] * b[k * size + col];
            c[index] = s;
        }

        static void AddConstantKernel(Index1D index, ArrayView<int> data, int constant) { data[index] += constant; }
        static void DoubleValueKernel(Index1D index, ArrayView<int> data) { data[index] *= 2; }
        static void AddTenKernel(Index1D index, ArrayView<int> data) { data[index] += 10; }

        static void BoundaryKernel(Index1D index, ArrayView<int> data, int length)
        {
            if (index == 0) data[index] = -1;
            else if (index == length - 1) data[index] = 1;
            else data[index] = 0;
        }

        static void FloatSpecialOpsKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
        {
            output[index] = Math.Min(Math.Max(input[index], 0.0f), 1.0f);
        }

        static void ModuloKernel(Index1D index, ArrayView<int> data) { data[index] = data[index] % 4; }
        static void SimpleSetKernel(Index1D index, ArrayView<int> data) { data[index] = index; }

        static void StencilKernel(Index1D index, ArrayView<float> input, ArrayView<float> output, int length)
        {
            int i = (int)index; int lastIndex = length - 1;
            if (i == 0) output[i] = input[i];
            else if (i == lastIndex) output[i] = input[i];
            else output[i] = (input[i - 1] + input[i] + input[i + 1]) / 3.0f;
        }

        static void ParallelSumKernel(Index1D index, ArrayView<int> data, ArrayView<int> result, int length) { Atomic.Add(ref result[0], data[index]); }

        static void TypeConversionKernel(Index1D index, ArrayView<float> floatIn, ArrayView<int> intOut, ArrayView<float> floatOut)
        {
            int truncated = (int)floatIn[index]; intOut[index] = truncated; floatOut[index] = (float)truncated;
        }

        static void LeftShiftKernel(Index1D index, ArrayView<int> data) { data[index] = data[index] << 2; }
        static void RightShiftKernel(Index1D index, ArrayView<int> data) { data[index] = data[index] >> 1; }

        static void NestedStructKernel(Index1D index, ArrayView<NestedOuterStruct> structs, ArrayView<int> result)
        {
            var s = structs[index]; result[index] = s.Inner.A + s.Inner.B + (int)s.Value;
        }

        static void CopyKernel(Index1D index, ArrayView<int> src, ArrayView<int> dst) { dst[index] = src[index]; }

        static void IntUnaryKernel(Index1D index, ArrayView<int> data) { data[index] = ~(-data[index]); }
        static void FloatUnaryKernel(Index1D index, ArrayView<float> data) { data[index] = -data[index]; }

        static void SmoothstepKernel(Index1D index, ArrayView<float> data) { float t = data[index]; data[index] = t * t * (3.0f - 2.0f * t); }

        static void LerpKernel(Index1D index, ArrayView<float> t, ArrayView<float> output, float a, float b)
        {
            output[index] = a + t[index] * (b - a);
        }

        static void MixedTypesKernel(Index1D index, ArrayView<int> ints, ArrayView<uint> uints, ArrayView<float> floats, ArrayView<float> result)
        {
            result[index] = ints[index] + uints[index] + floats[index];
        }

        static void ComplexExpressionKernel(Index1D index, ArrayView<float> data)
        {
            float x = data[index]; data[index] = ((x * 2 + 3) / 4 - 1) * 5 + x;
        }

        protected static void BroadcastKernel(Index1D index, ArrayView<int> data)
        {
            int gid = Grid.GlobalIndex.X;
            int val = data[gid];
            int broadcasted = Group.Broadcast(val, 0);
            data[gid] = broadcasted;
        }

        protected static void SubgroupShuffleKernel(Index1D index, ArrayView<int> data, ArrayView<int> warpSizeOut)
        {
            int gid = Grid.GlobalIndex.X;
            int val = data[gid];
            // Each thread reads from lane 0 of its subgroup via warp shuffle
            int shuffled = Warp.Shuffle(val, 0);
            data[gid] = shuffled;
            if (gid == 0)
                warpSizeOut[0] = Warp.WarpSize;
        }

        /// <summary>
        /// Kernel that takes a struct as a direct scalar argument (not in an ArrayView).
        /// Writes the sum of the struct's fields to each element of the output buffer.
        /// </summary>
        static void StructScalarArgKernel(Index1D index, ArrayView<float> output, ScalarStruct s)
        {
            output[index] = s.X + s.Y;
        }

        /// <summary>
        /// Kernel that takes a nested struct as a direct scalar argument.
        /// Writes Inner.A + Inner.B + (int)Value to each element of the output buffer.
        /// </summary>
        static void NestedStructScalarArgKernel(Index1D index, ArrayView<int> output, NestedOuterStruct s)
        {
            output[index] = s.Inner.A + s.Inner.B + (int)s.Value;
        }

        /// <summary>
        /// Kernel with 12 scalar parameters to test high storage buffer binding counts.
        /// With auto-grouping overhead (~7 additional bindings), this kernel requires ~19
        /// storage buffer bindings — well above WebGPU's default limit of 8.
        /// Validates that the adapter's max limit is requested correctly.
        /// </summary>
        static void ManyScalarKernel(
            Index1D index, ArrayView<int> output,
            int a, int b, int c, int d,
            int e, int f, int g, int h,
            int i, int j, int k, int l)
        {
            output[index] = a + b + c + d + e + f + g + h + i + j + k + l;
        }

        /// <summary>
        /// GPU kernel: depth float → RGBA inferno-style colormap.
        /// Uses multiple Math.Min(Math.Max(...)) calls and ArrayView.Length —
        /// the pattern that originally triggered the GetViewLength code generation bug.
        /// </summary>
        static void ColormapKernel(
            Index1D index,
            ArrayView<float> depth,
            ArrayView<uint> output,
            float minVal, float invRange)
        {
            int len = (int)output.Length;
            if (index >= len) return;

            float raw = (depth[index] - minVal) * invRange;
            float t = Math.Min(Math.Max(raw, 0f), 1f);

            // Three-segment colormap with multiple Math.Min/Max clamps
            float s0 = Math.Min(Math.Max(t * 3.030303f, 0f), 1f);
            float s1 = Math.Min(Math.Max((t - 0.33f) * 3.030303f, 0f), 1f);
            float s2 = Math.Min(Math.Max((t - 0.66f) * 2.941176f, 0f), 1f);

            float r0 = 0.05f + s0 * 0.35f;
            float r1 = 0.4f + s1 * 0.5f;
            float r2 = 0.9f + s2 * 0.1f;

            float g0 = 0.0f + s0 * 0.1f;
            float g1 = 0.1f + s1 * 0.3f;
            float g2 = 0.4f + s2 * 0.55f;

            float b0 = 0.2f + s0 * 0.4f;
            float b1 = 0.6f - s1 * 0.3f;
            float b2 = 0.3f - s2 * 0.15f;

            float r = r0 + s1 * (r1 - r0) + s2 * (r2 - r1);
            float g = g0 + s1 * (g1 - g0) + s2 * (g2 - g1);
            float b = b0 + s1 * (b1 - b0) + s2 * (b2 - b1);

            uint ri = (uint)(r * 255f);
            uint gi = (uint)(g * 255f);
            uint bi = (uint)(b * 255f);
            output[index] = ri | (gi << 8) | (bi << 16) | 0xFF000000u;
        }

        #endregion


    }
}
