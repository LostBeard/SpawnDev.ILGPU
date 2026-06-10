// Builds 00_kernel_1_dbg3.wasm: the real scan kernel + PUBLICATION-TIMING rings.
// (Seven 2026-06-10 - the writer-lag discriminator.)
//
// Question it answers: when a carry consumption reads a one-publication-behind
// rb (the residual - all 8 fingerprinted events today were EXACTLY v(t-2)-v(t-1)),
// had the publisher's store ALREADY executed (true memory-visibility anomaly) or
// does it execute LATER (fiber ran its publication segment in a later dispatcher
// phase than the schedule assumes = writer-side continuation lag, OUR bug)?
//
// Instrumentation - both sides record the live PHASE GENERATION (atomic load of
// fenceSlot+4) at the moment of execution:
//   PUB ring  (helper, at the scanResults[IdxX] = ws[IdxX] atomic copy store):
//     ring[tid][cursor] = { genAtWrite, valueWritten } (16B rec), cursor++
//   CONS ring (kernel, at the carry-update rb consumption - same anchor as ring2):
//     ring[tid][cursor] = { genAtRead, rbConsumed } (16B rec), cursor++
//
// Verdict per victim event (fiber s corrupt from tile t): compare
//   PUB[255][t-1].gen   (tid 255 publishes the rb the crossing into tile t consumes)
//   CONS[s][t-1].gen    (the victim's consumption of that rb)
// pubGen > consGen  -> WRITE WAS LATE (fiber/state-machine lag) - our codegen bug.
// pubGen < consGen  -> write preceded the read - genuine visibility anomaly.
//
// Layout: cursors/rings placed above the ring1/ring2/stamp regions; DBG_KERNEL=3
// bumps memory to 26 pages. fenceSlot is computed from manifest.json with the
// exact mirror of the harness computeLayout (N=16384 repro config).
import { readFileSync, writeFileSync } from 'fs';
import { execSync } from 'child_process';

const PUBC = 1380352, PUBD = 1384448;   // pub cursors (1KB) / rings (256KB)
const CONC = 1650688, COND = 1654784;   // cons cursors / rings

// ---- fenceSlot from manifest (mirror of run-real-scan computeLayout, N=16384) ----
const manifest = JSON.parse(readFileSync('./manifest.json', 'utf8'));
const info = manifest.kernels[0].info;
const num = k => parseInt(info.match(new RegExp(k + '=(\\d+)'))[1], 10);
const GROUP_SIZE = manifest.maxGroupSize, N = 16384;
const align = (x, a) => Math.ceil(x / a) * a;
const SPT = Math.max(num('scratchPerThread'), 64);
let total = align(N * 4, 8) + N * 4;
total += GROUP_SIZE * 1 * 8; // gridDimX pad (numGroups=1)
const scratchBase = align(total, 8);
const sharedMemBase = align(align(scratchBase + SPT * GROUP_SIZE, 8), 8);
const barrierBase = align(sharedMemBase + num('sharedMem'), 4);
const fenceSlot = barrierBase + num('barriers') * 8;
const GEN_ADDR = fenceSlot + 4;
// scanResults[DimX-1] absolute address: sharedMemBase + 4096 (scanResults offset) + 255*4
const SHARED_RB_ADDR = sharedMemBase + 4096 + (GROUP_SIZE - 1) * 4;
console.log(`fenceSlot=${fenceSlot} genAddr=${GEN_ADDR} sharedRb=${SHARED_RB_ADDR} (spt=${SPT} shared=${num('sharedMem')} barriers=${num('barriers')})`);

const wat = execSync('wasm2wat --enable-threads 00_kernel_1.wasm', { maxBuffer: 64 * 1024 * 1024 }).toString();
const lines = wat.split('\n');

// ---- PUB ring: helper func 25, anchor = the atomic copy store to scanResults ----
// The scanResults base = sharedMemBase-relative '+ 4096' (the unique i32.const 4096 in
// the helper); the publication is the FIRST i32.atomic.store after that constant.
const fStart = lines.findIndex(l => l.includes('(func (;25;)'));
if (fStart < 0) throw new Error('func 25 not found');
const fEnd = lines.findIndex((l, i) => i > fStart && l.includes('(func (;26;)'));
const declIdx = fStart + 1;
if (!lines[declIdx].trim().startsWith('(local')) throw new Error('helper local decl not found');
lines[declIdx] = lines[declIdx].replace(/\)\s*$/, ' i32 i32 i32)'); // locals 59,60,61

let c4096 = -1;
for (let i = fStart; i < fEnd; i++)
    if (lines[i].trim() === 'i32.const 4096') { c4096 = i; break; }
if (c4096 < 0) throw new Error('i32.const 4096 (scanResults base) not found in helper');
let pubAnchor = -1;
for (let i = c4096; i < fEnd; i++)
    if (lines[i].trim() === 'i32.atomic.store') { pubAnchor = i; break; }
if (pubAnchor < 0) throw new Error('publication atomic.store not found');
// The value local stored: line before the store is 'local.get <valLocal>'
const valM = lines[pubAnchor - 1].trim().match(/^local\.get (\d+)$/);
if (!valM) throw new Error('publication value local not identifiable: ' + lines[pubAnchor - 1]);
const pubVal = valM[1];

const pubRing = `
        ;; === PUB-TIMING RING (Seven 2026-06-10): {genAtWrite, valueWritten} per tid ===
        local.get 5
        i32.const 4
        i32.mul
        i32.const ${PUBC}
        i32.add
        local.set 59
        local.get 59
        i32.atomic.load
        local.set 60
        local.get 60
        i32.const 127
        i32.lt_u
        if
          local.get 5
          i32.const 1024
          i32.mul
          local.get 60
          i32.const 16
          i32.mul
          i32.add
          i32.const ${PUBD}
          i32.add
          local.set 61
          local.get 61
          i32.const ${GEN_ADDR}
          i32.atomic.load
          i32.atomic.store
          local.get 61
          i32.const 4
          i32.add
          local.get ${pubVal}
          i32.atomic.store
        end
        local.get 59
        local.get 60
        i32.const 1
        i32.add
        i32.atomic.store
        ;; === END PUB-TIMING RING ===`;
lines.splice(pubAnchor + 1, 0, pubRing);

// ---- CONS ring: kernel func 24, anchor = rb consumption (same as ring2's anchor:
// 'i32.atomic.load' + 'local.set 126') ----
const kStart = lines.findIndex(l => l.includes('(func (;24;)'));
if (kStart < 0) throw new Error('func 24 not found');
const kDecl = kStart + 1;
if (!lines[kDecl].trim().startsWith('(local')) throw new Error('kernel local decl not found');
lines[kDecl] = lines[kDecl].replace(/\)\s*$/, ' i32 i32 i32)'); // locals 158,159,160
const kEnd = lines.findIndex((l, i) => i > kStart && l.includes('(func (;25;)'));
let kAnchor = -1;
for (let i = kStart; i < kEnd; i++)
    if (lines[i].trim() === 'local.set 126' && lines[i - 1].trim() === 'i32.atomic.load') { kAnchor = i; break; }
if (kAnchor < 0) throw new Error('consumption anchor not found');
const consRing = `
                            ;; === CONS-TIMING RING: {genAtRead, rbConsumed} per tid ===
                            local.get 5
                            i32.const 4
                            i32.mul
                            i32.const ${CONC}
                            i32.add
                            local.set 158
                            local.get 158
                            i32.atomic.load
                            local.set 159
                            local.get 159
                            i32.const 127
                            i32.lt_u
                            if
                              local.get 5
                              i32.const 1024
                              i32.mul
                              local.get 159
                              i32.const 16
                              i32.mul
                              i32.add
                              i32.const ${COND}
                              i32.add
                              local.set 160
                              local.get 160
                              i32.const ${GEN_ADDR}
                              i32.atomic.load
                              i32.atomic.store
                              local.get 160
                              i32.const 4
                              i32.add
                              local.get 126
                              i32.atomic.store
                              ;; +8: DIRECT read of scanResults[DimX-1] (shared mem) at
                              ;; consumption time - discriminates scratch-handoff staleness
                              ;; (struct out-param clobbered) from shared-memory staleness.
                              local.get 160
                              i32.const 8
                              i32.add
                              i32.const ${SHARED_RB_ADDR}
                              i32.atomic.load
                              i32.atomic.store
                            end
                            local.get 158
                            local.get 159
                            i32.const 1
                            i32.add
                            i32.atomic.store
                            ;; === END CONS-TIMING RING ===`;
lines.splice(kAnchor + 1, 0, consRing);

writeFileSync('00_kernel_1_dbg3.wat', lines.join('\n'));
execSync('wat2wasm --enable-threads 00_kernel_1_dbg3.wat -o 00_kernel_1_dbg3.wasm');
console.log('built 00_kernel_1_dbg3.wasm (pub ring @' + PUBD + ', cons ring @' + COND + ')');
