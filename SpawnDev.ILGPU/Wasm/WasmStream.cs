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
        /// Synchronous Synchronize() is desktop-only — throws on Wasm. The single Blazor thread cannot
        /// block-wait on in-flight worker kernels; use <see cref="SynchronizeAsync"/> (the real drain).
        /// </summary>
        public override void Synchronize() =>
            throw new System.NotSupportedException(
                "Synchronous Synchronize() is desktop-only on Wasm; use `await SynchronizeAsync()`.");

        /// <summary>
        /// Flush (submit) is fire-and-forget and valid synchronously on Wasm: dispatch is already
        /// fire-and-forget worker tasks with nothing batched to submit, so this is a genuine no-op.
        /// (Submit is honest on browser; only the WAIT — <see cref="Synchronize"/> — is async-only.)
        /// </summary>
        public override void Flush() { }

        /// <summary>Async submit — no-op on Wasm (nothing batched). Matches sync <see cref="Flush"/>.</summary>
        public override System.Threading.Tasks.Task FlushAsync() =>
            System.Threading.Tasks.Task.CompletedTask;

        /// <summary>
        /// Host-&gt;device upload is consumed synchronously (SharedArrayBuffer memcpy), so the sync
        /// CopyFromCPU completion is a no-op on Wasm — nothing in flight to wait for.
        /// </summary>
        protected override void EnsureHostCopyConsumed() { }

        /// <summary>
        /// Real async drain. The synchronous <see cref="Synchronize"/> is desktop-only (throws on
        /// Wasm); this awaits all in-flight worker kernels via the accelerator's pending-work set.
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
