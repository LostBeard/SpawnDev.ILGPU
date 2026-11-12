# Upstream report draft: WebGPU on D3D12 / NVIDIA loses the device (DXGI_ERROR_DEVICE_HUNG) replaying a recorded call sequence

Status: **draft, not filed.** Written 2026-10-29 to be filed together with
[nvidia-d3d12-function-array-store-upstream-report.md](nvidia-d3d12-function-array-store-upstream-report.md). SpawnDev.Codecs
works around it (a queue drain after one kernel, Codecs 8d0efe6), so nothing of ours waits on a fix.

Where it could go:

- **Chromium / Dawn** (issues.chromium.org, component `Blink>WebGPU`). First stop: the reproducer is a plain WebGPU call trace,
  and Dawn can tell whether the hang is in its D3D12 backend or in the driver.
- **NVIDIA** (developer forums or the NVIDIA developer bug form), if Dawn routes it there.

Attachments: the folder `D:\users\tj\Projects\_upstream_reports\vp9-webgpu-device-hung\` (zip it): `index.html` (replay page),
`replay.js`, `trace.jsonl` (4.9 MB, the recording), `reduced.json` (records to skip for the reduced trace), `tracer.js` (the
recorder, for reference).

---

## Title

D3D12 on NVIDIA (RTX 4070, driver 32.0.15.9621): replaying a recorded sequence of compute dispatches loses the device with
DXGI_ERROR_DEVICE_HUNG

## Summary

A WebGPU page (SpawnDev.Codecs' VP9 encoder tests, compute only) loses its device with `DXGI_ERROR_DEVICE_HUNG`. The trigger is
an order of work, not one shader: the hang needs a compute kernel with workgroup barriers to still be running when the next
dispatches are queued, and it appears on a later, small dispatch that runs fine everywhere else. Every WebGPU call of the failing
page was recorded and the recording is replayed in a fresh page with no application code: the replay hangs the same way, at
the same record.

Error text (from Chrome, both the original page and the replay):

```
ID3D12Device::GetDeviceRemovedReason failed with DXGI_ERROR_DEVICE_HUNG (0x887A0006)
 - While handling unexpected error type Internal when allowed errors are Validation.
    at CheckHRESULTImpl (..\..\third_party\dawn\src\dawn\native\d3d\D3DError.cpp:121)
```

## Environment

| | |
|---|---|
| GPU | NVIDIA GeForce RTX 4070 (`nvidia` / `lovelace`), D3D12 backend (Chrome's default) |
| Driver | 32.0.15.9621 (2026-04-12) |
| OS | Windows 11 Home 10.0.26200 |
| Browser | Chrome 156.0.8078.48 |

## Reproducer

1. Unzip the attachment and serve the folder over `http://localhost` (for example `python -m http.server 8000`).
2. Open `http://localhost:8000/index.html` in Chrome.
3. Click **Replay the full trace**. On the system above the page reports `DEVICE LOST` at record 1288 or 1289 (the screen may
   flash once: Windows resets the display driver).

**Replay the reduced trace** drops 657 of the 1316 records (`reduced.json`) and still hangs.

What the replay does: `replay.js` reads `trace.jsonl`, which `tracer.js` recorded by wrapping the WebGPU prototypes of the
original page: every call in order (`createBuffer`, `writeBuffer` with its data, `createShaderModule`, `createComputePipeline`,
`createBindGroup`, command encoders, compute passes, `copyBufferToBuffer`, `clearBuffer`, `submit`, `mapAsync`, ...), plus the
point at which each promise settled. The replay reissues the calls in record order on a new device and awaits each promise where
the original page saw it settle, so the queue holds the same work at the same times.

Fidelity: the replay of a passing run of the same tests (not attached) reissued all 1160 records with no error and its 10
readbacks were byte-identical to the original page's. The full failing trace replayed twice: both hung at the same record.

## What the reduced trace contains

Besides buffer, shader and pipeline creation and the data uploads, three submits are left. Removing submit 1 or submit 2
(tested before the per-dispatch cuts) made the hang go away; submit 3 holds the dispatch that hangs.

1. One earlier encode's submit, reduced to no dispatches.
2. A submit with `Vp9EncodeWavefrontGroupKernel` (3 workgroups of 4 invocations; `workgroupBarrier()` once per loop iteration)
   followed by one `Vp9EntropyStepKernel` dispatch (1 workgroup) that reads buffers the first kernel writes. Both are required:
   without the second dispatch, or without the first, there is no hang.
3. One later dispatch of `Vp9EncodeWavefrontBatchKernel` (1 workgroup of 64 invocations, 3 of them do work, the rest return at
   once). This is the one that never completes.

In the original application, one `onSubmittedWorkDone()` wait after the barrier kernel (before the second dispatch is queued)
prevents the hang. Waiting 3 s at the test boundary does not.

## Ruled out

- **The shaders.** Every loop in the WGSL of the dispatch that hangs has a constant bound, and dispatched on its own it completes
  in milliseconds on this GPU (the barrier kernel too: its loop counts do not depend on its input data). The same dispatches
  pass when the barrier kernel has finished before the next dispatch is queued.
- **Compile timing** (tested in the application). Creating the barrier kernel's pipeline asynchronously ahead of time does not help.
- **Submission shape** (tested in the application). One compute pass per dispatch versus several dispatches per pass, one
  submit per dispatch versus one per batch: no difference.
- **Memory.** Chrome's GPU process peaked at about 2 GB of private memory on a 16 GB system.
- **The application's runtime.** The replay runs no application code.

Not yet narrowed: whether Dawn's D3D12 backend or the driver is at fault. A different GPU vendor was not available to compare.

## Second trigger (added 2026-11-10, Data + Geordi) - attach as case 2

Same error, same victim dispatch, a different lead-in: a full VP9 decode (SpawnDev.Codecs' `Vp9DecoderGpu`, which runs
compute kernels with workgroup barriers: the loop filter and intra wavefronts) instead of the encoder's own barrier kernel.
Page flow: encode 64x64 (q30), decode it with the full GPU decoder (a host wait after each step), encode 64x64 again (q120):
the second encode's first small wavefront dispatch dies with DXGI_ERROR_DEVICE_HUNG, 3 of 3 in the application.

Recorded with the same `tracer.js` (Codecs PMT `PMT_INIT_SCRIPT`), 855 records, the original page's device lost at record
826. Replayed in a fresh installed Chrome (own profile, no application code): **device lost with the same
DXGI_ERROR_DEVICE_HUNG at record 827, 2 of 2**. Chrome 157.0.8103.39, driver 32.0.15.9621 (NVIDIA 596.21), RTX 4070.

Attachment: `D:\users\tj\Projects\_upstream_reports\vp9-webgpu-device-hung-case2\` (index.html, replay.js, trace.jsonl
6.7 MB, tracer.js). Not reduced: every reduction step is a deliberate device reset on TJ's machine, so it waits for his OK
(the full trace already reproduces). The application keeps its workaround (a queue drain after the barrier kernel) on
the encoder path; extending it to the decoder path waits for TJ (Geordi, Codecs Docs/status.md).

Case 2 differs from case 1 on the drain (Geordi, 2026-11-10 15:45, watched, TJ-approved test): a queue drain
(`SynchronizeAsync`) after the decoder's loop-filter wavefront kernel does NOT prevent the hang, and the page already waits
for the GPU to go idle after every whole step. So encode -> full decode -> fully idle GPU -> encode still loses the device.
Without the old keyframe decoder it still hangs; with encodes only (no decoder) it passes. The trigger is state the full
decoder's work leaves behind, not work still in flight. Worth saying in the report: it points at the driver/Dawn keeping
something (a resource, residency or a pipeline state) across an idle point.

### Case 2 reduction (TJ 2026-11-11: OK if needed, never while Geordi or Tuvok has GPU work running)
Purpose: not the report (the full trace already reproduces without our code) but a decoder-path workaround for Codecs,
which has none (a drain does not prevent it). Trace map (dispatches only; 52 in all): encode 1 at 119-226 (submit 229),
the old keyframe decoder's Vp9KeyframeDecodeStepKernel at 338/341, decoder at 425 (CopyBytes), 493-520 (CopyContext x10),
527 CompressedHeader, 544 ParseKernel, 551 IntraWaveKernel, 555 SumCounts, 562 Adapt, 566 PackPlanes (submit 569),
encode 2 at 705-812 (submit 815, lost 826). The recorded decode is one 64x64 keyframe: NO loop-filter WavefrontKernel and
no inter kernels, so the shared-memory barrier kernel is not the trigger. Cuts drop only dispatchWorkgroups records (all
buffer and pipeline creation stays): A no decoder dispatches, B no keyframe-decoder dispatches, C both, then one kernel at
a time. Generator: scratchpad hang2/cuts.py; driver: scratchpad wgtrace/replaydrive.cs (skip-list argument).

### Case 2 reduction RESULTS (Data, 2026-11-11, window with Geordi and Tuvok idle; fresh installed Chrome, no app code)
8 replays, 5 device losses. Dispatch records dropped, every buffer / pipeline creation kept.
| cut | result |
|---|---|
| full trace (control) | LOST at 827 (3 of 3 across days) |
| drop all 17 decoder dispatches | alive |
| drop Parse .. PackPlanes | alive |
| keep through IntraWave, drop SumCounts .. PackPlanes | LOST |
| keep through Parse, drop IntraWave .. | alive |
| IntraWave alone (other decoder dispatches dropped: zero-initialized inputs) | alive |
| keep every decoder buffer alive (68 destroys skipped) | LOST |
| drop encode 1's dispatches | LOST |
| minimal: no encode 1, no keyframe decoder, decoder through IntraWave, then encode 2 | LOST at 828 |
So the trigger is the IntraWaveKernel dispatch (one workgroup) running on the REAL output of Parse (data-dependent), not
pipeline or buffer creation, not memory reuse, not encode 1. IntraWave's WGSL: 15.8k lines, 84 loops, 494 atomics, no
workgroupBarrier or subgroup operations. The device survives IntraWave (onSubmittedWorkDone resolves) and is lost on the
next big kernel (the encoder's wavefront, 27.7k lines) after the GPU went idle. Minimal skip list:
`_upstream_reports/vp9-webgpu-device-hung-case2/reduced.json`. Next (Geordi, Codecs): bisect IntraWave's paths live.

### Case 2: out-of-range access ruled out (2026-11-11)
- Geordi: the same sequence (seed 0x5EED, 64x64 q30 encode -> Vp9DecoderGpu decode -> q120 encode) on a CPU accelerator
  with OptimizationLevel.Debug (ArrayView bounds assertions in every kernel) ran clean; red-checked (src[src.Length] fails).
- Data: the full trace with EVERY storage index of IntraWaveKernel's recorded WGSL clamped (1,147 accesses: `paramN[i]` ->
  `paramN[min(u32(i), arrayLength(&paramN) - 1u)]`, incl. the 284 atomic byte operands; naga-valid): still LOST at 828. (A peer's 2-minute CUDA test run overlapped the start of
  this replay through crossed messages; it finished green, and the loss came at the same record as every earlier run.)
- IntraWave has no function-scope arrays (so not the NVIDIA function-array-store bug, which 5.4.9 works around).
So neither the C# indices nor the WGSL lowering access storage out of range. Left: a code path inside IntraWave that only
real data reaches poisons the D3D12 / NVIDIA device for the next big kernel. Next: bisect IntraWave's main body with an
early `return;` at top-level statement boundaries of the recorded WGSL (~8-10 resets, a dedicated window).
- Window 2 (both peers confirmed idle first): early `return;` as the first statement of InverseTransformAdd (DCT 16 / 8 and
  DC-only adds): still LOST at 828; of Predict (the DC predictors this keyframe uses): still LOST at 828. So neither helper
  alone is required. Next (1 reset): both disabled (trace_noboth, naga-valid). Alive -> either helper suffices; lost -> the
  trigger is in main's own code (the 16-block x 3-plane loop, eob / coefficient reads, byte atomics): bisect main.
- Window 3 (both peers confirmed idle first; the replay now logs every GPU wait's duration):
  - no-reset timing (stopped before encode 2): the decode submit's readback settles after 148.6 ms with IntraWave and
    129.6 ms without, so IntraWave runs ~19 ms and Parse et al ~130 ms (Parse: one thread for the frame's one tile).
  - IntraWave's WGSL REPLACED by a synthetic kernel (one thread, an LCG loop, touches no memory at run time; all 8 bindings
    declared so the recorded bind group stays valid): 1e7 iterations LOST at 828; 1e3 iterations (practically instant)
    LOST at 828.
  Combined with the earlier cuts (Parse on real data with nothing dispatched after it in that submit: alive; IntraWave
  alone on zeroed inputs, no Parse: alive): ParseKernel running long on real data, FOLLOWED BY ANY dispatch, poisons the
  device; it is lost on the next big kernel later, after the GPU went idle. Not IntraWave's code, not out-of-range access,
  not the second dispatch's length. Candidate mechanism: WDDM mid-dispatch preemption of the long single-thread dispatch
  plus a following dispatch in the same command buffer. Next: a synthetic-only repro (long spin + short dispatch + a big
  kernel, no codec code) for the report, and a workaround test (a submit boundary right after Parse; or Parse split into
  shorter launches).
- WORKAROUND CONFIRMED (Geordi, live, 2026-11-11): `await SynchronizeAsync()` right after the ParseKernel launch (a submit
  boundary plus wait; WebGPU only) -> Vp9DecoderGpu_DecodesOwnKeyframeEncoder_LikeKeyframeDecoder PASSED on WebGPU (43.9 s,
  GPU process peak 0.67 GB); it was 5 of 5 DEVICE_HUNG before. In the trace, Parse (544) and the next dispatch (551) were
  already in separate compute passes of one command encoder, so a pass boundary is not enough; the submit boundary is.
  Codecs keeps it on the decoder path; the real fix (Parse as shorter, resumable launches) is Codecs-side work for TJ to
  schedule. For the upstream report: a synthetic repro (a long single-thread dispatch, any dispatch after it in the same
  submit, then a big kernel) is the next step.
- Window 4 (0 resets): ParseKernel AND IntraWaveKernel both replaced by synthetic one-thread spins (bindings copied, no
  memory touched at run time; scratchpad hang2/spin2.py), the real encoder kernel kept as the victim: short Parse spin ->
  alive (decode readback 5.6 ms); LONG Parse spin (readback 376.7 ms, longer than the real Parse's ~130 ms) + a spin after
  it -> ALIVE. So duration alone is not the trigger: it is the REAL ParseKernel's work on this data, followed by any
  dispatch in the same submit (window 3: real Parse + a synthetic spin after it -> lost). Report shape: "ParseKernel
  (WGSL attached) on this data, then any dispatch in the same submit, poisons the device; a submit boundary after it
  avoids it". Bisecting inside ParseKernel is left unless TJ wants a smaller report (each step can cost a reset).

## Case 2 - report text (ready to attach; TJ decides whether and where to file)

**Title:** D3D12 on NVIDIA (RTX 4070, driver 596.21): after one long single-thread compute dispatch, any further dispatch in
the same submit leaves the device to die (DXGI_ERROR_DEVICE_HUNG) on a later kernel, after the GPU went idle

**Summary.** A page runs a VP9 decode (compute only) and then an encode. The decode's submit holds, in separate compute
passes of one command encoder: a one-workgroup kernel that runs ~130 ms (`ParseKernel`, one active thread, the frame's one
tile), then a few small dispatches. The submit completes, `onSubmittedWorkDone` resolves, a readback succeeds. The encode's
next submit then loses the device with `DXGI_ERROR_DEVICE_HUNG` within ~13 ms. Every WebGPU call was recorded; the replay in
a fresh Chrome profile with no application code loses the device at the same record (3 of 3 across two days, plus every
reduction below).

**What decides it** (each line one replay; dispatch records dropped or kernels replaced, every other call kept):
| replay | result |
|---|---|
| full trace | lost at 827/828 |
| the long kernel's dispatch dropped (and everything after it in that submit) | alive |
| the long kernel kept, nothing dispatched after it in that submit | alive |
| the long kernel kept, the next dispatch replaced by a trivial kernel (one thread, a 1,000-step loop, touches no memory) | **lost** |
| that next dispatch alone on zeroed inputs (no long kernel) | alive |
| the long kernel replaced by a synthetic 377 ms one-thread loop (touches no memory), trivial kernel after it | alive |
| every storage index of the second kernel clamped to its buffer | lost |
| a submit boundary + wait right after the long kernel (in the application) | alive (5 of 5 lost before) |

So it takes the real long kernel's work (a 11.3k-line WGSL kernel with atomics, one thread active, writing its buffers)
followed by ANY dispatch in the same submit; a long dispatch that touches no memory does not do it, and nothing reads out of
range (bounds-checked on a CPU implementation of the same kernels; storage indices clamped in the replay). The device dies
later, on the next large kernel, after the GPU went idle in between.

**Attachments** (`_upstream_reports/vp9-webgpu-device-hung-case2/`): `index.html`, `replay.js`, `tracer.js`, `trace.jsonl`
(855 records, 6.7 MB), `reduced.json` (records to skip for the minimal sequence), `trace_next_dispatch_trivial.jsonl` (the
trivial-kernel variant above). Application workaround in SpawnDev.Codecs: a submit boundary and wait after the long kernel.

### Case 2 ROOT TRIGGER found (2026-11-11, Geordi live + Data replay window 5, 1 reset)
- Geordi (live, TJ OK'd the real fix): ParseKernel's 1592-int + 64-int dynamically indexed function-scope arrays moved into
  the device work buffer, NO parse wait -> own-encoder test 3 of 3 pass; same build with the original private array and no
  wait -> DEVICE_HUNG 1 of 1.
- Replay, codec-free: ParseKernel replaced by a synthetic one-thread loop that writes and reads `var big : array<i32, 1592>`
  and `var small : array<i32, 64>` at data-dependent indices every iteration (readback 277-350 ms, under the TDR limit),
  the next dispatch a trivial kernel: LOST at 828. Same loop, nothing dispatched after it in the submit: alive. A 377 ms loop
  WITHOUT private arrays + the trivial dispatch: alive (window 4).
So: **a dispatch whose kernel holds a large dynamically indexed function-scope array (an indexable temp / scratch in the
D3D12 compile), followed by any dispatch in the same submit, leaves the device to die on the next large kernel** (the
encoder's wavefront kernel, which itself has 9 private arrays of up to 256 ints). Update the report text above: the "long
kernel" is "a kernel with a large dynamically indexed private array", and attach the synthetic variant
(scratchpad wgtrace/replay2/trace_synth_parse_arrays_short.jsonl, generator hang2/spin3.py). Related: the NVIDIA
function-array-store miscompile (nvidia-d3d12-function-array-store-upstream-report.md) is the same driver area.
Library question (Data): should the WGSL backend move large dynamically indexed private arrays out of function scope?

### Standalone repro plan (TJ 2026-11-11: reports must be undeniable; own repo + GitHub Pages repro is his idea)
Page drafted (not published): `_upstream_reports/webgpu-private-array-device-hung/index.html` - plain WebGPU, three WGSL
kernels (A: one thread, a dynamically indexed `array<i32, SIZE>` loop touching no memory; B: trivial; C: an ordinary
kernel with private arrays and memory traffic), buttons / `?case=` for trigger, controls (A alone; A without the array + B)
and the workaround (submit boundary between A and B). Driver: scratchpad wgtrace/pagedrive.cs. To do before showing TJ:
several runs per case in peer-confirmed windows; a SIZE sweep for the threshold (that number also sizes any ILGPU
mitigation); a fully synthetic victim (C) must reproduce, otherwise find what the victim needs; ideally a second GPU.
- Geordi 2026-11-11 ~16:45: case 2 fix VERIFIED at scale: 281/281 VP9 WebGPU tests pass with the parse wait removed (parse
  speed unchanged on CUDA / Velocity / CPU). Case 1: the wavefront kernels' five 256-entry arrays moved to device scratch
  and the drain removed: 281/281 pass. BUT the control (old arrays, no drain) passed the two Batch tests alone 2/2: case 1
  did not reproduce in isolation today, so case 1 is NOT yet attributed to the same cause. Full-suite control run pending.
  Until it reproduces, the report states case 1 as "probably the same mechanism (the same kind of private arrays)", not as
  proven.
- Geordi ~17:10: case 1 control (original wavefront arrays, encoder drain REMOVED) passed the full VP9 WebGPU filter 281/281
  and the two Batch tests alone 2/2: case 1 no longer reproduces with today's code. It is NOT attributed to the arrays; the
  encoder drain stays, the array-move patch is saved but not committed. Case 2 is the report: parse fix committed in Codecs
  (live 3/3 vs control 1/1 hang, the codec-free replay, speed unchanged, CUDA + WebGPU EveryFrame bit-exact).

### Standalone repro attempt, window 6 (2026-11-12 ~03:30, 0 resets): a fully synthetic page does NOT reproduce yet
Page `_upstream_reports/webgpu-private-array-device-hung/index.html` (plain WebGPU; A = one thread looping over a
dynamically indexed `array<i32, 1592>` ~94 ms; B = trivial; C = victim). All ALIVE on the RTX 4070:
- controls: A alone then C; A without the array + B then C; A, submit + wait, B, then C.
- trigger A + B in one submit, then C with 2 x 256-int private arrays (wide, many workgroups); then with 9 x 256 (like the
  encoder); then with an A-like one-thread private-array victim.
- in the REAL trace: ParseKernel and IntraWaveKernel synthetic (as window 5, which hung) AND Vp9EncodeWavefrontBatchKernel
  replaced by a synthetic one-thread private-array kernel: alive.
So the trigger part is codec-free (a large dynamically indexed private array, then any dispatch in the same submit), but the
device dies only on the REAL encoder wavefront kernel (27.7k lines, 9 private arrays up to 256 ints, ~1,335 atomics). What
the victim needs is still unknown: the report is NOT yet "undeniable" by TJ's standard. Next: bisect the victim (its WGSL
in the trace) the way IntraWave was, or find what it does that the synthetic victims do not.

### Window 7 (2026-11-12 ~05:15, 1 reset): the VICTIM's sub-word atomic stores are required
In the trace with the synthetic private-array ParseKernel + trivial dispatch (window 5, lost), the real
Vp9EncodeWavefrontBatchKernel with its 448 sub-word store blocks removed (`{ let _swp = &paramN[i / 4u]; ...;
atomicOr(_swp, b << s); atomicAnd(_swp, ~(~b & 0xFF) << s); }` -> `{ }`, everything else intact; scratchpad
hang2/victimcut.py `stores`): ALIVE 2 of 2. The same trace with the victim uncut, run as a control in the same window: LOST
at 828. So the device dies when, after a large private-array kernel + a dispatch in the same submit, a later kernel does
byte / short stores as atomicOr + atomicAnd on packed atomic<u32> words (ILGPU's WGSL sub-word store lowering,
WGSLCodeGenerator.SubWordAtomicStore). Next: a fully synthetic page whose victim does many such byte stores; and which
stores matter (recon planes vs coefficient shorts, store-site count vs dynamic count). ILGPU note: if it holds, a
WebGPU-side mitigation could change the sub-word store lowering on D3D12 (the CAS form was measured to multiply D3D
compile time in 5.4.8) - only after the report is nailed down (TJ 2026-11-11).

### Window 8 (2026-11-12 ~05:45, 0 resets): byte stores alone do not make a synthetic victim die
Page victim `?victim=bytes` (64 threads, unrolled byte stores as atomicOr + atomicAnd on packed atomic<u32> words, exactly
ILGPU's lowering): control (A alone, then S) alive; trigger (A + B in one submit, then S) alive with 64 store sites x 4096
loops and with 448 sites x 512 loops. So the victim's sub-word stores are NECESSARY (window 7) but not SUFFICIENT: the real
victim also has 9 private arrays (5 x 256 + small, passed by pointer into a large NoInlining helper) and 432 atomicLoads.
Next synthetic steps: a victim combining private arrays and byte stores; then the helper-call shape. Report status for TJ:
mechanism partly isolated; NOT yet a codec-free repro.
Real-kernel cut available (Geordi): SpawnDev.Codecs\_patches\vp9-encoder-wavefront-device-scratch-2026-11-11.patch moves the
victim's 5 big private arrays to device scratch; a trace recorded with it (tracer.js via Codecs PMT_INIT_SCRIPT) tests
"real victim without its private arrays" alongside the synthetic steps.

### Window 9 (2026-11-12 ~08:30, 0 resets): the mixed synthetic victim does not reproduce either
`?victim=mixed`: 9 private arrays (5 x 256 + 2 x 16 + 2 x 8) passed by pointer into a helper that mixes them and stores bytes
as atomicOr + atomicAnd (plus atomicLoad), 64 sites a call, 64 threads; ran ~4 ms (the driver may simplify it). Control and
trigger both ALIVE. Synthetic search PAUSED here. Status for the report: the trigger half is codec-free and shown; the victim
needs the real encoder kernel (its sub-word stores necessary, window 7). Most informative next step: a real-kernel cut via a
trace recorded with Geordi's device-scratch patch (victim without its big private arrays).

### Real-victim cut (Geordi, live, 2026-11-12 ~08:50, 0 resets): the victim's private arrays are required too
Build: the ORIGINAL ParseKernel (1592-int private array, no parse wait) + the encoder wavefront kernels with their 5 big
private arrays moved to device scratch (Codecs _patches/vp9-encoder-wavefront-device-scratch-2026-11-11.patch, not committed):
`Vp9DecoderGpu_DecodesOwnKeyframeEncoder_LikeKeyframeDecoder` PASSED 1 of 1 on WebGPU; the same state with the unpatched
victim was DEVICE_HUNG 5 of 5. Trace of the passing run (3,257 calls):
`_upstream_reports/vp9-webgpu-device-hung-case2/geordi/case2_victim_devscratch_ALIVE.jsonl`.
Picture now: (1) a kernel with large dynamically indexed function-scope arrays (DXC indexable temps / scratch), then ANY
dispatch in the same submit; (2) later, a kernel that ALSO has large private arrays AND does sub-word atomic stores
(window 7) loses the device. Moving the private arrays to device memory at EITHER end removes the hang (Parse: Codecs
9292785, 281/281; victim: this cut). Synthetic victims with both features did not reproduce yet (window 9's ran ~4 ms;
possibly simplified by the compiler), so the report still lacks a codec-free victim; but the evidence for an ILGPU
mitigation (TJ 2026-11-11: "move large run-time-indexed private arrays out of function scope ... if 100% nailed down") is
now on both ends of the failure.

### ILGPU mitigation scope (Data, code reading only, 2026-11-12; awaiting TJ's go)
Moving large dynamically indexed private arrays out of function scope on WebGPU touches: the two emission sites
(WGSLCodeGenerator ~1027 local allocas, ~2703 `new T[N]` arrays); a per-dispatch device scratch buffer sized at launch
(thread count x array size; the host allocates it, like the existing scalar arena) and one extra binding (maxStorageBuffers
= 10: kernels near the limit need the coalescing path); every access rewritten to a per-invocation slice
(base = invocation index x size); helpers that take arrays by pointer (`ptr<function, array<T, N>>`) reworked, because
storage pointers as function parameters are restricted in WGSL; sub-word (byte / short) arrays as packed atomic<u32>
storage. A size threshold decides which arrays move (both failing kernels' arrays were >= 256 ints; small arrays stay
in registers). Estimate: several days with tests on all WebGPU lanes. Alternative narrower mitigation: only for kernels
that would otherwise hit the failing combination; but the combination spans two kernels, so a per-kernel rule is the
practical one.

### Retest after a reboot, then on the latest NVIDIA driver (Data, 2026-11-12, 6 resets)
TJ asked for a clean-boot retest before any ILGPU rewrite (a stale GPU state once mimicked a bug). Fresh installed Chrome
157.0.8103.52, replaydrive.cs, RTX 4070:
| replay | 596.21 after reboot (09:05) | 621.14 Studio, 32.0.16.2114 (09:13) |
|---|---|---|
| trace.jsonl (original) | LOST at 828 | LOST at 828 |
| trace_next_dispatch_trivial.jsonl | LOST at 828 | LOST at 828 |
| variants/trace_synth_parse_arrays_short.jsonl | LOST at 828 | LOST at 828 |
| variants/trace_victim_stores.jsonl (control) | ALIVE 854/855 | ALIVE 854/855 |
Identical to every earlier window. Not a stale GPU state, and not fixed in the latest driver: the report can say "reproduces
on 621.14". Logged in _TestStats/SpawnDev.ILGPU.jsonl. Per TJ, the ILGPU mitigation waits for a review by Fable of whether it
is the REQUIRED path.

### Review by Seven (Fable 5.2) + Data's variant replays (2026-11-12 11:20-11:36)
- RTX 2060 (turing, driver 610.88, Chrome 157.0.8103.78, D3D12, shader-f16 present; Seven): all 5 traces ALIVE, the original
  2 of 2 with 9/9 readbacks byte-identical to the recording. Not reproducible there; architecture vs Chrome patch vs timing
  not separated. Seven's review: `_DevComms/2026-11-12-1121-Seven-TO-Data-webgpu-hang-2060-alive-and-rewrite-review.md`.
- Seven: window 7 was confounded (the victim's 448 sub-word store blocks are its ONLY storage writes; removing them lets the
  compiler drop the private arrays too). Confirmed on the 4070: victim with atomicOr only (v1a) LOST; with atomicLoad +
  atomicStore (v1b) LOST. The sub-word store lowering is NOT a factor.
- Trigger arrays as module-scope `var<private>` (v2): LOST. Submit boundary WITHOUT a wait right after the trigger (v3):
  ALIVE 2 of 2, and ALIVE applied to the original trace (real ParseKernel).
- Trigger threshold (deterministic): total function-scope array ints per thread in the TRIGGER kernel > 1652 (6608 bytes)
  -> lost. 1588+64 alive / 1589+64 lost (2 of 2 each), 1620+32 alive / 1621+32 lost; the follow-up kernel's size does not
  matter. Generators: `_upstream_reports/vp9-webgpu-device-hung-case2/seven/gen.py`, `tools/v4n.py`, `tools/v4x.py`.
- Conclusion (Seven + Data): the device-memory rewrite is NOT required. Candidates for TJ: the upstream report with the
  threshold and v3; optionally a submit boundary (FlushPending) after any kernel above the threshold (a WORKAROUND, needs
  TJ's approval; cost not measured); a compile-time diagnostic. Logged: _TestStats config case2-seven-variants-4070.
- Unit (Seven's question; Data 11:39): the edge is identical at @workgroup_size 1, 64 and 256 (1652 alive, 1653 lost each):
  PER-THREAD function-scope array bytes, not per group. Proven for i32 arrays on one GPU / driver only; any cut below 6608 B
  is a safety margin for TJ to choose, not a finding. Real ParseKernel was 1592 + 64 = 1656 ints (4 over); the victim holds
  1,328 (under). Seven's further points for the package: tests that can fail (ILGPU: kernel over the cut + second launch ->
  two submits, red-checked with an off switch on WebGPUBackend; Codecs: original Parse arrays, no parse wait, must pass with
  the flush and hang with it off), gate (if any) on adapter vendor "nvidia" not architecture, and the report openly states
  the 2060 result and the Chrome .52 / .78 difference; module scope (v2) is not advice to give.
- Chrome version ruled out (11:41): Seven, RTX 2060 on Chrome for Testing 157.0.8103.50 (older than our .52): ALIVE. Data,
  RTX 4070 on Chrome for Testing 157.0.8103.78 (Seven's version): LOST at 828. Same Chrome, different result per GPU: the
  difference is the GPU (lovelace vs turing) or timing, not Chrome. Reproduces on the latest Chrome and the latest driver.
- AMD (Lore, 11:57): f16-free portable traces (`tools/portable.py`: `enable f16;` removed, no optional features, only
  maxStorageBuffersPerShaderStage 10; validated on the 4070: trace LOST 828, control ALIVE, same as the originals). RX 590
  (amd / gcn-4, driver 31.0.21925.1001, Chrome 157.0.8103.51): all 5 traces ALIVE on D3D12 and on Vulkan (11 runs, device
  verified created, 0 errors, original trace 9/9 readbacks identical, no TDR). Vulkan selected by flags, not read back.
  Affected so far: RTX 4070 (lovelace) only. Unaffected: RTX 2060 (turing), RX 590 (gcn-4). Details:
  `_DevComms/2026-11-12-1157-Lore-TO-Data-rx590-portable-results.md`, `case2/lore/`.
- CORRECTION (Lore, 12:31): the RX 590 "Vulkan" runs above were D3D12. Chrome on Windows has no real-GPU Vulkan WebGPU:
  `--use-webgpu-adapter` takes default / d3d11 / opengles / swiftshader, and with `--enable-webgpu-developer-features`
  adapter.info.backend reads "D3D12" with the Vulkan flags set. RX 590 = D3D12 only (11 + 3 runs, all alive). On D3D11
  (`--use-webgpu-adapter=d3d11`) even the control loses the device at record 243/244 after 89.6 s ("A valid external
  Instance reference no longer exists"): a separate D3D11-path failure, not this case, not investigated.
- Public test page: https://lostbeard.github.io/webgpu-device-hung-test/ (repo LostBeard/webgpu-device-hung-test): control
  1652 / test 1653 / fix. RTX 4070 test LOST 828; RTX 2060 (Seven) and RX 590 (Lore) all pass on the page.
- Warf's laptop (12:4x, page, Windows 10, Chrome .51, D3D12): GeForce 940MX (maxwell, 430.63) and Intel HD 620 (gen-9,
  31.0.101.2145): control, test and fix all pass, no TDR. Chrome on Windows ignores powerPreference (crbug 369219127): GPU
  chosen per pass via the Windows per-app graphics preference. Affected so far: RTX 4070 ONLY (5 GPUs, 3 vendors tested).
- SEPARATE FINDING, D3D11 (`--use-webgpu-adapter=d3d11`): compiling only Vp9EncodeWavefrontBatchKernel (27,667 lines) on the
  4070: synchronous createComputePipeline -> WebGPU instance dropped at exactly 60.1 s ("A valid external Instance reference
  no longer exists"); createComputePipelineAsync -> done after 193.0 s; D3D12 sync -> 9.8 s. Explains the D3D11 control
  loss at record 243/244 (4070 and RX 590). ILGPU's default load path is the SYNC createComputePipeline
  (WebGPUComputeShader ctor, ~line 77); the async precompile path (CreateAsync) survives. Probe: case2/d3d11/compile.html +
  probedrive.cs. Not filed; TJ to decide (Chromium report: sync compile > 60 s drops the instance; ILGPU: D3D11 compile
  time of giant kernels / prefer async pipeline creation).
- FILED 2026-11-12: Chromium https://issues.chromium.org/issues/585125915 (TJ, Blink>WebGPU; text in
  _upstream_reports/webgpu-device-hung-drafts/chromium-form/). Tracking: https://github.com/LostBeard/SpawnDev.ILGPU/issues/10.
  Browsers on the 4070: Canary + Edge affected, Firefox (wgpu) not. TJ APPROVED the workaround: submit (no wait) after a
  kernel whose local arrays exceed 6608 B per thread, NVIDIA adapters only, WebGPUBackend off switch, removed when upstream
  is fixed. Tests (Seven): ILGPU mechanics (two submits, red-checked with the switch) + Codecs end to end (original Parse
  arrays, no parse wait: passes with the flush, hangs with it off). Every `// WORKAROUND:` links issue #10.
