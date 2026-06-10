// Writer-stamp instrumentation (Seven 2026-06-10) - Geordi's aliasing discriminator
// (Captain's "what if"). Takes 00_kernel_1_dbg.wasm (ring-instrumented) and wraps EVERY
// i32.store / i64.store / i32.atomic.store in funcs 24+25 with a range check: if the
// store's effective byte range intersects ANY tid's boundaries-struct region
// [myScratch+1384, +1392), stamp {writerTid, valueLow32, siteId, slotOffset} into the
// OWNER tid's 8-entry ring. A corruption event whose ring shows a writer tid != owner
// = ALIASING IN OUR BACKEND; owner-only writers + value still reverting = coherence.
//
// Layout (N=16384/48w repro config; scratchBase=133120, stride=2392, 256 tids):
//   STAMPC = 1568768  per-owner write cursor (4B x 256) = total writes to the region
//   STAMPD = 1572864  per-owner ring, 128B stride (8 entries x 16B {tid,val,site,slot})
// Memory >= 25 pages (driver: DBG_KERNEL=2 -> 26 pages).
// Emits sitemap.json: siteId -> {watLine, instr} for decoding foreign-writer sites.
import { readFileSync, writeFileSync } from 'fs';
import { execSync } from 'child_process';

const SCRATCH_BASE = 133120, STRIDE = 2392, NTIDS = 256;
const REGION_LO = 1384, REGION_HI = 1392; // [lo, hi) within each tid's scratch
const STAMPC = 1568768, STAMPD = 1572864;

const wat = execSync('wasm2wat --enable-threads 00_kernel_1_dbg.wasm', { maxBuffer: 256 * 1024 * 1024 }).toString();
const lines = wat.split('\n');

// locate funcs 24 (kernel) and 25 (helper); func 26 = dispatcher (not instrumented:
// its stores are fence/yieldState only, but include it anyway for completeness? It
// writes yieldState + fence slots - outside the watched region; skip for size.)
const funcStarts = [];
for (let i = 0; i < lines.length; i++) if (lines[i].match(/\(func \(;\d+;\)/)) funcStarts.push(i);
const fIdx = n => lines.findIndex(l => l.includes(`(func (;${n};)`));
const f24 = fIdx(24), f25 = fIdx(25), f26 = fIdx(26);
if (f24 < 0 || f25 < 0 || f26 < 0) throw new Error('func layout unexpected');

// count locals+params per func to assign fresh local indices
function countSlots(declLine, funcLine) {
    const params = (funcLine.match(/\(param[^)]*\)/g) || []).join(' ').match(/i32|i64|f32|f64/g) || [];
    const locals = declLine.trim().startsWith('(local')
        ? (declLine.match(/i32|i64|f32|f64/g) || []) : [];
    return params.length + locals.length;
}

const sitemap = {};
let siteId = 0;

function instrumentFunc(startIdx, endIdx) {
    const funcLine = lines[startIdx];
    const declIdx = startIdx + 1;
    const hasDecl = lines[declIdx].trim().startsWith('(local');
    const nSlots = countSlots(hasDecl ? lines[declIdx] : '', funcLine);
    // fresh locals: A(addr i32), V(val i32), V64(val i64), R(rel i32), S(slot i32),
    //               O(owner i32), C(cursorAddr i32), E(cursor i32)
    const A = nSlots, V = nSlots + 1, R = nSlots + 2, S = nSlots + 3,
          O = nSlots + 4, C = nSlots + 5, E = nSlots + 6, V64 = nSlots + 7;
    if (hasDecl) lines[declIdx] = lines[declIdx].replace(/\)\s*$/, ' i32 i32 i32 i32 i32 i32 i32 i64)');
    else throw new Error('no local decl line; unexpected for these funcs');

    const out = [];
    for (let i = startIdx; i < endIdx; i++) {
        const t = lines[i].trim();
        const m = t.match(/^(i32|i64)\.(atomic\.)?store( offset=(\d+))?$/);
        if (!m) { out.push(lines[i]); continue; }
        const is64 = m[1] === 'i64';
        const off = m[4] ? parseInt(m[4], 10) : 0;
        const w = is64 ? 8 : 4;
        const id = siteId++;
        sitemap[id] = { watLine: i + 1, instr: t };
        // hit iff slot > REGION_LO - w && slot < REGION_HI
        const sLo = REGION_LO - w, sHi = REGION_HI;
        out.push(`      local.set ${is64 ? V64 : V}
      local.set ${A}
      local.get ${A}
      local.get ${is64 ? V64 : V}
      ${t}
      local.get ${A}
      i32.const ${off + w - 1}
      i32.add
      i32.const ${SCRATCH_BASE}
      i32.sub
      local.set ${R}
      local.get ${R}
      i32.const 0
      i32.ge_s
      if
        local.get ${R}
        i32.const ${STRIDE * NTIDS + 8}
        i32.lt_u
        if
          local.get ${A}
          i32.const ${off}
          i32.add
          i32.const ${SCRATCH_BASE}
          i32.sub
          local.set ${R}
          local.get ${R}
          i32.const ${STRIDE}
          i32.rem_u
          local.set ${S}
          local.get ${S}
          i32.const ${sLo}
          i32.gt_s
          local.get ${S}
          i32.const ${sHi}
          i32.lt_s
          i32.and
          if
            local.get ${R}
            i32.const ${STRIDE}
            i32.div_u
            local.set ${O}
            local.get ${O}
            i32.const 4
            i32.mul
            i32.const ${STAMPC}
            i32.add
            local.set ${C}
            local.get ${C}
            i32.atomic.load
            local.set ${E}
            local.get ${O}
            i32.const 128
            i32.mul
            local.get ${E}
            i32.const 7
            i32.and
            i32.const 16
            i32.mul
            i32.add
            i32.const ${STAMPD}
            i32.add
            local.set ${A}
            local.get ${A}
            local.get 5
            i32.atomic.store
            local.get ${A}
            local.get ${is64 ? V64 : V}${is64 ? '\n            i32.wrap_i64' : ''}
            i32.atomic.store offset=4
            local.get ${A}
            i32.const ${id}
            i32.atomic.store offset=8
            local.get ${A}
            local.get ${S}
            i32.atomic.store offset=12
            local.get ${C}
            local.get ${E}
            i32.const 1
            i32.add
            i32.atomic.store
          end
        end
      end`);
    }
    return out;
}

// instrument helper first (higher line numbers) then kernel, splicing back
const helperOut = instrumentFunc(f25, f26);
const kernelOut = instrumentFunc(f24, f25);
const result = [
    ...lines.slice(0, f24),
    ...kernelOut,
    ...helperOut,
    ...lines.slice(f26),
].join('\n');

writeFileSync('00_kernel_1_dbg2.wat', result);
writeFileSync('sitemap.json', JSON.stringify(sitemap, null, 1));
execSync('wat2wasm --enable-threads 00_kernel_1_dbg2.wat -o 00_kernel_1_dbg2.wasm');
console.log(`built 00_kernel_1_dbg2.wasm: ${siteId} store sites instrumented (kernel+helper)`);
