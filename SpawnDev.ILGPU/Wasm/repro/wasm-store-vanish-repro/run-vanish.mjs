// Driver for the minimal store-vanish repro (see vanish.wat). NO ILGPU machinery.
// Mirrors run-real-scan.mjs's worker loop: dispatch -> yieldFlag -> park (Atomics.wait
// on the gen slot with the wasm-saved gen) or NO_PARK=1 busy-spin -> re-enter resume=1.
//
// Build first:  wat2wasm --enable-threads vanish.wat -o vanish.wasm
// Usage:        node run-vanish.mjs [workers] [iters] [rounds] [spinMax]
//   (oversubscribe workers past cores; iters ~ barrier crossings per round)
// Tier A/B:     node --no-wasm-tier-up --no-wasm-dynamic-tiering ... / node --no-liftoff ...
//
// PASS = zero immCount/postCount everywhere. Any nonzero log = the anomaly fired
// standalone -> upstream-reportable. firstKind decodes 0=plain-imm 1=atomic-imm
// 2=plain-post 3=atomic-post.
import { readFileSync } from 'fs';
import { Worker, isMainThread, parentPort, workerData } from 'worker_threads';
import { fileURLToPath } from 'url';

const DATA_BASE = 65536, DATA_STRIDE = 4096, YBASE = 64, FBASE = 4096, FSTRIDE = 64;

if (!isMainThread) {
    const { wasmBytes, memory, wid, wc, iters, spinMax } = workerData;
    const v = new Int32Array(memory.buffer);
    const notify = (addr, count) => Atomics.notify(v, addr >>> 2, count);
    const inst = new WebAssembly.Instance(new WebAssembly.Module(wasmBytes), { env: { memory, notify } });
    const run = inst.exports.run;
    const yIdx = (YBASE + wid * 16) >>> 2;
    let resume = 0, yields = 0;
    while (true) {
        const r = run(wid, wc, iters, spinMax, resume);
        if (r === 0) break;
        yields++;
        const savedGen = v[yIdx + 1];
        if (process.env.NO_PARK === '1') {
            while (Atomics.load(v, 1) === savedGen) { /* spin */ }
        } else {
            Atomics.wait(v, 1, savedGen);
        }
        resume = 1;
    }
    parentPort.postMessage({ ok: true, yields });
}

if (isMainThread) {
    const WORKERS = parseInt(process.argv[2] || '48');
    const ITERS = parseInt(process.argv[3] || '400');
    const ROUNDS = parseInt(process.argv[4] || '60');
    const SPINMAX = parseInt(process.argv[5] || '200000');
    const wasmBytes = readFileSync(new URL(`./${process.env.WASM || 'vanish.wasm'}`, import.meta.url));
    if (process.env.WASM) console.log(`module: ${process.env.WASM}`);
    const TIDS = parseInt(process.env.TIDS || '1'); // vanish3: 6 tid regions per worker
    const thisFile = fileURLToPath(import.meta.url);
    const pages = Math.ceil((DATA_BASE + WORKERS * TIDS * DATA_STRIDE) / 65536) + 1;
    console.log(`store-vanish minimal repro: workers=${WORKERS} iters=${ITERS} rounds=${ROUNDS} spinMax=${SPINMAX} park=${process.env.NO_PARK === '1' ? 'NO (busy-spin)' : 'Atomics.wait'}`);

    let badRounds = 0, totalImm = 0, totalPost = 0, totalYields = 0;
    for (let round = 0; round < ROUNDS; round++) {
        const memory = new WebAssembly.Memory({ initial: pages, maximum: 16384, shared: true });
        const v = new Int32Array(memory.buffer);
        const workers = [], promises = [];
        for (let w = 0; w < WORKERS; w++) {
            const wk = new Worker(thisFile, { workerData: { wasmBytes, memory, wid: w, wc: WORKERS, iters: ITERS, spinMax: SPINMAX } });
            workers.push(wk);
            promises.push(new Promise((res, rej) => { wk.on('message', res); wk.on('error', rej); }));
        }
        const results = await Promise.all(promises);
        for (const wk of workers) wk.terminate();
        totalYields += results.reduce((a, r) => a + ((r && r.yields) || 0), 0);

        let roundBad = false;
        for (let w = 0; w < WORKERS; w++) {
            const f = (FBASE + w * FSTRIDE) >>> 2;
            const imm = v[f], post = v[f + 1];
            if (imm || post) {
                roundBad = true; totalImm += imm; totalPost += post;
                const kind = ['plain-imm', 'atomic-imm', 'plain-post', 'atomic-post'][v[f + 5]] || v[f + 5];
                console.log(`  round ${round + 1} w${w}: immCount=${imm} postCount=${post} first{iter=${v[f + 2]} slot=${v[f + 3]} val=${v[f + 4]} kind=${kind}}`);
            }
        }
        if (roundBad) badRounds++;
    }
    console.log('='.repeat(72));
    console.log(`${badRounds === 0 ? 'PASS (anomaly did NOT fire standalone)' : 'FAIL (ANOMALY FIRED STANDALONE - upstream-reportable)'}: ${badRounds}/${ROUNDS} rounds bad, imm=${totalImm} post=${totalPost}, yields=${totalYields}`);
}
