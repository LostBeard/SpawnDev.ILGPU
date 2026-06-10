// Builds 00_kernel_1_dbg.wasm: the real scan kernel + a per-tid DEBUG RING write in
// the helper's final phase (ph3), immediately after the rightBoundary load
// (scanResults[DimX-1] -> local 54). Per execution it records, per tid:
//   ring[tid][cursor] = { rbObserved (i32), scanResults[DimX-2] (i32) }   (8 bytes)
//   cursor[tid]++   (cursor table is itself the per-tid ph3 EXECUTION COUNT)
// Layout (fixed, valid for the N=16384/48w repro layout which ends at 851,968):
//   DBGC = 851968  cursor table, 4B per tid (256 tids = 1KB)
//   DBGD = 856064  rings, 1024B per tid (128 entries x 8B), 256KB total
// Memory must be >= 18 pages (harness bumps pages when DBG_KERNEL=1).
// Patch points (verified against wasm2wat output of 00_kernel_1.wasm):
//   - helper func 25 local decl: 45 i32 locals (indices 14..58) -> add 3 (59,60,61)
//   - insertion anchor: the UNIQUE 'i32.atomic.load' + 'local.set 54' pair inside the
//     ph3 block (the other local.set 54 is the restore prologue, preceded by plain i32.load)
import { readFileSync, writeFileSync } from 'fs';
import { execSync } from 'child_process';

const DBGC = 851968, DBGD = 856064;
// Ring 2: the CONSUMPTION side - in the kernel's carry-update step, after the atomic
// load of boundaries.RightBoundary ([myScratch+1388] -> local 126, added to carry
// local 122). Records {rbConsumed, carryBefore} per execution per tid.
const DBGC2 = 852992, DBGD2 = 1118208;
const wat = execSync('wasm2wat --enable-threads 00_kernel_1.wasm', { maxBuffer: 64 * 1024 * 1024 }).toString();
const lines = wat.split('\n');

// 1. Find helper func 25 start
const fStart = lines.findIndex(l => l.includes('(func (;25;)'));
if (fStart < 0) throw new Error('func 25 not found');
// 2. Extend its local decl (next line)
const declIdx = fStart + 1;
if (!lines[declIdx].trim().startsWith('(local')) throw new Error('helper local decl not where expected');
lines[declIdx] = lines[declIdx].replace(/\)\s*$/, ' i32 i32 i32)');

// 3. Find the ph3 anchor: 'local.set 54' preceded by 'i32.atomic.load' within func 25
const fEnd = lines.findIndex((l, i) => i > fStart && l.includes('(func (;26;)'));
let anchor = -1;
for (let i = fStart; i < fEnd; i++) {
    if (lines[i].trim() === 'local.set 54' && lines[i - 1].trim() === 'i32.atomic.load') { anchor = i; break; }
}
if (anchor < 0) throw new Error('ph3 anchor not found');

// Ring1 records are 16B: {rbRead(local54 at read), tempBack([55+4] after plain store),
// outBack([13+4] after atomic copy), local54Again(at ring1c time)} - brackets every link
// of the chain scanResults -> local54 -> structTemp -> outParam.
const dbg = `
          ;; === DEBUG RING (Seven 2026-06-09): record rb observation per tid ===
          local.get 5
          i32.const 4
          i32.mul
          i32.const ${DBGC}
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
            i32.const ${DBGD}
            i32.add
            local.set 61
            local.get 61
            local.get 54
            i32.atomic.store
          end
          local.get 59
          local.get 60
          i32.const 1
          i32.add
          i32.atomic.store
          ;; === END DEBUG RING ===`;
lines.splice(anchor + 1, 0, dbg);

// Ring 1c: immediately after the PLAIN store '[55+4] = local54' - read back the temp
// and re-record local 54. Anchor: first 'local.get 54' + 'i32.store' pair after ring1.
let anchor1c = -1;
for (let i = anchor + 2; i < fEnd + 1; i++) {
    if (lines[i].trim() === 'i32.store' && lines[i - 1].trim() === 'local.get 54') { anchor1c = i; break; }
}
if (anchor1c < 0) throw new Error('ring1c anchor (local.get 54 + i32.store) not found');
const dbg1c = `
          ;; === DEBUG RING 1c: read-back structTemp [55+4] after plain store ===
          local.get 61
          i32.const 4
          i32.add
          local.get 55
          i32.const 4
          i32.add
          i32.atomic.load
          i32.atomic.store
          ;; === END DEBUG RING 1c ===`;
lines.splice(anchor1c + 1, 0, dbg1c);

// Ring 1b: read-back of the out-param rb field RIGHT AFTER the helper's atomic
// store to [param13+4] (the 2nd original atomic.store after the anchor; inserted
// blocks are single array elements so the line scan skips them).
let storeCount = 0, anchor1b = -1;
for (let i = anchor1c + 2; i < fEnd + 2; i++) {
    if (lines[i].trim() === 'i32.atomic.store') { storeCount++; if (storeCount === 2) { anchor1b = i; break; } }
}
if (anchor1b < 0) throw new Error('ring1b anchor (2nd atomic.store) not found');
const dbg1b = `
          ;; === DEBUG RING 1b: read-back BOTH out-param fields after the atomic copies ===
          local.get 61
          i32.const 8
          i32.add
          local.get 13
          i32.const 4
          i32.add
          i32.atomic.load
          i32.atomic.store
          local.get 61
          i32.const 12
          i32.add
          local.get 13
          i32.atomic.load
          i32.atomic.store
          ;; === END DEBUG RING 1b ===`;
lines.splice(anchor1b + 1, 0, dbg1b);

// ---- Ring 2: kernel func 24, consumption site ----
const kStart = lines.findIndex(l => l.includes('(func (;24;)'));
if (kStart < 0) throw new Error('func 24 not found');
const kDeclIdx = kStart + 1;
if (!lines[kDeclIdx].trim().startsWith('(local')) throw new Error('kernel local decl not where expected');
lines[kDeclIdx] = lines[kDeclIdx].replace(/\)\s*$/, ' i32 i32 i32)'); // locals 158,159,160
const kEnd = lines.findIndex((l, i) => i > kStart && l.includes('(func (;25;)'));
let kAnchor = -1;
for (let i = kStart; i < kEnd; i++) {
    if (lines[i].trim() === 'local.set 126' && lines[i - 1].trim() === 'i32.atomic.load') { kAnchor = i; break; }
}
if (kAnchor < 0) throw new Error('consumption anchor not found');
const dbg2 = `
                            ;; === DEBUG RING 2 (consumption): rbConsumed + carryBefore ===
                            local.get 5
                            i32.const 4
                            i32.mul
                            i32.const ${DBGC2}
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
                              i32.const ${DBGD2}
                              i32.add
                              local.set 160
                              local.get 160
                              local.get 126
                              i32.atomic.store
                              local.get 160
                              i32.const 4
                              i32.add
                              local.get 122
                              i32.atomic.store
                              local.get 160
                              i32.const 8
                              i32.add
                              local.get 125
                              i32.atomic.store
                            end
                            local.get 158
                            local.get 159
                            i32.const 1
                            i32.add
                            i32.atomic.store
                            ;; === END DEBUG RING 2 ===`;
lines.splice(kAnchor + 1, 0, dbg2);

writeFileSync('00_kernel_1_dbg.wat', lines.join('\n'));
execSync('wat2wasm --enable-threads 00_kernel_1_dbg.wat -o 00_kernel_1_dbg.wasm');
console.log('built 00_kernel_1_dbg.wasm');
