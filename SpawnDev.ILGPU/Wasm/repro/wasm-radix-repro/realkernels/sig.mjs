import { readFileSync } from 'fs';
for (const name of ['pass1', 'scan', 'pass2']) {
  const b = readFileSync(new URL(`./${name}.wasm`, import.meta.url));
  let p = 8; // magic + version
  const leb = () => { let r = 0, s = 0, x; do { x = b[p++]; r |= (x & 0x7f) << s; s += 7; } while (x & 0x80); return r; };
  let typeParamCounts = [], exports = [];
  while (p < b.length) {
    const id = b[p++]; const len = leb(); const end = p + len;
    if (id === 1) { // type section
      const n = leb();
      for (let i = 0; i < n; i++) { p++; /*form 0x60*/ const np = leb(); const ps = []; for (let j = 0; j < np; j++) ps.push(b[p++]); const nr = leb(); for (let j = 0; j < nr; j++) p++; typeParamCounts.push(np); }
    } else if (id === 7) { // export section
      const n = leb();
      for (let i = 0; i < n; i++) { const nl = leb(); const nm = Buffer.from(b.slice(p, p + nl)).toString(); p += nl; const k = b[p++]; const idx = leb(); exports.push(`${nm}#${idx}`); }
    }
    p = end;
  }
  // The 'kernel' export's func index; its type's param count = total kernel params (system + user).
  console.log(`${name}: exports=[${exports.join(', ')}]  typeParamCounts(first 6)=[${typeParamCounts.slice(0, 6).join(',')}]`);
}
