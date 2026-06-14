// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                Strongly-Typed PostMessage DTOs for Wasm Worker Dispatch
//
// Replaces anonymous-object PostMessage payloads (per TJ's anti-pattern feedback 2026-04-26).
// Property names are lowercase to match the JS-side wasmWorker.js field reads (d.script,
// d.memory, etc.) without requiring [JsonPropertyName] attributes.
// ---------------------------------------------------------------------------------------

using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace SpawnDev.ILGPU.Wasm
{
    /// <summary>
    /// Message sent to a Wasm worker for a barrier-mode kernel dispatch.
    /// Worker reads d.script (the dispatcher worker script), d.wasmBytes (optional - only on
    /// first dispatch to that worker), d.memory (SharedArrayBuffer-backed WebAssembly.Memory),
    /// and the thread range (d.threadStart, d.threadEnd).
    /// </summary>
    public sealed class WasmBarrierDispatchMessage
    {
        /// <summary>The async function body sent as the worker script (BuildWasmWorkerScript output).</summary>
        public string script { get; init; } = "";
        /// <summary>Compiled Wasm module bytes; sent only on first dispatch of this kernel to this worker.</summary>
        public byte[]? wasmBytes { get; init; }
        /// <summary>Stable identifier for the kernel — used by the worker to look up its cached module/instance.
        /// Multi-kernel pipelines (ML inference) alternate kernels; each gets a distinct kernelId so workers
        /// can keep multiple compiled modules in their per-kernel cache and skip re-compile on every switch.
        /// 2026-05-04 root-cause fix for Data's StyleMosaic Wasm 10+ minute hang at rc.16.</summary>
        public int kernelId { get; init; }
        /// <summary>When true, the worker drops its entire per-kernel module/instance cache BEFORE
        /// handling this dispatch (then re-compiles from the wasmBytes carried here). Set by the host
        /// at a fresh accelerator's first dispatch once cumulative kernels cross
        /// WasmBackend.ModuleCacheFlushThreshold, to bound the persistent-worker _modulesById
        /// accumulation that drives late-lane memory pressure (Tuvok trace 2026-06-14).</summary>
        public bool clearModuleCache { get; init; }
        /// <summary>SharedArrayBuffer-backed WebAssembly.Memory shared across all workers.</summary>
        public JSObject memory { get; init; } = null!;
        /// <summary>Inclusive thread range start for this worker's fiber band.</summary>
        public int threadStart { get; init; }
        /// <summary>Exclusive thread range end for this worker's fiber band.</summary>
        public int threadEnd { get; init; }
        /// <summary>
        /// Per-worker 16-byte buffer for the dispatcher's spin-yield save/restore state.
        /// Layout: [0]=yieldFlag, [4]=savedG, [8]=savedPhase, [12]=savedGen.
        /// The worker script reads yieldFlag after each dispatcher call; if non-zero,
        /// it re-dispatches with resumeMode=1 after a microtask boundary.
        /// </summary>
        public int yieldStateAddr { get; init; }
    }

    /// <summary>
    /// Message sent to a Wasm worker for a non-barrier (flat) kernel dispatch.
    /// Worker reads d.script, d.wasmBytes (optional), d.memory, and the item range
    /// (d.startIdx, d.endIdx) plus per-worker scratch (d.myScratch).
    /// </summary>
    public sealed class WasmFlatDispatchMessage
    {
        /// <summary>The async function body sent as the worker script (BuildWasmWorkerScript output).</summary>
        public string script { get; init; } = "";
        /// <summary>Compiled Wasm module bytes; sent only on first dispatch of this kernel to this worker.</summary>
        public byte[]? wasmBytes { get; init; }
        /// <summary>Stable identifier for the kernel — see WasmBarrierDispatchMessage.kernelId.</summary>
        public int kernelId { get; init; }
        /// <summary>Drop the worker's module/instance cache before this dispatch — see
        /// WasmBarrierDispatchMessage.clearModuleCache.</summary>
        public bool clearModuleCache { get; init; }
        /// <summary>SharedArrayBuffer-backed WebAssembly.Memory shared across all workers.</summary>
        public JSObject memory { get; init; } = null!;
        /// <summary>Inclusive item index start for this worker's range.</summary>
        public int startIdx { get; init; }
        /// <summary>Exclusive item index end for this worker's range.</summary>
        public int endIdx { get; init; }
        /// <summary>Per-worker scratch base address (separate region per worker to prevent races).</summary>
        public int myScratch { get; init; }
    }

    /// <summary>
    /// Response posted back from a Wasm worker after a dispatch.
    /// done=true means the worker completed successfully. done=false means error
    /// (with error string set). diag is optional debug data captured by the worker.
    /// </summary>
    public sealed class WasmDispatchResponse
    {
        /// <summary>True if the worker completed dispatch successfully; false on trap or error.</summary>
        public bool done { get; init; }
        /// <summary>Error message if done=false; null otherwise.</summary>
        public string? error { get; init; }
        /// <summary>Optional debug data (e.g., memory snapshot from worker 0 on early dispatches).</summary>
        public int[]? diag { get; init; }
    }
}
