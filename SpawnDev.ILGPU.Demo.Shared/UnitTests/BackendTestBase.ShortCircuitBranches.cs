using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Short-circuit conditions (`a || b`, `a && b`) in front of a body. Release IL compiles them to a chain of branches
    // that all jump to the same body block. The WGSL structured walker used to emit that body once per operand, loops
    // included, and inlined call sites multiplied it: SpawnDev.Codecs' VP8 bool decoder Fill
    // (`if (x < 0 || bitsLeft != 0) { while (..) }`) repeated its refill loop at every call site of the VP9 parse kernel,
    // and the VP9 inter-prediction kernel came out at 78k WGSL lines (Geordi, 2026-11-09). WebGPU and WebGL now lower the
    // chain to a flag and emit the body once. Every shape is checked against the kernel run on the CPU accelerator; on the
    // WebGPU and WebGL lanes the shader's loop count is gated too.
    public abstract partial class BackendTestBase
    {
        public struct ShortCircuitState
        {
            public int Pos;
            public int End;
            public ulong Value;
            public int Count;
        }

        // `if (a || b) for (..)`
        static void ShortCircuitOrLoopKernel(Index1D i, ArrayView<int> d, ArrayView<int> o)
        {
            int count = d[i & 255] & 15, pos = d[(i + 3) & 255] & 7;
            int acc = 1;
            if (count < 8 || pos == 0)
                for (int k = 0; k < count; k++) acc += d[(k + pos) & 255] ^ k;
            o[i] = acc;
        }

        // Codecs' Vp8BoolDecoderGpu.Fill shape: an if before the `||` feeds the loop bound, the state is a ref struct,
        // the `while` jumps straight to its condition block (the loop header is the shared target), called twice
        static void ShortCircuitFill(ref ShortCircuitState s, ArrayView<int> d)
        {
            int shift = 48 - s.Count;
            int bitsLeft = (s.End - s.Pos) * 8;
            int x = shift + 8 - bitsLeft;
            int loopEnd = 0;
            if (x >= 0) { s.Count += 0x4000; loopEnd = x; }
            if (x < 0 || bitsLeft != 0)
            {
                while (shift >= loopEnd)
                {
                    s.Count += 8;
                    s.Value |= (ulong)(uint)(d[s.Pos & 255] & 0xFF) << shift;
                    s.Pos++;
                    shift -= 8;
                }
            }
        }

        static void ShortCircuitFillKernel(Index1D i, ArrayView<int> d, ArrayView<int> o)
        {
            var s = new ShortCircuitState { Pos = i, End = i + (d[i & 255] & 7), Value = 0, Count = -8 + (d[(i + 1) & 255] & 15) };
            ShortCircuitFill(ref s, d);
            int first = (int)s.Value ^ (int)(s.Value >> 32);
            s.Count = -8 + (s.Count & 7);
            s.End = s.Pos + (d[(i + 2) & 255] & 3);
            ShortCircuitFill(ref s, d);
            o[i] = first ^ (int)s.Value ^ (int)(s.Value >> 32) ^ s.Pos ^ s.Count;
        }

        // three operands
        static void ShortCircuitOr3Kernel(Index1D i, ArrayView<int> d, ArrayView<int> o)
        {
            int a = d[i & 255], b = d[(i + 1) & 255], c = d[(i + 2) & 255];
            int acc = 0, k = a & 15;
            if ((a & 3) == 0 || (b & 3) == 1 || c < 0)
            {
                while (k > 0) { acc = acc * 7 + d[(k + b) & 255]; k--; }
            }
            o[i] = acc ^ k;
        }

        // `||` with an else arm, a loop in each
        static void ShortCircuitOrElseKernel(Index1D i, ArrayView<int> d, ArrayView<int> o)
        {
            int x = d[i & 255] & 15, y = d[(i + 5) & 255] & 15;
            int acc = x;
            if (x < 4 || y > 9)
                for (int k = 0; k < y; k++) acc += d[(k * 3 + x) & 255];
            else
                for (int k = 0; k < x; k++) acc ^= d[(k + y) & 255] >> 1;
            o[i] = acc;
        }

        // `&&`: the shared target is the else arm
        static void ShortCircuitAndElseKernel(Index1D i, ArrayView<int> d, ArrayView<int> o)
        {
            int x = d[i & 255] & 15, y = d[(i + 7) & 255] & 15;
            int acc;
            if (x > 2 && y < 5)
                acc = d[(x * y) & 255];
            else
            {
                acc = 3;
                for (int k = 0; k < x + y; k++) acc = acc * 5 + d[(k ^ x) & 255];
            }
            o[i] = acc;
        }

        // inside an outer loop
        static void ShortCircuitInLoopKernel(Index1D i, ArrayView<int> d, ArrayView<int> o)
        {
            int acc = 0;
            for (int r = 0; r < 6; r++)
            {
                int v = d[(i + r * 11) & 255];
                if ((v & 7) == 0 || (v & 0x70) == 0x10)
                {
                    int k = v & 7;
                    while (k < 9) { acc += d[(k + r) & 255] & 0xFFF; k += 2; }
                }
                acc ^= r;
            }
            o[i] = acc;
        }

        // the second operand's value is used in the else arm: the else arm runs inside the chain, where it is in scope
        static void ShortCircuitValueInElseKernel(Index1D i, ArrayView<int> d, ArrayView<int> o)
        {
            int a = d[i & 255];
            int t = 0;
            int acc = 0;
            if ((a & 1) == 0 || (t = d[(a >> 1) & 255]) > 1000000)
            {
                for (int k = 0; k < (a & 7); k++) acc += d[(k + a) & 255] & 0xFFFF;
            }
            else
                acc = t * 3;
            o[i] = acc;
        }

        // loops in the generated shader: WGSL `loop {`, GLSL `for (int _loopN = 0; ...)`
        static int ShaderLoopCount(string kernelName, bool webgl)
        {
            var method = typeof(BackendTestBase).GetMethod(kernelName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var src = ShaderCompiler.Generate(method!, webgl ? CapabilityProfiles.WebGL2Baseline : CapabilityProfiles.WebGPUBaseline).Source ?? "";
            string needle = webgl ? "for (int _loop" : "loop {";
            int n = 0, at = 0;
            while ((at = src.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
            return n;
        }

        async Task ShortCircuitCheck(Accelerator accelerator, Action<Index1D, ArrayView<int>, ArrayView<int>> kernelMethod, string name, int? loops)
        {
            bool webgl = accelerator.AcceleratorType == AcceleratorType.WebGL;
            if (loops != null && (webgl || accelerator.AcceleratorType == AcceleratorType.WebGPU))
            {
                int got = ShaderLoopCount(name, webgl);
                if (got != loops)
                    throw new Exception($"{name}: {got} {(webgl ? "GLSL" : "WGSL")} loops, expected {loops}: the short-circuit body is emitted more than once");
            }
            const int n = 512;
            var data = new int[256];
            var rnd = new Random(1109);
            for (int k = 0; k < data.Length; k++) data[k] = rnd.Next(int.MinValue, int.MaxValue);
            using var dData = accelerator.Allocate1D(data);
            using var dOut = accelerator.Allocate1D<int>(n);
            dOut.MemSetToZero();
            var kernel = accelerator.LoadAutoGroupedStreamKernel(kernelMethod);
            kernel((Index1D)n, dData.View, dOut.View);
            var got2 = await dOut.CopyToHostAsync<int>();

            // oracle: the kernel method itself on CPU-accelerator views, one thread at a time
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hData = cpu.Allocate1D(data);
            using var hOut = cpu.Allocate1D<int>(n);
            hOut.MemSetToZero();
            for (int t = 0; t < n; t++) kernelMethod(t, hData.View, hOut.View);
            var want = await hOut.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
                if (got2[t] != want[t]) throw new Exception($"{name}: o[{t}] = {got2[t]}, host {want[t]}");
        }

        [TestMethod]
        public async Task ShortCircuit_OrBeforeLoop_EmittedOnce() => await RunTest(async accelerator =>
            await ShortCircuitCheck(accelerator, ShortCircuitOrLoopKernel, nameof(ShortCircuitOrLoopKernel), 1));

        [TestMethod]
        public async Task ShortCircuit_FillShape_EmittedOnce() => await RunTest(async accelerator =>
            await ShortCircuitCheck(accelerator, ShortCircuitFillKernel, nameof(ShortCircuitFillKernel), 2));

        [TestMethod]
        public async Task ShortCircuit_ThreeOperands_EmittedOnce() => await RunTest(async accelerator =>
            await ShortCircuitCheck(accelerator, ShortCircuitOr3Kernel, nameof(ShortCircuitOr3Kernel), 1));

        [TestMethod]
        public async Task ShortCircuit_OrWithElse_EmittedOnce() => await RunTest(async accelerator =>
            await ShortCircuitCheck(accelerator, ShortCircuitOrElseKernel, nameof(ShortCircuitOrElseKernel), 2));

        [TestMethod]
        public async Task ShortCircuit_AndSharedElse_EmittedOnce() => await RunTest(async accelerator =>
            await ShortCircuitCheck(accelerator, ShortCircuitAndElseKernel, nameof(ShortCircuitAndElseKernel), 1));

        [TestMethod]
        public async Task ShortCircuit_InsideLoop_EmittedOnce() => await RunTest(async accelerator =>
            await ShortCircuitCheck(accelerator, ShortCircuitInLoopKernel, nameof(ShortCircuitInLoopKernel), 2));

        [TestMethod]
        public async Task ShortCircuit_OperandValueUsedInElse_EmittedOnce() => await RunTest(async accelerator =>
            await ShortCircuitCheck(accelerator, ShortCircuitValueInElseKernel, nameof(ShortCircuitValueInElseKernel), 1));
    }
}
