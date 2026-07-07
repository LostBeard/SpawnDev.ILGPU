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
        // JS-side timing of the most recent replay() call on this page: encodeMs = the re-encode
        // loop (createCommandEncoder .. last op), submitMs = enc.finish() + queue.submit(). GPU
        // EXECUTION is not included - it completes asynchronously after submit (await
        // onSubmittedWorkDone / SynchronizeAsync for that). Reading performance.now() is ~free,
        // so this records unconditionally; .NET fetches it on demand only.
        last: { ops: 0, encodeMs: 0, submitMs: 0 },
        // Rewrite the dstOffset (slot [i*7+4]) of copy entries in place - the patch surface for
        // parameterized replay (e.g. a KV-cache append whose destination row advances per decode
        // token). Entries must be tag-1 copies; throws otherwise (a wrong index would silently
        // corrupt a dispatch record).
        patchCopyDst(plan, entryIndices, newDstOffsets) {
            for (let k = 0; k < entryIndices.length; k++) {
                const i = entryIndices[k] * 7;
                if (plan[i] !== 1) throw new Error(`patchCopyDst: entry ${entryIndices[k]} is tag ${plan[i]}, not a copy`);
                plan[i + 4] = newDstOffsets[k];
            }
        },
        // Replays a recorded plan on the given device: one encoder, one pass per dispatch
        // (pass-per-dispatch keeps storage-buffer write->read ordering guarantees airtight),
        // copies/clears encoded inline in captured order, one queue submit.
        // Returns the number of operations encoded.
        replay(device, plan) {
            const t0 = performance.now();
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
            const t1 = performance.now();
            device.queue.submit([enc.finish()]);
            const t2 = performance.now();
            api.last.ops = n / 7;
            api.last.encodeMs = t1 - t0;
            api.last.submitMs = t2 - t1;
            return n / 7;
        },
        // Replays the plan with per-pass GPU timestamps and returns a JSON string aggregating GPU
        // time by pipeline label (the kernel name). Requires the device to have 'timestamp-query'
        // (requested by ILGPU device init when the adapter supports it) - returns
        // {"supported":false} otherwise. One timestamp at the START of each compute pass plus the
        // END of the last pass: passes execute back-to-back on the queue, so t[k+1]-t[k] is pass
        // k's duration (encoder-level copies/clears between passes are attributed to the preceding
        // pass - negligible). Chunked across query sets (4096 timestamps max each). NOTE: Chrome
        // quantizes timestamps to 100us unless --enable-webgpu-developer-features - the total
        // (last-first) telescopes exactly either way, but fine per-kernel attribution wants the
        // flag. Waits for GPU completion internally (the resolve readback maps after the work).
        async replayTimed(device, plan) {
            if (!device.features.has('timestamp-query'))
                return JSON.stringify({ supported: false, reason: "device lacks timestamp-query" });
            const n = plan.length;
            const passIdx = [];                       // plan record offset of each compute pass
            for (let i = 0; i < n; i += 7) if (plan[i] === 0) passIdx.push(i);
            const passes = passIdx.length;
            if (passes === 0)
                return JSON.stringify({ supported: false, reason: "plan has no compute passes" });
            const QS_CAP = 4096;
            const stamps = passes + 1;
            const querySets = [];
            for (let remaining = stamps; remaining > 0; remaining -= QS_CAP)
                querySets.push(device.createQuerySet({ type: 'timestamp', count: Math.min(remaining, QS_CAP) }));
            const qsOf = k => querySets[Math.floor(k / QS_CAP)];
            const qiOf = k => k % QS_CAP;

            const enc = device.createCommandEncoder();
            // The last pass also writes the closing timestamp (its end). timestampWrites targets ONE
            // query set, so if that end index would land in the next chunk (passes ≡ 0 mod 4096),
            // skip it - the last pass then simply has no duration row (correct, just one row short).
            const hasClosingStamp = Math.floor(passes / QS_CAP) === Math.floor((passes - 1) / QS_CAP);
            const measuredPasses = hasClosingStamp ? passes : passes - 1;
            let k = 0;
            for (let i = 0; i < n; i += 7) {
                const tag = plan[i];
                if (tag === 0) {
                    const tw = { querySet: qsOf(k), beginningOfPassWriteIndex: qiOf(k) };
                    if (k === passes - 1 && hasClosingStamp)
                        tw.endOfPassWriteIndex = qiOf(k + 1);
                    const pass = enc.beginComputePass({ timestampWrites: tw });
                    pass.setPipeline(plan[i + 1]);
                    pass.setBindGroup(0, plan[i + 2]);
                    pass.dispatchWorkgroups(plan[i + 3], plan[i + 4], plan[i + 5]);
                    pass.end();
                    k++;
                } else if (tag === 1) {
                    enc.copyBufferToBuffer(plan[i + 1], plan[i + 2], plan[i + 3], plan[i + 4], plan[i + 5]);
                } else if (tag === 2) {
                    enc.clearBuffer(plan[i + 1], plan[i + 2], plan[i + 3]);
                }
            }
            // Resolve every query set into one buffer, then copy to a mappable readback buffer.
            const resolveBuf = device.createBuffer({ size: stamps * 8, usage: GPUBufferUsage.QUERY_RESOLVE | GPUBufferUsage.COPY_SRC });
            const readBuf = device.createBuffer({ size: stamps * 8, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
            for (let s = 0, ofs = 0; s < querySets.length; s++) {
                enc.resolveQuerySet(querySets[s], 0, querySets[s].count, resolveBuf, ofs);
                ofs += querySets[s].count * 8;
            }
            enc.copyBufferToBuffer(resolveBuf, 0, readBuf, 0, stamps * 8);
            device.queue.submit([enc.finish()]);
            await readBuf.mapAsync(GPUMapMode.READ);
            const t = new BigUint64Array(readBuf.getMappedRange().slice(0));
            readBuf.unmap();
            readBuf.destroy(); resolveBuf.destroy();
            for (const qs of querySets) qs.destroy();

            // Aggregate pass durations by pipeline label.
            const byLabel = new Map();
            let totalNs = 0;
            for (let p = 0; p < measuredPasses; p++) {
                const durNs = Number(t[p + 1] - t[p]);
                if (!(durNs >= 0)) continue;          // guard against wrap/invalid
                totalNs += durNs;
                const label = plan[passIdx[p] + 1].label || '(unlabeled)';
                const e = byLabel.get(label) || { ms: 0, count: 0, maxMs: 0 };
                e.ms += durNs / 1e6; e.count++; e.maxMs = Math.max(e.maxMs, durNs / 1e6);
                byLabel.set(label, e);
            }
            const kernels = [...byLabel.entries()]
                .map(([label, e]) => ({ label, ms: +e.ms.toFixed(3), count: e.count, maxMs: +e.maxMs.toFixed(3) }))
                .sort((a, b) => b.ms - a.ms);
            return JSON.stringify({
                supported: true, passes, ops: n / 7,
                totalMs: +(totalNs / 1e6).toFixed(3),
                spanMs: +(Number(t[measuredPasses] - t[0]) / 1e6).toFixed(3),
                kernels
            });
        }
    };
    globalThis.ilgpuWebGPUPlan = api;
})();
