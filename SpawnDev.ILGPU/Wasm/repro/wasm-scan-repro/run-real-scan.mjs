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

// ---- kernel metadata: read LIVE from manifest.json (written by scan-emit next to the kernel).
// NEVER hardcode these. A kernel re-emit that grows scratchPerThread/barriers silently
// invalidates a hardcoded layout: a 2376-vs-2392 stride mismatch made adjacent threads'
// fiber scratch OVERLAP by 16 bytes here, producing intermittent yield-correlated corruption
// that mimicked the real race (found by Seven 2026-06-09 during the emitted-binary audit).
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
    let pages = Math.max(1, Math.ceil(totalWithBarriers / 65536)) + 1;
    // DBG_KERNEL=1 (00_kernel_1_dbg.wasm) appends per-tid debug rings at FIXED addresses
    // (built for the N=16384 layout): ring1 = rb at READ (helper ph3), ring2 = rb at
    // CONSUMPTION (kernel carry update) + carryBefore - needs >= 22 pages.
    if (process.env.DBG_KERNEL === '1') pages = Math.max(pages, 22);
    const zeroRegionSize = fenceSlot - sharedMemBase;
    return { inOff, outOff, scratchBase, sharedMemBase, barrierBase, fenceSlot,
             yieldStateRegionBase, zeroRegionSize, pages, numGroups, gridDimX };
}
const DBGC = 851968, DBGD = 856064;    // ring1: rb at read (helper ph3)
const DBGC2 = 852992, DBGD2 = 1118208; // ring2: rb at consumption + carryBefore (kernel)

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

    // YIELD LOG (instrumentation, 2026-06-09 Seven): every yield records the dispatcher's
    // saved state {flag, savedG, savedPhase, savedGen} + the live gen at yield and at wake.
    // JS-side reads only - zero wasm changes, no semantic effect on the protocol. Dumped by
    // the main thread ONLY on corrupted rounds, to correlate corruption with resume events
    // (was the corrupting thread's worker resuming at the corrupted tile's barrier?) and to
    // catch invariant violations (savedGen regression / saved-phase jump = smoking gun).
    // NOTE: for f=2 (group-barrier yield) the dispatcher does NOT write savedPhase (slot +8),
    // so p shows the slot's stale prior value on those records.
    const ylog = [];
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
        const rec = {
            f: yieldFlag,
            g: yMem32[yieldFlagIdx + 1],
            p: yMem32[yieldFlagIdx + 2],
            sg: savedGen,
            gy: Atomics.load(yMem32, waitGenIdx), // live gen at yield
        };
        // FIBER-STATE SAMPLE: each tid's saved state TRIPLE, offsets verified in the
        // emitted WAT (00_kernel_1.wasm):
        //   k = kernel continuation (_stateLocal spill, myScratch+8; br_table local 20)
        //   h = helperPhaseLocal   (kernel local 83 spill, myScratch+292)
        //   s = helper continuation (helper _stateLocal, helperScratch(+1400)+12 = myScratch+1412)
        // All tids march the same global phase schedule, so at any yield every sibling
        // tid's triple MUST be identical. sv = first tid's triple; sx set (loud) only
        // when siblings diverge - that divergence IS the fiber desync, caught at the
        // exact phase. The kernel-only probe (run 3) showed NO k divergence across 7
        // corruption events; h/s extend visibility into the helper state machine the
        // barrier protocol never verifies.
        if (threadEnd > threadStart) {
            const sts = [];
            for (let t = threadStart; t < threadEnd; t++) {
                const base = L.scratchBase + t * SCRATCH_PER_THREAD;
                sts.push(`${Atomics.load(yMem32, (base + 8) >>> 2)}.${Atomics.load(yMem32, (base + 292) >>> 2)}.${Atomics.load(yMem32, (base + 1412) >>> 2)}`);
            }
            rec.sv = sts[0];
            for (let k = 1; k < sts.length; k++)
                if (sts[k] !== sts[0]) { rec.sx = sts.join('/'); break; }
        }
        if (process.env.NO_PARK === '1') {
            // PARK-DISABLED: busy-spin on the gen instead of Atomics.wait, to isolate whether the
            // residual race lives in the Atomics.wait park/resume ordering (V8) or our pure-spin
            // gen/state-save logic. Same seq_cst gen load, no wait/notify.
            // (A/B result 2026-06-09: corrupts in BOTH modes at ~same per-yield rate ->
            // Atomics.wait exonerated; suspects = yield/re-enter machinery or preemption itself.)
            while (Atomics.load(yMem32, waitGenIdx) === savedGen) { /* spin */ }
        } else {
            Atomics.wait(yMem32, waitGenIdx, savedGen);
        }
        rec.gw = Atomics.load(yMem32, waitGenIdx); // live gen at wake (>= sg+1 expected for f=1)
        if (ylog.length < 20000) ylog.push(rec);
        resumeMode = 1;
    }
    parentPort.postMessage({ ok: true, yields: yieldIters, ylog });
}

if (isMainThread) {
    const N = parseInt(process.argv[2] || '4096');
    const WORKERS = parseInt(process.argv[3] || '8');
    const ROUNDS = parseInt(process.argv[4] || '20');
    const wasmBytes = readFileSync(new URL(
        process.env.DBG_KERNEL === '1' ? './00_kernel_1_dbg.wasm' : './00_kernel_1.wasm', import.meta.url));
    if (process.env.DBG_KERNEL === '1') console.log('DBG_KERNEL=1: per-tid rb-observation rings active (helper ph3)');
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
    // Tile values are PSEUDORANDOM (hash of tile index), not linear. With linear values
    // (v(t)=1+t%251) every one-tile-stale event collapses to the SAME delta (-GROUP_SIZE),
    // which is ambiguous: "previous tile's publication" and "value short by 256" are the
    // same number (Captain's observation, 2026-06-09). Pseudorandom values make the delta
    // a FINGERPRINT: a stale slot holding tile j's rb at tile t's update gives
    // delta = GROUP_SIZE*(v(j)-v(t)), uniquely identifying WHICH historical tile was held.
    const tileVal = t => 1 + ((Math.imul(t + 1, 2654435761) >>> 8) % 251);
    const input = new Int32Array(N);
    const ref = new Int32Array(N);
    let acc = 0;
    for (let i = 0; i < N; i++) {
        input[i] = tileVal(Math.floor(i / GROUP_SIZE));
        acc = (acc + input[i]) | 0;                              // int32 inclusive prefix sum
        ref[i] = acc;
    }
    console.log(`  input: per-tile PSEUDORANDOM (delta fingerprints the held tile); ref[0]=${ref[0]}, ref[${N - 1}]=${ref[N - 1]}`);

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

        // verify against the int32 inclusive-scan reference of the per-tile-distinct input.
        // Pattern analysis mirrors WasmTests.Wasm_MultiTileScan_Oversub48_PerTileDistinct:
        // distinctSlots==1 + allSameDelta=-GROUP_SIZE + contiguous tiles = the one-thread
        // stale-boundary-window signature; anything else = a different mechanism.
        const outBase = L.outOff >>> 2;
        let mism = 0, firstBad = -1, firstGot = 0, firstExp = 0;
        const slots = new Set(), tiles = new Set(), deltas = new Set();
        for (let i = 0; i < N; i++) {
            if (i32[outBase + i] !== ref[i]) {
                if (mism === 0) { firstBad = i; firstGot = i32[outBase + i]; firstExp = ref[i]; }
                slots.add(i % GROUP_SIZE);
                tiles.add(Math.floor(i / GROUP_SIZE));
                deltas.add(i32[outBase + i] - ref[i]);
                mism++;
            }
        }
        if (mism > 0) {
            totalBadRounds++; totalMismatch += mism;
            const tileArr = [...tiles].sort((a, b) => a - b);
            const tilesContiguous = tileArr.every((t, k) => k === 0 || t === tileArr[k - 1] + 1);
            console.log(`  round ${round + 1}: ${mism}/${N} wrong (first @${firstBad} [tile ${Math.floor(firstBad / GROUP_SIZE)}]: got ${firstGot}, expected ${firstExp}, delta ${firstGot - firstExp}) ` +
                `distinctSlots=${slots.size}${slots.size <= 4 ? `[${[...slots].join(',')}]` : ''} ` +
                `tiles=${tileArr[0]}..${tileArr[tileArr.length - 1]}(${tiles.size}${tilesContiguous ? ',contig' : ',GAPS'}) ` +
                `deltas=${[...deltas].length <= 4 ? [...deltas].join(',') : [...deltas].length + ' distinct'}`);

            // ---- YIELD-LOG DUMP (bad rounds only) ----
            // Suspect worker(s): owners of the corrupted slot(s). tid range per worker is
            // [w*fibersPerWorker, w*fibersPerWorker+fibersPerWorker).
            const suspects = new Set([...slots].map(s => Math.floor(s / fibersPerWorker)));
            const yieldCounts = results.map(r => (r && r.ylog) ? r.ylog.length : 0).join(',');
            console.log(`    yield counts per worker: [${yieldCounts}]`);
            // Invariant scan over EVERY worker's log: per flag-type, savedGen must be
            // non-decreasing; g must be non-decreasing; for f=1 within the same g, saved
            // phase must be non-decreasing. Any regression = smoking gun, print loudly.
            for (let w = 0; w < WORKERS; w++) {
                const log = (results[w] && results[w].ylog) || [];
                let lastSg = { 1: -1, 2: -1 }, lastG = -1, lastP = -1, lastPG = -1;
                for (let k = 0; k < log.length; k++) {
                    const r = log[k];
                    if (r.g < lastG) console.log(`    !! INVARIANT w${w}@${k}: g regressed ${lastG} -> ${r.g} (${JSON.stringify(r)})`);
                    if (r.sg < lastSg[r.f]) console.log(`    !! INVARIANT w${w}@${k}: savedGen(f=${r.f}) regressed ${lastSg[r.f]} -> ${r.sg} (${JSON.stringify(r)})`);
                    if (r.f === 1 && r.g === lastPG && r.p < lastP) console.log(`    !! INVARIANT w${w}@${k}: savedPhase regressed ${lastP} -> ${r.p} within g=${r.g} (${JSON.stringify(r)})`);
                    if (r.f === 1 && r.gy < r.sg) console.log(`    !! INVARIANT w${w}@${k}: live gen at yield ${r.gy} < savedGen ${r.sg} (${JSON.stringify(r)})`);
                    if (r.sx !== undefined) console.log(`    !! FIBER-DESYNC w${w}@${k} (tids ${w * fibersPerWorker}..): sibling continuations diverged [${r.sx}] at phase ${r.p} (${JSON.stringify(r)})`);
                    lastG = r.g; lastSg[r.f] = Math.max(lastSg[r.f], r.sg);
                    if (r.f === 1) { lastP = r.p; lastPG = r.g; }
                }
            }
            // Full compact log for each suspect worker: f/g/p/savedGen/genAtYield/genAtWake.
            for (const w of suspects) {
                const log = (results[w] && results[w].ylog) || [];
                const compact = log.map(r => `${r.f}:g${r.g}:p${r.p}:sg${r.sg}:gy${r.gy}:gw${r.gw}:sv${r.sv}${r.sx ? ':SX' + r.sx : ''}`).join(' ');
                console.log(`    suspect w${w} (tids ${w * fibersPerWorker}..${Math.min(w * fibersPerWorker + fibersPerWorker, GROUP_SIZE) - 1}) ylog[${log.length}]: ${compact}`);
            }
            // DEBUG RING DUMP (DBG_KERNEL=1): per-tid rb observations from INSIDE the
            // helper's final phase, at the moment of the read. Decisive fork:
            //   victim ring shows the stale rb  -> the READ itself returned old data
            //   victim ring shows correct rb    -> read fine; corruption is AFTER the read
            //                                      (out-param/carry/save-restore path)
            // Cursor sanity: every tid must have run ph3 exactly 64 times (one per tile).
            if (process.env.DBG_KERNEL === '1') {
                const cursorOf = t => i32[(DBGC + t * 4) >>> 2];
                // ring1 16B records: {rbRead, tempBack([55+4] after plain store),
                //                     outBack([13+4] after atomic copy), local54Again}
                const ringVal = (t, k, off) => i32[(DBGD + t * 1024 + k * 16 + off) >>> 2];
                const expTiles = Math.ceil(N / GROUP_SIZE);
                const badCursors = [];
                for (let t = 0; t < GROUP_SIZE; t++) if (cursorOf(t) !== expTiles) badCursors.push(`tid${t}=${cursorOf(t)}`);
                console.log(`    ring cursors: ${badCursors.length === 0 ? `all ${expTiles} (OK)` : 'ANOMALY: ' + badCursors.join(' ')}`);
                const cursor2Of = t => i32[(DBGC2 + t * 4) >>> 2];
                const ring2Val = (t, k, off) => i32[(DBGD2 + t * 1024 + k * 16 + off) >>> 2];
                const exp2 = expTiles - 1; // carry update runs once per tile TRANSITION (63)
                const badCursors2 = [];
                for (let t = 0; t < GROUP_SIZE; t++) if (cursor2Of(t) !== exp2) badCursors2.push(`tid${t}=${cursor2Of(t)}`);
                console.log(`    ring2 cursors: ${badCursors2.length === 0 ? `all ${exp2} (OK)` : 'ANOMALY: ' + badCursors2.join(' ')}`);
                const dumpTids = new Set();
                for (const s2 of slots) { dumpTids.add(s2); if (s2 > 0) dumpTids.add(s2 - 1); }
                dumpTids.add(GROUP_SIZE - 1); // the boundary writer's own ring
                for (const t of dumpTids) {
                    const rows = [];
                    for (let k = 0; k < Math.min(cursorOf(t), 64); k++) {
                        const exp = GROUP_SIZE * tileVal(k);
                        const expLeft = tileVal(k); // scanResults[0] of tile k = first element = v(k)
                        const rb = ringVal(t, k, 0), tempBack = ringVal(t, k, 4), outBack = ringVal(t, k, 8), leftBack = ringVal(t, k, 12);
                        if (rb !== exp || tempBack !== exp || outBack !== exp || leftBack !== expLeft)
                            rows.push(`tile${k}: rbREAD=${rb} tempBack=${tempBack} outBack=${outBack} exp=${exp} leftBack=${leftBack} expLeft=${expLeft}`);
                    }
                    const myPtr = L.scratchBase + t * SCRATCH_PER_THREAD + 1384 + 4;
                    // FINGERPRINT DECODER: with pseudorandom tile values, a stale consumed
                    // value uniquely identifies WHICH tile's publication the slot held.
                    const heldTile = v => {
                        if (v === -1) return 'INIT(-1, never written)';
                        for (let j = 0; j < expTiles; j++) if (GROUP_SIZE * tileVal(j) === v) return `tile ${j}'s rb`;
                        return 'NO MATCH (not any tile rb - arithmetic corruption!)';
                    };
                    for (let k = 0; k < Math.min(cursor2Of(t), 64); k++) {
                        const exp = GROUP_SIZE * tileVal(k);
                        const rb2 = ring2Val(t, k, 0), ptr = ring2Val(t, k, 8);
                        if (rb2 !== exp || ptr !== myPtr) rows.push(`tile${k}: rbCONSUMED=${rb2} exp=${exp} (d=${rb2 - exp}) HELD=${heldTile(rb2)} carryBefore=${ring2Val(t, k, 4)} ptr=${ptr}${ptr !== myPtr ? ` WRONG(own=${myPtr}, = tid${Math.floor((ptr - 1388 - L.scratchBase) / SCRATCH_PER_THREAD)}'s slot)` : '(own)'}`);
                    }
                    console.log(`    rings tid${t}${slots.has(t) ? ' [VICTIM]' : ''}: ${rows.length === 0 ? 'read/readBack/consumed rb all CORRECT' : rows.join(' | ')}`);
                }
            }
            // Cross-worker continuation agreement: at any phase p, every worker's sv
            // (sampled at ITS yield for that phase) must agree. Disagreement = a worker
            // whose tids are running a different continuation schedule = fiber desync.
            const phaseSv = new Map();
            for (let w = 0; w < WORKERS; w++) {
                for (const r of ((results[w] && results[w].ylog) || [])) {
                    if (r.f !== 1 || r.sv === undefined) continue;
                    if (!phaseSv.has(r.p)) phaseSv.set(r.p, new Map());
                    const m2 = phaseSv.get(r.p);
                    if (!m2.has(r.sv)) m2.set(r.sv, []);
                    m2.get(r.sv).push(w);
                }
            }
            for (const [p, m2] of [...phaseSv.entries()].sort((a, b) => a[0] - b[0])) {
                if (m2.size > 1) {
                    const desc = [...m2.entries()].map(([sv, ws]) => `sv${sv}:w[${ws.join(',')}]`).join(' vs ');
                    console.log(`    !! CROSS-WORKER SV MISMATCH at phase ${p}: ${desc}`);
                }
            }
        }
    }
    const status = totalBadRounds === 0 ? 'PASS' : 'FAIL';
    console.log('='.repeat(72));
    console.log(`${status}: ${totalBadRounds}/${ROUNDS} rounds bad, ${totalMismatch} total mismatches, ${totalYields} total JS-yields`);
}
