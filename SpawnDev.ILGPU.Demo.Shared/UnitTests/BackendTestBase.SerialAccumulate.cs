using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // SpawnDev.Codecs VorbisResidueAccumulateGpu (Geordi 2026-10-26): ONE thread loops over accumulation calls, each adding
    // a looked-up vector to target[off + d] for d < dims (run-time trip counts, offsets 0, 4, 0, 0). One store site writes
    // several elements, which WebGL's transform feedback cannot capture; WebGL must refuse the dispatch, not return a
    // buffer with an element left at its old value (target[0] came back 0.1, CPU 0.100000024, and nothing refused).
    public abstract partial class BackendTestBase
    {
        // Verbatim shape of SpawnDev.Codecs CodecsTestBase.AccumulateKernel + VorbisResidueAccumulateGpu.LookupAndAccumulate.
        static void SerialAccumulate_Kernel(Index1D _, ArrayView<int> mult, ArrayView<float> target, ArrayView<int> calls,
            int multLen, int entries, int dims, int lookupType, double minValue, double delta, int sequenceP, int callCount)
        {
            for (int i = 0; i < callCount; i++)
            {
                int entry = calls[i * 2 + 0];
                int targetOff = calls[i * 2 + 1];
                SerialAccumulate_LookupAndAccumulate(mult, 0, multLen, entry, entries, dims, lookupType, 0, minValue, delta,
                    sequenceP, target, targetOff);
            }
        }

        static void SerialAccumulate_LookupAndAccumulate(ArrayView<int> multiplicands, long multBase, int multLen,
            int entry, int entries, int dimensions, int lookupType, int quantvals, double minValue, double deltaValue,
            int sequenceP, ArrayView<float> target, long targetBase)
        {
            if (lookupType == 0 || entry < 0 || entry >= entries) return;
            double last = 0;
            if (lookupType == 1)
            {
                int indexDivisor = 1;
                for (int d = 0; d < dimensions; d++)
                {
                    int multIndex = (entry / indexDivisor) % quantvals;
                    int m = multIndex < multLen ? multiplicands[multBase + multIndex] : 0;
                    int absM = m < 0 ? -m : m;
                    double val = (double)absM * deltaValue + minValue + last;
                    if (sequenceP != 0) last = val;
                    target[targetBase + d] += (float)val;
                    indexDivisor *= quantvals;
                }
                return;
            }
            int baseIndex = entry * dimensions;
            for (int d = 0; d < dimensions; d++)
            {
                int flatIndex = baseIndex + d;
                int m = (flatIndex < 0 || flatIndex >= multLen) ? 0 : multiplicands[multBase + flatIndex];
                int absM = m < 0 ? -m : m;
                double val = (double)absM * deltaValue + minValue + last;
                if (sequenceP != 0) last = val;
                target[targetBase + d] += (float)val;
            }
        }

        [TestMethod]
        public async Task StoreAddress_SerialAccumulateCalls_RunsOrIsRefusedOnWebGL() => await RunEmulatedTest(async accelerator =>
        {
            int[] mult = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            var initial = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };
            int[] calls = { 0, 0, 1, 4, 2, 0, 1, 0 };
            const int dims = 4;
            var want = (float[])initial.Clone();
            for (int i = 0; i < calls.Length / 2; i++)
                for (int d = 0; d < dims; d++)
                    want[calls[i * 2 + 1] + d] += (float)(mult[calls[i * 2] * dims + d] * 0.5 + -2.0);
            using var dMult = accelerator.Allocate1D(mult);
            using var dTarget = accelerator.Allocate1D(initial);
            using var dCalls = accelerator.Allocate1D(calls);
            try
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>, ArrayView<int>, int, int, int, int, double, double, int, int>(
                    SerialAccumulate_Kernel)((Index1D)1, dMult.View, dTarget.View, dCalls.View, mult.Length, 3, dims, 2, -2.0, 0.5, 0, calls.Length / 2);
                await accelerator.SynchronizeAsync();
                var got = await dTarget.CopyToHostAsync<float>();
                if (accelerator is SpawnDev.ILGPU.WebGL.WebGLAccelerator)
                    throw new Exception($"WebGL ran a store site that writes several elements per thread; it must refuse it (target[0] = {got[0]}, expected {want[0]})");
                for (int i = 0; i < want.Length; i++)
                    if (got[i] != want[i]) throw new Exception($"target[{i}] = {got[i]:R}, expected {want[i]:R}");
            }
            catch (Exception ex) when (accelerator is SpawnDev.ILGPU.WebGL.WebGLAccelerator && !ex.Message.Contains("it must refuse it"))
            {
                bool refused = false;
                for (var e = ex; e != null; e = e.InnerException)
                    if (e.Message.Contains("wrote two different elements")) refused = true;
                if (!refused) throw;
            }
        });
    }
}
