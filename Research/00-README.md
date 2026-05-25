# SpawnDev.ILGPU Research & Deep Reference

Deep technical reference for the hard parts of SpawnDev.ILGPU — the runtime IL→GPU transpiler that targets WebGPU (WGSL), WebGL (GLSL), and **WebAssembly (binary + multi-worker fiber dispatch)** in the browser, alongside CUDA/OpenCL/CPU via upstream ILGPU.

This is the SpawnDev.ILGPU counterpart to the [SpawnDev.RTC](https://github.com/LostBeard/SpawnDev.RTC) and [SpawnDev.WebTorrent](https://github.com/LostBeard/SpawnDev.WebTorrent) `Research/` folders. Two purposes:

1. **Internal ground truth** for implementers — the specs and models our codegen/dispatch must obey, distilled and applied to our actual code paths.
2. **Community documentation** — clear, correct references the wider WebAssembly/GPU-compute community currently lacks (especially the wasm threads memory model applied to a real fiber-threaded compute backend).

## Documents

| # | Document | Description | Status |
|---|----------|-------------|--------|
| 00 | [00-README.md](00-README.md) | This index | Complete |
| 01 | [01-wasm-memory-model-and-atomics.md](01-wasm-memory-model-and-atomics.md) | The WebAssembly threads memory model (Watt 2019 formal axioms + spec-author guidance) and how it governs our fiber phase dispatcher: seqcst-only atomics, `sequenced-before ⊆ happens-before`, `synchronizes-with` requires two seqcst accesses to the same byte range, **`atomic.fence` is a no-op given seqcst atomics**, non-atomic races are defined-but-nondeterministic (no UB). Includes the happens-before proof that our generation barrier is correct without fences, and the rule: fix wasm races as LOGIC races, never with fences. | Complete |

## Conventions

- Numbered docs, `00-README.md` is the index. Add new topics as `NN-topic.md` and register them in the table above.
- Cite primary sources (specs, papers, spec-author issue threads) with links. Per crew rule 4b/4c: read the source, don't paraphrase from memory; quote where precision matters.
- When you change code that a doc describes (e.g. the Wasm barrier atomics), update the doc in the same change.

## Related in-repo references

- `Wasm/CLAUDE.md` — Wasm backend constraints, "Barriers are PURE SPIN" verdict, tribal knowledge (POST-HELPER BARRIER rule, local-alloca-to-scratch, struct-load-must-copy, etc.).
- `Wasm/Notes/` — point-in-time investigation logs (fiber refactor implementation notes, wait/notify race investigation).
- `Plans/` — forward-looking implementation plans.
- `.claude/skills/ilgpu_transpiler/` — hard-won transpiler mapping rules.
