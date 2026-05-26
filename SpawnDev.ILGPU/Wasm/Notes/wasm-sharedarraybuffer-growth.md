Context: Wasm Multi-Worker RadixSort / Scan Broadcast Race Condition
Target Files: SpawnDev.ILGPU\Wasm\WasmAccelerator.cs and SpawnDev.ILGPU\SpawnDev.ILGPU\SpawnDev.ILGPU\WorkerPool.cs

1. The Core Mechanical Discovery
The statistical discipline sweeps eliminated the JS yield/resume path and Atomics.wait loop from the failure chain; the bug occurs entirely within the pure-spin WASM execution layer during Phase 2 (Group Scan + Broadcast). The ±1 block-displacement failure signature is triggered by asymmetric, reactive memory growth propagation lag across the worker pool under full-suite session churn.

2. The Architectural Gap
Host Side (WasmAccelerator.cs): When a dispatch requires more linear memory pages, the host synchronously issues a grow call, maps the new SharedArrayBuffer reference, clears its local cache tracking (_initializedWorkersByKernel.Clear()), and immediately proceeds to queue up the next kernel dispatch execution pipeline.

Worker Side (WorkerPool.cs): Worker threads are completely unaware that memory has grown until they finish any current spinning/parking lifecycle, pull the next incoming message off their JS event loop, and reactively notice that _lastMemoryBuffer !== d.memory.buffer to clear _instancesById and re-instantiate.

3. The Race Condition Mechanism
The host executes a Memory.grow() operation and immediately writes fresh dispatch/pointer data into the newly allocated virtual space near the edge of the expanded scanMemory boundary.

Due to OS thread descheduling, JIT compilation pauses, or simple browser-engine propagation delay, a worker thread wakes up or remains in a spinning execution phase without having processed its microtask message queue yet.

The lagging worker's active WebAssembly.Instance remains bound to the old, unexpanded memory limits. When it hits the Phase 2 Scan & Broadcast loop, it performs a stale read against the boundary address, causing an offset failure that manifests as a block displacement error.

4. Actionable Remediation Path
Verification: If the current mini-sweep harness v2 yields scanFail > 0, it confirms memory growth is the key catalyst.

Tactical Fix: Implement a synchronous, blocking Post-Grow Handshake inside WasmAccelerator.cs immediately after a successful grow operation. The host must dispatch an explicit memory-synchronization signal to all workers and halt further execution until every active worker has successfully processed the update, wiped its local instance cache, and returned a hard "Ready" confirmation acknowledging the new memory boundaries.


