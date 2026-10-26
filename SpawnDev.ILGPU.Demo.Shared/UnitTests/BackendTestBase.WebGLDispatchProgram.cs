using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGL;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-26 (SpawnDev.Codecs WebGL, found by Geordi): 42 tests died on their first WebGL dispatch with
    // OutOfMemoryException while SpawnJS marshalled the dispatch message. The serial Inflate step kernel has 158 output
    // varyings (155 store sites of one view), more than transform feedback can capture, and every dispatch carried the
    // whole GLSL source (6,816 lines) ahead of one GLOutputEntry per varying (SpawnJS 3.0.3's tape then allocated a new
    // segment of the message's size for each small write). Now:
    // - a kernel with more output varyings than the device's MAX_TRANSFORM_FEEDBACK_INTERLEAVED_COMPONENTS is refused
    //   at load with UnsupportedKernelFeatureException;
    // - the GLSL source and varying names go to the GL worker with a program's first dispatch only.
    public abstract partial class BackendTestBase
    {
        private const int TfStoreSites = 320;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TfStore16(ArrayView<int> o, int b, int v)
        {
            o[b] = v; o[b + 1] = v ^ 1; o[b + 2] = v ^ 2; o[b + 3] = v ^ 3; o[b + 4] = v ^ 4; o[b + 5] = v ^ 5; o[b + 6] = v ^ 6; o[b + 7] = v ^ 7;
            o[b + 8] = v ^ 8; o[b + 9] = v ^ 9; o[b + 10] = v ^ 10; o[b + 11] = v ^ 11; o[b + 12] = v ^ 12; o[b + 13] = v ^ 13; o[b + 14] = v ^ 14; o[b + 15] = v ^ 15;
        }

        // 320 straight-line store sites per thread at positional offsets i * 320 + k: 320 output varyings on WebGL.
        private static void TfManyStoreSitesKernel(Index1D i, ArrayView<int> output)
        {
            int b = i * TfStoreSites;
            TfStore16(output, b + 0, i * 1000 + 0);
            TfStore16(output, b + 16, i * 1000 + 16);
            TfStore16(output, b + 32, i * 1000 + 32);
            TfStore16(output, b + 48, i * 1000 + 48);
            TfStore16(output, b + 64, i * 1000 + 64);
            TfStore16(output, b + 80, i * 1000 + 80);
            TfStore16(output, b + 96, i * 1000 + 96);
            TfStore16(output, b + 112, i * 1000 + 112);
            TfStore16(output, b + 128, i * 1000 + 128);
            TfStore16(output, b + 144, i * 1000 + 144);
            TfStore16(output, b + 160, i * 1000 + 160);
            TfStore16(output, b + 176, i * 1000 + 176);
            TfStore16(output, b + 192, i * 1000 + 192);
            TfStore16(output, b + 208, i * 1000 + 208);
            TfStore16(output, b + 224, i * 1000 + 224);
            TfStore16(output, b + 240, i * 1000 + 240);
            TfStore16(output, b + 256, i * 1000 + 256);
            TfStore16(output, b + 272, i * 1000 + 272);
            TfStore16(output, b + 288, i * 1000 + 288);
            TfStore16(output, b + 304, i * 1000 + 304);
        }

        private static void TfAddKernel(Index1D i, ArrayView<int> data, int k) => data[i] = data[i] + k;

        private static void TfXorKernel(Index1D i, ArrayView<int> data, int k) => data[i] = data[i] ^ (k * 7 + i);

        [TestMethod]
        public async Task WebGLDispatch_MoreOutputsThanTransformFeedback_RunsOrIsRefusedAtLoad() => await RunTest(async accelerator =>
        {
            const int N = 1024;
            int limit = accelerator.Device is WebGLILGPUDevice webglDevice
                ? webglDevice.NativeDevice.MaxTransformFeedbackInterleavedComponents
                : int.MaxValue;
            Action<Index1D, ArrayView<int>> launch;
            try
            {
                launch = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(TfManyStoreSitesKernel);
            }
            catch (UnsupportedKernelFeatureException ex)
            {
                if (TfStoreSites <= limit)
                    throw new Exception($"WebGLDispatch: {TfStoreSites} outputs refused although the device captures {limit}: {ex.Message}");
                if (!ex.Message.Contains("transform feedback", StringComparison.Ordinal) || !ex.Message.Contains(limit.ToString(), StringComparison.Ordinal))
                    throw new Exception($"WebGLDispatch: refusal does not name transform feedback and the device limit {limit}: {ex.Message}");
                return;
            }
            if (TfStoreSites > limit)
                throw new Exception($"WebGLDispatch: a kernel with {TfStoreSites} outputs loaded on a device that captures {limit}; it must be refused at load.");

            using var buffer = accelerator.Allocate1D<int>(N * TfStoreSites);
            launch(N, buffer.View);
            await accelerator.SynchronizeAsync();
            var got = await buffer.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                for (int k = 0; k < TfStoreSites; k++)
                {
                    int want = (i * 1000 + (k & ~15)) ^ (k & 15);
                    if (got[i * TfStoreSites + k] != want)
                        throw new Exception($"WebGLDispatch: thread {i} store {k}: expected {want}, got {got[i * TfStoreSites + k]}.");
                }
            }
        });

        [TestMethod]
        public async Task WebGLDispatch_ProgramsRedispatched_UseTheirCachedSource() => await RunTest(async accelerator =>
        {
            // Two programs alternate for 64 rounds: on WebGL every dispatch after a program's first carries no source and
            // no varying names, so the worker must take both from its cache by program id (and keep them apart).
            const int N = 4096;
            const int Rounds = 64;
            var init = new int[N];
            for (int i = 0; i < N; i++) init[i] = i * 31 - 5000;
            using var buffer = accelerator.Allocate1D(init);
            var add = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(TfAddKernel);
            var xor = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(TfXorKernel);
            for (int r = 0; r < Rounds; r++)
            {
                add(N, buffer.View, r + 1);
                xor(N, buffer.View, r);
                if ((r & 7) == 7) accelerator.Flush();
            }
            await accelerator.SynchronizeAsync();
            var got = await buffer.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int want = init[i];
                for (int r = 0; r < Rounds; r++)
                {
                    want += r + 1;
                    want ^= r * 7 + i;
                }
                if (got[i] != want)
                    throw new Exception($"WebGLDispatch: element {i} after {Rounds} alternating rounds: expected {want}, got {got[i]}.");
            }
        });
    }
}
