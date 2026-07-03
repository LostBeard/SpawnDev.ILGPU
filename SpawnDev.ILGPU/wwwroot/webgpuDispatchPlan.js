'use strict';
// SpawnDev.ILGPU WebGPU dispatch-plan replay helper (loaded on demand via dynamic import of
// _content/SpawnDev.ILGPU/webgpuDispatchPlan.js - the glWorker.js static-asset pattern).
//
// A dispatch plan is a flat JS array of 7-element tagged records:
//   [0, pipeline, bindGroup, x, y, z, 0]                      - compute dispatch
//   [1, srcBuffer, srcOffset, dstBuffer, dstOffset, size, 0]  - copyBufferToBuffer
//   [2, buffer, offset, size, 0, 0, 0]                        - clearBuffer (zero-fill)
// It is recorded ONCE by WebGPUDispatchPlan during a capture forward (the plan array holding the
// GPU objects is what keeps them alive - .NET-side wrapper disposal is irrelevant), then replayed
// here with a SINGLE .NET->JS interop crossing per forward: this loop re-encodes every operation
// into one command encoder in pure JS (microseconds per entry) and submits one command buffer.
// This is the browser twin of CUDA graph replay - WebGPU has no graph API, but the command encoder
// IS the graph recorder, and WebGPU guarantees ordering with implicit synchronization between
// passes/copies on the same queue.
(() => {
    const api = {
        // Replays a recorded plan on the given device: one encoder, one pass per dispatch
        // (pass-per-dispatch keeps storage-buffer write->read ordering guarantees airtight),
        // copies/clears encoded inline in captured order, one queue submit.
        // Returns the number of operations encoded.
        replay(device, plan) {
            const enc = device.createCommandEncoder();
            const n = plan.length;
            for (let i = 0; i < n; i += 7) {
                const tag = plan[i];
                if (tag === 0) {
                    const pass = enc.beginComputePass();
                    pass.setPipeline(plan[i + 1]);
                    pass.setBindGroup(0, plan[i + 2]);
                    pass.dispatchWorkgroups(plan[i + 3], plan[i + 4], plan[i + 5]);
                    pass.end();
                } else if (tag === 1) {
                    enc.copyBufferToBuffer(plan[i + 1], plan[i + 2], plan[i + 3], plan[i + 4], plan[i + 5]);
                } else if (tag === 2) {
                    enc.clearBuffer(plan[i + 1], plan[i + 2], plan[i + 3]);
                }
            }
            device.queue.submit([enc.finish()]);
            return n / 7;
        }
    };
    globalThis.ilgpuWebGPUPlan = api;
})();
