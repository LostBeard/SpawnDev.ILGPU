// Pure-Node A/B validation harness for ITEM 2 (Wasm radix no-boundaries single-value scan).
// Runs the REAL emitted in-kernel single-value GroupExtensions.ExclusiveScan exerciser
// (WasmInKernelScanEmit.InKernelExclusiveScanKernel - the same WasmGroupExtensions call
// RadixSortKernel1 uses for its per-group histogram scan) oversubscribed over
// worker_threads + SharedArrayBuffer, replicating WasmAccelerator's barrier dispatch.
//
// All-1 shared histogram => exclusive scan == Group.IdxX, so each group's 256-element
// output segment must read 0..255. Output is prefilled with -1 (slot-0's expected value
// is 0, which fresh zero memory would mask). ONE dispatch per round => fast at 48 workers.
//
// Usage: node run-inkernel-scan.mjs <variant: baseline|item2> [workers] [rounds]
//   baseline = committed ExclusiveScanWithBoundaries path (scanResults copy) - must FIRE
//   item2    = direct InclusiveScanImplementation view read              - must be 0
//
// Emit the kernels first (see README): dotnet run --project SpawnDev.ILGPU.DemoConsole -- inkernel-emit
//
// ⚠️ Oversubscribed runs peg cores. Announce + get the Captain's go before running.

import { readFileSync } from 'fs';
import { Worker, isMainThread, parentPort, workerData } from 'worker_threads';
import { fileURLToPath } from 'url';

const GROUPS = 64;            // 64 groups x 256 = 16384 outputs (matches the scan repro size)
const WASM_MAX_PAGES = 16384; // env.memory import declared max

const align = (x, a) => Math.ceil(x / a) * a;
const MATH_STUB = new Proxy({}, { get: () => () => 0 }); // int kernel never calls Math

// ---- layout: mirror of WasmAccelerator.RunKernelAsync (WasmAccelerator.cs ~819-899) for an
// explicitly-grouped barrier kernel with ONE dense int view. Metadata read LIVE from the
// variant's manifest.json - NEVER hardcode (the 2376-vs-2392 stride lesson).
function computeLayout(meta, N, W) {
    const groupSize = meta.GROUP_SIZE;
    const gridDimX = groupSize * GROUPS;            // total X extent; kernel derives Grid.DimX
    const outOff = 0;
    let totalMemoryBytes = N * 4;
    totalMemoryBytes += gridDimX * 8;               // grid-stride overshoot pad (groupSize>1)
    const scratchBase = align(totalMemoryBytes, 8);
    const scratchSize = meta.SCRATCH_PER_THREAD * groupSize; // barrier kernel: per-thread scratch
    const structRegionBase = align(scratchBase + scratchSize, 8); // totalStructBytes = 0
    const sharedMemBase = align(structRegionBase, 8);
    const afterShared = sharedMemBase + meta.SHARED_MEM;
    const barrierBase = align(afterShared, 4);
    const fenceSlot = barrierBase + meta.BARRIER_COUNT * 8;
    const yieldStateRegionBase = fenceSlot + 24;
    const totalWithBarriers = yieldStateRegionBase + 16 * Math.max(1, W);
    const pages = Math.max(1, Math.ceil(totalWithBarriers / 65536)) + 1;
    const zeroRegionSize = fenceSlot - sharedMemBase;
    return { outOff, scratchBase, sharedMemBase, barrierBase, fenceSlot,
             yieldStateRegionBase, zeroRegionSize, pages, gridDimX, groupSize };
}

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
            threadStart, threadEnd, GROUPS, L.groupSize, L.gridDimX, 1,
            L.scratchBase, L.SPT, L.sharedMemBase, L.barrierBase,
            0 /*dynSharedLen*/, L.zeroRegionSize, W, L.fenceSlot,
            yieldStateAddr, resumeMode, L.groupSize /*realGroupDimX*/, 1 /*realGroupDimY*/,
            L.outOff, N, 1, 0            // output view: offset,len,stride,stride2
        );
        const yieldFlag = Atomics.load(yMem32, yieldFlagIdx);
        if (yieldFlag === 0) break;
        if (++yieldIters >= MAX_YIELD_ITERS) { parentPort.postMessage({ err: 'MAX_YIELD_ITERS' }); return; }
        const savedGen = yMem32[yieldFlagIdx + 3];
        const waitGenIdx = (yieldFlag === 2) ? groupGenIdx : genIdx;
        if (process.env.NO_PARK === '1') {
            while (Atomics.load(yMem32, waitGenIdx) === savedGen) { /* spin */ }
        } else {
            Atomics.wait(yMem32, waitGenIdx, savedGen);
        }
        resumeMode = 1;
    }
    parentPort.postMessage({ ok: true, yields: yieldIters });
}

if (isMainThread) {
    const VARIANT = process.argv[2];
    if (VARIANT !== 'baseline' && VARIANT !== 'item2') {
        console.error('usage: node run-inkernel-scan.mjs <baseline|item2> [workers] [rounds]');
        process.exit(2);
    }
    const WORKERS = parseInt(process.argv[3] || '48');
    const ROUNDS = parseInt(process.argv[4] || '60');

    const manifest = JSON.parse(readFileSync(new URL(`./${VARIANT}/manifest.json`, import.meta.url), 'utf8'));
    const info = manifest.kernels[0].info;
    const infoNum = (key) => {
        const m = info.match(new RegExp(key + '=(\\d+)'));
        if (!m) throw new Error(`manifest info missing '${key}=' (info: ${info})`);
        return parseInt(m[1], 10);
    };
    const meta = {
        GROUP_SIZE: manifest.maxGroupSize,
        SHARED_MEM: infoNum('sharedMem'),
        BARRIER_COUNT: infoNum('barriers'),
        SCRATCH_PER_THREAD: Math.max(infoNum('scratchPerThread'), 64),
    };
    const wasmBytes = readFileSync(new URL(`./${VARIANT}/${manifest.kernels[0].wasm}`, import.meta.url));

    const N = meta.GROUP_SIZE * GROUPS;
    const L = computeLayout(meta, N, WORKERS);
    L.SPT = meta.SCRATCH_PER_THREAD;
    const fibersPerWorker = Math.ceil(meta.GROUP_SIZE / WORKERS);
    const thisFile = fileURLToPath(import.meta.url);

    console.log(`In-kernel ExclusiveScan A/B [${VARIANT}]: groups=${GROUPS} gs=${meta.GROUP_SIZE} N=${N}, workers=${WORKERS} (fibers/worker=${fibersPerWorker}), rounds=${ROUNDS}`);
    console.log(`  kernel: ${manifest.kernels[0].wasm} (${manifest.kernels[0].bytes}b) sharedMem=${meta.SHARED_MEM} barriers=${meta.BARRIER_COUNT} spt=${meta.SCRATCH_PER_THREAD}`);
    console.log(`  layout: pages=${L.pages}, scratchBase=${L.scratchBase}, sharedMemBase=${L.sharedMemBase}, fenceSlot=${L.fenceSlot}`);
    console.log('='.repeat(72));

    let totalBadRounds = 0, totalMismatch = 0, totalYields = 0;
    for (let round = 0; round < ROUNDS; round++) {
        const memory = new WebAssembly.Memory({ initial: L.pages, maximum: Math.min(WASM_MAX_PAGES, L.pages), shared: true });
        const i32 = new Int32Array(memory.buffer);
        i32.fill(-1, L.outOff >>> 2, (L.outOff >>> 2) + N); // -1 sentinel: slot-0 expects 0, zero-fill would mask it

        const workers = [], promises = [];
        for (let w = 0; w < WORKERS; w++) {
            const threadStart = w * fibersPerWorker;
            const threadEnd = Math.min(threadStart + fibersPerWorker, meta.GROUP_SIZE);
            const yieldStateAddr = L.yieldStateRegionBase + w * 16;
            const wk = new Worker(thisFile, { workerData: { wasmBytes, memory, threadStart, threadEnd, yieldStateAddr, L, N, W: WORKERS } });
            workers.push(wk);
            promises.push(new Promise((res, rej) => { wk.on('message', res); wk.on('error', rej); }));
        }
        const results = await Promise.all(promises);
        for (const wk of workers) wk.terminate();
        const err = results.find(r => r && r.err);
        if (err) { console.log(`  round ${round + 1}: WORKER ERROR ${err.err}`); totalBadRounds++; continue; }
        totalYields += results.reduce((a, r) => a + ((r && r.yields) || 0), 0);

        // verify: output[i] must equal (i % GROUP_SIZE) * v(group). Per-GROUP pseudorandom
        // scan input (mirrors the kernel's hash EXACTLY) - a stale one-publication-behind
        // slot holds slot * v(g-1), a visible delta that FINGERPRINTS the held group.
        const gv = g => 1 + ((Math.imul(g + 1, 2654435761) >>> 8) % 251);
        const outBase = L.outOff >>> 2;
        let mism = 0, firstBad = -1, firstGot = 0, firstExp = 0;
        const slots = new Set(), groups = new Set(), heldGroups = new Set();
        for (let i = 0; i < N; i++) {
            const g = Math.floor(i / meta.GROUP_SIZE), slot = i % meta.GROUP_SIZE;
            const exp = slot * gv(g);
            const got = i32[outBase + i];
            if (got !== exp) {
                if (mism === 0) { firstBad = i; firstGot = got; firstExp = exp; }
                slots.add(slot);
                groups.add(g);
                // fingerprint decode: which group's publication was the slot holding?
                let held = 'none';
                if (got === -1) held = 'INIT';
                else if (slot > 0 && got % slot === 0) {
                    const vh = got / slot;
                    for (let j = 0; j < GROUPS; j++) if (gv(j) === vh) { held = `g${j}`; break; }
                }
                heldGroups.add(held);
                mism++;
            }
        }
        if (mism > 0) {
            totalBadRounds++; totalMismatch += mism;
            console.log(`  round ${round + 1}: ${mism}/${N} wrong (first @${firstBad} [group ${Math.floor(firstBad / meta.GROUP_SIZE)} slot ${firstBad % meta.GROUP_SIZE}]: got ${firstGot}, expected ${firstExp}) ` +
                `distinctSlots=${slots.size}${slots.size <= 4 ? `[${[...slots].join(',')}]` : ''} ` +
                `groups=[${[...groups].sort((a, b) => a - b).join(',')}] held=[${[...heldGroups].join(',')}]`);
        }
    }
    const status = totalBadRounds === 0 ? 'PASS' : 'FAIL';
    console.log('='.repeat(72));
    console.log(`===INKERNEL-AB=== variant=${VARIANT} workers=${WORKERS} ${status}: ${totalBadRounds}/${ROUNDS} rounds bad, ${totalMismatch} total mismatches, ${totalYields} total JS-yields`);
}
