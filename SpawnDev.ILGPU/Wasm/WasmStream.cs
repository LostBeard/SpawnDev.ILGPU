// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmStream.cs
//
// A no-op accelerator stream for the Wasm backend.
// In the single-threaded Blazor WASM environment, all operations are synchronous
// on the main thread, so the stream requires no synchronization logic.
// ---------------------------------------------------------------------------------------

using global::ILGPU.Runtime;

namespace SpawnDev.ILGPU.Wasm
{
    /// <summary>
    /// Represents a no-op accelerator stream for the Wasm backend.
    /// </summary>
    sealed class WasmStream : AcceleratorStream
    {
        /// <summary>
        /// Constructs a new Wasm stream.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        internal WasmStream(Accelerator accelerator)
            : base(accelerator)
        { }

        /// <summary>
        /// Cleans up completed tasks and surfaces errors. Cannot block-wait in single-threaded WASM.
        /// </summary>
        public override void Synchronize() => Accelerator.Synchronize();

        /// <summary>
        /// Real async drain. The synchronous <see cref="Synchronize"/> cannot block on
        /// the single Blazor thread, so it only reaps completed dispatch tasks; this
        /// awaits all in-flight worker kernels via the accelerator's pending-work set.
        /// </summary>
        public override System.Threading.Tasks.Task SynchronizeAsync() =>
            ((WasmAccelerator)Accelerator).SynchronizeAsync();

        /// <inheritdoc/>
        protected override ProfilingMarker AddProfilingMarkerInternal() =>
            throw new System.NotSupportedException(
                "Profiling markers are not supported in Wasm backend.");

        /// <summary>
        /// Does not perform any operation.
        /// </summary>
        protected override void DisposeAcceleratorObject(bool disposing) { }
    }
}
