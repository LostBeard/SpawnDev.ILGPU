// Pure-Node repro of the REAL generated single-group inclusive-scan kernel
// (SpawnDev.ILGPU SingleGroupScanKernel — the exact kernel RadixSort uses for its
// counter scan). Replicates WasmAccelerator's barrier dispatch (layout + per-worker
// fiber ranges + the spin-yield/resume loop) over Node worker_threads + a shared
// SharedArrayBuffer. No Chromium, no Blazor, no FO76 — controllable worker count.
//
// Goal: reproduce the residual large-multi-tile scan/broadcast race in a terminal so
// we can bisect it cheaply. Input = all 1s; inclusive scan => output[i] must == i+1.
//
// Usage: node run-real-scan.mjs [N] [workers] [rounds]
//   N       input element count (>256 => multiple tiles; the regime that fails)
//   workers worker count (oversubscribe past cores to manufacture descheduling)
//   rounds  repetitions per config
//
// Emit the kernel first:
//   dotnet run --project SpawnDev.ILGPU.DemoConsole -- scan-emit

import { readFileSync } from 'fs';
import { Worker, isMainThread, parentPort, workerData } from 'worker_threads';
import { fileURLToPath } from 'url';

// ---- kernel metadata (from scan-emit manifest: sharedMem=5120, barriers=8, scratchPerThread=2376) ----
const GROUP_SIZE = 256;
const SHARED_MEM = 5120;
const BARRIER_COUNT = 8;
const SCRATCH_PER_THREAD = Math.max(2376, 64);
const WASM_MAX_PAGES = 16384; // env.memory import declared max

const align = (x, a) => Math.ceil(x / a) * a;

// Mirror of WasmAccelerator.cs RunKernel layout math (lines ~840-913) for a
// single-group (gridDim=1, groupDim=256) barrier kernel with 2 dense int views.
function computeLayout(N, W) {
    const numGroups = 1, groupSize = GROUP_SIZE;
    const gridDimX = groupSize * numGroups; // total extent in X (NOT group count); kernel does Grid.DimX = dimX/realGroupDimX
    const inOff = 0;
    const outOff = align(inOff + N * 4, 8);
    let totalMemoryBytes = outOff + N * 4;
    totalMemoryBytes += gridDimX * 8;               // grid-stride overshoot pad (groupSize>1)
    const scratchBase = align(totalMemoryBytes, 8);
    const scratchSize = SCRATCH_PER_THREAD * groupSize; // barrier: per-thread scratch
    const structRegionBase = align(scratchBase + scratchSize, 8);
    const afterScratch = structRegionBase;          // totalStructBytes = 0
    const sharedMemBase = align(afterScratch, 8);
    const afterShared = sharedMemBase + SHARED_MEM;
    const barrierBase = align(afterShared, 4);
    const barrierSize = BARRIER_COUNT * 8;
    const fenceSlot = barrierBase + barrierSize;
    const yieldStateRegionBase = fenceSlot + 24;
    const yieldStateRegionSize = 16 * Math.max(1, W);
    const totalWithBarriers = yieldStateRegionBase + yieldStateRegionSize;
    const pages = Math.max(1, Math.ceil(totalWithBarriers / 65536)) + 1;
    const zeroRegionSize = fenceSlot - sharedMemBase;
    return { inOff, outOff, scratchBase, sharedMemBase, barrierBase, fenceSlot,
             yieldStateRegionBase, zeroRegionSize, pages, numGroups, gridDimX };
}

const MATH_STUB = new Proxy({}, { get: () => () => 0 }); // int scan never calls Math

if (!isMainThread) { workerMain(); }

function workerMain() {
    const { wasmBytes, memory, threadStart, threadEnd, yieldStateAddr, L, N, W } = workerData;
    const yMem32 = new Int32Array(memory.buffer);
    const notify = (addrBytes, count) => Atomics.notify(yMem32, addrBytes >>> 2, count);
    const module = new WebAssembly.Module(wasmBytes);
    const instance = new WebAssembly.Instance(module, { env: { memory, notify }, Math: MATH_STUB });
    const dispatcher = instance.exports.dispatcher;

    const yieldFlagIdx = yieldStateAddr >>> 2;
    const genIdx = (L.fenceSlot + 4) >>> 2;
    const groupGenIdx = (L.fenceSlot + 20) >>> 2;
    const MAX_YIELD_ITERS = 2_000_000;

    let resumeMode = 0, yieldIters = 0;
    while (true) {
        dispatcher(
            threadStart, threadEnd, L.numGroups, GROUP_SIZE, L.gridDimX, 1,
            L.scratchBase, SCRATCH_PER_THREAD, L.sharedMemBase, L.barrierBase,
            0 /*dynSharedLen*/, L.zeroRegionSize, W, L.fenceSlot,
            yieldStateAddr, resumeMode, GROUP_SIZE /*realGroupDimX*/, 1 /*realGroupDimY*/,
            L.inOff, N, 1, 0,            // input view: offset,len,stride,stride2
            L.outOff, N, 1, 0           // output view
        );
        const yieldFlag = Atomics.load(yMem32, yieldFlagIdx);
        if (yieldFlag === 0) break;
        if (++yieldIters >= MAX_YIELD_ITERS) { parentPort.postMessage({ err: 'MAX_YIELD_ITERS' }); return; }
        const savedGen = yMem32[yieldFlagIdx + 3];
        const waitGenIdx = (yieldFlag === 2) ? groupGenIdx : genIdx;
        if (process.env.NO_PARK === '1') {
            // PARK-DISABLED: busy-spin on the gen instead of Atomics.wait, to isolate whether the
            // residual race lives in the Atomics.wait park/resume ordering (V8) or our pure-spin
            // gen/state-save logic. Same seq_cst gen load, no wait/notify.
            while (Atomics.load(yMem32, waitGenIdx) === savedGen) { /* spin */ }
        } else {
            Atomics.wait(yMem32, waitGenIdx, savedGen);
        }
        resumeMode = 1;
    }
    parentPort.postMessage({ ok: true, yields: yieldIters });
}

if (isMainThread) {
    const N = parseInt(process.argv[2] || '4096');
    const WORKERS = parseInt(process.argv[3] || '8');
    const ROUNDS = parseInt(process.argv[4] || '20');
    const wasmBytes = readFileSync(new URL('./00_kernel_1.wasm', import.meta.url));
    const thisFile = fileURLToPath(import.meta.url);

    const L = computeLayout(N, WORKERS);
    const fibersPerWorker = Math.ceil(GROUP_SIZE / WORKERS);
    console.log(`Real scan repro: N=${N}, workers=${WORKERS} (fibers/worker=${fibersPerWorker}), rounds=${ROUNDS}`);
    console.log(`  layout: pages=${L.pages} (${L.pages * 64}KB), scratchBase=${L.scratchBase}, sharedMemBase=${L.sharedMemBase}, fenceSlot=${L.fenceSlot}, tiles=${Math.ceil(N / GROUP_SIZE)}`);
    console.log('='.repeat(72));

    // PER-TILE-DISTINCT input (the all-1s version CANNOT detect a tile-boundary race: every tile of
    // 1s sums to GROUP_SIZE, so a stale boundary read from the wrong tile gives the right value by
    // accident). Here each tile (GROUP_SIZE elements) carries a DIFFERENT value, so tile sums differ
    // and a stale boundary carry is a VISIBLE error — the real "contiguous run shifted by an offset"
    // signature (heavy-duplicate-key counter arrays). int32 inclusive-scan reference matches the
    // kernel's wrapping arithmetic.
    const input = new Int32Array(N);
    const ref = new Int32Array(N);
    let acc = 0;
    for (let i = 0; i < N; i++) {
        input[i] = 1 + ((Math.floor(i / GROUP_SIZE) % 251) | 0); // distinct-ish per tile, bounded
        acc = (acc + input[i]) | 0;                              // int32 inclusive prefix sum
        ref[i] = acc;
    }
    console.log(`  input: per-tile-distinct (tile values 1..251); ref[0]=${ref[0]}, ref[${N - 1}]=${ref[N - 1]}`);

    let totalBadRounds = 0, totalMismatch = 0, totalYields = 0;
    for (let round = 0; round < ROUNDS; round++) {
        const memory = new WebAssembly.Memory({ initial: L.pages, maximum: Math.min(WASM_MAX_PAGES, L.pages), shared: true });
        const i32 = new Int32Array(memory.buffer);
        const inBase = L.inOff >>> 2;
        i32.set(input, inBase);

        const workers = [], promises = [];
        for (let w = 0; w < WORKERS; w++) {
            const threadStart = w * fibersPerWorker;
            const threadEnd = Math.min(threadStart + fibersPerWorker, GROUP_SIZE);
            const yieldStateAddr = L.yieldStateRegionBase + w * 16;
            const wk = new Worker(thisFile, { workerData: { wasmBytes, memory, threadStart, threadEnd, yieldStateAddr, L, N, W: WORKERS } });
            workers.push(wk);
            promises.push(new Promise((res, rej) => { wk.on('message', res); wk.on('error', rej); }));
        }
        const results = await Promise.all(promises);
        for (const wk of workers) wk.terminate();
        const err = results.find(r => r && r.err);
        if (err) { console.log(`  round ${round + 1}: WORKER ERROR ${err.err}`); totalBadRounds++; continue; }
        const roundYields = results.reduce((a, r) => a + ((r && r.yields) || 0), 0);
        totalYields += roundYields;

        // verify against the int32 inclusive-scan reference of the per-tile-distinct input
        const outBase = L.outOff >>> 2;
        let mism = 0, firstBad = -1, firstGot = 0, firstExp = 0;
        for (let i = 0; i < N; i++) {
            if (i32[outBase + i] !== ref[i]) { if (mism === 0) { firstBad = i; firstGot = i32[outBase + i]; firstExp = ref[i]; } mism++; }
        }
        if (mism > 0) {
            totalBadRounds++; totalMismatch += mism;
            console.log(`  round ${round + 1}: ${mism}/${N} wrong (first @${firstBad} [tile ${Math.floor(firstBad / GROUP_SIZE)}]: got ${firstGot}, expected ${firstExp}, delta ${firstGot - firstExp})`);
        }
    }
    const status = totalBadRounds === 0 ? 'PASS' : 'FAIL';
    console.log('='.repeat(72));
    console.log(`${status}: ${totalBadRounds}/${ROUNDS} rounds bad, ${totalMismatch} total mismatches, ${totalYields} total JS-yields`);
}
