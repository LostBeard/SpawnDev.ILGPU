# NVIDIA D3D12: a store into a function-scope array at a select index, inside a loop, writes the wrong element

2026-10-27, Data. Worked around in SpawnDev.ILGPU 5.4.9 (`WGSLCodeGenerator.FunctionArrayIndex`). Not yet reported upstream.

## The reproducer (plain WGSL, no ILGPU)

```wgsl
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
}
```

One workgroup of one invocation (`li` = 0, one loop iteration, `subset` = 0, `lowAt` = 0). Expected `out[0] = 100`.

| Where | `out` | |
|---|---|---|
| Chrome 156.0.8078.48, real adapter (NVIDIA RTX 4070, `nvidia/lovelace`, D3D12) | `out[8] = 100` | wrong |
| Chrome Canary 157.0.8079.0, same adapter | `out[8] = 100` | wrong |
| Edge 148.0.3967.54, same adapter | `out[8] = 100` | wrong |
| Chrome 156, SwiftShader (`--use-webgpu-adapter=swiftshader`) | `out[0] = 100` | right |
| node + `@kmamal/gpu` (Dawn, D3D12, FXC / DXBC) | `out[0] = 100` | right |

NVIDIA driver 32.0.15.9621 (2026-04-12), Windows 11 26200.

## Where it goes wrong

`--enable-dawn-features=dump_shaders,disable_symbol_renaming` dumps both stages, and both are correct:

- Tint's HLSL: `bool isz = (subset == int(0)); int lowAt = select(isz, int(0), int(8)); uint v_2 = min(uint(lowAt), 19u); cand[v_2] = int(100);`
- DXC's DXIL (`D3DCOMPILE_OPTIMIZATION_LEVEL3`, IEEE strictness): `%30 = icmp eq i32 %29, 0`, `%31 = select i1 %30, i32 0, i32 8`, `%32 = umin(%31, 19)`, `store i32 100, gep [20 x i32] %3, 0, %32`, with `%29` the loop phi starting at 0.

So the driver's compile of that DXIL stores at the other select arm. Writing `subset`, `lowAt` and `select(0, 1, isz)` to `out` in the same iteration shows 0, 0, 1 (all correct): only the store's address is wrong.

## What triggers it (all on the RTX 4070 / Chrome 156)

| Variant | Result |
|---|---|
| no loop, `isz = li == 0` | right |
| loop with a constant bound (`subset < 1`) | right |
| store into the storage buffer (`out[lowAt] = 100`) | right |
| a LOAD at the select index (`out[0] = cand[lowAt]`) | right |
| `select(9, 1, isz)` (no zero arm) | right |
| `select(3, 0, isz)`, `select(0, 8, !isz)`, array of 52 | wrong |
| if / else assigning a `var`, `8 * i32(!isz)`, `8 & -i32(!isz)`, `select(9, 1, isz) - 1` | wrong (DXC folds each to the same select) |
| `lowAt + (li - li)`, `lowAt + i32(arrayLength(&out) >> 31u)` | wrong (both fold to 0) |
| `lowAt + out[31]` (a zero loaded from the buffer) | right |

## The workaround

`_ilgpu_oz`, a module-scope `var<private> i32` set at kernel entry from an always-zero `_scalar_params` slot
(`ScalarPackingEntry.IsZeroSlot`), is added to the index of every store into a function-scope array whose index is,
past constant arithmetic and conversions, a select of two constants or a phi of constants. A buffer load cannot be
folded, so DXC no longer produces the select-with-zero-arm address. Loop-counter indices are left alone so they
stay foldable after unrolling (registers). Test: `FunctionArrayStore_SelectIndexInLoop_MatchesOracle`.

## Next

Report upstream with the reproducer (Dawn / Chromium GPU, which maintains driver workaround toggles, and/or NVIDIA).
Check again on new NVIDIA drivers; remove the workaround when fixed everywhere it matters.
