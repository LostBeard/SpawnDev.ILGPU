// CI diagnostic: does this WebGPU device's shader compiler FUSE a multiply and an add (contraction)? The f64 emulation's
// error-free transforms (Veltkamp split, Dekker two-product) are exact only when every product is rounded on its own.
// x = y = 1 + 2^-12, z = 1 + 2^-11, read from a buffer (no constant folding):
//   unfused: round(x*y) = 1 + 2^-11 (a tie, to even), so x*y - z = 0
//   fused:   fma(x, y, -z) = 2^-24 exactly
// Each variant writes its x*y - z; the split writes whether c - (c - a) gave a 12-bit high part that sums back to a.
import { create, globals } from 'webgpu'
Object.assign(globalThis, globals)
const gpu = create((process.env.DAWN_OPTIONS || '').split(/\s+/).filter(Boolean))
const adapter = await gpu.requestAdapter()
if (!adapter) { console.log('FMA probe: no adapter'); process.exit(0) }
const device = await adapter.requestDevice()
const code = `
@group(0) @binding(0) var<storage, read> inp: array<f32>;
@group(0) @binding(1) var<storage, read_write> out: array<f32>;
@compute @workgroup_size(1) fn main() {
  let x = inp[0]; let y = inp[1]; let z = inp[2]; let a = inp[3]; let zero = inp[4]; let flag = inp[5] > 0.5;
  // 0: plain expression
  out[0] = x * y - z;
  // 1: product in a let first
  let p1 = x * y;
  out[1] = p1 - z;
  // 2: select with a runtime-false condition between the product and the add
  let p2 = select(x * y, 0.0, flag);
  out[2] = p2 - z;
  // 3: bitcast round trip of the product
  let p3 = bitcast<f32>(bitcast<u32>(x * y));
  out[3] = p3 - z;
  // 4: add a runtime zero to the product
  let p4 = x * y + zero;
  out[4] = p4 - z;
  // 5: Veltkamp split of a (as f64_split does): c = 4097 a; hi = c - (c - a); lo = a - hi
  let c = 4097.0 * a;
  let hi = c - (c - a);
  let lo = a - hi;
  let hiBits = bitcast<u32>(hi);
  let ok = (hi + lo == a) && ((hiBits & 0xFFFu) == 0u);   // a 12-bit-or-shorter significand: the low 12 mantissa bits clear
  out[5] = select(0.0, 1.0, ok);
  out[6] = hi; out[7] = lo;
}`
const module = device.createShaderModule({ code })
const pipeline = device.createComputePipeline({ layout: 'auto', compute: { module, entryPoint: 'main' } })
const inputs = new Float32Array([1 + 2 ** -12, 1 + 2 ** -12, 1 + 2 ** -11, 1.2345678, 0, 0])
const inBuf = device.createBuffer({ size: inputs.byteLength, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST })
device.queue.writeBuffer(inBuf, 0, inputs)
const outBuf = device.createBuffer({ size: 8 * 4, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC })
const readBuf = device.createBuffer({ size: 8 * 4, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST })
const bg = device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries: [{ binding: 0, resource: { buffer: inBuf } }, { binding: 1, resource: { buffer: outBuf } }] })
const enc = device.createCommandEncoder()
const pass = enc.beginComputePass(); pass.setPipeline(pipeline); pass.setBindGroup(0, bg); pass.dispatchWorkgroups(1); pass.end()
enc.copyBufferToBuffer(outBuf, 0, readBuf, 0, 32)
device.queue.submit([enc.finish()])
await readBuf.mapAsync(GPUMapMode.READ)
const r = new Float32Array(readBuf.getMappedRange().slice(0))
const info = adapter.info || {}
const verdict = v => v === 0 ? 'separate (0)' : v === 2 ** -24 ? 'FUSED (2^-24)' : `other (${v})`
console.log(`FMA probe on ${[info.vendor, info.architecture, info.description].filter(Boolean).join(' / ')}`)
console.log(`  x*y - z plain            : ${verdict(r[0])}`)
console.log(`  product in a let         : ${verdict(r[1])}`)
console.log(`  select(runtime false)    : ${verdict(r[2])}`)
console.log(`  bitcast round trip       : ${verdict(r[3])}`)
console.log(`  + runtime zero           : ${verdict(r[4])}`)
console.log(`  Veltkamp split of 1.2345678: ${r[5] === 1 ? 'exact' : 'WRONG'} (hi ${r[6]}, lo ${r[7]})`)
device.destroy()
process.exit(0)
