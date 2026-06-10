// Cross-dispatch SAB-visibility micro-repro — MAIN THREAD orchestrator.
//
// Reproduces the SpawnDev.ILGPU Wasm dispatch boundary in isolation:
//   dispatch N : worker A writes a shared region NON-ATOMICALLY (like Kernel1 -> counter[]),
//                posts {done} to MAIN with no release fence.
//   MAIN       : awaits done, then postMessages dispatch N+1 to a DIFFERENT worker B.
//   dispatch N+1: worker B reads the region (like the scan/scatter reading counter[]).
// If postMessage does NOT carry happens-before for non-atomic SharedArrayBuffer writes, B can
// read STALE slots (the previous epoch). Count stale reads over many iterations, under load.
//
// A->B is always a DIFFERENT worker (cross-worker, exactly the production hazard). The region is
// NEVER zeroed between iterations, so a stale slot shows the PREVIOUS epoch (detectable).

const WORKERS = 4;                 // pool size (>=2). Different writer/reader each iter.
const REGION_U32 = 65536;          // 256 KB write region (mimics a large counter[])
const SAB_U32 = REGION_U32 + 16;   // a little slack
const BASE = 0;

const sab = new SharedArrayBuffer(SAB_U32 * 4);
const pool = [];
let ready = 0;

let epoch = 0;
let iters = 0;
let staleReads = 0;       // total stale SLOTS observed
let staleIters = 0;       // iterations where >=1 stale slot appeared
let running = false;
let startTime = 0;
const log = (s) => { const el = document.getElementById('log'); el.textContent = s + '\n' + el.textContent; };
const stat = () => {
  const secs = ((performance.now() - startTime) / 1000).toFixed(1);
  document.getElementById('stat').textContent =
    `iters=${iters}  staleIters=${staleIters}  staleSlots=${staleReads}  (${secs}s)  ` +
    (staleIters > 0 ? '>>> STALE DETECTED — postMessage does NOT carry non-atomic SAB visibility <<<'
                    : 'clean so far (remember: only FAILURES are evidence)');
};

// pending continuations keyed by worker idx
const onDone = new Array(WORKERS).fill(null);
const onRead = new Array(WORKERS).fill(null);

for (let i = 0; i < WORKERS; i++) {
  const w = new Worker('worker.js');
  w.onmessage = (e) => {
    const m = e.data;
    if (m.ready) { if (++ready === WORKERS) log(`all ${WORKERS} workers ready`); return; }
    if (m.done && onDone[m.idx]) { const f = onDone[m.idx]; onDone[m.idx] = null; f(); return; }
    if (m.readResult && onRead[m.idx]) { const f = onRead[m.idx]; onRead[m.idx] = null; f(m); return; }
  };
  w.postMessage({ cmd: 'init', sab, idx: i });
  pool[i] = w;
}

function iteration() {
  if (!running) return;
  epoch = (epoch + 1) >>> 0;
  if (epoch === 0) epoch = 1; // never use 0 (initial SAB value)
  const wi = iters % WORKERS;
  const ri = (iters + 1) % WORKERS;     // always a DIFFERENT worker
  const ep = epoch;

  onDone[wi] = () => {
    // writer done (program-order: all its non-atomic stores are issued). Now the cross-worker read.
    onRead[ri] = (res) => {
      iters++;
      if (res.stale > 0) {
        staleReads += res.stale;
        staleIters++;
        log(`STALE iter=${iters} epoch=${ep} writer=${wi} reader=${ri} staleSlots=${res.stale} ` +
            `firstBad[${res.firstBad}]=${res.firstBadVal} lastBad=${res.lastBad}`);
      }
      if (iters % 500 === 0) stat();
      iteration();
    };
    pool[ri].postMessage({ cmd: 'read', base: BASE, count: REGION_U32, epoch: ep });
  };
  pool[wi].postMessage({ cmd: 'write', base: BASE, count: REGION_U32, epoch: ep });
}

function startRun() {
  if (running) return;
  running = true; startTime = performance.now();
  log('started — run FO76 for contention; watch staleIters');
  iteration();
}
document.getElementById('start').onclick = startRun;
document.getElementById('stop').onclick = () => { running = false; stat(); log('stopped'); };
window.crossDispatchStats = () => ({ iters, staleIters, staleReads }); // for CDP/Playwright readout

// Report stats to the server (-> stats.json) so a driver can read progress without CDP.
setInterval(() => {
  const secs = ((performance.now() - startTime) / 1000).toFixed(1);
  fetch(`/report?iters=${iters}&staleIters=${staleIters}&staleSlots=${staleReads}&secs=${secs}&running=${running}`)
    .catch(() => {});
}, 1000);

// Auto-start when opened with ?auto (so a plain Chrome window can drive the run unattended).
if (location.search.includes('auto')) {
  if (ready >= WORKERS) startRun(); else setTimeout(startRun, 800);
}
