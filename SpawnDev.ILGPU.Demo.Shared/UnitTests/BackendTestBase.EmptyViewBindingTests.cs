using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part: an EMPTY (zero-length) buffer/view must be dispatchable on every backend.
    //
    // WHY THIS FILE EXISTS: WebGPU refuses to bind a zero-sized storage buffer - "Binding size for
    // [Buffer (unlabeled)] is zero", validated against `minBindingSize: 4` - and that failure does not
    // stop at the one binding: the BindGroup becomes invalid, then the CommandEncoder, then the whole
    // CommandBuffer, so a single legitimately-empty tensor takes down every dispatch batched with it.
    //
    // Empty is legitimate and common. ONNX uses a zero-length tensor to say "no padding here", and a
    // Slice can correctly select nothing. MEASURED 2026-09-01: ZipVoice's flow-matching decoder at 2054
    // frames died at node 896 (ReduceSum) with exactly that error on WebGPU, while the SAME graph
    // produced values matching onnxruntime on OpenCL - which tolerates a zero-length binding silently.
    // So this is a backend-portability defect, not a model bug, and only a browser lane can catch it.
    //
    // The fix is a 4-byte floor on both the allocation and the binding size. It changes no semantics:
    // the VIEW still has Length 0, so no kernel reads or writes an element of it.
    public abstract partial class BackendTestBase
    {
        private static void EmptyView_TouchKernel(Index1D i, ArrayView<int> empty, ArrayView<int> output)
        {
            // Deliberately takes the empty view as a real parameter so it must be BOUND. The guard is
            // what a correct kernel does with an empty input; the point is that binding it is legal.
            if (i < empty.Length) empty[i] = 1;
            output[i] = i + 1;
        }

        /// <summary>
        /// A zero-length view can be passed to a kernel and the dispatch still succeeds.
        /// </summary>
        /// <remarks>
        /// ⚠️ Asserts the OUTPUT is correct, not merely that nothing threw. On WebGPU an invalid bind
        /// group poisons the whole CommandBuffer, so the observable symptom is that the OTHER buffer -
        /// the one that had nothing wrong with it - comes back unwritten. Checking the output is what
        /// distinguishes "the dispatch ran" from "the dispatch was silently discarded".
        /// </remarks>
        [TestMethod]
        public async Task EmptyViewCanBeBoundToAKernelTest() => await RunTest(async accelerator =>
        {
            const int count = 64;

            using var empty = accelerator.Allocate1D<int>(0);
            using var output = accelerator.Allocate1D<int>(count);

            if (empty.View.Length != 0)
                throw new Exception($"the empty allocation reports Length {empty.View.Length}, expected 0 - "
                                  + "the 4-byte floor is an ALLOCATION detail and must not change the view");

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                EmptyView_TouchKernel);
            kernel(count, empty.View, output.View);

            await accelerator.SynchronizeAsync();
            var got = await output.View.CopyToHostAsync();

            for (int i = 0; i < count; i++)
                if (got[i] != i + 1)
                    throw new Exception($"output[{i}] = {got[i]}, expected {i + 1} - the dispatch did not "
                                      + "take effect, which is what an invalidated CommandBuffer looks "
                                      + "like from the outside");
        });
    }
}
