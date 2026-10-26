# WGSL helper functions that take addresses (views, `ref`) - 2026-10-26 (Data)

## Why

Tint's module step (WGSL resolve + uniformity) costs about (function-scope `var`s in scope) x (branches + loops) PER
FUNCTION (MEASURED 2026-10-26, node Dawn, 8,000 values / 4,000 branches: 1 function > 3 GB; 4 functions 953 MB; 8
functions 522 MB). After the var->let pass (5.4.6-local.3) the last Codecs kernel that still kills Chrome's GPU process is
`Vp8EntropyStepKernel`: 3,101 genuine phi vars x 13.9k branches in ONE 181k-line `main`. Real WGSL functions would split
that cost, but every helper with a `ref` or view parameter is inlined on WebGPU no matter what
(`InlineAddressParameterCalls`, `TakesAddress`), because the fn-def path miscompiled view arguments (SpawnScene
GpuEpipolarRansac, 2026-09-27). So `[MethodImpl(NoInlining)]` cannot split such a kernel (checked on Codecs'
`Vp8CoefBlockEncoderGpu.Encode`: `ref Vp8BoolEncoderGpuState` + 3 `ArrayView<byte>`; still one `main`).

## What is wrong today (WGSLFunctionGenerator.GetWgslViewParamType / GenerateHeaderStub, EmitNonInlinedMethodCall)

1. A view parameter is `ptr<storage|workgroup|function, array<T>>`: a pointer to the WHOLE binding/array. The view's
   element OFFSET is not passed: inside one function a SubView is an alias whose offset is applied at each LEA by walking
   the IR SubView chain (`WGSLCodeGenerator.GenerateCode(SubViewValue)`); in a helper that chain ends at the parameter,
   so a caller's sub-view offset is silently dropped. Only the coalesced-binding path passes (leader ptr, offset).
2. The view's LENGTH is not passed (`.Length` of a parameter view inside a helper has nothing to read).
3. A `ref`/pointer parameter (PointerType) is typed `TypeGenerator[pointer]`, not `ptr<function, T>` /
   `ptr<storage, T, read_write>`; the call site passes the caller's variable, not an address.
4. Pointer parameters into `storage` / `workgroup` need WGSL's `unrestricted_pointer_parameters` (Chrome has it); not
   declared today.

## Design

- **View parameter** = three WGSL parameters: `p_N : ptr<AS, array<T>[, read_write]>` (the root binding/array, as today),
  `p_N_offset : i32` (element offset of the view in it), `p_N_length : i32` (element count). Callers compute the offset
  by walking the argument's SubView/NewView chain to its root (the same walk the LEA override does) and pass the root's
  pointer. Inside the helper, LEA on `p_N` adds `p_N_offset`; GetViewLength on `p_N` reads `p_N_length`. The coalesced
  path keeps its leader-ptr form but gains the same length.
  Sub-word (`array<atomic<u32>>`) and emulated 64-bit (`array<u32>`) roots keep their existing element math; the offset
  is in ELEMENTS of the view, converted the same way the kernel's LEA converts.
- **Pointer parameter** (`ref T`, `out T`): `ptr<function, T>` for a function-local alloca (the common case: a state
  struct), `ptr<storage, T, read_write>` / `ptr<workgroup, T>` for an element of a binding; the call site passes `&v_x`
  (`&binding[idx]` for an element). Inside, Load/Store dereference `(*p_N)`, LoadFieldAddress gives `&(*p_N).field_K`.
- `requires unrestricted_pointer_parameters;` at the top when any helper takes a storage/workgroup pointer.
- `InlineAddressParameterCalls.TakesAddress` stays the gate, but answers "can the fn-def path express it" per parameter
  kind instead of "any address": views and `ref` to locals/elements -> expressible; anything else (a view stored inside a
  struct parameter, a pointer to a pointer, LocalMemory not yet covered) -> still inlined.

## Tests (red with today's code, every backend)

- A `[NoInlining]` helper taking a SUB-VIEW at an odd offset (byte and int views): reads and writes land at the offset.
- `.Length` of a view parameter inside the helper.
- `ref` to a local struct (int, int, long fields) through a `[NoInlining]` helper called in a loop (the Codecs bool
  encoder shape), checked against a CPU oracle.
- A helper taking a workgroup (`SharedMemory`) view.
- WGSL shape: the helper is a real `fn` (WGSL has it, `main` calls it); the whole-suite WGSL/GLSL/Wasm screen + naga on
  all Codecs kernels; both WebGPU lanes.
