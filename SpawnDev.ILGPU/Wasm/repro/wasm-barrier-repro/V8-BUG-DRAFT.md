# DRAFT: V8 Bug Report - memory.atomic.wait32 corrupts locals in large Wasm functions

**DO NOT FILE UNTIL NOOP TEST CONFIRMS. This is a draft.**

## Summary

`memory.atomic.wait32` inside a barrier loop causes data corruption in Wasm functions with ~275 locals. Replacing wait32 with a stack-equivalent noop (drop drop drop i32.const 0) eliminates the corruption. The spin-loop equivalent (i32.atomic.load polling) also works correctly.

## Environment
- Chrome [version] on Windows 11
- V8 [version]
- 12 Web Workers with SharedArrayBuffer

## Reproduction

A GPU compute kernel compiled to Wasm performs a parallel radix sort using barrier synchronization. The kernel function has 275 locals and 20 barrier yield points. The barrier uses a generation-counter pattern:

```
Last worker: reset arrival -> fence -> bump gen -> notify
Other workers: while (load(gen) == savedGen) { wait32(gen, savedGen, -1); drop; }
```

When wait32 is present: 346-1016 sort order violations out of 1.4M elements (probabilistic, scales with element count / barrier transition count).

When wait32 is replaced with `drop drop drop i32.const 0` (same stack effect, no runtime call): 0 violations.

When the barrier uses `i32.atomic.load` spin loop instead of wait32: 0 violations.

## Analysis

wait32 requires a C++ runtime call through FutexEmulation::WaitWasm32(). This spills all 275 locals to the stack frame and reloads them on return. With 20 barriers per group and hundreds of groups, this creates thousands of spill/reload cycles. The spin loop has zero spill/reload cycles (stays in compiled Wasm).

## Hypothesis

The spill/reload mechanism has a rare corruption bug under high local count (275) that manifests probabilistically with repeated barrier cycles. This may be specific to Liftoff (baseline compiler) or a TurboFan register allocator edge case.

## Minimal reproduction

[Link to repo with .wasm binary, worker script, and HTML page]

Note: The minimal hand-written .wat reproduction does NOT trigger the bug (it has ~20 locals, not 275). The bug requires a large compiled Wasm function. We provide the actual compiled kernel binary for reproduction.

## Workaround

Using `i32.atomic.load` spin loop instead of `memory.atomic.wait32` for barrier synchronization. This works but wastes CPU (spinning vs sleeping).
