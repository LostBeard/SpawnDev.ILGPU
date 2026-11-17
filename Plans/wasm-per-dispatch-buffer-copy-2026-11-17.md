# Wasm: per-dispatch buffer copy makes small launches over big buffers slow (2026-11-17, Data)

Status: PROPOSAL, measured, nothing built. Needs TJ's go (a structural change to a backend with a long race history).

## Found

SpawnDev.Codecs' VP9 decode on Wasm vs WebGPU (Geordi, ILGPU 5.4.35, 1,178 frames, mean ms per frame): serial
stages are FASTER on Wasm (compressed header 3.18 vs 3.97, tile parse 10.10 vs 16.66), but the two wavefront stages
are 81x and 221x slower: reconstruction 1,101 vs 13.66, loop filter 1,144 vs 5.18. Both launch one small kernel per
wavefront diagonal (~46 per 1080p frame), each taking the whole frame pool buffer.

## Measured (test `LaunchCost_WasmPoolSize`, Wasm lane, 40 launches of a 64-thread kernel touching 64 ints)

| pool buffer | ms per launch |
|---|---|
| 64 KB | 0.25 |
| 1 MB | 1.44 |
| 8 MB | 2.30 |
| 32 MB | 12.58 |

Cost scales with the BUFFER, not the work. Why: a Wasm buffer lives in its own SharedArrayBuffer, and every dispatch
(a pipelined one in its worker, otherwise on the host) zeroes the dispatch region of the shared linear memory, copies
each argument's used range in, runs, and copies it back out. A kernel handed the whole pool pays three passes over it
(~96 MB of memory traffic at 32 MB) for 256 bytes of work. ILGPU cannot know which elements a kernel touches, so the
"used range" is the whole view.

## Options

**A. Buffers resident in linear memory.** Allocate every buffer inside the shared WebAssembly.Memory (a sub-allocator)
and let kernels address it in place: no zero, no copy-in, no copy-out, host access through views of memory.buffer.
The WebGPU model. Largest win, largest change: every live buffer must fit in the linear memory (wasm32: 4 GiB at most,
1 GiB default cap), so ML-size working sets need a fallback to today's path (a hybrid); allocator, fragmentation and
grow handling are new; the host-write ordering and snapshot logic are rebuilt around it.

**B. Residency cache (recommended).** Keep buffers where the last dispatch put them: after a dispatch, leave each
buffer's bytes in linear memory, mark the linear copy authoritative and skip its copy-out. The next dispatch that uses
the same buffer at the same offset skips the zeroing and the copy-in for it. The bytes go back to the SharedArrayBuffer
only when something needs them there: a host read or write, a device copy, a dispatch whose layout would overlap the
region, a grow, the buffer's Dispose (after the work queued before it, as 5.4.35 does). Needs stable offsets for
resident buffers across dispatches (place them first, at the offsets they already have). Gets nearly all of the win
for the back-to-back launch pattern (the VP9 wavefront, ML layer loops), no capacity change, falls back to today's
path whenever anything is unusual. Risk: one more state per buffer (where its current bytes live) that every host
path must respect; the full Wasm suite plus Codecs' and ML's Wasm lanes are the gate.

**C. Small, independent: stop zeroing what copy-in overwrites.** The zero pass covers the buffer area too ("a kernel
reading outside the copied ranges gets garbage"); zeroing only the gaps saves about a third. Worth doing inside B
anyway.

## Plan if B is approved

1. A per-buffer residency record (memory, offset, length, dirty) in WasmMemoryBuffer; every host path (CopyFromCPU /
   staged host write, CopyToHost / CopyToRawAsync, MemSet, device copy, Dispose, snapshot) first evicts (copies the
   linear bytes back) when the buffer is resident and dirty.
2. Layout: resident buffers keep their offsets; new arguments are placed after them; an overlap or a grow evicts.
3. Pipelined dispatches: the worker skips zero/copy-in/copy-out for resident ranges (the host decides; the message
   carries a flag per range).
4. Gate: WasmTests (all), the new LaunchCost_WasmPoolSize numbers, Codecs WasmCodecsTests, ML Wasm lane; tests for
   every eviction trigger (host read after N launches, host write between launches, two accelerators sharing the
   memory, grow, dispose with work pending).
