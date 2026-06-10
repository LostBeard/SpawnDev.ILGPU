// Pure-Node fire/clean gate for the PRODUCTION RadixSortKernel1 (pass 1: per-tile presort +
// counter histogram - the kernel the Wasm large-sort residual fires in). Drives the REAL
// emitted kernel_2 from a radix-emit dir oversubscribed over worker_threads + SAB,
// replicating WasmAccelerator's barrier dispatch.
//
// Protocol (Geordi GO 2026-06-10): radix-baseline (HEAD, scanResults copy present) must FIRE
// under oversubscription; the *WithBoundaries-fixed emit must go CLEAN.
//
// Per-tile reference: stable counting-sort partition by 2-bit radix + per-tile bucket counts.
// bits = ((v ^ 0x80000000) >> shift) & 3 (AscendingInt32.ExtractRadixBits, arithmetic shift).
// Input is pseudorandom per element so tile compositions differ - a stale cross-tile scan
// publication produces visible misplacement, not an accidental match.
//
// Usage: node run-radix-pass1.mjs <emitDir e.g. radix-baseline> [workers] [rounds]
//
// ⚠️ Oversubscribed runs peg cores. Announce + get the Captain's go before running.

import { readFileSync } from 'fs';
import { Worker, isMainThread, parentPort, workerData } from 'worker_threads';
import { fileURLToPath } from 'url';

const GRID_DIM = 4;            // dispatcher groups; in-kernel grid-stride covers the rest
const NUM_VIRTUAL_GROUPS = 64; // virtual tiles (gridIdx 0..63 via gridIdx += Grid.DimX)
const UNROLL = 4;              // Specialization4: 2-bit radix, 4 buckets
const SHIFT = 0;
const WASM_MAX_PAGES = 16384;

const align = (x, a) => Math.ceil(x / a) * a;
const MATH_STUB = new Proxy({}, { get: () => () => 0 });

function computeLayout(meta, N, W) {
    const groupSize = meta.GROUP_SIZE;
    const gridDimX = groupSize * GRID_DIM;          // total X extent (NOT group count)
    const viewOff = 0;
    const counterOff = align(N * 4, 8);
    let totalMemoryBytes = counterOff + UNROLL * NUM_VIRTUAL_GROUPS * 4;
    totalMemoryBytes += gridDimX * 8;               // grid-stride overshoot pad (groupSize>1)
    const scratchBase = align(totalMemoryBytes, 8);
    const scratchSize = meta.SCRATCH_PER_THREAD * groupSize;
    const structRegionBase = align(scratchBase + scratchSize, 8);
    const sharedMemBase = align(structRegionBase, 8);
    const afterShared = sharedMemBase + meta.SHARED_MEM;
    const barrierBase = align(afterShared, 4);
    const fenceSlot = barrierBase + meta.BARRIER_COUNT * 8;
    const yieldStateRegionBase = fenceSlot + 24;
    const totalWithBarriers = yieldStateRegionBase + 16 * Math.max(1, W);
    const pages = Math.max(1, Math.ceil(totalWithBarriers / 65536)) + 1;
    const zeroRegionSize = fenceSlot - sharedMemBase;
    return { viewOff, counterOff, scratchBase, sharedMemBase, barrierBase, fenceSlot,
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
            threadStart, threadEnd, GRID_DIM, L.groupSize, L.gridDimX, 1,
            L.scratchBase, L.SPT, L.sharedMemBase, L.barrierBase,
            0 /*dynSharedLen*/, L.zeroRegionSize, W, L.fenceSlot,
            yieldStateAddr, resumeMode, L.groupSize /*realGroupDimX*/, 1 /*realGroupDimY*/,
            // user args: dense view (offset,len,stride,stride2), counter ptr,
            // numGroups(=virtual), paddedLength, shift
            L.viewOff, N, 1, 0,
            L.counterOff,
            NUM_VIRTUAL_GROUPS, N /*paddedLength (N is 256-aligned)*/, SHIFT
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
    const DIR = process.argv[2];
    if (!DIR) { console.error('usage: node run-radix-pass1.mjs <emitDir> [workers] [rounds]'); process.exit(2); }
    const WORKERS = parseInt(process.argv[3] || '48');
    const ROUNDS = parseInt(process.argv[4] || '120');

    const manifest = JSON.parse(readFileSync(new URL(`./${DIR}/manifest.json`, import.meta.url), 'utf8'));
    // kernel_2 = RadixSortKernel1 (the only barrier kernel in the radix emit)
    const k = manifest.kernels.find(x => /hasBarriers=True/.test(x.info));
    if (!k) throw new Error('no barrier kernel in manifest');
    const infoNum = (key) => {
        const m = k.info.match(new RegExp(key + '=(\\d+)'));
        if (!m) throw new Error(`manifest info missing '${key}=' (info: ${k.info})`);
        return parseInt(m[1], 10);
    };
    const meta = {
        GROUP_SIZE: manifest.maxGroupSize,
        SHARED_MEM: infoNum('sharedMem'),
        BARRIER_COUNT: infoNum('barriers'),
        SCRATCH_PER_THREAD: Math.max(infoNum('scratchPerThread'), 64),
    };
    const wasmBytes = readFileSync(new URL(`./${DIR}/${k.wasm}`, import.meta.url));

    const N = meta.GROUP_SIZE * NUM_VIRTUAL_GROUPS; // 16384, 256-aligned => paddedLength == N
    const L = computeLayout(meta, N, WORKERS);
    L.SPT = meta.SCRATCH_PER_THREAD;
    const fibersPerWorker = Math.ceil(meta.GROUP_SIZE / WORKERS);
    const thisFile = fileURLToPath(import.meta.url);

    console.log(`RadixSortKernel1 gate [${DIR}]: tiles=${NUM_VIRTUAL_GROUPS} gs=${meta.GROUP_SIZE} N=${N}, workers=${WORKERS} (fibers/worker=${fibersPerWorker}), rounds=${ROUNDS}`);
    console.log(`  kernel: ${k.wasm} (${k.bytes}b) sharedMem=${meta.SHARED_MEM} barriers=${meta.BARRIER_COUNT} spt=${meta.SCRATCH_PER_THREAD}`);
    console.log(`  layout: pages=${L.pages}, scratchBase=${L.scratchBase}, sharedMemBase=${L.sharedMemBase}, fenceSlot=${L.fenceSlot}`);
    console.log('='.repeat(72));

    // pseudorandom input (hash of i): heavy duplicates within 0..1023, per-tile-distinct mix
    const val = i => (Math.imul(i + 1, 2654435761) >>> 8) % 1024;
    const bitsOf = v => ((v ^ 0x80000000) >> SHIFT) & (UNROLL - 1);
    const input = new Int32Array(N);
    for (let i = 0; i < N; i++) input[i] = val(i);

    // per-tile reference: stable counting-sort partition + bucket counts
    const refOut = new Int32Array(N);
    const refCnt = new Int32Array(UNROLL * NUM_VIRTUAL_GROUPS);
    for (let t = 0; t < NUM_VIRTUAL_GROUPS; t++) {
        const base = t * meta.GROUP_SIZE;
        let w = base;
        for (let j = 0; j < UNROLL; j++) {
            let c = 0;
            for (let i = 0; i < meta.GROUP_SIZE; i++) {
                if (bitsOf(input[base + i]) === j) { refOut[w++] = input[base + i]; c++; }
            }
            refCnt[j * NUM_VIRTUAL_GROUPS + t] = c;
        }
    }

    let totalBadRounds = 0, totalMismatch = 0, totalYields = 0;
    for (let round = 0; round < ROUNDS; round++) {
        const memory = new WebAssembly.Memory({ initial: L.pages, maximum: Math.min(WASM_MAX_PAGES, L.pages), shared: true });
        const i32 = new Int32Array(memory.buffer);
        i32.set(input, L.viewOff >>> 2);            // counter region stays zero (fresh memory)

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

        const outBase = L.viewOff >>> 2, cntBase = L.counterOff >>> 2;
        let mism = 0, firstBad = -1, firstGot = 0, firstExp = 0, cntErr = 0;
        const tiles = new Set();
        for (let i = 0; i < N; i++) {
            if (i32[outBase + i] !== refOut[i]) {
                if (mism === 0) { firstBad = i; firstGot = i32[outBase + i]; firstExp = refOut[i]; }
                tiles.add(Math.floor(i / meta.GROUP_SIZE));
                mism++;
            }
        }
        for (let c = 0; c < UNROLL * NUM_VIRTUAL_GROUPS; c++) {
            if (i32[cntBase + c] !== refCnt[c]) cntErr++;
        }
        if (mism > 0 || cntErr > 0) {
            totalBadRounds++; totalMismatch += mism + cntErr;
            const tileArr = [...tiles].sort((a, b) => a - b);
            console.log(`  round ${round + 1}: out ${mism}/${N} wrong, counters ${cntErr}/${UNROLL * NUM_VIRTUAL_GROUPS} wrong ` +
                `(first @${firstBad} [tile ${Math.floor(firstBad / meta.GROUP_SIZE)} slot ${firstBad % meta.GROUP_SIZE}]: got ${firstGot}, expected ${firstExp}) ` +
                `tiles=[${tileArr.slice(0, 8).join(',')}${tileArr.length > 8 ? ',...' : ''}](${tiles.size})`);
        }
    }
    const status = totalBadRounds === 0 ? 'PASS' : 'FAIL';
    console.log('='.repeat(72));
    console.log(`===RADIXPASS1=== dir=${DIR} workers=${WORKERS} ${status}: ${totalBadRounds}/${ROUNDS} rounds bad, ${totalMismatch} total mismatches, ${totalYields} total JS-yields`);
}
