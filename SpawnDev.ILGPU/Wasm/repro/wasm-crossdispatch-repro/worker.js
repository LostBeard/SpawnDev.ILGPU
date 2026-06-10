// Cross-dispatch SAB-visibility micro-repro WORKER.
// Mirrors the SpawnDev.ILGPU Wasm dispatch handoff: a worker writes a shared region
// NON-ATOMICALLY (like Kernel1 writing counter[]), posts {done} to the main thread with NO
// release fence, and a DIFFERENT worker later reads it (like the scan/scatter dispatch). The
// question: does the postMessage handoff carry happens-before for those non-atomic SAB writes?
let u32 = null;       // non-atomic view
let idx = -1;         // this worker's index (for logging)

self.onmessage = (e) => {
  const m = e.data;
  if (m.cmd === 'init') {
    u32 = new Uint32Array(m.sab);   // plain (non-atomic) typed-array view over the SharedArrayBuffer
    idx = m.idx;
    self.postMessage({ ready: true, idx });
    return;
  }
  if (m.cmd === 'write') {
    // NON-ATOMIC stores, exactly like a Wasm kernel writing global memory (counter[]).
    // No Atomics, no fence. Fill [base, base+count) with the fresh epoch value.
    const { base, count, epoch } = m;
    for (let i = 0; i < count; i++) u32[base + i] = epoch;
    // Post done to MAIN with NO release fence — this is the production handoff (~WasmAccelerator:2093).
    self.postMessage({ done: true, idx });
    return;
  }
  if (m.cmd === 'read') {
    // Read the region a DIFFERENT worker just wrote. Count slots that DON'T show the fresh epoch
    // (a stale slot = this worker did not observe the writer's non-atomic store across the handoff).
    const { base, count, epoch } = m;
    let stale = 0, firstBad = -1, firstBadVal = 0, lastBad = -1;
    for (let i = 0; i < count; i++) {
      const v = u32[base + i];
      if (v !== epoch) {
        if (stale === 0) { firstBad = i; firstBadVal = v; }
        lastBad = i;
        stale++;
      }
    }
    self.postMessage({ readResult: true, idx, stale, firstBad, firstBadVal, lastBad, epoch });
    return;
  }
};
