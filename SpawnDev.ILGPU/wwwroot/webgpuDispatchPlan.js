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
    // Bind-group cache generation size (per device). Two generations: a lookup that misses the current one but hits the
    // previous one promotes the group; when the current generation passes this size it becomes the previous one and the
    // old previous one is dropped. So a group used at least once per generation is never dropped, while groups of a
    // shape no longer in use age out. MEASURED 2026-10-02 (Anaglyphohol DAv3 video): one input shape is ~2.8k distinct
    // groups, but the adaptive depth level cycles through many shapes (55k groups in all) - a single map cleared at a
    // cap kept dropping the hot shape (20% hits); with generations the hot shapes stay.
    const BG_GEN_MAX = 8192;
    const api = {
        // JS-side timing of the most recent replay() call on this page: encodeMs = the re-encode
        // loop (createCommandEncoder .. last op), submitMs = enc.finish() + queue.submit(). GPU
        // EXECUTION is not included - it completes asynchronously after submit (await
        // onSubmittedWorkDone / SynchronizeAsync for that). Reading performance.now() is ~free,
        // so this records unconditionally; .NET fetches it on demand only.
        last: { ops: 0, encodeMs: 0, submitMs: 0 },
        // Per-device staging buffer for submitBatch's ordered host uploads (tag 4). Grows, never shrinks.
        _staging: new WeakMap(),
        // Per-device bind-group cache for submitBatch (api.bindGroupReuse): key = layout id + every entry's
        // (binding, buffer id, offset, size) -> GPUBindGroup, in two generations of up to BG_GEN_MAX (see there), so a
        // workload whose buffers churn costs a bounded amount of memory. bgHits/bgMisses are cumulative counters.
        _bindGroups: new WeakMap(),
        bgHits: 0,
        bgMisses: 0,
        bgDrops: 0,      // generations started (first use per device, then one per rotation)
        bgLastKey: '',   // the most recent miss's key (diagnostic)
        // ── Interop cost probes ─────────────────────────────────────────────────────────────────
        // These do NOTHING on purpose. device.createBindGroup measured 1.14 ms per call in a Kokoro
        // pass (2,117 ms of 2,128 ms of the whole bind-group phase), and "1.14 ms" has three possible
        // owners with three different fixes: the .NET->JS crossing itself, MARSHALLING the descriptor
        // (its members are walked and rebuilt as a JS object per call - a layout reference, an entries
        // array, and a nested resource object per entry - so the cost scales with members), or Dawn's
        // own validation. A no-op crossing brackets the first; a no-op crossing that still marshals the
        // descriptor brackets the first two; whatever the real call costs beyond that is Dawn's.
        // noopDescriptor touches .entries.length so the marshalled object cannot be optimised away.
        noop(x) { return x | 0; },
        // WebGPUBackend.BatchSinglePass -> submitBatch (see there). Set from C# when the switch changes.
        setSinglePass(v) { api.singlePass = !!v; return 0; },
        // WebGPUBackend.BatchBindGroupReuse -> submitBatch's bind-group cache. Turning it off drops every cached group.
        setBindGroupReuse(v) { api.bindGroupReuse = !!v; if (!api.bindGroupReuse) api._bindGroups = new WeakMap(); return 0; },
        // DIAGNOSTIC ABLATION (WebGPUBackend.DiagSubmitAblation; results are garbage while set): 2 = skip createBindGroup
        // + the dispatch encode, 3 = return without doing anything. (A "skip only the scalar writes" mode ran every
        // kernel on stale loop bounds and HUNG the GPU - DXGI_ERROR_DEVICE_HUNG; 2 vs 3 measures the writes safely.)
        setAblation(v) { api.ablation = v | 0; return 0; },
        noopDescriptor(desc) { return desc && desc.entries ? desc.entries.length : 0; },

        // The SpawnJS instance whose wasm heap submitHeader reads (C# passes SpawnJSRuntime.DotnetInstance.Id once).
        // getHeap() returns the CURRENT heap buffer - it re-fetches it after memory growth detached the old one.
        setHeapSource(dotnetId) { api._heapInst = globalThis.SpawnJSInterop.getInstace(dotnetId); return 0; },
        // The batch submit's entry from WebGPUStream.SubmitBatch: ONE number crosses - the heap address of a pinned
        // Float64 header - and everything else is read from the wasm heap here:
        //   [0] device id  [1] records addr  [2] record count  [3] data addr  [4] data byte length
        //   [5] maxPassesPerSubmit  [6] upload bytes  [7..10] arenaR id, bytes, arenaW id, bytes
        //   [11] single pass (0/1)  [12] ablation  [13] bind-group reuse (0/1)
        // The records and data arrays are pinned C# arrays (never moved), so views over the heap at their
        // addresses ARE the arrays - nothing is copied and no HeapView object is created per submit.
        submitHeader(hdrAddr) {
            const heap = api._heapInst.getHeap();
            const h = new Float64Array(heap, hdrAddr, 14);
            api.singlePass = h[11] !== 0;
            api.ablation = h[12] | 0;
            const reuse = h[13] !== 0;
            if (reuse !== (api.bindGroupReuse !== false)) api.setBindGroupReuse(reuse);
            const device = globalThis.SpawnJSInterop.spawnJSObjects[h[0]];
            if (!device) throw new Error(`ilgpuWebGPUPlan.submitHeader: device (SpawnJS id ${h[0]}) is not held`);
            const n = h[2];
            const rec = new Float64Array(heap, h[1], n);
            const data = new Uint8Array(heap, h[3], h[4]);
            return api.submitBatch(device, rec, n, data, h[5], h[6], h[7], h[8], h[9], h[10]);
        },

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
        // Submits the accelerator's PLAIN (uncaptured) dispatch batch: WebGPUStream appends numeric
        // records while kernels are dispatched and hands the whole batch over in ONE crossing here, where
        // the bind groups are created and every op is encoded. Every GPU object is named by its SpawnJS
        // hold id and resolved from SpawnJSInterop.spawnJSObjects, so no descriptor is ever marshalled -
        // SpawnJS builds a nested .NET object one property Set per member, ~40 crossings for a bind-group
        // descriptor, which is what made createBindGroup the largest host cost of a graph forward.
        // Records (Float64Array `rec`, first `n` values):
        //   [0, pipelineId, layoutId, nEntries, x, y, z, (binding, bufferId, offset, size) * nEntries]
        //   [1, srcId, srcOffset, dstId, dstOffset, size]     copyBufferToBuffer
        //   [2, bufferId, offset, size]                       clearBuffer
        //   [3, bufferId, bufferOffset, dataOffset, size]     queue.writeBuffer from `data` (Uint8Array)
        //   [4, bufferId, bufferOffset, dataOffset, size]     ORDERED host upload: copyBufferToBuffer from the
        //                                                     staging copy of `data`, encoded in record order
        // A tag-3 write is issued when it is reached, so it precedes the submit of the command buffer holding
        // the dispatch that reads it - the same order as the per-dispatch path (write now, submit later). It
        // is only for buffers the batch alone uses (per-dispatch scalar/stride/lock buffers).
        // A tag-4 upload targets an ordinary buffer that earlier records in the batch may still read, so it
        // must land exactly where it sits in the batch: `data` is written ONCE into a persistent staging
        // buffer and each upload is a copyBufferToBuffer in record order. Reusing the staging buffer across
        // batches is safe because queue.writeBuffer executes after all previously submitted work.
        // Command buffers are split every maxPassesPerSubmit dispatches (see replay() for why).
        // SCALAR ARENAS (WebGPUBackend.BatchScalarArena): the batch's per-dispatch scalars are 256-byte slots of two
        // arena buffers (arenaRId: read-only bindings, arenaWId: read_write struct scalars; 0 = unused). Their used
        // prefixes are the LAST arenaRBytes + arenaWBytes bytes of `data` and are written FIRST, before any command
        // buffer of this batch is submitted - every slot is used once per batch, so one write per arena is exact.
        // BIND-GROUP REUSE (api.bindGroupReuse): a bind group is immutable and SpawnJS hold ids are never reused, so
        // two dispatch records with equal layout + entries can share one; with the arenas the keys repeat every frame.
        // ONE COMPUTE PASS FOR A RUN OF DISPATCHES (api.singlePass, set from WebGPUBackend.BatchSinglePass - default
        // true): consecutive dispatch records share an open pass; it is ended before an encoder-level command (copy,
        // clear, upload) and at every submit, and setPipeline is skipped when the pipeline repeats. Ordering is the
        // spec's, not ours: every dispatch in a compute pass is its OWN usage scope, so a storage write by one dispatch
        // is visible to the next exactly as across passes - the implementation inserts the barrier. What a pass per
        // dispatch added was a beginComputePass/end pair (and a fresh pass state) for each of ~800 dispatches a frame.
        // Returns the number of records processed.
        submitBatch(device, rec, n, data, maxPassesPerSubmit, uploadBytes, arenaRId, arenaRBytes, arenaWId, arenaWBytes) {
            const ablation = api.ablation | 0;
            if (ablation === 3) return 0;
            const objs = globalThis.SpawnJSInterop.spawnJSObjects;
            const obj = (id, what, at) => {
                const o = objs[id];
                if (o === undefined || o === null)
                    throw new Error(`ilgpuWebGPUPlan.submitBatch: ${what} (SpawnJS id ${id}) at record ${at} is not held - it was disposed while a dispatch still referenced it`);
                return o;
            };
            const cap = (maxPassesPerSubmit > 0) ? (maxPassesPerSubmit | 0) : 0x7fffffff;
            const queue = device.queue;
            let staging = null;
            if (uploadBytes > 0) {
                staging = api._staging.get(device);
                if (!staging || staging.size < uploadBytes) {
                    if (staging) staging.destroy();   // its last readers were submitted; destroy waits for them
                    let size = 65536;
                    while (size < uploadBytes) size *= 2;
                    staging = device.createBuffer({ label: 'ilgpu-batch-upload-staging', size, usage: GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST });
                    api._staging.set(device, staging);
                }
                queue.writeBuffer(staging, 0, data, 0, uploadBytes);
            }
            const arenaOff = data.length - (arenaRBytes | 0) - (arenaWBytes | 0);
            if (arenaRBytes > 0) queue.writeBuffer(obj(arenaRId, 'scalar arena', -1), 0, data, arenaOff, arenaRBytes);
            if (arenaWBytes > 0) queue.writeBuffer(obj(arenaWId, 'scalar arena (read_write)', -1), 0, data, arenaOff + arenaRBytes, arenaWBytes);
            let bgCache = null;
            if (api.bindGroupReuse !== false) {
                bgCache = api._bindGroups.get(device);
                if (!bgCache) { bgCache = { cur: new Map(), prev: new Map() }; api._bindGroups.set(device, bgCache); api.bgDrops++; }
                else if (bgCache.cur.size > BG_GEN_MAX) { bgCache.prev = bgCache.cur; bgCache.cur = new Map(); api.bgDrops++; }
            }
            const single = api.singlePass !== false;
            let enc = null, passes = 0, ops = 0;
            let pass = null, passPipeline = null;
            const endPass = () => { if (pass !== null) { pass.end(); pass = null; passPipeline = null; } };
            for (let i = 0; i < n; ops++) {
                const tag = rec[i];
                if (tag === 0 && ablation === 2) {
                    i += 7 + 4 * rec[i + 3];
                } else if (tag === 0) {
                    const ne = rec[i + 3];
                    const j0 = i + 7, jEnd = j0 + 4 * ne;
                    let bindGroup, key;
                    if (bgCache !== null) {
                        key = '' + rec[i + 2];
                        for (let j = j0; j < jEnd; j++) key += ',' + rec[j];
                        bindGroup = bgCache.cur.get(key);
                        if (bindGroup === undefined) {
                            bindGroup = bgCache.prev.get(key);
                            if (bindGroup !== undefined) bgCache.cur.set(key, bindGroup);
                        }
                    }
                    if (bindGroup === undefined) {
                        const entries = new Array(ne);
                        for (let e = 0, j = j0; e < ne; e++, j += 4)
                            entries[e] = { binding: rec[j], resource: { buffer: obj(rec[j + 1], 'buffer', i), offset: rec[j + 2], size: rec[j + 3] } };
                        bindGroup = device.createBindGroup({ layout: obj(rec[i + 2], 'bind group layout', i), entries });
                        if (bgCache !== null) { bgCache.cur.set(key, bindGroup); api.bgMisses++; api.bgLastKey = key; }
                    } else api.bgHits++;
                    const j = jEnd;
                    if (enc === null) enc = device.createCommandEncoder();
                    const pipeline = obj(rec[i + 1], 'pipeline', i);
                    if (single) {
                        if (pass === null) pass = enc.beginComputePass();
                        if (pipeline !== passPipeline) { pass.setPipeline(pipeline); passPipeline = pipeline; }
                        pass.setBindGroup(0, bindGroup);
                        pass.dispatchWorkgroups(rec[i + 4], rec[i + 5], rec[i + 6]);
                    } else {
                        const p1 = enc.beginComputePass();
                        p1.setPipeline(pipeline);
                        p1.setBindGroup(0, bindGroup);
                        p1.dispatchWorkgroups(rec[i + 4], rec[i + 5], rec[i + 6]);
                        p1.end();
                    }
                    i = j;
                    if (++passes >= cap) { endPass(); queue.submit([enc.finish()]); enc = null; passes = 0; }
                } else if (tag === 1) {
                    endPass();
                    if (enc === null) enc = device.createCommandEncoder();
                    enc.copyBufferToBuffer(obj(rec[i + 1], 'copy source', i), rec[i + 2], obj(rec[i + 3], 'copy destination', i), rec[i + 4], rec[i + 5]);
                    i += 6;
                } else if (tag === 2) {
                    endPass();
                    if (enc === null) enc = device.createCommandEncoder();
                    enc.clearBuffer(obj(rec[i + 1], 'clear target', i), rec[i + 2], rec[i + 3]);
                    i += 4;
                } else if (tag === 3) {
                    queue.writeBuffer(obj(rec[i + 1], 'write target', i), rec[i + 2], data, rec[i + 3], rec[i + 4]);
                    i += 5;
                } else if (tag === 4) {
                    endPass();
                    if (enc === null) enc = device.createCommandEncoder();
                    enc.copyBufferToBuffer(staging, rec[i + 3], obj(rec[i + 1], 'upload target', i), rec[i + 2], rec[i + 4]);
                    i += 5;
                } else {
                    throw new Error(`ilgpuWebGPUPlan.submitBatch: bad record tag ${tag} at ${i}`);
                }
            }
            endPass();
            if (enc !== null) queue.submit([enc.finish()]);
            return ops;
        },

        // Replays a recorded plan on the given device: one pass per dispatch (pass-per-dispatch keeps
        // storage-buffer write->read ordering guarantees airtight), copies/clears encoded inline in
        // captured order, submitted in BATCHES of maxPassesPerSubmit compute passes.
        // Returns the number of operations encoded.
        //
        // 🔴 WHY THIS BATCHES INSTEAD OF SUBMITTING ONCE. It used to encode the whole plan into a
        // single command encoder and issue one queue.submit(). That is fine for a small plan and it
        // LOSES THE DEVICE for a large one: MEASURED 2026-09-15, Kokoro at input_ids[1,360] replayed
        // and Chrome came back "WebGPU device has been lost and cannot accept commands" - the GPU
        // watchdog killing a single command buffer that ran too long. A 35-token Kokoro plan is
        // already 3,155 dispatches; the 360-token one is several times that, in ONE buffer, with no
        // point at which the driver can preempt.
        //
        // The uncaptured path never had this problem because WebGPUStream flushes as it goes - the
        // library's own documented rule is "if dispatching many kernels (>50), call Flush() every
        // 16-32 dispatches". Capture replay was the one path that ignored it. Splitting into several
        // command buffers changes nothing about ordering: buffers submitted to the same queue execute
        // in submission order, and the implicit inter-pass synchronization is per-queue, not
        // per-buffer.
        replay(device, plan, maxPassesPerSubmit) {
            const t0 = performance.now();
            const cap = (maxPassesPerSubmit > 0) ? (maxPassesPerSubmit | 0) : 0x7fffffff;
            const n = plan.length;
            let enc = device.createCommandEncoder();
            let passesInBatch = 0;
            let submitMs = 0;
            const flush = () => {
                const s0 = performance.now();
                device.queue.submit([enc.finish()]);
                submitMs += performance.now() - s0;
                passesInBatch = 0;
            };
            for (let i = 0; i < n; i += 7) {
                const tag = plan[i];
                if (tag === 0) {
                    const pass = enc.beginComputePass();
                    pass.setPipeline(plan[i + 1]);
                    pass.setBindGroup(0, plan[i + 2]);
                    pass.dispatchWorkgroups(plan[i + 3], plan[i + 4], plan[i + 5]);
                    pass.end();
                    // Close the batch only BETWEEN operations, never inside a pass.
                    if (++passesInBatch >= cap) {
                        flush();
                        enc = device.createCommandEncoder();
                    }
                } else if (tag === 1) {
                    enc.copyBufferToBuffer(plan[i + 1], plan[i + 2], plan[i + 3], plan[i + 4], plan[i + 5]);
                } else if (tag === 2) {
                    enc.clearBuffer(plan[i + 1], plan[i + 2], plan[i + 3]);
                }
            }
            const t1 = performance.now();
            flush();                     // the tail (a no-op encoder submits an empty buffer, which is legal)
            api.last.ops = n / 7;
            api.last.encodeMs = (t1 - t0) - submitMs;
            api.last.submitMs = submitMs;
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
    // Register - but never downgrade. A second copy of this module (another URL, an old cached file) must not
    // replace a newer one already in use: the library looks up ilgpuWebGPUPlan.submitBatch on every flush.
    // Bump HELPER_VERSION whenever the api's surface changes.
    const HELPER_VERSION = 5;   // 2: submitBatch (plain-dispatch record batches, ordered uploads); 3: single-pass runs, setSinglePass; 4: scalar arenas, bind-group reuse; 5: submitHeader (pinned heap header)
    api.version = HELPER_VERSION;
    const existing = globalThis.ilgpuWebGPUPlan;
    if (!existing || !(existing.version >= HELPER_VERSION)) globalThis.ilgpuWebGPUPlan = api;
})();
