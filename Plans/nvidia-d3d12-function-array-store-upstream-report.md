# Upstream report draft: NVIDIA D3D12 stores a function-scope array element at the wrong index

Status: **draft, not filed.** Written 2026-10-27 so it can be filed as is (TJ decides whether and where). Investigation notes:
[nvidia-d3d12-function-array-store-2026-10-27.md](nvidia-d3d12-function-array-store-2026-10-27.md). SpawnDev.ILGPU works around it
since 5.4.9 (`WGSLCodeGenerator.FunctionArrayIndex`), so nothing of ours waits on a fix.

Where it could go:

- **Chromium / Dawn** (issues.chromium.org, component `Blink>WebGPU`). Dawn keeps per-driver workaround toggles, so it can protect
  every WebGPU page on the affected drivers. The most useful place to file.
- **NVIDIA** (developer forums, or the NVIDIA developer bug form). Only NVIDIA can fix the driver itself.

---

## Title

D3D12 on NVIDIA (RTX 4070, driver 32.0.15.9621): store into a function-scope array at `select(K, 0, cond)` inside a loop writes element K

## Summary

Inside a `loop` with a trip count only known at run time, `cand[select(8, 0, isz)] = 100;` with `isz` true writes `cand[8]`
instead of `cand[0]`. The HLSL Tint generates and the DXIL DXC produces are both correct, and SwiftShader and FXC give the right
answer, so the wrong address appears in the driver's compile of the DXIL.

## Environment

| | |
|---|---|
| GPU | NVIDIA GeForce RTX 4070 (`nvidia` / `lovelace`), D3D12 backend |
| Driver | 32.0.15.9621 (2026-04-12) |
| OS | Windows 11 Home 10.0.26200 |
| Browsers (all wrong) | Chrome 156.0.8078.48, Chrome Canary 157.0.8079.0, Edge 148.0.3967.54 |
| Right | the same Chrome with `--use-webgpu-adapter=swiftshader`; Dawn in node (`@kmamal/gpu`, D3D12 with FXC / DXBC) |

## Reproducer

Save as `repro.html`, serve it over `http://localhost` (WebGPU needs a secure context) and open it. One invocation runs one loop
iteration with `subset = 0`, so `lowAt = select(8, 0, true) = 0`.

```html
<!doctype html>
<html>
<head><meta charset="utf-8"><title>Function-array store at a select index</title></head>
<body>
<pre id="log">running...</pre>
<script>
// One invocation, one loop iteration (subset = 0), so lowAt = select(8, 0, true) = 0 and cand[0] must be 100.
const code = `
@group(0) @binding(0) var<storage, read_write> out : array<i32>;
@compute @workgroup_size(1)
fn main(@builtin(local_invocation_index) li : u32) {
  var cand : array<i32, 20>;
  for (var i = 0; i < 20; i++) { cand[i] = 0; }
  var subset : i32 = 0;
  let subsets : i32 = 1 + i32(li);
  loop {
    if (!(subset < subsets)) { break; }
    let isz : bool = subset == 0;
    let lowAt : i32 = select(8, 0, isz);
    cand[lowAt] = 100;
    for (var k = 0; k < 20; k++) { out[k] = cand[k]; }
    subset = subset + 1;
  }
}`;
window.reproDone = (async () => {
  const log = document.getElementById('log');
  const adapter = await navigator.gpu?.requestAdapter();
  if (!adapter) return (log.textContent = 'WebGPU not available');
  const info = adapter.info ?? {};
  const device = await adapter.requestDevice();
  const module = device.createShaderModule({ code });
  const pipe = await device.createComputePipelineAsync({ layout: 'auto', compute: { module, entryPoint: 'main' } });
  const U = GPUBufferUsage;
  const out = device.createBuffer({ size: 20 * 4, usage: U.STORAGE | U.COPY_SRC });
  const rb = device.createBuffer({ size: 20 * 4, usage: U.MAP_READ | U.COPY_DST });
  const enc = device.createCommandEncoder();
  const pass = enc.beginComputePass();
  pass.setPipeline(pipe);
  pass.setBindGroup(0, device.createBindGroup({ layout: pipe.getBindGroupLayout(0), entries: [{ binding: 0, resource: { buffer: out } }] }));
  pass.dispatchWorkgroups(1);
  pass.end();
  enc.copyBufferToBuffer(out, 0, rb, 0, 20 * 4);
  device.queue.submit([enc.finish()]);
  await rb.mapAsync(GPUMapMode.READ);
  const v = Array.from(new Int32Array(rb.getMappedRange()));
  const ok = v[0] === 100 && v.every((x, i) => i === 0 || x === 0);
  return (log.textContent = `adapter: ${info.vendor} / ${info.architecture} / ${info.device} / ${info.description}\n` +
    `out: ${v.join(' ')}\n` +
    (ok ? 'PASS: out[0] = 100' : `FAIL: expected out[0] = 100 and the rest 0; 100 landed at index ${v.indexOf(100)}`));
})().catch(e => (document.getElementById('log').textContent = 'error: ' + e));
</script>
</body>
</html>
```

Measured 2026-10-27 with this exact page (headless Chrome 156, isolated profile):

```
adapter: nvidia / lovelace /  /
out: 0 0 0 0 0 0 0 0 100 0 0 0 0 0 0 0 0 0 0 0
FAIL: expected out[0] = 100 and the rest 0; 100 landed at index 8
```

```
adapter: google / swiftshader /  /
out: 100 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0
PASS: out[0] = 100
```

## Expected

`out[0] = 100`, every other element 0.

## Actual

`out[8] = 100`: the value is stored at the select's other operand.

## Why the driver

With `--enable-dawn-features=dump_shaders,disable_symbol_renaming` both stages Chrome hands to the driver are correct:

- Tint's HLSL: `bool isz = (subset == int(0)); int lowAt = select(isz, int(0), int(8)); uint v_2 = min(uint(lowAt), 19u); cand[v_2] = int(100);`
- DXC's DXIL (`D3DCOMPILE_OPTIMIZATION_LEVEL3`, IEEE strictness): `%30 = icmp eq i32 %29, 0`, `%31 = select i1 %30, i32 0, i32 8`,
  `%32 = umin(%31, 19)`, `store i32 100, gep [20 x i32] %3, 0, %32`, with `%29` the loop phi that starts at 0.

Writing `subset`, `lowAt` and `select(0, 1, isz)` to `out` in the same iteration gives 0, 0 and 1, all correct; only the store's
address is wrong.

## What triggers it (RTX 4070, Chrome 156)

| Variant | Result |
|---|---|
| no loop, `isz = li == 0` | right |
| loop with a constant bound (`subset < 1`) | right |
| store into the storage buffer instead (`out[lowAt] = 100`) | right |
| a load at the select index (`out[0] = cand[lowAt]`) | right |
| `select(9, 1, isz)` (no zero operand) | right |
| `select(3, 0, isz)`, `select(0, 8, !isz)`, an array of 52 | wrong |
| if / else assigning a `var`, `8 * i32(!isz)`, `8 & -i32(!isz)`, `select(9, 1, isz) - 1` | wrong (DXC folds each to the same select) |
| `lowAt + (li - li)`, `lowAt + i32(arrayLength(&out) >> 31u)` | wrong (both fold to 0) |
| `lowAt + out[31]` (a zero loaded from the buffer) | right |

So it needs a function-scope array, a store (not a load), a loop with a run-time trip count, and an index that DXC turns into a
select with a zero operand.

## How it was found

SpawnDev.ILGPU compiles C# kernels to WGSL. A BC7 texture encoder (SpawnDev.Codecs) stores candidate endpoints with
`cand[lowAt + c] = res[c]`, `lowAt = subset == 0 ? 0 : 8`; on this GPU subset 0's endpoints landed in subset 1's slots, on WebGPU
only. The kernel was reduced to the 15 lines above.

## Workaround we ship

Adding a zero the compiler cannot prove is zero to such an index avoids it: SpawnDev.ILGPU reads an always-zero word from a buffer
at kernel entry (`var<private>`) and adds it to the index of every store into a function-scope array whose index is a choice
between constants (`lowAt + out[31]` in the table above).
