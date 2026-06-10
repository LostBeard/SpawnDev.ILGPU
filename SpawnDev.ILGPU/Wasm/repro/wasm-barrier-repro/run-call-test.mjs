// Call-pattern barrier test: separate kernel function via Wasm `call`.
// Usage: node run-call-test.mjs [workers] [threadsPerWorker] [phases] [rounds]

import { readFileSync } from 'fs';
import { Worker, isMainThread, parentPort, workerData } from 'worker_threads';
import { fileURLToPath } from 'url';

const WORKERS = parseInt(process.argv[2] || '12');
const THREADS_PER_WORKER = parseInt(process.argv[3] || '64');
const PHASES = parseInt(process.argv[4] || '500');
const ROUNDS = parseInt(process.argv[5] || '5');

const TOTAL_THREADS = WORKERS * THREADS_PER_WORKER;
const MEM_SIZE = 24 + TOTAL_THREADS * 4 + WORKERS * 8 + 64;
const PAGES = Math.max(10, Math.ceil(MEM_SIZE / 65536));

if (!isMainThread) {
    const { wasmBytes, memory, workerIdx, workerCount, threadsPerWorker, numPhases, useWait32 } = workerData;
    const module = new WebAssembly.Module(wasmBytes);
    const instance = new WebAssembly.Instance(module, { env: { memory } });
    instance.exports.run(workerIdx, workerCount, threadsPerWorker, numPhases, useWait32);
    parentPort.postMessage('done');
} else {
    const wasmBytes = readFileSync(new URL('./call-barrier-test.wasm', import.meta.url));
    const thisFile = fileURLToPath(import.meta.url);

    console.log(`Call-pattern Barrier: ${WORKERS} workers, ${THREADS_PER_WORKER} threads/worker, ${TOTAL_THREADS} total, ${PHASES} phases, ${ROUNDS} rounds`);
    console.log('='.repeat(70));

    for (const mode of ['spin', 'wait32']) {
        const useWait32 = mode === 'wait32' ? 1 : 0;
        let totalViolations = 0;

        for (let round = 0; round < ROUNDS; round++) {
            const memory = new WebAssembly.Memory({ initial: PAGES, maximum: PAGES, shared: true });
            const view = new Int32Array(memory.buffer);
            view.fill(0);

            const violIdx = 16 >> 2;

            const workers = [];
            const promises = [];

            for (let i = 0; i < WORKERS; i++) {
                const w = new Worker(thisFile, {
                    workerData: { wasmBytes, memory, workerIdx: i, workerCount: WORKERS,
                                  threadsPerWorker: THREADS_PER_WORKER, numPhases: PHASES, useWait32 }
                });
                workers.push(w);
                promises.push(new Promise((resolve, reject) => {
                    w.on('message', resolve);
                    w.on('error', reject);
                }));
            }

            await Promise.all(promises);
            for (const w of workers) w.terminate();

            const violations = Atomics.load(view, violIdx);
            totalViolations += violations;
            if (violations > 0) {
                console.log(`  ${mode} round ${round + 1}: ${violations} violations`);
            }
        }

        const status = totalViolations === 0 ? 'PASS' : 'FAIL';
        console.log(`${mode.padEnd(8)}: ${status} (${totalViolations} violations across ${ROUNDS} rounds)`);
    }
}
