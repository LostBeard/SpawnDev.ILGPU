// Minimal Wasm barrier reproduction test.
// Tests whether memory.atomic.wait32/notify barrier is equivalent to spin barrier.
//
// Usage: node --experimental-wasm-threads run-test.mjs [workers] [phases] [rounds]

import { readFileSync } from 'fs';
import { Worker, isMainThread, parentPort, workerData } from 'worker_threads';
import { fileURLToPath } from 'url';

const WORKERS = parseInt(process.argv[2] || '4');
const PHASES = parseInt(process.argv[3] || '1000');
const ROUNDS = parseInt(process.argv[4] || '10');

// Memory layout (v3 - double barrier):
//   [0]    = barrier1 arrival counter
//   [4]    = barrier1 generation
//   [8]    = barrier2 arrival counter
//   [12]   = barrier2 generation
//   [16]   = data area (WORKERS * 4 bytes)
//   [16 + WORKERS*4] = violation count
const MEM_SIZE = 16 + WORKERS * 4 + 4;
const PAGES = Math.ceil(MEM_SIZE / 65536) || 1;

if (!isMainThread) {
  // Worker thread
  const { wasmBytes, memory, workerIdx, workerCount, numPhases, useWait32 } = workerData;
  const module = new WebAssembly.Module(wasmBytes);
  const instance = new WebAssembly.Instance(module, { env: { memory } });
  instance.exports.run(workerIdx, workerCount, numPhases, useWait32);
  parentPort.postMessage('done');
} else {
  // Main thread
  const wasmBytes = readFileSync(new URL('./barrier-test.wasm', import.meta.url));
  const thisFile = fileURLToPath(import.meta.url);

  console.log(`Barrier Test: ${WORKERS} workers, ${PHASES} phases, ${ROUNDS} rounds`);
  console.log('='.repeat(60));

  for (const mode of ['spin', 'wait32']) {
    const useWait32 = mode === 'wait32' ? 1 : 0;
    let totalViolations = 0;
    let totalRounds = 0;

    for (let round = 0; round < ROUNDS; round++) {
      const memory = new WebAssembly.Memory({ initial: PAGES, maximum: PAGES, shared: true });

      // Clear memory
      const view = new Int32Array(memory.buffer);
      view.fill(0);

      const violationAddr = (16 + WORKERS * 4) >> 2; // i32 index

      // Spawn workers
      const workers = [];
      const promises = [];

      for (let i = 0; i < WORKERS; i++) {
        const w = new Worker(thisFile, {
          workerData: {
            wasmBytes,
            memory,
            workerIdx: i,
            workerCount: WORKERS,
            numPhases: PHASES,
            useWait32,
          },
        });
        workers.push(w);
        promises.push(new Promise((resolve, reject) => {
          w.on('message', resolve);
          w.on('error', reject);
        }));
      }

      await Promise.all(promises);
      for (const w of workers) w.terminate();

      const violations = Atomics.load(view, violationAddr);
      totalViolations += violations;
      totalRounds++;

      if (violations > 0) {
        console.log(`  ${mode} round ${round + 1}: ${violations} violations`);
      }
    }

    const status = totalViolations === 0 ? 'PASS' : 'FAIL';
    console.log(`${mode.padEnd(8)}: ${status} (${totalViolations} violations across ${totalRounds} rounds of ${PHASES} phases)`);
  }
}
