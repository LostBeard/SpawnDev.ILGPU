// Persistent-worker + memory-GROWTH repro of the real scan kernel. Tests the corpus's
// leading-but-unconfirmed "SharedArrayBuffer growth-lag" hypothesis
// (wasm-sharedarraybuffer-growth.md) on the actual SingleGroupScanKernel.
//
// Faithfully replicates the real WorkerPool.WasmBootstrapScript flow:
//   - persistent workers, message-driven (NOT fresh per dispatch)
//   - per-kernel module cache; re-compile when wasmBytes re-sent (host does this after a grow)
//   - re-instantiate when memory.buffer swaps (the grow detector: _lastMemoryBuffer !== memory.buffer)
//   - the spin-yield/park/resume dispatcher loop
// Host side: a sequence of scan dispatches with VARYING N so the layout grows and the
// shared WebAssembly.Memory must `grow()` between dispatches (re-sending wasmBytes, exactly
// like WasmAccelerator clearing _initializedWorkersByKernel). Oversubscribed.
//
// Usage: node run-persistent-scan.mjs [maxN] [workers] [dispatches]

import { readFileSync } from 'fs';
import { Worker, isMainThread, parentPort, workerData } from 'worker_threads';
import { fileURLToPath } from 'url';

// Kernel layout metadata: read LIVE from manifest.json (see run-real-scan.mjs for why
// hardcoding these caused a 16-byte cross-thread fiber-scratch overlap after a re-emit).
const _manifest = JSON.parse(readFileSync(new URL('./manifest.json', import.meta.url), 'utf8'));
const _kernelInfo = _manifest.kernels[0].info;
const _infoNum = (key) => {
    const m = _kernelInfo.match(new RegExp(key + '=(\\d+)'));
    if (!m) throw new Error(`manifest.json kernel info is missing '${key}=' (info: ${_kernelInfo})`);
    return parseInt(m[1], 10);
};
const GROUP_SIZE = _manifest.maxGroupSize;
const SHARED_MEM = _infoNum('sharedMem');
const BARRIER_COUNT = _infoNum('barriers');
const SCRATCH_PER_THREAD = Math.max(_infoNum('scratchPerThread'), 64);
const WASM_MAX_PAGES = 16384;
const align = (x, a) => Math.ceil(x / a) * a;

function computeLayout(N, W) {
    const numGroups = 1, groupSize = GROUP_SIZE, gridDimX = groupSize * numGroups;
    const inOff = 0;
    const outOff = align(inOff + N * 4, 8);
    let totalMemoryBytes = outOff + N * 4 + gridDimX * 8;
    const scratchBase = align(totalMemoryBytes, 8);
    const scratchSize = SCRATCH_PER_THREAD * groupSize;
    const structRegionBase = align(scratchBase + scratchSize, 8);
    const sharedMemBase = align(structRegionBase, 8);
    const barrierBase = align(sharedMemBase + SHARED_MEM, 4);
    const fenceSlot = barrierBase + BARRIER_COUNT * 8;
    const yieldStateRegionBase = fenceSlot + 24;
    const totalWithBarriers = yieldStateRegionBase + 16 * Math.max(1, W);
    const pages = Math.max(1, Math.ceil(totalWithBarriers / 65536)) + 1;
    return { inOff, outOff, scratchBase, sharedMemBase, barrierBase, fenceSlot,
             yieldStateRegionBase, zeroRegionSize: fenceSlot - sharedMemBase, pages, numGroups, gridDimX, end: totalWithBarriers };
}

const MATH_STUB = new Proxy({}, { get: () => () => 0 });

if (!isMainThread) { workerMain(); }

function workerMain() {
    const { wasmBytes } = workerData;
    let module;
    try { module = new WebAssembly.Module(wasmBytes); }
    catch (ex) { parentPort.postMessage({ err: 'compile: ' + (ex && ex.message || ex) }); return; }
    let instance = null, lastBuffer = null, yMem32 = null;

    parentPort.on('message', (d) => {
        try {
            // grow detector: re-instantiate when the underlying buffer swapped (== a grow happened)
            if (lastBuffer !== d.memory.buffer) {
                lastBuffer = d.memory.buffer;
                instance = null;
                yMem32 = new Int32Array(d.memory.buffer);
            }
            if (!instance) {
                const notify = (addr, count) => Atomics.notify(new Int32Array(d.memory.buffer), addr >>> 2, count);
                instance = new WebAssembly.Instance(module, { env: { memory: d.memory, notify }, Math: MATH_STUB });
            }
            const dispatcher = instance.exports.dispatcher;
            const { threadStart, threadEnd, yieldStateAddr, L, N, W } = d;
            const yieldFlagIdx = yieldStateAddr >>> 2;
            const genIdx = (L.fenceSlot + 4) >>> 2, groupGenIdx = (L.fenceSlot + 20) >>> 2;
            let resumeMode = 0, yieldIters = 0;
            while (true) {
                dispatcher(threadStart, threadEnd, L.numGroups, GROUP_SIZE, L.gridDimX, 1,
                    L.scratchBase, SCRATCH_PER_THREAD, L.sharedMemBase, L.barrierBase,
                    0, L.zeroRegionSize, W, L.fenceSlot, yieldStateAddr, resumeMode, GROUP_SIZE, 1,
                    L.inOff, N, 1, 0, L.outOff, N, 1, 0);
                const yf = Atomics.load(yMem32, yieldFlagIdx);
                if (yf === 0) break;
                if (++yieldIters >= 4_000_000) { parentPort.postMessage({ err: 'MAX_YIELD' }); return; }
                const savedGen = yMem32[yieldFlagIdx + 3];
                Atomics.wait(yMem32, (yf === 2) ? groupGenIdx : genIdx, savedGen);
                resumeMode = 1;
            }
            parentPort.postMessage({ ok: true, yields: yieldIters });
        } catch (ex) { parentPort.postMessage({ err: String(ex && ex.message || ex) }); }
    });
}

if (isMainThread) {
    const MAXN = parseInt(process.argv[2] || '65536');
    const WORKERS = parseInt(process.argv[3] || '16');
    const DISPATCHES = parseInt(process.argv[4] || '200');
    const wasmBytes = readFileSync(new URL('./00_kernel_1.wasm', import.meta.url));
    const thisFile = fileURLToPath(import.meta.url);
    const fibersPerWorker = Math.ceil(GROUP_SIZE / WORKERS);

    // Allocate growable shared memory. Start SMALL so the first big dispatch must grow.
    let curPages = 4;
    const memory = new WebAssembly.Memory({ initial: curPages, maximum: WASM_MAX_PAGES, shared: true });

    // Persistent workers (created once, reused for every dispatch — like the real WorkerPool).
    const workers = [];
    for (let w = 0; w < WORKERS; w++) {
        const wk = new Worker(thisFile, { workerData: { wasmBytes } });
        wk.on('error', (e) => console.log(`  worker ${w} ERROR EVENT: ${e && e.message || e}`));
        workers.push(wk);
    }

    function dispatchOnce(N) {
        // grow if this N needs more pages than currently allocated (mirrors WasmAccelerator)
        const L = computeLayout(N, WORKERS);
        let grew = false;
        if (L.pages > curPages) { memory.grow(L.pages - curPages); curPages = L.pages; grew = true; }
        const i32 = new Int32Array(memory.buffer);
        // Fresh working region per dispatch (scratch/shared/barrier/fence/yield), like the
        // backend's scratch-zeroing — otherwise a shrink reuses a larger dispatch's stale
        // fence/arrival slots and the barrier deadlocks (harness artifact, not the bug).
        i32.fill(0, L.scratchBase >>> 2, L.end >>> 2);
        for (let i = 0; i < N; i++) i32[(L.inOff >>> 2) + i] = 1;     // input = 1s
        for (let i = 0; i < N; i++) i32[(L.outOff >>> 2) + i] = 0;    // clear output

        const promises = workers.map((wk, w) => new Promise((res) => {
            wk.once('message', res);
            wk.postMessage({ memory, threadStart: w * fibersPerWorker,
                threadEnd: Math.min((w + 1) * fibersPerWorker, GROUP_SIZE),
                yieldStateAddr: L.yieldStateRegionBase + w * 16, L, N, W: WORKERS });
        }));
        return { L, grew, done: Promise.all(promises) };
    }

    console.log(`Persistent+growth scan repro: maxN=${MAXN}, workers=${WORKERS}, dispatches=${DISPATCHES}, startPages=${curPages}`);
    console.log('='.repeat(72));

    let bad = 0, grows = 0, totalYields = 0;
    // Vary N each dispatch (small<->large) to force repeated grows + layout shifts + module re-instantiation.
    const sizes = [256, 1024, 8192, MAXN, 512, 32768, MAXN, 2048];
    for (let d = 0; d < DISPATCHES; d++) {
        const N = sizes[d % sizes.length];
        const { L, grew, done } = dispatchOnce(N);
        if (grew) grows++;
        const results = await done;
        const err = results.find(r => r && r.err);
        if (err) { console.log(`  dispatch ${d} (N=${N}): WORKER ERROR ${err.err}`); bad++; continue; }
        totalYields += results.reduce((a, r) => a + ((r && r.yields) || 0), 0);
        const i32 = new Int32Array(memory.buffer);
        let mism = 0, firstBad = -1, firstGot = 0;
        const outBase = L.outOff >>> 2;
        for (let i = 0; i < N; i++) if (i32[outBase + i] !== i + 1) { if (mism === 0) { firstBad = i; firstGot = i32[outBase + i]; } mism++; }
        if (mism > 0) { bad++; console.log(`  dispatch ${d} (N=${N}, grew=${grew}): ${mism}/${N} wrong (first @${firstBad}: got ${firstGot}, expected ${firstBad + 1})`); }
    }
    for (const wk of workers) wk.terminate();
    console.log('='.repeat(72));
    console.log(`${bad === 0 ? 'PASS' : 'FAIL'}: ${bad}/${DISPATCHES} dispatches bad, ${grows} grows, ${totalYields} total JS-yields`);
}
